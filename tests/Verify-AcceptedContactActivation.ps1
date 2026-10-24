param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
# Exercise the production goal postfix, contact activation and existing EFT wake helper.
# EFT lifecycle/standby are stand-ins; this does not establish in-raid SAIN perception.
$ErrorActionPreference = 'Stop'
function Read-Method([string]$Path, [string]$Pattern) {
    $source = Get-Content -Raw (Join-Path $RepositoryRoot $Path)
    $matches = [regex]::Matches($source, '(?ms)^        ' + $Pattern + '\(.*?^        \}')
    if ($matches.Count -ne 1) { throw "Expected one method $Pattern, found $($matches.Count)" }
    return $matches[0].Value
}
$wake = Read-Method 'client/Patches/LootPatrolSafetyPatch.cs' 'public static void WakeHostileBot'
$postfix = Read-Method 'client/Patches/BotMemoryPatch.cs' 'private static void PatchPostfix'
$postfix = '        [PatchPostfix]' + "`n" + $postfix
$helper = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FollowerAcceptedContactActivation.cs')
$fixture = (Get-Content -Raw (Join-Path $PSScriptRoot 'AcceptedContactActivationFixture.cs')).Replace('__WAKE__', $wake).Replace('__POSTFIX__', $postfix)
$imports = foreach ($source in @($helper, $fixture)) {
    [regex]::Matches($source, '(?m)^using [^\r\n]+;') | ForEach-Object Value
}
$sources = foreach ($source in @($helper, $fixture)) {
    [regex]::Replace($source, '(?m)^using [^\r\n]+;\r?\n', '')
}
$code = '#nullable disable' + "`n" + (($imports | Select-Object -Unique) -join "`n") + "`n" + ($sources -join "`n")
$code += "`npublic static class Entry { public static int Main() { try { Console.WriteLine(AcceptedContactChecks.Run() + "" accepted-contact activation checks passed.""); return 0; } catch (Exception e) { Console.Error.WriteLine(e); return 1; } } }"
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'Cannot find SDK compiler' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-contact-activation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $sourcePath = Join-Path $testRoot 'ContactActivation.cs'
    $exePath = Join-Path $testRoot 'ContactActivation.exe'
    [IO.File]::WriteAllText($sourcePath, $code)
    $arguments = @($compiler, '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', "/out:$exePath")
    foreach ($reference in @('mscorlib.dll', 'System.dll', 'System.Core.dll')) {
        $arguments += '/reference:' + (Join-Path $framework $reference)
    }
    & dotnet @arguments $sourcePath
    if ($LASTEXITCODE -ne 0) { throw 'Contact activation harness compilation failed' }
    & $exePath
    if ($LASTEXITCODE -ne 0) { throw 'Contact activation checks failed' }
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-contact-activation-*') { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
