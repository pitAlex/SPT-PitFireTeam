param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/AIBossPlayer.cs')
$method=[regex]::Match($source,'(?ms)^        private void ApplyOnYourOwnCommand\(.*?^        \}').Value
if(!$method){throw 'Missing production On Your Own receiver'}
$fixture=(Get-Content -Raw (Join-Path $PSScriptRoot 'GoonOnYourOwnFixture.cs')).Replace('__RECEIVER__',$method)
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-goon-own-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $code=Join-Path $temporary 'Fixture.cs';[IO.File]::WriteAllText($code,$fixture)
    $exe=Join-Path $temporary 'Fixture.exe'
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nostdlib+',"/out:$exe")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    & dotnet @arguments $code (Join-Path $RepositoryRoot 'client/Modules/CompanionCommandPolicy.cs') (Join-Path $RepositoryRoot 'going-rogue/client/GoonCommandPolicy.cs') (Join-Path $RepositoryRoot 'going-rogue/client/GoonCombatConfidence.cs') (Join-Path $RepositoryRoot 'client/BigBrain/FollowerPushRiskPolicy.cs')
    if($LASTEXITCODE -ne 0){throw 'Goon command fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Goon command fixture failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-goon-own-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
