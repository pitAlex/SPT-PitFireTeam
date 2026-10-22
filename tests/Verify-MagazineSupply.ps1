param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-magazine-supply-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$exe = Join-Path $temporary 'SupplyScope.exe'
$arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$exe")
foreach ($reference in @('mscorlib.dll','System.dll','System.Core.dll')) { $arguments += "/reference:$(Join-Path $framework $reference)" }
& dotnet @arguments (Join-Path $PSScriptRoot 'MagazineSupplyScopeFixture.cs') (Join-Path $RepositoryRoot 'client/Patches/FollowerMagazineSupplyPatch.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native refill-scope compilation failed' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Native refill-scope checks failed' }
$patrol = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerPatrolLayer.cs')
$recorder = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/BattleRecorder.AmmoRefill.cs')
foreach ($retired in @('peacefulObservation', 'NextObservation', 'BuildPeacefulMedicalObservation', 'RecordAmmoRefillGate')) {
    if ($recorder.Contains($retired) -or $patrol.Contains($retired)) { throw "Retired periodic refill diagnostic remains: $retired" }
}
if (!$recorder.Contains('WriteEventInternal("ammoRefill"')) { throw 'Event-based refill recorder must remain available' }
foreach ($contract in @('budget.Fill(magazine, bestAmmo)', 'if (!budget.Owns(weapon, magazine)) return false',
    'TeammateGearSwap.Current?.Bot == botOwner', 'TrackMagazineRefill(botOwner, magazine, beforeRefill, givenSource)',
    'FindBetterMagazine(BotOwner, currentWeapon)', 'NotifySupplyChanged', 'HasRefillWork(BotOwner)')) {
    if (!$patrol.Contains($contract)) { throw "Missing live supply boundary: $contract" }
}
