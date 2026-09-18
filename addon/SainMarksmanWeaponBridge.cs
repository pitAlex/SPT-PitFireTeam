using System;
using System.Collections.Generic;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using SAIN.Preset.Shared.Enums;
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.SAINAddon;

// Closed delegates bind once per Shooter. Reuse Core weapon eligibility/readiness and
// risk predicates only: never execute a Core decision, perception or geometry planner.
internal sealed class SainMarksmanWeaponBridge
{
    private readonly BotComponent bot;
    private readonly Func<bool> available, ready, request, supportReady, supportRequest, supportAvailable, restore, usingSupport, settled;
    private readonly Func<bool> hold, medical;
    private readonly Func<float> aggression;
    private readonly Func<EnemyInfo, bool> blocked, cautious;
    private readonly Func<EnemyInfo, float, bool> inRange;
    private readonly Func<BotOwner, EnemyInfo, bool> close;
    private readonly Func<float, int> allowedCount;
    private readonly HashSet<string> counted = new();
    private float nextAssessment, prepareUntil, retryAt, settleUntil;
    private string assessedEnemy;
    private bool eligible, pending, owned, orderedPreparation, decisionProtected;
    internal bool Preparing => pending || settleUntil > 0f;
    internal string State { get; private set; } = "idle";
    internal bool SupportAvailable => supportAvailable();
    internal bool SupportReady => supportReady();
    internal SainMarksmanWeaponBridge(BotComponent bot)
    {
        this.bot = bot;
        var assembly = typeof(pitFireTeam).Assembly;
        Type commonType = assembly.GetType("pitTeam.BigBrain.FollowerCombatCommon", true);
        object common = Activator.CreateInstance(commonType, bot.BotOwner);
        Type sniperType = assembly.GetType("pitTeam.BigBrain.FollowerCombatSniper", true);
        object sniper = Activator.CreateInstance(sniperType, bot.BotOwner, common);
        available = Bind<Func<bool>>(common, "HasAutomaticCloseCombatWeaponAvailable");
        ready = Bind<Func<bool>>(common, "IsAutomaticCloseCombatWeaponReady");
        request = Bind<Func<bool>>(common, "TryRequestAutomaticSupportForCloseCombat");
        supportReady = Bind<Func<bool>>(common, "IsEligibleAutomaticMarksmanSupportSelectedAndReady");
        settled = Bind<Func<bool>>(common, "IsWeaponSelectionSettledForAutomaticMarksmanSupportRequest");
        supportRequest = Bind<Func<bool>>(common, "TryRequestEligibleAutomaticMarksmanSupport");
        supportAvailable = Bind<Func<bool>>(common, "HasLoadedAutomaticMarksmanSupportWeapon");
        restore = Bind<Func<bool>>(common, "TrySwitchBackToPrimaryFromAutomaticMarksmanSupport");
        usingSupport = Bind<Func<bool>>(common, "IsUsingAutomaticMarksmanSupportOverNonAutomaticPrimary");
        hold = Bind<Func<bool>>(common, "IsTemporaryHoldPositionAggressionActive");
        medical = Bind<Func<bool>>(common, "HasReportedHealWorkForPush");
        aggression = Bind<Func<float>>(common, "GetAggression01");
        blocked = Bind<Func<EnemyInfo, bool>>(common, "ShouldBlockProactiveAutoPushForWeaponThreat", typeof(EnemyInfo));
        cautious = Bind<Func<EnemyInfo, bool>>(common, "ShouldUseCautiousWeaponThreatStyle", typeof(EnemyInfo));
        inRange = Bind<Func<EnemyInfo, float, bool>>(sniper, "IsWithinMarksmanAutoSearchDistance", typeof(EnemyInfo), typeof(float));
        close = (Func<BotOwner, EnemyInfo, bool>)AccessTools.Method(sniperType, "CanUseAutomaticSupportForCloseThreat").CreateDelegate(typeof(Func<BotOwner, EnemyInfo, bool>));
        allowedCount = (Func<float, int>)AccessTools.Method(commonType, "GetAllowedLowThreatEnemyCount").CreateDelegate(typeof(Func<float, int>));
    }
    private static T Bind<T>(object instance, string name, params Type[] parameters) where T : Delegate =>
        (T)(AccessTools.Method(instance.GetType(), name, parameters) ?? throw new MissingMethodException(name)).CreateDelegate(typeof(T), instance);
    internal bool IsClose(Enemy enemy) => enemy?.EnemyInfo != null && close(bot.BotOwner, enemy.EnemyInfo);
    internal bool CanAdvance(Enemy enemy)
    {
        if (enemy?.EnemyInfo == null || !(enemy.Seen || enemy.Heard) || hold() || medical()) return false;
        if (assessedEnemy == enemy.EnemyProfileId && Time.time < nextAssessment) return eligible;
        assessedEnemy = enemy.EnemyProfileId; nextAssessment = Time.time + 0.5f;
        float value = aggression();
        eligible = false;
        if (value <= 0.01f || !available() || blocked(enemy.EnemyInfo) || cautious(enemy.EnemyInfo) ||
            !inRange(enemy.EnemyInfo, value) || value < 0.4f && !bot.BotOwner.Memory.AttackImmediately) return false;
        // Same 35m Marksman cluster and aggression count; locations are SAIN knowledge.
        counted.Clear(); counted.Add(enemy.EnemyProfileId);
        foreach (Enemy contact in bot.EnemyController.KnownEnemies)
            if (SAINFollowerSquadSupportObjective.Valid(contact) &&
                (contact.LastKnownPosition.GetValueOrDefault() - enemy.LastKnownPosition.GetValueOrDefault()).sqrMagnitude <= 35f * 35f)
                counted.Add(contact.EnemyProfileId);
        return eligible = counted.Count <= allowedCount(value) && (value < 0.4f || counted.Count < 3);
    }
    internal void SetDecisionContext(ECombatDecision solo, ESelfActionType self) => decisionProtected = Protected(solo, self);
    internal void ClearDecisionContext() => decisionProtected = false;
    private static bool Protected(ECombatDecision solo, ESelfActionType self) => self != ESelfActionType.None ||
        solo == ECombatDecision.Retreat || solo == ECombatDecision.DogFight || solo == ECombatDecision.MeleeAttack ||
        solo == ECombatDecision.AvoidGrenade || solo == ECombatDecision.ThrowGrenade || solo == ECombatDecision.FightZombies;
    private bool SwitchesBlocked => decisionProtected || medical() || SainAddonBridge.IsUsingMedical(bot.BotOwner) ||
        Protected(bot.Decision.CurrentCombatDecision, bot.Decision.CurrentSelfDecision);

    // 1 ready, 0 bounded preparation, -1 unavailable/timeout. One accepted request per attempt.
    internal int Prepare(bool ordered = false)
    {
        if ((ordered ? supportReady : ready)())
        { pending = false; settleUntil = 0f; owned |= usingSupport(); State = "ready"; return 1; }
        if (pending)
        {
            if (orderedPreparation != ordered || Time.time >= prepareUntil)
            { pending = false; retryAt = Time.time + 4f; State = "prepareTimeout"; return -1; }
            return 0;
        }
        if (Time.time < retryAt) return -1;
        if (ordered && !settled())
        {
            if (settleUntil <= 0f) settleUntil = Time.time + 3f;
            if (Time.time < settleUntil) { State = "settling"; return 0; }
            settleUntil = 0f; retryAt = Time.time + 4f; State = "settleTimeout"; return -1;
        }
        settleUntil = 0f;
        if (SwitchesBlocked || !(ordered ? supportRequest : request)())
        { retryAt = Time.time + 4f; State = "weaponUnavailable"; return -1; }
        pending = owned = true; orderedPreparation = ordered; prepareUntil = Time.time + 3f;
        State = "preparing"; return 0;
    }
    internal void Maintain(Enemy enemy, bool intent)
    {
        if (intent || IsClose(enemy)) return;
        StopPreparing();
        if (SwitchesBlocked) return; // Retain ownership so safe lifecycle ticks can restore later.
        if (owned && bot.BotOwner?.WeaponManager?.Selector != null && !bot.BotOwner.WeaponManager.Selector.IsChanging)
        { if (!usingSupport() || restore()) { owned = false; State = "primary"; } }
    }
    internal void StopPreparing() { pending = false; settleUntil = 0f; }
    internal void Cancel(Enemy enemy = null) { StopPreparing(); Maintain(enemy, false); }
    internal object Snapshot => new { state = State, pending, owned, eligible, settleRemaining = Mathf.Max(0f, settleUntil - Time.time), prepareRemaining = Mathf.Max(0f, prepareUntil - Time.time) };
}
