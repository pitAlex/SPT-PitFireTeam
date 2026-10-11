using System;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    // ApplyShot has already resolved armor here. Patch only the health-entry boundary:
    // health-controller overflow/bleeding must not apply this multiplier a second time.
    internal sealed class FollowerFriendlyFireDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(
            typeof(Player), nameof(Player.ApplyDamageInfo),
            new[] { typeof(DamageInfo), typeof(EBodyPart), typeof(EBodyPartColliderType), typeof(float) });

        [PatchPrefix]
        private static void PatchPrefix(Player __instance, ref DamageInfo damageInfo)
        {
            try
            {
                if (__instance?.HealthController?.IsAlive != true ||
                    damageInfo.Damage <= 0f || float.IsNaN(damageInfo.Damage) || float.IsInfinity(damageInfo.Damage)) return;

                IPlayer attacker = damageInfo.Player?.iPlayer;
                if (attacker == null || attacker.IsAI == __instance.IsAI) return;

                IPlayer human = __instance.IsAI ? attacker : __instance;
                IPlayer teammate = __instance.IsAI ? __instance : attacker;
                BotOwner bot = teammate.AIData?.BotOwner;
                var follower = BossPlayers.GetFollowerByProfileId(teammate.ProfileId);
                var boss = follower?.GetBoss();
                if (bot == null || follower?.GetBot() != bot ||
                    !ReferenceEquals(bot.GetPlayer, teammate) ||
                    !ReferenceEquals(boss?.realPlayer, human) ||
                    !ReferenceEquals(bot.BotFollower?.BossToFollow, boss)) return;

                damageInfo.Damage *= 0.5f;
            }
            catch
            {
                // Lifecycle failures must never prevent native damage processing.
            }
        }
    }
}
