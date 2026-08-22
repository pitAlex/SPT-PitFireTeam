using Comfort.Common;
using EFT;

using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

using pitTeam.Modules;
using pitTeam.Utils;

namespace pitTeam.Patches
{
    internal static class ReactionRateLimiter
    {
        private static readonly Dictionary<int, float> CooldownUntil = new Dictionary<int, float>();

        public static bool IsSuppressed(string botProfileId, string sourceProfileId, int reactionType, float cooldownSeconds)
        {
            int key;
            unchecked
            {
                int h1 = botProfileId != null ? botProfileId.GetHashCode() : 0;
                int h2 = sourceProfileId != null ? sourceProfileId.GetHashCode() : 0;
                key = ((h1 * 397) ^ h2) * 397 ^ reactionType;
            }
            if (CooldownUntil.TryGetValue(key, out float until) && Time.time < until)
            {
                return true;
            }

            CooldownUntil[key] = Time.time + cooldownSeconds;
            return false;
        }
    }

    internal class HearingSensorPatch : ModulePatch
    {
        private const bool EnableReactionTrace = false;
        private const float StepReactionCooldownSeconds = 0.14f;
        private const float SoundReactionCooldownSeconds = 0.12f;
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotHearingSensor), nameof(BotHearingSensor.OnSoundPlayed));
        }

        [PatchPostfix]
        public static void PatchPostfix(BotHearingSensor __instance, IPlayer player, Vector3 position, float power, AISoundType type)
        {
            BotOwner botOwner_0 = __instance._botOwner;
            if (BossPlayers.IsFollower(botOwner_0) && FollowerEnemyEnforceSuppression.IsSuppressed(botOwner_0))
            {
                return;
            }

            // check if enemy is trying to sneak up on the bot - only during combat
            if (type == AISoundType.step)
            {
                if (BossPlayers.IsFollower(botOwner_0) && player != null && !BossPlayers.IsPlayerBoss(player.ProfileId))
                {
                    if (player.IsAI && BossPlayers.IsFollower(player.AIData.BotOwner)) return;

                    Player person = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(player.ProfileId);
                    if (person == null) return;

                    bool knownEnemy = botOwner_0.EnemiesController.IsEnemy(player) || botOwner_0.BotsGroup.IsEnemy(player);
                    bool hostileToBossGroup = FollowerAwareness.IsHostileToBossGroupForReaction(botOwner_0, person);
                    if (!knownEnemy && !hostileToBossGroup)
                    {
                        Trace(botOwner_0, $"Hearing.step ignore notEnemy src={player.ProfileId}");
                        return;
                    }

                    if (ReactionRateLimiter.IsSuppressed(botOwner_0.ProfileId, player.ProfileId, 1, StepReactionCooldownSeconds))
                    {
                        return;
                    }

                    if (botOwner_0.Memory.HaveEnemy && botOwner_0.Memory.GoalEnemy != null &&
                        botOwner_0.Memory.GoalEnemy.IsVisible && botOwner_0.Memory.GoalEnemy.CanShoot)
                    {
                        Trace(botOwner_0, $"Hearing.step ignore visibleShootable goal={botOwner_0.Memory.GoalEnemy.ProfileId}");
                        return;
                    }

                    bool shouldReact = __instance.IsSoundHeard(position, power, out var distance);
                    if (!shouldReact)
                    {
                        Trace(botOwner_0, $"Hearing.step ignore method6False dist={distance:F1} power={power:F1}");
                    }

                    if (shouldReact && ShouldSuppressThreatSoundReaction(botOwner_0, position))
                    {
                        Trace(botOwner_0, "Hearing.step ignore olderOrWorseSound");
                        shouldReact = false;
                    }

                    if (person != null && shouldReact)
                    {
                        Trace(botOwner_0, $"Hearing.step pass dist={distance:F1} power={power:F1}");
                        FollowerAwareness.SoundHeard(botOwner_0, person, position, distance, type);
                    }
                }

                return;
            }
            else if (BossPlayers.IsFollower(botOwner_0) && player != null && !BossPlayers.IsPlayerBoss(player.ProfileId))
            {
                if (player.IsAI && BossPlayers.IsFollower(player.AIData.BotOwner)) return;
                Player person = Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(player.ProfileId);
                if (person == null) return;

                bool knownEnemy = botOwner_0.EnemiesController.IsEnemy(player) || botOwner_0.BotsGroup.IsEnemy(player);
                bool hostileToBossGroup = FollowerAwareness.IsHostileToBossGroupForReaction(botOwner_0, person);
                if (knownEnemy || hostileToBossGroup)
                {
                    if (ReactionRateLimiter.IsSuppressed(botOwner_0.ProfileId, player.ProfileId, 2, SoundReactionCooldownSeconds))
                    {
                        return;
                    }

                    bool shouldReact = __instance.IsSoundHeard(position, power, out var distance);
                    Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;
                    if (!shouldReact)
                    {
                        Trace(botOwner_0, $"Hearing.{type} ignore method6False dist={distance:F1} power={power:F1}");
                    }
                    if (shouldReact && ShouldSuppressThreatSoundReaction(botOwner_0, position))
                    {
                        Trace(botOwner_0, $"Hearing.{type} ignore olderOrWorseSound");
                        shouldReact = false;
                    }

                    if (shouldReact)
                    {
                        Trace(botOwner_0, $"Hearing.{type} pass dist={distance:F1} power={power:F1}");
                        FollowerAwareness.SoundHeard(botOwner_0, person, position, distance, type);
                    }
                }
            }
        }

        private static void Trace(BotOwner bot, string msg)
        {
            if (!EnableReactionTrace || bot == null) return;
            Modules.Logger.LogInfo($"[ReactTrace] bot={bot.Profile?.Nickname ?? bot.name} {msg}");
        }

        private static bool ShouldSuppressThreatSoundReaction(BotOwner bot, Vector3 soundPosition)
        {
            if (bot?.Memory?.GoalEnemy == null)
            {
                return false;
            }

            EnemyInfo goalEnemy = bot.Memory.GoalEnemy;
            if (!Enemy.TryGetReliableKnownPosition(bot, goalEnemy, out Vector3 knownThreatPosition))
            {
                return false;
            }

            Vector3 botPosition = bot.GetPlayer?.Transform?.position ?? bot.Position;
            float soundDistanceSqr = (soundPosition - botPosition).sqrMagnitude;
            float knownThreatDistanceSqr = (knownThreatPosition - botPosition).sqrMagnitude;
            float suppressionMarginSqr = 4f * 4f;

            return soundDistanceSqr > knownThreatDistanceSqr + suppressionMarginSqr;
        }
    }

    internal class FootstepSoundPatch : ModulePatch
    {
        private const bool EnableReactionTrace = false;
        private const float FootstepReactionCooldownSeconds = 0.18f;
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), "PlayStepSound");
        }

        [PatchPostfix]
        public static void PatchPostfix(Player __instance, BetterSource ___NestedStepSoundSource)
        {
            float volume = __instance.MovementContext.CovertMovementVolumeBySpeed * __instance.CalculateStepSoundVolumeSpeedFactor();
            float range = ___NestedStepSoundSource.MaxDistance * 0.85f;

            if (BossPlayers.IsPlayerBoss(__instance.ProfileId)) return;

            foreach (var follower in BossPlayers.GetFollowers())
            {
                BotOwner bot = follower.GetBot();
                if (bot == null || FollowerEnemyEnforceSuppression.IsSuppressed(bot)) continue;
                if (bot.ProfileId == __instance.ProfileId) continue;
                bool knownEnemy = bot.EnemiesController.IsEnemy(__instance) || bot.BotsGroup.IsEnemy(__instance);
                bool hostileToBossGroup = FollowerAwareness.IsHostileToBossGroupForReaction(bot, __instance);
                if (!knownEnemy && !hostileToBossGroup) continue;
                if (ReactionRateLimiter.IsSuppressed(bot.ProfileId, __instance.ProfileId, 3, FootstepReactionCooldownSeconds)) continue;

                if (bot.Memory.HaveEnemy && bot.Memory.GoalEnemy != null &&
                    bot.Memory.GoalEnemy.IsVisible && bot.Memory.GoalEnemy.CanShoot)
                {
                    continue;
                }

                Vector3 position = __instance.Transform.position;

                float power = range * volume;

                power = Mathf.Min(25f, power);

                float distance = Vector3.Distance(bot.GetPlayer.Transform.position, position);

                bool shouldReact = distance <= power;
                if (!shouldReact)
                {
                    Trace(bot, $"FootstepPatch ignore tooFar src={__instance.ProfileId} dist={distance:F1} power={power:F1}");
                    continue;
                }

                Trace(bot, $"FootstepPatch pass src={__instance.ProfileId} dist={distance:F1} power={power:F1}");
                FollowerAwareness.SoundHeard(bot, __instance, position, distance, AISoundType.step);

            }
        }

        private static void Trace(BotOwner bot, string msg)
        {
            if (!EnableReactionTrace || bot == null) return;
            Modules.Logger.LogInfo($"[ReactTrace] bot={bot.Profile?.Nickname ?? bot.name} {msg}");
        }

    }

    internal class PlayerSayPatch : ModulePatch
    {
        private const bool EnableReactionTrace = false;
        private const float VoiceReactDistance = 40f;
        private const float VoiceReactionCooldownSeconds = 0.3f;
        private static float reported = 0f;
        private static float freq = 0;
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), "Say");
        }

        // Patch Player.Say to make followers turn towards the enemy when they talk
        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {
            if (__instance == null) return;
            if (Time.time < reported || Time.time < freq) return;

            freq = Time.time + 0.5f;

            if (BossPlayers.IsPlayerBoss(__instance.ProfileId)) return;

            bool isfollower = false;
            foreach (var f in BossPlayers.GetFollowers())
            {
                if (f.GetBot().ProfileId == __instance.ProfileId)
                {
                    isfollower = true;
                    break;
                }
            }

            if (isfollower) return;

            bool reportEnemy = false;
            BossPlayers.GetFollowers().ForEach(follower =>
            {
                if (follower == null) return;
                BotOwner bot = follower.GetBot();
                if (bot == null || bot.IsDead || bot.GetPlayer == null || bot.HearingSensor == null) return;
                if (bot.EnemiesController == null || bot.Memory == null || bot.BotsGroup == null) return;
                if (FollowerEnemyEnforceSuppression.IsSuppressed(bot)) return;
                if (FollowerAwareness.WasRecentlyHit(bot))
                {
                    Trace(bot, $"Voice ignore recentlyHit src={__instance.ProfileId}");
                    return;
                }
                if (bot.Memory.HaveEnemy)
                {
                    Trace(bot, $"Voice ignore alreadyHaveEnemy src={__instance.ProfileId}");
                    return;
                }
                bool knownEnemy = bot.EnemiesController.IsEnemy(__instance) || bot.BotsGroup.IsEnemy(__instance);
                bool hostileToBossGroup = FollowerAwareness.IsHostileToBossGroupForReaction(bot, __instance);
                if (!knownEnemy && !hostileToBossGroup)
                {
                    Trace(bot, $"Voice ignore notEnemy src={__instance.ProfileId}");
                    return;
                }

                if (ReactionRateLimiter.IsSuppressed(bot.ProfileId, __instance.ProfileId, 4, VoiceReactionCooldownSeconds))
                {
                    return;
                }

                bool heardVoice = bot.HearingSensor.IsSoundHeard(__instance.Transform.position, VoiceReactDistance, out var distance);
                bool speakerHasLosToFollower = EnemySpeakerHasLineOfSightToFollower(__instance, bot, out float losDistance);
                bool shouldReact = heardVoice || (speakerHasLosToFollower && losDistance <= VoiceReactDistance);
                bool followerHasLosToSpeaker = FollowerHasLineOfSightToSpeaker(bot, __instance);
                Trace(bot, $"Voice precheck src={__instance.ProfileId} heard={heardVoice} dist={distance:F1} los={speakerHasLosToFollower} losDist={losDistance:F1} shouldReact={shouldReact}");
                if (!shouldReact)
                {
                    Trace(bot, $"Voice ignore heard={heardVoice} dist={distance:F1} los={speakerHasLosToFollower} losDist={losDistance:F1}");
                }

                if (shouldReact)
                {
                    float effectiveDistance = heardVoice ? distance : losDistance;
                    if (effectiveDistance < 16f)
                    {
                        reported = Time.time + 3f;
                        bool canAcquire = followerHasLosToSpeaker;
                        if (canAcquire)
                        {
                            if (!reportEnemy) bot.BotsGroup.ReportAboutEnemy(__instance, EEnemyPartVisibleType.Visible, bot);
                            EnemyInfo? info = Utils.Enemy.MakeEnemy(
                                bot,
                                __instance,
                                countSharedSeenAsPersonal: false);
                            info?.SetVisible(true);
                            Utils.Enemy.RepairPersonalMemory(info, __instance.Transform.position, true);
                            reportEnemy = true;
                            Trace(bot, $"Voice react close result turn=false autoAcquire={info != null} dist={effectiveDistance:F1} followerLos={followerHasLosToSpeaker}");
                        }
                        else
                        {
                            Vector3 sourcePos = __instance.Transform.position;
                            if (__instance.MainParts != null && __instance.MainParts.TryGetValue(BodyPartType.body, out var mainBodyPart) && mainBodyPart != null)
                            {
                                sourcePos = mainBodyPart.Position;
                            }
                            bool turned = FollowerAwareness.FakeShot(bot, sourcePos, effectiveDistance, __instance);
                            if (!reportEnemy)
                            {
                                bot.BotsGroup.ReportAboutEnemy(__instance, EEnemyPartVisibleType.NotVisible, bot);
                                bot.BotTalk.TrySay(EPhraseTrigger.NoisePhrase);
                            }
                            reportEnemy = true;
                            Trace(bot, $"Voice react close result turn={turned} autoAcquire=false dist={effectiveDistance:F1} followerLos={followerHasLosToSpeaker}");
                        }
                    }
                    else if (effectiveDistance <= VoiceReactDistance)
                    {
                        reported = Time.time + 3f;
                        Vector3 sourcePos = __instance.Transform.position;
                        if (__instance.MainParts != null && __instance.MainParts.TryGetValue(BodyPartType.body, out var mainBodyPart) && mainBodyPart != null)
                        {
                            sourcePos = mainBodyPart.Position;
                        }
                        bool turned = FollowerAwareness.FakeShot(bot, sourcePos, effectiveDistance, __instance);
                        if (!reportEnemy)
                        {
                            bot.BotsGroup.ReportAboutEnemy(__instance, EEnemyPartVisibleType.NotVisible, bot);
                            bot.BotTalk.TrySay(EPhraseTrigger.NoisePhrase);
                        }
                        reportEnemy = true;
                        Trace(bot, $"Voice react far result turned={turned} dist={effectiveDistance:F1} heard={heardVoice} los={speakerHasLosToFollower}");
                    }
                }
            });
        }

        private static void Trace(BotOwner bot, string msg)
        {
            if (!EnableReactionTrace || bot == null) return;
            Modules.Logger.LogInfo($"[ReactTrace] bot={bot.Profile?.Nickname ?? bot.name} {msg}");
        }

        private static bool EnemySpeakerHasLineOfSightToFollower(Player speaker, BotOwner follower, out float distance)
        {
            distance = float.MaxValue;
            if (speaker == null || follower == null || follower.GetPlayer == null) return false;
            if (!speaker.IsAI || speaker.AIData?.BotOwner == null) return false;

            BotOwner speakerBot = speaker.AIData.BotOwner;
            Vector3 speakerPos = speaker.Transform.position;
            if (speaker.MainParts != null && speaker.MainParts.TryGetValue(BodyPartType.body, out var bodyPart) && bodyPart != null)
            {
                speakerPos = bodyPart.Position;
            }

            var followerPlayer = follower.GetPlayer;
            Vector3 followerHead = followerPlayer.MainParts?[BodyPartType.head]?.Position ?? (followerPlayer.Transform.position + Vector3.up * 1.4f);
            Vector3 followerBody = followerPlayer.MainParts?[BodyPartType.body]?.Position ?? (followerPlayer.Transform.position + Vector3.up * 1.0f);

            distance = Vector3.Distance(speakerPos, followerBody);

            LayerMask mask = speakerBot.LookSensor != null ? speakerBot.LookSensor.Mask : LayersMaskController.HighPolyWithTerrainMask;
            bool canSeeHead = Utils.Utils.CanShootToTarget(new ShootToPoint(followerHead, 1f), speakerPos, mask, false);
            if (canSeeHead) return true;

            bool canSeeBody = Utils.Utils.CanShootToTarget(new ShootToPoint(followerBody, 1f), speakerPos, mask, false);
            return canSeeBody;
        }

        private static bool FollowerHasLineOfSightToSpeaker(BotOwner follower, Player speaker)
        {
            if (speaker == null || follower == null || follower.GetPlayer == null) return false;

            var followerPlayer = follower.GetPlayer;
            Vector3 followerPos = followerPlayer.MainParts?[BodyPartType.head]?.Position ?? (followerPlayer.Transform.position + Vector3.up * 1.4f);
            Vector3 speakerHead = speaker.MainParts?[BodyPartType.head]?.Position ?? (speaker.Transform.position + Vector3.up * 1.4f);
            Vector3 speakerBody = speaker.MainParts?[BodyPartType.body]?.Position ?? (speaker.Transform.position + Vector3.up * 1.0f);

            LayerMask mask = follower.LookSensor != null ? follower.LookSensor.Mask : LayersMaskController.HighPolyWithTerrainMask;
            if (Utils.Utils.CanShootToTarget(new ShootToPoint(speakerHead, 1f), followerPos, mask, false)) return true;
            return Utils.Utils.CanShootToTarget(new ShootToPoint(speakerBody, 1f), followerPos, mask, false);
        }
    }

}
