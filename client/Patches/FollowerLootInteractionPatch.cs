using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    internal sealed class FollowerLootInteractionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            typeof(InteractionContextHelper), nameof(InteractionContextHelper.GetAvailableActions),
            new[] { typeof(GamePlayerOwner), typeof(IInteractive) });

        [PatchPostfix]
        private static void Postfix(GamePlayerOwner owner, IInteractive interactive, AvailableInteractionState __result)
        {
            try
            {
                if (__result?.Actions == null || !CanShow(owner, interactive)) return;
                bool hasWeapon = HasWeapon(interactive as Corpse);
                Add(FollowerLootMode.Normal, "LootActionThis");
                if (hasWeapon) Add(FollowerLootMode.LootAndGetWeapon, "LootActionAndWeapon");
                Add(FollowerLootMode.LootAndGetGear, "LootActionAndGear");
                if (hasWeapon) Add(FollowerLootMode.GetWeapon, "LootActionWeapon");
                Add(FollowerLootMode.GetGear, "LootActionGear");

                void Add(FollowerLootMode mode, string key)
                {
                    __result.Actions.Add(new InteractionAction
                    {
                        Name = pitFireTeam.GetSocialUiText(key),
                        Action = () => Execute(owner, interactive, mode)
                    });
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"Follower loot interaction failed: {ex}");
            }
        }

        internal static bool CanShow(GamePlayerOwner owner, IInteractive target)
        {
            if (owner?.Player?.IsYourPlayer != true || owner.Player.HealthController?.IsAlive != true)
                return false;
            if (target is not Corpse) return false;
            return BossPlayers.GetBoss(owner.Player.ProfileId)?.Followers
                .Any(bot => bot != null && !bot.IsDead) == true;
        }

        internal static bool HasWeapon(Corpse corpse)
        {
            Item root = corpse?.ItemOwner?.RootItem;
            return root is CompoundItem && root.GetAllItems()
                .Any(item => item is Weapon && item.GetItemComponent<KnifeComponent>() == null);
        }

        private static void Execute(GamePlayerOwner owner, IInteractive target, FollowerLootMode mode)
        {
            try
            {
                // Capture the displayed target; never dispatch against the mutable quick-menu target.
                if (!CanShow(owner, target) || !ReferenceEquals(owner.Player.InteractableObject, target)) return;
                if ((mode == FollowerLootMode.GetWeapon || mode == FollowerLootMode.LootAndGetWeapon) &&
                    !HasWeapon(target as Corpse)) return;
                var boss = BossPlayers.GetBoss(owner.Player.ProfileId);
                boss.SayLootInteraction(owner.Player, target, mode);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"Follower loot interaction command failed: {ex}");
            }
        }
    }

    internal sealed class FollowerLootInteractionRefreshPatch : ModulePatch
    {
        private sealed class VisibilityState
        {
            public float NextCheck;
            public bool Visible;
            public bool HasWeapon;
        }

        private static readonly ConditionalWeakTable<ActionPanel, VisibilityState> States = new();
        private static readonly FieldInfo OwnerField = AccessTools.Field(typeof(ActionPanel), "_owner");

        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(ActionPanel), nameof(ActionPanel.Update));

        [PatchPostfix]
        private static void Postfix(ActionPanel __instance)
        {
            try
            {
                var state = States.GetValue(__instance, _ => new VisibilityState());
                if (UnityEngine.Time.unscaledTime < state.NextCheck) return;
                state.NextCheck = UnityEngine.Time.unscaledTime + 0.25f;
                var owner = OwnerField.GetValue(__instance) as GamePlayerOwner;
                bool visible = FollowerLootInteractionPatch.CanShow(owner, owner?.Player?.InteractableObject);
                bool hasWeapon = visible && FollowerLootInteractionPatch.HasWeapon(owner.Player.InteractableObject as Corpse);
                if (state.Visible == visible && state.HasWeapon == hasWeapon) return;
                state.Visible = visible;
                state.HasWeapon = hasWeapon;
                owner?.InteractionsChangedHandler();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"Follower loot interaction refresh failed: {ex}");
            }
        }
    }
}
