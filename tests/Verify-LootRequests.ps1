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

# These are source-boundary checks, not a substitute for inventory/Unity raid qualification.
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
    foreach ($key in @('LootActionThis','LootActionAndWeapon','LootActionAndGear','LootActionWeapon','LootActionGear')) {
        if ($locale.socialUi.$key -notmatch '^CMD: \S') { throw "Missing CMD label $key in $($localeFile.Name)" }
    }
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
