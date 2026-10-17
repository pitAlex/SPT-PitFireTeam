param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatCommon.cs')
$methods = @('TryReleaseHealRetreatWithoutThreat', 'EndCommittedHealMovement', 'EndRunToCover',
    'EndAttackMoving', 'EndAttackRetreat', 'IsStableNoCoverHoldReason', 'EndBaseHoldPosition', 'HasRecoveryPressure') | ForEach-Object {
    $found = [regex]::Matches($source, '(?ms)^        (?:public|private|internal)[^\r\n]*\b' + $_ + '\(.*?^        \}')
    if ($found.Count -ne 1) { throw "Expected one production method: $_" }
    $found[0].Value
}
$fixture = (Get-Content -Raw (Join-Path $PSScriptRoot 'CombatHealHandoffFixture.cs')).Replace('/* PRODUCTION METHODS */', ($methods -join "`n"))
Add-Type -TypeDefinition $fixture -Language CSharp
$count = [CombatHealHandoffChecks]::Run()
Write-Output "Passed $count production heal-retreat and missing-enemy hold checks. Native treatment/navigation and linger handoff require a raid test."
