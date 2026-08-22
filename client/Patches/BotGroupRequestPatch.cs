using EFT;
using EFT.Interactive;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;


namespace pitTeam.Patches
{
    internal class FollowRequestPatch : ModulePatch
    {
        private const int FirstPickupConversionDelayMs = 75;
        private const float RecruitForcedPhraseSeconds = 2.5f;

        private static double MathClamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotGroupRequestController), "TryAskFollowMeRequest");

        }
        [PatchPrefix]
        private static bool PatchPrefix(BotGroupRequestController __instance, ref bool __result, IPlayer player, BotOwner posibleExecuter)
        {

            pitAIBossPlayer playerBoss = BossPlayers.Instance.GetBossPlayer(player.ProfileId);

            if (playerBoss != null && posibleExecuter != null)
            {
                bool isAFollower = BossPlayers.IsFollower(posibleExecuter);

                if (isAFollower)
                {
                    // if BOT is already a follower, allow "follow me" request to take place if it is the boss who is requesting it
                    if (posibleExecuter.BotFollower.HaveBoss)
                    {
                        if (posibleExecuter.BotFollower.BossToFollow.IsMe(playerBoss.Player()))
                        {
                            return true;
                            // - this is a follower of someone else
                        }
                        else
                        {
                            posibleExecuter.BotTalk.TrySay(EPhraseTrigger.Negative);
                            posibleExecuter.Gesture.TryGestus(EInteraction.NoGesture, true);
                            __result = false;
                            return false;
                        }
                    }
                }
                // allow player to request a BOT to follow him
                if (player.Side == posibleExecuter.Side)
                {
                    // Do not allow recruiting bots that are already in combat.
                    // Visibility does not matter; any active enemy memory means deny.
                    if (posibleExecuter.Memory?.HaveEnemy == true)
                    {
                        posibleExecuter.BotTalk.TrySay(EPhraseTrigger.DontKnow);
                        posibleExecuter.Gesture.TryGestus(EInteraction.NoGesture, true);
                        __result = false;
                        return false;
                    }

                    if (!pitFireTeam.pickupEnabled.Value)
                    {
                        posibleExecuter.BotTalk.TrySay(EPhraseTrigger.Negative);
                        posibleExecuter.Gesture.TryGestus(EInteraction.NoGesture, true);
                        __result = false;
                        return false;
                    }

                    bool canPickup = false;
                    List<Components.BotFollowerPlayer> followers = BossPlayers.GetFollowersByBoss(player.ProfileId);
                    List<Components.BotFollowerPlayer> activeFollowers = followers.FindAll(f =>
                    {
                        if (f == null) return false;
                        BotOwner bot = f.GetBot();
                        return bot != null &&
                               !bot.IsDead &&
                               bot.BotState == EBotState.Active &&
                               bot.GetPlayer != null &&
                               bot.GetPlayer.HealthController != null &&
                               bot.GetPlayer.HealthController.IsAlive;
                    });
                    int configuredPickups = Math.Max(0, pitFireTeam.maximumPickup.Value);
                    int hardPickupLimit = Math.Min(10, configuredPickups);
                    int currentPickups = activeFollowers.FindAll(f => !f.IsSquadMate).Count;
                    if (BossPlayers.HasDeniedRecruitment(posibleExecuter.ProfileId))
                    {
                        // A tiered level-based refusal is final for this bot for the current raid.
                        canPickup = false;
                    }
                    // Tiered pickup uses the old player-vs-bot acceptance rules.
                    else if (pitFireTeam.tieredPickup.Value)
                    {
                        // - SCAV : based on fence level
                        if (player.Side == EPlayerSide.Savage)
                        {
                            double standing = player.Profile.FenceInfo.Standing;

                            if (standing >= 1.0)
                            {
                                double ratio = (standing - 1.0) / (6.0 - 1.0); // 0 to 1
                                int maxAllowedByStanding = (int)Math.Round(1 + ratio * (10 - 1));

                                int effectiveLimit = Math.Min(maxAllowedByStanding, hardPickupLimit);

                                if (currentPickups < effectiveLimit)
                                {
                                    canPickup = true;
                                }
                            }
                        }
                        // - PMC:
                        else
                        {
                            int playerLevel = player.Profile.Info.Level;
                            int botLevel = posibleExecuter.Profile.Info.Level;
                            int levelDiff = playerLevel - botLevel;
                            bool rememberDenial = false;
                            // - - limit reached → deny
                            if (currentPickups >= hardPickupLimit)
                            {
                                canPickup = false;
                            }
                            else if (levelDiff >= 10)
                            {
                                // - - player much stronger → always allow
                                canPickup = true;
                            }
                            else if (levelDiff <= -10)
                            {
                                // - - bot much stronger → always deny
                                canPickup = false;
                                rememberDenial = true;
                            }
                            else if (playerLevel == botLevel)
                            {
                                // -- equal levels → 50/50 chance
                                canPickup = new System.Random().NextDouble() < 0.5;
                                rememberDenial = !canPickup;
                            }
                            else
                            {
                                // -- different levels → 0% to 100% chance based on level difference
                                double chance = MathClamp((levelDiff + 10) / 20.0, 0.0, 1.0);
                                canPickup = new System.Random().NextDouble() < chance;
                                rememberDenial = !canPickup;
                            }

                            if (rememberDenial)
                            {
                                BossPlayers.RememberRecruitmentDenial(posibleExecuter.ProfileId);
                            }
                        }
                    }
                    else
                    {
                        canPickup = currentPickups < hardPickupLimit;
                    }

                    // add BOT as follower to the player BOSS if limit was not reached
                    if (canPickup)
                    {
                        // Defer conversion to BotOwner manual-update cycle to avoid recruit-time activation races.
                        BotOwnerManualUpdatePatch.BotOwnerUpdate[posibleExecuter.ProfileId] = me =>
                        {
                            BotOwnerManualUpdatePatch.BotOwnerUpdate.Remove(me.ProfileId);

                            try
                            {
                                if (me == null || me.IsDead || me.BotState != EBotState.Active || me.GetPlayer == null || !me.GetPlayer.HealthController.IsAlive)
                                {
                                    return;
                                }

                                if (BossPlayers.HasDeniedRecruitment(me.ProfileId))
                                {
                                    me.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                                    me.Gesture.TryGestus(EInteraction.NoGesture, true);
                                    return;
                                }

                                // Re-check pickup cap at execution time because this runs deferred and
                                // multiple recruit requests can be queued in the same window.
                                List<Components.BotFollowerPlayer> deferredFollowers = BossPlayers.GetFollowersByBoss(player.ProfileId);
                                List<Components.BotFollowerPlayer> deferredActiveFollowers = deferredFollowers.FindAll(f =>
                                {
                                    if (f == null) return false;
                                    BotOwner bot = f.GetBot();
                                    return bot != null &&
                                           !bot.IsDead &&
                                           bot.BotState == EBotState.Active &&
                                           bot.GetPlayer != null &&
                                           bot.GetPlayer.HealthController != null &&
                                           bot.GetPlayer.HealthController.IsAlive;
                                });
                                int deferredConfiguredPickups = Math.Max(0, pitFireTeam.maximumPickup.Value);
                                int deferredHardPickupLimit = Math.Min(10, deferredConfiguredPickups);
                                int deferredCurrentPickups = deferredActiveFollowers.FindAll(f => !f.IsSquadMate).Count;
                                if (deferredCurrentPickups >= deferredHardPickupLimit)
                                {
                                    me.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                                    me.Gesture.TryGestus(EInteraction.NoGesture, true);
                                    return;
                                }

                                if (me.Memory?.HaveEnemy == true)
                                {
                                    me.BotTalk.TrySay(EPhraseTrigger.DontKnow, false);
                                    me.Gesture.TryGestus(EInteraction.NoGesture, true);
                                    return;
                                }

                                me.BotTalk.SetSilence(2f);
                                FollowerForcedPhraseGate.Arm(me, EPhraseTrigger.Roger, RecruitForcedPhraseSeconds);

                                int conversionDelayMs = playerBoss.bossGroup == null ? FirstPickupConversionDelayMs : 0;
                                Action completeRecruit = () => CompleteRecruitConversion(me, playerBoss);
                                if (conversionDelayMs > 0)
                                {
                                    Utils.Utils.SetTimeout(completeRecruit, conversionDelayMs);
                                }
                                else
                                {
                                    completeRecruit();
                                }
                            }
                            catch (Exception ex)
                            {
                                Modules.Logger.LogError("Failed deferred recruit conversion");
                                Modules.Logger.LogError(ex);
                            }
                        };
                    }
                    else
                    {
                        // bot signals "NO"
                        posibleExecuter.BotTalk.TrySay(EPhraseTrigger.Negative);
                        posibleExecuter.Gesture.TryGestus(EInteraction.NoGesture, true);
                    }

                    __result = false;
                    return false;
                }
                else
                {
                    // bot signals "NO"
                    posibleExecuter.BotTalk.TrySay(EPhraseTrigger.Toxic);
                    posibleExecuter.Gesture.TryGestus(EInteraction.GetOffGesture, true);
                    __result = false;
                    return false;
                }

            }
            // allow default to take place
            return true;
        }

        private static void CompleteRecruitConversion(BotOwner bot, pitAIBossPlayer playerBoss)
        {
            if (bot == null || playerBoss == null || bot.IsDead || bot.BotState != EBotState.Active || bot.GetPlayer == null || !bot.GetPlayer.HealthController.IsAlive)
            {
                return;
            }

            if (BossPlayers.HasDeniedRecruitment(bot.ProfileId))
            {
                FollowerForcedPhraseGate.Arm(bot, EPhraseTrigger.Negative, 1.5f);
                bot.BotTalk.SetSilence(0f);
                bot.BotTalk.DropNextSayPeriod();
                bot.BotTalk.Say(EPhraseTrigger.Negative, true);
                bot.Gesture.TryGestus(EInteraction.NoGesture, true);
                return;
            }

            if (bot.Memory?.HaveEnemy == true)
            {
                FollowerForcedPhraseGate.Arm(bot, EPhraseTrigger.DontKnow, 1.5f);
                bot.BotTalk.SetSilence(0f);
                bot.BotTalk.DropNextSayPeriod();
                bot.BotTalk.Say(EPhraseTrigger.DontKnow, true);
                bot.Gesture.TryGestus(EInteraction.NoGesture, true);
                return;
            }

            if (BossPlayers.AddFollower(bot, playerBoss) != null)
            {
                TrySayControlledFollowerPhrase(
                    bot,
                    EPhraseTrigger.Roger,
                    EInteraction.OkGesture,
                    UnityEngine.Random.Range(300, 700));
                return;
            }

            FollowerForcedPhraseGate.Arm(bot, EPhraseTrigger.DontKnow, 1.5f);
            bot.BotTalk.SetSilence(0f);
            bot.BotTalk.DropNextSayPeriod();
            bot.BotTalk.Say(EPhraseTrigger.DontKnow, true);
        }

        private static void TrySayControlledFollowerPhrase(
            BotOwner bot,
            EPhraseTrigger phrase,
            EInteraction gesture,
            int delayMs)
        {
            if (bot == null || bot.IsDead || bot.BotState != EBotState.Active)
            {
                return;
            }

            Utils.Utils.SetTimeout(() =>
            {
                if (bot == null || bot.IsDead || bot.BotState != EBotState.Active)
                {
                    return;
                }
                bot.BotTalk.SetSilence(0f);
                bot.BotTalk.DropNextSayPeriod();
                bool saidPhrase = false;
                if (pitFireTeam.ShouldDisableSainForFollowers)
                {
                    saidPhrase = TryPlayDirectFollowerPhrase(bot, phrase);
                }

                if (!saidPhrase)
                {
                    bot.BotTalk.Say(phrase, true);
                }

                bot.Gesture.TryGestus(gesture, true);
            }, delayMs);
        }

        private static bool TryPlayDirectFollowerPhrase(BotOwner bot, EPhraseTrigger phrase)
        {
            if (bot?.GetPlayer?.Speaker == null)
            {
                return false;
            }

            var speaker = bot.GetPlayer.Speaker;
            if (speaker.Speaking || speaker.Busy)
            {
                return false;
            }

            ETagStatus mask = (bot.BotsGroup != null && bot.BotsGroup.MembersCount > 1)
                ? ETagStatus.Coop
                : ETagStatus.Solo;

            mask |= ETagStatus.Unaware;
            return speaker.Play(phrase, mask, true, null) != null;
        }
    }

    internal class HoldRequestPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotGroupRequestController), "TryActivateWait");

        }
        [PatchPrefix]
        private static bool PatchPrefix(BotGroupRequestController __instance, IPlayer player, BotOwner posibleExecuter)
        {

            pitAIBossPlayer playerBoss = BossPlayers.Instance.GetBossPlayer(player.ProfileId);


            if (playerBoss != null && posibleExecuter != null)
            {
                // If player is explicitly targeting a bot, hold should apply only to that bot.
                // Otherwise keep the vanilla/broadcast behavior (all eligible followers can receive it).
                if (player is Player requesterPlayer && TryGetLookedAtBot(requesterPlayer, 15f, out BotOwner targetedBot))
                {
                    if (targetedBot != posibleExecuter)
                    {
                        return false;
                    }
                }

                // boss can only send hold requests to it's followers
                if (BossPlayers.IsFollower(posibleExecuter, playerBoss))
                {

                    return true;
                }

                // bot signals "NO"
                posibleExecuter.BotTalk.TrySay(EPhraseTrigger.Negative);
                posibleExecuter.Gesture.TryGestus(EInteraction.NoGesture, true);

                return false;
            }
            // allow default to take place
            return true;
        }

        private static bool TryGetLookedAtBot(Player requesterPlayer, float distance, out BotOwner bot)
        {
            bot = null;
            if (requesterPlayer == null) return false;

            const float sphereRadius = 0.4f;
            RaycastHit[] hits = new RaycastHit[10];
            Ray ray = requesterPlayer.InteractionRay;
            int hitCount = Physics.SphereCastNonAlloc(ray, sphereRadius, hits, distance, LayersMaskController.PlayerMask);
            if (hitCount <= 0) return false;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider?.gameObject == null) continue;
                BotOwner hitBot = hit.collider.gameObject.GetComponentInParent<BotOwner>();
                if (hitBot == null) continue;

                bot = hitBot;
                return true;
            }

            return false;
        }

    }

    internal class OpenDoorRequestPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(BotGroupRequestController),
                "TryActivateOpenDoorRequest",
                new[] { typeof(IPlayer), typeof(Door), typeof(Action) });
        }

        [PatchPrefix]
        private static bool PatchPrefix(ref bool __result, IPlayer requester, Door door, Action completeCallback)
        {
            // Keep player-issued command behavior; block only autonomous AI follower door requests.
            BotOwner requesterBot = requester?.AIData?.BotOwner;
            if (requesterBot != null && BossPlayers.IsFollower(requesterBot))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

}
