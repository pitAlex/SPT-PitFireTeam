param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$GameRoot,
    [Parameter(Mandatory)][string]$ClientSourceRoot
)
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$taskAssembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $taskType=$taskAssembly.MainModule.GetType('EFT.UI.NumberSlider')
    foreach($taskName in @('Show','FormattedValue','SetCurrentValue','UpdateValue','SetStringValue')) {
        if(!($taskType.Methods | Where-Object {$_.Name -eq $taskName -and $_.IsPublic -and $_.HasBody})) {throw "Missing native NumberSlider method: $taskName"}
    }
    $taskSetter=$taskType.Methods | Where-Object Name -eq 'SetCurrentValue'
    if(!($taskSetter.Body.Instructions | Where-Object {$_.Operand -and $_.Operand.ToString() -eq 'System.Void UnityEngine.UI.Slider::set_value(System.Single)'})) {throw 'Native slider feedback boundary changed'}
} finally {$taskAssembly.Dispose()}
$taskNative=[IO.File]::ReadAllText((Join-Path $ClientSourceRoot 'EFT/UI/NumberSlider.cs'))
$taskMethods=@()
foreach($taskName in @('Show','FormattedValue','SetCurrentValue','UpdateValue','SetStringValue')) {
    $taskMatch=[regex]::Match($taskNative,'public (?:void|string) '+$taskName+'\([^\r\n]+\)')
    if(!$taskMatch.Success){throw "Native source method missing: $taskName"}
    $taskOpen=$taskNative.IndexOf('{',$taskMatch.Index); $taskDepth=1; $taskEnd=$taskOpen+1
    while($taskDepth -gt 0 -and $taskEnd -lt $taskNative.Length) {
        if($taskNative[$taskEnd] -eq '{'){$taskDepth++}elseif($taskNative[$taskEnd] -eq '}'){$taskDepth--};$taskEnd++
    }
    $taskMethods+=$taskNative.Substring($taskMatch.Index,$taskEnd-$taskMatch.Index)
}
$taskSettings=[IO.File]::ReadAllText((Join-Path $RepositoryRoot 'client/Components/SquadControlMenuUi.Settings.cs'))
$taskFormat=[regex]::Match($taskSettings,'slider.Show\(acceptableRange.MinValue, acceptableRange.MaxValue, "([^"]+)"\)')
if(!$taskFormat.Success){throw 'Cannot find native setting slider numeric format'}
if(!$taskSettings.Contains('CreateText("PercentSuffix", "%"')){throw 'Percent suffix must remain outside the editable numeric value'}
$taskSdk=dotnet --list-sdks | Select-Object -Last 1
if($taskSdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$taskCompiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$taskFramework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$taskTemp=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-number-slider-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskTemp | Out-Null
try {
    $taskFixture=[IO.File]::ReadAllText((Join-Path $RepositoryRoot 'tests/NumberSliderFixture.cs'))
    $taskFixture+="`nnamespace EFT.UI { public partial class NumberSlider {"+($taskMethods -join "`n")+'} }'
    [IO.File]::WriteAllText((Join-Path $taskTemp 'Fixture.cs'),$taskFixture)
    $taskExe=Join-Path $taskTemp 'Fixture.exe'
    & dotnet $taskCompiler /nologo /langversion:latest /target:exe /out:$taskExe /reference:"$taskFramework/mscorlib.dll" /reference:"$taskFramework/System.dll" /reference:"$taskFramework/System.Core.dll" (Join-Path $taskTemp 'Fixture.cs')
    if($LASTEXITCODE -ne 0){throw 'Number slider fixture compile failed'}
    & $taskExe $taskFormat.Groups[1].Value
    if($LASTEXITCODE -ne 0){throw 'Native number slider regression failed'}
} finally {
    $taskResolved=(Resolve-Path -LiteralPath $taskTemp).Path
    if(!$taskResolved.StartsWith([IO.Path]::GetTempPath(),[StringComparison]::OrdinalIgnoreCase)){throw 'Refusing cleanup outside temporary directory'}
    Remove-Item -LiteralPath $taskResolved -Recurse -Force
}
