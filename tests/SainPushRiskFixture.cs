using System;
using EFT;
using pitTeam.BigBrain;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace EFT {
    public class RiskAIData {public float PowerOfEquipment=100;}
    public class RiskProfile {public RiskInfo Info=new RiskInfo();}
    public class RiskInfo {public RiskSettings Settings=new RiskSettings();}
    public class RiskSettings {public WildSpawnType Role=WildSpawnType.assault;}
    public partial class Player {public RiskAIData AIData=new RiskAIData();public RiskProfile Profile=new RiskProfile();}
    public class RiskMagazine {public RiskCartridges Cartridges=new RiskCartridges();public int MaxCount=30;}
    public class RiskCartridges {public int Count=30;}
    public class RiskWeapon : EFT.InventoryLogic.Weapon {public bool Automatic=true,Shotgun,Precision;public RiskMagazine Magazine=new RiskMagazine();public RiskMagazine GetCurrentMagazine()=>Magazine;}
    public class RiskShootController {public RiskWeapon Item=new RiskWeapon();}
    public class RiskSelector {public bool IsChanging;}
    public partial class RiskWeaponManager {public RiskShootController ShootController=new RiskShootController();public RiskWeapon CurrentWeapon=>ShootController.Item;public bool IsWeaponReady=true;public RiskSelector Selector=new RiskSelector();}
    public partial class BotOwner {
        public RiskAIData AIData=new RiskAIData();public RiskWeaponManager WeaponManager=new RiskWeaponManager();
        public bool RiskMedical,RiskCritical,RiskLongGunReady=true,LastActiveWeaponPolicyRead;public int RiskWeaponPolicy;
    }
}
namespace pitTeam.BigBrain {
    public sealed partial class FollowerCombatCommon {
        private BotOwner riskOwner;
        private BotOwner botOwner => riskOwner;
        public FollowerCombatCommon(BotOwner owner){riskOwner=owner;}
        public static bool IsAutomaticWeapon(RiskWeapon w)=>w?.Automatic==true;
        public static bool IsShotgunWeapon(RiskWeapon w)=>w?.Shotgun==true;
        public static bool IsPrecisionRifleWeapon(RiskWeapon w)=>w?.Precision==true;
        public static bool IsPushReadyLongGunActive(BotOwner b)=>b.RiskLongGunReady;
        public int GetAutoPushWeaponThreatPolicy(EnemyInfo e,bool activeWeaponOnly){riskOwner.LastActiveWeaponPolicyRead=activeWeaponOnly;return riskOwner.RiskWeaponPolicy;}
        __THREAT_RANGE_READER__
        public bool HasReportedHealWorkForPush()=>riskOwner.RiskMedical||riskOwner.Medecine.FirstAid.Have2Do;
        public bool IsFollowerCriticallyWounded()=>riskOwner.RiskCritical;
    }
    public static class FollowerCombatRiflemanEngagement {
        public static float GetCombatRoleThreatMultiplier(WildSpawnType role)=>role==WildSpawnType.bossKnight?1.5f:1f;
    }
}
public static partial class CombatChecks {
    private static void TestPushRisk(){
        TestRejectedSearchFiringCover();
        // Independent expected values from core's existing formulas, including clamps.
        bool formulaParity=true;
        foreach(float ratio in new[]{0f,.4f,1f,1.5f,3f}) foreach(float role in new[]{1f,1.15f,1.5f,3f})
        foreach(int group in new[]{1,2,3,4,8}) foreach(int weapon in new[]{0,1,2}) {
            float equipment=Math.Max(-6f,Math.Min(12f,(ratio-1f)*10f));
            float roleCost=Math.Max(0f,Math.Min(12f,(role-1f)*12f));
            float groupCost=group<=1?-5f:group==2?2f:group==3?7f:12f;
            float weaponCost=weapon==1?4f:weapon==2?12f:0f;
            float original=Math.Max(-12f,Math.Min(30f,equipment+roleCost+groupCost+weaponCost));
            formulaParity &= Math.Abs(FollowerPushRiskPolicy.Threat(ratio,role,group,weapon)-original)<0.00001f;
        }
        Check(formulaParity,"shared threat formula preserves core results across 300 input combinations");
        Check(FollowerPushRiskPolicy.Threat(1,1,1,0)==-5,"isolated equal-gear target keeps core threat bonus");
        Check(FollowerPushRiskPolicy.Threat(3,1.5f,4,2)==30,"combined gear role group and ammunition threat clamps at core cap");
        Check(FollowerPushRiskPolicy.Threat(.1f,1,1,0)==-11,"weaker equipment receives core bounded advantage");
        Check(FollowerPushRiskPolicy.Required(112.5f,12,20)==132,"Rifleman route threat and player pull share required aggression");
        Check(FollowerPushRiskPolicy.PlayerPull(80,20,100)==20&&FollowerPushRiskPolicy.PlayerPull(80,100,20)==-8,"core player separation penalty and approach bonus keep caps");
        for (int aggression = 0; aggression <= 100; aggression++)
            Check(Math.Abs(FollowerPushRiskPolicy.Required(aggression * 1.5f, 0, 0) - aggression) < .0001f,
                "shared Core/addon distance scale at aggression " + aggression);
        Check(Math.Abs(FollowerPushRiskPolicy.PlayerPull(40,10,30) - 6.285714f) < .0001f,
            "150m distance reference preserves existing player separation weighting");
        Check(Math.Abs(FollowerPushRiskPolicy.Required(70.5537643f,-10.5363321f,20) - 48.66020f) < .001f,
            "recorded Boris route and threat now fit sixty aggression");
        Check(FollowerPushRiskPolicy.ThreatRangeMultiplier(-5)==1.2f,"existing low-threat score increases final distance twenty percent");
        Check(FollowerPushRiskPolicy.ThreatRangeMultiplier(-2)==1&&FollowerPushRiskPolicy.ThreatRangeMultiplier(0)==1,
            "ordinary threat remains neutral instead of equating not-low with dangerous");
        Check(FollowerPushRiskPolicy.ThreatRangeMultiplier(2)==.75f,"positive combined threat shortens final distance twenty-five percent");
        Check(FollowerPushRiskPolicy.ThreatRangeMultiplier(-11,1.5f)==.75f,
            "dangerous boss role takes priority over weak equipment bonus");
        Check(FollowerPushRiskPolicy.ThreatRangeMultiplier(-5,1,1)==.75f,
            "cautious ammunition risk retains priority over low-threat score");
        foreach(float multiplier in new[]{.75f,1f,1.2f}) {
            float threat=multiplier==.75f?2:multiplier==1.2f?-5:0;
            float allowed=(60-threat-20)*1.5f*multiplier;
            Check(Math.Abs(FollowerPushRiskPolicy.Required(allowed,threat,20)-60)<.0001f,
                "Rifleman multiplies final adjusted distance rather than aggression: "+multiplier);
        }
        Check(FollowerPushRiskPolicy.EquipmentRatio(0,300)==1,"missing equipment baseline remains neutral");
        Check(!FollowerPushRiskPolicy.RestrictMagazine(20,20,true,false,false,out _),"loaded small automatic magazine is not treated as low ammo");
        Check(FollowerPushRiskPolicy.RestrictMagazine(6,8,false,true,false,out bool shotgun)&&shotgun,"core six-round shotgun exception remains available");
        Check(FollowerPushRiskPolicy.RestrictMagazine(10,10,false,false,true,out _),"small precision magazine restricts automatic pushes");
        var b=PushBot("riskAssessment",false);b.Follower.CombatAggression=100;
        var enemy=b.Sain.GoalEnemy;var assessment=new SAINFollowerPushAssessment(b.Sain);
        assessment.Evaluate(enemy,35,false);float baseline=assessment.RequiredAggression;
        Check(b.LastActiveWeaponPolicyRead,"Grunt retains active-weapon threat assessment");
        Check(assessment.EnemyCount==1&&!assessment.AllowsAutomatic,"automatic advance must also pass player separation score");
        b.Leader.Position=new Vector3(50,0,0);assessment.Evaluate(enemy,35,false);
        Check(assessment.RequiredAggression<baseline&&assessment.AllowsAutomatic,"moving toward player makes the same fight more acceptable");
        var other=new Enemy();other.EnemyPlayer.ProfileId="cluster2";other.KnownPlaces.LastKnownPosition=new Vector3(52,0,0);other.EnemyPosition=new Vector3(1000,0,0);
        b.Sain.EnemyController.KnownEnemies.Add(other);assessment.Evaluate(enemy,100,false);
        Check(assessment.EnemyCount==2&&assessment.Cautious,"known hostile near target promotes cautious approach despite hidden live location");
        other.KnownPlaces.LastKnownPosition=new Vector3(200,0,0);other.EnemyPosition=new Vector3(50,0,0);assessment.Evaluate(enemy,100,false);
        Check(assessment.EnemyCount==1,"hidden enemy moving into area cannot increase known cluster count");
        other.EnemyKnown=false;other.KnownPlaces.LastKnownPosition=new Vector3(50,0,0);assessment.Evaluate(enemy,100,false);
        Check(assessment.EnemyCount==1,"forgotten contact is excluded from risk count");
        baseline=assessment.RequiredAggression;enemy.EnemyPlayer.AIData.PowerOfEquipment=300;enemy.EnemyPlayer.Profile.Info.Settings.Role=WildSpawnType.bossKnight;
        assessment.Evaluate(enemy,100,false);Check(assessment.RequiredAggression>baseline&&assessment.Cautious,"enemy gear and role increase Rifleman threat requirement");
        b.RiskMedical=true;assessment.Evaluate(enemy,100,false);Check(assessment.SafetyBlocked&&!assessment.AllowsAutomatic,"pending medical work blocks even maximum aggression");b.RiskMedical=false;
        b.Sain.Memory.Health.HealthStatus=ETagStatus.BadlyInjured;assessment.Evaluate(enemy,100,false);Check(assessment.SafetyBlocked,"badly injured health blocks proactive pressure before dying");b.Sain.Memory.Health.HealthStatus=ETagStatus.Healthy;
        b.WeaponManager.Selector.IsChanging=true;assessment.Evaluate(enemy,100,false);Check(assessment.WeaponBlocked,"weapon switch must finish before advancing");b.WeaponManager.Selector.IsChanging=false;
        b.RiskWeaponPolicy=2;assessment.Evaluate(enemy,100,false);Check(assessment.Reason=="weaponThreat","poor active ammunition blocks distant auto push");b.RiskWeaponPolicy=0;
        b.WeaponManager.ShootController.Item.Magazine.Cartridges.Count=5;assessment.Evaluate(enemy,100,false);Check(assessment.Reason=="magazineReadiness","five remaining rounds block automatic advance");
        b=PushBot("riskOrdered");var p=SAINFollowerRuntime.GetPush(b);b.RiskMedical=true;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Ordered&&p.Phase==SAINPushPhase.Recovery&&!p.OwnsMovement,"ordered push retains target while waiting for treatment");
        b.RiskMedical=false;Time.time+=4;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);Check(p.OwnsMovement&&p.Ordered,"ordered push resumes after treatment clears");
        b=PushBot("riskCautiousCover");p=SAINFollowerRuntime.GetPush(b);b.Sain.GoalEnemy.EnemyPlayer.Profile.Info.Settings.Role=WildSpawnType.bossKnight;
        Physics.Blocked=true;b.Sain.Cover.CoverPoints.Add(CoverAt(15));b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Reason=="cautiousApproachCover"&&p.Destination.Value.x==15,"dangerous ordered approach accepts protective cover without an immediate firing lane");Physics.Blocked=false;
        b=PushBot("riskCautiousNoCover",false);p=SAINFollowerRuntime.GetPush(b);b.Sain.GoalEnemy.EnemyPlayer.Profile.Info.Settings.Role=WildSpawnType.bossKnight;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);int riskQueries=SAIN.SAINComponent.SubComponents.CoverFinder.SainBotCoverData.Queries;
        Check(p.Mode==SAINPushMode.Automatic&&p.OwnsMovement&&p.Destination.HasValue&&
            p.Destination.Value.x==20&&p.Reason=="provisionalAdvance",
            "risk-approved cautious automatic push uses bounded remembered-position step without forward cover");
        Time.time+=4;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(SAIN.SAINComponent.SubComponents.CoverFinder.SainBotCoverData.Queries==riskQueries&&p.Destination.Value.x==20,
            "no-cover cautious fallback retains its leg without repeating collider discovery");
        b=PushBot("riskAmmoParity");p=SAINFollowerRuntime.GetPush(b);b.Sain.Decision.SelfActionDecisions.AmmoRatio=0.4f;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.OwnsMovement,"native half-magazine heuristic cannot override core loaded-round readiness");
        b=PushBot("riskOrderedWeapon");p=SAINFollowerRuntime.GetPush(b);b.RiskLongGunReady=false;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Ordered&&!p.OwnsMovement&&p.Phase==SAINPushPhase.Recovery,"accepted order waits for a push-ready long gun");
        b.RiskLongGunReady=true;Time.time+=4;b.Sain.Cover.CoverPoint_MovingTo=CoverAt(10);b.Sain.Mover.Moving=true;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!p.OwnsMovement,"recovery does not cancel ongoing native cover travel when readiness improves");
        b=PushBot("riskAutoHeld",false);b.Follower.CombatAggression=30;p=SAINFollowerRuntime.GetPush(b);b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Phase==SAINPushPhase.Assessing&&!p.OwnsMovement&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.SeekCover,"native Search cannot bypass failed Rifleman admission");
        b.Follower.CombatAggression=100;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);Check(!p.OwnsMovement,"improved score does not immediately churn out of risk hold");
        Time.time+=4;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);Check(p.OwnsMovement,"improved assessment resumes after stable hold");
        var committed=p.Destination; b.Follower.CombatAggression=1;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.OwnsMovement&&p.Destination.HasValue&&committed.HasValue&&(p.Destination.Value-committed.Value).sqrMagnitude<0.01f,"ordinary risk change preserves committed movement leg");
        b.Sain.Memory.Health.HealthStatus=ETagStatus.BadlyInjured;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!p.OwnsMovement&&p.Phase==SAINPushPhase.Recovery,"health deterioration interrupts committed advance");
        TestCautiousNoCoverFallback();
        TestRiskHeldRegroup();
    }

    private static BotOwner CautiousNoCoverBot(string id)
    {
        var b=PushBot(id,false);
        b.Sain.GoalEnemy.EnemyPlayer.Profile.Info.Settings.Role=WildSpawnType.bossKnight;
        return b;
    }
    private static void TestCautiousNoCoverFallback()
    {
        var b=CautiousNoCoverBot("cautiousRouteFallback");var p=SAINFollowerRuntime.GetPush(b);
        UnityEngine.AI.NavMesh.SampleAllowed=v=>v.x!=20;
        UnityEngine.AI.NavMesh.Route=new[]{new Vector3(),new Vector3(0,0,25),new Vector3(50,0,25),new Vector3(50,0,0)};
        int publications=b.Sain.Decision.Manager.Publications;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.OwnsMovement&&p.Reason=="routeAdvance"&&p.Destination.Value.z==20&&
            b.Sain.Decision.Manager.Publications==publications+1,
            "cautious no-cover automatic push follows a bounded detour in one native publication");
        var destination=p.Destination;int probes=UnityEngine.AI.NavMesh.Calculations;
        b.Sain.GoalEnemy.EnemyPosition=new Vector3(-500,0,-500);
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Destination.HasValue&&destination.HasValue&&
            (p.Destination.Value-destination.Value).sqrMagnitude<0.01f&&UnityEngine.AI.NavMesh.Calculations==probes,
            "hidden live movement cannot redirect or replan the cautious fallback");
        var action=new SAINFollowerMoveToEngageAction(b);action.Start();
        action.Update(new DrakiaXYZ.BigBrain.Brains.CustomLayer.ActionData());
        Check(b.Sain.Mover.Destination.z==20,"cautious fallback uses existing native-derived movement executor");
        Time.time+=7;action.Update(new DrakiaXYZ.BigBrain.Brains.CustomLayer.ActionData());
        Check(p.Exhausted&&p.Reason=="noProgress","cautious fallback retains existing six-second stall bound");
        action.Stop();b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Exhausted&&!p.OwnsMovement,"repeated native Search cannot rearm a stalled cautious fallback");

        b=CautiousNoCoverBot("cautiousPartialFallback");p=SAINFollowerRuntime.GetPush(b);
        UnityEngine.AI.NavMesh.SampleAllowed=v=>v.x!=20;UnityEngine.AI.NavMesh.RouteComplete=false;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Exhausted&&p.Reason=="approachRouteIncomplete"&&!p.OwnsMovement,
            "cautious no-cover fallback rejects an actually incomplete route");

        b=CautiousNoCoverBot("cautiousReservedFallback");p=SAINFollowerRuntime.GetPush(b);
        ((pitTeam.Components.pitAIBossPlayer)b.BotFollower.BossToFollow).CombatEvents.Claims["other"]=new Vector3(20,0,0);
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Exhausted&&p.Reason=="approachReserved"&&!p.OwnsMovement,
            "cautious no-cover fallback cannot take a teammate's reserved destination");

        b=CautiousNoCoverBot("cautiousPressureFallback");p=SAINFollowerRuntime.GetPush(b);
        b.Memory.IsUnderFire=true;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Phase==SAINPushPhase.Recovery&&!p.OwnsMovement,
            "missing cover never bypasses incoming-fire recovery");
        UnityEngine.AI.NavMesh.SampleAllowed=null;UnityEngine.AI.NavMesh.Route=null;
    }
    private static BotOwner RejectedSearchBot(string id)
    {
        var b = PushBot(id, false);
        b.Follower.CombatAggression = 35;
        b.Leader.Position = new Vector3(-11, 0, 0);
        b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition = new Vector3(70, 0, 0);
        b.Sain.GoalEnemy.EnemyPlayer.AIData.PowerOfEquipment = 45;
        return b;
    }
    private static void TestRejectedSearchFiringCover()
    {
        var b = RejectedSearchBot("rejectedSearchLocalFire");
        var local = CoverAt(-5, 5); b.Sain.Cover.CoverPoints.Add(local);
        var p = SAINFollowerRuntime.GetPush(b);
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.OwnsMovement && p.Reason == "localFiringCover" && p.Destination.HasValue && (p.Destination.Value - local.Position).sqrMagnitude < .01f,
            "rejected full pursuit chooses a nearby firing cover even without forward progress");
        Check(!b.Sain.GoalEnemy.IsVisible && !b.Sain.GoalEnemy.CanShoot,
            "planning lane does not grant sight or permission to shoot");
        b.Sain.Cover.CoverPoints.Add(CoverAt(-8, 8));
        Time.time += 1; b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Destination.HasValue && (p.Destination.Value - local.Position).sqrMagnitude < .01f && p.OwnsMovement,
            "risk rejection cannot cancel or redirect the committed local leg");
        b.GetPlayer.Position = local.Position;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.Phase == SAINPushPhase.Pressure && b.Sain.Decision.CurrentCombatDecision == ECombatDecision.StandAndShoot,
            "local firing-cover arrival uses the existing three-second stationary hold");
        Time.time += 3.1f; b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!p.Destination.HasValue && b.Sain.Decision.CurrentCombatDecision == ECombatDecision.SeekCover,
            "local arrival without a shot falls back to SeekCover after its hold");
        for (int i = 0; i < 10; i++) { Time.time += .5f; b.Sain.Decision.Manager.Publish(ECombatDecision.Search); }
        Check(!p.OwnsMovement && !p.Destination.HasValue,
            "repeated Search cannot hop among local firing covers for the same blocked contact");
        b.Follower.CombatAggression = 100; b.Leader.Position = new Vector3(35, 0, 0); Time.time += 4;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(p.OwnsMovement, "passing full pursuit score resumes ordinary advance after local fallback");

        foreach (string failure in new[] { "blockedLane", "longRoute", "floor", "reserved", "outsidePlayer" })
        {
            b = RejectedSearchBot("rejectedSearch" + failure); p = SAINFollowerRuntime.GetPush(b);
            local = CoverAt(-5, failure == "longRoute" ? 26 : 5);
            if (failure == "blockedLane") Physics.Blocked = true;
            if (failure == "floor") local.Position = new Vector3(-5, 10, 0);
            if (failure == "reserved") ((pitTeam.Components.pitAIBossPlayer)b.BotFollower.BossToFollow).CombatEvents.Claims["other"] = local.Position;
            if (failure == "outsidePlayer") local.Position = new Vector3(24, 0, 0);
            b.Sain.Cover.CoverPoints.Add(local);
            for (int i = 0; i < 4; i++) { Time.time += .2f; b.Sain.Decision.Manager.Publish(ECombatDecision.Search); }
            Check(!p.OwnsMovement && b.Sain.Decision.CurrentCombatDecision == ECombatDecision.SeekCover,
                "local firing-cover fallback rejects " + failure);
        }
        b = RejectedSearchBot("rejectedSearchMedical"); b.RiskMedical = true;
        b.Sain.Cover.CoverPoints.Add(CoverAt(-5, 5));
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(SAINFollowerRuntime.GetPush(b).Phase == SAINPushPhase.Recovery && !SAINFollowerRuntime.GetPush(b).OwnsMovement,
            "local firing cover never bypasses medical recovery");
        b = RejectedSearchBot("rejectedSearchFactory");
        Comfort.Common.Singleton<GameWorld>.Instance.LocationId = "factory4_day";
        b.Sain.Cover.CoverPoints.Add(CoverAt(-5, 13)); b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!SAINFollowerRuntime.GetPush(b).OwnsMovement, "Factory local firing cover retains twelve-metre route limit");
        Comfort.Common.Singleton<GameWorld>.Instance.LocationId = null;
        Physics.Blocked = false;

        b = RejectedSearchBot("rejectedSearchBudget");
        for (int i = 0; i < 32; i++) b.Sain.Cover.CoverPoints.Add(CoverAt(-3 - i * .2f, 3 + i * .2f));
        var finder = new SAINFollowerCoverFinder(b.Sain);
        int polls = 0, found;
        do
        {
            Time.time += .3f;
            int before = Physics.LinecastCalls + SAIN.SAINComponent.SubComponents.CoverFinder.CoverAnalyzer.Rechecks + pitTeam.Utils.Utils.PathCalls;
            found = finder.FindNearbyFiring(b.Sain.GoalEnemy, b.Leader.Position, 28).Count;
            Check(Physics.LinecastCalls + SAIN.SAINComponent.SubComponents.CoverFinder.CoverAnalyzer.Rechecks + pitTeam.Utils.Utils.PathCalls - before <= 4,
                "local firing cover shares four-operation planning budget: " + polls);
            polls++;
        } while (finder.Pending && polls < 40);
        Check(!finder.Pending && found == 32, "local firing cover scan completes across slow polls without starvation");

        b = RejectedSearchBot("rejectedSearchRetrySector"); b.Sain.Cover.CoverPoints.Add(CoverAt(-5, 5));
        Physics.Blocked = true; b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Physics.Blocked = false; Time.time += 4; b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!SAINFollowerRuntime.GetPush(b).OwnsMovement, "unchanged rejected contact retains completed local scan");
        b.Leader.Position = Vector3.zero; Time.time += .3f; b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(SAINFollowerRuntime.GetPush(b).Reason == "localFiringCover", "player sector change permits a new local firing-cover attempt");
    }
    private static BotOwner RiskHeldBot(string id){
        var b=PushBot(id,false);b.Follower.CombatAggression=30;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        return b;
    }
    private static void TestRiskHeldRegroup(){
        var b=RiskHeldBot("riskRegroupNear");var p=SAINFollowerRuntime.GetPush(b);
        Check(p.Phase==SAINPushPhase.Assessing&&!SAINFollowerRuntime.GetRegroup(b).Active,"rejected automatic push holds locally inside player envelope");
        b.Leader.Position=new Vector3(-85,0,0);var cover=CoverAt(0);
        b.Sain.Cover.CoverSeekingState=SAIN.SAINComponent.Classes.ECoverSeekingState.MoveTo;
        b.Sain.Cover.CoverPoint_MovingTo=cover;b.Sain.Mover.Moving=true;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active,"risk-held push preserves native cover travel despite distant player");
        b.Sain.Mover.Moving=false;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active,"risk-held push preserves assigned cover before mover starts");
        SAINFollowerRuntime.GetCover(b).Selected(cover);Arrive(b,cover);
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active,"risk-held push preserves initial cover arrival hold");
        Time.time+=3.1f;int pubs=b.Sain.Decision.Manager.Publications;
        b.Sain.GoalEnemy.IsVisible=false;b.Sain.GoalEnemy.InLineOfSight=true;b.Sain.GoalEnemy.CanShoot=true;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(SAINFollowerRuntime.GetRegroup(b).Mode==SAINRegroupMode.Auto&&b.Sain.Decision.CurrentSquadDecision==ESquadDecision.Regroup&&b.Sain.Decision.Manager.Publications==pubs+1,"settled risk-held push hands off to automatic regroup in one publication");
        b.Sain.Decision.Manager.Frame();
        Check(SAINFollowerRuntime.GetRegroup(b).Active&&!p.OwnsMovement,"regroup keeps ownership while rejected automatic push remains paused");
        b=RiskHeldBot("riskNoCoverRegroup");b.Leader.Position=new Vector3(-85,0,0);
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(SAINFollowerRuntime.GetRegroup(b).Mode==SAINRegroupMode.Auto,"rejected push with exhausted cover selection can regroup without a failed movement attempt");
        b=RiskHeldBot("riskRecoveryRegroup");b.Leader.Position=new Vector3(-85,0,0);b.RiskMedical=true;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(SAINFollowerRuntime.GetPush(b).Phase==SAINPushPhase.Recovery&&!SAINFollowerRuntime.GetRegroup(b).Active,"pending treatment keeps recovery ahead of risk-held regroup");
        b=RiskHeldBot("riskVisibleRegroup");b.Leader.Position=new Vector3(-85,0,0);b.Sain.GoalEnemy.IsVisible=true;b.Sain.GoalEnemy.CanShoot=true;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.StandAndShoot,"visible shootable contact wins over risk-held regroup");
        b=RiskHeldBot("riskPathRegroup");b.Leader.Position=new Vector3(-85,0,0);pitTeam.Utils.Utils.PathComplete=false;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active,"risk-held regroup still requires a complete player route");pitTeam.Utils.Utils.PathComplete=true;
        b=RiskHeldBot("riskIndependentRegroup");b.Leader.Position=new Vector3(-85,0,0);b.Follower.CombatIndependent=true;
        b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!SAINFollowerRuntime.GetRegroup(b).Active,"On Your Own suppresses risk-held automatic regroup");
        b=PushBot("riskOrderedNoRegroup");b.Leader.Position=new Vector3(-85,0,0);b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(SAINFollowerRuntime.GetPush(b).Ordered&&!SAINFollowerRuntime.GetRegroup(b).Active,"ordered push keeps its intent despite a distant player");
    }

}
