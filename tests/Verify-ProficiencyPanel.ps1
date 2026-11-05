param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/OtherPlayerProfileScreenPatch.ProficiencyUi.cs')
$methods=foreach($name in @('ShowProficiencyOverlay','CreateProficiencySliderRow','ResetProficiencyValues')){
    $method=[regex]::Match($source,'(?ms)^        private static (?:void|RectTransform) '+$name+'\(.*?^        \}').Value
    if(!$method){throw ('Production proficiency UI method missing: '+$name)}
    $method
}
$constants=[regex]::Matches($source,'(?m)^        private const float ProficiencyRow(?:Height|Spacing) = .*?;') | ForEach-Object Value
$gate=[regex]::Match($source,'(?m)^            ActiveProfileCombatSettingsLocked = .*?;').Value
if(!$gate -or $constants.Count -ne 2){throw 'Proficiency panel admission/geometry boundary changed'}
$work=Join-Path $RepositoryRoot 'tests/artifacts/proficiency-panel'
New-Item -ItemType Directory -Force -Path $work | Out-Null
[IO.File]::WriteAllText((Join-Path $work 'Production.cs'),"using System; using UnityEngine; using UnityEngine.UI; using ResultProfile=Profile;`npartial class PanelHarness {`n"+($constants -join "`n")+"`n"+($methods -join "`n")+"`nstatic void Configure(Options options){`n$gate`n}`n}")
dotnet run --project (Join-Path $RepositoryRoot 'tests/ProficiencyPanel') -c Debug
if($LASTEXITCODE -ne 0){throw 'Production proficiency overlay layout/control fixture failed'}
