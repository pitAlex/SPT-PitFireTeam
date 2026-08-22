using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace pitTeam.Patches
{
    internal class PatrolDataFollowerUpdateGuardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(PatrolDataFollower), "ManualUpdate");
        }

        [PatchPrefix]
        private static bool PatchPrefix(PatrolDataFollower __instance)
        {
            if (__instance == null)
            {
                return false;
            }

            BotOwner botOwner = Traverse.Create(__instance).Field("_owner").GetValue<BotOwner>();
            if (botOwner == null)
            {
                return false;
            }

            bool isConfirmedFollower = IsConfirmedFollower(botOwner);
            bool hasFollowerBossLink = botOwner.BotFollower?.BossToFollow != null;
            if (!isConfirmedFollower && !hasFollowerBossLink)
            {
                return true;
            }

            IBossToFollow boss = botOwner.BotFollower?.BossToFollow;
            if (boss == null && TryRecoverBoss(botOwner, out pitAIBossPlayer recoveredBoss))
            {
                boss = recoveredBoss;
                botOwner.BotFollower.BossToFollow = recoveredBoss;
            }

            if (boss == null)
            {
                botOwner.StopMove();
                return false;
            }

            if (boss.IsAI)
            {
                // Some AI boss implementations can throw inside MoveSpeed getter when not fully initialized.
                // Probe here and skip vanilla follower patrol update instead of letting it throw every frame.
                if (!CanReadBossMoveSpeed(boss))
                {
                    botOwner.StopMove();
                    return false;
                }

                return __instance.followerAIBase != null;
            }

            if (__instance.followerPlayerBase == null)
            {
                Player bossPlayer = boss.Player() as Player;
                if (bossPlayer == null)
                {
                    botOwner.StopMove();
                    return false;
                }

                __instance.InitPlayer(bossPlayer);
            }

            return __instance.followerPlayerBase != null;
        }

        private static bool CanReadBossMoveSpeed(IBossToFollow boss)
        {
            if (boss == null)
            {
                return false;
            }

            try
            {
                PropertyInfo? moveSpeedProperty = boss.GetType().GetProperty("MoveSpeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (moveSpeedProperty == null)
                {
                    return true;
                }

                _ = moveSpeedProperty.GetValue(boss);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryRecoverBoss(BotOwner botOwner, out pitAIBossPlayer boss)
        {
            boss = null;

            BotFollowerPlayer follower = BossPlayers.Instance?.GetFollower(botOwner);
            if (follower == null)
            {
                return false;
            }

            boss = follower.GetBoss();
            return boss?.realPlayer != null;
        }

        private static bool IsConfirmedFollower(BotOwner botOwner)
        {
            if (botOwner == null) return false;
            if (botOwner.IsDead || botOwner.BotState != EBotState.Active) return false;
            if (botOwner.BotFollower == null) return false;
            return BossPlayers.Instance?.GetFollower(botOwner) != null;
        }
    }

    internal class AvoidDangerFollowerGuardPatch : ModulePatch
    {
        private static System.Func<object, BotOwner> _botOwnerGetter;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(AvoidDangerLayer), "ShallUseNow");
        }

        [PatchPrefix]
        private static bool PatchPrefix(AvoidDangerLayer __instance, ref bool __result)
        {
            if (__instance == null)
            {
                __result = false;
                return false;
            }

            _botOwnerGetter ??= LootPatrolActiveLayerListPatch.BuildBotOwnerGetter(__instance.GetType());
            BotOwner botOwner = _botOwnerGetter?.Invoke(__instance);
            if (!IsConfirmedFollower(botOwner))
            {
                return true;
            }

            if (botOwner.WeaponManager?.Grenades == null ||
                botOwner.ArtilleryDangerPlace == null ||
                botOwner.BewareGrenade == null ||
                botOwner.BewareBTR == null ||
                botOwner.BotTurnAwayLight == null ||
                botOwner.FlashGrenade == null ||
                botOwner.SmokeGrenade == null ||
                botOwner.BewarePlantedMine == null)
            {
                __result = false;
                return false;
            }

            return true;
        }

        [PatchFinalizer]
        private static Exception PatchFinalizer(AvoidDangerLayer __instance, Exception __exception, ref bool __result)
        {
            if (__exception == null)
            {
                return null;
            }

            try
            {
                _botOwnerGetter ??= LootPatrolActiveLayerListPatch.BuildBotOwnerGetter(__instance?.GetType());
                BotOwner botOwner = _botOwnerGetter?.Invoke(__instance);
                if (IsConfirmedFollower(botOwner))
                {
                    __result = false;
                    return null;
                }
            }
            catch
            {
                // If the guard itself cannot identify the bot, let the original exception surface.
            }

            return __exception;
        }

        private static bool IsConfirmedFollower(BotOwner botOwner)
        {
            if (botOwner == null) return false;
            if (botOwner.IsDead || botOwner.BotState != EBotState.Active) return false;
            if (botOwner.BotFollower == null) return false;
            return BossPlayers.Instance?.GetFollower(botOwner) != null;
        }
    }
}
