param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [Parameter(Mandatory)][string]$GameRoot
)
$ErrorActionPreference='Stop'
# Read actual installed metadata without loading EFT/Unity into the test process.
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx/plugins/SAIN/SAIN.dll'))
try {
    function Assert-SainMethod([string]$typeName,[string]$name,[string]$returnType,[string[]]$parameters=@()) {
        $type=$assembly.MainModule.Types | Where-Object FullName -eq $typeName
        if (!$type) { throw "Missing installed SAIN type: $typeName" }
        $found=@($type.Methods | Where-Object {
            $_.Name -eq $name -and $_.ReturnType.FullName -eq $returnType -and
            (($_.Parameters | ForEach-Object {$_.ParameterType.FullName}) -join '|') -eq ($parameters -join '|')
        })
        if ($found.Count -ne 1) { throw "Unsupported installed SAIN method: $typeName.$name" }
    }
    $squad='SAIN.BotController.Classes.Squad'
    $manager='SAIN.BotController.Classes.BotSquads'
    $bot='SAIN.Components.BotComponent'
    $container='SAIN.SAINComponent.Classes.Info.BotSquadContainer'
    Assert-SainMethod 'SAIN.SAINEnableClass' 'GetSAIN' 'System.Boolean' @('System.String',($bot+'&'))
    Assert-SainMethod 'SAIN.Components.BotManagerComponent' 'get_Instance' 'SAIN.Components.BotManagerComponent'
    Assert-SainMethod 'SAIN.Components.BotManagerComponent' 'get_BotSquads' $manager
    Assert-SainMethod $bot 'get_Squad' $container
    Assert-SainMethod $container 'get_SquadInfo' $squad
    Assert-SainMethod $container 'set_SquadInfo' 'System.Void' @($squad)
    Assert-SainMethod 'SAIN.SAINComponent.BotBase' 'get_BotOwner' 'EFT.BotOwner'
    Assert-SainMethod $container 'get_DistanceToSquadLeader' 'System.Single'
    Assert-SainMethod $container 'get_BotInGroup' 'System.Boolean'
    Assert-SainMethod $manager 'GetSquad' $squad @('EFT.BotOwner')
    Assert-SainMethod $manager 'RemoveSquad' 'System.Void' @($squad)
    Assert-SainMethod $manager 'get_Squads' ('System.Collections.Generic.Dictionary`2<System.String,'+$squad+'>')
    Assert-SainMethod $manager 'get_SquadArray' ('System.Collections.Generic.HashSet`1<'+$squad+'>')
    Assert-SainMethod $squad '.ctor' 'System.Void'
    Assert-SainMethod $squad 'get_GUID' 'System.String'
    Assert-SainMethod $squad 'get_Members' ('System.Collections.Generic.Dictionary`2<System.String,'+$bot+'>')
    Assert-SainMethod $squad 'get_LeaderId' 'System.String'
    Assert-SainMethod $squad 'get_LeaderComponent' $bot
    Assert-SainMethod $squad 'get_LeaderIsDeadorNull' 'System.Boolean'
    Assert-SainMethod $squad 'findSquadLeader' 'System.Void'
    Assert-SainMethod $squad 'assignSquadLeader' 'System.Void' @($bot)
    Assert-SainMethod $squad 'AddMember' 'System.Void' @($bot)
    Assert-SainMethod $squad 'RemoveMember' 'System.Void' @('System.String')
    Assert-SainMethod $squad 'Dispose' 'System.Void'
    Assert-SainMethod $squad 'add_OnSquadEmpty' 'System.Void' @('System.Action`1<'+$squad+'>')
    Write-Output "Installed SAIN $($assembly.Name.Version): all 25 required metadata signatures verified."
} finally { $assembly.Dispose() }
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'addon/SainPlayerSquadBridge.cs')
$fixture=Get-Content -Raw (Join-Path $PSScriptRoot 'SainPlayerSquadFixture.cs')
$plugin=Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
$gates=[regex]::Matches($plugin,'(?ms)^        public static bool (?:IsSainManTacticAvailable|IsSainFollowerCombatAvailable|UseSainFollowerCombat|ShouldDisableSainForFollower)\b[^;]+;')
if ($gates.Count -ne 4) { throw 'Review the tactic/combat gate declarations.' }
$fixture=$fixture.Replace('__COMBAT_GATE__',($gates.Value -join [Environment]::NewLine))
$botSource=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/BotFollowerPlayer.cs')
$enum=[regex]::Match($botSource,'(?ms)^    public enum FollowerCombatTactic\s*\{.*?^    \}')
$parse=[regex]::Match($botSource,'(?ms)^        public static FollowerCombatTactic ParseCombatTactic\(.*?^        \}')
$coreTactic=[regex]::Match($botSource,'public FollowerCombatTactic CoreCombatTactic => [^;]+;')
$serverSource=Get-Content -Raw (Join-Path $RepositoryRoot 'server/Services/FriendlyTeammateService.cs')
$normalize=[regex]::Match($serverSource,'(?ms)^    private static string NormalizeCombatTactic\(.*?^    \}')
$uiSource=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/OtherPlayerProfileScreenPatch.ProfileOptions.cs')
$availability=[regex]::Match($uiSource,'(?ms)^        private static bool IsUnavailableTactic\(.*?^        \}')
foreach($match in @($enum,$parse,$coreTactic,$normalize,$availability)){if(!$match.Success){throw 'Missing production tactic policy in harness'}}
$fixture=$fixture.Replace('__TACTIC_ENUM__',$enum.Value).Replace('__TACTIC_PARSER__',$parse.Value).Replace('__CORE_TACTIC__',$coreTactic.Value)
$fixture=$fixture.Replace('__SERVER_NORMALIZER__',$normalize.Value.Replace('private static','public static')).Replace('__UI_AVAILABILITY__',$availability.Value.Replace('private static','public static').Replace('pitFireTeam.','pitTeam.pitFireTeam.'))
$defaultAggression=[regex]::Match($serverSource,'(?ms)^    private static float GetDefaultAggressionForTactic\(.*?^    \}')
if(!$defaultAggression.Success){throw 'Missing production aggression default'}
$fixture=$fixture.Replace('__DEFAULT_AGGRESSION__',$defaultAggression.Value.Replace('private static','public static'))
$coreGuard=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/FollowerSainSquadLeaderPatch.cs')
$coreGuard=[regex]::Replace($coreGuard,'Type.GetType\("([^"]+), SAIN"(?:, true)?\)','Type.GetType("$1", true)')
$bridge=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/SainAddonBridge.cs')
$sdk=dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'Cannot locate SDK compiler.' }
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-sain-leader-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    [IO.File]::WriteAllText((Join-Path $temporary 'Production.cs'),$source)
    [IO.File]::WriteAllText((Join-Path $temporary 'Bridge.cs'),$bridge)
    [IO.File]::WriteAllText((Join-Path $temporary 'CoreGuard.cs'),$coreGuard)
    [IO.File]::WriteAllText((Join-Path $temporary 'Fixture.cs'),$fixture)
    $harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object { $_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll' } | Copy-Item -Destination $temporary
    $exe=Join-Path $temporary 'Leadership.exe'
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nostdlib+','/nowarn:8632',"/out:$exe","/reference:$harmony")
    foreach ($reference in @('mscorlib.dll','System.dll','System.Core.dll')) {$arguments+='/reference:'+(Join-Path $framework $reference)}
    $arguments+=@(Join-Path $RepositoryRoot 'client/Components/FollowerCombatTactics.cs'; Join-Path $RepositoryRoot 'addon/SAINAddonPatches.cs'; Join-Path $temporary 'Production.cs'; Join-Path $temporary 'Bridge.cs'; Join-Path $temporary 'Fixture.cs'; Join-Path $temporary 'CoreGuard.cs')
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Leadership harness compilation failed.' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Leadership harness failed.' }
    Write-Output 'Controlled SAIN membership model verified. Actual raid initialization and group transitions still need a raid check.'
}
finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if (!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-sain-leader-*') {throw 'Unsafe temporary cleanup path.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}