param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[string]$NativeRoot='F:/Projects/SPT-Tarkov/SPT-4.1.3/CLIENT-4.1.3/Assembly-CSharp')
$ErrorActionPreference='Stop'
$medical=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerMedical.cs')
$snapshot=[regex]::Match($medical,'(?ms)^        private readonly struct MedicalProgressSnapshot\s*\{.*?^        \}').Value
$progress=[regex]::Match($medical,'(?ms)^        private static bool HasMedicalProgressed\(.*?^        \}').Value
$native=Get-Content -Raw (Join-Path $NativeRoot 'EFT/Player.cs')
$finish=[regex]::Match($native,'(?ms)^\t{4}public override void OnDropFinishedAction\(\)\r?\n\t{4}\{.*?^\t{4}\}').Value
if(!$snapshot -or !$progress -or !$finish -or !$finish.Contains('_onUsedCallback')){throw 'Production/native recovery boundary changed'}
$fixture=(Get-Content -Raw (Join-Path $PSScriptRoot 'FollowerMedicalRecoveryFixture.cs')).Replace('/* PROGRESS SNAPSHOT */',$snapshot).Replace('/* PROGRESS METHOD */',$progress).Replace('/* NATIVE FINISH */',$finish)
$retry=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerFirstAidRetry.cs')
$grenade=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FollowerBlockedGrenadeRecovery.cs')
foreach($source in @($retry,$grenade)) { $fixture += "`n" + [regex]::Replace($source,'(?m)^using [^\r\n]+\r?\n','') }
Add-Type -TypeDefinition $fixture -Language CSharp
[MedicalRecoveryChecks]::Main()
