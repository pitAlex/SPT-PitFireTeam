param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$GameRoot = 'E:/SPTushanka'
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')
foreach ($assemblyPath in @(
    (Join-Path $RepositoryRoot 'client/libs4.1/Assembly-CSharp.dll'),
    (Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))) {
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule($assemblyPath)
    try {
        $controller = $module.GetType('EFT.Player/FirearmController')
        foreach ($name in @('SetTriggerPressed', 'ManualUpdate')) {
            $method = @($controller.Methods | Where-Object Name -eq $name)
            $expected = if ($name -eq 'SetTriggerPressed') {'System.Boolean'} else {'System.Single'}
            if ($method.Count -ne 1 -or ($method[0].Parameters.ParameterType.FullName -join ',') -ne $expected) { throw "Controller hook changed: $name" }
        }
        $playerField = $module.GetType('EFT.Player/ItemHandsController').Fields | Where-Object Name -eq '_player'
        if ($playerField.FieldType.FullName -ne 'EFT.Player') { throw 'Injected controller player field changed' }
        $automatic = $module.GetType('EFT.Player/FirearmController/AutomaticFireOperation')
        $round = @($automatic.Methods | Where-Object Name -eq 'InternalOnFireEvent')
        if ($round.Count -ne 1 -or $round[0].Parameters.Count -ne 0) { throw 'Automatic round hook changed' }
        $calls = @($round[0].Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.Name })
        if ([Array]::IndexOf($calls,'ProcessInventoryOperationsForSingleshotWithMalf') -lt 0 -or
            [Array]::IndexOf($calls,'ProcessInventoryOperationsForSingleshotWithMalf') -ge [Array]::IndexOf($calls,'MakeShot')) { throw 'Review changed automatic ammo/shot ordering' }
        $idle = $automatic.Methods | Where-Object Name -eq 'SwitchToIdling'
        if (!$idle.IsPublic -or $idle.Parameters.Count -ne 0) { throw 'Native automatic idle transition changed' }
        $idleCalls = @($idle.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.Name })
        foreach ($required in @('SetTriggerPressed','set_State','InitiateOperation','Start')) {
            if ($required -notin $idleCalls) { throw "Idle transition missing $required" }
        }
        if ($idleCalls | Where-Object { $_ -match 'Ammo|Shot|InventoryOperations' }) { throw 'Idle transition now mutates shot/ammo transaction' }
        $allRoundEntries = @($controller.NestedTypes | ForEach-Object { $_.Methods | Where-Object Name -eq 'InternalOnFireEvent' })
        if ($allRoundEntries.Count -ne 1) { throw 'New automatic round override requires review' }
        Write-Output "Verified controller hooks, automatic pre-ammo admission and idle transition: $assemblyPath"
    } finally { $module.Dispose() }
}
$sainModule = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'BepInEx/plugins/SAIN/SAIN.dll'))
try {
    $manual = $sainModule.GetType('SAIN.SAINComponent.Classes.WeaponFunction.ManualShootClass')
    $entry = @($manual.Methods | Where-Object Name -eq 'TryShoot')
    if ($entry.Count -ne 1 -or ($entry[0].Parameters.ParameterType.FullName -join ',') -ne
        'SAIN.SAINComponent.Classes.EnemyClasses.Enemy,UnityEngine.Vector3,System.Boolean,SAIN.Models.Enums.EShootReason') { throw 'SAIN manual hook changed' }
    foreach ($name in @('Reason','ShootPosition')) { if ($name -notin $manual.Properties.Name) { throw "Manual accessor changed: $name" } }
    $getSain = $sainModule.GetType('SAIN.SAINEnableClass').Methods | Where-Object Name -eq 'GetSAIN'
    if (($getSain.Parameters.ParameterType.FullName -join ',') -ne 'System.String,SAIN.Components.BotComponent&') { throw 'SAIN lookup changed' }
    Write-Output 'Verified installed SAIN manual-fire accessors and hook.'
} finally { $sainModule.Dispose() }

$sdk = dotnet --list-sdks | Select-Object -Last 1
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'SDK missing' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-fire-safety-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $harmony = Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object { $_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll' } | Copy-Item -Destination $temporary
    # Compile the production firearm vector and the production lane geometry.
    # Grenade-only APIs are excluded; movement, inventory and animation remain controlled stand-ins.
    $base = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/Actions/FollowerCombatActionBase.cs')
    $begin = $base.IndexOf('        internal static bool TryGetActualFirearmShotVector(')
    $end = $base.IndexOf('        protected CoverSearchType SetAttackCoverSearchType', $begin)
    if ($begin -lt 0 -or $end -le $begin) { throw 'Firearm vector extraction boundary changed' }
    $vector = 'using EFT; using UnityEngine; namespace pitTeam.BigBrain.Actions { internal static class FollowerCombatActionBase {' +
        $base.Substring($begin,$end-$begin) + '}}'
    [IO.File]::WriteAllText((Join-Path $temporary 'Vector.cs'), $vector)
    $geometry = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerShotSafety.cs')
    $end = $geometry.IndexOf('        public static bool IsFriendlyNearImpact(')
    $tail = $geometry.IndexOf('        private static bool IsFinite(Vector3 value)')
    if ($end -lt 0 -or $tail -le $end) { throw 'Shot geometry extraction boundary changed' }
    [IO.File]::WriteAllText((Join-Path $temporary 'Geometry.cs'), $geometry.Substring(0,$end)+$geometry.Substring($tail))
    $sources = @('client/Utils/FollowerFireSafety.cs','client/Patches/FollowerShootDataSafetyPatch.cs',
        'client/Patches/FollowerFirearmSafetyPatch.cs','client/Patches/FollowerSainFriendlyFirePatch.cs',
        'client/Patches/FollowerSainManualFireSafety.cs','client/Modules/SainBotOwnerAccessor.cs','tests/FollowerFireSafetyFixture.cs')
    $paths = @((Join-Path $temporary 'Vector.cs'),(Join-Path $temporary 'Geometry.cs'))
    foreach ($sourcePath in $sources) {
        $source = Get-Content -Raw (Join-Path $RepositoryRoot $sourcePath)
        $source = [regex]::Replace($source,'Type.GetType\("([^"]+), SAIN"\)','FireSafetyChecks.ResolveType("$1")')
        $path = Join-Path $temporary ([IO.Path]::GetFileName($sourcePath))
        [IO.File]::WriteAllText($path,$source); $paths += $path
    }
    $exe = Join-Path $temporary 'FireSafety.exe'
    $arguments = @($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nowarn:8632','/nostdlib+',"/out:$exe","/reference:$harmony")
    foreach ($reference in @('mscorlib.dll','System.dll','System.Core.dll')) { $arguments += "/reference:$(Join-Path $framework $reference)" }
    & dotnet @arguments @paths
    if ($LASTEXITCODE -ne 0) { throw 'Fire safety fixture compilation failed' }
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Fire safety fixture failed' }
} finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-fire-safety-*') { throw 'Unsafe cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
