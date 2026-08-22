using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI.Gestures;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace pitTeam.Patches
{
    internal static class QuickPanelHurtPhraseFilter
    {
        private static readonly EPhraseTrigger[] BlockedPhrases =
        [
            EPhraseTrigger.HurtLight,
            EPhraseTrigger.HurtMedium,
            EPhraseTrigger.HurtHeavy,
            EPhraseTrigger.HurtNearDeath,
            EPhraseTrigger.OnBeingHurt,
            EPhraseTrigger.OnBeingHurtDissapoinment
        ];

        public static bool IsBlocked(EPhraseTrigger phrase)
        {
            return Array.IndexOf(BlockedPhrases, phrase) >= 0;
        }

        public static void RemoveBlockedCommands(GesturesQuickPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            foreach (EPhraseTrigger phrase in BlockedPhrases)
            {
                panel.SetPhraseActive(phrase, false);
            }
        }
    }

    internal class QuickPanelPatch : ModulePatch
    {
        private static readonly EPhraseTrigger ViewBackpackPhrase = (EPhraseTrigger)CustomPhrases.ViewBackpack;
        private static readonly FieldInfo QuickPanelAvailablePhrasesField = AccessTools.Field(typeof(GesturesQuickPanel), "_availablePhrases");
        private static readonly FieldInfo QuickPanelPlayerField = AccessTools.Field(typeof(GesturesQuickPanel), "_player");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GesturesQuickPanel), nameof(GesturesQuickPanel.OnPossibleInteractionChangedHandler));
        }
        /** Patch QuickGesturesPanel to disable the "Cooperative" phrase if bot is a follower **/
        [PatchPrefix]
        private static bool PatchPrefix(GesturesQuickPanel __instance)
        {
            Player player = QuickPanelPlayerField.GetValue(__instance) as Player;
            if (player != null)
            {
                QuickPanelHurtPhraseFilter.RemoveBlockedCommands(__instance);
                RefreshViewBackpackQuickCommand(__instance, player);

                try
                {
                    // original
                    LootItem? lootItem = player.InteractableObject as LootItem;
                    bool flag = lootItem != null && lootItem.ItemOwner.RootItem.GetItemComponent<KeyComponent>() != null;
                    bool flag2 = lootItem != null && lootItem.ItemOwner.RootItem is EFT.InventoryLogic.Money;
                    bool flag3 = lootItem != null && (lootItem.ItemOwner.RootItem is Weapon || lootItem.ItemOwner.RootItem.GetItemComponent<KnifeComponent>() != null);
                    Corpse? corpse = player.InteractableObject as Corpse;
                    LootableContainer? lootContainer = player.InteractableObject as LootableContainer;
                    bool canLootContainer = lootContainer != null &&
                                            lootContainer.isActiveAndEnabled &&
                                            lootContainer.DoorState != EDoorState.Locked;

                    // Commanded follower looting uses the same world target for key, money, weapon,
                    // and generic loot phrases. Keep it pinned for any loot phrase the panel exposes.
                    InteractableObjects.SetCurLootItem(corpse == null && lootContainer == null ? lootItem : null);
                    if (corpse != null)
                    {
                        InteractableObjects.SetCurBodyLootTarget(corpse);
                    }
                    InteractableObjects.SetCurLootContainerTarget(canLootContainer ? lootContainer : null);

                    // original - loot command
                    __instance.SetPhraseActive(EPhraseTrigger.LootKey, flag);
                    __instance.SetPhraseActive(EPhraseTrigger.LootMoney, flag2);
                    __instance.SetPhraseActive(EPhraseTrigger.LootWeapon, flag3);
                    __instance.SetPhraseActive(EPhraseTrigger.LootGeneric, corpse == null && lootContainer == null && lootItem != null && !flag && !flag2 && !flag3);
                    // Body phrases are routed to a follower body-gear recovery command, not vanilla bot corpse work.
                    __instance.SetPhraseActive(EPhraseTrigger.LootBody, corpse != null);
                    __instance.SetPhraseActive(EPhraseTrigger.CheckHim, corpse != null);
                    __instance.SetPhraseActive(EPhraseTrigger.LootContainer, canLootContainer);
                }
                catch (Exception e)
                {
                    Logger.LogError("Loot Command Failed:");
                    Logger.LogError(e);
                }

                // modification here - open door command
                Door? door = player.InteractableObject as Door;
                try
                {
                    bool canOpen = door != null && door.DoorState != EDoorState.Open;
                    if (door != null && canOpen)
                    {
                        InteractableObjects.SetCurDoor(door);
                    }
                    __instance.SetPhraseActive(EPhraseTrigger.OpenDoor, canOpen);
                }
                catch (Exception e)
                {
                    Logger.LogError("Open Door Command Failed:");
                    Logger.LogError(e);
                }

                // original
                __instance.SetPhraseActive(EPhraseTrigger.LockedDoor, door != null && (door.DoorState == EDoorState.Locked || door.DoorState == EDoorState.Shut));

                // Show cooperation for any alive non-follower AI target.
                try
                {
                    if (!pitFireTeam.pickupEnabled.Value)
                    {
                        __instance.SetPhraseActive(EPhraseTrigger.Cooperation, false);
                        return false;
                    }

                    if (player.InteractablePlayer != null && player.InteractablePlayer.IsAI && player.InteractablePlayer.HealthController.IsAlive)
                    {
                        BotOwner targetBot = player.InteractablePlayer.AIData?.BotOwner;
                        if (targetBot == null || BossPlayers.IsFollower(targetBot) || player.InteractablePlayer.Side != player.Side)
                        {
                            __instance.SetPhraseActive(EPhraseTrigger.Cooperation, false);

                            return false;
                        }
                        else
                        {
                            __instance.SetPhraseActive(EPhraseTrigger.Cooperation, true);
                        }
                    }
                    else
                    {
                        __instance.SetPhraseActive(EPhraseTrigger.Cooperation, false);
                    }
                }
                catch (Exception e)
                {
                    Logger.LogError("Cooperation Command Failed:");
                    Logger.LogError(e);
                }

                return false;
            }

            return true;
        }

        private static void EnsureViewBackpackQuickCommand(GesturesQuickPanel panel)
        {
            if (!GesturesQuickPanel.PhrasePriorities.ContainsKey(ViewBackpackPhrase))
            {
                GesturesQuickPanel.PhrasePriorities.Add(ViewBackpackPhrase, 84);
            }

            HashSet<EPhraseTrigger> availablePhrases = QuickPanelAvailablePhrasesField.GetValue(panel) as HashSet<EPhraseTrigger>;
            availablePhrases?.Add(ViewBackpackPhrase);
        }

        private static void RefreshViewBackpackQuickCommand(GesturesQuickPanel panel, Player player)
        {
            EnsureViewBackpackQuickCommand(panel);
            panel.SetPhraseActive(ViewBackpackPhrase, TeammateBackpackInspection.CanShowQuickInteraction(player));
        }
    }

    internal class QuickPanelHurtPhrasePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GesturesQuickPanel), nameof(GesturesQuickPanel.SetPhraseActive));
        }

        [PatchPrefix]
        private static void PatchPrefix(EPhraseTrigger phrase, ref bool active)
        {
            if (!QuickPanelHurtPhraseFilter.IsBlocked(phrase))
            {
                return;
            }

            // Hurt statuses have higher stock priority than interaction prompts, so keep them out
            // of the player's quick-command panel entirely while leaving actual voice playback alone.
            active = false;
        }
    }

    internal class QuickPanelUpdateBackpackInteractionPatch : ModulePatch
    {
        private static readonly FieldInfo QuickPanelPlayerField = AccessTools.Field(typeof(GesturesQuickPanel), "_player");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GesturesQuickPanel), nameof(GesturesQuickPanel.Update));
        }

        [PatchPostfix]
        private static void PatchPostfix(GesturesQuickPanel __instance)
        {
            // The stock quick panel refreshes situational phrases mostly from interaction-change events. Our
            // "looked at follower" condition can change every frame without a stock event, so refresh it here
            // to avoid stale VIEW BACKPACK prompts.
            Player player = QuickPanelPlayerField.GetValue(__instance) as Player;
            if (player == null)
            {
                return;
            }

            QuickPanelHurtPhraseFilter.RemoveBlockedCommands(__instance);

            EPhraseTrigger viewBackpackPhrase = (EPhraseTrigger)CustomPhrases.ViewBackpack;
            HashSet<EPhraseTrigger> availablePhrases = AccessTools.Field(typeof(GesturesQuickPanel), "_availablePhrases").GetValue(__instance) as HashSet<EPhraseTrigger>;
            availablePhrases?.Add(viewBackpackPhrase);

            if (!GesturesQuickPanel.PhrasePriorities.ContainsKey(viewBackpackPhrase))
            {
                GesturesQuickPanel.PhrasePriorities.Add(viewBackpackPhrase, 84);
            }

            __instance.SetPhraseActive(viewBackpackPhrase, TeammateBackpackInspection.CanShowQuickInteraction(player));
        }
    }
}
