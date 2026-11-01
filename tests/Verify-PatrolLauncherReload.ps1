param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerPatrolLayer.cs')
$names = @('TryReturnSelectedLauncherToPrimaryAfterCombat','TryHandleOutOfCombatReloadInternal',
    'ShouldReloadCurrentWeaponOutOfCombat','TryForceReloadCurrentWeaponOutOfCombat','TrySelectNextWeaponToTopOff',
    'TrySelectWeaponToTopOff','ShouldReloadWeaponInSlot','IsSafeForPatrolSupportLauncher',
    'TryCompleteReturnAfterTopOffSwitch','AreAllReloadWeaponsProcessed','IsReloadSlotProcessed',
    'IsReloadWeaponProcessed','MarkReloadWeaponProcessed')
$methods = foreach ($name in $names) {
    $matches = @([regex]::Matches($source, '(?ms)^        private [^\r\n]*\b'+$name+'\(.*?^        \}'))
    if ($name -eq 'ShouldReloadWeaponInSlot') { $matches = @($matches | Where-Object Value -match ', Weapon weapon\)') }
    if ($name -eq 'MarkReloadWeaponProcessed') { $matches = @($matches | Where-Object Value -match '\(string\? weaponId\)') }
    if ($matches.Count -ne 1) { throw "Expected one production patrol method: $name" }
    $matches[0].Value
}
foreach ($name in @('IsPatrolSupportLauncher','CanMaintainPatrolSupportLauncher')) {
    $match = [regex]::Match($source, '(?ms)^        private bool '+$name+'\([^\r\n]+=>.*?;')
    if (!$match.Success) { throw "Missing production expression: $name" }
    $methods += $match.Value
}
$fixture = (Get-Content -Raw (Join-Path $PSScriptRoot 'PatrolLauncherReloadFixture.cs')).Replace('/* PRODUCTION METHODS */', ($methods -join "`n"))
Add-Type -TypeDefinition $fixture -Language CSharp
[PatrolLauncherReloadChecks]::Run()
