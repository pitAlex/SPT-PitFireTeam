param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
function Read-Method([string]$Path, [string]$Name) {
    $source = Get-Content -Raw $Path
    $pattern = '(?ms)^        (?:public|private|internal)[^\r\n]*\b' + [regex]::Escape($Name) + '\(.*?^        \}'
    $found = [regex]::Matches($source, $pattern)
    if ($found.Count -ne 1) { throw "Expected one $Name in $Path; found $($found.Count)" }
    return $found[0].Value.Replace('public override ', 'public ')
}
$common = Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatCommon.cs'
$action = Join-Path $RepositoryRoot 'client/BigBrain/Actions/CombatSuppressFireAction.cs'
$objective = Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatOrderedPushObjective.cs'
$commonMethods = @('TryGetSuppressTarget', 'TryCreateSuppressDecision', 'TryCreateOrderedSuppressWeaponFallbackDecision', 'IsFollowerSuppressReason', 'IsRecoverySuppressReason', 'IsAutonomousSuppressReason', 'IsAutoSuppressReason', 'IsBossProtectionSuppressReason', 'IsOrderedSuppressReason', 'IsGrenadeLauncherSuppressReason') | ForEach-Object { Read-Method $common $_ }
$actionMethods = @('Update', 'IsFollowerSuppressActive', 'TryGetSuppressTargetForAction') | ForEach-Object { Read-Method $action $_ }
$objectiveMethods = @('EndPressureRecovery', 'TryCreatePressureRecoveryFallback', 'IsOrderedPushSuppressReason') | ForEach-Object { Read-Method $objective $_ }
$policy = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerSuppressTargetPolicy.cs')
$harness = Get-Content -Raw (Join-Path $PSScriptRoot 'RifleSuppressionHarness.cs')
$newline = [Environment]::NewLine
$harness = $harness.Replace('__POLICY__', $policy.Replace('using EFT;', '').Replace('using UnityEngine;', '')).Replace('__COMMON__', ($commonMethods -join $newline)).Replace('__ACTION__', ($actionMethods -join $newline)).Replace('__OBJECTIVE__', ($objectiveMethods -join $newline))
Add-Type -TypeDefinition $harness -Language CSharp
$count = [RifleSuppressionChecks]::Run()
Write-Output "Passed $count rifle suppression target, expiry, action dispatch, fallback, and ordered recovery checks. Geometry and movement integration still require an in-raid test."
