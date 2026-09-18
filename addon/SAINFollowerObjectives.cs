using EFT;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes.Decision;

namespace pitTeam.SAINAddon;

// Objectives own intent across native actions. The native manager still calculates
// and publishes each decision once; urgent work and target perception remain native.
internal sealed class SAINFollowerObjectives
{
    private readonly BotComponent bot;
    private readonly SAINFollowerRegroupObjective regroup;
    internal SAINFollowerPushObjective Push { get; }
    internal SAINFollowerRelocationObjective Relocation { get; }
    internal SAINFollowerMarksmanObjective? Marksman { get; }
    internal SAINFollowerSquadSupportObjective SquadSupport { get; }
    internal SAINFollowerObjectives(BotComponent bot, SAINFollowerRegroupObjective regroup)
    {
        this.bot = bot; this.regroup = regroup;
        // Native Clear preserves its two-second timer: competing intents share one scan budget.
        var finder = new FiringPositionFinder(bot);
        Push = new(bot); Relocation = new(bot);
        Marksman = SainAddonBridge.IsShooterSelected(bot.BotOwner) ? new(bot, finder) : null;
        SquadSupport = new(bot, finder);
    }
    internal string Current => SquadSupport.Active ? SquadSupport.Mode.ToString() : Relocation.Active ? "Relocation" : regroup.Active ? "Regroup" : Marksman != null ? "Marksman" : Push.Active ? "Push" : "NativeCombat";
    internal void Observe()
    {
        // Observe replacement commands before regroup consumes them.
        SquadSupport.Observe();
        Marksman?.Observe();
        Push.Observe();
        Relocation.Observe();
        regroup.Observe();
    }
    internal bool Filter(Enemy enemy, ECombatDecision solo, ESquadDecision squad, ESelfActionType self,
        out ECombatDecision nextSolo, out ESquadDecision nextSquad)
    {
        // Cover cancellation/command observers run before native publication. Guard
        // their weapon cleanup with the incoming decision as well as native live state.
        Marksman?.Weapons.SetDecisionContext(solo, self);
        try { return FilterDecisions(enemy, solo, squad, self, out nextSolo, out nextSquad); }
        finally { Marksman?.Weapons.ClearDecisionContext(); }
    }
    private bool FilterDecisions(Enemy enemy, ECombatDecision solo, ESquadDecision squad, ESelfActionType self,
        out ECombatDecision nextSolo, out ESquadDecision nextSquad)
    {
        nextSolo = solo; nextSquad = squad;
        Observe();
        if (Relocation.GetDecision(enemy, solo, self, out nextSolo))
        { nextSquad = ESquadDecision.None; return true; }
        if (SquadSupport.Filter(enemy, solo, squad, self, out nextSquad))
        { nextSolo = ECombatDecision.None; return true; }
        if (Marksman != null)
        {
            // Regroup remains a fallback at a passive boundary; it cannot cancel an owned leg.
            if (!Marksman.OwnsMovement && !Marksman.Preparing && regroup.TryBeginAuto(enemy, solo, squad, self))
            { Marksman.Clear("regroup"); nextSolo = ECombatDecision.None; nextSquad = ESquadDecision.Regroup; return true; }
            bool handled = Marksman.Filter(enemy, solo, squad, self, regroup.Active, out nextSolo, out nextSquad);
            if (handled && nextSolo == ECombatDecision.SeekCover &&
                regroup.TryBeginAuto(enemy, nextSolo, nextSquad, self))
            { Marksman.Clear("regroup"); nextSolo = ECombatDecision.None; nextSquad = ESquadDecision.Regroup; }
            return handled;
        }
        if (Push.GetDecision(enemy, solo, squad, self, regroup.Active, out nextSolo))
        {
            nextSquad = ESquadDecision.None;
            // Like core Rifleman, rejected advancement may hold locally or regroup.
            // Assessing has no committed advance; the normal regroup gates still protect
            // native cover travel/arrival, useful fire, recovery and independence.
            // TryBeginAuto also protects unfinished orders; only an exhausted order may yield.
            if ((Push.Exhausted || Push.Phase == SAINPushPhase.Assessing) &&
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
    internal void Clear(string reason) { Push.Clear(reason); Relocation.Clear(reason); Marksman?.Clear(reason); SquadSupport.Clear(reason); }
    internal object Snapshot => new { current = Current, push = Push.Snapshot, relocation = Relocation.Snapshot, marksman = Marksman?.Snapshot, squadSupport = SquadSupport.Snapshot };
}
