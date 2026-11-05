param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/BotFollowerPlayer.cs')
$determination=[regex]::Match($source,'(?m)^        public float EffectiveSelfDetermination =>.*$').Value
$aggression=[regex]::Match($source,'(?ms)^        public float EffectiveCombatAggression\s*\{.*?^        \}').Value
if(!$determination -or !$aggression){throw 'Production effective value properties missing'}
$fixture=(Get-Content -Raw (Join-Path $PSScriptRoot 'TeamSplit/Program.cs')).Replace('/* EFFECTIVE_VALUES */',$determination+"`n"+$aggression)
$boss=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/AIBossPlayer.cs')
$handlers=@('ApplyTeamSplitCommand','ApplyRegroupCommand','ApplyNeedHelpPhrase','ApplyNeedSniperPhrase','ApplyTemporaryCombatAggressionCommand') | ForEach-Object {
    $method=[regex]::Match($boss,'(?ms)^        private void '+$_+'\(.*?^        \}').Value
    if(!$method){throw "Missing handler $_"}; $method
}
$attention=[regex]::Match($boss,'(?ms)^        private static void ClearEnemyStateForAttention\(.*?^        \}').Value
if(!$attention -or $attention -match 'clearSharedGroup' -or
   $boss -notmatch 'ClearEnemyStateForAttention\(follower, clearedGroupIds\);'){
    throw 'Attention must retain unconditional shared-group reset'
}
$handlers+=$attention
$recipients=[regex]::Match($boss,'(?m)^        private IEnumerable<BotOwner> CommandFollowers =>.*$').Value
$fixture=$fixture.Replace('/* COMMAND_HANDLERS */',($handlers -join "`n")).Replace('/* COMMAND_RECIPIENTS */',$recipients)
$output=Join-Path $PSScriptRoot 'artifacts/team-split/fixture'
New-Item -ItemType Directory -Force -Path $output | Out-Null
[IO.File]::WriteAllText((Join-Path $output 'Program.cs'),$fixture)
$project=@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable></PropertyGroup><ItemGroup>
<Compile Include="$RepositoryRoot/client/Modules/CompanionTeamCommands.cs" Link="Commands.cs" />
<Compile Include="$RepositoryRoot/client/Components/BotFollowerPlayer.TeamSplit.cs" Link="Follower.cs" />
<Compile Include="$RepositoryRoot/going-rogue/client/Combat/GoonTeamSplit.cs" Link="Split.cs" />
</ItemGroup></Project>
"@
[IO.File]::WriteAllText((Join-Path $output 'Fixture.csproj'),$project)
& dotnet run --project (Join-Path $output 'Fixture.csproj') --nologo
if($LASTEXITCODE -ne 0){throw 'Team Split production fixture failed'}

# Check the installed UI boundary and stable phrase registration separately from state behavior.
$plugin=Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
foreach($entry in @(@('TeamStatus',10001),@('TeamSplit',10005),@('TeamCombine',10006))){
    if($plugin -notmatch ('\b'+$entry[0]+'\s*=\s*'+$entry[1]+'\b')){throw "Stable phrase ID changed: $($entry[0])"}
}
$visibility=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/TeamSplitMenuAvailabilityPatch.cs')
if($visibility -match '\.Binds\b|SetChanges\('){throw 'Availability must never rewrite native bindings'}
$groups=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/GestureMenuPatch.cs')
if($groups -notmatch '(?s)item.gameObject.name == "COMMAND"\s*\? new List<CustomPhrases> \{ CustomPhrases.TeamSplit, CustomPhrases.TeamCombine \}\s*: new List<CustomPhrases> \{ CustomPhrases.TeamStatus \}' -or
   $groups -notmatch '(?s)localizationKey == "COMMAND"[^}]*CustomPhrases.TeamSplit,\s*\(EPhraseTrigger\)CustomPhrases.TeamCombine' -or
   $groups -notmatch '(?s)localizationKey == "TEAM STATUS"[^}]*phrases = \[\s*\(EPhraseTrigger\)CustomPhrases.TeamStatus\s*\]'){
    throw 'Both menu paths must put Split/Combine under Command and keep Status Report under Team Status'
}
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
$module=[Mono.Cecil.ModuleDefinition]::ReadModule('E:/SPTushanka/EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')
try {
    $menu=$module.GetType('EFT.UI.Gestures.GesturesMenu')
    if(!($menu.Fields | Where-Object {$_.Name -eq '_availablePhrases' -and $_.FieldType.FullName -eq 'System.Collections.Generic.HashSet`1<EPhraseTrigger>'})){throw 'Installed menu availability field changed'}
    if(!($menu.Methods | Where-Object {$_.Name -eq 'Show' -and $_.Parameters.Count -eq 0})){throw 'Installed menu Show signature changed'}
    $bind=$menu.Methods | Where-Object {$_.Name -eq 'Bind' -and $_.Parameters.Count -eq 2}
    if(!$bind -or $bind.Parameters[1].ParameterType.FullName -ne 'System.Int32' -or
        !($bind.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SetChanges'})){
        throw 'Installed native ID binding/persistence boundary changed'
    }
} finally {$module.Dispose()}
Write-Output 'PASS: stable phrase IDs, both menu paths and installed native ID binding/persistence signatures.'
