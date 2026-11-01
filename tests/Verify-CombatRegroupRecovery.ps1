param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[switch]$BeforeFix)
$ErrorActionPreference='Stop'
function Read-Method([string]$path,[string]$method) {
    $source=Get-Content -Raw (Join-Path $RepositoryRoot $path)
    if($BeforeFix -and $path -eq 'client/BigBrain/FollowerCombatLogicBase.cs') {
        $source=git -C $RepositoryRoot show "HEAD:$path"
        if($LASTEXITCODE -ne 0){throw 'Unable to read the previous Core router'}
        $source=$source -join "`n"
    }
    $matches=[regex]::Matches($source,'(?ms)^        (?:public|private|internal|protected)[^\r\n]*\b'+$method+'\(.*?^        \}')
    if($matches.Count -ne 1){throw "Expected one production method: ${path}::$method"}
    return $matches[0].Value
}
$logic='client/BigBrain/FollowerCombatLogicBase.cs'
$fixture=Get-Content -Raw (Join-Path $PSScriptRoot 'CombatRegroupRecoveryFixture.cs')
$fixture=$fixture.Replace('/* REGROUP ADMISSION */',(Read-Method $logic 'ShouldConsumeRegroupCommand'))
$fixture=$fixture.Replace('/* REGROUP INTERRUPTION */',(Read-Method $logic 'CanInterruptForRegroupOrder'))
$fixture=$fixture.Replace('/* PRODUCTION END ROUTER */',(Read-Method $logic 'ShallEndCurrentDecision'))
$fixture=$fixture.Replace('/* RECOVERY REASON */',(Read-Method 'client/BigBrain/FollowerCombatCommon.cs' 'IsNoEnemyThreatCoverReason'))
Add-Type -TypeDefinition $fixture -Language CSharp -CompilerOptions '/nullable:annotations'
$count=[CombatRegroupRecoveryChecks]::Run()
Write-Output "PASS: $count production Core recovery/Regroup command routing checks. Controlled EFT inputs; Unity movement remains a raid check."
