param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot = 'E:/SPTushanka')
$ErrorActionPreference = 'Stop'
function Get-Region([string]$source, [string]$start, [string]$end) {
    $first = $source.IndexOf($start)
    $last = $source.IndexOf($end, $first)
    if ($first -lt 0 -or $last -le $first) { throw "Missing source region $start" }
    $source.Substring($first, $last - $first)
}
$session = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/TeammateGearSwap.cs')
$panel = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/TeammateGearSwapPatch.cs')
if (!$session.Contains('GearSwapSnapshot.Normalize(JsonConvert.SerializeObject(')) {
    throw 'All opening, stale-state and final replay snapshots must use grid-order normalization'
}
foreach ($backpackContract in @('AllowBackpackSwap = follower.IsSpawnedSquadMate',
    'new HashSet<string>(_initialOpaqueBackpackIds)',
    'RememberBackpack(bot.Inventory.Equipment, replayOpaqueBackpacks)',
    'RememberBackpack(FollowerEquipment, _opaqueBackpackIds)')) {
    if (!$session.Contains($backpackContract)) { throw "Missing backpack policy: $backpackContract" }
}
foreach ($patchType in @('GearSwapBackpackSlotContentsPatch', 'GearSwapBackpackContentsPatch', 'GearSwapBackpackOpenPatch',
    'GearSwapArmorSlotPatch', 'GearSwapArmorSlotUiPatch')) {
    if (!$panel.Contains("class $patchType") -or
        !(Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')).Contains("new $patchType().Enable()")) {
        throw "Missing opaque backpack boundary: $patchType"
    }
}
$plateAcceptance = Get-Region $panel '    internal sealed class GearSwapArmorSlotPatch' '    internal sealed class GearSwapArmorSlotUiPatch'
$plateUi = Get-Region $panel '    internal sealed class GearSwapArmorSlotUiPatch' '    internal sealed class GearSwapContextPatch'
if (!$plateAcceptance.Contains('CanEditPlateSlot(__instance, allowCommit: true)') -or
    !$plateAcceptance.Contains('error = null;') -or !$plateAcceptance.Contains('__result = true;') -or
    !$plateUi.Contains('__result.Key != EModLockedState.RaidLock') -or
    !$plateUi.Contains('slot.ContainedItem is ArmorPlate') -or
    !$plateUi.Contains('CanEditPlateSlot(slot)')) {
    throw 'Plate exception must stay scoped to editable armor slots; live replay cannot unlock inspection UI'
}
foreach ($contract in @(
    'buttonObject.transform.SetParent(header.parent, false)',
    'GetSlotView(EquipmentSlot.Headwear)',
    'rect.position = new Vector3(center.x, nameCenter.y, nameCenter.z)',
    'button.SetRawText(pitFireTeam.GetSocialUiText("SwapGearApply"), Mathf.RoundToInt(panel._containerName.fontSize))',
    'label.fontStyle |= FontStyles.Bold',
    'Canvas.willRenderCanvases -= positionButton',
    'DefaultUIButton template = CommonUI.Instance?.InventoryScreen?._backButton',
    'UnityEngine.Object.Instantiate(template, rect, false)',
    'button.OnClick.RemoveAllListeners()',
    'button._onPointerEnterSound = true',
    'button._onPointerClickSound = true',
    'UnityEngine.Object.Instantiate(loaderTemplate, rect, false)',
    'pitFireTeam.GetSocialUiText("SwapGearTransferInProgress")'
)) {
    if (!$panel.Contains($contract)) { throw "Missing Swap Gear header source contract: $contract" }
}
$click = Get-Region $panel '            button.OnClick.AddListener(async () =>' '    internal sealed class GearSwapPanelClosePatch'
if ($click.IndexOf('progress.SetActive(true)') -lt 0 -or
    $click.IndexOf('notice.SetActive(true)') -lt 0 -or
    $click.IndexOf('button.gameObject.SetActive(false)') -lt 0 -or
    $click.IndexOf('button.gameObject.SetActive(false)') -gt $click.IndexOf('progress.SetActive(true)') -or
    $click.IndexOf('progress.SetActive(true)') -gt $click.IndexOf('await session.Apply()') -or
    $click.IndexOf('notice.SetActive(true)') -gt $click.IndexOf('await session.Apply()')) {
    throw 'Transfer feedback must be visible before awaiting Apply'
}
$apply = Get-Region $session '        internal async Task Apply()' '        private void ValidateFinalEquipment()'
if (!$apply.Contains('keepDraft = !handsStarted && ex is GearSwapValidationException draftError') -or
    !$apply.Contains('draftError.Key == "SwapGearMagazineSpace"') -or
    !$apply.Contains('draftError.Key == "SwapGearNeedsWeapon"') -or
    !$apply.Contains('if (!keepDraft) Close();') -or
    !$click.Contains('ReferenceEquals(TeammateGearSwap.Current, session)') -or
    !$click.Contains('progress.SetActive(false)') -or !$click.Contains('notice.SetActive(false)') -or
    !$click.Contains('button.gameObject.SetActive(true)') -or !$click.Contains('button.Interactable = true')) {
    throw 'Correctable pre-transfer validation must preserve the draft and restore the Apply action'
}
if ($apply.LastIndexOf('Close();') -lt $apply.LastIndexOf('await RestoreHands(Owner.Player, playerHands)')) {
    throw 'Swap Gear must close automatically after player hands restoration'
}
$open = Get-Region $session '        internal static void Open(GamePlayerOwner owner)' '        internal static bool Holds('
if ([regex]::Matches($open, 'UnsafeReason\(').Count -ne 1) {
    throw 'Opening must evaluate safety once before displaying its generic warning'
}
foreach ($temporaryTrace in @('_diagnostics', 'GearSwapDiagnostics', '[SwapGear][Trace]', '[SwapGear][BackpackUI]')) {
    if ($session.Contains($temporaryTrace) -or $panel.Contains($temporaryTrace)) {
        throw "Temporary Swap Gear diagnostics must remain removed: $temporaryTrace"
    }
}
$restore = Get-Region $session '        private Task RestoreHands(Player player, Item preferred)' '        private Task RestoreHandsCore(Player player, Item preferred)'
foreach ($boundary in @('GearSwapBodyRefresh.Run(', 'player.PlayerBody.SlotViews.Where(view => view.LoadingJob != null).Select(view => view.LoadingJob)',
    '() => RestoreHandsCore(player, preferred)', '() => VerifyHeldWeaponBody(player)',
    'view._item != null || view.Model != null')) {
    if (!$restore.Contains($boundary)) { throw "Missing synchronized body/hands boundary: $boundary" }
}
Write-Output 'Swap Gear header, safety and diagnostic-cleanup source guards verified (not a Unity visual test).'
$fixture = Get-Content -Raw (Join-Path $PSScriptRoot 'GearSwapFixture.cs')
$fixture = $fixture.Replace('/* SLOTS */', (Get-Region $session '        internal static readonly HashSet<EquipmentSlot> VisibleSlots' '        internal static readonly EquipmentSlot[] FirearmSlots'))
$fixture = $fixture.Replace('/* ACCESS */', (Get-Region $session '        internal static bool CanEdit(' '        internal bool IsDraft('))
$fixture = $fixture.Replace('/* COMMIT KNOWN */', (Get-Region $session '        internal static bool TreatCommitItemKnown(' '        internal static void Update('))
$edit = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/GearSwapEdit.cs')
$fixture = $fixture.Replace('/* PROVENANCE */', (Get-Region $edit '        internal void ValidateProvenance(' '        internal static GearSwapEdit Capture('))
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-gear-swap-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$source = Join-Path $temporary 'GearSwap.cs'
$exe = Join-Path $temporary 'GearSwap.exe'
[IO.File]::WriteAllText($source, $fixture)
$arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$exe")
foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
    $arguments += "/reference:$(Join-Path $framework $reference)"
}
& dotnet @arguments $source (Join-Path $RepositoryRoot 'client/Modules/GearSwapTransaction.cs')
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap fixture compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap fixture failed' }

$controllerExe = Join-Path $temporary 'GearSwapController.exe'
$controllerArgs = @($arguments | Where-Object { $_ -notlike '/out:*' }) + "/out:$controllerExe"
& dotnet @controllerArgs (Join-Path $PSScriptRoot 'GearSwapControllerFixture.cs') (Join-Path $RepositoryRoot 'client/Modules/GearSwapInventoryController.cs')
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap controller fixture compilation failed' }
& $controllerExe
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap controller fixture failed' }

$presentation = Get-Content -Raw (Join-Path $PSScriptRoot 'GearSwapBackpackPresentationFixture.cs')
$presentation = $presentation.Replace('/* PRESENTATION */', (Get-Region $panel '    internal static class GearSwapBackpackPresentation' '    internal sealed class GearSwapBackpackContentsPatch'))
$presentationSource = Join-Path $temporary 'GearSwapBackpackPresentation.cs'
[IO.File]::WriteAllText($presentationSource, $presentation)
$presentationExe = Join-Path $temporary 'GearSwapBackpackPresentation.exe'
$presentationArgs = @($arguments | Where-Object { $_ -notlike '/out:*' }) + "/out:$presentationExe"
& dotnet @presentationArgs $presentationSource
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap backpack presentation fixture compilation failed' }
& $presentationExe
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap backpack presentation fixture failed' }

$plate = Get-Content -Raw (Join-Path $PSScriptRoot 'GearSwapPlateFixture.cs')
$plate = $plate.Replace('/* PLATE POLICY */', (Get-Region $session '        internal bool CanEditPlateSlot(' '        private static void RememberBackpack('))
$plateSource = Join-Path $temporary 'GearSwapPlate.cs'
[IO.File]::WriteAllText($plateSource, $plate)
$plateExe = Join-Path $temporary 'GearSwapPlate.exe'
$plateArgs = @($arguments | Where-Object { $_ -notlike '/out:*' }) + "/out:$plateExe"
& dotnet @plateArgs $plateSource
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap plate fixture compilation failed' }
& $plateExe
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap plate scope fixture failed' }

$handsExe = Join-Path $temporary 'GearSwapHands.exe'
$handsArgs = @($arguments | Where-Object { $_ -notlike '/out:*' }) + "/out:$handsExe"
& dotnet @handsArgs (Join-Path $PSScriptRoot 'GearSwapHandsFixture.cs') (Join-Path $RepositoryRoot 'client/Modules/GearSwapHandsTransition.cs')
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap hands fixture compilation failed' }
& $handsExe
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap hands transition fixture failed' }

$bodyExe = Join-Path $temporary 'GearSwapBodyRefresh.exe'
$bodyArgs = @($arguments | Where-Object { $_ -notlike '/out:*' }) + "/out:$bodyExe"
& dotnet @bodyArgs (Join-Path $PSScriptRoot 'GearSwapBodyRefreshFixture.cs') (Join-Path $RepositoryRoot 'client/Modules/GearSwapBodyRefresh.cs')
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap body refresh fixture compilation failed' }
& $bodyExe
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap body refresh fixture failed' }

$newtonsoft = Join-Path $RepositoryRoot 'client/libs4.1/Newtonsoft.Json.dll'
Copy-Item -LiteralPath $newtonsoft -Destination $temporary
$snapshotExe = Join-Path $temporary 'GearSwapSnapshot.exe'
$snapshotArgs = @($arguments | Where-Object { $_ -notlike '/out:*' }) + "/out:$snapshotExe" +
    "/reference:$newtonsoft" + "/reference:$(Join-Path $framework 'netstandard.dll')"
& dotnet @snapshotArgs (Join-Path $PSScriptRoot 'GearSwapSnapshotFixture.cs') (Join-Path $RepositoryRoot 'client/Modules/GearSwapSnapshot.cs')
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap snapshot normalization fixture compilation failed' }
& $snapshotExe
if ($LASTEXITCODE -ne 0) { throw 'Gear Swap snapshot normalization fixture failed' }

# Reproduce the original constructor-side event leak with the same fixture and old conversion call.
$oldWrapperSource = Join-Path $temporary 'OldGearSwapInventoryController.cs'
$controllerSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/GearSwapInventoryController.cs')
$oldWrapper = $controllerSource.Replace('new DraftOperation(GetAndIncrementNextOperationId(), this)', 'base.ConvertOperationResultToOperation(result)')
if ($oldWrapper -eq $controllerSource) { throw 'Missing local operation conversion regression boundary' }
[IO.File]::WriteAllText($oldWrapperSource, $oldWrapper)
$oldWrapperExe = Join-Path $temporary 'OldGearSwapController.exe'
$oldWrapperArgs = @($arguments | Where-Object { $_ -notlike '/out:*' }) + "/out:$oldWrapperExe"
& dotnet @oldWrapperArgs (Join-Path $PSScriptRoot 'GearSwapControllerFixture.cs') $oldWrapperSource
if ($LASTEXITCODE -ne 0) { throw 'Old wrapper reproduction failed to compile' }
& $oldWrapperExe --expect-native-regression
if ($LASTEXITCODE -ne 0) { throw 'Old wrapper busy-event reproduction failed' }

# Check patch signatures on the installed runtime as well as compiling against the supported baseline.
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    foreach ($fieldContract in @(
        @('EFT.PlayerBody/SlotView', '_item'),
        @('EFT.PlayerBody/SlotView', 'LoadingJob'),
        @('EFT.UI.InventoryScreen', '_backButton'),
        @('EFT.UI.DefaultUIButton', '_headerLabel'),
        @('EFT.UI.ButtonFeedback', '_onPointerEnterSound'),
        @('EFT.UI.ButtonFeedback', '_onPointerClickSound'),
        @('EFT.UI.DragAndDrop.ItemView', '_iconLoader'),
        @('EFT.UI.ItemIconView', '_iconLoader'),
        @('EFT.UI.DragAndDrop.SearchableSlotView', '_searchableItemView'),
        @('EFT.UI.DragAndDrop.SearchableSlotView', '_specSlotsPanel')
    )) {
        $nativeType = $module.GetType($fieldContract[0])
        if (!$nativeType -or !($nativeType.Fields | Where-Object Name -eq $fieldContract[1])) {
            throw "Installed native field missing: $($fieldContract -join ' / ')"
        }
    }
    foreach ($contract in @(
        @('EFT.InventoryLogic.ItemManipulator', 'Discard', 4),
        @('EFT.InventoryLogic.ItemManipulator', 'CanModifyItem', 4),
        @('EFT.InventoryLogic.ItemManipulator', 'CanTransferTo', 3),
        @('EFT.UI.DragAndDrop.ComplexStashPanel', 'Show', 6),
        @('EFT.UI.DragAndDrop.ComplexStashPanel', 'Close', 0),
        @('EFT.UI.DragAndDrop.SearchableSlotView', 'ShowContent', 1),
        @('EFT.UI.DragAndDrop.SearchableItemView', 'Show', 5),
        @('EFT.UI.ItemUiContext', 'OpenItem', 2),
        @('ArmorSlot', 'CanAcceptRaid', 1),
        @('EFT.UI.ItemSpecificationPanel', 'GetModLockedState', 1),
        @('EFT.UI.EquipmentTab', 'GetSlotView', 1),
        @('EFT.UI.BaseItemContextInteractions', 'IsActive', 1),
        @('EFT.UI.BaseItemContextInteractions', 'ExecuteInteractionInternal', 1),
        @('EFT.HealthSystem.PlayerHealthController', 'ApplyItem', 3),
        @('EFT.UI.TabGroup', 'SelectTab', 2),
        @('BotWeaponManager', 'ManualUpdate', 0)
    )) {
        $type = $module.Types | Where-Object FullName -eq $contract[0]
        if (!($type.Methods | Where-Object { $_.Name -eq $contract[1] -and $_.Parameters.Count -eq $contract[2] })) {
            throw "Installed signature missing: $($contract -join ' / ')"
        }
    }
} finally { $module.Dispose() }
$sainPath = Join-Path $GameRoot 'BepInEx/plugins/SAIN/SAIN.dll'
if (Test-Path -LiteralPath $sainPath) {
    $sainModule = [Mono.Cecil.ModuleDefinition]::ReadModule($sainPath)
    try {
        $component = $sainModule.Types | Where-Object FullName -eq 'SAIN.Components.PlayerComponentSpace.PlayerComponent'
        $equipment = $sainModule.Types | Where-Object FullName -eq 'SAIN.Components.PlayerComponentSpace.Classes.Equipment.SAINEquipmentClass'
        if (!($component.Properties | Where-Object Name -eq 'Equipment') -or
            !($equipment.Properties | Where-Object Name -eq 'WeaponInfos') -or
            !($equipment.Methods | Where-Object { $_.Name -eq 'getAllWeapons' -and $_.Parameters.Count -eq 0 }) -or
            !($equipment.Methods | Where-Object { $_.Name -eq 'OnWeaponEquiped' -and $_.Parameters.Count -eq 2 })) {
            throw 'Installed optional SAIN equipment refresh boundary does not match'
        }
    } finally { $sainModule.Dispose() }
}
foreach ($language in @('en', 'ru', 'chs')) {
    $locale = Get-Content -Raw -Encoding UTF8 (Join-Path $RepositoryRoot "server/Resources/lang/$language.json") | ConvertFrom-Json
    foreach ($key in @('SwapGearApply', 'SwapGearTransferInProgress', 'SwapGearStale', 'SwapGearNeedsWeapon', 'SwapGearMagazineSpace', 'SwapGearRecoveryFailed', 'SwapGearUnavailable', 'SwapGearActionBlocked', 'SwapGearApplyFailed', 'SwapGearRefreshFailed')) {
        if (!$locale.socialUi.$key) { throw "Missing $language localization $key" }
    }
}
Write-Output 'Gear Swap native patch boundaries and EN/RU/CHS text verified. Unity/raid behavior still requires runtime testing.'
