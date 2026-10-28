param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot)
$ErrorActionPreference = 'Stop'
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-loot-request-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$exe = Join-Path $temporary 'LootRequests.exe'
$arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$exe")
foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
    $arguments += "/reference:$(Join-Path $framework $reference)"
}
foreach ($source in @('client/Modules/FollowerLootRequest.cs', 'client/Modules/FollowerLootCategoryService.cs', 'tests/LootRequestFixture.cs')) {
    $arguments += Join-Path $RepositoryRoot $source
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Loot fixture compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Loot request fixture failed' }

# Run the real body finalizer with command-owned success surviving an action restart.
$bodySource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/Actions/GestureCommandAction.BodyLoot.cs')
$finishStart = $bodySource.IndexOf('        private void FinishBodyLootNoMoreMoves()')
$finishEnd = $bodySource.IndexOf('        private void CleanupBodyLootInteraction(', $finishStart)
if ($finishStart -lt 0 -or $finishEnd -le $finishStart) { throw 'Missing body completion boundary' }
$finishFixture = Get-Content -Raw (Join-Path $PSScriptRoot 'RecruitLootCompletionFixture.cs')
$finishCode = $finishFixture.Replace('/* FINISH BODY LOOT */', $bodySource.Substring($finishStart, $finishEnd - $finishStart))
$finishSource = Join-Path $temporary 'RecruitLootCompletion.cs'
[IO.File]::WriteAllText($finishSource, $finishCode)
$finishExe = Join-Path $temporary 'RecruitLootCompletion.exe'
$finishArgs = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$finishExe")
foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
    $finishArgs += "/reference:$(Join-Path $framework $reference)"
}
& dotnet @finishArgs $finishSource (Join-Path $RepositoryRoot 'client/Modules/FollowerLootRequest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Recruit loot completion fixture compilation failed' }
& $finishExe
if ($LASTEXITCODE -ne 0) { throw 'Recruit loot completion fixture failed' }

# Exercise the production eligibility getters across recruit-only, mixed, death and transit cases.
$followerSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/BotFollowerPlayer.cs')
$eligibilityStart = $followerSource.IndexOf('        public bool CanHandleBodyContainerLootCommands')
$eligibilityEnd = $followerSource.IndexOf('        protected WildSpawnType _botRole;', $eligibilityStart)
if ($eligibilityStart -lt 0 -or $eligibilityEnd -le $eligibilityStart) { throw 'Missing loot eligibility boundary' }
$eligibilityFixture = Get-Content -Raw (Join-Path $PSScriptRoot 'LootFollowerEligibilityFixture.cs')
$eligibilityCode = $eligibilityFixture.Replace('/* LOOT_ELIGIBILITY_MEMBERS */',
    $followerSource.Substring($eligibilityStart, $eligibilityEnd - $eligibilityStart))
$lootInteraction = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/FollowerLootInteractionPatch.cs')
$visibility = [regex]::Match($lootInteraction, '(?ms)^        internal static bool CanShow\(.*?^        \}').Value
$quickPanel = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/QuickPanelPatch.cs')
$quickGate = [regex]::Match($quickPanel, '(?ms)^                    bool canCommandBodyContainer =.*?;').Value
$quickFlags = [regex]::Matches($quickPanel, '(?m)^                    __instance.SetPhraseActive\(EPhraseTrigger\.(?:LootKey|LootMoney|LootWeapon|LootGeneric|LootBody|CheckHim|LootContainer),[^\r\n]+').Value
if (!$visibility -or !$quickGate -or $quickFlags.Count -ne 7) { throw 'Missing production loot prompt boundaries' }
$eligibilityCode = $eligibilityCode.Replace('/* CMD_VISIBILITY */', $visibility).Replace('/* QUICK_VISIBILITY */', $quickGate + "`n" + ($quickFlags -join "`n"))
$bossCommands = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/AIBossPlayer.cs')
foreach ($commandMethod in @('ApplyTakeBodyGearCommand', 'ApplyTakeContainerLootCommand')) {
    $dispatch = [regex]::Match($bossCommands, '(?ms)^        internal void ' + $commandMethod + '\(.*?^        \}').Value
    if (!$dispatch.Contains('FollowerLootCommandPolicy.TryRefuseBodyContainer(requester, realPlayer, Followers)')) { throw "Missing native Negative refusal before $commandMethod assignment" }
}
$eligibilitySource = Join-Path $temporary 'LootFollowerEligibility.cs'
$eligibilityExe = Join-Path $temporary 'LootFollowerEligibility.exe'
[IO.File]::WriteAllText($eligibilitySource, $eligibilityCode)
$eligibilityArguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$eligibilityExe")
foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
    $eligibilityArguments += "/reference:$(Join-Path $framework $reference)"
}
& dotnet @eligibilityArguments $eligibilitySource (Join-Path $RepositoryRoot 'client/Modules/CompanionGearPolicy.cs') (Join-Path $RepositoryRoot 'going-rogue/client/GoonGearPolicy.cs') (Join-Path $RepositoryRoot 'client/Modules/FollowerLootCommandPolicy.cs')
if ($LASTEXITCODE -ne 0) { throw 'Loot eligibility fixture compilation failed' }
& $eligibilityExe
if ($LASTEXITCODE -ne 0) { throw 'Loot eligibility fixture failed' }

# These are source-boundary checks, not a substitute for inventory/Unity raid qualification.
$plugin = Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
if ($plugin -match 'lootAllowGearSwapping|IsLootGearSwappingEnabled') { throw 'Obsolete equipment toggle remains active' }
$requestPolicy = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/Actions/GestureCommandAction.LootRequest.cs')
if ($requestPolicy -notmatch 'ActiveLootRequest\?\.EnablesWeaponPickup' -or
    $requestPolicy -notmatch 'ActiveLootRequest\?\.EnablesGearPickup') { throw 'Acquisition must use category/request policy' }
$gearPlanner = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/Actions/GestureCommandAction.GearSwapping.cs')
if ($gearPlanner -notmatch '(?s)private bool TryBuildEasyWeaponEquipMove\(.*?if \(!IsRequestedWeaponPickupEnabled\(\)' -or
    $gearPlanner -notmatch '(?s)private bool TryBuildTacticalVestEquipMove\(.*?if \(!IsRequestedGearPickupEnabled\(\)') {
    throw 'Missing category acquisition gate'
}
if ($gearPlanner -match 'CanReplaceOccupiedGearSlot|TacticalVest.PreserveOld') { throw 'Occupied vest replacement must remain disabled' }
$follower = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/BotFollowerPlayer.cs')
if ($follower -notmatch '(?s)public void ClearCommand\(.*?LootRequest = new FollowerLootRequest\(\)') { throw 'Missing command cleanup' }
foreach ($completion in @('CompleteTakeBodyGear', 'CompleteTakeContainerLoot')) {
    if ($follower -notmatch "(?s)public void $completion\(\).*?LootRequest = new FollowerLootRequest\(\);\s*if \(_resumeHoldAfterTakeLoot\)") {
        throw "Missing hold-resume cleanup in $completion"
    }
}
foreach ($kind in @('Body', 'Container')) {
    $source = Get-Content -Raw (Join-Path $RepositoryRoot "client/BigBrain/Actions/GestureCommandAction.${kind}Loot.cs")
    if ($source -notmatch 'ActiveLootRequest\?\.CompletePriority\(\)') { throw "Missing $kind priority transition" }
    if ($source -match 'if \(TryStart(?:Preferred.*PrimaryWeapon|.*PrimaryTactical|.*SupportTactical|.*SupportLooseFeed|.*SecondaryWeaponPromotion|.*BackpackCargoWeaponPromotion)') {
        throw "Unguarded $kind weapon maintenance"
    }
}
$body = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/Actions/GestureCommandAction.BodyLoot.cs')
if ($body -match '(?s)if \(distance > 1\.9f\)\s*\{\s*bodyLootReadyAt = 0f') { throw 'Leaving corpse range erases search deadline' }
if ($body -notmatch 'InitializeBodyWeaponSelection\(corpseEquipment, followerEquipment\)') { throw 'Body search does not initialize request selection' }
if ($body -notmatch '"selectiveWeaponCargo"') { throw 'Missing selected long-gun cargo-only path' }
$candidates = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/Actions/GestureCommandAction.LootCandidates.cs')
if ($candidates -notmatch 'ActiveLootRequest\?\.AllowsSelectedWeapon') { throw 'Missing centralized weapon selection gate' }
foreach ($localeFile in Get-ChildItem (Join-Path $RepositoryRoot 'server/Resources/lang') -Filter '*.json') {
    $locale = Get-Content -Raw -Encoding UTF8 $localeFile.FullName | ConvertFrom-Json
    foreach ($key in @('LootActionCheckHim','LootActionAndWeapon','LootActionAndGear','LootActionWeapon','LootActionGear')) {
        if ($locale.socialUi.$key -notmatch '^CMD ?: \S') { throw "Missing CMD label $key in $($localeFile.Name)" }
    }
}
$interaction = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/FollowerLootInteractionPatch.cs')
if ($interaction -notmatch 'interactive is not Corpse corpse' -or
    $interaction -notmatch '(?s)internal static bool CanShow\(.*?if \(target is not Corpse \|\|.*?return false;' -or
    $interaction -notmatch 'Add\(FollowerLootMode.Normal, "LootActionCheckHim"\)' -or
    $interaction -match 'LootActionThis|LootActionTakeThis|LootableContainer' -or
    $interaction -notmatch 'if \(!CanShow\(owner, target\)') {
    throw 'CMD display and execution must remain corpse-only; normal container/item commands are separate'
}
if ($GameRoot) {
    Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
    try {
        $helper = $module.Types | Where-Object FullName -eq 'EFT.InteractionContextHelper'
        $method = @($helper.Methods | Where-Object {
            $_.Name -eq 'GetAvailableActions' -and $_.IsStatic -and $_.Parameters.Count -eq 2 -and
            $_.Parameters[0].ParameterType.FullName -eq 'EFT.GamePlayerOwner' -and
            $_.Parameters[1].ParameterType.Name -eq 'IInteractive'
        })
        if ($method.Count -ne 1) { throw 'Native interaction builder signature changed' }
        $panel = $module.Types | Where-Object FullName -eq 'EFT.UI.ActionPanel'
        if (!($panel.Fields | Where-Object Name -eq '_owner')) { throw 'Native action panel owner field changed' }
        if (!($panel.Methods | Where-Object { $_.Name -eq 'Update' -and $_.Parameters.Count -eq 0 })) { throw 'Native action panel update changed' }
        Write-Output 'Installed interaction builder and action panel metadata verified.'
    } finally { $module.Dispose() }
}
Write-Output 'Loot request integration source and localization checks passed.'
