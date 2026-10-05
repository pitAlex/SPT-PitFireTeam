param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [Parameter(Mandatory)][string]$GameRoot
)
# Exercise the production request/conversion flow and reflection-only native enemy reader.
# Speech, game lifecycle and native enemy activity use controlled stand-ins, not a raid simulation.
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$installedSain = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx/plugins/SAIN/SAIN.dll'))
try {
    foreach ($boundary in @(
        @{ Type = 'SAIN.Patches.Talk.BotTalkPatch'; Method = 'PatchPrefix'; PhraseIndex = 1; PhraseName = 'type' },
        @{ Type = 'SAIN.Patches.Talk.PlayerTalkPatch'; Method = 'PatchPrefix'; PhraseIndex = 1; PhraseName = 'phrase' },
        @{ Type = 'SAIN.Components.PlayerComponentSpace.PlayerComponent'; Method = 'PlayVoiceLine'; PhraseIndex = 0; PhraseName = 'phrase' },
        @{ Type = 'SAIN.SAINComponent.Classes.Talk.SAINBotTalkClass'; Method = 'Say'; PhraseIndex = 0; PhraseName = 'phrase' }
    )) {
        $methods = @($installedSain.MainModule.GetType($boundary.Type).Methods | Where-Object Name -eq $boundary.Method)
        if ($methods.Count -ne 1 -or $methods[0].ReturnType.FullName -ne 'System.Boolean' -or
            $methods[0].Parameters[$boundary.PhraseIndex].ParameterType.FullName -ne 'EPhraseTrigger' -or
            $methods[0].Parameters[$boundary.PhraseIndex].Name -ne $boundary.PhraseName) {
            throw "Unsupported native speech signature: $($boundary.Type).$($boundary.Method)"
        }
    }
    Write-Output 'Installed SAIN recruitment speech signatures verified.'
}
finally { $installedSain.Dispose() }
$request = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotGroupRequestPatch.cs')
$boundary = $request.IndexOf('    internal class HoldRequestPatch')
if ($boundary -lt 0) { throw 'Missing recruitment class boundary' }
$request = $request.Substring(0, $boundary) + '}'
$bridge = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/SainGoalEnemyBridge.cs')
$talk = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotTalkPatch.cs')
$talk = $talk.Substring(0, $talk.IndexOf('    public static class FollowerContactPhraseGate')) + '}'
$sainSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/SAINPatches.cs')
$sainMethods = foreach ($name in @('BypassSainTalkPatchForFollower', 'UseVanillaTalkForFollower',
        'DisableSainTalkSayForFollower', 'DisableSainTalkUpdateForFollower', 'IsSainTalkOwnerFollower')) {
    $matches = [regex]::Matches($sainSource, '(?ms)^        private static [^\r\n]*\b' + $name + '\(.*?^        \}')
    if ($matches.Count -ne 1) { throw "Expected one production speech hook: $name" }
    $matches[0].Value
}
$sainHooks = 'using EFT;
using HarmonyLib;
using System.Reflection;
using pitTeam.Modules;
namespace pitTeam.Patches { internal static class SAINPatch {
internal static PropertyInfo sainPlayerComponentPlayerProperty = typeof(NativeSpeechPlayer).GetProperty("Player");' +
    ($sainMethods -join "`n") + '}}'
$fixture = Get-Content -Raw (Join-Path $PSScriptRoot 'RecruitmentCombatFixture.cs')
$voiceFixture = Get-Content -Raw (Join-Path $PSScriptRoot 'RecruitmentSpeechFixture.cs')
$imports = foreach ($source in @($request, $bridge, $talk, $sainHooks, $fixture, $voiceFixture)) {
    [regex]::Matches($source, '(?m)^using [^\r\n]+;') | ForEach-Object Value
}
$sources = foreach ($source in @($request, $bridge, $talk, $sainHooks, $fixture, $voiceFixture)) {
    [regex]::Replace($source, '(?m)^using [^\r\n]+;\r?\n', '')
}
$code = '#nullable disable' + "`n#pragma warning disable CS8632`n" + (($imports | Select-Object -Unique) -join "`n") + "`n" + ($sources -join "`n")
$code += "`npublic static class Entry { public static int Main() { try { RecruitmentSpeechChecks.Install(); Console.WriteLine(RecruitmentCombatChecks.Run() + "" recruitment combat checks passed.""); Console.WriteLine(RecruitmentSpeechChecks.Run() + "" recruitment speech checks passed with real Harmony.""); return 0; } catch (Exception e) { Console.Error.WriteLine(e); return 1; } } }"
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
