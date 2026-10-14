using EFT;
using EFT.Interactive;
using EFT.AnimatedInteractionsSubsystem.Controller;
using EFT.AnimatedInteractionsSubsystem.Models;
using pitTeam.Patches;
using UnityEngine;

namespace pitTeam.Modules
{
    // One instance per raid selection, retained across activation and lost sight.
    internal sealed class AllegianceFriendlyGreeting
    {
        private int spokenCount;
        private float nextCheck;
        private float nextPhrase = -1f;
        private bool secondRolled;
        private bool secondSkipped;
        private bool greeted;
        private bool cancelled;

        internal void Cancel() => cancelled = true;
        internal bool HelloSent => greeted;

        internal void Update(BotOwner bot, Player human)
        {
            if (cancelled || spokenCount >= 2 || secondSkipped || !AllegiancePmcFriendship.IsEnabled || bot == null ||
                bot.IsDead || bot.BotState != EBotState.Active || BossPlayers.IsFollower(bot) ||
                bot.GetPlayer?.HealthController?.IsAlive != true || human?.HealthController?.IsAlive != true)
                return;

            float now = Time.time;
            if (now < nextCheck) return;
            nextCheck = now + 0.5f;

            if (!CanSignal(bot)) return;
            float distanceSquared = (bot.Position - human.Position).sqrMagnitude;
            if (!HasSight(bot, human, 50f)) return;

            if (nextPhrase < 0f)
            {
                // Sample range at first sight: 0.2s up close, rising to 2s at 50m.
                nextPhrase = now + 0.2f + 1.8f * (float)System.Math.Sqrt(distanceSquared) / 50f;
                nextCheck = System.Math.Min(nextCheck, nextPhrase);
            }

            if (now < nextPhrase)
            {
                // Do not round a short reaction or its deadline up to the sight-check interval.
                nextCheck = System.Math.Min(nextCheck, nextPhrase);
                return;
            }
            var speaker = bot.GetPlayer.Speaker;
            if (speaker == null || speaker.Speaking || speaker.Busy ||
                bot.BotTalk == null || FollowerForcedPhraseGate.TryGetArmedPhrase(bot, out _)) return;

            if (spokenCount == 1 && !secondRolled)
            {
                // Roll once at the second line's eligible contact, using current range.
                // Near players need fewer repeat calls; a skipped call stays spent for the raid.
                float chance = (float)System.Math.Sqrt(distanceSquared) / 50f;
                float roll = UnityEngine.Random.value;
                secondRolled = true;
                secondSkipped = chance < 1f && roll >= chance;
                Logger.LogInfo($"[Allegiance] candidate={bot.ProfileId} second-greeting chance={chance:F2} roll={roll:F3} skipped={secondSkipped}");
                if (secondSkipped) return;
            }

            // The existing pre-recruitment speech scope bypasses only this exact
            // phrase in SAIN. Ordinary bot speech and shared presets remain owned by SAIN.
            EPhraseTrigger phrase = bot.Side == EPlayerSide.Savage ? EPhraseTrigger.MumblePhrase : EPhraseTrigger.HoldFire;
            FollowerForcedPhraseGate.ArmRecruitmentResponse(bot, phrase, 0.9f);
            try
            {
                bot.BotTalk.SetSilence(-1f);
                bot.BotTalk.DropNextSayPeriod();
                bot.BotTalk.Say(phrase, true);
            }
            finally
            {
                // Say is immediate; no queued greeting, retained speech scope or callback
                // should interfere with the next native warning, recruitment or raid.
                if (FollowerForcedPhraseGate.IsRecruitmentResponse(bot, phrase))
                    FollowerForcedPhraseGate.Clear(bot);
            }
            if (!speaker.Speaking) return;

            spokenCount++;
            nextPhrase = now + UnityEngine.Random.Range(1f, 3f);
            nextCheck = System.Math.Min(nextCheck, nextPhrase);
            Logger.LogInfo($"[Allegiance] candidate={bot.ProfileId} greeting={phrase} count={spokenCount}/2");
        }

        internal static bool HasSight(BotOwner bot, Player human, float range)
        {
            var sight = bot?.LookSensor;
            if (sight == null || human == null || bot.GetPlayer == null) return false;
            float distanceSquared = (bot.Position - human.Position).sqrMagnitude;
            return distanceSquared <= range * range && sight.VisibleDist > 0f &&
                distanceSquared <= sight.VisibleDist * sight.VisibleDist &&
                sight.IsPointInVisibleSector(human.Position) && sight.CheckLookSimple(bot.GetPlayer, human);
        }

        internal static bool CanSignal(BotOwner bot, bool allowHello = false, bool allowRefusal = false)
        {
            if (bot?.GetPlayer == null) return false;
            var danger = bot.BewareGrenade?.GrenadeDangerPoint;
            // Passive state only: do not invoke medical or danger decision providers.
            return bot.Memory?.HaveEnemy != true && bot.Memory?.IsUnderFire != true &&
                !SainGoalEnemyBridge.HasEnemy(bot) && bot.ShootData?.Shooting != true &&
                !(danger != null && danger.Grenade != null && Time.time <= danger.EndTime) &&
                !TeammateBackpackInspection.HasActiveOrPendingHealWork(bot) &&
                bot.WeaponManager?.Grenades?.ThrowindNow != true &&
                bot.WeaponManager?.Selector?.IsChanging != true &&
                bot.WeaponManager?._currentWeaponInfo?.Reload?.Reloading != true &&
                bot.GetPlayer.MovementContext?.IsInMountedState != true &&
                bot.GetPlayer.MovementContext?.IsStationaryWeaponInHands != true &&
                // The timed refusal owns the response hands work; its deadline does not inspect animation state.
                (allowRefusal || bot.GetPlayer.HandsController?.IsInInteractionStrictCheck() == false ||
                    (allowHello && IsHelloPlaying(bot.GetPlayer)));
        }

        private static bool IsHelloPlaying(Player player) =>
            player.MovementContext?.PlayerAnimator?.AnimatedInteractions is EFT.AnimatedInteractionsSubsystem.AnimatedInteractions animations &&
            animations._controller is AnimatedInteractionsController controller &&
            controller._model is AnimatedInteractionsModel model && model._currentInteraction == EInteraction.FriendlyGesture;

        internal void TryHello(BotOwner bot, Player human, float distanceSquared)
        {
            if (cancelled || greeted || distanceSquared > FriendlyEncounterInteraction.Range * FriendlyEncounterInteraction.Range || bot.Gesture == null ||
                bot.Settings?.FileSettings?.Patrol?.CAN_GESTUS != true ||
                FollowerForcedPhraseGate.TryGetArmedPhrase(bot, out _)) return;

            Ray ray = human.InteractionRay;
            var parts = bot.GetPlayer.MainParts;
            Vector3 target = parts != null && parts.TryGetValue(BodyPartType.body, out var body)
                ? body.Position : bot.Position;
            Vector3 toTarget = target - ray.origin;
            // Match the existing Cooperation look cone; do not steer the bot toward us.
            if (toTarget.sqrMagnitude < 0.01f || ray.direction.sqrMagnitude < 0.01f ||
                Vector3.Angle(ray.direction, toTarget) > 18f) return;

            var player = bot.GetPlayer;
            var movement = player.MovementContext;
            var interactions = movement?.PlayerAnimator?.AnimatedInteractions;
            if (player.UsedSimplifiedSkeleton || movement == null || movement.IsSprintEnabled ||
                movement.IsInMountedState || movement.IsStationaryWeaponInHands ||
                player.HandsController?.FirearmsAnimator?.IsIdling() != true ||
                interactions == null || interactions.IsScheduleDenied || !interactions.CanInteract ||
                interactions.IsInteractionPlaying) return;

            // Native scheduling/cooldowns may reject the request. Only accepted animation
            // work spends the single Hello. Its normal finish/interruption stays EFT-owned.
            bot.Gesture.TryGestus(EInteraction.FriendlyGesture, false);
            greeted = interactions.IsInteractionPlaying;
            if (greeted) Logger.LogInfo($"[Allegiance] candidate={bot.ProfileId} greeting=Hello");
        }
    }
}
