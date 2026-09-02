using EFT;

using EFT.InventoryLogic;
using pitTeam;
using pitTeam.Modules;
using HarmonyLib;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using pitTeam.BigBrain;
using Unity.Collections;
using UnityEngine;

namespace pitTeam.Patches
{
    internal class SAINPatch
    {
        private static Type? squadType = null;
        private static Type? SAINEnableClass = null;
        private static Type? combatSoloLayerType = null;
        private static Type? combatSquadLayerType = null;
        private static Type? avoidThreatLayerType = null;
        private static Type? extractLayerType = null;
        private static Type? flashBangedLayerType = null;
        private static Type? debugLayerType = null;
        private static Type? sainEnemyControllerType = null;

        private static Type? enemyTalk = null;
        private static Type? GroupClass = null;
        private static Type? sainPlayerTalkPatch = null;
        private static Type? sainBotTalkPatch = null;
        private static Type? sainBotTalkManualUpdatePatch = null;
        private static Type? sainBotTalkClassType = null;
        private static Type? sainPlayerComponentType = null;
        private static Type? sainMoverClass = null;
        private static Type? selfActionDecisionClassType = null;
        private static Type? sainShootDataType = null;
        private static Type? sainVisionRaycastJobType = null;
        private static PropertyInfo? sainPlayerComponentPlayerProperty = null;
        private static FieldInfo? sainVisionRaycastEnemiesField = null;
        private static FieldInfo? sainVisionRaycastColliderTypesField = null;
        private static FieldInfo? sainVisionRaycastCastPointsField = null;
        private const float FollowerCloseFoliageMaskDistance = 10f;
        private static readonly QueryParameters sainDefaultLosParams = new QueryParameters(LayersMaskController.HighPolyWithTerrainNoGrassMask);
        private static readonly QueryParameters sainDefaultVisionParams = new QueryParameters(LayersMaskController.AI);
        private static readonly QueryParameters sainDefaultShootParams = new QueryParameters(LayersMaskController.HighPolyWithTerrainMaskAI);
        private static readonly QueryParameters sainCloseFollowerParams = new QueryParameters(LayersMaskController.HighPolyWithTerrainMask);
        public static void PatchSAINIfInstalled(Harmony harmony)
        {
            if (!pitFireTeam.IsSAINInstalled) return;

            if (squadType == null)
            {
                squadType = Type.GetType("SAIN.BotController.Classes.Squad, SAIN");
            }

            if (SAINEnableClass == null)
            {
                SAINEnableClass = Type.GetType("SAIN.SAINEnableClass, SAIN");
            }


            if (enemyTalk == null)
            {
                enemyTalk = Type.GetType("SAIN.SAINComponent.Classes.Talk.EnemyTalk, SAIN");

            }

            if (GroupClass == null)
            {
                GroupClass = Type.GetType("SAIN.SAINComponent.Classes.Talk.GroupTalk, SAIN");
            }

            if (sainPlayerTalkPatch == null)
            {
                sainPlayerTalkPatch = Type.GetType("SAIN.Patches.Talk.PlayerTalkPatch, SAIN");
            }

            if (sainBotTalkPatch == null)
            {
                sainBotTalkPatch = Type.GetType("SAIN.Patches.Talk.BotTalkPatch, SAIN");
            }

            if (sainBotTalkManualUpdatePatch == null)
            {
                sainBotTalkManualUpdatePatch = Type.GetType("SAIN.Patches.Talk.BotTalkManualUpdatePatch, SAIN");
            }

            if (sainBotTalkClassType == null)
            {
                sainBotTalkClassType = Type.GetType("SAIN.SAINComponent.Classes.Talk.SAINBotTalkClass, SAIN");
            }

            if (sainPlayerComponentType == null)
            {
                sainPlayerComponentType = Type.GetType("SAIN.Components.PlayerComponentSpace.PlayerComponent, SAIN");
                sainPlayerComponentPlayerProperty = sainPlayerComponentType?.GetProperty("Player");
            }

            if (enemyTalk != null)
            {
                harmony.Patch(AccessTools.Method(enemyTalk, "playerTalked"), new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchPlayerTalked), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
            }

            if (GroupClass != null)
            {
                harmony.Patch(AccessTools.Method(GroupClass, "EnemyConversation"), new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchEnemyConvesation), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
            }

            PatchFollowerLayerFallbackIfAddonMissing(harmony);
            PatchFollowerEnemyClearGuardIfAddonMissing(harmony);
            PatchFollowerCombatPatrolStanceWithoutAddon(harmony);
            PatchFollowerReloadBlockIfAddonMissing(harmony);
            PatchFollowerWeaponSelectionGuard(harmony);
            FollowerSainCenterMassPatch.Apply(harmony);
            PatchSainTalkPrefixesForFollowers(harmony);
            PatchSainTalkGenerationForFollowers(harmony);
            PatchSainPlayerVoiceLineForFollowers(harmony);
            PatchFollowerCloseFoliageVision(harmony);
            FollowerSainProficiency.ApplyPatches(harmony);


            if (squadType != null && SAINEnableClass != null)
            {
                var assignLeader = AccessTools.Method(squadType, "assignSquadLeader");
                if (assignLeader != null)
                {
                    harmony.Patch(assignLeader, new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchAssignSquadLeader), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
                }
                Modules.Logger.LogInfo("SAIN Patched");
            }
        }

        private static void PatchFollowerCloseFoliageVision(Harmony harmony)
        {
            sainVisionRaycastJobType ??= Type.GetType("SAIN.Components.VisionRaycastJob, SAIN");
            MethodInfo? createCommands = sainVisionRaycastJobType != null
                ? AccessTools.Method(sainVisionRaycastJobType, "CreateCommands")
                : null;
            if (createCommands == null)
            {
                return;
            }

            sainVisionRaycastEnemiesField ??= AccessTools.Field(sainVisionRaycastJobType, "_enemies");
            sainVisionRaycastColliderTypesField ??= AccessTools.Field(sainVisionRaycastJobType, "_colliderTypes");
            sainVisionRaycastCastPointsField ??= AccessTools.Field(sainVisionRaycastJobType, "_castPoints");
            if (sainVisionRaycastEnemiesField == null ||
                sainVisionRaycastColliderTypesField == null ||
                sainVisionRaycastCastPointsField == null)
            {
                return;
            }

            harmony.Patch(
                createCommands,
                prefix: new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(CreateFollowerAwareSainVisionRaycastCommands), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static void PatchFollowerCombatPatrolStanceWithoutAddon(Harmony harmony)
        {
            sainMoverClass ??= Type.GetType("SAIN.SAINComponent.Classes.Mover.SAINMoverClass, SAIN");

            MethodInfo? updateStance = sainMoverClass != null
                ? AccessTools.Method(sainMoverClass, "UpdateStance", new[] { typeof(float) })
                : null;
            if (updateStance != null)
            {
                harmony.Patch(updateStance, postfix: new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(ForcePatrolOffForFollowerCombat), BindingFlags.NonPublic | BindingFlags.Static)));
            }
        }

        private static void PatchFollowerReloadBlockIfAddonMissing(Harmony harmony)
        {
            selfActionDecisionClassType ??= Type.GetType("SAIN.SAINComponent.Classes.Decision.SelfActionDecisionClass, SAIN");
            if (selfActionDecisionClassType == null)
            {
                return;
            }

            MethodInfo? tryReload = AccessTools.GetDeclaredMethods(selfActionDecisionClassType)
                .FirstOrDefault(method =>
                    method.Name == "TryReload" &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType == typeof(BotOwner));

            if (tryReload != null)
            {
                harmony.Patch(
                    tryReload,
                    prefix: new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(BlockFollowerSainReloadIfAddonMissing), BindingFlags.NonPublic | BindingFlags.Static)));
            }
        }

        private static void PatchFollowerWeaponSelectionGuard(Harmony harmony)
        {
            sainShootDataType ??= Type.GetType("SAIN.SAINComponent.Classes.SAINShootData, SAIN");
            if (sainShootDataType == null)
            {
                return;
            }

            MethodInfo? tryChangeWeapon = AccessTools.Method(sainShootDataType, "TryChangeWeapon", new[] { typeof(EquipmentSlot) });
            if (tryChangeWeapon != null)
            {
                harmony.Patch(
                    tryChangeWeapon,
                    prefix: new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(GuardFollowerSainWeaponSelection), BindingFlags.NonPublic | BindingFlags.Static)));
            }
        }

        private static void PatchFollowerLayerFallbackIfAddonMissing(Harmony harmony)
        {
            combatSoloLayerType ??= Type.GetType("SAIN.Layers.Combat.Solo.CombatSoloLayer, SAIN");
            combatSquadLayerType ??= Type.GetType("SAIN.Layers.Combat.Squad.CombatSquadLayer, SAIN");
            avoidThreatLayerType ??= Type.GetType("SAIN.Layers.SAINAvoidThreatLayer, SAIN");
            extractLayerType ??= Type.GetType("SAIN.Layers.ExtractLayer, SAIN");
            flashBangedLayerType ??= Type.GetType("SAIN.Layers.Flashed.SAINFlashedLayer, SAIN");
            debugLayerType ??= Type.GetType("SAIN.Layers.Combat.Run.DebugLayer, SAIN");

            var disablePrefix = new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(DisableSainLayerForFollowersWithoutAddon), BindingFlags.NonPublic | BindingFlags.Static));

            PatchLayerIsActive(harmony, combatSoloLayerType, disablePrefix);
            PatchLayerIsActive(harmony, combatSquadLayerType, disablePrefix);
            PatchLayerIsActive(harmony, avoidThreatLayerType, disablePrefix);
            PatchLayerIsActive(harmony, extractLayerType, disablePrefix);
            PatchLayerIsActive(harmony, flashBangedLayerType, disablePrefix);
            PatchLayerIsActive(harmony, debugLayerType, disablePrefix);
        }

        private static void PatchFollowerEnemyClearGuardIfAddonMissing(Harmony harmony)
        {
            sainEnemyControllerType ??= Type.GetType("SAIN.SAINComponent.Classes.EnemyClasses.SAINEnemyController, SAIN");
            MethodInfo? clearEnemy = sainEnemyControllerType != null
                ? AccessTools.Method(sainEnemyControllerType, "ClearEnemy")
                : null;
            if (clearEnemy == null)
            {
                return;
            }

            harmony.Patch(
                clearEnemy,
                prefix: new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(BlockFollowerSainClearEnemyIfRetained), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        private static void PatchLayerIsActive(Harmony harmony, Type? layerType, HarmonyMethod disablePrefix)
        {
            MethodInfo? isActive = layerType != null ? AccessTools.Method(layerType, "IsActive") : null;
            if (isActive != null)
            {
                harmony.Patch(isActive, prefix: disablePrefix);
            }
        }

        private static void PatchSainTalkPrefixesForFollowers(Harmony harmony)
        {
            var bypassPrefix = new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(BypassSainTalkPatchForFollower), BindingFlags.NonPublic | BindingFlags.Static));

            if (sainPlayerTalkPatch != null)
            {
                MethodInfo? patchPrefix = AccessTools.Method(sainPlayerTalkPatch, "PatchPrefix");
                if (patchPrefix != null)
                {
                    harmony.Patch(patchPrefix, prefix: bypassPrefix);
                }
            }

            if (sainBotTalkPatch != null)
            {
                MethodInfo? patchPrefix = AccessTools.Method(sainBotTalkPatch, "PatchPrefix");
                if (patchPrefix != null)
                {
                    harmony.Patch(patchPrefix, prefix: bypassPrefix);
                }
            }

            if (sainBotTalkManualUpdatePatch != null)
            {
                MethodInfo? patchPrefix = AccessTools.Method(sainBotTalkManualUpdatePatch, "PatchPrefix");
                if (patchPrefix != null)
                {
                    harmony.Patch(patchPrefix, prefix: bypassPrefix);
                }
            }
        }

        private static void PatchSainPlayerVoiceLineForFollowers(Harmony harmony)
        {
            MethodInfo? playVoiceLine =
                sainPlayerComponentType != null
                ? AccessTools.Method(sainPlayerComponentType, "PlayVoiceLine", new[] { typeof(EPhraseTrigger), typeof(ETagStatus), typeof(bool) })
                : null;

            if (playVoiceLine != null)
            {
                harmony.Patch(
                    playVoiceLine,
                    prefix: new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(UseVanillaTalkForFollower), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            else
            {
                Modules.Logger.LogError("Failed to find SAIN 4.5 PlayerComponent.PlayVoiceLine for follower vanilla-talk routing.");
            }
        }

        private static void PatchSainTalkGenerationForFollowers(Harmony harmony)
        {
            if (sainBotTalkClassType == null)
            {
                Modules.Logger.LogError("Failed to find SAIN 4.5 SAINBotTalkClass for follower vanilla-talk routing.");
                return;
            }

            MethodInfo? manualUpdate = AccessTools.Method(sainBotTalkClassType, "ManualUpdate", Type.EmptyTypes);
            MethodInfo? say = AccessTools.Method(
                sainBotTalkClassType,
                "Say",
                new[] { typeof(EPhraseTrigger), typeof(ETagStatus?), typeof(bool), typeof(bool) });

            if (manualUpdate != null)
            {
                harmony.Patch(
                    manualUpdate,
                    prefix: new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(DisableSainTalkUpdateForFollower), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            else
            {
                Modules.Logger.LogError("Failed to find SAIN 4.5 SAINBotTalkClass.ManualUpdate for follower vanilla-talk routing.");
            }

            if (say != null)
            {
                harmony.Patch(
                    say,
                    prefix: new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(DisableSainTalkSayForFollower), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            else
            {
                Modules.Logger.LogError("Failed to find SAIN 4.5 SAINBotTalkClass.Say for follower vanilla-talk routing.");
            }
        }

        [HarmonyPrefix]
        private static bool BypassSainTalkPatchForFollower(object[] __args, ref bool __result)
        {
            try
            {
                if (__args == null || __args.Length == 0) return true;

                BotOwner? botOwner = null;

                if (__args[0] is Player player)
                {
                    if (!player.IsAI) return true;
                    botOwner = player.AIData?.BotOwner;
                }
                else if (__args[0] is BotTalk botTalk)
                {
                    botOwner = botTalk._owner;
                }

                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                // Skip SAIN talk-patch logic for followers so EFT/plugin talk behavior can run.
                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPrefix]
        private static bool PatchPlayerTalked(EPhraseTrigger phrase, ETagStatus mask, Player player)
        {
            if (phrase == (EPhraseTrigger)CustomPhrases.TeamStatus)
            {
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool CreateFollowerAwareSainVisionRaycastCommands(
            object __instance,
            NativeArray<RaycastCommand> raycastCommands,
            int enemyCount)
        {
            try
            {
                if (sainVisionRaycastEnemiesField?.GetValue(__instance) is not IList enemies ||
                    sainVisionRaycastColliderTypesField?.GetValue(__instance) is not IList colliderTypes ||
                    sainVisionRaycastCastPointsField?.GetValue(__instance) is not IList castPoints)
                {
                    return true;
                }

                colliderTypes.Clear();
                castPoints.Clear();

                int commands = 0;
                const float minDist = 0.01f;
                const float padding = 0.05f;

                for (int i = 0; i < enemyCount; i++)
                {
                    object? enemy = enemies[i];
                    object? bot = GetMemberValue(enemy, "Bot");
                    BotOwner? botOwner = GetMemberValue(bot, "BotOwner") as BotOwner;
                    object? botTransform = GetMemberValue(bot, "Transform");
                    if (bot == null ||
                        botTransform == null ||
                        !TryGetVectorMember(botTransform, "EyePosition", out Vector3 eyePosition))
                    {
                        return true;
                    }

                    object? weaponData = GetMemberValue(botTransform, "WeaponData");
                    if (!TryGetVectorMember(weaponData, "FirePort", out Vector3 weaponFirePort))
                    {
                        weaponFirePort = eyePosition;
                    }

                    object? vision = GetMemberValue(enemy, "Vision");
                    object? enemyParts = GetMemberValue(vision, "EnemyParts");
                    if (GetMemberValue(enemyParts, "PartsArray") is not Array parts)
                    {
                        return true;
                    }

                    bool follower = botOwner != null && BossPlayers.IsFollower(botOwner);
                    for (int j = 0; j < parts.Length; j++)
                    {
                        object? part = parts.GetValue(j);
                        MethodInfo? getRaycast = part != null ? AccessTools.Method(part.GetType(), "GetRaycast") : null;
                        object? raycastData = getRaycast?.Invoke(part, null);
                        if (raycastData == null ||
                            !TryGetVectorMember(raycastData, "CastPoint", out Vector3 castPoint))
                        {
                            return true;
                        }

                        object? colliderType = GetMemberValue(raycastData, "ColliderType");
                        if (colliderType == null)
                        {
                            return true;
                        }

                        colliderTypes.Add(colliderType);
                        castPoints.Add(castPoint);

                        Vector3 eyeVec = castPoint - eyePosition;
                        float eyeMag = eyeVec.magnitude;
                        Vector3 eyeDir = eyeMag > 1e-6f ? eyeVec / eyeMag : Vector3.forward;
                        float eyeDist = Mathf.Max(eyeMag, minDist);

                        Vector3 weaponVec = castPoint - weaponFirePort;
                        float weaponMag = weaponVec.magnitude;
                        Vector3 weaponDir = weaponMag > 1e-6f ? weaponVec / weaponMag : Vector3.forward;
                        float weaponDist = Mathf.Max(weaponMag, minDist);

                        bool closeFollowerCheck = follower && eyeMag <= FollowerCloseFoliageMaskDistance;
                        QueryParameters losParams = closeFollowerCheck ? sainCloseFollowerParams : sainDefaultLosParams;
                        QueryParameters visionParams = closeFollowerCheck ? sainCloseFollowerParams : sainDefaultVisionParams;
                        QueryParameters shootParams = closeFollowerCheck ? sainCloseFollowerParams : sainDefaultShootParams;

                        raycastCommands[commands++] = new RaycastCommand(eyePosition, eyeDir, losParams, eyeDist + padding);
                        raycastCommands[commands++] = new RaycastCommand(eyePosition, eyeDir, visionParams, eyeDist + padding);
                        raycastCommands[commands++] = new RaycastCommand(weaponFirePort, weaponDir, shootParams, weaponDist + padding);
                    }
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static object? GetMemberValue(object? instance, string name)
        {
            if (instance == null)
            {
                return null;
            }

            Type type = instance.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo? property = type.GetProperty(name, flags);
            if (property != null)
            {
                return property.GetValue(instance);
            }

            FieldInfo? field = type.GetField(name, flags);
            return field?.GetValue(instance);
        }

        private static bool TryGetVectorMember(object? instance, string name, out Vector3 value)
        {
            object? member = GetMemberValue(instance, name);
            if (member is Vector3 vector)
            {
                value = vector;
                return true;
            }

            value = default;
            return false;
        }

        [HarmonyPrefix]
        private static bool PatchEnemyConvesation(EPhraseTrigger trigger, ETagStatus status, Player player)
        {
            return PatchPlayerTalked(trigger, status, player);
        }

        [HarmonyPrefix]
        private static bool UseVanillaTalkForFollower(object __instance, ref bool __result)
        {
            try
            {
                Player? player = sainPlayerComponentPlayerProperty?.GetValue(__instance) as Player;
                BotOwner? botOwner = player?.AIData?.BotOwner;
                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                // SAIN's vanilla-talk option is global. Emulate it only for pitFireTeam followers:
                // its BotTalk patch prefixes are bypassed above so EFT/plugin speech remains active,
                // while this SAIN-only output path is suppressed. The configured follower talk gate
                // can therefore remain the single authority over vanilla combat trash talk.
                __result = false;
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPrefix]
        private static bool DisableSainTalkUpdateForFollower(object __instance)
        {
            try
            {
                return !IsSainTalkOwnerFollower(__instance);
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPrefix]
        private static bool DisableSainTalkSayForFollower(object __instance, ref bool __result)
        {
            try
            {
                if (!IsSainTalkOwnerFollower(__instance))
                {
                    return true;
                }

                __result = false;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsSainTalkOwnerFollower(object instance)
        {
            BotOwner? botOwner = GetMemberValue(instance, "BotOwner") as BotOwner;
            return botOwner != null && BossPlayers.IsFollower(botOwner);
        }

        [HarmonyPrefix]
        private static bool PatchAssignSquadLeader(object __instance, object sain)
        {
            try
            {
                if (sain == null) return true;

                var botOwnerProp = AccessTools.Property(sain.GetType(), "BotOwner");
                var botOwner = botOwnerProp?.GetValue(sain) as BotOwner;
                if (botOwner == null) return true;

                if (BossPlayers.IsFollower(botOwner))
                {
                    return false;
                }
            }
            catch
            {
                return true;
            }
            return true;
        }

        [HarmonyPrefix]
        private static bool DisableSainLayerForFollowersWithoutAddon(object __instance, ref bool __result)
        {
            if (!pitFireTeam.ShouldDisableSainForFollowers)
            {
                return true;
            }

            try
            {
                BotOwner? botOwner = AccessTools.Property(__instance.GetType(), "BotOwner")?.GetValue(__instance) as BotOwner;
                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                __result = false;
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPrefix]
        private static bool BlockFollowerSainClearEnemyIfRetained(object __instance)
        {
            if (!pitFireTeam.ShouldDisableSainForFollowers)
            {
                return true;
            }

            try
            {
                BotOwner? botOwner = AccessTools.Property(__instance.GetType(), "BotOwner")?.GetValue(__instance) as BotOwner;
                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                return !FollowerContactEnemyRetention.HasActiveRetainedContact(botOwner);
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPostfix]
        private static void ForcePatrolOffForFollowerCombat(object __instance)
        {
            if (!pitFireTeam.ShouldDisableSainForFollowers)
            {
                return;
            }

            try
            {
                BotOwner? botOwner = AccessTools.Property(__instance.GetType(), "BotOwner")?.GetValue(__instance) as BotOwner;
                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return;
                }

                MovementContext movementContext = botOwner.GetPlayer?.MovementContext;
                if (movementContext == null || !movementContext._isInPatrol)
                {
                    return;
                }

                if (FollowerCombatLayer.IsFollowerCombatLayerActive(botOwner) || botOwner.Memory?.HaveEnemy == true)
                {
                    movementContext.SetPatrol(false);
                }
            }
            catch
            {
            }
        }

        [HarmonyPrefix]
        private static bool BlockFollowerSainReloadIfAddonMissing(BotOwner botOwner)
        {
            if (!pitFireTeam.ShouldDisableSainForFollowers)
            {
                return true;
            }

            try
            {
                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                // SAIN self-action reload can call into BotReload.CanReload(), which in turn
                // can trigger a forced switch back to the main weapon. Keep the rest of SAIN's
                // decision tick alive so enemy acquisition and other passive bookkeeping still run.
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPrefix]
        private static bool GuardFollowerSainWeaponSelection(object __instance, EquipmentSlot slot)
        {
            if (!pitFireTeam.IsSAINInstalled)
            {
                return true;
            }

            try
            {
                BotOwner? botOwner = AccessTools.Property(__instance.GetType(), "BotOwner")?.GetValue(__instance) as BotOwner;
                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
