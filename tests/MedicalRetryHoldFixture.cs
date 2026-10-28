using System;
using System.Collections.Generic;

public enum BotLogicDecision { holdPosition, heal, runToCover, shootFromPlace, dogFight }
public class CoreActionResultParams { }
public struct AICoreActionResult<T, D>
{
    public T Action; public string Reason;
    public AICoreActionResult(T action, string reason) { Action = action; Reason = reason; }
}
public struct AICoreActionEnd
{
    public string Reason; public bool Value;
    public AICoreActionEnd(string reason, bool value) { Reason = reason; Value = value; }
}
public class EnemyInfo { }
public class Memory { public EnemyInfo GoalEnemy = new EnemyInfo(); }
public class BotOwner { public Memory Memory = new Memory(); }
public class FollowerCombatCommon
{
    public bool IsHealDecisionRetryBlocked, IsActivelyUsingMedical, HaveWork = true,
        Safe, Pressure, Critical, Ready, RecoveryMove = true, Immediate, DogFight;
    public int Probes, CoverSearches;
    public AICoreActionResult<BotLogicDecision, CoreActionResultParams>? Prepared;
    public bool HasActiveCombatEnemy(EnemyInfo enemy) => enemy != null;
    public bool HasActiveOrPendingHealWork() => HaveWork || IsActivelyUsingMedical;
    public bool HasReachedSafeHealPosition(EnemyInfo enemy) => Safe;
    public bool HasRecoveryPressure(float seconds) => Pressure;
    public bool IsFollowerCriticallyWounded() => Critical;
    public AICoreActionResult<BotLogicDecision, CoreActionResultParams>? TryGetNeedHealDecision()
    {
        Probes++;
        if (Ready) return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.heal,"treat");
        if (HaveWork) IsHealDecisionRetryBlocked = true; // Real failed-search cooldown behavior.
        return null;
    }
    public bool TryGetCommittedRecoveryDecision(EnemyInfo enemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
    {
        CoverSearches++;
        decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.runToCover,"recoveryCover");
        return RecoveryMove;
    }
    public AICoreActionResult<BotLogicDecision, CoreActionResultParams> CreateNoCoverRecoveryDecision(EnemyInfo enemy)
        => new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition,"recoveryNoCoverThreatHold");
    public AICoreActionResult<BotLogicDecision, CoreActionResultParams>? TryGetImmediateShootDecision(string reason)
        => Immediate ? new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace,reason) : null;
    public AICoreActionResult<BotLogicDecision, CoreActionResultParams>? TryGetDogFightDecision()
        => DogFight ? new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.dogFight,"dogfight") : null;
    public bool TryPrepareDecisionTransition(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current, string reason,
        AICoreActionResult<BotLogicDecision, CoreActionResultParams> next) { Prepared = next; return true; }
    public static AICoreActionEnd Continue() => new AICoreActionEnd("continue",false);
    /* COMMON RECOVERY */
}
public class RiflemanRetry
{
    public readonly FollowerCombatCommon combatCommon;
    public readonly BotOwner botOwner = new BotOwner();
    public RiflemanRetry(FollowerCombatCommon c) { combatCommon = c; }
    private bool TryGetHealDecision(out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
    { var value = combatCommon.TryGetNeedHealDecision(); decision = value.GetValueOrDefault(); return value.HasValue; }
    private bool TryGetImmediateFightDecision(out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
    { var value = combatCommon.TryGetImmediateShootDecision("immediate"); decision = value.GetValueOrDefault(); return value.HasValue; }
    private bool TryGetBossUnderAttackDecision(EnemyInfo enemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
    { decision = default; return false; }
    public AICoreActionEnd Run() => EndMedicalRetryHold(default);
    /* RIFLEMAN END */
}
public class MarksmanRetry
{
    public readonly FollowerCombatCommon CombatCommon;
    public readonly BotOwner BotOwner = new BotOwner();
    public MarksmanRetry(FollowerCombatCommon c) { CombatCommon = c; }
    private bool TryGetBossUnderAttackDecision(EnemyInfo enemy, out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
    { decision = default; return false; }
    public AICoreActionEnd Run() => EndMedicalRetryHold(default);
    /* MARKSMAN END */
}
public class KnightRetry
{
    private readonly string prefix = "knight.";
    public readonly FollowerCombatCommon CombatCommon;
    public readonly BotOwner BotOwner = new BotOwner();
    public KnightRetry(FollowerCombatCommon c) { CombatCommon = c; }
    public AICoreActionEnd Run() => EndMedicalRetryHold(default);
    /* KNIGHT END */
}
public static class MedicalRetryHoldChecks
{
    public static int Run()
    {
        int checks = 0;
        foreach (string brain in new[] { "Rifleman", "Marksman", "Knight" })
        foreach (string scenario in new[] { "ready", "quietFailed", "blockedQuiet", "blockedPressure", "safeBlocked",
            "safeFailed", "safeReady", "activeBlocked", "cleared", "noCover", "critical" })
        {
            var c = new FollowerCombatCommon();
            c.Ready = scenario == "ready" || scenario == "safeReady";
            c.IsHealDecisionRetryBlocked = scenario == "blockedQuiet" || scenario == "blockedPressure" ||
                scenario == "safeBlocked" || scenario == "activeBlocked" || scenario == "critical";
            c.Safe = scenario.StartsWith("safe", StringComparison.Ordinal);
            c.Pressure = scenario == "blockedPressure";
            c.Critical = scenario == "critical";
            c.IsActivelyUsingMedical = scenario == "activeBlocked";
            c.HaveWork = scenario != "cleared";
            c.RecoveryMove = scenario != "noCover";
            AICoreActionEnd end = brain == "Rifleman" ? new RiflemanRetry(c).Run() :
                brain == "Marksman" ? new MarksmanRetry(c).Run() : new KnightRetry(c).Run();
            Action<bool,string> check = (ok,why) => { checks++; if (!ok) throw new Exception(brain+"/"+scenario+": "+why); };
            check(c.Probes <= 1,"medical provider has a single owner per evaluation");
            if (c.Ready) check(end.Value && c.Prepared?.Action == BotLogicDecision.heal && c.CoverSearches == 0,"ready treatment precedes recovery");
            else if (scenario == "cleared") check(end.Value && !c.Prepared.HasValue,"completed medical work releases retry hold");
            else if (scenario == "blockedQuiet" || c.Safe || c.IsActivelyUsingMedical)
                check(!end.Value && !c.Prepared.HasValue && c.CoverSearches == 0,"safe/cooldown/active treatment cannot be replaced by movement");
            else
            {
                check(end.Value && c.CoverSearches == 1,"exposed failed retry selects one concrete recovery");
                check(c.Prepared?.Reason == (scenario == "noCover" ? "recoveryNoCoverThreatHold" : "recoveryCover"),"shared cover or bounded fallback owns successor");
                if (scenario == "quietFailed") check(c.IsHealDecisionRetryBlocked,"captured due retry survives provider rearming cooldown");
            }
        }
        return checks;
    }
}
