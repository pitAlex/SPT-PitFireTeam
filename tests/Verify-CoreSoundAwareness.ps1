param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference='Stop'
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-core-sound-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $exe=Join-Path $temporary 'Sound.exe'
    $argsList=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nostdlib+',"/out:$exe")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$argsList+='/reference:'+(Join-Path $framework $reference)}
    foreach($source in @('client/Modules/FollowerSoundAwareness.cs','tests/CoreSoundAwarenessFixture.cs')){$argsList+=(Join-Path $RepositoryRoot $source)}
    & dotnet @argsList
    if($LASTEXITCODE -ne 0){throw 'Core sound fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Core sound fixture failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-core-sound-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
