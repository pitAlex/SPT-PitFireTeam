param([string]$RepositoryRoot,[Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference='Stop'
if(!$RepositoryRoot){$RepositoryRoot=Split-Path $PSScriptRoot -Parent}
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $karma=$assembly.MainModule.GetType('EFT.Profile').Fields | Where-Object Name -eq 'KarmaValue'
    if(!$karma.IsInitOnly -or $karma.FieldType.FullName -ne 'System.Single'){throw 'Native PMC karma field boundary changed'}
    $stop=$assembly.MainModule.GetType('EFT.LocalGame').Methods | Where-Object {$_.Name -eq 'Stop' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'System.String|EFT.ExitStatus|System.String|System.Single'}
    if(!$stop -or ($stop.Parameters.Name -join '|') -ne 'profileId|exitStatus|exitName|delay'){throw 'Native raid-end hook boundary changed'}
    $sound=$assembly.MainModule.GetType('EFT.UI.GUISounds').Methods | Where-Object {$_.Name -eq 'PlayKarmaSound' -and $_.IsPublic -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'System.Boolean'}
    if(!$sound){throw 'Native karma sound boundary changed'}
    foreach($name in @('Survived','Runner','Killed','MissingInAction','Transit')) {
        if(!($assembly.MainModule.GetType('EFT.ExitStatus').Fields | Where-Object Name -eq $name)){throw "Missing native exit status: $name"}
    }
} finally {$assembly.Dispose()}
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-pmc-karma-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $exe=Join-Path $temporary 'Karma.exe'
    $json=Join-Path $RepositoryRoot 'client/libs4.1/Newtonsoft.Json.dll'
    $harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $json,$harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object { $_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll' } |
        Copy-Item -Destination $temporary
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:annotations','/nostdlib+',"/out:$exe","/reference:$json","/reference:$harmony")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    $arguments+=@((Join-Path $RepositoryRoot 'tests/PmcKarmaClientFixture.cs'),(Join-Path $RepositoryRoot 'client/Modules/PmcKarmaRuntime.cs'),(Join-Path $RepositoryRoot 'shared/PmcKarmaPolicy.cs'))
    & dotnet @arguments
    if($LASTEXITCODE -ne 0){throw 'PMC karma client fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'PMC karma client regression failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-pmc-karma-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
