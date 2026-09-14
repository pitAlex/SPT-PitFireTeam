using EFT;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using Enemy = SAIN.SAINComponent.Classes.EnemyClasses.Enemy;
using UnityEngine;

namespace pitTeam.SAINAddon;

internal enum SAINFollowerCombatPhase { Combat, Linger, Released }

// One handoff per follower, shared by the solo and squad layers. Layer switches must
// not restart the timer or carry a dead enemy's native action into peaceful follow.
internal sealed class SAINFollowerCombatHandoff
{
    internal const float LingerSeconds = 3f;
    private bool hadCombat;
    private bool hadEnemy;
    private float lingerUntil;
    internal bool EnteredCombat => hadCombat || lingerUntil > 0f;

    internal SAINFollowerCombatPhase Update(BotComponent bot)
    {
        bool liveEnemy = HasLiveEnemy(bot);
        if (liveEnemy && !EnteredCombat)
            BossPlayers.Instance?.GetFollower(bot.BotOwner)?.BeginCombatIndependenceFromPatrol();
        if (liveEnemy && !hadEnemy) SainAddonBridge.EndPostCombatFullHeal(bot.BotOwner);
        hadEnemy = liveEnemy;
        if (liveEnemy || SainAddonBridge.IsUsingMedical(bot.BotOwner) || IsMedical(bot.Decision.CurrentSelfDecision) ||
            bot.Decision.CurrentCombatDecision == ECombatDecision.AvoidGrenade)
        {
            if (bot.Decision.HasDecision) hadCombat = true;
            lingerUntil = 0f;
            return SAINFollowerCombatPhase.Combat;
        }

        if (hadCombat)
        {
            hadCombat = false;
            lingerUntil = Time.time + LingerSeconds;
            pitTeam.Modules.Logger.LogInfo($"[SAIN] Linger started: follower={bot.ProfileId}");
        }

        // Reset this follower through SAIN's publisher, never its private decision fields
        // or living enemy memory. A stale decision must not reactivate either replica.
        if (bot.Decision.HasDecision) bot.Decision.ResetDecisions(false);
        if (lingerUntil > Time.time) return SAINFollowerCombatPhase.Linger;
        if (lingerUntil > 0f)
        {
            lingerUntil = 0f;
            SainAddonBridge.BeginPostCombatFullHeal(bot.BotOwner);
            BossPlayers.Instance?.GetFollower(bot.BotOwner)?.ClearActiveCombatIndependent();
            pitTeam.Modules.Logger.LogInfo($"[SAIN] Linger completed: follower={bot.ProfileId}");
        }
        return SAINFollowerCombatPhase.Released;
    }

    internal static bool HasLiveEnemy(BotComponent bot)
    {
        if (bot == null || !AllowsEnemyCombat(bot.BotOwner)) return false;
        if (IsLive(bot.GoalEnemy)) return true;
        if (bot.EnemyController?.KnownEnemies != null)
            foreach (Enemy enemy in bot.EnemyController.KnownEnemies)
                if (IsLive(enemy)) return true;
        return false;
    }

    // Native heard contacts can have a SAIN goal while core deliberately rejects the
    // EFT goal. They may steer this follower's combat brain only in independent mode.
    internal static bool AllowsEnemyCombat(BotOwner owner)
    {
        var follower = BossPlayers.Instance?.GetFollower(owner);
        return follower?.CombatIndependent == true ||
            (!SAINFollowerRuntime.HasEnteredCombat(owner) && follower?.CombatIndependencePreference == true) ||
            SainAddonBridge.HasAcceptedGoalEnemy(owner);
    }

    internal static bool AllowsDecision(BotComponent bot, ECombatDecision solo, ESelfActionType self) =>
        AllowsEnemyCombat(bot.BotOwner) || SainAddonBridge.IsUsingMedical(bot.BotOwner) ||
        IsMedical(self) || solo == ECombatDecision.AvoidGrenade;

    private static bool IsMedical(ESelfActionType self) =>
        self == ESelfActionType.FirstAid || self == ESelfActionType.Surgery || self == ESelfActionType.Stims;

    internal void Release(BotOwner owner)
    {
        if (EnteredCombat) SainAddonBridge.BeginPostCombatFullHeal(owner);
        BossPlayers.Instance?.GetFollower(owner)?.ClearActiveCombatIndependent();
        Clear();
    }

    private static bool IsLive(Enemy enemy) =>
        enemy?.EnemyPlayer?.HealthController?.IsAlive == true && Enemy.IsEnemyActive(enemy);

    internal void Clear()
    {
        hadCombat = false;
        hadEnemy = false;
        lingerUntil = 0f;
    }
}
