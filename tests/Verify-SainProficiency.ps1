param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),[Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference='Stop'
function Block([string]$source,[string]$signature){
    $match=[regex]::Match($source,$signature);if(!$match.Success){throw "Missing source boundary: $signature"}
    $start=$source.IndexOf('{',$match.Index);$depth=0
    for($i=$start;$i -lt $source.Length;$i++){
        if($source[$i] -eq '{'){$depth++};if($source[$i] -eq '}'){$depth--;if($depth -eq 0){return $source.Substring($match.Index,$i-$match.Index+1)}}
    };throw 'Unbalanced production source'
}
$model=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FollowerProficiencyValues.cs')
$modifiers=Block $model 'public sealed class FollowerProficiencyModifierValues'
$core=Block $model 'public sealed class FollowerSainCoreValues'
$adapter=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FollowerSainProficiency.cs')
$recoil=Block $adapter 'private static void ApplyFollowerAccuracyToCalculatedRecoil\('
$resolve=Block $adapter 'private static void ResolveRecoilRuntimeFields\('
$fields=[regex]::Matches($adapter,'(?m)^        private static (?:PropertyInfo\?|FieldInfo\?|bool) _(?:recoilBotOwnerProperty|recoilBotOwnerField|currentRecoilHorizAngleField|currentRecoilVertAngleField|recoilRuntimeFieldsResolved|accuracyPatchFailureReported);').Value -join "`n"
$generated="using System;using System.Collections.Generic;using System.Reflection;using EFT;using HarmonyLib;using UnityEngine;using Newtonsoft.Json;using pitTeam.Modules;namespace pitTeam.Modules { $modifiers $core } public static class RecoilHooks { $fields $recoil $resolve }"
$aimMethods=foreach($name in @('BeginDefaultFollowerAim','EndDefaultFollowerAim','UseDefaultFollowerFasterCqb','UseDefaultFollowerAdsAimTime','UseDefaultFollowerAimClamp','TryGetActiveAimValues')) { Block $adapter ('private static [^\r\n]+ '+$name+'\(') }
$generated+=' public static partial class HotAimHooks {'+($aimMethods -join "`n")+' }'

if(!$adapter.Contains('state.EftCore.Apply(state.Bot.Settings.Current, sain.Core)') -or !$adapter.Contains('state.EftCore.Restore()')){throw 'Core projection is not wired into apply/restore'}
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx/plugins/SAIN/SAIN.dll'))
try {
    $recoilType=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.WeaponFunction.Recoil'
    foreach($name in @('_currentRecoilHorizAngle','_currentRecoilVertAngle')){if(!($recoilType.Fields | Where-Object {$_.Name -eq $name -and $_.FieldType.FullName -eq 'System.Single'})){throw "Missing installed recoil field $name"}}
    $aimType=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.Patches.Shoot.Aim.AimTimePatch'
    foreach($name in @('CalculateAim','CalcFasterCQB','CalcADSModifier','ClampAimTime')){if(!($aimType.Methods|Where-Object Name -eq $name)){throw "Missing installed aim method $name"}}
    $arguments=@{CalculateAim=@('SAIN.Components.BotComponent','System.Single');CalcFasterCQB=@('System.Single','System.Single');CalcADSModifier=@('System.Boolean','System.Single');ClampAimTime=@('System.Single')}
    foreach($name in $arguments.Keys){
        $method=@($aimType.Methods|Where-Object Name -eq $name)
        if($method.Count -ne 1){throw "Ambiguous installed aim method $name"}
        for($i=0;$i -lt $arguments[$name].Count;$i++){if($method[0].Parameters[$i].ParameterType.FullName -ne $arguments[$name][$i]){throw "Changed aim argument $name $i"}}
    }
    'Installed SAIN aim and recoil boundaries verified.'
} finally {$assembly.Dispose()}
$sdk=dotnet --list-sdks|Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-sain-proficiency-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary|Out-Null
try {
    $generatedPath=Join-Path $temporary 'Production.cs';[IO.File]::WriteAllText($generatedPath,$generated)
    $sources=@($generatedPath,(Join-Path $PSScriptRoot 'SainProficiencyFixture.cs'),(Join-Path $PSScriptRoot 'SainHotAimFixture.cs'),(Join-Path $RepositoryRoot 'client/Modules/SainBotOwnerAccessor.cs'),(Join-Path $RepositoryRoot 'client/Modules/FollowerSainEftCoreProjection.cs'),(Join-Path $RepositoryRoot 'client/Patches/FollowerAimTimeProficiencyPatch.cs'))
    $harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll';Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll'|Where-Object {$_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll'}|Copy-Item -Destination $temporary
    $exe=Join-Path $temporary 'Proficiency.exe'
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/nostdlib+','/nowarn:8632',"/out:$exe","/reference:$harmony")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    & dotnet @arguments @sources;if($LASTEXITCODE -ne 0){throw 'Proficiency harness compilation failed'}
    & $exe;if($LASTEXITCODE -ne 0){throw 'Proficiency harness failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary);$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-sain-proficiency-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
