param([Parameter(Mandatory)][string]$RepositoryRoot,[Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $group=$assembly.MainModule.GetType('BotsGroup')
    $info=$assembly.MainModule.GetType('BotGroupEnemyInfo')
    foreach($pair in @(@($group,'EnemyLastSeenTimeReal'),@($info,'EnemyLastVisiblePosition'))){
        if(!($pair[0].Properties | Where-Object {$_.Name -eq $pair[1] -and $_.SetMethod})){throw "Missing setter: $($pair[1])"}
    }
    $report=@($group.Methods | Where-Object {$_.Name -eq 'ReportAboutEnemy' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EFT.IPlayer|EEnemyPartVisibleType|EFT.BotOwner'})
    if($report.Count -ne 1 -or ($report[0].Parameters.Name -join '|') -ne 'enemy|isVisibleOnlyBySence|reporter'){throw 'Report hook signature changed'}
    foreach($name in @('_botGroup','Player')){if(!($info.Fields | Where-Object {$_.Name -eq $name -and $_.IsPublic})){throw "Missing field: $name"}}
    'Installed group sight hooks and public identity fields verified.'
} finally {$assembly.Dispose()}
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-diagnostics-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $exe=Join-Path $temporary 'Diagnostics.exe'
    $harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object Name -ne '0Harmony.dll' | Copy-Item -Destination $temporary
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/define:DEBUG','/nostdlib+',"/out:$exe","/reference:$harmony")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    $production=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/BattleRecorder.PatrolWait.cs')
    $begin=$production.IndexOf('        private static void RecordPatrolWait(')
    $finish=$production.IndexOf('        private static object PatrolGroupEvidence(')
    if($begin -lt 0 -or $finish -le $begin){throw 'Wait method extraction boundary changed'}
    $fixture=(Get-Content -Raw (Join-Path $RepositoryRoot 'tests/GroupSightRecorderFixture.cs')).Replace('__WAIT_METHOD__',$production.Substring($begin,$finish-$begin))
    $fixture="using pitTeam.Utils;`r`n"+$fixture
    $fixturePath=Join-Path $temporary 'GroupSightFixture.cs'
    [IO.File]::WriteAllText($fixturePath,$fixture)
    $arguments+=(Join-Path $RepositoryRoot 'client/Patches/GroupSightRecorderPatch.cs')
    $arguments+=$fixturePath
    & dotnet @arguments
    if($LASTEXITCODE -ne 0){throw 'Diagnostic fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Diagnostic behavior fixture failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-diagnostics-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
