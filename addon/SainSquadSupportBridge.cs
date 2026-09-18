using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.Components;
using FollowerShotSafety = pitTeam.Utils.FollowerShotSafety;
using SAIN.Components;
using SAIN.Preset.Shared.GlobalSettings;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.SAINAddon;

// Inaccessible Core predicates and native exact-target shooting are bound once.
// These helpers do not run Core decisions or change shared SAIN settings.
internal static class SainSquadSupportBridge
{
    private delegate bool SuppressTarget(EnemyInfo enemy, out Vector3 point);
    private static SuppressTarget reportedTarget;
    private static Func<Weapon, bool> capable, launcher;
    private static Func<BotOwner, Vector3, bool> directFireLane;
    private static Func<Vector3, Vector3, Vector3, bool> pushPosition;
    private static Func<Vector3, Vector3, LayerMask, bool> softLane;
    private static Func<SAINShootData, Enemy, BotComponent, bool> shoot;

    internal static void Apply(Harmony harmony)
    {
        Type core = typeof(pitFireTeam).Assembly.GetType("pitTeam.BigBrain.FollowerCombatCommon", true);
        Type policy = typeof(pitFireTeam).Assembly.GetType("pitTeam.BigBrain.FollowerSuppressTargetPolicy", true);
        Type firePolicy = typeof(pitFireTeam).Assembly.GetType("pitTeam.BigBrain.FollowerImmediateFirePolicy", true);
        directFireLane = Bind<Func<BotOwner, Vector3, bool>>(firePolicy, "HasDirectFireLane", typeof(BotOwner), typeof(Vector3));
        pushPosition = Bind<Func<Vector3, Vector3, Vector3, bool>>(core, "IsTeamSearchSupportPosition", typeof(Vector3), typeof(Vector3), typeof(Vector3));
        reportedTarget = Bind<SuppressTarget>(policy, "TryGetTarget", typeof(EnemyInfo), typeof(Vector3).MakeByRefType());
        capable = Bind<Func<Weapon, bool>>(core, "IsSuppressCapableWeapon", typeof(Weapon));
        launcher = Bind<Func<Weapon, bool>>(core, "IsGrenadeLauncherWeapon", typeof(Weapon));
        softLane = Bind<Func<Vector3, Vector3, LayerMask, bool>>(core, "IsSoftObstructedSuppressionLane",
            typeof(Vector3), typeof(Vector3), typeof(LayerMask));
        shoot = Bind<Func<SAINShootData, Enemy, BotComponent, bool>>(typeof(SAINShootData), "AimAndShootAtEnemy",
            typeof(Enemy), typeof(BotComponent));
        var issue = AccessTools.Method(typeof(pitAIBossPlayer), "TryIssueSuppressCommand",
            new[] { typeof(BotOwner), typeof(BotFollowerPlayer), typeof(Player), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
        if (issue?.ReturnType != typeof(bool)) throw new MissingMethodException("Core suppression command boundary changed.");
        harmony.Patch(AccessTools.Method(typeof(pitAIBossPlayer), "ApplySuppressPhrase"),
            transpiler: new HarmonyMethod(typeof(SainSquadSupportBridge), nameof(AllowShooterSupport)));
        harmony.Patch(issue, prefix: new HarmonyMethod(typeof(SainSquadSupportBridge), nameof(WeaponOnly)));
    }
    // Remove only the old addon exclusion in Core's candidate selection. All Core
    // boss-facing, role, weapon and target admission rules remain in that method.
    private static IEnumerable<CodeInstruction> AllowShooterSupport(IEnumerable<CodeInstruction> instructions)
    {
        var target = AccessTools.Method(typeof(pitFireTeam), nameof(pitFireTeam.UseSainFollowerCombat));
        int count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(target))
            { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(SainSquadSupportBridge), nameof(UnsupportedAddon)); count++; }
            yield return instruction;
        }
        if (count != 1) throw new MissingMethodException("Core suppression addon exclusion changed.");
    }
    private static bool UnsupportedAddon(BotOwner owner) => pitFireTeam.UseSainFollowerCombat(owner) &&
        SAINFollowerRuntime.GetMarksman(owner) == null;
    private static T Bind<T>(Type type, string name, params Type[] parameters) where T : Delegate =>
        (T)(AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.FullName, name)).CreateDelegate(typeof(T));

    // Keep Core targeting/eligibility, but never accept a launcher-only request on this brain.
    private static bool WeaponOnly(BotOwner follower, ref bool requireLauncher, ref bool forceWeapon,
        ref bool useAutomaticSecondary, ref bool __result)
    {
        if (!pitFireTeam.UseSainFollowerCombat(follower)) return true;
        var marksman = SAINFollowerRuntime.GetMarksman(follower);
        if (marksman != null)
        {
            if (!useAutomaticSecondary || !marksman.Weapons.SupportAvailable) { __result = false; return false; }
            requireLauncher = false; forceWeapon = true; return true;
        }
        if (!CanSuppress(follower)) { __result = false; return false; }
        requireLauncher = false; forceWeapon = true; useAutomaticSecondary = false;
        return true;
    }
    internal static bool IsPushSupportPosition(Vector3 point, Vector3 pusher, Vector3 enemy) => pushPosition?.Invoke(point, pusher, enemy) == true;
    internal static bool CanSuppress(BotOwner owner)
    {
        Weapon weapon = owner?.WeaponManager?.ShootController?.Item;
        return weapon != null && capable?.Invoke(weapon) == true && launcher?.Invoke(weapon) != true;
    }
    internal static bool FireVisible(BotComponent bot, Enemy target) => target.IsVisible && target.CanShoot &&
        !bot.Mover.Running && shoot?.Invoke(bot.Shoot, target, bot) == true;

    internal static bool CanContinueVisibleFire(BotComponent bot, Vector3 point)
    {
        if (!Finite(point) || bot.Mover.Running || !bot.ManualShoot.CanShoot(true)) return false;
        Vector3 origin = bot.Transform.WeaponData.FirePort;
        Vector3 direction = bot.Transform.WeaponData.PointDirection;
        Vector3 delta = point - origin;
        // Core's stationary continuity: actual muzzle alignment within 18 degrees,
        // hard-geometry verification and both target-lane and current aim-lane safety.
        return Finite(origin) && Finite(direction) && delta.sqrMagnitude > 0.0001f &&
            direction.sqrMagnitude > 0.0001f && Vector3.Dot(direction.normalized, delta.normalized) >= 0.9510565f &&
            directFireLane?.Invoke(bot.BotOwner, point) == true &&
            !FollowerShotSafety.IsFriendlyInShotLane(bot.BotOwner, origin, point) &&
            !FollowerShotSafety.IsFriendlyInAimLane(bot.BotOwner, origin, direction, delta.magnitude);
    }

    internal static SainSuppressionAim ResolveSuppressionAim(BotComponent bot, Enemy enemy)
    {
        if (!GlobalSettingsClass.Instance.Mind.TARGET_SUPPRESS_TOGGLE)
            return new(null, null, "none", "suppressionDisabled", false);
        if (enemy.IsZombie || enemy.IsVisible)
            return new(null, null, "none", "nativeTargetIneligible", false);
        Vector3? native = enemy.SuppressionTarget;
        string rejection = "nativeTargetUnavailableAndNoFreshReport";
        if (native.HasValue && CheckLane(bot, native.Value, out rejection))
            return new(native, native, "native", rejection, true);

        // Core's hidden-contact policy pairs each position with its own <=2s timestamp.
        // Do not enter its live body-position branch when SAIN does not see the enemy.
        EnemyInfo info = enemy.EnemyInfo;
        if (info?.IsVisible == false && info.ProfileId == enemy.EnemyProfileId &&
            reportedTarget != null && reportedTarget(info, out Vector3 report))
        {
            bool ready = CheckLane(bot, report, out string lane);
            return new(native, report, "coreRecentReport", lane, ready);
        }
        return new(native, native, native.HasValue ? "native" : "none", rejection, false);
    }
    private static bool CheckLane(BotComponent bot, Vector3 point, out string reason)
    {
        reason = "invalidSuppressionPoint";
        if (!Finite(point)) return false;
        Vector3 origin = bot.BotOwner.WeaponRoot != null ? bot.BotOwner.WeaponRoot.position : bot.Position + Vector3.up * 1.2f;
        Vector3 delta = point - origin;
        if (delta.sqrMagnitude <= 0.01f) return false;
        if (FollowerShotSafety.IsFriendlyInSuppressionLane(bot.BotOwner, origin, point))
        { reason = "friendlyLane"; return false; }
        // Use the same obstruction mask as Core's suppression action.
        LayerMask mask = bot.BotOwner.LookSensor.Mask;
        if (!Physics.Raycast(origin, delta.normalized, delta.magnitude, mask))
        { reason = "directLane"; return true; }
        if (softLane?.Invoke(origin, point, mask) == true)
        { reason = "foliageLane"; return true; }
        reason = "hardObstruction"; return false;
    }
    private static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) &&
        !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);
    internal static void Reset() { capable = launcher = null; directFireLane = null; pushPosition = null; softLane = null; shoot = null; reportedTarget = null; }
}

// Captured by actual planning/firing, never recomputed by recorder reads.
internal readonly struct SainSuppressionAim(Vector3? nativePoint, Vector3? point, string source, string gate, bool ready)
{
    internal Vector3? NativePoint { get; } = nativePoint;
    internal Vector3? Point { get; } = point;
    internal string Source { get; } = source;
    internal string Gate { get; } = gate;
    internal bool Ready { get; } = ready;
}
