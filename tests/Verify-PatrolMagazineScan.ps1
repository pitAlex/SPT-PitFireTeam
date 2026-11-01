param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerPatrolLayer.cs')
$first = $source.IndexOf('        private static bool TryFillReachableSpareMagazines(')
$last = $source.IndexOf('        private static FollowerWeaponSupplyBudget BuildRefillBudget(', $first)
if ($first -lt 0 -or $last -le $first) { throw 'Missing production spare-magazine refill method' }
$method = $source.Substring($first, $last - $first)
$carried = [regex]::Match($source, '(?ms)^        public static bool TryFillCarriedWeaponMagazines\(.*?^        \}').Value
$refill = [regex]::Match($source, '(?ms)^        internal static bool TryFillMagazineWithLooseAmmo\(.*?^        \}').Value
$topOff = [regex]::Match($source, '(?ms)^        public static bool CanTopOffWeapon\(.*?^        \}').Value
$installed = [regex]::Match($source, '(?ms)^        private static bool IsInstalledInWeaponTree\(.*?^        \}').Value
if (!$carried -or !$refill -or !$topOff -or !$installed) { throw 'Missing production refill boundaries' }
$completion = [regex]::Match($source, '(?s)            if \(reloadingInProgress\)\r?\n.*?(?=            if \(!BotOwner\.WeaponManager\.IsWeaponReady)')
if (!$completion.Success) { throw 'Missing production reload completion branch' }
$fixture = (Get-Content -Raw (Join-Path $PSScriptRoot 'PatrolMagazineScanFixture.cs')).Replace('/* SPARE_FILL */', $method).
    Replace('/* RELOAD_COMPLETION */', $completion.Value).Replace('/* CARRIED_FILL */', $carried).
    Replace('/* REFILL */', $refill).Replace('/* CAN_TOP_OFF */', $topOff).Replace('/* INSTALLED */', $installed)
Add-Type -TypeDefinition $fixture -Language CSharp
$count = [PatrolMagazineScanChecks]::Run()
Write-Output "Passed $count production patrol magazine scan/completion checks, including simulated inventory mutation, rollback and removed-magazine refills. Live refill still requires raid validation."
