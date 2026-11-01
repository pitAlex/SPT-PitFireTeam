param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/SquadControlMenuUi.Roster.cs')
$start=$source.IndexOf('        private void UpdateRosterHealth()')
$end=$source.IndexOf('        private static void ResolveRosterTileColors', $start)
if($start -lt 0 -or $end -lt 0){throw 'Production polling boundary changed'}
$code='using System;using System.Collections.Generic;using System.Linq;using System.Threading.Tasks;using Newtonsoft.Json;internal partial class RosterFixture { private const string TeammatesRoute="/teammates";'+$source.Substring($start,$end-$start)+'}'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-roster-membership-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary|Out-Null
try{
    $sdk=dotnet --list-sdks|Select-Object -Last 1
    if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
    $compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
    $framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
    $production=Join-Path $temporary 'Production.cs';$code|Set-Content -LiteralPath $production -Encoding utf8
    $json=Join-Path $RepositoryRoot 'client/libs4.1/Newtonsoft.Json.dll';Copy-Item -LiteralPath $json -Destination $temporary
    $exe=Join-Path $temporary 'Roster.exe'
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nowarn:0649','/nostdlib+',"/out:$exe","/reference:$json")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    $facade=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2/Facades/netstandard.dll'
    $arguments+='/reference:'+$facade
    & dotnet @arguments $production (Join-Path $PSScriptRoot 'RosterMembershipRefreshFixture.cs')
    if($LASTEXITCODE -ne 0){throw 'Roster fixture compile failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Roster fixture failed'}
}finally{
    $resolved=[IO.Path]::GetFullPath($temporary);$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-roster-membership-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
