using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using EFT.HealthSystem;
using UnityEngine;

namespace pitTeam.Utils
{
    // Native first aid and manual top-offs share this failed-HP-treatment budget.
    // Damage and spent supplies are not evidence that a treatment worked.
    internal static class FollowerFirstAidRetry
    {
        private sealed class Attempt
        {
            internal float Health, RetryAt;
            internal int Failures;
        }

        private sealed class State
        {
            internal readonly Dictionary<EBodyPart, Attempt> Parts = new Dictionary<EBodyPart, Attempt>();
            internal EBodyPart Target;
            internal bool Pending;
        }

        private static readonly ConditionalWeakTable<Player, State> States = new ConditionalWeakTable<Player, State>();

        internal static void Clear(Player player)
        {
            if (player != null) States.Remove(player);
        }

        internal static void Begin(BotFirstAid firstAid)
        {
            BotOwner bot = firstAid?._owner;
            Player player = bot?.GetPlayer;
            if (player?.HealthController == null || firstAid.Using || !firstAid._bodyPartToHeal.HasValue ||
                FollowerMedical.HasActiveBleeding(player)) return;
            EBodyPart part = firstAid._bodyPartToHeal.Value;
            if (player.HealthController.IsBodyPartBroken(part) || player.HealthController.IsBodyPartDestroyed(part)) return;
            ValueStruct health = player.HealthController.GetBodyPartHealth(part, false);
            if (health.Current <= 0f || health.Current >= health.Maximum - 0.5f) return;

            Observe(bot);
            State state = States.GetValue(player, _ => new State());
            if (!state.Parts.TryGetValue(part, out Attempt attempt))
            {
                attempt = new Attempt();
                state.Parts.Add(part, attempt);
            }
            attempt.Health = health.Current;
            state.Target = part;
            state.Pending = true;
        }

        internal static void Observe(BotOwner bot)
        {
            Player player = bot?.GetPlayer;
            if (player?.HealthController == null || !States.TryGetValue(player, out State state) ||
                !state.Pending || bot.Medecine?.FirstAid?.Using == true) return;
            state.Pending = false;
            Attempt attempt = state.Parts[state.Target];
            float current = player.HealthController.GetBodyPartHealth(state.Target, false).Current;
            if (current > attempt.Health + 0.5f)
            {
                attempt.Failures = 0;
                attempt.RetryAt = 0f;
            }
            else
            {
                attempt.Failures++;
                attempt.RetryAt = Time.time + (attempt.Failures >= 2 ? 30f : 3f);
            }
        }

        internal static bool CanAttempt(Player player, EBodyPart part)
        {
            if (player == null || !States.TryGetValue(player, out State state) ||
                !state.Parts.TryGetValue(part, out Attempt attempt)) return true;
            if (Time.time >= attempt.RetryAt)
            {
                if (attempt.Failures >= 2) attempt.Failures = 0;
                return true;
            }
            // Real combat and treatable bleeding/fractures keep their emergency priority.
            BotOwner bot = player.AIData?.BotOwner;
            if (Components.BotFollowerPlayer.IsEnemyInfoAlive(bot?.Memory?.GoalEnemy) ||
                FollowerMedical.HasActiveBleeding(player) || player.HealthController.IsBodyPartBroken(part)) return true;
            return false;
        }

        internal static void ValidateSelection(BotFirstAid firstAid)
        {
            BotOwner bot = firstAid?._owner;
            Player player = bot?.GetPlayer;
            Observe(bot);
            if (player?.HealthController == null || firstAid.Using || !firstAid.Damaged ||
                !firstAid._bodyPartToHeal.HasValue || CanAttempt(player, firstAid._bodyPartToHeal.Value)) return;
            // Continue native HP priority on another part rather than suppressing all medicine.
            firstAid.Damaged = false;
            firstAid._bodyPartToHeal = null;
            foreach (EBodyPart part in firstAid.parts)
            {
                ValueStruct health = player.HealthController.GetBodyPartHealth(part, false);
                if (health.Normalized <= 0f || health.Normalized >= bot.Settings.FileSettings.Mind.PART_PERCENT_TO_HEAL ||
                    !CanAttempt(player, part) || firstAid.CurUsingMeds == null ||
                    !player.HealthController.CanApplyItem(firstAid.CurUsingMeds, part)) continue;
                firstAid.Damaged = true;
                firstAid._bodyPartToHeal = part;
                return;
            }
        }
    }
}
