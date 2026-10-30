param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatCommon.cs')
$method=[regex]::Match($source,'(?ms)^        internal bool TryPlanCombatFollowDecision\(.*?^        \}').Value
if(!$method){throw 'Missing Core group-follow planner'}
$advance=[regex]::Match($source,'(?ms)^        internal bool TryPlanContactAdvanceDecision\(.*?^        \}').Value
if(!$advance){throw 'Missing Core contact-advance planner'}
$ground=[regex]::Match($source,'(?ms)^        internal static bool HasSupportedCombatGround\(.*?^        \}').Value
if(!$ground){throw 'Missing Core physical-ground admission'}
$directory=Join-Path $RepositoryRoot 'tests/artifacts/group-follow'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$production=@"
using UnityEngine; using UnityEngine.AI;
partial class Planner {
$method
$advance
$ground
}
"@
[IO.File]::WriteAllText((Join-Path $directory 'Production.cs'),$production)
dotnet run --project (Join-Path $RepositoryRoot 'tests/CombatGroupFollow') -c Debug -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'Core group-follow planner fixture failed'}
