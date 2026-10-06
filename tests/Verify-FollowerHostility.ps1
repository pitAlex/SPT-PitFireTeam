param([Parameter(Mandatory)][string]$RepositoryRoot,[Parameter(Mandatory)][string]$GameRoot)
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $group=$assembly.MainModule.GetType('BotsGroup')
    foreach($name in @('AddEnemy','IsEnemy','IsPlayerEnemy','IsAlly')) {
        if(!($group.Methods | Where-Object {$_.Name -eq $name -and $_.IsPublic -and $_.ReturnType.FullName -eq 'System.Boolean'})){throw "Missing group method: $name"}
    }
    $add=@($group.Methods | Where-Object {$_.Name -eq 'AddEnemy' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EFT.IPlayer|EBotEnemyCause'})
    if($add.Count -ne 1 -or ($add[0].Parameters.Name -join '|') -ne 'person|cause'){throw 'AddEnemy hook boundary changed'}
    if(!($group.Methods | Where-Object {$_.Name -eq 'RemoveEnemy' -and $_.IsPublic -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EFT.IPlayer|EBotEnemyCause'}) -or
       !($group.Events | Where-Object {$_.Name -eq 'OnEnemyRemove' -and $_.EventType.FullName -eq 'System.Action`1<EFT.IPlayer>'})) {throw 'Native enemy removal notification boundary changed'}
    if(!($group.Properties | Where-Object {$_.Name -eq 'TargetMembersCount' -and $_.PropertyType.FullName -eq 'System.Int32'}) -or
       !($group.Events | Where-Object {$_.Name -eq 'OnMemberAdd' -and $_.EventType.FullName -eq 'System.Action`1<EFT.BotOwner>'})) {throw 'Group-size lifecycle boundary changed'}
    $owner=$assembly.MainModule.GetType('EFT.BotOwner')
    $spawnProperty=$owner.Properties | Where-Object Name -eq 'SpawnProfileData'
    $spawnData=$assembly.MainModule.GetType($spawnProperty.PropertyType.FullName)
    $spawnParamsProperty=$spawnData.Properties | Where-Object Name -eq 'SpawnParams'
    $spawnParams=$assembly.MainModule.GetType($spawnParamsProperty.PropertyType.FullName)
    $groupProperty=$spawnParams.Properties | Where-Object Name -eq 'ShallBeGroup'
    $originalGroup=$assembly.MainModule.GetType($groupProperty.PropertyType.FullName)
    if(!($originalGroup.Fields | Where-Object {$_.Name -eq 'Group' -and $_.FieldType.FullName -eq 'System.Boolean'}) -or
       !($originalGroup.Properties | Where-Object {$_.Name -eq 'StartCount' -and $_.PropertyType.FullName -eq 'System.Int32'})) {throw 'Original spawn-group boundary changed'}
    $cause=$assembly.MainModule.GetType('EBotEnemyCause')
    $values=$cause.Fields | Where-Object HasConstant | ForEach-Object {$_.Name+' = '+$_.Constant}
    $enum='namespace EFT { public enum EBotEnemyCause { '+($values -join ',')+' } }'
} finally {$assembly.Dispose()}
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-hostility-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/BotGroupPatch.cs')
    $start=$source.IndexOf('    internal class BotGroupAddEnemyPatch')
    $end=$source.IndexOf('    internal class BotGroupReportEnemyPatch')
    if($start -lt 0 -or $end -le $start){throw 'Group patch extraction boundary changed'}
    $prefix=$source.Substring(0,$source.IndexOf('    internal class BotGroupUsecEnemyPatch'))
    Set-Content (Join-Path $temporary 'GroupPatch.cs') ($prefix+$source.Substring($start,$end-$start)+'}')
    $enemy=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/Enemy.cs')
    $start=$enemy.IndexOf('        public static bool RequiresAcquisitionAwarenessGate(')
    $end=$enemy.IndexOf('        public static void RepairPersonalMemory(', $start)
    if($start -lt 0 -or $end -le $start){throw 'Awareness method boundary changed'}
    $fixture=(Get-Content -Raw (Join-Path $RepositoryRoot 'tests/FollowerHostilityFixture.cs')).Replace('__AWARENESS_METHOD__',$enemy.Substring($start,$end-$start))
    $acquire=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FollowerCalcGoalEnemyAcquire.cs')
    $start=$acquire.IndexOf('        public static bool ShouldBlockCandidateForMissingHostileIntent(')
    $end=$acquire.IndexOf('        private static bool IsProcessedFriendlySeen(', $start)
    if($start -lt 0 -or $end -le $start){throw 'Acquisition extraction boundary changed'}
    $fixture=$fixture.Replace('__ACQUISITION_METHOD__',$acquire.Substring($start,$end-$start))
    $faction=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FactionHostility.cs')
    $start=$faction.IndexOf('        internal static void EnsureNeutral(')
    $end=$faction.IndexOf('        private static bool EnsureEnemy(', $start)
    if($start -lt 0 -or $end -le $start){throw 'Neutral relationship extraction boundary changed'}
    $fixture=$fixture.Replace('__NEUTRAL_METHOD__',$faction.Substring($start,$end-$start))
    $bossSource=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/AIBossPlayer.cs')
    $hit=[regex]::Match($bossSource,'(?ms)^        public void OnHit\(.*?^        \}').Value
    if(!$hit){throw 'Human damage entry point boundary changed'}
    $fixture=$fixture.Replace('__BOSS_HIT_METHOD__',$hit)
    Set-Content (Join-Path $temporary 'Fixture.cs') ($fixture+$enum)
    $exe=Join-Path $temporary 'Hostility.exe'
    $harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll'
    Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object Name -ne '0Harmony.dll' | Copy-Item -Destination $temporary
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:annotations','/nostdlib+',"/out:$exe","/reference:$harmony")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    $arguments+=@((Join-Path $temporary 'GroupPatch.cs'),(Join-Path $temporary 'Fixture.cs'),(Join-Path $RepositoryRoot 'client/Modules/FollowerGroupHostility.cs'),(Join-Path $RepositoryRoot 'client/Modules/AllegiancePmcFriendship.cs'))
    & dotnet @arguments
    if($LASTEXITCODE -ne 0){throw 'Hostility fixture compilation failed'}
    & $exe
    if($LASTEXITCODE -ne 0){throw 'Hostility regression failed'}
} finally {
    $resolved=[IO.Path]::GetFullPath($temporary)
    $parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'pitFireTeam-hostility-*'){throw 'Unsafe cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
