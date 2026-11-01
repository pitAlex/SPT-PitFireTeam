using EFT;
using UnityEngine;

public enum BotLogicDecision { holdPosition, shootFromCover, shootFromPlace, lay, heal, healStimulators, runToCover, attackMoving, goToEnemy, runToEnemy, suppressFire, suppressGrenade, dogFight, goToPoint, search }
public class CoreActionResultParams { }
public class CustomNavigationPoint { public Vector3 Position=new(20,0,0); }
public struct AICoreActionResult<T, D>(T action, string reason, D data = default)
{ public T Action = action; public string Reason = reason; public D Data = data; }
public struct AICoreActionEnd(string reason, bool value) { public string Reason = reason; public bool Value = value; }
public class AICoreLayer<T>(string name, int priority)
{ public virtual string Name() => name; public int Priority = priority; public int Activations; }
public class BossKnightFightLayer() : AICoreLayer<BotLogicDecision>("KnightFight", 62) { }
public class BigPipeFightLayer() : AICoreLayer<BotLogicDecision>("BigPipeFight", 60) { }
public class BirdEyeFightLayer() : AICoreLayer<BotLogicDecision>("BirdEyeFight", 50) { }
public class BirdEyeHoldLayer() : AICoreLayer<BotLogicDecision>("BirdHold", 55) { }
public class AssaultBuildingLayer() : AICoreLayer<BotLogicDecision>("Assault Building", 72) { }
public class ControlBuildingLayer() : AICoreLayer<BotLogicDecision>("Enemy Building", 70) { }
public class AssaultHaveEnemyLayer() : AICoreLayer<BotLogicDecision>("Enemy", 50) { }
public class WarnPlayerLayer() : AICoreLayer<BotLogicDecision>("Warn", 75) { }
public class PeacefulRequestLayer() : AICoreLayer<BotLogicDecision>("Peace", 30) { }
public class BaseBrain
{
    public string BrainName="BigPipe"; public string ShortName()=>BrainName;
    public Dictionary<int, AICoreLayer<BotLogicDecision>> _layers = new();
    public List<AICoreLayer<BotLogicDecision>> _activeLayers = new();
}
public class FollowerBigPipeLayersStrategy : BaseBrain { }
public class FollowerBirdEyeLayersStrategy : BaseBrain { }
public class BotWeaponSelector { public EFT.BotOwner Owner; }
public class BigPipeBotWeaponSelector : BotWeaponSelector { public void ManualUpdate() { } }
namespace UnityEngine
{
    public static class Time { public static float time; }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); public static float Clamp(float v,float min,float max)=>Math.Max(min,Math.Min(max,v)); }
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z;
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x*b,a.y*b,a.z*b);
        public static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a-b).sqrMagnitude);
        public Vector3 normalized {get {float m=MathF.Sqrt(sqrMagnitude);return new(x/m,y/m,z/m);}}
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>new(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t);
    }
}
namespace EFT
{
    public class Health { public bool IsAlive = true; }
    public class MovementContext { public bool IsGrounded=true; }
    public class Player { public MovementContext MovementContext=new(); public Health HealthController = new(); public bool IsYourPlayer=true; public Profile Profile=new(); public AIData AIData=new();
        public class FirearmController {public bool Reloading;public bool IsInReloadOperation()=>Reloading;}}
    public class AIData { public float PowerOfEquipment=100; public BotOwner BotOwner; }
    public enum EBotState { NonActive, Active, Disposed }
    public enum WildSpawnType { followerBigPipe, bossKnight, assault, assaultGroup, cursedAssault, marksman, pmcUSEC, pmcBEAR, bossSanitar, followerBirdEye }
    public class ProfileSettings { public WildSpawnType Role=WildSpawnType.followerBigPipe; }
    public class ProfileInfo { public ProfileSettings Settings=new(); }
    public class Profile { public ProfileInfo Info=new(); }
    public class GroupInfo { public float EnemyLastSeenTimeReal = 99; }
    public class EnemyInfo
    {
        public string ProfileId = "enemy"; public bool IsVisible, CanShoot, MemoryOnly, Suppressed, WantsSuppress, Known=true;
        public float Distance,PersonalLastSeenTime,TimeLastSeenReal=99; public GroupInfo GroupInfo=new(); public Vector3 Anchor=new(30,0,0); public Player Person = new();
        public bool IsSuppressed()=>Suppressed; public bool ShallISuppress()=>WantsSuppress;
    }
    public class Memory { public EnemyInfo GoalEnemy = new(); public bool IsUnderFire, IsInCover; public float ComeToCoverTime; }
    public class Medicine { public bool Using; }
    public class Grenades { public bool ThrowindNow, ReadyToThrow; }
    public class Reload { public bool Reloading; }
    public class WeaponSelector { public bool IsChanging; }
    public class Weapons { public Grenades Grenades = new(); public Reload Reload=new();public bool IsWeaponReady=true; public bool HaveBullets=true;public WeaponSelector Selector=new();public Player.FirearmController ShootController=new(); }
    public class BossSettings { public float KILLA_START_SEARCH_SEC=10,KILLA_HOLD_DELAY=2,KILLA_CLOSEATTACK_DELAY=2; }
    public class Settings { public Settings FileSettings=>this; public BossSettings Boss=new(); }
    public class Follower { public object BossToFollow; }
    public class Agent { public List<string> Deactivated = new(); public void Deactivate(string name) => Deactivated.Add(name); }
    public class Brain { public BaseBrain BaseBrain = new(); public Agent Agent = new(); }
    public class BotsGroup { public List<BotOwner> Members=new(); public int MembersCount=>Members.Count; public BotOwner Member(int i)=>Members[i]; }
    public class BotOwner
    {
        public BotsGroup BotsGroup=new();
        public string ProfileId=Guid.NewGuid().ToString(); public Profile Profile=new(); public EBotState BotState=EBotState.Active;
        public AIData AIData=new(); public int KnownEnemies=1;
        public bool IsDead,HasLauncher; public Memory Memory = new(); public Medicine Medecine = new(); public Settings Settings=new();
        public Weapons WeaponManager = new(); public Follower BotFollower = new();
        public Mover Mover=new(); public Player GetPlayer = new(); public Brain Brain = new(); public Vector3 Position; public PointData GoToSomePointData=new();
    }
    public class Mover { public bool HasPathAndNoComplete; }
    public class PointData { public bool Arrived; public Vector3 Point;public void SetPoint(Vector3 point){Point=point;} public bool HaveTarget()=>true; public bool IsCome()=>Arrived; }
}
namespace pitTeam.Components
{
    public enum FollowerCommandType { None, PushEnemy, SuppressEnemy, NeedSniper, Regroup }
    public class BotFollowerPlayer
    {
        public float? UnitAggression,UnitDetermination;
        public BotOwner CompanionLeader;
        public void SetCompanionCombatLeader(BotOwner leader)=>CompanionLeader=leader;
        public void SetCompanionCombatPreferences(float aggression,float determination){UnitAggression=aggression;UnitDetermination=determination;}
        public void ClearCompanionCombatPreferences(){UnitAggression=UnitDetermination=null;CompanionLeader=null;}
        public void ResetCombatPercentageOverrides(string source){}
        public BotOwner Owner; public pitAIBossPlayer Boss=new();
        public string Tactic="Balanced";
        public void Init() { }
        public void SetCombatTacticFromString(string tactic)=>Tactic=tactic;
        public int Cleared, AggressionCleared;
        public void ClearCommand(string reason) { Command=FollowerCommandType.None;Cleared++; }
        public void ClearTemporaryCombatAggressionOverride(string reason) { AggressionCleared++; }
        public BotOwner GetBot()=>Owner; public pitAIBossPlayer GetBoss()=>Boss;
        public FollowerCommandType Command;
        public bool CombatRegroupUsesBossAnchor, SuppressEnemyUseAutomaticSecondary;
        public bool HasOrderedPushTargetLock;
        public bool TryGetActiveCommand(out FollowerCommandType command, out Vector3 target)
        { command = Command; target = default; return command != FollowerCommandType.None; }
        public bool TryPeekActiveCommand(out FollowerCommandType command, out Vector3 target, out float until)
        { until = 100; return TryGetActiveCommand(out command, out target); }
    }
    public class BossLogic { public bool IsHitted; }
    public class pitAIBossPlayer
    {
        public static int CountKnownAdvanceEnemies(BotOwner owner,EnemyInfo enemy)=>owner.KnownEnemies;
        public Player realPlayer=new();
        public BossLogic Logic = new(); public BotOwner Attacker;
        public BossLogic GetBossLogic() => Logic; public BotOwner ClosestEnemy() => Attacker;
    }
    public class CombatEvents
    {
        public class PushEvent
        { public BotOwner Owner = new(); public bool IsSearchPush; public Vector3 EnemyPosition, Destination; }
    }
}
namespace pitTeam.Modules
{
    public class CombatDistanceConfiguration {public static CombatDistanceConfiguration Instance=new();public bool IsUrbanDetourRegroup(float direct,float nav)=>false;public float GetBossRegroupTriggerDistance(BotOwner owner)=>35f;}
    public static class FollowerCombatAnchor {public static bool Independent;public static bool IsCombatIndependent(BotOwner owner)=>Independent;}
    public static class BattleRecorder {public static void RecordObjectiveDiagnostic(BotOwner owner,string objective,string action,string reason,Func<object> details){} }
    public static class FollowerAwareness { }
    public static class FollowerEnemyTracking
    { public static bool GroupSearching;
      public static void SearchTick(EnemyInfo enemy){GroupSearching=true;}
      public static void EndSearch(BotOwner owner,string enemy){GroupSearching=false;}
      public static float Distance(EnemyInfo enemy)=>enemy.Distance;
      public static bool TryGetKnownPosition(EnemyInfo enemy,out Vector3 point,out string source){point=enemy.Anchor;source="fixture";return enemy.Known;} }
}
namespace pitTeam.Utils
{
    public static class Enemy
    {
        public enum EnemyDistance { Close, Far }
        public static EnemyDistance Distance(EnemyInfo e) => e.Distance >= 96f ? EnemyDistance.Far : EnemyDistance.Close;
        public static bool IsMemoryOnlyAcquisitionWithoutPersonalContact(EnemyInfo enemy)=>enemy.MemoryOnly;
    }
}
namespace pitTeam.BigBrain
{
    public static class FollowerCombatRiflemanEngagement
    {public static float GetCombatRoleThreatMultiplier(WildSpawnType role)=>role==WildSpawnType.marksman?1.15f:1f;}
    public abstract class FollowerCombatLogicBase
    {
        protected enum CombatObjectiveKind { Default, OrderedPush }
        protected CombatObjectiveKind currentObjective;
        protected readonly BotOwner BotOwner;
        protected readonly FollowerCombatCommon combatCommon;
        protected readonly FollowerCombatObjectiveBase defaultObjective;
        protected FollowerCombatLogicBase(BotOwner owner) { BotOwner=owner;combatCommon=new FollowerCombatCommon();defaultObjective = CreateDefaultObjective(owner, combatCommon); }
        protected bool CanInterruptForOrderedPushOrder(AICoreActionResult<BotLogicDecision,CoreActionResultParams> current)=>current.Action!=BotLogicDecision.dogFight&&!FollowerCombatCommon.IsMedicalDecision(current);
        protected void ActivatePrimaryObjective(string reason) { currentObjective=CombatObjectiveKind.Default; }
        public void ProbeBeginPush()=>currentObjective=CombatObjectiveKind.OrderedPush;
        public bool ProbeInPush=>currentObjective==CombatObjectiveKind.OrderedPush;
        public FollowerCombatCommon ProbeCommon=>combatCommon;
        public virtual AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision,CoreActionResultParams> current)=>default;
        protected abstract FollowerCombatObjectiveBase CreateDefaultObjective(BotOwner owner, FollowerCombatCommon common);
        protected abstract FollowerCombatObjectiveBase GetObjective();
        protected virtual bool ShouldConsumePushCommand(pitTeam.Components.BotFollowerPlayer follower, EnemyInfo enemy) => false;
        protected virtual bool ShouldConsumeSuppressCommand(pitTeam.Components.BotFollowerPlayer follower, EnemyInfo enemy) => false;
        protected virtual bool ShouldConsumeNeedSniperCommand(pitTeam.Components.BotFollowerPlayer follower, EnemyInfo enemy) => false;
        public bool ProbePush(pitTeam.Components.BotFollowerPlayer follower) => ShouldConsumePushCommand(follower, new EnemyInfo());
        public bool ProbeSuppress(pitTeam.Components.BotFollowerPlayer follower) => ShouldConsumeSuppressCommand(follower, new EnemyInfo());
        public bool ProbeNeedSniper(pitTeam.Components.BotFollowerPlayer follower) => ShouldConsumeNeedSniperCommand(follower, new EnemyInfo());
    }
    public abstract class FollowerCombatObjectiveBase(BotOwner owner, FollowerCombatCommon common)
    {
        protected readonly BotOwner BotOwner = owner;
        protected readonly FollowerCombatCommon CombatCommon = common;
        public virtual void Reset() { } public virtual void Activate() { } public virtual void Deactivate() { }
        public abstract void StartDecision();
        public abstract AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision(EnemyInfo enemy);
        public abstract AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current);
        public abstract void DecisionChanged(AICoreActionResult<BotLogicDecision, CoreActionResultParams>? previous, AICoreActionResult<BotLogicDecision, CoreActionResultParams> next);
    }
    public class FollowerCombatCommon
    {
        public static bool SupportedGround=true;
        internal static bool HasSupportedCombatGround(Vector3 point)=>SupportedGround;
        public const string HealRetryHoldReason = "healRetry";
        public void ClearInitialDecision() { }
        public Vector3 BossPosition;public float BossNavDistance;public bool RecentFight,HoldAggression;
        public bool IsTemporaryHoldPositionAggressionActive()=>HoldAggression;
        public Vector3 GetBossPosition()=>BossPosition;
        public float GetBossNavDistance(Vector3 point)=>BossNavDistance;
        public static float GetSafeRegroupDistance(float nav,float direct)=>MathF.Max(nav,direct);
        public bool ShouldDeferAutonomousRegroupAfterRecentFight(EnemyInfo enemy,float distance,float trigger)=>RecentFight;
        public const string RecoveryNoCoverThreatHoldReason = "recoveryNoCoverThreatHold";
        public bool HealWork, MedicalUsing, SafeHeal, RecoveryPressure, Critical, RecoveryMove, RecoveryEnded;
        public List<string> Calls = new();
        public bool Reload, DogFight, Grenade, Medical, HealRetry, Position, Movement, Cover, Arrived,
            CoverShot, Immediate, NearbyPush, TeamSearch, PushCover, FiringPoint, Engagement, Settled,
            Danger, RecentHit, Admission = true, LeaderCover, Suppress;
        public bool LongGunPreparation, WeaponSettled, ReloadEnded;
        public bool LowThreat;
        public bool LauncherCooldown,LauncherFallback,LauncherTarget,CandidateCover,UnplannedLauncher;
        public bool BlockWeaponThreat;public int WeaponThreat;
        public bool ShouldBlockProactiveAutoPushForWeaponThreat(EnemyInfo enemy)=>BlockWeaponThreat;
        public int GetAutoPushWeaponThreatPolicy(EnemyInfo enemy)=>WeaponThreat;
        public void RequestLauncherPrimaryFallback(string reason) { if(UnplannedLauncher)LauncherFallback=true; }
        public bool TryCreatePendingLauncherPrimaryFallbackDecision(out AICoreActionResult<BotLogicDecision,CoreActionResultParams> decision)
            =>Emit(LauncherFallback,"launcherPrimaryFallback",BotLogicDecision.holdPosition,out decision);
        public bool IsGrenadeLauncherSuppressCooldownActive(bool ordered,out float remaining){remaining=0;return LauncherCooldown;}
        public bool HasPendingLauncherPrimaryFallback()=>LauncherFallback;
        public bool HasAutonomousGrenadeLauncherTarget(EnemyInfo enemy,out string reason){Calls.Add("launcherPlan");reason="";return LauncherTarget;}
        public static bool HasUsableEquippedGrenadeLauncher(BotOwner owner)=>owner.HasLauncher;
        public static Vector3 GetEnemyAnchor(EnemyInfo enemy)=>enemy.Anchor;
        public CustomNavigationPoint GetClosestShootCover(Vector3 center,float radius,float maxDistanceFromBot,bool avoidBossFireLane)
        {Calls.Add("shootCoverSearch");return CandidateCover?new(){Position=CandidatePosition}:null;}
        public Vector3 CandidatePosition=new(20,0,0);
        public bool IsWithinCommittedCoverArrivalHoldDistance(CustomNavigationPoint cover)=>cover!=null && cover.Position.sqrMagnitude<=4f;
        public bool TryCommitSelectedCombatCoverWithAction(EnemyInfo enemy,CustomNavigationPoint cover,string reason,BotLogicDecision action)
        {Calls.Add("commitSelectedCover");return cover!=null;}
        public AICoreActionResult<BotLogicDecision,CoreActionResultParams> CreateMemoryOnlyEnemySearchDecision(EnemyInfo enemy,string reason)
            =>D(SearchBlocked?BotLogicDecision.holdPosition:BotLogicDecision.goToEnemy,reason+(SearchBlocked?".completed":".walk"));
        public bool SearchBlocked;
        public bool GroupFollowPlan=true;
        public Vector3? GroupFollowPoint;
        public bool TryPlanCombatFollowDecision(EnemyInfo enemy,Vector3 leader,string reason,out Vector3 point,out AICoreActionResult<BotLogicDecision,CoreActionResultParams> decision)
        { point=GroupFollowPoint ?? leader;return Emit(GroupFollowPlan,reason,BotLogicDecision.goToPoint,out decision); }
        public bool ContactAdvance;
        public bool CanSprint = true;
        public bool CanSprintForCombatMovement() => CanSprint;
        public bool CanRunToEnemyNow() => CanSprint;
        public float AdvanceStop;
        public bool TryPlanContactAdvanceDecision(EnemyInfo enemy,float stop,string reason,out Vector3 point,out AICoreActionResult<BotLogicDecision,CoreActionResultParams> decision)
        { point=new(20,0,0); AdvanceStop=stop; decision=D(BotLogicDecision.goToPoint,reason); Calls.Add("planContactAdvance");return ContactAdvance && enemy.Known; }
        public void CommitContactAdvancePoint(Vector3 point) { Calls.Add("commitContactAdvance"); }
        public bool TryCreateContactAdvanceDecision(EnemyInfo enemy,float stop,string reason,out AICoreActionResult<BotLogicDecision,CoreActionResultParams> decision) { decision=D(BotLogicDecision.goToPoint,reason); Calls.Add("contactAdvance");return ContactAdvance; }
        public bool IsEnemyLowThreat(EnemyInfo e, bool ignoreEquip, float maximumEnemies)
        {
            if (ignoreEquip || maximumEnemies != 2f) throw new Exception("Knight threat admission drift");
            Calls.Add("lowThreat"); return LowThreat;
        }
        public bool TransitionAllowed=true;
        public bool TryPrepareAdmittedTargetTransition(AICoreActionResult<BotLogicDecision,CoreActionResultParams> current,
            string reason,EnemyInfo target,AICoreActionResult<BotLogicDecision,CoreActionResultParams> next)
            =>Admission && TryPrepareDecisionTransition(current,reason,next);
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams>? PreparedTransition;
        public pitTeam.Components.CombatEvents.PushEvent Push = new();
        public float HoldUntil;
        private static AICoreActionResult<BotLogicDecision, CoreActionResultParams> D(BotLogicDecision a, string s) => new(a,s);
        private bool Emit(bool value, string label, BotLogicDecision action, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> result)
        { Calls.Add(label); result = value ? D(action,label) : default; return value; }
        public void RefreshShootCover() { } public void ValidateCommittedCover() { }
        public bool TryGetReloadRetreatDecision(EnemyInfo e, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) => Emit(Reload,"reload",BotLogicDecision.runToCover,out d);
        public bool TryGetCombatLongGunPreparationDecision(EnemyInfo e, bool orderedPush, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> d)
        {
            if (orderedPush) throw new Exception("Knight's ordinary recovery cannot force an ordered push");
            return Emit(LongGunPreparation, "weaponSwitchLongGun.pistolReload", BotLogicDecision.holdPosition, out d);
        }
        public static bool IsReloadHoldReason(string reason) => reason is "reloadLongGun" or "reloadInCover" or "reloadNoCover";
        public static bool IsWeaponPreparationHoldReason(string reason) => reason?.StartsWith("weaponSwitchLongGun") == true || reason?.StartsWith("weaponPrepareLongGun") == true;
        public AICoreActionEnd EndReloadHold(AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) { Calls.Add("reloadEnd"); return new("reloadEnd", ReloadEnded); }
        public AICoreActionEnd EndWeaponPreparationHold(string reason) { Calls.Add("weaponEnd"); return new("weaponEnd", WeaponSettled); }
        public bool TryPrepareDecisionTransition(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current, string reason, AICoreActionResult<BotLogicDecision, CoreActionResultParams> next)
        { Calls.Add("prepareTransition"); if(!TransitionAllowed)return false;PreparedTransition = next; return true; }
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams>? TryGetDogFightDecision() => DogFight ? D(BotLogicDecision.dogFight,"dogfight") : null;
        public bool TryGetCommittedGrenadeDecision(out AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) => Emit(Grenade,"grenade",BotLogicDecision.suppressGrenade,out d);
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams>? TryGetNeedHealDecision() { Calls.Add("medicalProvider"); return Medical ? D(BotLogicDecision.heal,"medical") : null; }
        public bool IsHealDecisionRetryBlocked => HealRetry;
        public bool IsActivelyUsingMedical => MedicalUsing;
        public bool HasReachedSafeHealPosition(EnemyInfo e) => SafeHeal;
        public bool HasRecoveryPressure(float seconds) => RecoveryPressure;
        public bool IsFollowerCriticallyWounded() => Critical;
        public bool TryGetMedicalRetryRecoveryDecision(EnemyInfo e, bool retryWasDue, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> d)
        {
            d = default;
            if (MedicalUsing || !HasActiveOrPendingHealWork() || SafeHeal || (!retryWasDue && !RecoveryPressure && !Critical)) return false;
            if (!TryGetCommittedRecoveryDecision(e, out d)) d = CreateNoCoverRecoveryDecision(e);
            return true;
        }
        public bool TryGetCommittedRecoveryDecision(EnemyInfo e, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> d)
            => Emit(RecoveryMove,"medicalRecoveryCover",BotLogicDecision.runToCover,out d);
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams> CreateNoCoverRecoveryDecision(EnemyInfo e)
            => D(BotLogicDecision.holdPosition,RecoveryNoCoverThreatHoldReason);
        public AICoreActionEnd EndRecoveryNoCoverThreatHold() { Calls.Add("recoveryEnd"); return new("recoveryEnd",RecoveryEnded); }
        public bool ShouldHoldForHealRetry() => HealRetry;
        public bool HasCommittedPosition(out AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) => Emit(Position,"position",BotLogicDecision.holdPosition,out d);
        public bool TryGetCommittedMovementDecision(EnemyInfo e, bool r, bool p, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) => Emit(Movement,"movement",BotLogicDecision.runToCover,out d);
        public bool HasCommittedCover() => Cover; public bool IsBotInCommittedCover() => Arrived;
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams> CreateCommittedCoverMoveDecision() => D(BotLogicDecision.runToCover,"committedCover");
        public bool CanShootFromCurrentCover(out string reason) { reason = "lane"; return CoverShot; }
        public void ClearCommittedCover(string reason) { Cover=false; Calls.Add("clearCover"); }
        public bool TryUseSupportGoalEnemy(BotOwner enemy, string reason, out EnemyInfo threat) { Calls.Add("admitLeaderThreat"); threat = Admission ? new() : null; return Admission; }
        public bool HasActiveCombatEnemy(EnemyInfo enemy = null) => enemy == null || enemy.Person.HealthController.IsAlive;
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams>? TryGetImmediateShootDecision(string reason) => Immediate ? D(BotLogicDecision.shootFromPlace,reason) : null;
        public bool TryCreateSuppressFromPlaceDecision(EnemyInfo e, string r, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> d, bool allowSoftObstructedSuppression) => Emit(Suppress,"leaderSuppress",BotLogicDecision.suppressFire,out d);
        public bool TryCommitSupportFiringCover(EnemyInfo e, string r, out string reason, bool preferBackline, float maxSearchRadius) { reason=r; return LeaderCover; }
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams> CreateMoveToCommittedCoverDecision(string reason) => D(BotLogicDecision.runToCover,reason);
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams> CreateRegroupObjectiveDecision() => D(BotLogicDecision.holdPosition,"regroup");
        public bool HasActiveOrPendingHealWork() => Medical || HealWork || HealRetry || MedicalUsing; public bool HasImmediateExplosiveDanger() => Danger;
        public static bool WasHitRecently(BotOwner bot, float seconds) => RecentHitProbe;
        public static bool RecentHitProbe;
        public bool HasCommittedHolderSettled(float seconds) => Settled;
        public static bool IsMedicalDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) => d.Action == BotLogicDecision.heal || d.Reason==HealRetryHoldReason;
        public static AICoreActionEnd Continue() => new("continue",false);
        public float CommittedUntil; public bool CommittedInvalid;
        public bool IsCommittedHolderReason(string r)=>r!=null && (r.StartsWith("committedCoverHold") || r.StartsWith("committedPositionHold"));
        public AICoreActionEnd EndCommittedPositionHold(AICoreActionResult<BotLogicDecision,CoreActionResultParams> current)
        {Calls.Add("committedHoldEnd");return new("committedHoldComplete",CommittedInvalid || Time.time>=CommittedUntil);}
        public AICoreActionEnd EndBaseHoldPosition(string r) {Calls.Add("baseHoldEnd");return new("hold",Time.time >= HoldUntil);}
        public AICoreActionEnd? SharedEndResult;
        public AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) { Calls.Add("sharedEnd"); return SharedEndResult ?? Continue(); }
        public void HandleSharedDecisionChanged(AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) { }
        public void HandleCommittedCoverDecisionChanged(AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) { }
        public void HandleFollowerSuppressDecisionChanged(AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) { }
        public bool ShouldCommitMovementDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> d,bool push) => false;
        public void CommitMovement(AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) { }
        public bool IsSameCommittedMovement(AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) => true;
        public void ClearCommittedMovement(string reason = null) { }
        public void ClearCommittedPosition(string reason = null) { }
        public void UpdateRecoveryNoCoverCommitment(AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) { }
        public void HoldFor(float seconds) => HoldUntil=Time.time+seconds;
        public bool TryActivateFollowerGrenade(EnemyInfo e,out AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) => Emit(false,"newGrenade",BotLogicDecision.suppressGrenade,out d);
        public bool TryGetNearbyActivePushEventForCurrentEnemy(float straight,float nav,out pitTeam.Components.CombatEvents.PushEvent push)
        { if(straight!=45 || nav!=65) throw new Exception("support distance drift"); Calls.Add("pushEvent"); push=Push; return NearbyPush; }
        public void ExtendCommittedCover() => Calls.Add("extendCover");
        public bool TryCreateTeamSearchSupportDecision(pitTeam.Components.CombatEvents.PushEvent p,EnemyInfo e,string reason,out AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) => Emit(TeamSearch,"teamSearch",BotLogicDecision.attackMoving,out d);
        public bool TryCommitPushSupportCover(EnemyInfo e, Vector3 p,Vector3 enemy,Vector3 dest,string r,out string reason) { Calls.Add("pushCover"); reason=r; return PushCover; }
        public static bool IsFinite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        public bool TryCreateSupportFiringPositionDecision(EnemyInfo e, Vector3 anchor,string r,out AICoreActionResult<BotLogicDecision, CoreActionResultParams> d,bool preferBackline,bool enforceMarksmanPositionPolicy) => Emit(FiringPoint,"supportPosition",BotLogicDecision.attackMoving,out d);
        public bool AssignedBackline, AssignedSniper;
        public Vector3 AssignedPoint;
        public bool TryPlanAssignedFiringPositionDecision(EnemyInfo e, Vector3 point,string r,out Vector3 position,out AICoreActionResult<BotLogicDecision,CoreActionResultParams> d,bool preferBackline,bool enforceMarksmanPositionPolicy)
        { position=point;return TryCreateAssignedFiringPositionDecision(e,point,r,out d,preferBackline,enforceMarksmanPositionPolicy); }
        public void ClearCommittedPushDecision(string reason) { }
        public bool TryCreateAssignedFiringPositionDecision(EnemyInfo e, Vector3 point,string r,out AICoreActionResult<BotLogicDecision, CoreActionResultParams> d,bool preferBackline,bool enforceMarksmanPositionPolicy)
        { AssignedPoint=point;AssignedBackline=preferBackline;AssignedSniper=enforceMarksmanPositionPolicy;return Emit(FiringPoint,"assignedPosition",BotLogicDecision.goToPoint,out d); }
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams>? TryGetAllyEngagementSupportDecision()
        { Calls.Add("engagement"); return Engagement ? D(BotLogicDecision.runToCover,"allyEngagement") : null; }
    }
    public static class FollowerCombatGrenadierObjective
    {
        public const float OpportunityWindowSeconds=5;
        public static AICoreActionResult<BotLogicDecision,CoreActionResultParams> CreateAutonomousActivationDecision()
            =>new(BotLogicDecision.holdPosition,"objectiveGrenadier.activateAuto");
    }
}
namespace pitTeam.GoingRogue.Combat
{
    public static class KnightSquadCoordinator
    {
        public static bool Assigned; public static Vector3 Point;
        public static bool TryGetMemberPosition(BotOwner owner,out Vector3 position){position=Point;return Assigned;}
        public static bool FreshDirective;
        public static bool TryGetPushOverride(BotOwner owner,out EnemyInfo enemy,out Vector3 point){enemy=owner.Memory.GoalEnemy;point=Point;return FreshDirective;}
    }
    public static class GoonPushLeadership
    {
        public static int Completed, IntentClears; public static bool AdvanceAllowed=true, PublishedAdvance;public static string PublishedTarget;
        public static void Complete(BotOwner member) { Completed++;KnightSquadCoordinator.FreshDirective=false; }
        public static void ClearAutonomousIntent(BotOwner owner) { IntentClears++;PublishedTarget=null; }
        public static bool GroupSearch; public static BotOwner SearchLeader=new(){Position=new Vector3(20,0,0)};
        public static bool TryGetSearchLeader(BotOwner member,EnemyInfo enemy,out BotOwner leader){leader=SearchLeader;return GroupSearch;}
        public static void PublishAutonomousIntent(BotOwner owner,string target,bool advance,bool searching=false) { PublishedTarget=target;PublishedAdvance=advance; }
        public static bool CanAutonomousAdvance(BotOwner owner,EnemyInfo target)=>AdvanceAllowed;
    }
    public static class KnightCombatLayer { public const int LayerPriority = 76; }
    internal sealed partial class KnightVanillaCombat : IGoonCombatSelector
    {
        private readonly BotOwner _owner;
        private pitTeam.BigBrain.FollowerCombatCommon common;
        public KnightVanillaCombat(BotOwner bot, int priority) { _owner = bot; }
        public static int Decisions, Begins, Disposals;
        public BotLogicDecision? LastSelectedDecision { get; set; }
        public float RetryUntil { get; private set; } = 101f;
        public void Begin(pitTeam.BigBrain.FollowerCombatCommon c) { common = c; Begins++; }
        public void Dispose() => Disposals++;
        public void InvalidatePosition() { }
        public AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision() { Decisions++; return new(BotLogicDecision.attackMoving,"vanilla"); }
        public AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> d) => new("nativeEnd",false);
    }
}
