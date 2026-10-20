using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using pitTeam.Patches;

namespace pitTeam.Modules
{
    // Raid-local encounters in both modes. Never changes shared bot settings or factions.
    internal static class AllegiancePmcFriendship
    {
        private const string GreetingSubscriber = "AllegianceFriendlyGreeting";
        private sealed class Decision
        {
            internal BotOwner Bot;
            internal BotsGroup Group;
            internal bool Friendly;
            internal float SelectionChance;
            internal bool? RecruitmentAccepted;
            internal float? RecruitmentChance;
            internal Action<BotOwner> MemberAdded;
            internal readonly AllegianceFriendlyGreeting Greeting = new AllegianceFriendlyGreeting();
            internal readonly FriendlyEncounterInteraction Interaction = new FriendlyEncounterInteraction();
        }

        private static readonly Dictionary<string, Decision> Decisions = new Dictionary<string, Decision>(StringComparer.Ordinal);
        private static Player Human => Singleton<GameWorld>.Instance?.MainPlayer;
        internal static Player EncounterPlayer => Human;
        private static string LogPrefix => GameplayModeRuntime.IsAllegiance ? "[Allegiance]" : "[GunsForHire]";
        internal static bool IsEnabled => GameplayModeRuntime.IsAllegiance ||
            (!Utils.Utils.FlagGet("pitFireTeam") && !Utils.Utils.FlagGet("isBadGuy"));

        internal static float GetSameSideChance() => Math.Max(5, Math.Min(80, pitFireTeam.friendlyEncounterChance?.Value ?? 30)) / 100f;
        internal static float GetOppositeSideChance(float sameSideChance) => sameSideChance <= 0.10f
            ? sameSideChance : sameSideChance < 0.30f
                ? 0.10f + (sameSideChance - 0.10f) * 0.25f : sameSideChance * 0.5f;

        private static float GetSelectionChance(EPlayerSide botSide, EPlayerSide playerSide)
        {
            float sameSideChance = GetSameSideChance();
            float oppositeSideChance = GetOppositeSideChance(sameSideChance);
            float chance = botSide == EPlayerSide.Savage
                ? ScavFenceKarma.EncounterChance(oppositeSideChance, Human?.Profile?.FenceInfo?.Standing ?? 0d)
                : botSide == playerSide ? sameSideChance : oppositeSideChance;
            return Shared.FriendlyEncounterPenaltyPolicy.Apply(chance,
                GameplayModeRuntime.IsAllegiance ? FriendlyEncounterPenaltyRuntime.GetReductionPercent() : 0);
        }

        internal static void Reset()
        {
            BotOwnerUpdateHub.Unregister(GreetingSubscriber);
            foreach (var decision in Decisions.Values)
            {
                decision.Greeting.Cancel();
                decision.Interaction.Cancel(decision.Bot);
                if (decision.MemberAdded != null) decision.Group.OnMemberAdd -= decision.MemberAdded;
            }
            Decisions.Clear();
            FriendlyEncounterInteraction.ResetRaid();
        }

        private static bool IsPmc(BotOwner bot) =>
            bot?.Profile?.Info?.Settings?.Role == WildSpawnType.pmcUSEC ||
            bot?.Profile?.Info?.Settings?.Role == WildSpawnType.pmcBEAR;

        internal static bool IsRegularScav(BotOwner bot) => bot?.Side == EPlayerSide.Savage &&
            bot.Profile?.Info?.Settings?.Role == WildSpawnType.assault;

        private static bool IsCandidate(BotOwner bot) => IsPmc(bot) ||
            (GameplayModeRuntime.IsAllegiance && IsRegularScav(bot));

#if DEBUG
        internal static void RegisterDebugRecruit(BotOwner bot, IPlayer leader)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId) || leader?.IsYourPlayer != true ||
                leader.ProfileId != Human?.ProfileId || !IsCandidate(bot)) return;
            if (Decisions.TryGetValue(bot.ProfileId, out var previous))
            {
                previous.Greeting.Cancel();
                previous.Interaction.Cancel(previous.Bot);
                if (previous.MemberAdded != null) previous.Group.OnMemberAdd -= previous.MemberAdded;
            }
            Decisions[bot.ProfileId] = new Decision
            {
                Bot = bot, Group = bot.BotsGroup, Friendly = true,
                SelectionChance = 1f, RecruitmentAccepted = true, RecruitmentChance = 1f
            };
        }
#endif

        private static bool IsGrouped(BotOwner bot)
        {
            var group = bot.BotsGroup;
            var original = bot.SpawnProfileData?.SpawnParams?.ShallBeGroup;
            return group == null || group is BotsGroupPlayer || group.MembersCount > 1 ||
                   group.TargetMembersCount > 1 || (original != null && original.Group && original.StartCount > 1);
        }

        internal static void Apply(BotOwner bot)
        {
            var human = Human;
            if (!IsEnabled || human == null || human.Side == EPlayerSide.Savage ||
                !IsCandidate(bot) || bot.IsDead || bot.GetPlayer?.HealthController?.IsAlive != true ||
                BossPlayers.IsFollower(bot) || AllegianceScavRelations.RefusesRecruitment(bot) || string.IsNullOrEmpty(bot.ProfileId)) return;
            if (!GameplayModeRuntime.IsAllegiance && bot.Side != human.Side) return;

            if (!Decisions.TryGetValue(bot.ProfileId, out var decision))
            {
                decision = new Decision { Bot = bot, Group = bot.BotsGroup };
                Decisions.Add(bot.ProfileId, decision);
                if (IsGrouped(bot))
                {
                    Logger.LogInfo($"{LogPrefix} candidate={bot.ProfileId} decision=excluded-group");
                    RecordSelection(bot, "excludedGroup");
                    return;
                }
                float roll = UnityEngine.Random.value;
                float chance = GetSelectionChance(bot.Side, human.Side);
                decision.SelectionChance = chance;
                decision.Friendly = chance >= 1f || roll < chance;
                if (decision.Friendly)
                {
                    // Reserve this raid-local solo group. Native spawn lookup skips locked
                    // groups, so a later wave cannot reuse it and revoke the encounter.
                    decision.Group.Lock();
                    BotOwnerUpdateHub.Register(GreetingSubscriber, UpdateGreeting);
                    decision.MemberAdded = member =>
                    {
                        if (BossPlayers.IsFollower(decision.Bot) || decision.Bot.BotsGroup != decision.Group)
                        {
                            decision.Group.OnMemberAdd -= decision.MemberAdded;
                            decision.MemberAdded = null;
                            return;
                        }
                        if (decision.Group.MembersCount > 1 && decision.Friendly)
                        {
                            Revoke(decision, "joined-group");
                            // Losing solo eligibility is not aggression. Keep the existing
                            // neutral relationship; native hostility/contact/damage owns escalation.
                        }
                    };
                    decision.Group.OnMemberAdd += decision.MemberAdded;
                }
                Logger.LogInfo($"{LogPrefix} candidate={bot.ProfileId} side={bot.Side} playerSide={human.Side} roll={roll:F3} chance={chance:F2} friendly={decision.Friendly}");
                RecordSelection(bot, decision.Friendly ? "selected" : "failedRoll", roll, chance);
            }
            if (!IsFriendly(bot)) return;

            Neutralize(bot, human);
            PmcKarmaRuntime.NoteFriendly(bot);
            foreach (var follower in BossPlayers.GetFollowersByBoss(human.ProfileId))
            {
                var member = follower?.GetBot();
                if (member != null && !member.IsDead && member.GetPlayer != null)
                {
                    Neutralize(bot, member.GetPlayer);
                    FactionHostility.EnsureNeutral(member.BotsGroup, bot.GetPlayer);
                    member.Memory?.DeleteInfoAboutEnemy(bot.GetPlayer);
                }
            }
        }

        private static void UpdateGreeting(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId) || BossPlayers.IsFollower(bot) ||
                !Decisions.TryGetValue(bot.ProfileId, out var decision) || decision.Bot != bot ||
                !IsFriendly(bot)) return;
            decision.Greeting.Update(bot, Human);
        }

        internal static void OnFollowerAdded(BotOwner follower, IPlayer leader)
        {
            var human = Human;
            if (!IsEnabled || human == null || human.Side == EPlayerSide.Savage ||
                leader?.ProfileId != human.ProfileId || follower == null || follower.IsDead ||
                follower.BotsGroup == null || follower.GetPlayer?.HealthController?.IsAlive != true ||
                !BossPlayers.IsFollower(follower)) return;

            if (Decisions.TryGetValue(follower.ProfileId, out var recruited) && recruited.Bot == follower)
            {
                recruited.Greeting.Cancel();
                recruited.Interaction.Cancel(follower);
            }

            // Selection may predate this follower. Ambient AddEnemy guards cannot remove
            // the pair's existing hostility, including SAIN's separately cached contact.
            foreach (var decision in Decisions.Values)
            {
                var candidate = decision.Bot;
                if (!decision.Friendly || candidate == null || candidate.IsDead ||
                    candidate.GetPlayer?.HealthController?.IsAlive != true ||
                    BossPlayers.IsFollower(candidate) || !IsFriendly(candidate)) continue;

                Neutralize(candidate, follower.GetPlayer);
                Neutralize(follower, candidate.GetPlayer);
                Logger.LogInfo($"{LogPrefix} follower={follower.ProfileId} candidate={candidate.ProfileId} friendship-refreshed=both-directions");
            }
        }

        private static void Neutralize(BotOwner bot, IPlayer person)
        {
            FactionHostility.EnsureNeutral(bot.BotsGroup, person);
            bot.Memory?.DeleteInfoAboutEnemy(person);
        }

        private static bool IsFriendly(BotOwner bot)
        {
            if (bot == null || !Decisions.TryGetValue(bot.ProfileId, out var decision) || !decision.Friendly) return false;
            if (AllegianceScavRelations.RefusesRecruitment(bot)) Revoke(decision, "scav-trust-broken");
            if (!BossPlayers.IsFollower(bot) && IsGrouped(bot)) Revoke(decision, "grouped");
            return decision.Friendly;
        }

        internal static bool CanRecruit(BotOwner bot, IPlayer player) =>
            !AllegianceScavRelations.RefusesRecruitment(bot) &&
            (!GameplayModeRuntime.IsAllegiance || player?.Side == EPlayerSide.Savage || IsSelectedFriendly(bot, player));

        internal static bool IsSelectedFriendly(BotOwner bot, IPlayer player) =>
            IsEnabled && !AllegianceScavRelations.RefusesRecruitment(bot) && player != null && player.ProfileId == Human?.ProfileId && IsCandidate(bot) && IsFriendly(bot);

        internal static bool AcceptScavRecruitment(BotOwner bot, IPlayer player)
        {
            return IsRegularScav(bot) && TryResolveRecruitmentAcceptance(bot, player, false, out bool accepted) && accepted;
        }

        internal static bool TryGetInteraction(BotOwner bot, out FriendlyEncounterInteraction interaction, out AllegianceFriendlyGreeting greeting)
        {
            interaction = null;
            greeting = null;
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId) || !IsSelectedFriendly(bot, Human) || BossPlayers.IsFollower(bot) ||
                !Decisions.TryGetValue(bot.ProfileId, out var decision) || decision.Bot != bot) return false;
            interaction = decision.Interaction;
            greeting = decision.Greeting;
            return true;
        }

        internal static bool TryResolveRecruitmentAcceptance(BotOwner bot, IPlayer player, bool proactive, out bool accepted)
        {
            accepted = false;
            if (!IsSelectedFriendly(bot, player) || !Decisions.TryGetValue(bot.ProfileId, out var decision)) return false;
            float chance = decision.RecruitmentChance ?? (IsRegularScav(bot)
                ? ScavFenceKarma.RecruitmentAcceptanceChance(decision.SelectionChance)
                : GameplayModeRuntime.GetEffectiveValue(pitFireTeam.tieredPickup)
                    ? Math.Max(0f, Math.Min(1f, (player.Profile.Info.Level - bot.Profile.Info.Level + 10) / 20f)) : 1f);
            if (proactive && chance < 0.6f) return false;
            if (!decision.RecruitmentAccepted.HasValue)
            {
                float? roll = chance >= 1f || chance <= 0f ? null : UnityEngine.Random.value;
                decision.RecruitmentChance = chance;
                decision.RecruitmentAccepted = chance >= 1f || (chance > 0f && roll.GetValueOrDefault() < chance);
                RecordSelection(bot, proactive ? (decision.RecruitmentAccepted.Value ? "preRolledYes" : "preRolledNo")
                    : (decision.RecruitmentAccepted.Value ? "accepted" : "refused"), roll, chance, "acceptance");
            }
            accepted = decision.RecruitmentAccepted.Value;
            return true;
        }

        internal static void NoteRequestedRefusal(BotOwner bot)
        {
            if (bot == null || !Decisions.TryGetValue(bot.ProfileId, out var decision)) return;
            if (!IsRegularScav(bot)) BossPlayers.RememberRecruitmentDenial(bot.ProfileId);
            decision.Interaction.BeginRefusal(bot);
            decision.Greeting.Cancel();
            RecordSelection(bot, "requestedRefusal", null, decision.RecruitmentChance, "acceptance");
        }

        internal static bool HandleEncounterPhrase(Player human, EPhraseTrigger phrase)
        {
            if (human != Human) return false;
            var inviting = FriendlyEncounterInteraction.InvitingBot;
            if (phrase == EPhraseTrigger.Roger || phrase == EPhraseTrigger.Negative)
                return TryGetInteraction(inviting, out var pending, out _) && pending.Respond(inviting, human, phrase == EPhraseTrigger.Roger);
            if (phrase != EPhraseTrigger.Cooperation) return false;
            // During the waiting window, directed Cooperate reaches the bot from 12 m.
            Decision target = null;
            float nearest = float.MaxValue;
            foreach (var decision in Decisions.Values)
            {
                var bot = decision.Bot;
                if (!decision.Interaction.Waiting || !IsSelectedFriendly(bot, human) ||
                    !decision.Interaction.CanWait(bot, human) ||
                    !AllegianceFriendlyGreeting.HasSight(bot, human, FriendlyEncounterInteraction.Range)) continue;
                if (human.InteractablePlayer != null && human.InteractablePlayer != bot.GetPlayer) continue;
                var parts = bot.GetPlayer.MainParts;
                var point = parts != null && parts.TryGetValue(BodyPartType.body, out var body) ? body.Position : bot.Position;
                var ray = human.InteractionRay;
                var delta = point - ray.origin;
                if (ray.direction.sqrMagnitude < 0.01f || delta.sqrMagnitude < 0.01f || UnityEngine.Vector3.Angle(ray.direction, delta) > 18f) continue;
                float distance = (bot.Position - human.Position).sqrMagnitude;
                if (distance >= nearest) continue;
                target = decision;
                nearest = distance;
            }
            if (target == null) return false;
            target.Interaction.End(target.Bot);
            target.Bot.BotsGroup?.RequestsController?.TryAskFollowMeRequest(human, target.Bot);
            return true;
        }

        internal static bool HandleEncounterGesture(Player human, EInteraction gesture)
        {
            if (human != Human || (gesture != EInteraction.OkGesture && gesture != EInteraction.NoGesture)) return false;
            var bot = FriendlyEncounterInteraction.InvitingBot;
            return TryGetInteraction(bot, out var pending, out _) && pending.Respond(bot, human, gesture == EInteraction.OkGesture);
        }

        internal static bool ShouldBlockEnemyAddition(BotsGroup group, IPlayer person, EBotEnemyCause cause)
        {
            if (!IsEnabled || group == null || person == null) return false;
            var human = Human;
            if (human == null) return false;
            Decision matched = null;
            foreach (var decision in Decisions.Values)
            {
                if (!decision.Friendly || BossPlayers.IsFollower(decision.Bot)) continue;
                bool toSquad = group == decision.Bot.BotsGroup &&
                    (person.ProfileId == human.ProfileId || FollowerGroupHostility.FindBoss(person)?.realPlayer?.ProfileId == human.ProfileId);
                bool fromSquad = person.ProfileId == decision.Bot.ProfileId &&
                    ((group is BotsGroupPlayer playerGroup && playerGroup.Boss?.realPlayer?.ProfileId == human.ProfileId) ||
                     AllegianceScavRelations.IsCompanionGroup(group));
                if (toSquad || fromSquad) { matched = decision; break; }
            }
            if (matched == null || !IsFriendly(matched.Bot)) return false;
            if (FollowerGroupHostility.IsExplicitHostility(cause))
            {
                Revoke(matched, cause.ToString());
                return false;
            }
            return true;
        }

        internal static void RevokeGroup(BotsGroup group, EBotEnemyCause cause)
        {
            if (!IsEnabled || group == null || !FollowerGroupHostility.IsExplicitHostility(cause)) return;
            foreach (var decision in Decisions.Values)
                if (decision.Friendly && !BossPlayers.IsFollower(decision.Bot) && decision.Bot.BotsGroup == group)
                    Revoke(decision, cause.ToString());
            // Contact/damage can precede activation of a standby bot. Never clear that
            // real hostility when its first activation finally reaches the selection hook.
            var human = Human;
            if (human == null || human.Side == EPlayerSide.Savage) return;
            for (int i = 0; i < group.MembersCount; i++)
            {
                var bot = group.Member(i);
                if (!IsCandidate(bot) || BossPlayers.IsFollower(bot) ||
                    string.IsNullOrEmpty(bot.ProfileId) || Decisions.ContainsKey(bot.ProfileId)) continue;
                if (!GameplayModeRuntime.IsAllegiance && bot.Side != human.Side) continue;
                Decisions.Add(bot.ProfileId, new Decision { Bot = bot, Group = group });
                Logger.LogInfo($"{LogPrefix} candidate={bot.ProfileId} decision=hostile-before-selection cause={cause}");
            }
        }

        private static void Revoke(Decision decision, string reason)
        {
            decision.Friendly = false;
            decision.Greeting.Cancel();
            decision.Interaction.Cancel(decision.Bot);
            // Reactivation and repeat requests cannot reroll this decision.
            Logger.LogInfo($"{LogPrefix} candidate={decision.Bot.ProfileId} friendship-revoked={reason}");
            RecordSelection(decision.Bot, "revoked/" + reason);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void RecordSelection(BotOwner bot, string reason, float? roll = null, float? chance = null, string stage = "selection")
        {
            BattleRecorder.RecordFriendlyEncounter(bot, stage, () => new
            {
                reason, roll, chance, role = bot.Profile?.Info?.Settings?.Role.ToString(),
                side = bot.Side.ToString(), groupId = bot.BotsGroup?.Id,
                members = bot.BotsGroup?.MembersCount, kind = IsRegularScav(bot) ? "scav" : "pmc"
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        internal static void RecordCooperationAttempt(BotOwner bot, IPlayer requester, bool canReact)
        {
            BattleRecorder.RecordFriendlyEncounter(bot, "cooperate", () =>
            {
                Decisions.TryGetValue(bot.ProfileId, out var decision);
                var boss = BossPlayers.GetBoss(requester.ProfileId);
                return new
                {
                    canReact, role = bot.Profile?.Info?.Settings?.Role.ToString(), side = bot.Side.ToString(),
                    candidate = IsCandidate(bot), selectionKnown = decision != null,
                    selected = decision?.Friendly, trustBroken = AllegianceScavRelations.TrustBroken,
                    encounterChance = decision?.SelectionChance, recruitmentAccepted = decision?.RecruitmentAccepted,
                    groupId = bot.BotsGroup?.Id, members = bot.BotsGroup?.MembersCount,
                    enemyToPlayer = bot.BotsGroup?.IsEnemy(requester), memoryHasEnemy = bot.Memory?.HaveEnemy,
                    followers = boss?.Followers?.Where(f => f != null && f.GetPlayer != null).Select(f => new
                    {
                        profileId = f.ProfileId,
                        candidateEnemyToFollower = bot.BotsGroup?.IsEnemy(f.GetPlayer),
                        followerEnemyToCandidate = bot.GetPlayer != null ? f.BotsGroup?.IsEnemy(bot.GetPlayer) : null
                    }).ToArray()
                };
            });
        }
    }
}
