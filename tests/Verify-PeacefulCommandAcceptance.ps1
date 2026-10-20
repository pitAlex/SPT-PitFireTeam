param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/AIBossPlayer.cs')
$fixture = Get-Content -Raw (Join-Path $PSScriptRoot 'PeacefulCommandAcceptanceFixture.cs')
foreach ($entry in @(
    @{ Name = 'ApplyHoldIntent'; Marker = '/* HOLD HANDLER */' },
    @{ Name = 'HasHoldCombatContext'; Marker = '/* COMBAT CONTEXT */' },
    @{ Name = 'ApplyTemporaryCombatAggressionCommand'; Marker = '/* AGGRESSION HANDLER */' })) {
    $method = [regex]::Match($source, '(?ms)^        private (?:static )?[^\r\n]*\b' + $entry.Name + '\(.*?^        \}').Value
    if (!$method) { throw "Production command boundary changed: $($entry.Name)" }
    $fixture = $fixture.Replace($entry.Marker, $method)
}
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-peaceful-commands-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $path = Join-Path $temporary 'Commands.cs'
    $exe = Join-Path $temporary 'Commands.exe'
    [IO.File]::WriteAllText($path, $fixture)
    $arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$exe")
    foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
        $arguments += "/reference:$(Join-Path $framework $reference)"
    }
    & dotnet @arguments $path
    if ($LASTEXITCODE -ne 0) { throw 'Peaceful command fixture compilation failed' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Peaceful command fixture failed' }
} finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-peaceful-commands-*') { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
