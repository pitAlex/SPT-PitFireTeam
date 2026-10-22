using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using FollowerShotSafety = pitTeam.Utils.FollowerShotSafety;
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes.WeaponFunction;
using UnityEngine;

namespace pitTeam.SAINAddon;

// Native SAIN plans and starts the throw inside GetDecision. Adapt its enable
// switches/permission and validate the chosen arc before drawing. Native safety
// remains, with Core impact/release checks and a local failed-sequence backoff.
internal static class SainGrenadeThrowBridge
{
    // Failed sequences must not cycle drawing/putting away a grenade. Keep this
    // separate from Core's successful-release timers and squad reservation.
    private const float FailedThrowRetrySeconds = 15f;
    private sealed class Retry { internal float Until; }
    private static ConditionalWeakTable<BotOwner, Retry> retries = new();
    private static readonly HashSet<BotOwner> Owners = new();
    private static Action<BotOwner> enable;
    private static Action<BotOwner, bool> finish;
    private static Func<BotOwner, bool> allowed, cooldown, released;

    internal static void Apply(Harmony harmony)
    {
        Type gate = typeof(SainAddonBridge).Assembly.GetType("pitTeam.Modules.FollowerGrenadeRuntimeGate", true);
        Type timers = typeof(SainAddonBridge).Assembly.GetType("pitTeam.Modules.FollowerGrenadeCooldowns", true);
        enable = Bind<Action<BotOwner>>(gate, "EnableExplicitThrow", typeof(BotOwner));
        finish = Bind<Action<BotOwner, bool>>(gate, "FinishExplicitThrow", typeof(BotOwner), typeof(bool));
        allowed = Bind<Func<BotOwner, bool>>(gate, "IsThrowAllowed", typeof(BotOwner));
        released = Bind<Func<BotOwner, bool>>(gate, "HasReleasedThrow", typeof(BotOwner));
        cooldown = Bind<Func<BotOwner, bool>>(timers, "CanProceedToThrow", typeof(BotOwner));
        var decision = AccessTools.Method(typeof(GrenadeThrowDecider), nameof(GrenadeThrowDecider.GetDecision),
            new[] { typeof(Enemy), typeof(string).MakeByRefType() });
        if (decision?.ReturnType != typeof(bool) || decision.IsStatic)
            throw new MissingMethodException("SAIN grenade decision boundary changed.");
        harmony.Patch(decision,
            prefix: new HarmonyMethod(typeof(SainGrenadeThrowBridge), nameof(BeforeDecision)),
            transpiler: new HarmonyMethod(typeof(SainGrenadeThrowBridge), nameof(EnableReads)),
            finalizer: new HarmonyMethod(typeof(SainGrenadeThrowBridge), nameof(AfterDecision)));
        var start = AccessTools.Method(typeof(GrenadeThrowDecider), "TryThrowGrenade", Type.EmptyTypes);
        if (start?.ReturnType != typeof(bool) || start.IsStatic)
            throw new MissingMethodException("SAIN grenade start boundary changed.");
        harmony.Patch(start, prefix: new HarmonyMethod(typeof(SainGrenadeThrowBridge), nameof(BeforeThrow)));
        harmony.Patch(AccessTools.Method(typeof(GrenadeThrowDecider), "CheckFriendlyDistances",
            new[] { typeof(Vector3) }),
            postfix: new HarmonyMethod(typeof(SainGrenadeThrowBridge), nameof(FriendlyDistances)));
        harmony.Patch(AccessTools.Method(typeof(BotGrenadeController), nameof(BotGrenadeController.EndAll),
            new[] { typeof(EFT.InventoryLogic.ThrowWeap) }),
            prefix: new HarmonyMethod(typeof(SainGrenadeThrowBridge), nameof(BeforeEnd)),
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

    private static void FriendlyDistances(GrenadeThrowDecider __instance, Vector3 __0, ref bool __result)
    {
        // SAIN's Members contains BotComponents only; the human leader is absent.
        // Apply this to native candidates too, after dispersion/blind-corner selection.
        if (__result && Owns(__instance.Bot))
            __result = !FollowerShotSafety.IsFriendlyNearGrenadeImpact(__instance.BotOwner, __0,
                FollowerShotSafety.RegularGrenadeUnsafeRadius, includeMovementPrediction: true, out _);
    }

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
        if (retries.TryGetValue(owner, out Retry retry))
        {
            if (Time.time < retry.Until)
            { __1 = "followerGrenadeRetry"; __result = false; return false; }
            retries.Remove(owner);
        }
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

    private static bool BeforeThrow(GrenadeThrowDecider __instance, ref bool __result)
    {
        var owner = __instance.BotOwner;
        if (!Owns(__instance.Bot) || !Owners.Contains(owner)) return true;
        var grenades = owner.WeaponManager?.Grenades;
        var data = grenades?.AIGreanageThrowData;
        // Native eligibility remains authoritative. Probe only a selected, fresh
        // candidate that is actually about to draw, never an idle decision poll.
        if (grenades == null || grenades.ThrowindNow || !grenades.ReadyToThrow || data?.IsUpToDate() != true) return true;
        if (!FollowerShotSafety.IsRegularGrenadeTrajectoryUnsafeForThrower(owner, grenades, data, out string reason)) return true;
        DeferRetry(owner, "preDraw:" + reason, data.Target);
        __result = false;
        return false;
    }

    private static void BeforeEnd(BotGrenadeController __instance, out BotOwner __state)
    {
        var owner = __instance._owner;
        // Capture before Core's EndAll postfix consumes the release marker. A
        // cancelled draw and a real spawn must not depend on postfix ordering.
        __state = owner != null && Owners.Contains(owner) && __instance.ThrowindNow && !released(owner) ? owner : null;
    }

    private static void Ended(BotGrenadeController __instance, BotOwner __state)
    {
        Owners.Remove(__instance._owner);
        if (__state != null) DeferRetry(__state, "endedWithoutRelease", __instance.AIGreanageThrowData?.Target);
    }

    private static void DeferRetry(BotOwner owner, string reason, Vector3? target)
    {
        float until = Time.time + FailedThrowRetrySeconds;
        retries.GetValue(owner, _ => new Retry()).Until = until;
        if (SainCombatRecorderBridge.IsRecording)
            SainCombatRecorderBridge.RecordEvent(owner, "sainGrenade", new {
                action = "retryDeferred", reason, retryUntil = until,
                target = target.HasValue ? new { x = target.Value.x, y = target.Value.y, z = target.Value.z } : null
            });
    }

    internal static void Release(BotOwner owner)
    {
        // A short combat/tactic handoff cannot rearm a failed throw. Weak retry
        // state expires on the next eligible decision or with the bot itself.
        if (owner != null && Owners.Remove(owner)) finish(owner, false);
    }

    internal static void Reset()
    {
        foreach (var owner in Owners) finish(owner, false);
        Owners.Clear();
        retries = new();
    }
}
