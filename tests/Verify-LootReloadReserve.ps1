param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-reload-reserve-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$exe = Join-Path $temporary 'LootReloadReserve.exe'
$arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$exe")
foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
    $arguments += "/reference:$(Join-Path $framework $reference)"
}
foreach ($source in @('client/BigBrain/Actions/GestureCommandAction.ReloadReserve.cs', 'tests/LootReloadReserveFixture.cs')) {
    $arguments += Join-Path $RepositoryRoot $source
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Reload reserve fixture compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Reload reserve fixture failed' }

# Source-boundary checks complement the linked production guard, not live EFT qualification.
$actions = Join-Path $RepositoryRoot 'client/BigBrain/Actions'
foreach ($file in @('BodyLoot', 'LootPickup', 'AmmoSalvage.Execution')) {
    $source = Get-Content -Raw (Join-Path $actions "GestureCommandAction.$file.cs")
    if ($source -notmatch 'PreservesEquippedMagazineReloadSpace\(') { throw "Missing guard in $file" }
}
$planner = Get-Content -Raw (Join-Path $actions 'GestureCommandAction.LootMagazines.cs')
if ($planner -notmatch '\.Concat\(GetAlternateReloadReservesForSupportMagazinePlan\(inventory, followerEquipment, weapon\)\)') {
    throw 'New-weapon planner does not include equipped reserves'
}
$tactical = Get-Content -Raw (Join-Path $actions 'GestureCommandAction.TacticalAmmo.cs')
$reserveMethod = ($tactical -split 'private EFT.InventoryLogic.Magazine\? GetReloadReserveForEquippedWeapon', 2)[1]
$reserveMethod = ($reserveMethod -split 'private string DescribeReloadReserves', 2)[0]
if ($reserveMethod -match 'InsertedContribution|EvaluateActual') { throw 'Readiness must not exempt an equipped magazine from reload protection' }
Write-Output 'Loot reload reserve integration checks passed.'
