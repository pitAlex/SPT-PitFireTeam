#if DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using pitTeam.Modules;
using UnityEngine;
using Logger = pitTeam.Modules.Logger;

namespace pitTeam.Patches;

// Failure-only animation logging and bounded medical event history. These hooks
// preserve exceptions and never repair inventory, invoke medical providers or change AI.
internal static class RuntimeFailureDiagnostics
{
    private sealed class History
    {
        internal readonly Queue<string> Events = new();
        internal int Failures;
    }
    private static readonly ConditionalWeakTable<ItemController, History> Medical = new();
    private static readonly ConditionalWeakTable<object, History> Failures = new();
    private static readonly FieldInfo WeaponPrefabField = AccessTools.Field(typeof(PlayerModelLoader), "_weaponPrefab");
    private static readonly FieldInfo HandsPlayerField = AccessTools.Field(typeof(Player.FirearmController), "_player");

    internal static void Apply(Harmony harmony)
    {
        Install(harmony, AccessTools.Method(typeof(ItemController), nameof(ItemController.ProcessActivity), new[] { typeof(ItemEventArgs) }), nameof(ObserveDrain), false);
        Install(harmony, AccessTools.Method(typeof(PlayerModelLoader), nameof(PlayerModelLoader.CreateWeapon)), nameof(PreviewFailed), true);
        Install(harmony, AccessTools.Method(typeof(Player.FirearmController), "ResetAimingAnimationsFlags"), nameof(AimingFailed), true);
    }

    private static void Install(Harmony harmony, MethodInfo target, string hook, bool finalizer)
    {
        try
        {
            if (target == null) throw new MissingMethodException(hook);
            var patch = new HarmonyMethod(typeof(RuntimeFailureDiagnostics), hook);
            harmony.Patch(target, prefix: finalizer ? null : patch, finalizer: finalizer ? patch : null);
        }
        catch (Exception ex) { Logger.LogError($"[RuntimeFailureDiagnostics] Could not install {hook}: {ex}"); }
    }

    private static void ObserveDrain(ItemController __instance, ItemEventArgs args)
    {
        if (args is not DrainItemEventArgs) return;
        try
        {
            var player = (__instance as Player.PlayerInventoryController)?.Player;
            var bot = player?.AIData?.BotOwner;
            if (bot == null || !BossPlayers.IsFollower(bot)) return;
            History trace = Medical.GetOrCreateValue(__instance);
            bool paired = false;
            foreach (ItemEventArgs active in __instance.ActiveEvents)
                if (active.GetType() == args.GetType() && ReferenceEquals(active.Item, args.Item)) { paired = true; break; }
            string state = $"t={Time.time:F3} frame={Time.frameCount} event={args.EventId} status={args.Status} item={args.Item?.Id} paired={paired} " +
                $"firstAid={bot.Medecine?.FirstAid?.Using} selected={bot.Medecine?.FirstAid?.CurUsingMeds?.Id} " +
                $"surgery={bot.Medecine?.SurgicalKit?.Using} hands={player.HandsController?.GetType().Name}";
            if (args.Status != CommandStatus.Begin && !paired && trace.Failures++ < 3)
                Logger.LogError($"[MedicalActivity] follower={player.ProfileId} {state}\nRecent drain events: {string.Join(" | ", trace.Events)}\n{Environment.StackTrace}");
            if (trace.Events.Count == 8) trace.Events.Dequeue();
            trace.Events.Enqueue(state);
        }
        catch { /* Diagnostics cannot change inventory event processing. */ }
    }

    private static Exception PreviewFailed(PlayerModelLoader __instance, Item weapon, Exception __exception)
    {
        if (__exception == null) return null;
        try
        {
            if (Failures.GetOrCreateValue(__instance).Failures++ >= 3) return __exception;
            var prefab = WeaponPrefabField?.GetValue(__instance) as Component;
            var animatorType = AccessTools.TypeByName("UnityEngine.Animator");
            var animator = animatorType == null ? null : prefab?.GetComponentInChildren(animatorType, true);
            var profileScreen = OtherPlayerProfileScreenPatch.ActiveProfileScreen;
            bool teammateProfile = profileScreen != null && profileScreen.gameObject.activeInHierarchy;
            Logger.LogError($"[PreviewFailure] item={weapon?.Id} template={weapon?.TemplateId} teammateProfileOpen={teammateProfile} " +
                $"previewPath={Path(__instance.ModelPlayerPoser?.transform)} prefabPath={Path(prefab?.transform)} " +
                $"animator={AnimatorState(animator)} exception={__exception.GetType().Name}");
        }
        catch { }
        return __exception;
    }

    private static Exception AimingFailed(Player.FirearmController __instance, Exception __exception)
    {
        if (__exception == null) return null;
        try
        {
            if (Failures.GetOrCreateValue(__instance).Failures++ >= 3) return __exception;
            var player = HandsPlayerField?.GetValue(__instance) as Player;
            var bot = player?.AIData?.BotOwner;
            Logger.LogError($"[AimingFailure] player={player?.ProfileId} follower={bot != null && BossPlayers.IsFollower(bot)} " +
                $"alive={player?.HealthController?.IsAlive} item={__instance.Item?.Id} template={__instance.Item?.TemplateId} " +
                $"currentHands={ReferenceEquals(player?.HandsController, __instance)} firearmsAnimatorNull={__instance.FirearmsAnimator == null} " +
                $"firstAid={bot?.Medecine?.FirstAid?.Using} surgery={bot?.Medecine?.SurgicalKit?.Using} exception={__exception.GetType().Name}");
        }
        catch { }
        return __exception;
    }

    private static string AnimatorState(Component animator) => animator == null ? "missing" :
        $"{animator.name},active={animator.gameObject.activeInHierarchy},enabled={AccessTools.Property(animator.GetType(), "enabled")?.GetValue(animator)}," +
        $"controller={AccessTools.Property(animator.GetType(), "runtimeAnimatorController")?.GetValue(animator)},layers={AccessTools.Property(animator.GetType(), "layerCount")?.GetValue(animator)}";

    private static string Path(Transform transform)
    {
        if (transform == null) return "missing";
        string path = transform.name;
        for (int i = 0; i < 12 && transform.parent != null; i++) { transform = transform.parent; path = transform.name + "/" + path; }
        return path;
    }
}
#endif
