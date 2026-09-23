using HarmonyLib;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes.Info;
using SAIN.SAINComponent.Classes.WeaponFunction;

namespace pitTeam.SAINAddon;

// SAIN's idle mode selection uses aim distance even without a combat target.
// Leave the held weapon alone during follower patrol, including idle inspection animations.
internal static class SainIdleWeaponGuard
{
    internal static void Apply(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(Firemode), nameof(Firemode.CheckSwapFireMode),
            new[] { typeof(BotComponent), typeof(BotWeaponInfoClass) }),
        prefix: new HarmonyMethod(typeof(SainIdleWeaponGuard), nameof(BeforeCheckSwap)));

    private static bool BeforeCheckSwap(BotComponent __0)
    {
        if (__0 == null || !SainAddonBridge.IsAddonTacticSelected(__0.BotOwner)) return true;
        // Pure admission reads: do not evaluate providers/handoff, refresh memory,
        // enumerate known enemies or mutate native mode/timers from this hot callback.
        Enemy enemy = __0.GoalEnemy;
        return enemy != null && enemy.EnemyKnown && Enemy.IsEnemyActive(enemy) &&
            enemy.EnemyPlayer?.HealthController?.IsAlive == true &&
            SAINFollowerCombatHandoff.AllowsEnemyCombat(__0.BotOwner);
    }
}
