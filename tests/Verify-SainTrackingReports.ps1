param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[string]$GameRoot='E:/SPTushanka',
    [string]$NativeSourceRoot='F:/Projects/SPT-Tarkov/SPT-4.1.3/SAIN-4.5.1/SAIN')
$ErrorActionPreference='Stop'
function Read-Method([string]$path,[string]$signature){
    $source=Get-Content -Raw -LiteralPath $path
    $start=$source.IndexOf($signature,[StringComparison]::Ordinal)
    if($start -lt 0){throw "Missing native source boundary: $signature"}
    $brace=$source.IndexOf('{',$start);$depth=0
    for($i=$brace;$i -lt $source.Length;$i++){
        if($source[$i] -eq '{'){$depth++}
        if($source[$i] -eq '}'){$depth--;if(!$depth){return $source.Substring($start,$i-$start+1)}}
    }
    throw "Unterminated native boundary: $signature"
}
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$native=[Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'BepInEx/plugins/SAIN/SAIN.dll'))
try{
    $pairs=@(
        @('SAIN.Classes.Bot.Sense.Hearing.HearingSensor','ReactToHeardSound',1),
        @('SAIN.Classes.Bot.Sense.Hearing.HearingSensor','ReactToBulletFlyBy',2),
        @('SAIN.Classes.Bot.Sense.Hearing.HearingDispersion','CalcRandomizedPosition',2),
        @('SAIN.SAINComponent.Classes.EnemyClasses.EnemyKnownPlaces','UpdateSeenPlace',2),
        @('SAIN.SAINComponent.Classes.EnemyClasses.EnemyKnownPlaces','UpdatePersonalHeardPosition',2))
    foreach($pair in $pairs){
        $methods=@($native.GetType($pair[0]).Methods | Where-Object {$_.Name -eq $pair[1] -and $_.Parameters.Count -eq $pair[2]})
        if($methods.Count -ne 1){throw "Native SAIN signature changed: $pair"}
    }
    if(!$native.GetType('SAIN.Components.PlayerComponentSpace.AISoundData').IsValueType){throw 'Native AISoundData value-type contract changed'}
    $report=$native.GetType('SAIN.Models.Structs.SAINHearingReport')
    foreach($name in @('position','soundType','placeType')){if(!($report.Fields | Where-Object Name -eq $name)){throw "Missing native report field: $name"}}
    foreach($name in @('UpdateSeenPlace','UpdatePersonalHeardPosition')){
        $method=$native.GetType('SAIN.SAINComponent.Classes.EnemyClasses.EnemyKnownPlaces').Methods | Where-Object Name -eq $name
        if(!($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SetLastKnown'})){throw "Native report publication owner changed: $name"}
    }
}finally{$native.Dispose()}
$fixture=Get-Content -Raw (Join-Path $RepositoryRoot 'tests/SainTrackingReportFixture.cs')
$places=Join-Path $NativeSourceRoot 'Classes/Bot/EnemyClasses/Position/EnemyKnownPlaces.cs'
$sensor=Join-Path $NativeSourceRoot 'Classes/Bot/Sense/Hearing/HearingSensor.cs'
foreach($pair in @(@('/*NATIVE_SEEN*/',$places,'public EnemyPlace UpdateSeenPlace('),
    @('/*NATIVE_HEARD*/',$places,'public EnemyPlace UpdatePersonalHeardPosition('),
    @('/*NATIVE_SET_KNOWN*/',$places,'private void SetLastKnown('),
    @('/*NATIVE_REACTION*/',$sensor,'public void ReactToHeardSound('),
    @('/*NATIVE_FLYBY*/',$sensor,'public void ReactToBulletFlyBy(AISoundData sound, float FlyByDistance)'))){
    $fixture=$fixture.Replace($pair[0],(Read-Method $pair[1] $pair[2]))
}
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-sain-tracking-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try{
    foreach($name in @('0Harmony.dll','MonoMod.RuntimeDetour.dll','MonoMod.Utils.dll','Mono.Cecil.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Rocks.dll')){
        Copy-Item -LiteralPath (Join-Path $GameRoot "BepInEx/core/$name") -Destination $temporary
    }
    $source=Join-Path $temporary 'Fixture.cs';[IO.File]::WriteAllText($source,$fixture)
    $exe=Join-Path $temporary 'SAIN.exe'
    $argsList=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nowarn:8632','/nostdlib+',"/out:$exe","/reference:$temporary/0Harmony.dll")
    foreach($name in @('mscorlib.dll','System.dll','System.Core.dll')){$argsList+='/reference:'+(Join-Path $framework $name)}
    $argsList+=$source
    foreach($path in @('client/Modules/FollowerEnemyTracking.cs','client/Modules/SainGoalEnemyBridge.Tracking.cs','client/Patches/FollowerSainTrackingReportPatch.cs')){$argsList+=(Join-Path $RepositoryRoot $path)}
    & dotnet @argsList
    if($LASTEXITCODE -ne 0){throw 'SAIN tracking fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'SAIN tracking fixture failed'}
    Write-Output 'PASS: installed native hearing/report targets and publication call chains verified.'
}finally{
    $resolved=[IO.Path]::GetFullPath($temporary);$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-sain-tracking-*'){throw 'Invalid temporary fixture directory'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
