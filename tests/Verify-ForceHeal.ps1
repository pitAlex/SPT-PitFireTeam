param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
function Read-ProductionMethod([string]$source, [string]$signature) {
    $match = [regex]::Match($source, '(?ms)^        ' + [regex]::Escape($signature) + '\r?\n        \{.*?^        \}')
    if (!$match.Success) { throw "Missing production method: $signature" }
    return $match.Value
}
$medical = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerMedical.cs')
$plugin = Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
$methods = @(
    'public static void ForceHeal(BotOwner bot)',
    'public static void ForceHeal(BotOwner bot, bool clearNegativeEffects)',
    'private static void ResetExplicitHealTracking(BotOwner bot)',
    'private static void RemoveNegativeMedicalEffects(Player player)',
    'private static void RestoreAllBodyPartsToMaximum(Player player)',
    'private static bool IsMovementDamagedLeg(Player player, EBodyPart part)'
) | ForEach-Object { Read-ProductionMethod $medical $_ }
$hotkey = Read-ProductionMethod $plugin 'private void _BotHeal()'
$fixture = Get-Content -Raw (Join-Path $PSScriptRoot 'ForceHealFixture.cs')
$fixture = $fixture.Replace('__MEDICAL__', ($methods -join "`n")).Replace('__HOTKEY__', $hotkey)
Add-Type -TypeDefinition $fixture -WarningAction SilentlyContinue
[ForceHealChecks]::Run()
