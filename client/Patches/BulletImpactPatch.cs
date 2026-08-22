using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;
using Systems.Effects;
using UnityEngine;

using pitTeam.Modules;
using pitTeam.Utils;

namespace pitTeam.Patches
{
    // patch to detect nearby bullet impacts
    public class BulletImpactPatch : ModulePatch
    {
        private static readonly bool EnableReactionTrace = false;
        private const float BulletReactCooldownSeconds = 0.1f;
        private static readonly Dictionary<int, float> BulletReactionCooldownUntil = new Dictionary<int, float>();

        private static int GetReactionKey(string botProfileId, string shooterProfileId)
        {
            unchecked
            {
                int h1 = botProfileId != null ? botProfileId.GetHashCode() : 0;
                int h2 = shooterProfileId != null ? shooterProfileId.GetHashCode() : 0;
                return (h1 * 397) ^ h2;
            }
        }
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EffectsCommutator), "PlayHitEffect");
        }

        [PatchPostfix]
        public static void PatchPostfix(EffectsCommutator __instance, EFT.Ballistics.Shot info, EFT.PlayerHitInfo playerHitInfo)
        {
            if (info == null) return;
            bool processed = __instance.IsHitPointAlreadyProcessed(info.HitPoint);

            if (info.Player != null)
            {
                int totalFollowers = 0;
                int forwarded = 0;
                string shooterProfileId = info.PlayerProfileID;
                var followers = BossPlayers.GetFollowers();
                foreach (var follower in followers)
                {
                    totalFollowers++;
                    BotOwner bot = follower.GetBot();
                    if (bot == null || bot.IsDead || bot.BotState != EBotState.Active || !BossPlayers.IsFollower(bot))
                    {
                        continue;
                    }

                    int key = GetReactionKey(bot.ProfileId, shooterProfileId);
                    if (BulletReactionCooldownUntil.TryGetValue(key, out float cooldownUntil) && Time.time < cooldownUntil)
                    {
                        continue;
                    }

                    BulletReactionCooldownUntil[key] = Time.time + BulletReactCooldownSeconds;
                    forwarded++;
                    FollowerAwareness.BulletFelt(bot, info, info.HitPoint);
                }
                if (EnableReactionTrace)
                {
                    Modules.Logger.LogInfo($"[ReactTrace] BulletImpact shooter={info.PlayerProfileID} processed={processed} followers={totalFollowers} forwarded={forwarded} hit={info.HitPoint}");
                }
            }
        }
    }
}
