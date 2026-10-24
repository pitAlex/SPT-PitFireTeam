param(
    [Parameter(Mandatory=$true)][string]$RepositoryRoot,
    [Parameter(Mandatory=$true)][string]$GameRoot
)
$ErrorActionPreference='Stop'
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-encounters-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/PlayerPatch.cs')
    $fixture=Get-Content -Raw (Join-Path $RepositoryRoot 'tests/FriendlyEncounterPenaltyFixture.cs')
    foreach($pair in @(@('RECORD','TryRecordPlayerKillMessage'),@('KIND','GetKillMessageKind'),@('TEXT','GetKillMessageText'))) {
        $method=[regex]::Match($source,'(?ms)^        private static (?:void|string) '+$pair[1]+'\(.*?^        \}').Value
        if(!$method){throw ('Kill report extraction boundary changed: '+$pair[1])}
        $fixture=$fixture.Replace('__'+$pair[0]+'_METHOD__',$method)
    }
    Set-Content (Join-Path $temporary 'Fixture.cs') $fixture
    $exe=Join-Path $temporary 'Encounters.exe'
    $newtonsoft=Join-Path $RepositoryRoot 'client/libs4.1/Newtonsoft.Json.dll'
    Copy-Item -LiteralPath $newtonsoft -Destination $temporary
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:annotations','/nostdlib+',"/out:$exe","/reference:$newtonsoft")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    $arguments+='/reference:'+(Join-Path $framework 'netstandard.dll')
    $arguments+=@((Join-Path $temporary 'Fixture.cs'),(Join-Path $RepositoryRoot 'client/Modules/FriendlyEncounterPenaltyRuntime.cs'),(Join-Path $RepositoryRoot 'shared/FriendlyEncounterPenaltyPolicy.cs'))
    & dotnet @arguments
    if($LASTEXITCODE -ne 0){throw 'Encounter penalty fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Encounter penalty regression failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-encounters-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
