using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using pitTeam.Components;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace pitTeam.Patches
{
    /** On extracting via transit point, remember bots the player was with so we can re-spawn them **/
    internal class TransitPointPatch : ModulePatch
    {
        private const string LabyrinthLocationId = "6733700029c367a3d40b02af";
        private const string LabyrinthLocationName = "Labyrinth";

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TransitPoint), "method_6");
        }

        [PatchPostfix]
        private static void PatchPostfix(TransitPoint __instance,
                        string groupId,
                        HashSet<string> __result,
                        HashSet<string> singlePlayers,
                        HashSet<string> partyPlayers,
                        ref bool partyIsFull)
        {
            partyIsFull = true;

            IEnumerable<IPlayer> groupPlayers = Singleton<GameWorld>.Instance.GroupPlayers(groupId, null);

            Utils.SpawnHelper.spawnMemberIdsScav.Clear();
            Utils.SpawnHelper.spawnMemberIds.Clear();
            Utils.SpawnHelper.spawnMemberIdsBoss.Clear();
            FollowerTransitStateCache.Clear();

            if (IsLabyrinthTransit(__instance))
            {
                Modules.Logger.LogInfo(
                    $"[Transit] Target '{__instance?.parameters?.location ?? __instance?.parameters?.target ?? "unknown"}' does not allow teammate transit. " +
                    "Squadmates will resolve as escaped with their carried loot.");
                return;
            }

            Dictionary<string, List<string>> protectedEquipmentByProfileId = InteractableObjects.GetStoredEquipment();

            foreach (IPlayer player in groupPlayers)
            {
                if (BossPlayers.IsPlayerBoss(player.ProfileId))
                {
                    pitAIBossPlayer boss = BossPlayers.GetBoss(player.ProfileId);
                    BossPlayers.GetFollowersByBoss(player.ProfileId).ForEach(follower =>
                    {
                        BotOwner bot = follower?.GetBot();

                        if (follower != null && bot != null && !bot.IsDead &&
                            (
                                follower.IsSquadMate ||
                                (Utils.Props.BossFollowersType.Contains(bot.Profile.Info.Settings.Role) && !Utils.Utils.FlagGet("questGoons"))
                            )
                        )
                        {
                            protectedEquipmentByProfileId.TryGetValue(bot.ProfileId, out List<string> protectedEquipmentIds);
                            List<string> trackedReturnItemIds = InteractableObjects.GetStoredItems(bot.ProfileId);
                            Profile transitProfile = FollowerTransitStateCache.TryCapture(bot, protectedEquipmentIds, trackedReturnItemIds, out Profile capturedProfile)
                                ? capturedProfile
                                : bot.Profile;
                            InfoClass info = transitProfile.Info;

                            bool isBoss = Utils.Props.BossFollowersType.Contains(transitProfile.Info.Settings.Role);

                            LastPlayerStateClass playerVisualization = new LastPlayerStateClass(new GClass1410
                            {
                                Level = isBoss ? 60 : info.Level,
                                MemberCategory = isBoss ? EMemberCategory.Sherpa : ((info.Side != EPlayerSide.Savage) ? info.SelectedMemberCategory : EMemberCategory.Default),
                                SelectedMemberCategory = isBoss ? EMemberCategory.Sherpa : info.SelectedMemberCategory,
                                Nickname = info.Nickname,
                                Side = info.Side,
                                Health = transitProfile.Health
                            }, transitProfile.Customization, transitProfile.Inventory.Equipment);

                            MainMenuControllerPatch.TransitPlayers.Add(new GroupPlayerViewModelClass(new GroupPlayerDataClass
                            {
                                AccountId = bot.AccountId,
                                Id = transitProfile.Id,
                                Info = new GClass1410
                                {
                                    Level = isBoss ? 60 : info.Level,
                                    PrestigeLevel = info.PrestigeLevel,
                                    MemberCategory = isBoss ? EMemberCategory.Sherpa : info.MemberCategory,
                                    SelectedMemberCategory = isBoss ? EMemberCategory.Sherpa : info.SelectedMemberCategory,
                                    Nickname = transitProfile.GetCorrectedNickname(),
                                    Side = info.Side,
                                    SavageLockTime = player.Profile.Info.SavageLockTime,
                                    SavageNickname = player.Profile.Info.Nickname,
                                    GameVersion = info.GameVersion,
                                    HasCoopExtension = info.HasCoopExtension,
                                    Health = transitProfile.Health,
                                },
                                PlayerVisualRepresentation = playerVisualization
                            }));

                            if (player.Profile.Side == EPlayerSide.Savage) Utils.SpawnHelper.spawnMemberIdsScav.Add(bot.AccountId);
                            else if (isBoss) Utils.SpawnHelper.spawnMemberIdsBoss.Add(transitProfile.Info.Settings.Role);
                            else Utils.SpawnHelper.spawnMemberIds.Add(bot.AccountId);

                            Modules.Logger.LogInfo("Transitioning with " + transitProfile.Nickname);

                            Utils.Utils.FlagSet("RaidTransit", true);
                            Utils.SpawnHelper.ScavSquadSize = 0;
                        }
                    });

                }
            }
        }

        private static bool IsLabyrinthTransit(TransitPoint transitPoint)
        {
            LocationSettingsClass.Location.TransitParameters parameters = transitPoint?.parameters;
            if (parameters == null) return false;

            return string.Equals(parameters.target, LabyrinthLocationId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parameters.location, LabyrinthLocationName, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal class TransitWeaponHeatVisualPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(WeaponPrefab),
                nameof(WeaponPrefab.InitHotObjects),
                new Type[] { typeof(Weapon) });
        }

        [PatchPostfix]
        private static void PatchPostfix(WeaponPrefab __instance, Weapon weapon)
        {
            FollowerTransitStateCache.ResetTransitWeaponHeatVisuals(__instance, weapon);
        }
    }
}
