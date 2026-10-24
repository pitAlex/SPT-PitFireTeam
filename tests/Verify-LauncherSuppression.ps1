param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$EftSource = 'F:\Projects\SPT-Tarkov\SPT-4.1.3\CLIENT-4.1.3\Assembly-CSharp'
)
$ErrorActionPreference = 'Stop'
function Read-Method([string]$Path, [string]$Name, [string]$Indent = '        ') {
    $source = Get-Content -Raw $Path
    $pattern = '(?ms)^' + $Indent + '(?:public|private|internal)[^\r\n]*\b' + [regex]::Escape($Name) + '\(.*?^' + $Indent + '\}'
    $found = [regex]::Matches($source, $pattern)
    if ($found.Count -ne 1) { throw "Expected one $Name in $Path; found $($found.Count)" }
    return $found[0].Value.Replace('public override ', 'public ')
}
function Read-Class([string]$Path, [string]$Name, [string]$Indent) {
    $source = Get-Content -Raw $Path
    $pattern = '(?ms)^' + $Indent + '(?:public|internal) sealed class ' + $Name + '\b.*?^' + $Indent + '\}'
    $found = [regex]::Matches($source, $pattern)
    if ($found.Count -ne 1) { throw "Expected one class $Name" }
    return $found[0].Value
}
$common = Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatCommon.cs'
$objective = Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatGrenadierObjective.cs'
$policy = Join-Path $RepositoryRoot 'client/Patches/FollowerWeaponSwitchPolicyPatch.cs'
$native = Join-Path $EftSource 'BotWeaponSelector.cs'
$commonMethods = @('TryPrepareGrenadeLauncherWeaponForSuppress','TryStartGrenadeLauncherFireDecision','StartLauncherSuppressFireProfile','TryGetLauncherSuppressFireEndReason','IsGrenadeLauncherSuppressCommitmentExpired') | ForEach-Object { Read-Method $common $_ }
$objectiveMethods = @('GetDecision','MarkLauncherReady','TryGetLauncherDecision','TryUseLauncherPlan','EndLauncherFire') | ForEach-Object { Read-Method $objective $_ }
$reloadHelpers = @('ExcludeLauncherFromReloadSupport','RestoreReloadSupport') | ForEach-Object { Read-Method $policy $_ }
$nativeMethods = @('ShallChangeIfNoAmmo','TrySwitchToLauncherOrChangeWeapon','TryChangeWeapon') | ForEach-Object { Read-Method $native $_ '\t' }
$patches = @('FollowerLauncherNoAmmoSwitchPatch','FollowerCombatReloadFallbackSuppressPatch') | ForEach-Object { Read-Class $policy $_ '    ' }
$harness = Get-Content -Raw (Join-Path $PSScriptRoot 'LauncherSuppressionHarness.cs')
$newline = [Environment]::NewLine
$harness = $harness.Replace('__COMMON_METHODS__', ($commonMethods -join $newline)).Replace('__OBJECTIVE_METHODS__', ($objectiveMethods -join $newline)).Replace('__RELOAD_HELPERS__', ($reloadHelpers -join $newline)).Replace('__NATIVE_RELOAD_METHODS__', ($nativeMethods -join $newline)).Replace('__RELOAD_PATCHES__', ($patches -join $newline)).Replace('__PLAN__', (Read-Class $common 'GrenadeLauncherFirePlan' '        '))
Add-Type -TypeDefinition $harness -Language CSharp
$count = [LauncherChecks]::Run()
Write-Output "Passed $count launcher plan, draw, discharge, and native reload-fallback execution checks. Unity ballistics and in-raid timing still require a raid test."
