using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes.WeaponFunction;

namespace pitTeam.SAINAddon;

// Native SAIN plans and starts the throw inside GetDecision. Only its enable
// switches and Core's explicit permission are adapted; all safety checks stay native.
internal static class SainGrenadeThrowBridge
{
    private static readonly HashSet<BotOwner> Owners = new();
    private static Action<BotOwner> enable;
    private static Action<BotOwner, bool> finish;
    private static Func<BotOwner, bool> allowed, cooldown;

    internal static void Apply(Harmony harmony)
    {
        Type gate = typeof(SainAddonBridge).Assembly.GetType("pitTeam.Modules.FollowerGrenadeRuntimeGate", true);
        Type timers = typeof(SainAddonBridge).Assembly.GetType("pitTeam.Modules.FollowerGrenadeCooldowns", true);
        enable = Bind<Action<BotOwner>>(gate, "EnableExplicitThrow", typeof(BotOwner));
        finish = Bind<Action<BotOwner, bool>>(gate, "FinishExplicitThrow", typeof(BotOwner), typeof(bool));
        allowed = Bind<Func<BotOwner, bool>>(gate, "IsThrowAllowed", typeof(BotOwner));
        cooldown = Bind<Func<BotOwner, bool>>(timers, "CanProceedToThrow", typeof(BotOwner));
        var decision = AccessTools.Method(typeof(GrenadeThrowDecider), nameof(GrenadeThrowDecider.GetDecision),
            new[] { typeof(Enemy), typeof(string).MakeByRefType() });
        if (decision?.ReturnType != typeof(bool) || decision.IsStatic)
            throw new MissingMethodException("SAIN grenade decision boundary changed.");
        harmony.Patch(decision,
            prefix: new HarmonyMethod(typeof(SainGrenadeThrowBridge), nameof(BeforeDecision)),
            transpiler: new HarmonyMethod(typeof(SainGrenadeThrowBridge), nameof(EnableReads)),
            finalizer: new HarmonyMethod(typeof(SainGrenadeThrowBridge), nameof(AfterDecision)));
        harmony.Patch(AccessTools.Method(typeof(BotGrenadeController), nameof(BotGrenadeController.EndAll),
            new[] { typeof(EFT.InventoryLogic.ThrowWeap) }),
            postfix: new HarmonyMethod(typeof(SainGrenadeThrowBridge), nameof(Ended)));
    }

    private static T Bind<T>(Type type, string name, params Type[] parameters) where T : Delegate =>
        (T)(AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.FullName, name))
            .CreateDelegate(typeof(T));

    private static bool Owns(BotComponent bot) => bot != null &&
        SainAddonBridge.IsFollowerCombatEnabled(bot.BotOwner) && SainAddonBridge.IsCombatReady(bot.BotOwner);

    // Project the three enable reads, never mutate preset/global or cached bot fields.
    private static IEnumerable<CodeInstruction> EnableReads(IEnumerable<CodeInstruction> instructions)
    {
        int master = 0, capability = 0, versusBots = 0;
        var project = AccessTools.Method(typeof(SainGrenadeThrowBridge), nameof(Enabled));
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.opcode != OpCodes.Ldfld || instruction.operand is not FieldInfo field) continue;
            bool match = false;
            if (field.DeclaringType == typeof(GrenadeThrowDecider) && field.Name == "_grenadesEnabled") { master++; match = true; }
            if (field.DeclaringType == typeof(GrenadeThrowDecider) && field.Name == "_canThrowGrenades") { capability++; match = true; }
            if (field.DeclaringType?.FullName == "SAIN.Preset.Shared.GlobalSettings.Categories.General.GeneralSettings" &&
                field.Name == "BotVsBotGrenade") { versusBots++; match = true; }
            if (!match) continue;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, project);
        }
        if (master != 1 || capability != 1 || versusBots != 1)
            throw new MissingMethodException("SAIN grenade enable switches changed.");
    }

    private static bool Enabled(bool native, GrenadeThrowDecider decider) =>
        Owns(decider.Bot) ? pitFireTeam.botGrenades.Value : native;

    private static bool BeforeDecision(GrenadeThrowDecider __instance, Enemy __0, ref string __1,
        ref bool __result, out BotOwner __state)
    {
        __state = null;
        var bot = __instance.Bot;
        if (!Owns(bot)) return true;
        var owner = bot.BotOwner;
        if (!pitFireTeam.botGrenades.Value || !SAINFollowerCombatHandoff.AllowsEnemyCombat(owner) ||
            __0?.EnemyPlayer?.HealthController?.IsAlive != true || !Enemy.IsEnemyActive(__0))
        { __1 = "followerGrenadesDisabledOrNoContact"; __result = false; return false; }
        if (owner.WeaponManager?.Grenades == null)
        { __1 = "followerGrenadeControllerMissing"; __result = false; return false; }
        // An accepted sequence owns its window until native EndAll, including after release.
        if (owner.WeaponManager.Grenades.ThrowindNow) return true;
        if (!cooldown(owner))
        { __1 = "followerGrenadeCooldown"; __result = false; return false; }
        __state = owner;
        Owners.Add(owner);
        enable(owner);
        if (allowed(owner)) return true;
        __1 = "followerGrenadeWindowBusy"; __result = false; return false;
    }

    // Runs even when native planning/DoThrow throws. A failed scan cannot leave a
    // squad-wide permission window open. A real asynchronous throw retains ownership.
    private static Exception AfterDecision(BotOwner __state, Exception __exception)
    {
        if (__state != null && __state.WeaponManager?.Grenades?.ThrowindNow != true && Owners.Remove(__state))
            finish(__state, false);
        return __exception;
    }

    private static void Ended(BotGrenadeController __instance) => Owners.Remove(__instance._owner);

    internal static void Release(BotOwner owner)
    {
        if (owner != null && Owners.Remove(owner)) finish(owner, false);
    }

    internal static void Reset()
    {
        foreach (var owner in Owners) finish(owner, false);
        Owners.Clear();
    }
}
