param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
# Compile the entire production objective/base with controlled boundary stand-ins.
$sources = @(
    (Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatObjectiveBase.cs')),
    (Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatNeedSniperObjective.cs')),
    (Get-Content -Raw (Join-Path $PSScriptRoot 'NeedSniperArrivalFixture.cs'))
)
$imports = foreach ($source in $sources) { [regex]::Matches($source, '(?m)^using [^\r\n]+;') | ForEach-Object Value }
$bodies = foreach ($source in $sources) { [regex]::Replace($source, '(?m)^using [^\r\n]+;\r?\n', '') }
$code = '#nullable disable' + "`n#pragma warning disable CS8632`n" + (($imports | Select-Object -Unique) -join "`n") + "`n" + ($bodies -join "`n")
$code += "`npublic static class Entry { public static int Main() { try { Console.WriteLine(NeedSniperArrivalChecks.Run() + "" ordered sniper arrival checks passed.""); return 0; } catch (Exception e) { Console.Error.WriteLine(e); return 1; } } }"
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'Cannot find SDK compiler' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-need-sniper-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $sourcePath = Join-Path $testRoot 'NeedSniper.cs'; $exePath = Join-Path $testRoot 'NeedSniper.exe'
    [IO.File]::WriteAllText($sourcePath, $code)
    $arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$exePath")
    foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) { $arguments += '/reference:' + (Join-Path $framework $reference) }
    & dotnet @arguments $sourcePath
    if ($LASTEXITCODE -ne 0) { throw 'Ordered sniper harness compilation failed' }
    & $exePath
    if ($LASTEXITCODE -ne 0) { throw 'Ordered sniper arrival checks failed' }
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-need-sniper-*') { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
