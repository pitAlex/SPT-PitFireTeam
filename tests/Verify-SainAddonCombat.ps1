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
    $shoot=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.SAINShootData'
    if(@($shoot.Methods | Where-Object {$_.Name -eq 'AimAndShootAtEnemy' -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'SAIN.SAINComponent.Classes.EnemyClasses.Enemy|SAIN.Components.BotComponent'}).Count -ne 1){throw 'Native exact-target aiming boundary changed'}
    $suppress=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.WeaponFunction.SAINBotSuppressClass'
    if(@($suppress.Methods | Where-Object {$_.Name -eq 'SuppressPosition' -and $_.IsPublic -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'UnityEngine.Vector3|SAIN.SAINComponent.Classes.EnemyClasses.Enemy'}).Count -ne 1){throw 'Native suppression execution boundary changed'}
    $manual=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.WeaponFunction.ManualShootClass'
    if(@($manual.Methods | Where-Object {$_.Name -eq 'TryShoot' -and $_.IsPublic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'SAIN.SAINComponent.Classes.EnemyClasses.Enemy|UnityEngine.Vector3|System.Boolean|SAIN.Models.Enums.EShootReason'}).Count -ne 1){throw 'Native manual firing API changed'}
    if(@($manual.Methods | Where-Object {$_.Name -eq 'Reset' -and $_.IsPublic -and $_.ReturnType.FullName -eq 'System.Void' -and $_.Parameters.Count -eq 0}).Count -ne 1){throw 'Native manual firing reset API changed'}
    if(@($manual.Methods | Where-Object {$_.Name -eq 'CanShoot' -and $_.IsPublic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'System.Boolean'}).Count -ne 1){throw 'Native manual weapon-readiness API changed'}
    $finder=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.Decision.FiringPositionFinder'
    if(!$finder.IsPublic -or @($finder.Methods | Where-Object {$_.Name -eq 'Find' -and $_.IsPublic -and $_.ReturnType.FullName -eq 'System.Boolean' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'SAIN.SAINComponent.Classes.EnemyClasses.Enemy'}).Count -ne 1){throw 'Native firing-position finder API changed'}
    $cover=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.SAINCoverClass'
    $selection=@($cover.Methods | Where-Object {$_.Name -eq 'FindCoverPoint' -and !$_.IsStatic -and $_.Parameters.Count -eq 0 -and $_.ReturnType.FullName -eq 'SAIN.SAINComponent.SubComponents.CoverFinder.CoverPoint'})
    if($selection.Count -ne 1 -or !($cover.Fields | Where-Object {$_.Name -eq '_shallSprint' -and $_.FieldType.FullName -eq 'System.Boolean'})){throw 'Cover selection boundary changed'}
    $self=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.Decision.SelfActionDecisionClass'
    if(@($self.Methods | Where-Object {$_.Name -eq 'ShallFirstAidCheckEnemy' -and $_.ReturnType.FullName -eq 'System.Boolean' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'SAIN.SAINComponent.Classes.EnemyClasses.Enemy'}).Count -ne 1){throw 'First-aid safety boundary changed'}
    $surgery=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.BotSurgery'
    foreach($name in @('CheckEnemies','CheckAreaClearForSurgery')){if(@($surgery.Methods | Where-Object {$_.Name -eq $name -and $_.ReturnType.FullName -eq 'System.Boolean' -and $_.Parameters.Count -eq 0}).Count -ne 1){throw "Surgery boundary changed: $name"}}
    if(!($surgery.Properties | Where-Object {$_.Name -eq 'AreaClearForSurgery' -and $_.PropertyType.FullName -eq 'System.Boolean' -and $_.SetMethod})){throw 'Surgery clearance setter missing'}
    $dogfight=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.Mover.DogFight'
    if(@($dogfight.Methods | Where-Object {$_.Name -eq 'DogFightMove' -and $_.ReturnType.FullName -eq 'System.Void' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'System.Boolean|SAIN.SAINComponent.Classes.EnemyClasses.Enemy'}).Count -ne 1){throw 'No-cover movement boundary changed'}
    $enemyController=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.EnemyClasses.SAINEnemyController'
    if(@($enemyController.Methods | Where-Object {$_.Name -eq 'SelectEnemy' -and !$_.IsStatic -and $_.Parameters.Count -eq 0 -and $_.ReturnType.FullName -eq 'SAIN.SAINComponent.Classes.EnemyClasses.Enemy'}).Count -ne 1){throw 'Native enemy preference boundary changed'}
    $provider=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.Decision.SquadDecisionClass'
    $getDecision=@($provider.Methods | Where-Object {$_.Name -eq 'GetDecision' -and $_.ReturnType.FullName -eq 'System.Boolean' -and $_.Parameters.Count -eq 2})
    if($getDecision.Count -ne 1 -or $getDecision[0].Parameters[0].ParameterType.FullName -ne 'SAIN.Preset.Shared.Enums.ESquadDecision&' -or $getDecision[0].Parameters[1].ParameterType.FullName -ne 'SAIN.SAINComponent.Classes.EnemyClasses.Enemy'){throw 'Squad provider signature changed'}
    $manager=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.Decision.BotDecisionManager'
    $publish=@($manager.Methods | Where-Object {$_.Name -eq 'SetDecisions' -and $_.ReturnType.FullName -eq 'System.Void' -and !$_.IsStatic -and $_.Parameters.Count -eq 4})
    $expected=@('SAIN.Preset.Shared.Enums.ECombatDecision','SAIN.Preset.Shared.Enums.ESquadDecision','SAIN.Preset.Shared.Enums.ESelfActionType','SAIN.SAINComponent.Classes.EnemyClasses.Enemy')
    if($publish.Count -ne 1){throw 'Decision publisher signature changed'}
    for($i=0;$i -lt 4;$i++){if($publish[0].Parameters[$i].ParameterType.FullName -ne $expected[$i]){throw 'Decision publisher parameter changed'}}
    $info=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.Info.SAINBotInfoClass'
    foreach($name in @('get_Personality','set_Personality','get_PersonalitySettingsClass','set_PersonalitySettingsClass','get_Difficulty','get_ForgetEnemyTime','set_ForgetEnemyTime','SetPersonality','CalcTimeBeforeSearch','CalcHoldGroundDelay')){
        if(!($info.Methods | Where-Object Name -eq $name)){throw "Missing personality member: $name"}
    }
    $searchAction=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.Layers.Combat.Solo.SearchAction'
    foreach($field in @('_sprintEnabled','_sprintTimer')){if(!($searchAction.Fields | Where-Object Name -eq $field)){throw "Missing search cache: $field"}}
    $talk=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.Talk.EnemyTalk'
    if(!($talk.Methods | Where-Object Name -eq 'UpdatePresetSettings')){throw 'Missing personality talk refresh'}
    $friendly=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.SAINFriendlyFireClass'
    if(@($friendly.Methods | Where-Object {$_.Name -eq 'CheckFriendlyFireStatus' -and $_.IsStatic -and $_.Parameters.Count -eq 4}).Count -ne 2){throw 'Friendly-fire overloads changed'}
    $manualShoot=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.SAINComponent.Classes.WeaponFunction.ManualShootClass'
    if(@($manualShoot.Methods | Where-Object {$_.Name -eq 'TryShoot' -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'SAIN.SAINComponent.Classes.EnemyClasses.Enemy|UnityEngine.Vector3|System.Boolean|SAIN.Models.Enums.EShootReason'}).Count -ne 1){throw 'Native regroup suppression trigger boundary changed'}
    $layer=$assembly.MainModule.Types | Where-Object FullName -eq 'SAIN.Layers.SAINLayer'
    if(!$layer.IsPublic -or !$layer.IsAbstract){throw 'SAINLayer extension boundary changed'}
    Write-Output "Installed SAIN 4.5.1 validated: $($actions.Count) action constructors, decision publisher, cover selection, personality API, friendly-fire overloads, public layer base."
} finally {$assembly.Dispose()}

# Reflection-only addon bindings must exist in the installed Core, not just fixture stand-ins.
$coreAssembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameRoot 'BepInEx/plugins/pitFireTeam/pitFireTeam.dll'))
try {
    $common=$coreAssembly.MainModule.Types | Where-Object FullName -eq 'pitTeam.BigBrain.FollowerCombatCommon'
    foreach($name in @('IsSuppressCapableWeapon','IsGrenadeLauncherWeapon')){
        if(@($common.Methods | Where-Object {$_.Name -eq $name -and $_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EFT.InventoryLogic.Weapon'}).Count -ne 1){throw "Core suppression predicate changed: $name"}
    }
    if(@($common.Methods | Where-Object {$_.Name -eq 'IsSoftObstructedSuppressionLane' -and $_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'UnityEngine.Vector3|UnityEngine.Vector3|UnityEngine.LayerMask'}).Count -ne 1){throw 'Core suppression lane boundary changed'}
    if(@($common.Methods | Where-Object {$_.Name -eq 'IsTeamSearchSupportPosition' -and $_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'UnityEngine.Vector3|UnityEngine.Vector3|UnityEngine.Vector3'}).Count -ne 1){throw 'Core push support positioning predicate changed'}
    $contactType=$coreAssembly.MainModule.Types | Where-Object FullName -eq 'pitTeam.Components.pitAIBossPlayer'
    $contact=@($contactType.Methods | Where-Object {$_.Name -eq 'RegisterContactEnemyForFollower' -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Void' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EFT.BotOwner|EFT.Player|System.Boolean|System.Boolean|System.Boolean'})
    if($contact.Count -ne 1 -or $contact[0].Parameters[3].Name -ne 'allowGoalPromotion' -or $contact[0].Parameters[2].Name -ne 'prioritizeAsGoal'){throw 'Core Contact command signature changed'}
    $makeCalls=@($contact[0].Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'pitTeam.Utils.Enemy' -and $_.Operand.Name -eq 'MakeEnemy'})
    if($makeCalls.Count -ne 1 -or ($makeCalls[0].Operand.Parameters.ParameterType.FullName -join '|') -ne 'EFT.BotOwner|EFT.Player|EBotEnemyCause|System.Boolean'){throw 'Core Contact enemy creation boundary changed'}
    $firePolicy=$coreAssembly.MainModule.Types | Where-Object FullName -eq 'pitTeam.BigBrain.FollowerImmediateFirePolicy'
    if(@($firePolicy.Methods | Where-Object {$_.Name -eq 'HasDirectFireLane' -and $_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EFT.BotOwner|UnityEngine.Vector3'}).Count -ne 1){throw 'Core direct-fire lane predicate changed'}
    $targetPolicy=$coreAssembly.MainModule.Types | Where-Object FullName -eq 'pitTeam.BigBrain.FollowerSuppressTargetPolicy'
    if(@($targetPolicy.Methods | Where-Object {$_.Name -eq 'TryGetTarget' -and $_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EnemyInfo|UnityEngine.Vector3&'}).Count -ne 1){throw 'Core suppression report resolver changed'}
    $boss=$coreAssembly.MainModule.Types | Where-Object FullName -eq 'pitTeam.Components.pitAIBossPlayer'
    if(@($boss.Methods | Where-Object {$_.Name -eq 'TryIssueSuppressCommand' -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EFT.BotOwner|pitTeam.Components.BotFollowerPlayer|EFT.Player|System.Boolean|System.Boolean|System.Boolean|System.Boolean'}).Count -ne 1){throw 'Core suppression command boundary changed'}
    $sniper=$coreAssembly.MainModule.Types | Where-Object FullName -eq 'pitTeam.BigBrain.FollowerCombatSniper'
    foreach($name in @('IsWeaponSelectionSettledForAutomaticMarksmanSupportRequest','HasAutomaticCloseCombatWeaponAvailable','IsAutomaticCloseCombatWeaponReady','TryRequestAutomaticSupportForCloseCombat','IsEligibleAutomaticMarksmanSupportSelectedAndReady','TryRequestEligibleAutomaticMarksmanSupport','HasLoadedAutomaticMarksmanSupportWeapon','TrySwitchBackToPrimaryFromAutomaticMarksmanSupport','IsUsingAutomaticMarksmanSupportOverNonAutomaticPrimary','IsTemporaryHoldPositionAggressionActive','HasReportedHealWorkForPush')){
        if(@($common.Methods | Where-Object {$_.Name -eq $name -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and $_.Parameters.Count -eq 0}).Count -ne 1){throw "Core support weapon binding changed: $name"}
    }
    foreach($name in @('ShouldBlockProactiveAutoPushForWeaponThreat','ShouldUseCautiousWeaponThreatStyle')){
        if(@($common.Methods | Where-Object {$_.Name -eq $name -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EnemyInfo'}).Count -ne 1){throw "Core weapon threat binding changed: $name"}
    }
    if(@($common.Methods | Where-Object {$_.Name -eq 'GetAggression01' -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Single' -and $_.Parameters.Count -eq 0}).Count -ne 1){throw 'Core aggression binding changed'}
    if(@($common.Methods | Where-Object {$_.Name -eq 'GetAllowedLowThreatEnemyCount' -and $_.IsStatic -and $_.ReturnType.FullName -eq 'System.Int32' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'System.Single'}).Count -ne 1){throw 'Core Marksman count policy changed'}
    if(@($sniper.Methods | Where-Object {$_.Name -eq 'IsWithinMarksmanAutoSearchDistance' -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EnemyInfo|System.Single'}).Count -ne 1){throw 'Core Marksman range policy changed'}
    if(@($sniper.Methods | Where-Object {$_.Name -eq 'CanUseAutomaticSupportForCloseThreat' -and $_.IsStatic -and $_.ReturnType.FullName -eq 'System.Boolean' -and ($_.Parameters.ParameterType.FullName -join '|') -eq 'EFT.BotOwner|EnemyInfo'}).Count -ne 1){throw 'Core Marksman close-threat policy changed'}
    $phrase=$boss.Methods | Where-Object Name -eq 'ApplySuppressPhrase'
    if(@($phrase.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq 'System.Boolean pitTeam.pitFireTeam::UseSainFollowerCombat(EFT.BotOwner)'}).Count -ne 1){throw 'Core suppression candidate exclusion changed'}
    Write-Output 'Installed Core support weapon, Marksman policy and suppression hook bindings validated.'
    Write-Output 'Installed Core suppression bindings validated.'
} finally {$coreAssembly.Dispose()}

$fixture=Get-Content -Raw (Join-Path $PSScriptRoot 'SainAddonCombatFixture.cs')
$followerSource=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Components/BotFollowerPlayer.cs')
$push=[regex]::Match($followerSource,'(?ms)^        public void SetPushEnemy[(]float duration[)].*?^        [}]')
if(!$push.Success){throw 'Push command entry changed'}
$fixture=$fixture.Replace('__PUSH_METHOD__',$push.Value)
$gestureMethods=foreach($name in @('SetCombatComeToBossCover','SetCombatMoveToPointTactical','TryGetActiveCommand')){
    $match=[regex]::Match($followerSource,'(?ms)^        public (?:void|bool) '+$name+'\(.*?^        [}]')
    if(!$match.Success){throw "Gesture command boundary missing: $name"}
    $match.Value.Replace('public bool TryGetActiveCommand','public bool ReadGestureCommand')
}
$fixture=$fixture.Replace('__GESTURE_METHODS__',($gestureMethods -join [Environment]::NewLine))
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
$recorderSource=Get-Content -Raw -Encoding UTF8 (Join-Path $RepositoryRoot 'client/Modules/BattleRecorder.cs')
$recorderMethods=foreach($name in @('RecordCombatLayerState','RecordAddonCombatState','RecordAddonEvent','CreateRecorderStatePayload')){
    $match=[regex]::Match($recorderSource,'(?m)^        (?:public|private|internal) static [^\r\n]*\b'+$name+'\(')
    if(!$match.Success){throw "Missing recorder method: $name"}
    $begin=$recorderSource.IndexOf('{',$match.Index);$depth=0
    for($i=$begin;$i -lt $recorderSource.Length;$i++){
        if($recorderSource[$i] -eq '{'){$depth++}
        if($recorderSource[$i] -eq '}'){$depth--;if($depth -eq 0){$recorderSource.Substring($match.Index,$i-$match.Index+1);break}}
    }
}
$recorderMethods=$recorderMethods -join [Environment]::NewLine
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('pitFireTeam-sain-combat-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $sources=@('tests/SainCoverPerformanceFixture.cs','addon/SainRegroupFireSafety.cs','tests/SainRegroupFireFixture.cs','addon/SainContactEnemyBridge.cs','tests/SainContactFixture.cs','addon/SainMarksmanWeaponBridge.cs','tests/SainShooterWeaponFixture.cs','client/BigBrain/FollowerSuppressTargetPolicy.cs','addon/SAINFollowerSquadSupportObjective.cs','addon/SAINFollowerSquadSupportAction.cs','addon/SainSquadSupportBridge.cs','tests/SainSquadSupportFixture.cs','tests/SainShooterFixture.cs','client/Components/FollowerCombatTactics.cs','addon/SAINFollowerMarksmanObjective.cs','tests/SainRelocationFixture.cs','client/BigBrain/FollowerCombatCommandGeometry.cs','addon/SAINFollowerRelocationObjective.cs','addon/SAINFollowerRelocationAction.cs','addon/SAINFollowerApproachRoute.cs','addon/SainMedicalDecisionBridge.cs','tests/SainMedicalFixture.cs','client/Modules/SainBotOwnerAccessor.cs','client/Modules/SainCoverGeometry.cs','tests/SainRegroupChurnFixture.cs','client/BigBrain/FollowerPushRiskPolicy.cs','client/Modules/SainPushRiskBridge.cs','addon/SAINFollowerPushAssessment.cs','tests/SainPushRiskFixture.cs','addon/SAINFollowerPushHoldAction.cs','addon/SAINFollowerObjectives.cs','addon/SAINFollowerPushObjective.cs','client/BigBrain/FollowerPushGeometry.cs','tests/SainPushFixture.cs','tests/SainEnemyMarkerFixture.cs','addon/SAINFollowerCover.cs','addon/SAINFollowerCoverFinder.cs','addon/SainCoverSelectionBridge.cs','tests/SainCoverFixture.cs','addon/SAINFollowerPersonality.cs','tests/SainPersonalityFixture.cs','addon/SAINFollowerEngageAttempt.cs','addon/SAINFollowerMoveToEngageAction.cs','addon/SAINFollowerRecorder.cs','client/Modules/SainCombatRecorderBridge.cs','tests/SainEngageRecorderFixture.cs','addon/SAINFollowerRegroupObjective.cs','client/Modules/SainRegroupBridge.cs','tests/SainRegroupFixture.cs','addon/SAINFollowerCombatHandoff.cs','addon/SAINFollowerLingerAction.cs','tests/SainLingerFixture.cs','addon/SAINActionTypes.cs','addon/SAINFollowerSoloCombatLayer.cs','addon/SAINFollowerSquadCombatLayer.cs','addon/SAINFollowerSquadDecision.cs','addon/SAINFollowerSquadRegroupAction.cs','addon/SAINFollowerFollowSearchPartyAction.cs','addon/SainSquadDecisionBridge.cs','tests/SainSquadFixture.cs','addon/SAINFollowerRuntime.cs','client/Modules/SainAddonBridge.cs','addon/SainManPersonality.cs','client/Patches/FollowerSainFriendlyFirePatch.cs')
    $paths=@()
    foreach($sourcePath in $sources){
        $source=Get-Content -Raw -Encoding UTF8 (Join-Path $RepositoryRoot $sourcePath)
        if($sourcePath -eq 'tests/SainEngageRecorderFixture.cs'){$source=$source.Replace('__RECORDER_METHODS__',$recorderMethods)}
        if($sourcePath -eq 'tests/SainEnemyMarkerFixture.cs'){
            $ping=Get-Content -Raw -Encoding UTF8 (Join-Path $RepositoryRoot 'client/Utils/PingTeamates.cs')
            $begin=$ping.IndexOf('        private bool SynchronizeEnemyMarkerContacts(')
            $end=$ping.IndexOf('        private void CreateGuiStyle()', $begin)
            if($begin -lt 0 -or $end -le $begin){throw 'Marker resolver boundaries changed'}
            $source=$source.Replace('__MARKER_METHODS__',$ping.Substring($begin,$end-$begin))
            $begin=$ping.IndexOf('    internal sealed class EnemyMarkerContact')
            $end=$ping.IndexOf('    internal sealed class RetainedEnemyDownContact',$begin)
            if($begin -lt 0 -or $end -le $begin){throw 'Marker contact boundaries changed'}
            $source=$source.Replace('__MARKER_CONTACT__',$ping.Substring($begin,$end-$begin))
        }
        if($sourcePath -eq 'tests/SainSquadSupportFixture.cs'){
            $core=Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatCommon.cs')
            $position=[regex]::Match($core,'(?ms)^        private static bool IsTeamSearchSupportPosition\(.*?^        [}]')
            if(!$position.Success){throw 'Core push support predicate source missing'}
            $source=$source.Replace('__PUSH_SUPPORT_POSITION__',$position.Value)
        }
        if($sourcePath -eq 'tests/SainMedicalFixture.cs'){
            $covers=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/Covers.cs')
            $begin=$covers.IndexOf('        public static bool IsHardCoverFromThreat(Vector3 coverPosition, Vector3 threatPosition)')
            $end=$covers.IndexOf('        public static bool IsNavigablePoint(', $begin)
            if($begin -lt 0 -or $end -le $begin){throw 'Core hard-cover geometry changed'}
            $constants=[regex]::Matches($covers,'(?m)^        private const float HardCover[^;]+;').Value -join [Environment]::NewLine
            $source=$source.Replace('__CORE_COVER__',$constants+[Environment]::NewLine+$covers.Substring($begin,$end-$begin))
        }
        # Fixture types live alongside production under test; runtime uses the separate SAIN assembly.
        $source=[regex]::Replace($source,'Type.GetType\("([^"]+), SAIN"(?:, true)?\)','CombatChecks.ResolveType("$1")')
        $path=Join-Path $temporary ([IO.Path]::GetFileName($sourcePath))
        [IO.File]::WriteAllText($path,$source);$paths+=$path
    }
    $path=Join-Path $temporary 'Fixture.cs';[IO.File]::WriteAllText($path,$fixture);$paths+=$path
    $harmony=Join-Path $RepositoryRoot 'client/libs/0Harmony.dll';Copy-Item -LiteralPath $harmony -Destination $temporary
    Get-ChildItem (Join-Path $GameRoot 'BepInEx/core') -Filter '*.dll' |
        Where-Object {$_.Name -like 'Mono*' -or $_.Name -eq 'System.ValueTuple.dll'} | Copy-Item -Destination $temporary
    $json=Join-Path $RepositoryRoot 'client/libs4.1/Newtonsoft.Json.dll';Copy-Item -LiteralPath $json -Destination $temporary
    $shared=Join-Path $RepositoryRoot 'addon/refs/4.5.1/SAIN.Preset.Shared.dll';Copy-Item -LiteralPath $shared -Destination $temporary
    $exe=Join-Path $temporary 'Combat.exe'
    $arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nullable:disable','/define:DEBUG','/nostdlib+','/nowarn:8632',"/out:$exe","/reference:$harmony","/reference:$json","/reference:$shared")
    foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll','System.Runtime.Serialization.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
    $facade=Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2/Facades/netstandard.dll'
    if(!(Test-Path -LiteralPath $facade)){throw '.NET Framework 4.7.2 targeting pack is required for the recorder fixture'}
    # SAIN.Preset.Shared targets 2.1; Unity's facade forwards these settings types to Framework assemblies.
    Copy-Item -LiteralPath (Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/netstandard.dll') -Destination $temporary
    $arguments+='/reference:'+$facade
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