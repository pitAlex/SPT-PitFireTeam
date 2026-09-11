param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [Parameter(Mandatory)][string]$GameRoot,
    [string]$SainSourcesRoot = 'F:\Projects\SPT-Tarkov\SPT-4.1.3',
    [string]$EftSourcesRoot = 'F:\Projects\SPT-Tarkov\SPT-4.1.3\CLIENT-4.1.3\Assembly-CSharp'
)
$ErrorActionPreference = 'Stop'
# Check the live optional dependency without loading Unity into the test process.
[Reflection.Assembly]::LoadFrom((Join-Path $RepositoryRoot 'client/libs/Mono.Cecil.dll')) | Out-Null
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
foreach ($directory in @('BepInEx/plugins/SAIN','BepInEx/core','EscapeFromTarkov_Data/Managed')) {
    $resolver.AddSearchDirectory((Join-Path $GameRoot $directory))
}
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$module = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'BepInEx/plugins/SAIN/SAIN.dll'), $parameters)
try {
    $enable = $module.Types | Where-Object FullName -EQ 'SAIN.SAINEnableClass'
    $lookup = @($enable.Methods | Where-Object { $_.Name -eq 'GetSAIN' -and $_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and $_.Parameters.Count -eq 2 -and $_.Parameters[0].ParameterType.FullName -eq 'System.String' -and $_.Parameters[1].ParameterType.FullName -eq 'SAIN.Components.BotComponent&' })
    if ($lookup.Count -ne 1) { throw 'Unsupported installed SAIN GetSAIN signature' }
    $reaction = $module.Types | Where-Object FullName -EQ 'SAIN.SAINComponent.Classes.WeaponFunction.GrenadeReactionClass'
    $notification = @($reaction.Methods | Where-Object { $_.Name -eq 'EnemyGrenadeThrown' -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Void' -and ($_.Parameters.ParameterType.FullName -join ',') -eq 'EFT.Grenade,UnityEngine.Vector3,System.String' })
    if ($notification.Count -ne 1) { throw 'Unsupported installed SAIN grenade notification signature' }
    $ownerType = $reaction
    $ownerGetter = $null
    while ($ownerType -and !$ownerGetter) {
        $ownerGetter = $ownerType.Methods | Where-Object { $_.Name -eq 'get_BotOwner' -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'EFT.BotOwner' }
        if (!$ownerGetter) { $ownerType = if ($ownerType.BaseType) { $ownerType.BaseType.Resolve() } else { $null } }
    }
    if (!$ownerGetter) { throw 'Installed SAIN grenade owner getter not found' }
    Write-Output ('Installed grenade API verified: ' + $module.Assembly.Name.FullName)
}
finally { $module.Dispose(); $resolver.Dispose() }

# Verify the live activation boundary instead of assuming the source checkout is current.
$gameModule = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $tripwire = $gameModule.Types | Where-Object FullName -EQ 'EFT.SynchronizableObjects.TripwireSynchronizableObject'
    foreach ($field in @(@('_grenadeInWorld','EFT.Grenade'),@('_soundController','EFT.Tripwire.ITripwireSoundController'))) {
        if (@($tripwire.Fields | Where-Object { $_.Name -eq $field[0] -and $_.FieldType.FullName -eq $field[1] }).Count -ne 1) { throw "Unsupported tripwire field $($field[0])" }
    }
    $activate = @($tripwire.Methods | Where-Object { $_.Name -eq 'ActivateGrenade' -and $_.Parameters.Count -eq 0 })
    if ($activate.Count -ne 1) { throw 'Unsupported tripwire activation signature' }
    $calls = @($activate[0].Body.Instructions | Where-Object { $_.OpCode.Name -in @('call','callvirt') } | ForEach-Object { $_.Operand.Name })
    $lastIndex = -1
    foreach ($name in @('Create','ExternalStartTimer','PlayPinSound','ThrowGrenade','set_TripwireState')) {
        $index = [array]::IndexOf($calls,$name)
        if ($index -le $lastIndex) { throw "Live tripwire activation order changed at $name" }
        $lastIndex = $index
    }
    foreach ($api in @(
        @('EFT.SynchronizableObjects.BaseTripwire','CollisionEnter','UnityEngine.Collider'),
        @('EFT.Tripwire.TripwireSoundController','PlayPinSound','UnityEngine.Vector3'),
        @('BotBewareGrenade','AddGrenadeDanger','UnityEngine.Vector3,EFT.Grenade')
    )) {
        $type = $gameModule.Types | Where-Object FullName -EQ $api[0]
        if (@($type.Methods | Where-Object { $_.Name -eq $api[1] -and ($_.Parameters.ParameterType.FullName -join ',') -eq $api[2] -and !$_.IsStatic }).Count -ne 1) { throw "Unsupported tripwire API $($api[0]).$($api[1])" }
    }
    $baseline = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $RepositoryRoot 'client/libs4.1/Assembly-CSharp.dll'))
    try {
        foreach ($contract in @(
            @('AvoidDangerLayer','ShallUseNow'),@('AvoidDangerLayer','GetDecision'),
            @('AvoidDangerLayer','EndHoldPosition'),@('AvoidDangerLayer','EndRunAwayGrenade'),
            @('GrenadeDangerPoint','ShallRunAway')
        )) {
            $liveType = $gameModule.Types | Where-Object FullName -EQ $contract[0]
            $baseType = $baseline.Types | Where-Object FullName -EQ $contract[0]
            $liveMethod = $liveType.Methods | Where-Object Name -EQ $contract[1]
            $baseMethod = $baseType.Methods | Where-Object Name -EQ $contract[1]
            $liveIl = ($liveMethod.Body.Instructions | ForEach-Object { $_.ToString() }) -join "`n"
            $baseIl = ($baseMethod.Body.Instructions | ForEach-Object { $_.ToString() }) -join "`n"
            if (!$liveIl -or $liveIl -cne $baseIl) { throw "Native avoidance behavior differs from tested baseline: $($contract -join '.')" }
        }
        Write-Output 'Installed avoidance decisions match the source baseline.'
    }
    finally { $baseline.Dispose() }
    Write-Output 'Installed tripwire fields, patch signatures, and fuse/pin/dispatch ordering verified.'
}
finally { $gameModule.Dispose() }

function Get-NativeMethod([string]$RelativePath,[string]$Name) {
    $source = Get-Content -Raw (Join-Path $EftSourcesRoot $RelativePath)
    $pattern = '(?ms)^(\t+)public (?:override )?[^\r\n(]+ ' + [regex]::Escape($Name) + '\([^\r\n]*\).*?^\1\}'
    $matched = [regex]::Matches($source,$pattern)
    if ($matched.Count -ne 1) { throw "Expected one native method $Name" }
    return $matched[0].Value
}

# Execute the production patch against both published source versions' identical dispatch.
$prefixPattern = '(?ms)^        public static bool PatchPrefix\(BotsController __instance, Grenade grenade, Vector3 position, Vector3 force, float mass\).*?^        \}'
$prefixes = foreach ($version in @('4.5.0','4.5.1')) {
    $source = Get-Content -Raw (Join-Path $SainSourcesRoot "SAIN-$version/SAIN/Patches/GenericPatches.cs")
    $matched = [regex]::Matches($source,$prefixPattern)
    if ($matched.Count -ne 1) { throw "Expected one SAIN $version grenade prefix" }
    $matched[0].Value.Replace("`r`n","`n")
}
if ($prefixes[0] -cne $prefixes[1]) { throw 'SAIN dispatch versions differ; exercise each separately.' }
$production = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/FollowerSainGrenadeAwarenessPatch.cs')
$lookup = 'Type.GetType("SAIN.SAINEnableClass, SAIN")'
if (!$production.Contains($lookup)) { throw 'Review production assembly lookup changes before updating the harness.' }
# All stand-ins live in the test EXE; retain the rest of production Apply unchanged.
$production = $production.Replace($lookup,'typeof(SAIN.SAINEnableClass)')
$production = [regex]::Replace($production,'(?m)^using [^\r\n]+;\r?\n','')
$harness = Get-Content -Raw (Join-Path $PSScriptRoot 'GrenadeAwarenessHarness.cs')
$tripwireHarness = Get-Content -Raw (Join-Path $PSScriptRoot 'TripwireAwarenessHarness.cs')
$tripwireProduction = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/FollowerTripwireAwarenessPatch.cs')
$tripwireProduction = [regex]::Replace($tripwireProduction,'(?m)^using [^\r\n]+;\r?\n','')
$nativeDanger = Get-Content -Raw (Join-Path $EftSourcesRoot 'GrenadeDangerPoint.cs')
$nativeDanger = [regex]::Replace($nativeDanger,'(?m)^using [^\r\n]+;\r?\n','')
$harness = $harness.Replace('__NATIVE_HEARING__',(Get-NativeMethod 'BotHearingSensor.cs' 'IsSoundHeard'))
$bewareMethods = @('IgnoreGrenade','ShallRunAway','SetGrenadeDangerPoint') | ForEach-Object { Get-NativeMethod 'BotBewareGrenade.cs' $_ }
$harness = $harness.Replace('__NATIVE_BEWARE__',($bewareMethods -join "`n"))
$tripwireHarness = $tripwireHarness.Replace('__NATIVE_COLLISION__',(Get-NativeMethod 'EFT/SynchronizableObjects/BaseTripwire.cs' 'CollisionEnter'))
$avoidanceHarness = Get-Content -Raw (Join-Path $PSScriptRoot 'TripwireAvoidanceHarness.cs')
$avoidanceProduction = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerTripwireLayer.cs')
$avoidanceProduction = [regex]::Replace($avoidanceProduction,'(?m)^using [^\r\n]+;\r?\n','')
$avoidanceMethods = @('ShallUseNow','GetDecision','EndHoldPosition','EndRunAwayGrenade') | ForEach-Object { '[MethodImpl(MethodImplOptions.NoInlining)]' + "`n" + (Get-NativeMethod 'AvoidDangerLayer.cs' $_) }
$avoidanceHarness = $avoidanceHarness.Replace('__NATIVE_AVOIDANCE__',($avoidanceMethods -join "`n"))
function Get-ProjectMethod([string]$RelativePath,[string]$Name) {
    $source = Get-Content -Raw (Join-Path $RepositoryRoot $RelativePath)
    $pattern = '(?ms)^        (?:public|private|internal) (?:static )?[^\r\n(]+ ' + [regex]::Escape($Name) + '\([^)]*\)\s*\{.*?^        \}'
    $matched = [regex]::Matches($source,$pattern)
    if ($matched.Count -ne 1) { throw "Expected one project method $RelativePath $Name" }
    return $matched[0].Value
}
$factory = Get-ProjectMethod 'client/BigBrain/FollowerCombatLayer.cs' 'CreateBigBrainAction'
$avoidanceHarness = $avoidanceHarness.Replace('__ACTION_FACTORY__',$factory)
$actionNames = [regex]::Matches($factory,'typeof\((\w+)\)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$actionStubs = $actionNames | Where-Object { $_ -ne 'CombatDogFightAction' } | ForEach-Object { "public class $_ { }" }
$avoidanceHarness = $avoidanceHarness.Replace('__ACTION_STUBS__',($actionStubs -join "`n"))
$enumNames = @('flashed','turnAwayLight','lay','runAwayArtillery','runAwayGrenade','deactivateMine') + @([regex]::Matches($factory,'BotLogicDecision\.(\w+)') | ForEach-Object { $_.Groups[1].Value })
$avoidanceHarness = $avoidanceHarness.Replace('__DECISIONS__',(($enumNames | Sort-Object -Unique) -join ','))
$actionDataSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/Actions/FollowerCombatActionBase.cs')
$actionData = [regex]::Match($actionDataSource,'(?ms)^    internal sealed class FollowerCombatActionData.*?^    \}').Value
if (!$actionData) { throw 'Missing shared action payload' }
$avoidanceHarness = $avoidanceHarness.Replace('__ACTION_DATA__',$actionData)
$avoidanceHarness = $avoidanceHarness.Replace('__DOGFIGHT_DATA__',(Get-ProjectMethod 'client/BigBrain/Actions/CombatDogFightAction.cs' 'ResolveCurrentActionData'))
$avoidanceHarness = $avoidanceHarness.Replace('__DOGFIGHT_MOVEMENT__',(Get-ProjectMethod 'client/BigBrain/Actions/CombatDogFightAction.cs' 'TryGoToDogFightPoint'))
$coverHelpers = @('IsPathTooCloseToEnemy','DistanceXZSqr','DistancePointToSegmentXZSqr') | ForEach-Object { Get-ProjectMethod 'client/Utils/Covers.cs' $_ }
$avoidanceHarness = $avoidanceHarness.Replace('__COVER_HELPERS__',($coverHelpers -join "`n"))
$medicalHelpers = @('CancelActiveMedical','IsUsingMedical') | ForEach-Object { Get-ProjectMethod 'client/Utils/FollowerMedical.cs' $_ }
$avoidanceHarness = $avoidanceHarness.Replace('__MEDICAL_HELPERS__',($medicalHelpers -join "`n"))
$combatSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatCommon.cs')
$enemyHelper = [regex]::Match($combatSource,'(?ms)^        internal static bool HasActiveCombatEnemy\(BotOwner botOwner, EnemyInfo\? goalEnemy\).*?^        \}').Value
$avoidanceHarness = $avoidanceHarness.Replace('__COMBAT_HELPERS__',$enemyHelper + "`n" + (Get-ProjectMethod 'client/BigBrain/FollowerCombatCommon.cs' 'WasHitRecently'))
$bigBrainSource = Get-Content -Raw (Join-Path $SainSourcesRoot 'SPT-BigBrain-1.5.0/Brains/CustomLayer.cs')
$bigBrainSource = [regex]::Replace($bigBrainSource,'(?m)^using [^\r\n]+;\r?\n','')
$avoidanceHarness += "`n#nullable disable`n" + $bigBrainSource + "`n#nullable enable`n"
$harness = '#nullable enable' + "`nusing System.Linq.Expressions;`nusing System.Text;`nusing UnityEngine.AI;`nusing DrakiaXYZ.BigBrain.Brains;`nusing pitTeam.BigBrain.Actions;`nusing pitTeam.Utils;`nusing pitTeam.Patches;`nusing System.Reflection;`nusing Comfort.Common;`nusing EFT.SynchronizableObjects;`nusing EFT.Tripwire;`n" + $harness.Replace('__SAIN_PREFIX__',$prefixes[0]) + "`n" + $production + "`n" + $tripwireProduction + "`n" + $tripwireHarness + "`n" + $avoidanceProduction + "`n" + $avoidanceHarness + "`n#nullable disable`n" + $nativeDanger
$harmonyPath = Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$sdk = (dotnet --list-sdks | Select-Object -Last 1)
if ($sdk -notmatch '^(\S+) \[(.+)\]$') { throw 'Cannot locate the .NET SDK compiler.' }
$compiler = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-grenade-compat-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $sourcePath = Join-Path $testRoot 'GrenadeCompatibility.cs'
    $exePath = Join-Path $testRoot 'GrenadeCompatibility.exe'
    [IO.File]::WriteAllText($sourcePath,$harness)
    Copy-Item -LiteralPath $harmonyPath -Destination $testRoot
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object { $_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll' } | Copy-Item -Destination $testRoot
    $arguments = @($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:enable','/nostdlib+',"/out:$exePath","/reference:$harmonyPath")
    foreach ($reference in @('mscorlib.dll','System.dll','System.Core.dll')) { $arguments += '/reference:' + (Join-Path $frameworkRoot $reference) }
    $arguments += $sourcePath
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Grenade compatibility harness compilation failed.' }
    & $exePath
    if ($LASTEXITCODE -ne 0) { throw 'Grenade compatibility checks failed.' }
    Write-Output 'SAIN 4.5.0/4.5.1 dispatch checked. Unity avoidance, cover, and command resumption require a raid test.'
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    $temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$resolvedRoot.StartsWith($temporaryParent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedRoot) -notlike 'pitFireTeam-grenade-compat-*') { throw 'Refusing to clean an unexpected test directory.' }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
