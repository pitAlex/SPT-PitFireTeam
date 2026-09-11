using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;
using EFT.SynchronizableObjects;
using EFT.Tripwire;
using HarmonyLib;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.Patches
{
    internal static class FollowerTripwireAwarenessPatch
    {
        private sealed class CollisionContext
        {
            internal TripwireSynchronizableObject Wire = null!;
            internal BotOwner? TriggeringBot;
        }

        // These calls are synchronous. Restore the outer context even when EFT throws,
        // and match the wire so nested activations cannot inherit another wire's tripper.
        [ThreadStatic] private static CollisionContext? _collision;
        [ThreadStatic] private static TripwireSynchronizableObject? _activating;
        private static readonly ConditionalWeakTable<Grenade, HashSet<string>> KnownFollowers = new();
        private static readonly AccessTools.FieldRef<TripwireSynchronizableObject, Grenade> LiveGrenade =
            AccessTools.FieldRefAccess<TripwireSynchronizableObject, Grenade>("_grenadeInWorld");
        private static readonly AccessTools.FieldRef<TripwireSynchronizableObject, ITripwireSoundController> SoundController =
            AccessTools.FieldRefAccess<TripwireSynchronizableObject, ITripwireSoundController>("_soundController");

        internal static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(BaseTripwire), nameof(BaseTripwire.CollisionEnter)),
                prefix: new HarmonyMethod(typeof(FollowerTripwireAwarenessPatch), nameof(BeginCollision)),
                finalizer: new HarmonyMethod(typeof(FollowerTripwireAwarenessPatch), nameof(EndCollision)));
            harmony.Patch(AccessTools.Method(typeof(TripwireSynchronizableObject), nameof(TripwireSynchronizableObject.ActivateGrenade)),
                prefix: new HarmonyMethod(typeof(FollowerTripwireAwarenessPatch), nameof(BeginActivation)),
                finalizer: new HarmonyMethod(typeof(FollowerTripwireAwarenessPatch), nameof(EndActivation)));
            harmony.Patch(AccessTools.Method(typeof(TripwireSoundController), nameof(TripwireSoundController.PlayPinSound)),
                postfix: new HarmonyMethod(typeof(FollowerTripwireAwarenessPatch), nameof(OnPinSound)));
            harmony.Patch(AccessTools.Method(typeof(BotBewareGrenade), nameof(BotBewareGrenade.AddGrenadeDanger)),
                prefix: new HarmonyMethod(typeof(FollowerTripwireAwarenessPatch), nameof(SkipKnownNotification)));
        }

        private static void BeginCollision(BaseTripwire __instance, Collider colliderComponent, out CollisionContext? __state)
        {
            __state = _collision;
            _collision = null;
            try
            {
                if (colliderComponent == null || colliderComponent.isTrigger || Singleton<GameWorld>.Instance == null) return;
                Player player = Singleton<GameWorld>.Instance.GetPlayerByCollider(colliderComponent);
                _collision = new CollisionContext
                {
                    Wire = __instance._tripwireSyncObject,
                    TriggeringBot = player != null && player.IsAI ? player.AIData?.BotOwner : null
                };
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Tripwire] Could not identify the triggering follower: {ex}");
            }
        }

        private static void EndCollision(CollisionContext? __state) => _collision = __state;
        private static void BeginActivation(TripwireSynchronizableObject __instance, out TripwireSynchronizableObject? __state)
        {
            __state = _activating;
            _activating = __instance;
        }
        private static void EndActivation(TripwireSynchronizableObject? __state) => _activating = __state;

        private static void OnPinSound(TripwireSoundController __instance, Vector3 grenadePos)
        {
            try
            {
                TripwireSynchronizableObject? wire = _activating;
                if (wire == null || !ReferenceEquals(SoundController(wire), __instance) || BossPlayers.Instance == null) return;
                Grenade grenade = LiveGrenade(wire);
                if (grenade == null) return;

                // EFT starts the live grenade's fuse before playing this sound, then
                // dispatches ThrowGrenade afterwards. Register before that normal event.
                var pin = __instance._soundStorage?.GetGrenadePinSound();
                float soundRange = pin != null && pin.GetVolume() > 0f ? pin.GetMaxDistance() : 0f;
                BotOwner? tripper = ReferenceEquals(_collision?.Wire, wire) ? _collision?.TriggeringBot : null;
                var followers = BossPlayers.GetFollowers();
                for (int i = 0; i < followers.Count; i++)
                {
                    BotOwner? bot = followers[i]?.GetBot();
                    try
                    {
                        if (!CanObserve(bot) || IsKnown(bot!, grenade)) continue;
                        bool tripped = ReferenceEquals(bot, tripper);
                        if (!tripped && !HeardPin(bot!, grenadePos, soundRange)) continue;
                        RegisterKnownGrenade(bot!, grenade, tripped);
                    }
                    catch (Exception ex)
                    {
                        Modules.Logger.LogError($"[Tripwire] Follower awareness failed for {bot?.ProfileId}: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Tripwire] Pin-sound awareness failed: {ex}");
            }
        }

        private static bool CanObserve(BotOwner? bot) =>
            bot != null && !bot.IsDead && bot.BotState == EBotState.Active &&
            bot.GetPlayer != null && bot.BewareGrenade != null && bot.BotsGroup != null &&
            bot.Settings?.FileSettings != null && !bot.Settings.FileSettings.Mind.GRENADE_DAMAGE_IGNORE &&
            BossPlayers.IsFollower(bot);

        private static bool HeardPin(BotOwner bot, Vector3 position, float range)
        {
            if (range <= 0f || float.IsNaN(range) || float.IsInfinity(range) || bot.HearingSensor == null ||
                !bot.HearingSensor.IsSoundHeard(position, range, out _)) return false;
            Vector3 head = bot.Position + Vector3.up * 1.4f;
            var parts = bot.GetPlayer.MainParts;
            if (parts != null && parts.TryGetValue(BodyPartType.head, out var part) && part != null) head = part.Position;
            // A conservative hearing gate for the short pin click, without requiring
            // visual attention or inventing an AI sound/enemy at the planter's position.
            return !Physics.Linecast(position + Vector3.up * 0.05f, head,
                LayersMaskController.HighPolyWithTerrainMask, QueryTriggerInteraction.Ignore);
        }

        private static void RegisterKnownGrenade(BotOwner bot, Grenade grenade, bool tripped)
        {
            BotBewareGrenade beware = bot.BewareGrenade;
            if (beware.IgnoreGrenade(grenade)) return;
            Vector3 position = grenade.transform.position;
            var danger = new GrenadeDangerPoint(position, grenade, bot, 0f);
            // This is a stationary planted grenade with a freshly started fuse. Keep
            // awareness for its entire fuse; native destruction still ends it at once.
            float fuse = grenade.WeaponSource?.GetExplDelay ?? 0f;
            if (!float.IsNaN(fuse) && !float.IsInfinity(fuse))
                danger.EndTime = Math.Max(danger.EndTime, Time.time + Math.Max(0f, fuse) + 1f);
            beware.GrenadeDangerPoint?.Destroy();
            beware.SetGrenadeDangerPoint(danger);
            if (!KnownFollowers.TryGetValue(grenade, out var known))
            {
                known = new HashSet<string>(StringComparer.Ordinal);
                KnownFollowers.Add(grenade, known);
                grenade.DestroyEvent += OnGrenadeDestroyed;
            }
            known.Add(bot.ProfileId);

            // Preserve native cover invalidation and the awareness event, while skipping
            // SubAddGrenade's random recognition roll and generic grenade voice queue.
            try
            {
                if (!(grenade is SmokeGrenade) && !(grenade is StunGrenade) && bot.BotsGroup.CoverPointMaster != null)
                    beware.SpottedAllPointNearPos(position);
                beware.OnBewareGrenade?.Invoke(grenade);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Tripwire] Native awareness notification failed for {bot.ProfileId}: {ex}");
            }
            if (bot.BotTalk != null && !bot.BotTalk.IsSilenced)
            {
                FollowerForcedPhraseGate.Arm(bot, EPhraseTrigger.Spreadout, 1.5f);
                bot.BotTalk.DropNextSayPeriod();
                bot.BotTalk.Say(EPhraseTrigger.Spreadout, true);
            }
            Modules.Logger.LogInfo($"[Tripwire] Follower {bot.ProfileId} detected grenade by {(tripped ? "trigger" : "hearing")} at {position}.");
        }

        private static void OnGrenadeDestroyed(Throwable throwable)
        {
            throwable.DestroyEvent -= OnGrenadeDestroyed;
            if (throwable is Grenade grenade && KnownFollowers.TryGetValue(grenade, out var known))
            {
                KnownFollowers.Remove(grenade);
                foreach (string profileId in known)
                    Modules.Logger.LogInfo($"[Tripwire] Grenade ended for follower {profileId}; avoidance may release.");
            }
        }

        internal static bool IsKnown(BotOwner bot, Grenade grenade) =>
            grenade != null && bot != null && BossPlayers.IsFollower(bot) &&
            KnownFollowers.TryGetValue(grenade, out var known) && known.Contains(bot.ProfileId);

        private static bool SkipKnownNotification(BotBewareGrenade __instance, Grenade grenade) =>
            !IsKnown(__instance._owner, grenade);
    }
}
