using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Modules;
using CoreEnemy = pitTeam.Utils.Enemy;

namespace pitTeam.SAINAddon;

// Contact eligibility and retention remain Core-owned. Only an accepted, prioritized goal-promoting
// command on a ready addon follower may convert a neutral relationship explicitly.
internal static class SainContactEnemyBridge
{
    internal static void Apply(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(pitAIBossPlayer), "RegisterContactEnemyForFollower",
            new[] { typeof(BotOwner), typeof(Player), typeof(bool), typeof(bool), typeof(bool) });
        if (target == null || target.IsStatic || target.ReturnType != typeof(void))
            throw new MissingMethodException("Core Contact registration boundary changed.");
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(SainContactEnemyBridge), nameof(ExplicitContact)));
    }

    private static IEnumerable<CodeInstruction> ExplicitContact(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.Method(typeof(CoreEnemy), nameof(CoreEnemy.MakeEnemy),
            new[] { typeof(BotOwner), typeof(Player), typeof(EBotEnemyCause), typeof(bool) });
        var replacement = AccessTools.Method(typeof(SainContactEnemyBridge), nameof(MakeContactEnemy));
        int count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(original))
            {
                // The original four arguments are on the stack. Keep labels and exception
                // boundaries on the new load of allowGoalPromotion (instance argument 4).
                var allow = new CodeInstruction(OpCodes.Ldarg_S, (byte)4);
                allow.labels.AddRange(instruction.labels); instruction.labels.Clear();
                allow.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                yield return allow;
                yield return new CodeInstruction(OpCodes.Ldarg_3); // prioritizeAsGoal; background reports must not override
                instruction.opcode = OpCodes.Call; instruction.operand = replacement; count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new MissingMethodException("Core Contact enemy creation boundary changed.");
    }

    private static EnemyInfo? MakeContactEnemy(BotOwner owner, Player enemy, EBotEnemyCause cause,
        bool countSharedSeenAsPersonal, bool allowGoalPromotion, bool prioritizeAsGoal)
    {
        if (!allowGoalPromotion || !prioritizeAsGoal || !pitFireTeam.UseSainFollowerCombat(owner))
            return CoreEnemy.MakeEnemy(owner, enemy, cause, countSharedSeenAsPersonal);

        bool wasAlly = owner.BotsGroup?.Allies.Contains(enemy) == true;
        bool wasNeutral = owner.BotsGroup?.Neutrals.ContainsKey(enemy) == true;
        // addPlayer is Core's existing explicit-hostility cause. MakeEnemy retains its
        // player/teammate/protected-role exclusions and the game's AddEnemy validation.
        EnemyInfo? info = CoreEnemy.MakeEnemy(owner, enemy, EBotEnemyCause.addPlayer, countSharedSeenAsPersonal);
        bool admitted = info != null && owner.BotsGroup?.Enemies.ContainsKey(enemy) == true;
        if (admitted)
        {
            // AddEnemy already does this for new enemies, but skips it when the enemy
            // dictionary already contains the target. Reconcile only that exact target.
            owner.BotsGroup.Allies.Remove(enemy);
            owner.BotsGroup.Neutrals.Remove(enemy);
        }
        if (SainCombatRecorderBridge.IsRecording)
            SainCombatRecorderBridge.RecordEvent(owner, "sainContactOverride", new
            { enemyId = enemy?.ProfileId, wasAlly, wasNeutral, admitted });
        return info;
    }
}
