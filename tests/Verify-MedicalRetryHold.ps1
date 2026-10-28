param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
function Method([string]$path, [string]$name) {
    $source = Get-Content -Raw (Join-Path $RepositoryRoot $path)
    $found = [regex]::Matches($source, '(?ms)^        (?:public|private|internal)[^\r\n]*\b' + $name + '\(.*?^        \}')
    if ($found.Count -ne 1) { throw "Expected one production method: $path::$name" }
    return $found[0].Value
}
$fixture = Get-Content -Raw (Join-Path $PSScriptRoot 'MedicalRetryHoldFixture.cs')
$fixture = $fixture.Replace('/* COMMON RECOVERY */', (Method 'client/BigBrain/FollowerCombatCommon.cs' 'TryGetMedicalRetryRecoveryDecision'))
$fixture = $fixture.Replace('/* RIFLEMAN END */', (Method 'client/BigBrain/FollowerCombatDefault.cs' 'EndMedicalRetryHold'))
$fixture = $fixture.Replace('/* MARKSMAN END */', (Method 'client/BigBrain/FollowerCombatSniper.cs' 'EndMedicalRetryHold'))
$fixture = $fixture.Replace('/* KNIGHT END */', (Method 'going-rogue/client/Combat/GoonCombatObjective.cs' 'EndMedicalRetryHold'))
Add-Type -TypeDefinition $fixture -Language CSharp -CompilerOptions '/nullable:annotations'
$count = [MedicalRetryHoldChecks]::Run()
Write-Output "PASS: $count production Rifleman/Marksman/Knight medical retry and shared recovery checks. Stubbed treatment/navigation inputs; no Unity raid simulation."
