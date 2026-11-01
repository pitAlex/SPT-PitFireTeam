param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$layer = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatLayer.cs')
$methods = @('IsActive', 'IsCurrentActionEnding', 'HasPendingMedicalWork',
    'ShouldKeepCombatLayerForMedicalWork', 'IsCombatTreatmentRunning', 'IsHealingDecision',
    'ClearMedicalKeepActive', 'ClearLinger', 'ArmLingerIfNeeded', 'IsLingerExpired') | ForEach-Object {
    $found = [regex]::Matches($layer, '(?ms)^        (?:public|private|internal)[^\r\n]*\b' + $_ + '\(.*?^        \}')
    if ($found.Count -ne 1) { throw "Expected one production method: $_" }
    $found[0].Value
}
$medical = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerMedical.cs')
$using = [regex]::Match($medical, '(?ms)^        public static bool IsUsingMedical\(.*?^        \}').Value
if (!$using) { throw 'Missing production medical-use predicate' }
$fixture = (Get-Content -Raw (Join-Path $PSScriptRoot 'CombatMedicalOwnershipFixture.cs')).Replace(
    '/* PRODUCTION LAYER METHODS */', ($methods -join "`n")).Replace('/* PRODUCTION MEDICAL USE */', $using)
Add-Type -TypeDefinition $fixture -Language CSharp -CompilerOptions '/nowarn:0649'
[CombatMedicalOwnershipFixture]::Run()
