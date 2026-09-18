using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.Classes.Decision;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;
namespace EFT.InventoryLogic { public class Weapon {public bool IsGrenadeLauncher;} }
namespace UnityEngine {
    public struct LayerMask {public int Value;public static implicit operator LayerMask(int v)=>new LayerMask{Value=v};}
    public static partial class Physics {public static int RaycastCalls;public static LayerMask LastMask;public static Func<Vector3,Vector3,float,bool> RayBlock;public static bool Raycast(Vector3 from,Vector3 direction,float distance,LayerMask mask){RaycastCalls++;LastMask=mask;return Blocked||RayBlock?.Invoke(from,direction,distance)==true;}}
    public class SupportTransform {public Vector3 position;}
}
namespace EFT {
    public class SupportLookSensor {public LayerMask Mask=7;}
    public class BotGroupEnemyInfo {public float EnemyLastSeenTimeReal,EnemyLastSeenTimeSense;}
    public partial class EnemyInfo {
        public Vector3 PersonalLastPos,EnemyLastPositionReal,EnemyLastPosition;
        public float PersonalLastSeenTime;public BotGroupEnemyInfo GroupInfo;
        public Vector3 GetBodyPartPosition(){throw new Exception("hidden live position must not be read");}
    }
    public class SupportAiming {public Vector3 RealTargetPoint;}
    public class SupportAimingManager {public SupportAiming CurrentAiming=new SupportAiming();}
    public partial class BotOwner {public SupportAimingManager AimingManager=new SupportAimingManager();public bool FriendlyInAim;public SupportTransform WeaponRoot;public SupportLookSensor LookSensor=new SupportLookSensor();public static Vector3 STAY_HEIGHT=new Vector3(0,1.5f,0);} }
namespace pitTeam.BigBrain {
    public static class FollowerImmediateFirePolicy {
        public static bool HasDirectFireLane(BotOwner owner,Vector3 point)=>!Physics.Raycast(owner.WeaponRoot?.position??owner.Position+Vector3.up*1.2f,(point-owner.Position).normalized,(point-owner.Position).magnitude,LayersMaskController.HighPolyWithTerrainMask);
    }
    public sealed partial class FollowerCombatCommon {
        public static bool IsFinite(Vector3 p)=>!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.x)&&!float.IsInfinity(p.y)&&!float.IsInfinity(p.z);
        __PUSH_SUPPORT_POSITION__
        public static bool SoftLane;
        public static bool IsSuppressCapableWeapon(EFT.InventoryLogic.Weapon weapon)=>weapon is RiskWeapon w&&(w.Automatic||w.Magazine.MaxCount>=25);
        public static bool IsGrenadeLauncherWeapon(EFT.InventoryLogic.Weapon weapon)=>weapon.IsGrenadeLauncher;
        public static bool IsSoftObstructedSuppressionLane(Vector3 from,Vector3 target,LayerMask mask)=>SoftLane;
    }
}
namespace pitTeam.Utils {
    public static partial class FollowerShotSafety {public static bool IsFriendlyInSuppressionLane(BotOwner bot,Vector3 origin,Vector3 target)=>bot.FriendlyInLane;
        public static bool IsFriendlyInShotLane(BotOwner bot,Vector3 origin,Vector3 target)=>bot.FriendlyInLane;
        public static bool IsFriendlyInAimLane(BotOwner bot,Vector3 origin,Vector3 direction,float distance)=>bot.FriendlyInAim;}
}
namespace SAIN.SAINComponent.Classes {
    public class SAINShootData {
        public BotComponent Bot;public int Ends;public bool Succeeds;public Enemy LastTarget;public bool Trigger=true;
        public void EndShoot(){Ends++;if(Bot!=null)Bot.BotOwner.ShootData.Shooting=false;}
        public bool ShootAnyVisibleEnemies(Enemy enemy)=>Succeeds;
        private bool AimAndShootAtEnemy(Enemy enemy,BotComponent bot){LastTarget=enemy;bot.BotOwner.AimingManager.CurrentAiming.RealTargetPoint=enemy.LastKnownPosition.Value+Vector3.up;bot.BotOwner.ShootData.Shooting=Succeeds&&Trigger;return Succeeds;}
    }
}
namespace SAIN.Models.Enums {public enum EShootReason {None,Suppress}}
namespace SAIN.SAINComponent.Classes.WeaponFunction {
    public class ManualShootClass {
        public BotComponent Bot;public int Calls,Resets;public bool Ready=true;public bool CanShoot(bool checkFF=true)=>Ready;public bool Succeeds=true;public Enemy LastTarget;
        public Vector3 ShootPosition;public bool CheckFriendly;public SAIN.Models.Enums.EShootReason Reason;
        public bool TryShoot(Enemy enemy,Vector3 point,bool checkFF=true,SAIN.Models.Enums.EShootReason reason=SAIN.Models.Enums.EShootReason.None){
            Calls++;LastTarget=enemy;CheckFriendly=checkFF;
            if(!Succeeds||Bot.BotOwner.FriendlyInLane){Reset();return false;}
            ShootPosition=point;Reason=reason;Bot.BotOwner.ShootData.Shooting=true;return true;
        }
        public void Reset(){Resets++;Reason=SAIN.Models.Enums.EShootReason.None;ShootPosition=Vector3.zero;if(Bot!=null)Bot.BotOwner.ShootData.Shooting=false;}
    }
}
namespace SAIN.Components {
    public partial class BotComponent {public SAIN.SAINComponent.Classes.WeaponFunction.ManualShootClass ManualShoot=new SAIN.SAINComponent.Classes.WeaponFunction.ManualShootClass();}
    public partial class Suppression {
        public BotComponent Bot;public bool SuppressingTarget;
        public Enemy LastTarget;public Vector3 LastPoint;public int Calls,Resets;public bool Succeeds=true;
        public bool TrySuppressEnemy(Enemy enemy,bool withBehaviorChecks=true){if(withBehaviorChecks)throw new Exception("ordered recency remains disabled");Calls++;LastTarget=enemy;return Succeeds;}
        public bool SuppressPosition(Vector3 point,Enemy enemy){Calls++;LastPoint=point;LastTarget=enemy;SuppressingTarget=Succeeds;return Succeeds;}
        public void ResetSuppressing(){Resets++;SuppressingTarget=false;if(Bot!=null)Bot.ManualShoot.Reset();}
        // SAIN 4.5.1 CheckEndSuppression -> UpdateSuppressionVector ownership contract.
        public void NativeUpdate(){if(!SuppressingTarget)return;if(LastTarget?.SuppressionTarget==null){ResetSuppressing();return;}Bot.ManualShoot.ShootPosition=LastTarget.SuppressionTarget.Value;}
    }
}
namespace pitTeam.Components {
    public partial class BotFollowerPlayer {public string SuppressEnemyTargetProfileId;public bool SuppressEnemyUseAutomaticSecondary;}
    public partial class pitAIBossPlayer {
        public List<BotOwner> Followers=new List<BotOwner>();public string Engagement;
        public sealed class SupportBossLogic {public bool IsHitted;}
        public SupportBossLogic SupportLogic=new SupportBossLogic();public BotOwner Attacker;
        public SupportBossLogic GetBossLogic()=>SupportLogic;
        public BotOwner ClosestEnemy()=>Attacker;
        public bool IsPlayerEngaging(out string id,out Vector3 point){id=Engagement;point=new Vector3(999,0,999);return id!=null;}
        public bool Require,Force,Secondary;public int Issued;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool TryIssueSuppressCommand(BotOwner follower,BotFollowerPlayer followerData,Player requestedEnemy,
            bool allowEnemyRetarget,bool requireLauncher=false,bool forceWeapon=false,bool useAutomaticSecondary=false)
        {Require=requireLauncher;Force=forceWeapon;Secondary=useAutomaticSecondary;Issued++;return true;}
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ApplySuppressPhrase(Player requester){Require=pitTeam.pitFireTeam.UseSainFollowerCombat(Followers[0]);}
        public bool AddonExcluded(BotOwner b){Followers.Clear();Followers.Add(b);ApplySuppressPhrase(null);return Require;}
        public bool Issue(BotOwner b,bool launcher=true)=>TryIssueSuppressCommand(b,b.Follower,null,false,launcher,false,true);
    }
    public partial class CombatEvents {
        public struct PushEvent {public BotOwner Owner;public string EnemyProfileId;public Vector3 Destination;}
        public PushEvent? Push;
        public bool TryGetActivePushFor(BotOwner bot,out PushEvent push){push=Push.GetValueOrDefault();return Push.HasValue&&push.Owner!=bot;}
    }
}
public static partial class CombatChecks {
    private static BotOwner SupportBot(string id) {
        var b=RegroupBot(id,0);b.Sain.Shoot.Bot=b.Sain;b.Sain.ManualShoot.Bot=b.Sain;b.Sain.Suppression.Bot=b.Sain;b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(60,0,0);
        b.Sain.GoalEnemy.EnemyPlayer.ProfileId=id+"enemy";b.Sain.EnemyController.KnownEnemies.Add(b.Sain.GoalEnemy);
        b.Memory.GoalEnemy=new EnemyInfo{ProfileId=b.Sain.GoalEnemy.EnemyProfileId,Person=b.Sain.GoalEnemy.EnemyPlayer};
        b.BotFollower.BossToFollow=new pitAIBossPlayer();
        b.Sain.Cover.CoverInUse=new SAIN.SAINComponent.SubComponents.CoverFinder.CoverPoint();
        b.Sain.Cover.CoverSeekingState=ECoverSeekingState.HoldInCover;
        FiringPositionFinder.Candidate=new Vector3(-10,0,0);FiringPositionFinder.Calls=0;
        return b;
    }
    private static SAINFollowerSquadSupportAction OrderSuppress(BotOwner b) {
        b.Follower.Command=FollowerCommandType.SuppressEnemy;b.Follower.SuppressEnemyTargetProfileId=b.Sain.GoalEnemy.EnemyProfileId;
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        var a=new SAINFollowerSquadSupportAction(b);a.Start();return a;
    }
    private static void OfferSupport(BotOwner b) {
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);Time.time+=1.1f;
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
    }
    private static void TestSquadSupport() {
        Physics.Blocked=false;pitTeam.BigBrain.FollowerCombatCommon.SoftLane=false;
        var b=SupportBot("squadOrder");var boss=(pitAIBossPlayer)b.BotFollower.BossToFollow;
        Check(boss.Issue(b)&&!boss.Require&&boss.Force&&!boss.Secondary,"addon command hook normalizes launcher preference to weapon-only without retargeting");
        b.WeaponManager.ShootController.Item.IsGrenadeLauncher=true;
        Check(!boss.Issue(b)&&boss.Issued==1,"launcher-only addon candidate cannot claim suppression order");
        b.Follower.CombatTactic=FollowerCombatTactic.Balanced;
        Check(boss.Issue(b)&&boss.Require&&boss.Secondary,"Core command flags are unchanged by addon hook");
        b.Follower.CombatTactic=FollowerCombatTactic.SainMan;b.WeaponManager.ShootController.Item.IsGrenadeLauncher=false;
        var squadLayer=new SAINFollowerSquadCombatLayer(b,75);Tick();
        var action=OrderSuppress(b);var objective=SAINFollowerRuntime.GetSquadSupport(b);
        Check(objective.Mode==SAINSquadSupportMode.OrderedSuppress&&b.Follower.Command==FollowerCommandType.None&&
            b.Sain.Decision.CurrentSquadDecision==ESquadDecision.Suppress&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.None,
            "suppression consumed once into Squad publication");
        squadLayer.IsActive();
        Check(squadLayer.GetNextAction().Type==typeof(SAINFollowerSquadSupportAction),"owned suppression dispatches through addon Squad action");
        int publications=b.Sain.Decision.Manager.Publications;
        action.Update(null);action.OnSteeringTicked();
        Check(b.Sain.Suppression.LastTarget==b.Sain.GoalEnemy&&b.Sain.Mover.Paths==0,"ordered suppression fires exact target without native pursuit");
        Check(b.Sain.Decision.Manager.Publications==publications,"support execution does not publish decisions");
        Time.time+=1.9f;action.OnSteeringTicked();Check(objective.Active,"suppression retains its opening burst");
        int calls=b.Sain.Suppression.Calls;Time.time+=.2f;action.Update(null);action.OnSteeringTicked();
        Check(!objective.Active&&b.Sain.Suppression.Calls==calls&&b.Sain.Suppression.Resets>0,"two-second burst ends and stale action cannot rearm it");
        action.Stop();

        b=SupportBot("squadBlocked");action=OrderSuppress(b);b.FriendlyInLane=true;action.OnSteeringTicked();
        Check(b.Sain.Suppression.Calls==0,"friendly lane prevents ordered trigger");
        b.FriendlyInLane=false;Physics.Blocked=true;action.OnSteeringTicked();
        Check(b.Sain.Suppression.Calls==0,"hard obstruction prevents ordered trigger");
        pitTeam.BigBrain.FollowerCombatCommon.SoftLane=true;action.OnSteeringTicked();
        Check(b.Sain.Suppression.Calls==1,"verified foliage-only lane retains Core ordered suppression permission");
        pitTeam.BigBrain.FollowerCombatCommon.SoftLane=false;action.OnSteeringTicked();
        Check(b.Sain.Suppression.Resets>0,"lane loss immediately resets owned native suppression");
        Physics.Blocked=false;b.Follower.Command=FollowerCommandType.RegroupNearBoss;action.OnSteeringTicked();
        Check(!SAINFollowerRuntime.GetSquadSupport(b).Active,"replacement command cancels before next shot");action.Stop();

        b=SupportBot("squadVisible");b.Sain.GoalEnemy.IsVisible=b.Sain.GoalEnemy.CanShoot=true;b.Sain.Shoot.Succeeds=true;b.Sain.Shoot.Trigger=false;
        action=OrderSuppress(b);objective=SAINFollowerRuntime.GetSquadSupport(b);action.OnSteeringTicked();
        Time.time+=2.1f;action.Update(null);Check(objective.Active,"native aiming alone does not spend the firing burst");
        b.Sain.Shoot.Trigger=true;action.OnSteeringTicked();
        Check(b.Sain.Shoot.LastTarget==b.Sain.GoalEnemy,"visible ordered target uses exact native aiming path");
        Time.time+=2.1f;action.Update(null);Check(!objective.Active&&b.Sain.Shoot.Ends>0,"visible burst completion releases owned firing");action.Stop();

        b=SupportBot("squadMedical");b.Follower.Command=FollowerCommandType.SuppressEnemy;
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.FirstAid);
        Check(!SAINFollowerRuntime.GetSquadSupport(b).Active&&b.Follower.Command==FollowerCommandType.SuppressEnemy,"medicine retains original pending order");
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);action=new SAINFollowerSquadSupportAction(b);action.Start();action.OnSteeringTicked();
        b.Sain.Medical.TimeSinceShot=0;action.OnSteeringTicked();Check(!SAINFollowerRuntime.GetSquadSupport(b).Active,"fresh damage cancels support immediately");action.Stop();

        b=SupportBot("squadPosition");b.Sain.GoalEnemy.SuppressionTarget=null;action=OrderSuppress(b);action.Update(null);
        objective=SAINFollowerRuntime.GetSquadSupport(b);
        Check(objective.Destination.HasValue&&FiringPositionFinder.Calls==1,"missing suppression lane can plan one native firing position");
        action.Update(null);Check(b.Sain.Mover.Paths>0,"squad action executes committed support point");
        b.GetPlayer.Position=objective.Destination.Value;action.Update(null);Time.time+=2.1f;action.Update(null);
        Check(!objective.Active,"reached position without firing expires rather than starting pursuit");action.Stop();

        b=SupportBot("allyPlayer");boss=(pitAIBossPlayer)b.BotFollower.BossToFollow;boss.Engagement=b.Sain.GoalEnemy.EnemyProfileId;
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);Check(FiringPositionFinder.Calls==0,"ally support waits for settled cover");
        Time.time+=1.1f;b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);objective=SAINFollowerRuntime.GetSquadSupport(b);
        Check(objective.Mode==SAINSquadSupportMode.AllySupport&&b.Sain.Decision.CurrentSquadDecision==ESquadDecision.Help,"player engagement prepares squad firing support");
        Check(objective.Destination.Value.x==-10,"support uses native remembered geometry rather than live player cue coordinates");
        for(int i=0;i<30;i++)b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(FiringPositionFinder.Calls==1,"support publication retains one destination without rescanning");
        action=new SAINFollowerSquadSupportAction(b);action.Start();action.Update(null);Time.time+=4.1f;action.Update(null);
        Check(!objective.Active,"stalled support releases the claim and objective");action.Stop();
        Time.time+=5f;OfferSupport(b);Check(!objective.Active,"failed same firing point cannot restart movement after cooldown");
        FiringPositionFinder.Candidate=new Vector3(-16,0,0);Time.time+=2.1f;b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(objective.Active,"different native point can provide a new bounded support attempt");objective.Clear("test");

        b=SupportBot("allyCore");boss=(pitAIBossPlayer)b.BotFollower.BossToFollow;
        var ally=Spawn("coreAlly",FollowerCombatTactic.Balanced);ally.Memory.GoalEnemy=new EnemyInfo{ProfileId=b.Sain.GoalEnemy.EnemyProfileId,IsVisible=true,CanShoot=true};boss.Followers.Add(ally);
        OfferSupport(b);Check(SAINFollowerRuntime.GetSquadSupport(b).Active,"Core teammate fight is visible to addon squad support");
        SAINFollowerRuntime.GetSquadSupport(b).Clear("test");
        b=SupportBot("allyFresh");boss=(pitAIBossPlayer)b.BotFollower.BossToFollow;boss.Engagement=b.Sain.GoalEnemy.EnemyProfileId;b.Sain.GoalEnemy.TimeSinceSeen=1;
        OfferSupport(b);Check(!SAINFollowerRuntime.GetSquadSupport(b).Active&&FiringPositionFinder.Calls==0,"fresh personal fight prevents ally reselection");
        b.Sain.GoalEnemy.TimeSinceSeen=20;b.Sain.Mover.Moving=true;OfferSupport(b);
        Check(!SAINFollowerRuntime.GetSquadSupport(b).Active,"committed movement is not interrupted for opportunistic support");
        b.Sain.Mover.Moving=false;FiringPositionFinder.Candidate=null;OfferSupport(b);
        Check(!SAINFollowerRuntime.GetSquadSupport(b).Active,"failed planning leaves existing hold intact");
        FiringPositionFinder.Candidate=new Vector3(10,0,0);Time.time+=2.1f;OfferSupport(b);
        Check(!SAINFollowerRuntime.GetSquadSupport(b).Active,"automatic support refuses an assaultward native point");
        FiringPositionFinder.Candidate=new Vector3(-10,0,0);boss.Engagement="unknown";Time.time+=2.1f;OfferSupport(b);
        Check(!SAINFollowerRuntime.GetSquadSupport(b).Active,"ally signal cannot invent an unknown native enemy");
        b=SupportBot("foliageReport");b.Sain.GoalEnemy.SuppressionTarget=null;
        var info=b.Sain.GoalEnemy.EnemyInfo;
        info.GroupInfo=new BotGroupEnemyInfo{EnemyLastSeenTimeSense=Time.time-.5f};info.EnemyLastPosition=new Vector3(38,0,4);
        Physics.Blocked=true;pitTeam.BigBrain.FollowerCombatCommon.SoftLane=true;
        action=OrderSuppress(b);objective=SAINFollowerRuntime.GetSquadSupport(b);action.Update(null);action.OnSteeringTicked();
        Check(b.Sain.ManualShoot.Calls==1&&b.Sain.ManualShoot.LastTarget==b.Sain.GoalEnemy&&
            (b.Sain.ManualShoot.ShootPosition-(info.EnemyLastPosition+BotOwner.STAY_HEIGHT)).sqrMagnitude<.001f,
            "missing native suppression point uses exact Core recent sensed report through foliage");
        Check(!objective.Destination.HasValue&&FiringPositionFinder.Calls==0,"valid report lane suppresses in place without a position scan");
        Check(Physics.LastMask.Value==b.LookSensor.Mask.Value,"suppression obstruction checks use Core's look mask");
        var diag=Newtonsoft.Json.Linq.JObject.FromObject(objective.Snapshot);
        Check((string)diag["suppressionSource"]=="coreRecentReport"&&(string)diag["laneGate"]=="foliageLane"&&
            (string)diag["fireState"]=="firingSuppression","recorder snapshot exposes selected fallback and foliage admission");
        int rays=Physics.RaycastCalls;int shots=b.Sain.ManualShoot.Calls;
        for(int i=0;i<30;i++)action.Update(null);
        Check(Physics.RaycastCalls==rays,"accepted stationary suppression does not repeat planning lane probes every frame");
        for(int i=0;i<30;i++)Newtonsoft.Json.Linq.JObject.FromObject(objective.Snapshot);
        Check(Physics.RaycastCalls==rays&&b.Sain.ManualShoot.Calls==shots,"suppression diagnostics never reevaluate geometry or trigger firing");
        pitTeam.BigBrain.FollowerCombatCommon.SoftLane=false;action.OnSteeringTicked();
        Check(b.Sain.ManualShoot.Calls==shots&&(string)Newtonsoft.Json.Linq.JObject.FromObject(objective.Snapshot)["fireState"]=="hardObstruction",
            "hard geometry immediately blocks fallback execution and records why");
        Physics.Blocked=false;b.FriendlyInLane=true;action.OnSteeringTicked();
        Check(b.Sain.ManualShoot.Calls==shots&&(string)Newtonsoft.Json.Linq.JObject.FromObject(objective.Snapshot)["fireState"]=="friendlyLane",
            "friendly lane still vetoes Core-report suppression");
        b.FriendlyInLane=false;info.GroupInfo.EnemyLastSeenTimeSense=Time.time-1.5f;
        action.OnSteeringTicked();shots=b.Sain.ManualShoot.Calls;Time.time+=.6f;action.OnSteeringTicked();
        Check(b.Sain.ManualShoot.Calls==shots&&objective.Active&&
            (string)Newtonsoft.Json.Linq.JObject.FromObject(objective.Snapshot)["fireState"]=="nativeTargetUnavailableAndNoFreshReport",
            "expired report stops the burst without refreshing old evidence");
        objective.Clear("test");action.Stop();

        b=SupportBot("reportPairing");b.Sain.GoalEnemy.SuppressionTarget=null;info=b.Sain.GoalEnemy.EnemyInfo;
        info.GroupInfo=new BotGroupEnemyInfo{EnemyLastSeenTimeSense=Time.time-5,EnemyLastSeenTimeReal=Time.time-.2f};
        info.EnemyLastPosition=new Vector3(900,0,900);info.EnemyLastPositionReal=new Vector3(30,0,3);
        var aim=SainSquadSupportBridge.ResolveSuppressionAim(b.Sain,b.Sain.GoalEnemy);
        Check(aim.Ready&&(aim.Point.Value-(info.EnemyLastPositionReal+BotOwner.STAY_HEIGHT)).sqrMagnitude<.001f,
            "fresh visual report cannot renew a different stale sensed position");
        info.GroupInfo.EnemyLastSeenTimeReal=Time.time+1f;
        Check(!SainSquadSupportBridge.ResolveSuppressionAim(b.Sain,b.Sain.GoalEnemy).Ready,"future report timestamps cannot authorize suppression");
        info.PersonalLastSeenTime=Time.time-.5f;info.PersonalLastPos=new Vector3(32,0,2);
        Check(SainSquadSupportBridge.ResolveSuppressionAim(b.Sain,b.Sain.GoalEnemy).Ready,"fresh personal report also uses shared Core policy");
        info.IsVisible=true;
        Check(!SainSquadSupportBridge.ResolveSuppressionAim(b.Sain,b.Sain.GoalEnemy).Ready,"native-hidden fallback never enters Core live body-position branch");
        info.IsVisible=false;info.ProfileId="differentEnemy";
        Check(!SainSquadSupportBridge.ResolveSuppressionAim(b.Sain,b.Sain.GoalEnemy).Ready,"report identity must match ordered native contact");
        info.ProfileId=b.Sain.GoalEnemy.EnemyProfileId;
        SAIN.Preset.Shared.GlobalSettings.GlobalSettingsClass.Instance.Mind.TARGET_SUPPRESS_TOGGLE=false;
        Check(!SainSquadSupportBridge.ResolveSuppressionAim(b.Sain,b.Sain.GoalEnemy).Ready,"report fallback cannot bypass global native suppression disablement");
        SAIN.Preset.Shared.GlobalSettings.GlobalSettingsClass.Instance.Mind.TARGET_SUPPRESS_TOGGLE=true;
        b.Sain.GoalEnemy.IsZombie=true;
        Check(!SainSquadSupportBridge.ResolveSuppressionAim(b.Sain,b.Sain.GoalEnemy).Ready,"point execution preserves native zombie exclusion");
        b.Sain.GoalEnemy.IsZombie=false;

        b=SupportBot("suppressionAlignment");b.Sain.Steering.AimAngle=20;
        action=OrderSuppress(b);action.OnSteeringTicked();objective=SAINFollowerRuntime.GetSquadSupport(b);
        Check(b.Sain.Suppression.Calls==0&&b.Sain.Steering.FallbackLooks==0&&
            (b.Sain.Steering.LookPoint-b.Sain.GoalEnemy.SuppressionTarget.Value).sqrMagnitude<.001f&&
            (string)Newtonsoft.Json.Linq.JObject.FromObject(objective.Snapshot)["fireState"]=="aligning",
            "alignment wait retains admitted point instead of replacing it with native look fallback");
        b.Sain.Steering.AimAngle=0;b.Sain.Suppression.Succeeds=false;action.OnSteeringTicked();
        Check(b.Sain.Steering.FallbackLooks==0&&(string)Newtonsoft.Json.Linq.JObject.FromObject(objective.Snapshot)["fireState"]=="nativeTriggerRejected",
            "native trigger rejection remains distinguishable and preserves suppression aim");
        b.Sain.Suppression.Succeeds=true;action.OnSteeringTicked();
        Check((string)Newtonsoft.Json.Linq.JObject.FromObject(objective.Snapshot)["fireState"]=="firingSuppression","aligned native trigger can start the bounded burst");
        objective.Clear("test");action.Stop();

        b=SupportBot("squadTarget");var original=b.Sain.GoalEnemy;
        var other=SupportBot("squadOther").Sain.GoalEnemy;b.Sain.EnemyController.KnownEnemies.Add(other);
        b.Follower.Command=FollowerCommandType.SuppressEnemy;b.Follower.SuppressEnemyTargetProfileId=other.EnemyProfileId;
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);objective=SAINFollowerRuntime.GetSquadSupport(b);
        action=new SAINFollowerSquadSupportAction(b);action.Start();action.OnSteeringTicked();
        Check(objective.EnemyId==other.EnemyProfileId&&b.Sain.Suppression.LastTarget==other,"directed suppression preserves requested known target instead of current native goal");
        Check(objective.PreferEnemy(original)==other,"cold native selection may retain ordered target");
        original.IsVisible=original.CanShoot=true;
        Check(objective.PreferEnemy(original)==original,"immediate personal contact retains native target priority");
        int before=b.Sain.Suppression.Calls;b.Sain.Decision.Manager.Publish(ECombatDecision.StandAndShoot);action.OnSteeringTicked();
        Check(objective.Active&&!objective.OwnsAction&&b.Sain.Suppression.Calls==before,"new visible threat pauses exact-target suppression without firing at either target through support");
        original.IsVisible=original.CanShoot=false;b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        b.Follower.Command=FollowerCommandType.HoldPosition;action.OnSteeringTicked();
        Check(!objective.Active&&objective.OwnsAction,"cancelled support keeps stale published action inert until next publication");
        b.Follower.Command=FollowerCommandType.None;b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(!objective.OwnsAction,"next native publication relinquishes cancelled support action ownership");action.Stop();

        b=SupportBot("squadWait");b.Follower.Command=FollowerCommandType.SuppressEnemy;b.Follower.SuppressEnemyTargetProfileId="unreceived";
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(b.Follower.Command==FollowerCommandType.SuppressEnemy&&!SAINFollowerRuntime.GetSquadSupport(b).Active,"explicit target waits for native knowledge without substituting current enemy");
        Time.time+=3.1f;b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(b.Follower.Command==FollowerCommandType.None&&!SAINFollowerRuntime.GetSquadSupport(b).Active,"unreceived suppression target expires without firing or inventing contact");
        b=SupportBot("squadIndependent");b.Follower.CombatIndependent=true;action=OrderSuppress(b);action.OnSteeringTicked();
        Check(SAINFollowerRuntime.GetSquadSupport(b).Active&&b.Sain.Suppression.Calls==1,"explicit suppression still works during On Your Own");
        b.Sain.GoalEnemy.EnemyPlayer.HealthController.IsAlive=false;action.OnSteeringTicked();
        Check(!SAINFollowerRuntime.GetSquadSupport(b).Active&&b.Sain.Suppression.Calls==1,"target death stops suppression without substituting another enemy");action.Stop();

        b=SupportBot("allyCorePush");boss=(pitAIBossPlayer)b.BotFollower.BossToFollow;
        ally=Spawn("pusher",FollowerCombatTactic.Balanced);ally.GetPlayer.Position=new Vector3(10,0,0);
        boss.CombatEvents.Push=new CombatEvents.PushEvent{Owner=ally,EnemyProfileId=b.Sain.GoalEnemy.EnemyProfileId,Destination=new Vector3(-10,0,0)};
        OfferSupport(b);Check(!SAINFollowerRuntime.GetSquadSupport(b).Active,"Core push support does not duplicate the pusher destination");
        FiringPositionFinder.Candidate=new Vector3(-15,0,0);Time.time+=2.1f;OfferSupport(b);
        Check(SAINFollowerRuntime.GetSquadSupport(b).Mode==SAINSquadSupportMode.PushSupport,"nearby Core automatic push can prepare distinct firing support");
        SAINFollowerRuntime.GetSquadSupport(b).Clear("test");
        b=SupportBot("allySainPush");boss=(pitAIBossPlayer)b.BotFollower.BossToFollow;
        ally=PushBot("nativePusher",false);ally.Sain.GoalEnemy.EnemyPlayer.ProfileId=b.Sain.GoalEnemy.EnemyProfileId;
        ally.Memory.GoalEnemy.ProfileId=b.Sain.GoalEnemy.EnemyProfileId;ally.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        boss.Followers.Add(ally);FiringPositionFinder.Candidate=new Vector3(-10,0,0);OfferSupport(b);
        Check(SAINFollowerRuntime.GetPush(ally).Mode==SAINPushMode.Automatic&&SAINFollowerRuntime.GetPush(ally).OwnsMovement&&
            SAINFollowerRuntime.GetSquadSupport(b).Active,"addon automatic push supplies support without publishing a Core push event");
        SAINFollowerRuntime.GetSquadSupport(b).Clear("test");
        b=SupportBot("allyArea");boss=(pitAIBossPlayer)b.BotFollower.BossToFollow;boss.Engagement=b.Sain.GoalEnemy.EnemyProfileId;
        FiringPositionFinder.Candidate=new Vector3(-25,0,0);OfferSupport(b);
        Check(!SAINFollowerRuntime.GetSquadSupport(b).Active,"dependent automatic support respects player area");
        b.Follower.CombatIndependent=true;Time.time+=2.1f;OfferSupport(b);
        Check(SAINFollowerRuntime.GetSquadSupport(b).Active,"independent automatic support removes only player-area restriction");
        SAINFollowerRuntime.GetSquadSupport(b).Clear("test");

        b=SupportBot("squadSharedScan");b.Follower.CombatTactic=FollowerCombatTactic.SAINShooter;Tick();
        boss=(pitAIBossPlayer)b.BotFollower.BossToFollow;boss.Engagement=b.Sain.GoalEnemy.EnemyProfileId;
        FiringPositionFinder.Candidate=null;FiringPositionFinder.Calls=0;
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);Time.time+=2.1f;
        int scans=FiringPositionFinder.Calls;b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(FiringPositionFinder.Calls==scans+1,"Shooter and squad support share the native scan cadence");
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(FiringPositionFinder.Calls==scans+1,"repeated support rejection cannot duplicate a native scan");
        Time.time+=2.1f;scans=FiringPositionFinder.Calls;b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(FiringPositionFinder.Calls==scans+1,"simultaneously eligible Marksman and squad retries execute only one native scan");
        Physics.Blocked=false;FiringPositionFinder.Candidate=null;
    }
    private static BotOwner BossSupportBot(string id) {
        var b=SupportBot(id);b.Memory.HaveEnemy=true;b.Sain.GoalEnemy.TimeSinceSeen=1f;
        var boss=(pitAIBossPlayer)b.BotFollower.BossToFollow;boss.SupportLogic.IsHitted=true;
        boss.Attacker=new BotOwner{ProfileId=b.Sain.GoalEnemy.EnemyProfileId,GetPlayer=b.Sain.GoalEnemy.EnemyPlayer};
        return b;
    }
    private static bool OfferGrunt(BotOwner b,ECombatDecision solo=ECombatDecision.SeekCover,ESelfActionType self=ESelfActionType.None) =>
        SAINFollowerRuntime.GetSquadSupport(b).Filter(b.Sain.GoalEnemy,solo,ESquadDecision.None,self,out _);
    private static void TestGruntSupport() {
        Physics.Blocked=false;pitTeam.BigBrain.FollowerCombatCommon.SoftLane=false;
        var b=BossSupportBot("bossBurst");var o=SAINFollowerRuntime.GetSquadSupport(b);
        Check(OfferGrunt(b)&&o.Mode==SAINSquadSupportMode.BossSupport&&!o.Destination.HasValue&&FiringPositionFinder.Calls==0,
            "boss hit with recent personal contact prepares stationary native suppression before any movement");
        o.Steer();Check(b.Sain.Suppression.Calls==1&&b.Sain.Suppression.LastTarget==b.Sain.GoalEnemy,"boss protection suppresses its admitted native target");
        int publications=b.Sain.Decision.Manager.Publications;
        Time.time+=2.1f;o.Tick();Check(!o.Active&&b.Sain.Suppression.Resets>0,"automatic boss burst stops after actual firing budget");
        Check(!OfferGrunt(b)&&b.Sain.Decision.Manager.Publications==publications,"completed boss burst has retry delay and never publishes decisions itself");

        b=BossSupportBot("bossUnknown");((pitAIBossPlayer)b.BotFollower.BossToFollow).Attacker.ProfileId="unknownBossThreat";
        Check(!OfferGrunt(b)&&FiringPositionFinder.Calls==0,"boss hit cannot manufacture missing native knowledge");
        b=BossSupportBot("bossOwnShot");b.Sain.GoalEnemy.IsVisible=b.Sain.GoalEnemy.CanShoot=true;
        Check(!OfferGrunt(b),"immediate personal shot retains normal combat instead of starting protection");
        b=BossSupportBot("bossStale");b.Sain.GoalEnemy.TimeSinceSeen=3;
        Check(!OfferGrunt(b),"Core boss protection requires fresh personal contact when already fighting");
        b=BossSupportBot("bossNoWillingness");b.Follower.BossProtectionWillingness01=.44f;
        Check(!OfferGrunt(b),"boss protection preserves Core pickup willingness threshold");
        b=BossSupportBot("bossNoHit");((pitAIBossPlayer)b.BotFollower.BossToFollow).SupportLogic.IsHitted=false;
        Check(!OfferGrunt(b),"nearby known enemy alone is not a boss-under-attack signal");
        b=BossSupportBot("bossIndependent");b.Follower.CombatIndependent=true;
        Check(!OfferGrunt(b),"On Your Own disables automatic boss protection");
        b=BossSupportBot("bossSwitchIndependent");Check(OfferGrunt(b),"boss support admitted before independence change");
        b.Follower.CombatIndependent=true;SAINFollowerRuntime.GetSquadSupport(b).Steer();
        Check(!SAINFollowerRuntime.GetSquadSupport(b).Active&&b.Sain.Suppression.Calls==0,"independence change cancels already prepared boss fire");
        b=BossSupportBot("bossMedicine");
        Check(!OfferGrunt(b,ECombatDecision.SeekCover,ESelfActionType.FirstAid),"incoming treatment keeps priority over boss support");
        b.Memory.IsUnderFire=true;Check(!OfferGrunt(b),"own incoming pressure keeps recovery priority");
        b=BossSupportBot("bossOrder");b.Follower.Command=FollowerCommandType.HoldPosition;
        Check(!OfferGrunt(b)&&b.Follower.Command==FollowerCommandType.HoldPosition,"support does not consume another pending command");
        b=BossSupportBot("bossTravel");b.Sain.Mover.Moving=true;
        Check(!OfferGrunt(b),"boss opportunity does not interrupt existing travel");
        b.Sain.Mover.Moving=false;b.Sain.Cover.CoverPoint_MovingTo=CoverAt(5);
        Check(!OfferGrunt(b),"assigned cover is preserved before its first movement tick");
        b.Sain.Cover.CoverPoint_MovingTo=null;b.Sain.Cover.CoverSeekingState=ECoverSeekingState.Shift;
        Check(!OfferGrunt(b),"native cover shift is not mistaken for a passive support boundary");
        b.Sain.Cover.CoverSeekingState=ECoverSeekingState.HoldInCover;b.Sain.Cover.CoverInUse.Spotted=true;
        Check(!OfferGrunt(b),"compromised cover keeps native recovery instead of stationary support");

        b=BossSupportBot("bossExactShot");var other=SupportBot("bossVisibleThreat").Sain.GoalEnemy;
        other.IsVisible=other.CanShoot=true;b.Sain.EnemyController.KnownEnemies.Add(other);
        ((pitAIBossPlayer)b.BotFollower.BossToFollow).Attacker=new BotOwner{ProfileId=other.EnemyProfileId,GetPlayer=other.EnemyPlayer};
        b.Sain.Shoot.Succeeds=true;Check(OfferGrunt(b),"known visible boss threat can prepare an immediate shot without a position search");
        o=SAINFollowerRuntime.GetSquadSupport(b);o.Steer();
        Check(b.Sain.Shoot.LastTarget==other&&b.Sain.Suppression.Calls==0,"boss fire uses native exact-target aiming rather than the previous hidden goal");
        o.Clear("test");

        b=BossSupportBot("bossCover");b.Sain.GoalEnemy.SuppressionTarget=null;
        b.Sain.Cover.CoverPoints.Add(CoverAt(8));FiringPositionFinder.Candidate=null;
        int queries=SAIN.SAINComponent.SubComponents.CoverFinder.SainBotCoverData.Queries;
        Check(OfferGrunt(b)&&SAINFollowerRuntime.GetSquadSupport(b).Destination.Value.x==8&&FiringPositionFinder.Calls==0,
            "boss support reuses a native validated firing cover position before invoking the firing finder");
        o=SAINFollowerRuntime.GetSquadSupport(b);o.Tick();int paths=b.Sain.Mover.Paths;
        for(int i=0;i<20;i++){OfferGrunt(b);o.Tick();}
        Check(o.Destination.Value.x==8&&b.Sain.Mover.Paths==paths&&SAIN.SAINComponent.SubComponents.CoverFinder.SainBotCoverData.Queries==queries,
            "support keeps its committed cover position without new overlaps or frame-rate path refreshes");
        o.Clear("test");
        b=BossSupportBot("bossRecheckedCover");b.Sain.GoalEnemy.SuppressionTarget=null;FiringPositionFinder.Candidate=null;
        var shifted=CoverAt(8);shifted.RecheckedPosition=new Vector3(100,0,0);b.Sain.Cover.CoverPoints.Add(shifted);
        Check(!OfferGrunt(b),"native cover recheck cannot move an admitted point beyond route and player limits");
        b=BossSupportBot("bossCoverBudget");b.Sain.GoalEnemy.SuppressionTarget=null;FiringPositionFinder.Candidate=null;
        for(int i=0;i<10;i++){var bad=CoverAt(-5-i);bad.Valid=false;b.Sain.Cover.CoverPoints.Add(bad);}
        int checks=SAIN.SAINComponent.SubComponents.CoverFinder.CoverAnalyzer.Rechecks;
        Check(!OfferGrunt(b)&&SAIN.SAINComponent.SubComponents.CoverFinder.CoverAnalyzer.Rechecks==checks+4&&FiringPositionFinder.Calls==1,
            "failed support checks at most four cached covers and one native firing search");
        Time.time+=.6f;Check(!OfferGrunt(b)&&SAIN.SAINComponent.SubComponents.CoverFinder.CoverAnalyzer.Rechecks==checks+4&&FiringPositionFinder.Calls==1,
            "opportunity polling cannot repeat geometry before the two-second planning budget");
        Check(b.Sain.Cover.CoverInUse!=null&&b.Sain.Mover.Paths==0,"no prepared support successor leaves the existing cover hold intact");
        Check(!SAINFollowerRuntime.GetSquadSupport(b).Filter(b.Sain.GoalEnemy,ECombatDecision.SeekCover,ESquadDecision.Help,ESelfActionType.None,out var unchanged)&&unchanged==ESquadDecision.Help,
            "failed automatic preparation preserves the proposed native squad decision");

        b=SupportBot("gruntAllyCover");var boss=(pitAIBossPlayer)b.BotFollower.BossToFollow;
        boss.Engagement=b.Sain.GoalEnemy.EnemyProfileId;b.Sain.Cover.CoverPoints.Add(CoverAt(-8));
        OfferSupport(b);o=SAINFollowerRuntime.GetSquadSupport(b);
        Check(o.Mode==SAINSquadSupportMode.AllySupport&&o.Destination.Value.x==-8&&FiringPositionFinder.Calls==0,
            "settled Grunt ally support prefers usable cached native cover over an exposed firing point");o.Clear("test");
        b=SupportBot("gruntAllyShot");other=SupportBot("allyVisibleThreat").Sain.GoalEnemy;
        other.IsVisible=other.CanShoot=true;b.Sain.EnemyController.KnownEnemies.Add(other);
        ((pitAIBossPlayer)b.BotFollower.BossToFollow).Engagement=other.EnemyProfileId;b.Sain.Shoot.Succeeds=true;
        OfferSupport(b);o=SAINFollowerRuntime.GetSquadSupport(b);o.Steer();
        Check(o.Mode==SAINSquadSupportMode.AllySupport&&!o.Destination.HasValue&&b.Sain.Shoot.LastTarget==other,
            "settled ally support takes an available exact-target shot before asking for movement");
        other.IsVisible=other.CanShoot=false;o.Steer();
        Check(b.Sain.Suppression.Calls==0,"ordinary ally supporting fire does not silently become hidden suppression");o.Clear("test");

        b=SupportBot("gruntPusherRelative");boss=(pitAIBossPlayer)b.BotFollower.BossToFollow;
        var ally=Spawn("supportPusher",FollowerCombatTactic.Balanced);ally.GetPlayer.Position=new Vector3(15,0,0);
        boss.CombatEvents.Push=new CombatEvents.PushEvent{Owner=ally,EnemyProfileId=b.Sain.GoalEnemy.EnemyProfileId,Destination=new Vector3(25,0,0)};
        boss.Engagement="irrelevantPlayerTarget";FiringPositionFinder.Candidate=new Vector3(10,0,0);
        Check(OfferGrunt(b)&&SAINFollowerRuntime.GetSquadSupport(b).Mode==SAINSquadSupportMode.PushSupport&&
            SAINFollowerRuntime.GetSquadSupport(b).Destination.Value.x==10,"Grunt may advance behind a nearby pusher using Core support geometry without becoming the lead assault");
        SAINFollowerRuntime.GetSquadSupport(b).Clear("test");Time.time+=2.1f;
        Check(SAINFollowerRuntime.GetSquadSupport(b).Filter(b.Sain.GoalEnemy,ECombatDecision.None,ESquadDecision.Help,ESelfActionType.None,out var assisted)&&assisted==ESquadDecision.Help&&
            SAINFollowerRuntime.GetSquadSupport(b).Mode==SAINSquadSupportMode.PushSupport,"native squad Help publication can enter prepared Grunt push support without a solo decision");
        SAINFollowerRuntime.GetSquadSupport(b).Clear("test");
        b=SupportBot("gruntNoOvertake");boss=(pitAIBossPlayer)b.BotFollower.BossToFollow;
        boss.CombatEvents.Push=new CombatEvents.PushEvent{Owner=ally,EnemyProfileId=b.Sain.GoalEnemy.EnemyProfileId,Destination=new Vector3(30,0,0)};
        FiringPositionFinder.Candidate=new Vector3(18,0,0);Check(!OfferGrunt(b),"push support refuses a native point ahead of the pusher");
        FiringPositionFinder.Candidate=new Vector3(10,0,0);ally.GetPlayer.Position=new Vector3(46,0,0);Time.time+=2.1f;
        Check(!OfferGrunt(b),"push support still requires a nearby helper within Core's distance limit");
        Physics.Blocked=false;FiringPositionFinder.Candidate=null;
    }

    private static void TestReportSuppressionOwnership() {
        Physics.Blocked=false;Physics.RayBlock=null;pitTeam.BigBrain.FollowerCombatCommon.SoftLane=false;
        var b=SupportBot("reportOwnsShot");var enemy=b.Sain.GoalEnemy;enemy.SuppressionTarget=null;
        var info=enemy.EnemyInfo;info.GroupInfo=new BotGroupEnemyInfo{EnemyLastSeenTimeSense=Time.time-.1f};info.EnemyLastPosition=new Vector3(30,0,0);
        var action=OrderSuppress(b);var o=SAINFollowerRuntime.GetSquadSupport(b);action.OnSteeringTicked();
        var point=b.Sain.ManualShoot.ShootPosition;int resets=b.Sain.ManualShoot.Resets;
        Check(b.Sain.Suppression.Calls==0&&!b.Sain.Suppression.SuppressingTarget&&b.Sain.ManualShoot.Calls==1&&
            b.Sain.ManualShoot.CheckFriendly&&b.Sain.ManualShoot.Reason==SAIN.Models.Enums.EShootReason.Suppress&&enemy.Status.EnemyIsSuppressed,
            "fresh report uses public manual fire with native friendly checks and owned suppression status");
        b.Sain.Suppression.NativeUpdate();
        Check(b.ShootData.Shooting&&b.Sain.ManualShoot.Resets==resets&&(b.Sain.ManualShoot.ShootPosition-point).sqrMagnitude<.001f,
            "native suppression update cannot cancel an admitted report with no native point");
        enemy.SuppressionTarget=new Vector3(90,0,0);Physics.RayBlock=(from,direction,distance)=>distance>70;
        action.OnSteeringTicked();b.Sain.Suppression.NativeUpdate();
        Check(b.ShootData.Shooting&&!b.Sain.Suppression.SuppressingTarget&&(b.Sain.ManualShoot.ShootPosition-point).sqrMagnitude<.001f,
            "rejected native suppression point cannot overwrite the admitted fresh report during native update");
        b.FriendlyInLane=true;action.OnSteeringTicked();
        Check(!b.ShootData.Shooting&&!enemy.Status.EnemyIsSuppressed&&b.Sain.ManualShoot.Resets>resets,
            "friendly lane interruption releases the fallback trigger and its suppression status");
        b.FriendlyInLane=false;enemy.SuppressionTarget=null;b.Sain.ManualShoot.Succeeds=false;action.OnSteeringTicked();
        Check(!b.ShootData.Shooting&&!enemy.Status.EnemyIsSuppressed&&
            (string)Newtonsoft.Json.Linq.JObject.FromObject(o.Snapshot)["fireState"]=="nativeTriggerRejected",
            "manual native trigger rejection never marks the enemy suppressed");
        b.Sain.ManualShoot.Succeeds=true;action.OnSteeringTicked();b.Follower.Command=FollowerCommandType.RegroupNearBoss;action.OnSteeringTicked();
        Check(!o.Active&&!b.ShootData.Shooting&&!enemy.Status.EnemyIsSuppressed,"replacement command cleans up the report-owned shot");action.Stop();
        Physics.RayBlock=null;

        b=SupportBot("reportSources");enemy=b.Sain.GoalEnemy;info=enemy.EnemyInfo;
        info.GroupInfo=new BotGroupEnemyInfo{EnemyLastSeenTimeSense=Time.time-.1f};info.EnemyLastPosition=new Vector3(32,0,2);
        action=OrderSuppress(b);o=SAINFollowerRuntime.GetSquadSupport(b);action.OnSteeringTicked();
        Check(b.Sain.Suppression.SuppressingTarget&&b.Sain.Suppression.Calls==1,"ordinary native suppression retains its original owner");
        enemy.SuppressionTarget=null;Time.time+=.25f;action.OnSteeringTicked();b.Sain.Suppression.NativeUpdate();
        Check(!b.Sain.Suppression.SuppressingTarget&&b.ShootData.Shooting&&enemy.Status.EnemyIsSuppressed,
            "native-to-report handoff releases the native updater before starting manual fire");
        enemy.SuppressionTarget=new Vector3(35,1,0);resets=b.Sain.ManualShoot.Resets;Time.time+=.25f;action.OnSteeringTicked();
        Check(b.Sain.Suppression.SuppressingTarget&&b.Sain.Suppression.Calls==2&&b.Sain.ManualShoot.Resets>resets,
            "report-to-native handoff releases the owned manual shot before native suppression resumes");
        Time.time+=1.6f;action.Update(null);Check(!o.Active,"switching point sources cannot restart the original two-second burst");action.Stop();

        b=BossSupportBot("bossReportMedical");enemy=b.Sain.GoalEnemy;enemy.SuppressionTarget=null;info=enemy.EnemyInfo;
        info.GroupInfo=new BotGroupEnemyInfo{EnemyLastSeenTimeSense=Time.time-.1f};info.EnemyLastPosition=new Vector3(25,0,2);
        Check(OfferGrunt(b),"boss support accepts the same fresh report fallback");o=SAINFollowerRuntime.GetSquadSupport(b);o.Steer();b.Sain.Suppression.NativeUpdate();
        Check(b.ShootData.Shooting,"boss report shot survives the native suppression update");
        b.UsingMedical=true;o.Steer();Check(!o.Active&&!b.ShootData.Shooting&&!enemy.Status.EnemyIsSuppressed,
            "medical takeover releases automatic report suppression without another publication");
        Physics.Blocked=false;Physics.RayBlock=null;
    }

    private static BotOwner VisibleSupportBurst(string id) {
        Physics.Blocked=false;Physics.RayBlock=null;SupportWeaponData.Current=new SupportWeaponData();
        var b=SupportBot(id);b.Sain.GoalEnemy.IsVisible=b.Sain.GoalEnemy.CanShoot=true;b.Sain.Shoot.Succeeds=true;
        OrderSuppress(b).OnSteeringTicked();b.Sain.GoalEnemy.CanShoot=false;return b;
    }
    private static void TestVisibleSupportFlicker() {
        var b=VisibleSupportBurst("fireFlicker");var o=SAINFollowerRuntime.GetSquadSupport(b);
        int ends=b.Sain.Shoot.Ends;Time.time+=.2f;o.Steer();
        Check(b.ShootData.Shooting&&b.Sain.Shoot.Ends==ends&&
            (string)Newtonsoft.Json.Linq.JObject.FromObject(o.Snapshot)["fireState"]=="visibleFlickerGrace",
            "safe visible CanShoot flicker retains the already firing burst");
        b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(80,0,4);Time.time+=.2f;o.Steer();
        Check(b.ShootData.Shooting&&(b.Sain.Steering.LookPoint-new Vector3(60,1,0)).sqrMagnitude<.001f,
            "flicker continuity stays at the captured verified aim point rather than following newer knowledge");
        int rays=Physics.RaycastCalls;for(int i=0;i<20;i++)Newtonsoft.Json.Linq.JObject.FromObject(o.Snapshot);
        Check(Physics.RaycastCalls==rays,"continuity diagnostics never rerun safety checks");
        Time.time+=.11f;o.Steer();Check(!b.ShootData.Shooting&&b.Sain.Shoot.Ends>ends,
            "repeated flicker ticks cannot renew the half-second limit");o.Clear("test");
        b=VisibleSupportBurst("fireFlickerFriendly");b.FriendlyInLane=true;SAINFollowerRuntime.GetSquadSupport(b).Steer();
        Check(!b.ShootData.Shooting,"friendly in retained target lane stops the burst inside the grace period");
        b=VisibleSupportBurst("fireFlickerAimFriend");b.FriendlyInAim=true;SAINFollowerRuntime.GetSquadSupport(b).Steer();
        Check(!b.ShootData.Shooting,"friendly in actual aim lane stops the burst inside the grace period");
        b=VisibleSupportBurst("fireFlickerWall");Physics.Blocked=true;pitTeam.BigBrain.FollowerCombatCommon.SoftLane=true;
        SAINFollowerRuntime.GetSquadSupport(b).Steer();Check(!b.ShootData.Shooting,
            "visible-fire continuity requires Core direct geometry and cannot borrow foliage suppression permission");
        pitTeam.BigBrain.FollowerCombatCommon.SoftLane=false;
        b=VisibleSupportBurst("fireFlickerTurn");SupportWeaponData.Current.PointDirection=new Vector3(0,0,1);
        SAINFollowerRuntime.GetSquadSupport(b).Steer();Check(!b.ShootData.Shooting,"actual muzzle misalignment ends flicker continuation");
        b=VisibleSupportBurst("fireFlickerMoved");b.GetPlayer.Position=new Vector3(.76f,0,0);
        SAINFollowerRuntime.GetSquadSupport(b).Steer();Check(!b.ShootData.Shooting,"moving off the verified firing spot cancels continuity");
        b=VisibleSupportBurst("fireFlickerWeapon");b.Sain.ManualShoot.Ready=false;SAINFollowerRuntime.GetSquadSupport(b).Steer();
        Check(!b.ShootData.Shooting,"native weapon readiness remains required during continuity");
        b=VisibleSupportBurst("fireFlickerNoRestart");b.ShootData.Shooting=false;SAINFollowerRuntime.GetSquadSupport(b).Steer();
        Check(!b.ShootData.Shooting&&b.Sain.ManualShoot.Calls==0,"flicker grace never starts another burst after native trigger expiry");
        b=VisibleSupportBurst("fireFlickerRenew");o=SAINFollowerRuntime.GetSquadSupport(b);Time.time+=.4f;
        b.Sain.GoalEnemy.CanShoot=true;o.Steer();Time.time+=.2f;b.Sain.GoalEnemy.CanShoot=false;o.Steer();
        Check(b.ShootData.Shooting,"a newly verified native shot legitimately renews continuity");
        o.Pause();ends=b.Sain.Shoot.Ends;b.ShootData.Shooting=true;o.Steer();
        Check((string)Newtonsoft.Json.Linq.JObject.FromObject(o.Snapshot)["fireState"]!="visibleFlickerGrace"&&b.Sain.Shoot.Ends==ends,
            "paused support loses its continuity lease without stopping a subsequent unowned shot");
        b.ShootData.Shooting=false;o.Clear("test");
        Physics.Blocked=false;SupportWeaponData.Current=new SupportWeaponData();
    }

}
