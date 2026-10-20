param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot = 'E:/SPTushanka')
$ErrorActionPreference = 'Stop'
function Region([string]$source, [string]$start, [string]$end) {
    $first = $source.IndexOf($start)
    if ($first -lt 0) { throw "Missing source boundary: $start" }
    $last = $source.IndexOf($end, $first)
    if ($last -le $first) { throw "Missing source boundary: $end" }
    $source.Substring($first, $last - $first)
}
$session = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/TeammateGearSwap.cs')
$giving = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/TeammateGearSwap.GiveGear.cs')
$planner = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/GiveGearPlanner.cs')
foreach ($contract in @('message => TraceDonation("phase=plan " + message)', 'phase=committed result=ownershipTracked',
    'phase=plan result=rolledBack reason=applyDidNotCommit')) {
    if (!$giving.Contains($contract)) { throw "Missing session-scoped Give Gear decision diagnostic: $contract" }
}
$insurance = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/GiveGearInsurance.cs')
$insuranceLedger = Get-Content -Raw (Join-Path $RepositoryRoot 'server/Services/FollowerInsuranceRaidDiagnostics.cs')
$recruits = Get-Content -Raw (Join-Path $RepositoryRoot 'server/Services/FriendlyRecruitService.cs')
$teammates = Get-Content -Raw (Join-Path $RepositoryRoot 'server/Services/FriendlyTeammateService.cs')
if ($recruits.IndexOf('SanitizeRecruitCandidateEquipment(sessionId, candidate)') -gt $recruits.IndexOf('GiveGearInvitePolicy.Apply(') -or
    !$recruits.Contains('SanitizeRecruitCandidateEquipment(sessionId, request)') -or
    ([regex]::Matches($teammates, 'SanitizeRecruitCandidateEquipment\(sessionId, candidate\)')).Count -ne 2) {
    throw 'Restricted recruit filtering must cover queue/chance, pending requests, preview and acceptance'
}
foreach ($contract in @('if (Giving) PrepareDonation()', 'if (Giving) AddLiveDonationContainers(items)',
    'if (Giving) PrepareDonationDrops(items)', 'if (Giving) TrackDonation(); else TrackOwnership()',
    'Giving ? Donation : (CompoundItem)FollowerEquipment', 'giving && follower.IsSquadMate',
    '!giving && !follower.IsSpawnedSquadMate')) {
    if (!$session.Contains($contract)) { throw "Missing shared transaction boundary: $contract" }
}
foreach ($contract in @('10, 12', 'RegisterNonReturnableIds(Bot, _nativeDonationSplitIds.Concat(_borrowedDonationIds))',
    'TrackCommandedLoot(Bot, live, _scavBeforeDonation)', 'Bot.Side != EPlayerSide.Savage',
    '_applyScope.Preparation.ThrowIfCancellationRequested()', 'Snapshot(overflow) != Snapshot(_donationOverflow)',
    '_planningDonation = false', 'IsProtectedFollowerEquipment', 'PreserveBorrowedGearOwnership()')) {
    if (!$giving.Contains($contract)) { throw "Missing donation ownership/safety boundary: $contract" }
}
if ($planner -match 'pitFireTeam\.(lootPickup|pickup|gearSwap)' -or $giving.Contains('StoreGearSwapReturnItems')) {
    throw 'Explicit donations must not modify looting preferences or broaden saved-squad return admission'
}
if (!$giving.Contains('Bot.Side != EPlayerSide.Usec && Bot.Side != EPlayerSide.Bear') -or
    !$giving.Contains('_offeredDonationIds.Contains(i.Id)') -or
    !$insurance.Contains('FollowerInsuranceRaidReports.Prepare') -or
    !$insuranceLedger.Contains('!AcceptReport(raid, request.InsuranceServerId, request.InsuranceReportId)') -or
    !$insuranceLedger.Contains('gifts.Cancel(sessionId, raid.GiftedItemIds)') -or
    !$giving.Contains('GiveGearInsurance.Track(') -or
    $session.IndexOf('TrackDonationInsurance();') -lt $session.IndexOf('_applyScope.Commit();')) {
    throw 'Insurance candidates must follow committed retained PMC donations and the exact-raid report barrier'
}
$raidEnd = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/PmcKarmaRaidEndPatch.cs')
$bosses = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/BossPlayers.cs')
$lifecycle = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotsControllerPatch.cs')
if (!$raidEnd.Contains('GiveGearInsurance.EndRaid(profileId)') -or
    !$bosses.Contains('GiveGearInsurance.EndRaid(boss.realPlayer.ProfileId)') -or
    $bosses.IndexOf('GiveGearInsurance.EndRaid(boss.realPlayer.ProfileId)') -gt $bosses.IndexOf('FollowerDeathEscapeResolver.ResolveAndSend') -or
    !$lifecycle.Contains('GiveGearInsurance.Reset();') -or
    $lifecycle.IndexOf('BossPlayers.Dispose();') -gt $lifecycle.IndexOf('FollowerInsuranceRaidReports.Complete();') -or
    !$insurance.Contains('retained.ExceptWith(recovered.GetAllItems()') -or
    !$insurance.Contains('if (!player.HealthController.IsAlive) continue;')) {
    throw 'Final gift insurance requires live ownership before teardown/sealing, recovery exclusion and per-raid reset'
}
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-give-access-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$source = Join-Path $temporary 'Access.cs'
$exe = Join-Path $temporary 'Access.exe'
$access = Region $giving '        private bool CanPlaceDonation(' '        private void PrepareDonation()'
$fixture = (Get-Content -Raw (Join-Path $PSScriptRoot 'GiveGearAccessFixture.cs')).Replace('/* ACCESS */', $access)
[IO.File]::WriteAllText($source, $fixture)
$arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$exe")
foreach ($reference in @('mscorlib.dll','System.dll','System.Core.dll')) { $arguments += "/reference:$(Join-Path $framework $reference)" }
& dotnet @arguments $source
if ($LASTEXITCODE -ne 0) { throw 'Give Gear access fixture compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Give Gear access fixture failed' }
foreach ($localeName in @('en','ru','chs')) {
    $locale = Get-Content -Raw -Encoding UTF8 (Join-Path $RepositoryRoot "server/Resources/lang/$localeName.json") | ConvertFrom-Json
    if (!$locale.gestures.GiveGear) { throw "Missing Give Gear interaction in $localeName" }
    foreach ($key in @('GiveGearApply','GiveGearWorking','GiveGearUnavailable','GiveGearApplyFailed','SwapGearTransferInProgress')) {
        if (!$locale.socialUi.$key) { throw "Missing $key in $localeName" }
    }
}
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $weaponTemplate = $module.GetType('EFT.InventoryLogic.WeaponTemplate')
    foreach ($field in @(@('BoltAction','System.Boolean'), @('SingleFireRate','System.Int32'), @('bEffDist','System.Int32'),
        @('weapFireType','EFT.InventoryLogic.Weapon/EFireMode[]'))) {
        if (!($weaponTemplate.Fields | Where-Object { $_.Name -eq $field[0] -and $_.FieldType.FullName -eq $field[1] -and $_.IsPublic })) {
            throw "Installed native weapon cadence metadata changed: $($field[0])"
        }
    }
    foreach ($name in @('single','semiauto','fullauto','burst')) {
        if (!($module.GetType('EFT.InventoryLogic.Weapon/EFireMode').Fields | Where-Object { $_.Name -eq $name -and $_.HasConstant })) {
            throw "Installed native cadence fire-mode enum changed: $name"
        }
    }
    $armor = $module.GetType('EFT.InventoryLogic.ArmorComponent')
    foreach ($property in @(@('ArmorColliders', 'System.Collections.Generic.IEnumerable`1<EBodyPartColliderType>'),
        @('ArmorPlateCollidersMask', 'EArmorPlateCollider'))) {
        if (!($armor.Properties | Where-Object { $_.Name -eq $property[0] -and $_.PropertyType.FullName -eq $property[1] -and $_.GetMethod.IsPublic })) {
            throw "Installed native armor coverage API changed: $($property[0])"
        }
    }
    foreach ($boundary in @(@('EArmorPlateCollider', @('Plate_Granit_SAPI_chest','Plate_Korund_chest','Plate_Granit_SAPI_back','Plate_6B13_back')),
        @('EBodyPartColliderType', @('RibcageUp','RibcageLow','SpineTop','SpineDown')))) {
        $coverage = $module.GetType($boundary[0])
        foreach ($name in $boundary[1]) {
            if (!($coverage.Fields | Where-Object { $_.Name -eq $name -and $_.HasConstant })) {
                throw "Installed native armor coverage enum changed: $($boundary[0]).$name"
            }
        }
    }
    $move = $module.GetType('EFT.InventoryLogic.ItemManipulator').Methods | Where-Object {
        $_.Name -eq 'Move' -and $_.Parameters.Count -eq 4 -and $_.Parameters[0].Name -eq 'item' -and
        $_.Parameters[1].Name -eq 'to' -and $_.Parameters[2].Name -eq 'itemController' -and
        $_.ReturnType.FullName -eq 'Diz.LanguageExtensions.OperationResult`1<EFT.InventoryLogic.MoveResult>'
    }
    if (!$move) { throw 'Installed native donation move/preview boundary changed' }
    $panel = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/GiveGearPanelPatch.cs')
    foreach ($contract in @('ignoreLayout = true', 'Canvas.willRenderCanvases += positionButton',
        'Canvas.willRenderCanvases -= positionButton', 'var row = (RectTransform)header.parent',
        'rect.position = new Vector3(center.x, nameCenter.y, nameCenter.z)')) {
        if (!$panel.Contains($contract)) { throw "Missing Give Items header layout boundary: $contract" }
    }
    if ($panel.Contains('__instance._header.transform')) { throw 'Give Items must not anchor to the left LOOT label' }
    if (!$panel.Contains('GearSwapPanelPatch.CreateTransferFeedback(__instance, rect, button._headerLabel, out var progress, out var notice)')) {
        throw 'Give Items must reuse Apply Changes transfer feedback and its localized notice'
    }
    $click = $panel.Substring($panel.IndexOf('button.OnClick.AddListener(async () =>'))
    foreach ($feedback in @('button.gameObject.SetActive(false)', 'progress.SetActive(true)', 'notice.SetActive(true)')) {
        if ($click.IndexOf($feedback) -lt 0 -or $click.IndexOf($feedback) -gt $click.IndexOf('await session.Apply()')) {
            throw "Give Items transfer feedback must be shown before awaiting Apply: $feedback"
        }
    }
    foreach ($restore in @('progress.SetActive(false)', 'notice.SetActive(false)', 'button.gameObject.SetActive(true)', 'button.Interactable = true')) {
        if ($click.IndexOf($restore) -lt $click.IndexOf('await session.Apply()')) {
            throw "A retained Give Gear draft must restore transfer feedback/button after Apply: $restore"
        }
    }
    if ($giving.Contains('ProtectedDonationException') -or $session.Contains('ProtectedDonationException') -or
        (Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')).Contains('new GiveGearDonationMovePatch().Enable()')) {
        throw 'Restricted loans must remain transferable; filtering belongs to permanent gifts/returns/invitations'
    }
    foreach ($target in @(@('EFT.UI.SimpleStashPanel','Show',8), @('EFT.UI.SimpleStashPanel','Close',0),
        @('EFT.GameWorld','CreateLootWithRigidbody',9), @('EFT.GameWorld','DestroyLoot',1))) {
        $type = $module.Types | Where-Object FullName -eq $target[0]
        if (!($type.Methods | Where-Object { $_.Name -eq $target[1] -and $_.Parameters.Count -eq $target[2] })) {
            throw "Installed native donation boundary changed: $($target[0]).$($target[1])"
        }
    }
    $company = $module.GetType('EFT.UI.Insurance.InsuranceCompany')
    $magazine = $module.GetType('EFT.InventoryLogic.Magazine')
    $applyItem = $magazine.Methods | Where-Object { $_.Name -eq 'ApplyItem' -and $_.Parameters.Count -eq 4 }
    if (!$applyItem -or !($applyItem.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and
        $_.Operand.DeclaringType.Name -eq 'LoadInsertedMagazineError' })) {
        throw 'Installed detachable-magazine loading restriction changed; recheck staged detach/refill/reinsert'
    }
    if (!($company.Fields | Where-Object { $_.Name -eq '_missingInsuredItems' -and $_.IsPublic }) -or
        !($company.Methods | Where-Object { $_.Name -eq 'get_InsuredItems' })) {
        throw 'Installed native exact insurance cancellation API changed'
    }
} finally { $module.Dispose() }
Write-Output 'Give Gear shared-scope/ownership guards, EN/RU/CHS locales and installed native boundaries verified; not a raid/UI test.'
