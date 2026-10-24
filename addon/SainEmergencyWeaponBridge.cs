using System;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.Decision;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.SAINAddon;

// SAIN bypasses vanilla's empty-gun switch inside FightShallReload. Admit one
// emergency draw before its self-action provider; native publication stays single-owned.
internal static class SainEmergencyWeaponBridge
{
    private sealed class Attempt
    {
        internal float RetryAt, PrepareUntil;
        internal string PendingWeapon;
    }
    private static ConditionalWeakTable<BotComponent, Attempt> attempts = new();
    private static Func<Weapon, int> loadedRounds;
    private static Func<Weapon, bool> launcher;
    private static bool reported;

    internal static void Apply(Harmony harmony)
    {
        var method = AccessTools.Method(typeof(SelfActionDecisionClass), nameof(SelfActionDecisionClass.GetDecision),
            new[] { typeof(ESelfActionType).MakeByRefType(), typeof(Enemy) });
        if (method?.ReturnType != typeof(bool) || method.IsStatic)
            throw new MissingMethodException("SAIN self-action decision boundary changed.");
        Type common = typeof(pitFireTeam).Assembly.GetType("pitTeam.BigBrain.FollowerCombatCommon", true);
        loadedRounds = (Func<Weapon, int>)AccessTools.Method(common, "CountLoadedRounds", new[] { typeof(Weapon) })
            .CreateDelegate(typeof(Func<Weapon, int>));
        launcher = (Func<Weapon, bool>)AccessTools.Method(common, "IsGrenadeLauncherWeapon", new[] { typeof(Weapon) })
            .CreateDelegate(typeof(Func<Weapon, bool>));
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(SainEmergencyWeaponBridge), nameof(BeforeDecision)));
    }

    internal static void Reset() { attempts = new(); loadedRounds = null; launcher = null; reported = false; }

    private static bool BeforeDecision(SelfActionDecisionClass __instance, ref ESelfActionType __0,
        Enemy __1, ref bool __result)
    {
        try
        {
            if (!TryPrepare(__instance.Bot, __1)) return true;
            // This is a hands transition, not a reload or medical action. Suppress
            // competing self-actions only during the bounded accepted draw.
            __0 = ESelfActionType.None;
            __result = false;
            return false;
        }
        catch (Exception ex)
        {
            if (!reported) { reported = true; pitTeam.Modules.Logger.LogError($"[SAIN] Emergency backup failed; retaining native self-actions. {ex}"); }
            return true;
        }
    }

    private static bool TryPrepare(BotComponent bot, Enemy enemy)
    {
        BotOwner owner = bot?.BotOwner;
        if (owner == null || !SainAddonBridge.IsSainManSelected(owner) ||
            !pitFireTeam.UseSainFollowerCombat(owner) || !SAINFollowerCombatHandoff.AllowsEnemyCombat(owner) ||
            enemy == null || !Enemy.IsEnemyActive(enemy) || !enemy.EnemyKnown ||
            enemy.EnemyPlayer?.HealthController?.IsAlive != true) return false;

        var manager = owner.WeaponManager;
        var selector = manager?.Selector;
        if (selector == null || manager.Reload == null) return false;
        if (SainAddonBridge.IsUsingMedical(owner) || manager.Reload.Reloading ||
            bot.Decision.CurrentSelfDecision != ESelfActionType.None ||
            bot.Decision.CurrentCombatDecision == ECombatDecision.ThrowGrenade ||
            bot.Decision.CurrentCombatDecision == ECombatDecision.AvoidGrenade ||
            bot.Decision.CurrentCombatDecision == ECombatDecision.MeleeAttack) return false;

        var state = attempts.GetValue(bot, _ => new Attempt());
        var hands = owner.GetPlayer?.HandsController as Player.FirearmController;
        if (state.PendingWeapon != null)
        {
            if (Time.time < state.PrepareUntil &&
                (hands?.Item?.Id != state.PendingWeapon || !manager.IsReady ||
                 !selector.IsWeaponReady || selector.IsChanging)) return true;
            state.PendingWeapon = null;
            return false;
        }

        if (Time.time < state.RetryAt || !enemy.IsVisible || !enemy.CanShoot ||
            !(enemy.RealDistance > 0f && enemy.RealDistance <= 10f) ||
            !manager.IsReady || !selector.IsWeaponReady || selector.IsChanging || manager.IsMelee ||
            hands == null || hands.IsInReloadOperation() || hands.IsInInteraction() ||
            hands.Item == null || manager.CurrentWeapon?.Id != hands.Item.Id ||
            loadedRounds(hands.Item) != 0 || launcher(hands.Item) ||
            selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon) return false;

        var equipment = owner.GetPlayer.InventoryController?.Inventory?.Equipment;
        Weapon second = equipment?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem as Weapon;
        Weapon pistol = equipment?.GetSlot(EquipmentSlot.Holster)?.ContainedItem as Weapon;
        EquipmentSlot slot;
        Weapon backup;
        if (selector.CanChangeToSecondWeapons && Eligible(second))
        { slot = EquipmentSlot.SecondPrimaryWeapon; backup = second; }
        else if (selector._canChangeToSupportWeapons && Eligible(pistol))
        { slot = EquipmentSlot.Holster; backup = pistol; }
        else return false;

        // Reserve before calling EFT: rejected requests also cannot churn every tick.
        state.RetryAt = Time.time + 25f;
        if (!selector.TryChangeToSlot(slot, false)) return false;
        state.PendingWeapon = backup.Id;
        state.PrepareUntil = Time.time + 3f;
        if (pitFireTeam.IsDebugBuild)
            SainCombatRecorderBridge.RecordEvent(owner, "sainEmergencyWeapon", new { slot = slot.ToString(), weapon = backup.Id, distance = enemy.RealDistance });
        return true;
    }

    private static bool Eligible(Weapon weapon) => weapon != null && !launcher(weapon) &&
        weapon.MalfState.State == Weapon.EMalfunctionState.None && loadedRounds(weapon) > 0;
}
