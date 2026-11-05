param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[string]$GameRoot='E:/SPTushanka')
$ErrorActionPreference='Stop'
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$work=Join-Path $RepositoryRoot ('tests/artifacts/rogue-enemy-reports-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$exe=Join-Path $work 'Reports.exe'
$arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/define:DEBUG','/nostdlib+',"/out:$exe")
foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
foreach($file in @('going-rogue/shared/RogueAlliancePolicy.cs','going-rogue/client/RogueEnemyReports.cs','tests/RogueEnemyReportsFixture.cs')){$arguments+=Join-Path $RepositoryRoot $file}
& dotnet @arguments
if($LASTEXITCODE -ne 0){throw 'Rogue report fixture compilation failed'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Rogue report fixture failed'}
