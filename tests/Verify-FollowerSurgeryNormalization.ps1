param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerMedical.cs')
$method=[regex]::Match($source,'(?ms)^        private static void NormalizeSurgeryRecoveredPartsForFirstAid\(.*?^        \}').Value
if (!$method) { throw 'Production normalization missing' }
$fixture=(Get-Content -Raw (Join-Path $PSScriptRoot 'FollowerSurgeryNormalizationFixture.cs')).Replace('/* PRODUCTION NORMALIZATION */',$method)
Add-Type -TypeDefinition $fixture -Language CSharp
$count=[SurgeryNormalizationChecks]::Run()
Write-Output "Passed $count production emergency-recovery/bleeding-priority checks. Native bleed cure and Unity treatment still require a raid test."
