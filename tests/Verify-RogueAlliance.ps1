param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[string]$GameRoot='E:/SPTushanka')
$ErrorActionPreference='Stop'
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$work=Join-Path $RepositoryRoot ('tests/artifacts/rogue-alliance-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
foreach($ref in @('Newtonsoft.Json.dll','0Harmony.dll')){Copy-Item -LiteralPath (Join-Path $RepositoryRoot ('client/libs/'+$ref)) -Destination $work}
Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
    Where-Object {$_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll'} | Copy-Item -Destination $work
$exe=Join-Path $work 'Alliance.exe'
$argsList=@($compiler,'/nologo','/target:exe','/langversion:latest','/nostdlib+',"/out:$exe")
foreach($ref in @('mscorlib.dll','System.dll','System.Core.dll')){$argsList+='/reference:'+(Join-Path $framework $ref)}
$argsList+='/reference:'+(Join-Path $framework 'netstandard.dll')
foreach($ref in @('Newtonsoft.Json.dll','0Harmony.dll')){$argsList+='/reference:'+(Join-Path $work $ref)}
foreach($source in @('shared/FriendlyEncounterPenaltyPolicy.cs','going-rogue/shared/RogueAlliancePolicy.cs','going-rogue/shared/KnightQuestPolicy.cs','going-rogue/client/RogueAlliance.cs','going-rogue/client/RogueWarnings.cs','tests/RogueAllianceClientFixture.cs')){$argsList+=Join-Path $RepositoryRoot $source}
& dotnet @argsList
if($LASTEXITCODE -ne 0){throw 'Rogue alliance client fixture compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Rogue alliance client fixture failed'}
