param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [Parameter(Mandatory)][string]$GameRoot
)
# Exercise the production request/conversion flow and reflection-only native enemy reader.
# Speech, game lifecycle and native enemy activity use controlled stand-ins, not a raid simulation.
$ErrorActionPreference = 'Stop'
$request = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotGroupRequestPatch.cs')
$boundary = $request.IndexOf('    internal class HoldRequestPatch')
if ($boundary -lt 0) { throw 'Missing recruitment class boundary' }
$request = $request.Substring(0, $boundary) + '}'
$bridge = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/SainGoalEnemyBridge.cs')
$fixture = Get-Content -Raw (Join-Path $PSScriptRoot 'RecruitmentCombatFixture.cs')
$imports = foreach ($source in @($request, $bridge, $fixture)) {
    [regex]::Matches($source, '(?m)^using [^\r\n]+;') | ForEach-Object Value
}
$sources = foreach ($source in @($request, $bridge, $fixture)) {
    [regex]::Replace($source, '(?m)^using [^\r\n]+;\r?\n', '')
}
$code = '#nullable disable' + "`n#pragma warning disable CS8632`n" + (($imports | Select-Object -Unique) -join "`n") + "`n" + ($sources -join "`n")
$code += "`npublic static class Entry { public static int Main() { try { Console.WriteLine(RecruitmentCombatChecks.Run() + "" recruitment combat checks passed.""); return 0; } catch (Exception e) { Console.Error.WriteLine(e); return 1; } } }"
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'Cannot find SDK compiler' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-recruitment-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $sourcePath = Join-Path $testRoot 'Recruitment.cs'
    $exePath = Join-Path $testRoot 'Recruitment.exe'
    [IO.File]::WriteAllText($sourcePath, $code)
    $harmony = Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $harmony -Destination $testRoot
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object { $_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll' } |
        Copy-Item -Destination $testRoot
    $arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$exePath", "/reference:$harmony")
    foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
        $arguments += '/reference:' + (Join-Path $framework $reference)
    }
    $arguments += $sourcePath
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Recruitment harness compilation failed' }
    & $exePath
    if ($LASTEXITCODE -ne 0) { throw 'Recruitment combat checks failed' }
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-recruitment-*') { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
