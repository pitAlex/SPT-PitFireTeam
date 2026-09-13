param([Parameter(Mandatory)][string]$RepositoryRoot,[Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx/plugins/SAIN/SAIN.dll'))
try {
    if($assembly.Name.Version.ToString() -ne '4.5.1.0'){throw 'SAIN version must be 4.5.1.0'}
    $layerSource=Get-Content -Raw (Join-Path $RepositoryRoot 'addon/SAINActionTypes.cs')
    $actions=[regex]::Matches($layerSource,'"((?:Solo|Squad)\.[^"]+Action)"') | ForEach-Object {$_.Groups[1].Value}
    foreach($name in $actions){
        $type=$assembly.MainModule.Types | Where-Object FullName -eq ('SAIN.Layers.Combat.'+$name)
        $ctor=@($type.Methods | Where-Object { $_.Name -eq '.ctor' -and $_.IsPublic -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'EFT.BotOwner' })
        if($ctor.Count -ne 1){throw "Missing native action constructor: $name"}
    }
    $provider=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.Decision.SquadDecisionClass'
    $getDecision=@($provider.Methods | Where-Object {$_.Name -eq 'GetDecision' -and $_.ReturnType.FullName -eq 'System.Boolean' -and $_.Parameters.Count -eq 2})
    if($getDecision.Count -ne 1 -or $getDecision[0].Parameters[0].ParameterType.FullName -ne 'SAIN.Preset.Shared.Enums.ESquadDecision&' -or $getDecision[0].Parameters[1].ParameterType.FullName -ne 'SAIN.SAINComponent.Classes.EnemyClasses.Enemy'){throw 'Squad provider signature changed'}
    $info=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.Info.SAINBotInfoClass'
    foreach($name in @('get_Personality','get_PersonalitySettingsClass','get_Difficulty','get_ForgetEnemyTime','set_ForgetEnemyTime','SetPersonality','CalcTimeBeforeSearch','CalcHoldGroundDelay')){
        if(!($info.Methods | Where-Object Name -eq $name)){throw "Missing personality member: $name"}
    }
    $friendly=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.SAINFriendlyFireClass'
    if(@($friendly.Methods | Where-Object {$_.Name -eq 'CheckFriendlyFireStatus' -and $_.IsStatic -and $_.Parameters.Count -eq 4}).Count -ne 2){throw 'Friendly-fire overloads changed'}
    $layer=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.Layers.SAINLayer'
    if(!$layer.IsPublic -or !$layer.IsAbstract){throw 'SAINLayer extension boundary changed'}
    Write-Output "Installed SAIN 4.5.1 validated: $($actions.Count) action constructors, personality API, friendly-fire overloads, public layer base."
} finally {$assembly.Dispose()}

$fixture=Get-Content -Raw (Join-Path $PSScriptRoot 'SainAddonCombatFixture.cs')
$plugin=Get-Content -Raw (Join-Path $RepositoryRoot 'client/friendlyPlugin.cs')
$gates=[regex]::Matches($plugin,'(?ms)^        public static bool (?:IsSainManTacticAvailable|IsSainFollowerCombatAvailable|UseSainFollowerCombat|ShouldDisableSainForFollower)\b[^;]+;')
if($gates.Count -ne 4){throw 'Combat ownership declarations changed'}
$fixture=$fixture.Replace('__COMBAT_GATES__',($gates.Value -join [Environment]::NewLine))
$patch=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/SAINPatches.cs')
$nativeGate=[regex]::Match($patch,'(?ms)^        private static bool DisableSainLayerForFollowersWithoutAddon\(.*?^        \}')
if(!$nativeGate.Success){throw 'Native layer ownership gate changed'}
$fixture+=@"
public static class NativeOwnershipGate {
    private static Type avoidThreatLayerType=typeof(SAIN.Layers.SAINAvoidThreatLayer);
    private static Type flashBangedLayerType=typeof(SAIN.Layers.Flashed.SAINFlashedLayer);
    public static bool Allow(object instance){bool result=true;return DisableSainLayerForFollowersWithoutAddon(instance,ref result);}
$($nativeGate.Value)
}
"@
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-sain-combat-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $sources=@('addon/SAINActionTypes.cs','addon/SAINFollowerSoloCombatLayer.cs','addon/SAINFollowerSquadCombatLayer.cs','addon/SAINFollowerSquadDecision.cs','addon/SAINFollowerSquadRegroupAction.cs','addon/SAINFollowerFollowSearchPartyAction.cs','client/Modules/SainSquadDecisionBridge.cs','tests/SainSquadFixture.cs','addon/SAINFollowerRuntime.cs','client/Modules/SainAddonBridge.cs','client/Modules/SainManPersonality.cs','client/Patches/FollowerSainFriendlyFirePatch.cs')
    $paths=@()
    foreach($sourcePath in $sources){
        $source=Get-Content -Raw (Join-Path $RepositoryRoot $sourcePath)
        # Fixture types live alongside production under test; runtime uses the separate SAIN assembly.
        $source=[regex]::Replace($source,'Type.GetType\("([^"]+), SAIN"(?:, true)?\)','CombatChecks.ResolveType("$1")')
        $path=Join-Path $temporary ([IO.Path]::GetFileName($sourcePath))
        [IO.File]::WriteAllText($path,$source);$paths+=$path
    }
    $path=Join-Path $temporary 'Fixture.cs';[IO.File]::WriteAllText($path,$fixture);$paths+=$path
    $harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll';Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object {$_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll'} | Copy-Item -Destination $temporary
    $exe=Join-Path $temporary 'Combat.exe'
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nostdlib+','/nowarn:8632',"/out:$exe","/reference:$harmony")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    & dotnet @arguments @paths
    if($LASTEXITCODE -ne 0){throw 'Addon combat harness compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Addon combat harness failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-sain-combat-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}