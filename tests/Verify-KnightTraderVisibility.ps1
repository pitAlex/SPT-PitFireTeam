param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[string]$GameRoot='E:/SPTushanka')
$ErrorActionPreference='Stop'
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-knight-visibility-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object {$_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll'} | Copy-Item -Destination $temporary
    $exe=Join-Path $temporary 'Visibility.exe'
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nostdlib+',"/out:$exe","/reference:$harmony")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    & dotnet @arguments (Join-Path $RepositoryRoot 'going-rogue/client/Trading/KnightTraderVisibility.cs') (Join-Path $RepositoryRoot 'tests/KnightTraderVisibilityFixture.cs')
    if($LASTEXITCODE -ne 0){throw 'Trader visibility fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Trader visibility fixture failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-knight-visibility-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
