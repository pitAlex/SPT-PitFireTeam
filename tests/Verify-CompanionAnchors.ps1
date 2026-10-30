param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$follow=Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/Actions/FollowAction.cs')
$method=[regex]::Match($follow,'(?ms)^        private bool TryGetBossAndPlayer\(\).*?^        \}').Value
if(!$method){throw 'Missing native FollowAction leader binding'}
$sniper=Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatSniper.cs')
$regroup=[regex]::Match($sniper,'(?ms)^        private bool ShouldRegroupForBossDistance\(\).*?^        \}').Value
if(!$regroup){throw 'Missing Marksman automatic regroup selector'}
$directory=Join-Path $RepositoryRoot 'tests/artifacts/companion-anchors'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$source=@"
using EFT; using pitTeam.Components; using pitTeam.Modules; using UnityEngine; using pitTeam.BigBrain;
partial class FollowHarness {
$method
}
partial class MarksmanRegroupHarness {
$regroup
}
"@
[IO.File]::WriteAllText((Join-Path $directory 'FollowBinding.cs'),$source)
$regroupSource=Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatRegroupObjective.cs')
$release=[regex]::Match($regroupSource,'(?ms)^        private bool ShouldReleaseAutomaticRegroup\(.*?^        \}').Value
$endPrefix=[regex]::Match($regroupSource,'(?ms)^        public override AICoreActionEnd ShallEndCurrentDecision\(.*?(?=^            if \(!CombatCommon.HasActiveCombatEnemy\(goalEnemy\)\))').Value
if(!$release -or !$endPrefix -or !$regroupSource.Contains('automaticRegroup = automatic;')){throw 'Missing automatic regroup lifecycle boundary'}
$endPrefix=$endPrefix.Replace('public override','public')+"            return default;`n        }"
$automatic=@"
using EFT; using pitTeam.Modules; using pitTeam.BigBrain;
partial class AutomaticRegroupHarness {
$release
$endPrefix
}
"@
[IO.File]::WriteAllText((Join-Path $directory 'AutomaticRegroup.cs'),$automatic)
dotnet run --project (Join-Path $RepositoryRoot 'tests/CompanionAnchors') -c Debug -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){throw 'Companion leader anchor fixture failed'}
