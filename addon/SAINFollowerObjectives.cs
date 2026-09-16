using EFT;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;

namespace pitTeam.SAINAddon;

// Objectives own intent across native actions. The native manager still calculates
// and publishes each decision once; urgent work and target perception remain native.
internal sealed class SAINFollowerObjectives(BotComponent bot, SAINFollowerRegroupObjective regroup)
{
    internal SAINFollowerPushObjective Push { get; } = new(bot);
    internal string Current => regroup.Active ? "Regroup" : Push.Active ? "Push" : "NativeCombat";
    internal void Observe()
    {
        // Observe replacement commands before regroup consumes them.
        Push.Observe();
        regroup.Observe();
    }
    internal bool Filter(Enemy enemy, ECombatDecision solo, ESquadDecision squad, ESelfActionType self,
        out ECombatDecision nextSolo, out ESquadDecision nextSquad)
    {
        nextSolo = solo; nextSquad = squad;
        Observe();
        if (Push.GetDecision(enemy, solo, squad, self, regroup.Active, out nextSolo))
        {
            nextSquad = ESquadDecision.None;
            // Like core Rifleman, rejected automatic advancement may hold locally or regroup.
            // Assessing has no committed advance; the normal regroup gates still protect
            // native cover travel/arrival, visible contact, recovery, orders and independence.
            if (!Push.Ordered && (Push.Exhausted || Push.Phase == SAINPushPhase.Assessing) &&
                regroup.TryBeginAuto(enemy, nextSolo, nextSquad, self))
            { nextSolo = ECombatDecision.None; nextSquad = ESquadDecision.Regroup; }
            return true;
        }
        if (SAINFollowerRuntime.GetCover(bot.BotOwner)?.TryHoldDecision(enemy, solo, squad, self) == true)
        { nextSolo = ECombatDecision.SeekCover; return true; }
        if (regroup.TryBeginAuto(enemy, solo, squad, self))
        { nextSolo = ECombatDecision.None; nextSquad = ESquadDecision.Regroup; return true; }
        if (solo == ECombatDecision.MoveToEngage && squad == ESquadDecision.None && self == ESelfActionType.None &&
            SAINFollowerRuntime.GetEngageAttempt(bot.BotOwner)?.FailedFor(enemy) == true)
        { nextSolo = ECombatDecision.SeekCover; return true; }
        return false;
    }
    internal void Clear(string reason) => Push.Clear(reason);
    internal object Snapshot => new { current = Current, push = Push.Snapshot };
}
