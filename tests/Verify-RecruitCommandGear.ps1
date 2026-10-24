param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot = 'E:/SPTushanka')
$ErrorActionPreference = 'Stop'
$core = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/TeammateGearSwap.cs')
$bridge = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/TeammateGearSwap.CommandLoot.cs')
$giving = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/TeammateGearSwap.GiveGear.cs')
$action = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/Actions/GestureCommandAction.RecruitGear.cs')
$patches = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/TeammateGearSwapPatch.cs')
$body = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/Actions/GestureCommandAction.BodyLoot.cs')
if (!$action.Contains('ActiveLootRequest?.CompleteGearExchange()') -or
    !$action.Contains('ActiveLootRequest?.GearExchangeCommitted == true') -or
    !$body.Contains('ActiveLootRequest?.HasSuccessfulLoot(bodyLootReportedMovesSucceeded)') -or
    !$body.Contains('BotOwner.BotTalk.TrySay(EPhraseTrigger.Ready, false);')) {
    throw 'Committed recruit exchanges must survive action restarts and use shared successful body completion/Ready.'
}
foreach ($contract in @('_commandSourceDraft = Clone(source)', 'ReferenceEquals(pair.Value, originals[pair.Key])',
    '(body && !follower.CanHandleBodyContainerLootCommands)', 'follower.IsSquadMate',
    'Snapshot(_commandSource) != _commandSourceBefore', 'Snapshot(_commandSource) != Snapshot(_commandSourceDraft)',
    'ReferenceEquals(_commandSource.Owner, _commandSourceOwner)', 'ReferenceEquals(_commandSourceOwner?.RootItem, _commandSource)',
    'Snapshot(_commandSourceDraft) != _commandSourceBefore',
    '_planningDonation = true;', 'finally { _planningDonation = false; }',
    'await session.Apply()', 'committed = session._committed')) {
    if (!$bridge.Contains($contract)) { throw "Missing recruit command transaction boundary: $contract" }
}
if (!$patches.Contains('TeammateGearSwap.Current == null || TeammateGearSwap.Current.CommandLoot')) {
    throw 'Headless commands must not restrict the player inventory tabs.'
}
foreach ($contract in @('if (CommandLoot) return;', 'if (!CommandLoot) await EmptyHands(Owner.Player);',
    'private static Item Clone(Item source) => ItemBinarySerializer.SerializeItem(source,',
    'FullySearchedSearchController.Instance).Deserialize(new Dictionary<MongoID, Item>())',
    'if (!CommandLoot && Applying && CanContinue(Owner.Player))', 'AddCommandSource(items);', 'ValidateCommandReplay();',
    'if (_screen != null)', 'if (!CommandLoot && Owner.Player != null', 'var actor = CommandLoot ? bot : player;',
    '(!CommandLoot && _playerBefore != Snapshot(', '(!CommandLoot && playerActual != playerExpected)')) {
    if (!$core.Contains($contract)) { throw "Headless commands must preserve editor/player ownership: $contract" }
}
if (!$giving.Contains('new GiveGearPlanner(BotDraft') -or !$giving.Contains('TrackDonationInsurance()') -or
    !$giving.Contains('GiveGearContainer.Create(Donation.Id, CommandLoot ? 64 : 10, CommandLoot ? 64 : 12,') -or
    !$action.Contains('generation != recruitGearGeneration') -or !$action.Contains('ActiveLootRequest?.CompletePriority()') -or
    !$action.Contains('ReferenceEquals(ActiveLootRequest, request)') -or
    !$action.Contains('GiveGearPlanner.EquipmentSlots') -or !$action.Contains('if (TeammateGearSwap.Current != null) return true;')) {
    throw 'Recruit commands must reuse donation planning/ownership, defer busy exchanges and reject stale completion.'
}
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $deserialize = $module.GetType('EFT.ItemDescriptor').Methods | Where-Object { $_.Name -eq 'Deserialize' -and !$_.HasGenericParameters -and $_.IsPublic -and $_.Parameters.Count -eq 1 }
    if (!$deserialize) { throw 'Installed native descriptor clone API changed.' }
    foreach ($methodName in @('SerializeItem', 'DeserializeItemInternal')) {
        $method = $module.GetType('EFT.ItemBinarySerializer').Methods | Where-Object Name -eq $methodName
        foreach ($field in @('Malfunction', 'ActiveCamora', 'IsUnderBarrelDeviceActive', 'ShellsInWeapon', 'Components', 'SpawnedInSession')) {
            if (!$method.Body.Instructions.Where({$_.Operand -and $_.Operand.ToString().Contains("EFT.ItemDescriptor::$field")})) {
                throw "Native $methodName no longer carries full snapshot field: $field"
            }
        }
    }
    $parent = $module.GetType('EFT.InventoryLogic.ItemController').Methods | Where-Object Name -eq 'get_ParentItem'
    if (!$parent.Body.Instructions.Where({$_.Operand -and $_.Operand.ToString().Contains('get_RootItem()')})) {
        throw 'World root address ownership no longer resolves through ItemController.RootItem.'
    }
} finally { $module.Dispose() }
& dotnet run --project (Join-Path $RepositoryRoot 'tests/RecruitCommandGear/RecruitCommandGear.csproj') -c Debug --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Recruit command production routing fixture failed.' }
Write-Output 'Recruit command source/recipient replay guards and installed clone/world-owner boundaries verified; not a live world-transfer test.'
