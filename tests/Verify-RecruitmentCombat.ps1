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
    $nativeBot=$installedSain.MainModule.GetType('SAIN.Components.BotComponent')
    $info=$nativeBot.Properties | Where-Object Name -eq 'Info'
    $nativeInfo=$installedSain.MainModule.GetType($info.PropertyType.FullName)
    $personality=$nativeInfo.Properties | Where-Object Name -eq 'Personality'
    $personalityAssembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot ('BepInEx/plugins/SAIN/'+$personality.PropertyType.Scope.Name+'.dll')))
    try {
        $nativePersonality=$personalityAssembly.MainModule.GetType($personality.PropertyType.FullName)
        foreach ($name in @('Coward','Rat','Normal','Chad','GigaChad','Wreckless','SnappingTurtle','Timmy')) {
            if (!($nativePersonality.Fields | Where-Object {$_.Name -eq $name -and $_.HasConstant})) {throw "Missing native personality: $name"}
        }
    } finally {$personalityAssembly.Dispose()}
}
finally { $installedSain.Dispose() }
$request = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotGroupRequestPatch.cs')
$boundary = $request.IndexOf('    internal class HoldRequestPatch')
if ($boundary -lt 0) { throw 'Missing recruitment class boundary' }
$request = $request.Substring(0, $boundary) + '}'
$bridge = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/SainGoalEnemyBridge.cs')
$talk = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotTalkPatch.cs')
$sayGateStart = $talk.IndexOf('    internal class BotTalkSayPatch')
if ($sayGateStart -lt 0) { throw 'Missing production immediate speech gate' }
$sayGate = 'namespace pitTeam.Patches {' + $talk.Substring($sayGateStart)
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
$inputFixture = Get-Content -Raw (Join-Path $PSScriptRoot 'RecruitmentInputFixture.cs')
foreach($boundary in @(
    @{File='client/Patches/QuickPanelPatch.cs';Name='CanShowCooperation';Placeholder='__COOPERATION_AVAILABILITY__'},
    @{File='client/Patches/GestureMenuPatch.cs';Name='AddCooperationToHelpGroup';Placeholder='__HELP_GROUP_METHOD__'})) {
    $source=Get-Content -Raw (Join-Path $RepositoryRoot $boundary.File)
    $method=[regex]::Match($source,'(?ms)^        internal static [^\r\n]*\b'+$boundary.Name+'\(.*?^        \}').Value
    if(!$method){throw "Recruitment input boundary changed: $($boundary.Name)"}
    $inputFixture=$inputFixture.Replace($boundary.Placeholder,$method)
}
$inputPatch=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotRecruitPatch.cs')
$followerSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/BotFollowerPlayer.cs')
$captureMethods = foreach ($name in @('TryGetNativeSainPersonality','GetSainBot')) {
    $methods=[regex]::Matches($followerSource,'(?ms)^        (?:internal|private) static [^\r\n]*\b'+$name+'\(.*?^        \}')
    if ($methods.Count -ne 1) {throw "Native recruitment reader boundary changed: $name"}
    $methods[0].Value
}
$fixture=$fixture.Replace('__NATIVE_PERSONALITY_CAPTURE__',($captureMethods -join "`n"))
$bossSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/BossPlayers.cs')
$constants = [regex]::Matches($bossSource,'(?m)^        private (?:const float|static readonly Random|static readonly object) Recruit[^\r\n]+;') | ForEach-Object Value
$constants = @($constants | ForEach-Object {$_.Replace('readonly Random','readonly System.Random').Replace('new Random()','new System.Random()')})
$create = [regex]::Match($bossSource,'(?ms)^        private static float CreateRecruitCombatAggression\(.*?^        \}').Value
if ($constants.Count -ne 4 -or !$create) {throw 'Recruit aggression creation boundary changed'}
$fixture += 'namespace pitTeam.Modules { public partial class BossPlayers {' + ($constants -join "`n") + $create + '}}'
$fixture += Get-Content -Raw (Join-Path $PSScriptRoot 'RecruitmentPersonalityFixture.cs')
$spawnSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotsControllerPatch.cs')
$spawnSide = [regex]::Match($spawnSource,'(?ms)^        internal static EPlayerSide ResolveFollowerSpawnSide\(.*?^        \}').Value
if (!$spawnSide) {throw 'Saved follower faction boundary changed'}
$fixture += 'namespace pitTeam.Patches { internal class BotsControllerPatch {' + $spawnSide + '}}'
$mapping = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/RecruitCombatAggression.cs')
$voiceFixture = Get-Content -Raw (Join-Path $PSScriptRoot 'RecruitmentSpeechFixture.cs')
$imports = foreach ($source in @($request, $bridge, $talk, $sayGate, $sainHooks, $fixture, $voiceFixture,$inputFixture,$inputPatch)) {
    [regex]::Matches($source, '(?m)^using [^\r\n]+;') | ForEach-Object Value
}
$sources = foreach ($source in @($request, $bridge, $talk, $sayGate, $sainHooks, $fixture, $voiceFixture, $mapping,$inputFixture,$inputPatch)) {
    [regex]::Replace($source, '(?m)^using [^\r\n]+;\r?\n', '')
}
$code = '#nullable disable' + "`n#pragma warning disable CS8632`n" + (($imports | Select-Object -Unique) -join "`n") + "`n" + ($sources -join "`n")
$code += "`npublic static class Entry { public static int Main() { try { RecruitmentSpeechChecks.Install(); Console.WriteLine(RecruitmentCombatChecks.Run() + "" recruitment combat checks passed.""); Console.WriteLine(RecruitmentSpeechChecks.Run() + "" recruitment speech checks passed with real Harmony.""); Console.WriteLine(RecruitmentPersonalityChecks.Run() + "" recruitment personality checks passed.""); Console.WriteLine(RecruitmentInputChecks.Run() + "" recruitment input checks passed.""); return 0; } catch (Exception e) { Console.Error.WriteLine(e); return 1; } } }"
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
