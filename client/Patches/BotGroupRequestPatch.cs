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

        internal static bool IsRecruitmentSideAllowed(IPlayer player, BotOwner bot) =>
            player != null && bot != null &&
            (player.Side == bot.Side ||
             (GameplayModeRuntime.IsAllegiance &&
              (player.Side == EPlayerSide.Usec || player.Side == EPlayerSide.Bear) &&
              AllegiancePmcFriendship.CanRecruit(bot, player)));

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
                            TrySayRecruitmentResponse(posibleExecuter, EPhraseTrigger.Negative);
                            posibleExecuter.Gesture.TryGestus(EInteraction.NoGesture, true);
                            __result = false;
                            return false;
                        }
                    }
                }
                // allow player to request a BOT to follow him
                if (IsRecruitmentSideAllowed(player, posibleExecuter))
                {
                    if (!AllegiancePmcFriendship.CanRecruit(posibleExecuter, player))
                    {
                        TrySayRecruitmentResponse(posibleExecuter, EPhraseTrigger.Negative);
                        posibleExecuter.Gesture.TryGestus(EInteraction.NoGesture, true);
                        __result = false;
                        return false;
                    }

                    // Do not allow recruiting bots that are already in combat.
                    // SAIN can own an active enemy without an EFT goal. Combat is a temporary
                    // refusal and must precede the raid-scoped level decision.
                    if (HasRecruitmentCombatEnemy(posibleExecuter))
                    {
                        TrySayRecruitmentResponse(posibleExecuter, EPhraseTrigger.DontKnow);
                        posibleExecuter.Gesture.TryGestus(EInteraction.NoGesture, true);
                        __result = false;
                        return false;
                    }

                    if (!GameplayModeRuntime.GetEffectiveValue(pitFireTeam.pickupEnabled))
                    {
                        TrySayRecruitmentResponse(posibleExecuter, EPhraseTrigger.Negative);
                        posibleExecuter.Gesture.TryGestus(EInteraction.NoGesture, true);
                        __result = false;
                        return false;
                    }

                    bool canPickup = false;
                    int hardPickupLimit = GetHardPickupLimit();
                    int currentPickups = GetActivePickupCount(player.ProfileId);
                    if (BossPlayers.HasDeniedRecruitment(posibleExecuter.ProfileId))
                    {
                        // A tiered level-based refusal is final for this bot for the current raid.
                        canPickup = false;
                    }
                    // Tiered pickup uses the old player-vs-bot acceptance rules.
                    else if (GameplayModeRuntime.GetEffectiveValue(pitFireTeam.tieredPickup))
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

                                if (HasRecruitmentCombatEnemy(me))
                                {
                                    TrySayRecruitmentResponse(me, EPhraseTrigger.DontKnow, false);
                                    me.Gesture.TryGestus(EInteraction.NoGesture, true);
                                    return;
                                }

                                if (!AllegiancePmcFriendship.CanRecruit(me, player) || BossPlayers.HasDeniedRecruitment(me.ProfileId))
                                {
                                    TrySayRecruitmentResponse(me, EPhraseTrigger.Negative, false);
                                    me.Gesture.TryGestus(EInteraction.NoGesture, true);
                                    return;
                                }

                                // Re-check pickup cap at execution time because this runs deferred and
                                // multiple recruit requests can be queued in the same window.
                                if (GetActivePickupCount(player.ProfileId) >= GetHardPickupLimit())
                                {
                                    TrySayRecruitmentResponse(me, EPhraseTrigger.Negative, false);
                                    me.Gesture.TryGestus(EInteraction.NoGesture, true);
                                    return;
                                }

                                me.BotTalk.SetSilence(2f);
                                FollowerForcedPhraseGate.ArmRecruitmentResponse(me, EPhraseTrigger.Roger, RecruitForcedPhraseSeconds);

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
                        TrySayRecruitmentResponse(posibleExecuter, EPhraseTrigger.Negative);
                        posibleExecuter.Gesture.TryGestus(EInteraction.NoGesture, true);
                    }

                    __result = false;
                    return false;
                }
                else
                {
                    // bot signals "NO"
                    TrySayRecruitmentResponse(posibleExecuter, EPhraseTrigger.Toxic);
                    posibleExecuter.Gesture.TryGestus(EInteraction.GetOffGesture, true);
                    __result = false;
                    return false;
                }

            }
            // allow default to take place
            return true;
        }

        private static bool HasRecruitmentCombatEnemy(BotOwner bot)
        {
            return bot.Memory?.HaveEnemy == true || SainGoalEnemyBridge.HasEnemy(bot);
        }

        private static int GetHardPickupLimit() => Math.Min(10, Math.Max(0, GameplayModeRuntime.GetEffectiveValue(pitFireTeam.maximumPickup)));

        private static int GetActivePickupCount(string bossProfileId) =>
            BossPlayers.GetFollowersByBoss(bossProfileId).FindAll(f =>
            {
                BotOwner member = f?.GetBot();
                return f != null && !f.IsSquadMate && member != null && !member.IsDead &&
                       member.BotState == EBotState.Active && member.GetPlayer?.HealthController?.IsAlive == true;
            }).Count;

        private static void CompleteRecruitConversion(BotOwner bot, pitAIBossPlayer playerBoss)
        {
            if (bot == null || playerBoss == null || bot.IsDead || bot.BotState != EBotState.Active || bot.GetPlayer == null || !bot.GetPlayer.HealthController.IsAlive)
            {
                return;
            }

            // Repeated requests can queue this same candidate during the first-group delay.
            if (BossPlayers.IsFollower(bot)) return;

            if (HasRecruitmentCombatEnemy(bot))
            {
                FollowerForcedPhraseGate.ArmRecruitmentResponse(bot, EPhraseTrigger.DontKnow, 1.5f);
                PrepareImmediateRecruitmentSpeech(bot);
                bot.BotTalk.Say(EPhraseTrigger.DontKnow, true);
                bot.Gesture.TryGestus(EInteraction.NoGesture, true);
                return;
            }

            if (!AllegiancePmcFriendship.CanRecruit(bot, playerBoss.Player()) || BossPlayers.HasDeniedRecruitment(bot.ProfileId) ||
                GetActivePickupCount(playerBoss.Player().ProfileId) >= GetHardPickupLimit())
            {
                FollowerForcedPhraseGate.ArmRecruitmentResponse(bot, EPhraseTrigger.Negative, 1.5f);
                PrepareImmediateRecruitmentSpeech(bot);
                bot.BotTalk.Say(EPhraseTrigger.Negative, true);
                bot.Gesture.TryGestus(EInteraction.NoGesture, true);
                return;
            }

            if (BossPlayers.AddFollower(bot, playerBoss) != null)
            {
                PmcKarmaRuntime.NoteRecruitment(bot, playerBoss.Player(), false);
                TrySayControlledFollowerPhrase(
                    bot,
                    EPhraseTrigger.Roger,
                    EInteraction.OkGesture,
                    UnityEngine.Random.Range(300, 700));
                return;
            }

            FollowerForcedPhraseGate.ArmRecruitmentResponse(bot, EPhraseTrigger.DontKnow, 1.5f);
            PrepareImmediateRecruitmentSpeech(bot);
            bot.BotTalk.Say(EPhraseTrigger.DontKnow, true);
        }

        private static void PrepareImmediateRecruitmentSpeech(BotOwner bot)
        {
            // EFT expires silence only when silenceEnds < Time.time. Zero duration
            // still blocks Say in this frame, so expire it before sending the reply.
            bot.BotTalk.SetSilence(-1f);
            bot.BotTalk.DropNextSayPeriod();
        }

        private static void TrySayRecruitmentResponse(BotOwner bot, EPhraseTrigger phrase, bool? withGroupDelay = null)
        {
            if (!pitFireTeam.IsSAINInstalled)
            {
                if (withGroupDelay.HasValue) bot.BotTalk.TrySay(phrase, withGroupDelay.Value);
                else bot.BotTalk.TrySay(phrase);
                return;
            }

            // Native SAIN suppresses both queued EFT speech and BotTalk.Say before
            // this candidate is a follower. Keep only this command reply Core-owned.
            FollowerForcedPhraseGate.ArmRecruitmentResponse(bot, phrase, 1.5f);
            PrepareImmediateRecruitmentSpeech(bot);
            bot.BotTalk.Say(phrase, true);
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
                PrepareImmediateRecruitmentSpeech(bot);
                bool saidPhrase = false;
                if (pitFireTeam.ShouldDisableSainForFollower(bot))
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
