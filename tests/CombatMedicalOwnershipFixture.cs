using System;

public enum EBotState { Active, Disposed }
public enum BotLogicDecision { heal, healStimulators, holdPosition, runToCover, runToEnemy }
public class CoreActionResultParams { }
public struct AICoreActionResult<T, D>
{
    public T Action; public string Reason;
    public AICoreActionResult(T action, string reason) { Action = action; Reason = reason; }
}
public struct AICoreActionEnd
{
    public bool Value; public string Reason;
    public AICoreActionEnd(string reason, bool value) { Reason = reason; Value = value; }
}
public static class Time { public static float time; }
public sealed class Health { public bool IsAlive = true; }
public sealed class Player { public Health HealthController = new Health(); }
public class pitAIBossPlayer { }
public sealed class Follower { public bool HaveBoss = true; public object BossToFollow = new pitAIBossPlayer(); }
public sealed class MedicalUse { public bool Using, Have2Do, HaveWork; }
public sealed class Medicine
{
    public MedicalUse FirstAid = new MedicalUse(), SurgicalKit = new MedicalUse(), Stimulators = new MedicalUse();
}
public sealed class RunController { public void EndMove() { } }
public sealed class BotOwner
{
    public EBotState BotState = EBotState.Active;
    public Player GetPlayer = new Player();
    public Follower BotFollower = new Follower();
    public Medicine Medecine = new Medicine();
    public RunController BotRun = new RunController();
    public bool IsFollower = true, Sain;
}
public static class BossPlayers { public static bool IsFollower(BotOwner owner) { return owner.IsFollower; } }
public static class pitFireTeam { public static bool UseSainFollowerCombat(BotOwner owner) { return owner.Sain; } }
public static class FollowerRecovery { public static void CheckReloadTimeout(BotOwner owner) { } }
public static class FollowerEnemyTracking { public static void Update(BotOwner owner) { } }
public static class FollowerCombatCommon
{
    public static bool IsMedicalDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
    { return decision.Action == BotLogicDecision.heal || decision.Action == BotLogicDecision.healStimulators; }
}
public static class BattleRecorder
{
    public static void RecordDecisionEnd(BotOwner owner, AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision,
        AICoreActionEnd end, string objective) { }
}
namespace Utils
{
    public static class FollowerMedical
    {
        /* PRODUCTION MEDICAL USE */
    }
}
public sealed class CombatLogic
{
    public bool Pending, EndTreatment, Explosive;
    public int EndChecks;
    public bool HasActiveOrPendingHealWork() { return Pending; }
    public bool HasImmediateExplosiveDanger() { return Explosive; }
    public string GetCurrentObjectiveName() { return "fixture"; }
    public AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
    { EndChecks++; return new AICoreActionEnd { Value = EndTreatment }; }
}
public abstract class Layer
{
    public abstract bool IsActive();
    public abstract bool IsCurrentActionEnding();
}
public sealed class CombatMedicalOwnershipFixture : Layer
{
    private const float PostEnemyKeepActiveSeconds = 3f;
    private const float PostCombatFirstAidKeepActiveSeconds = 7f;
    private const float PostCombatSurgeryKeepActiveSeconds = 20f;
    private const string LingerReason = "linger", LingerExpiredReason = "lingerExpired";
    private BotOwner BotOwner = new BotOwner();
    private CombatLogic combatLogic = new CombatLogic();
    private object addonLogicFactory;
    private bool hadCombatSinceActivation, lingerArmed, medicalKeepActiveTimedOut;
    private float lingerUntil, lingerHardUntil, medicalKeepActiveStartedAt;
    private AICoreActionResult<BotLogicDecision, CoreActionResultParams>? currentDecision;
    private bool forceRelease, liveEnemy, request;
    private int releases;
    private bool TryConsumeForceReleaseRequest(out string reason) { reason = "explicit"; bool result = forceRelease; forceRelease = false; return result; }
    private void CompletePostCombatLinger(string reason, bool allowRetainedLayerRearm = false) { releases++; ClearLinger(); }
    private bool HasCurrentLiveGoalEnemy() { return liveEnemy; }
    private bool TryKeepActiveForOrderedPush() { return false; }
    private bool ShouldTreatCombatAsActive() { return liveEnemy; }
    private bool ShouldYieldPostCombatLingerToRequestCommand() { return request && !ShouldKeepCombatLayerForMedicalWork(); }
    private bool IsMovementOrPushDecision(BotLogicDecision decision) { return decision == BotLogicDecision.runToCover; }

    /* PRODUCTION LAYER METHODS */

    private static int checks;
    private static void Check(bool result, string message)
    { checks++; if (!result) throw new Exception(message); }
    private static CombatMedicalOwnershipFixture New(bool participated = true)
    { Time.time = 100f; return new CombatMedicalOwnershipFixture { hadCombatSinceActivation = participated }; }
    private void Heal(bool stim = false)
    { currentDecision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(stim ? BotLogicDecision.healStimulators : BotLogicDecision.heal, "healInCover"); }
    public static void Run()
    {
        // Reproduce the actual 20-second release while surgery was still using.
        foreach (string type in new[] { "firstAid", "surgery", "stim" })
        {
            var f = New(); f.Heal(type == "stim");
            MedicalUse use = type == "firstAid" ? f.BotOwner.Medecine.FirstAid : type == "surgery" ? f.BotOwner.Medecine.SurgicalKit : f.BotOwner.Medecine.Stimulators;
            use.Using = true;
            foreach (float age in new[] { 0f, 8f, 21f, 46f, 91f })
            {
                Time.time = 100f + age;
                Check(f.IsActive(), type + " retains combat layer beyond handoff deadline " + age);
                Check(!f.IsCurrentActionEnding(), type + " preserves running action " + age);
                Check(f.ShouldKeepCombatLayerForMedicalWork(), type + " retains action routing " + age);
            }
            Check(f.releases == 0 && f.combatLogic.EndChecks == 5, type + " action end owner retained");
            use.Using = false; f.combatLogic.EndTreatment = true;
            Check(f.IsCurrentActionEnding(), type + " completed/cancelled controller permits normal end");
            Check(f.IsActive(), type + " ordinary linger begins after treatment");
            Time.time += 3.1f;
            Check(!f.IsActive() && f.releases == 1, type + " releases after completed treatment linger");
        }
        foreach (bool surgery in new[] { false, true })
        {
            var f = New(); f.BotOwner.Medecine.FirstAid.Have2Do = !surgery; f.BotOwner.Medecine.SurgicalKit.HaveWork = surgery;
            Check(f.ShouldKeepCombatLayerForMedicalWork(), "pending medical work gets original window");
            Time.time += surgery ? 20.1f : 7.1f;
            Check(!f.ShouldKeepCombatLayerForMedicalWork() && f.medicalKeepActiveTimedOut, "pending work still times out");
            Time.time += 30f;
            Check(!f.ShouldKeepCombatLayerForMedicalWork(), "stale pending flag cannot rearm window");
            f.Heal(); f.BotOwner.Medecine.SurgicalKit.Using = true;
            Check(f.IsActive() && f.ShouldKeepCombatLayerForMedicalWork(), "actual treatment wins even after pending timeout");
            f.BotOwner.Medecine.SurgicalKit.Using = false;
            Check(!f.ShouldKeepCombatLayerForMedicalWork(), "stale pending latch remains bounded after treatment");
        }
        {
            var f = New(); f.Heal(); f.BotOwner.Medecine.SurgicalKit.Using = true;
            f.lingerArmed = true; f.lingerUntil = f.lingerHardUntil = 99f;
            Check(f.IsActive() && !f.lingerArmed && f.releases == 0, "expired linger cannot preempt a running treatment");
            f.request = true;
            Check(f.IsActive() && !f.IsCurrentActionEnding(), "peaceful request cannot break treatment at enemy loss");
            f.combatLogic.EndTreatment = true;
            Check(f.IsCurrentActionEnding(), "tactical cancellation still belongs to action end handler");
            f.forceRelease = true;
            Check(!f.IsActive() && f.releases == 1, "explicit lifecycle release remains authoritative");
        }
        {
            var f = New(false); f.BotOwner.Medecine.FirstAid.Using = true;
            Check(!f.IsActive() && !f.ShouldKeepCombatLayerForMedicalWork(), "peaceful medicine cannot activate unrelated combat layer");
            f = New(); f.Heal(); f.BotOwner.Medecine.SurgicalKit.Using = true; f.BotOwner.IsFollower = false;
            Check(!f.IsActive(), "dismissal still releases layer");
            f.BotOwner.IsFollower = true; f.BotOwner.GetPlayer.HealthController.IsAlive = false;
            Check(!f.IsActive(), "death still releases layer");
            f.BotOwner.GetPlayer.HealthController.IsAlive = true; f.BotOwner.BotState = EBotState.Disposed;
            Check(!f.IsActive(), "disposed bot cannot retain layer");
            f = New(); f.Heal(); f.BotOwner.Medecine.SurgicalKit.Using = true; f.BotOwner.Sain = true;
            Check(!f.IsActive(), "ready SAIN owner still excludes Core fallback");
            f.addonLogicFactory = new object();
            Check(f.IsActive(), "injected Goon driver uses same retention boundary");
        }
        Console.WriteLine("Passed " + checks + " production combat medical ownership checks. Unity medical callbacks still require live qualification.");
    }
}
