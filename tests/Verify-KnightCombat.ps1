param(
    [Parameter(Mandatory)][string]$GameRoot,
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'
$addon = Join-Path $RepositoryRoot 'going-rogue/client/bin/Debug/net472/pitFireTeam.GoingRogue.dll'
$core = Join-Path $RepositoryRoot 'client/bin/Debug/net472'
[Reflection.Assembly]::LoadFrom((Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')) | Out-Null
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
foreach ($root in @($resolver.GetSearchDirectories())) { $resolver.RemoveSearchDirectory($root) }
foreach ($root in @((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed'),
        (Join-Path $GameRoot 'BepInEx/plugins'), (Join-Path $GameRoot 'BepInEx/core'), $core,
        (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'))) {
    $resolver.AddSearchDirectory($root)
}
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$module = [Mono.Cecil.ModuleDefinition]::ReadModule($addon, $parameters)
try {
    if ($module.AssemblyReferences.Name -contains 'SAIN') { throw 'Knight acquired a typed SAIN dependency' }
    $count = 0
    foreach ($reference in @($module.GetTypeReferences()) + @($module.GetMemberReferences())) {
        $type = if ($reference -is [Mono.Cecil.TypeReference]) { $reference } else { $reference.DeclaringType }
        if ($type.GetElementType().Scope.Name -notin @('Assembly-CSharp', 'DrakiaXYZ-BigBrain', 'pitFireTeam', '0Harmony')) { continue }
        $resolved = $reference.Resolve()
        if (!$resolved) { throw "Missing runtime API: $reference" }
        if ($type.GetElementType().Scope.Name -eq 'Assembly-CSharp' -and
            [IO.Path]::GetFullPath($resolved.Module.FileName) -ne [IO.Path]::GetFullPath((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))) {
            throw "Game API resolved outside installed runtime: $reference -> $($resolved.Module.FileName)"
        }
        $count++
    }
    # Check the exact methods patched through Harmony's attributes as well as typed calls.
    $gameRef = $module.AssemblyReferences | Where-Object Name -eq 'Assembly-CSharp'
    $game = $resolver.Resolve($gameRef).MainModule
    foreach ($pair in @(@('BotBoss','get_Followers'), @('BotBoss','ManualUpdate'), @('BotBoss','OfferSelf'),
        @('BossKnight','CalcOptimalPositions'), @('BossKnight','ForceRecalcShootPos'), @('BossKnightLayersStrategy','ForceRecalcShootPos'),
        @('EFT.EftClientBackendSession','SendRaidSettings'), @('BigPipeBotWeaponSelector','ManualUpdate'),
        @('EFT.UI.MerchantsList','Show'), @('EFT.UI.TraderScreensGroup','get_TradersList'),
        @('EFT.UI.TraderScreensGroup','SelectTrader'), @('EFT.UI.TraderScreensGroup','Show'), @('EFT.UI.TradingScreen','Show'),
        @('EFT.Quests.ConditionCounterManager','Test'), @('EFT.Player','OnBeenKilledByAggressor'))) {
        $type = $game.GetType($pair[0])
        if (!$type -or !($type.Methods | Where-Object Name -eq $pair[1])) { throw "Missing Harmony boundary: $pair" }
    }
    $selectorType = $game.GetType('BotWeaponSelector')
    $ownerField = $null
    while ($selectorType -and !$ownerField) {
        $ownerField = $selectorType.Fields | Where-Object Name -eq '_owner'
        $selectorType = if ($selectorType.BaseType) { $selectorType.BaseType.Resolve() } else { $null }
    }
    if (!$ownerField -or $ownerField.FieldType.FullName -ne 'EFT.BotOwner') {
        throw 'Missing native selector owner field used by the shared scoped weapon-switch helper'
    }
    $coreRef = $module.AssemblyReferences | Where-Object Name -eq 'pitFireTeam'
    $coreType = $resolver.Resolve($coreRef).MainModule.GetType('pitTeam.Modules.BossPlayers')
    if (!($coreType.Methods | Where-Object Name -eq 'AddFollower')) { throw 'Missing Core conversion hook' }
    $followerType=$resolver.Resolve($coreRef).MainModule.GetType('pitTeam.Components.BotFollowerPlayer')
    if (!($followerType.Methods | Where-Object {$_.Name -eq 'Init' -and !$_.HasParameters})) { throw 'Missing Birdeye follower Init patch boundary' }
    $screenShow=@($game.GetType('EFT.UI.TradingScreen').Methods | Where-Object {
        $_.Name -eq 'Show' -and ($_.Parameters.ParameterType.FullName -join ',') -eq 'EFT.UI.TradingScreen/TradingScreenController'
    })
    if($screenShow.Count -ne 1){throw 'TradingScreen controller Show overload changed'}
    $rememberedTrader=$game.GetType('EFT.UI.MerchantsList').Fields | Where-Object Name -eq '_currentTrader'
    if(!$rememberedTrader -or $rememberedTrader.FieldType.FullName -ne 'EFT.Trading.Trader'){throw 'MerchantsList remembered trader field changed'}
    $counterTest=@($game.GetType('EFT.Quests.ConditionCounterManager').Methods | Where-Object {
        $_.Name -eq 'Test' -and $_.IsStatic -and ($_.Parameters.ParameterType.FullName -join ',') -eq 'System.Int32,EFT.Quests.TaskConditionCounter,EFT.Quests.ConditionCheck[]'
    })
    if($counterTest.Count -ne 1){throw 'Native static quest counter Test overload changed'}
    Write-Output "PASS: $count addon Core/installed EFT/BigBrain/Harmony API references and 17 patch targets resolved. No typed SAIN dependency."
}
finally { $module.Dispose(); $resolver.Dispose() }

# The extracted push planner must remain the exact Rifleman implementation, apart
# from its method name and location. Compare against the repository baseline.
$baseline = git -C $RepositoryRoot show ed865b3c3a34ecdbf4862b27d2c75131b408966b:client/BigBrain/FollowerCombatDefault.cs
if ($LASTEXITCODE -ne 0) { throw 'Unable to read Rifleman baseline' }
$before = [regex]::Match(($baseline -join "`n"), '(?ms)^        private bool TryBuildAndCommitPushSupportDecision\(.*?^        \}').Value
$current = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatSupportPlanner.cs')
$after = [regex]::Match($current, '(?ms)^        private bool TryPush\(.*?^        \}').Value
if (!$before -or !$after -or (($before.Replace('TryBuildAndCommitPushSupportDecision','TryPush') -replace '\s','') -ne ($after -replace '\s',''))) {
    throw 'Shared push-support extraction drifted from Rifleman baseline; review intended changes and update parity evidence.'
}
Write-Output 'PASS: extracted push-support planner matches the existing Rifleman implementation.'
