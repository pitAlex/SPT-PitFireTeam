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
    internal class QuickPanelPatch : ModulePatch
    {
        private static readonly EPhraseTrigger ViewBackpackPhrase = (EPhraseTrigger)CustomPhrases.ViewBackpack;
        private static readonly FieldInfo QuickPanelAvailablePhrasesField = AccessTools.Field(typeof(GesturesQuickPanel), "hashSet_0");
        private static readonly FieldInfo QuickPanelPlayerField = AccessTools.Field(typeof(GesturesQuickPanel), "player_0");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GesturesQuickPanel), "method_1");
        }
        /** Patch QuickGesturesPanel to disable the "Cooperative" phrase if bot is a follower **/
        [PatchPrefix]
        private static bool PatchPrefix(GesturesQuickPanel __instance)
        {
            Player player = QuickPanelPlayerField.GetValue(__instance) as Player;
            if (player != null)
            {
                RefreshViewBackpackQuickCommand(__instance, player);

                try
                {
                    // original
                    LootItem? lootItem = player.InteractableObject as LootItem;
                    bool flag = lootItem != null && lootItem.ItemOwner.RootItem.GetItemComponent<KeyComponent>() != null;
                    bool flag2 = lootItem != null && lootItem.ItemOwner.RootItem is MoneyItemClass;
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
                    __instance.method_7(EPhraseTrigger.LootKey, flag);
                    __instance.method_7(EPhraseTrigger.LootMoney, flag2);
                    __instance.method_7(EPhraseTrigger.LootWeapon, flag3);
                    __instance.method_7(EPhraseTrigger.LootGeneric, corpse == null && lootContainer == null && lootItem != null && !flag && !flag2 && !flag3);
                    // Body phrases are routed to a follower body-gear recovery command, not vanilla bot corpse work.
                    __instance.method_7(EPhraseTrigger.LootBody, corpse != null);
                    __instance.method_7(EPhraseTrigger.CheckHim, corpse != null);
                    __instance.method_7(EPhraseTrigger.LootContainer, canLootContainer);
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
                    __instance.method_7(EPhraseTrigger.OpenDoor, canOpen);
                }
                catch (Exception e)
                {
                    Logger.LogError("Open Door Command Failed:");
                    Logger.LogError(e);
                }

                // original
                __instance.method_7(EPhraseTrigger.LockedDoor, door != null && (door.DoorState == EDoorState.Locked || door.DoorState == EDoorState.Shut));

                // Show cooperation for any alive non-follower AI target.
                try
                {
                    if (!pitFireTeam.pickupEnabled.Value)
                    {
                        __instance.method_7(EPhraseTrigger.Cooperation, false);
                        return false;
                    }

                    if (player.InteractablePlayer != null && player.InteractablePlayer.IsAI && player.InteractablePlayer.HealthController.IsAlive)
                    {
                        BotOwner targetBot = player.InteractablePlayer.AIData?.BotOwner;
                        if (targetBot == null || BossPlayers.IsFollower(targetBot) || player.InteractablePlayer.Side != player.Side)
                        {
                            __instance.method_7(EPhraseTrigger.Cooperation, false);

                            return false;
                        }
                        else
                        {
                            __instance.method_7(EPhraseTrigger.Cooperation, true);
                        }
                    }
                    else
                    {
                        __instance.method_7(EPhraseTrigger.Cooperation, false);
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
            panel.method_7(ViewBackpackPhrase, TeammateBackpackInspection.CanShowQuickInteraction(player));
        }
    }

    internal class QuickPanelUpdateBackpackInteractionPatch : ModulePatch
    {
        private static readonly FieldInfo QuickPanelPlayerField = AccessTools.Field(typeof(GesturesQuickPanel), "player_0");

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

            EPhraseTrigger viewBackpackPhrase = (EPhraseTrigger)CustomPhrases.ViewBackpack;
            HashSet<EPhraseTrigger> availablePhrases = AccessTools.Field(typeof(GesturesQuickPanel), "hashSet_0").GetValue(__instance) as HashSet<EPhraseTrigger>;
            availablePhrases?.Add(viewBackpackPhrase);

            if (!GesturesQuickPanel.PhrasePriorities.ContainsKey(viewBackpackPhrase))
            {
                GesturesQuickPanel.PhrasePriorities.Add(viewBackpackPhrase, 84);
            }

            __instance.method_7(viewBackpackPhrase, TeammateBackpackInspection.CanShowQuickInteraction(player));
        }
    }
}
