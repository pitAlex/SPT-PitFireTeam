using System;
using EFT;
using pitTeam.Components;
using UnityEngine;

namespace pitTeam.Modules
{
    // Optional companion policy; Core retains input dispatch and execution ownership.
    public static class CompanionTeamCommands
    {
        private static Func<pitAIBossPlayer, bool> available;
        private static Func<pitAIBossPlayer, bool, bool> setSplit;
        private static Action refresh;
        private static bool failureReported;
        [ThreadStatic] private static InputScope input;

        public static void Register(Func<pitAIBossPlayer, bool> canSplit,
            Func<pitAIBossPlayer, bool, bool> changeSplit, Action refreshState)
        { available = canSplit; setSplit = changeSplit; refresh = refreshState; failureReported = false; }

        public static void Unregister() { available = null; setSplit = null; refresh = null; }
        public static void Refresh()
        {
            try { refresh?.Invoke(); }
            catch (Exception ex) { ReportFailure(ex); }
        }
        public static bool Available(pitAIBossPlayer boss)
        {
            try { return boss != null && available?.Invoke(boss) == true; }
            catch (Exception ex) { ReportFailure(ex); return false; }
        }
        public static bool SetSplit(pitAIBossPlayer boss, bool split)
        {
            Refresh();
            try { return Available(boss) && setSplit?.Invoke(boss, split) == true; }
            catch (Exception ex) { ReportFailure(ex); return false; }
        }

        private static void ReportFailure(Exception exception)
        {
            if (failureReported) return;
            failureReported = true;
            Logger.LogError($"[CompanionTeamCommands] Optional team policy failed: {exception}");
        }

        public static bool AllowsPhrase(BotOwner bot, EPhraseTrigger phrase)
        {
            var follower = BossPlayers.Instance?.GetFollower(bot);
            if (follower?.TeamSplitCombatActive != true) return true;
            // Attention resets shared group memory, so every side must receive it.
            if (phrase == EPhraseTrigger.Look || phrase == (EPhraseTrigger)CustomPhrases.TeamStatus) return true;
            if (phrase == EPhraseTrigger.NeedSniper && bot.Profile?.Info?.Settings?.Role == WildSpawnType.followerBirdEye) return true;
            var human = follower.GetBoss()?.realPlayer;
            return phrase == EPhraseTrigger.NeedHelp && human != null &&
                IsFinite(bot.Position) && IsFinite(human.Position) &&
                (bot.Position - human.Position).sqrMagnitude <= 40f * 40f;
        }

        private static bool IsFinite(Vector3 point) =>
            !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
            !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
            !float.IsNaN(point.z) && !float.IsInfinity(point.z);

        internal static IDisposable Enter(pitAIBossPlayer boss, IPlayer requester,
            EPhraseTrigger phrase, bool gesture = false)
        {
            if (requester == null || boss == null || requester.ProfileId != boss.realPlayer?.ProfileId) return null;
            Refresh();
            return new InputScope(boss, phrase, gesture);
        }

        internal static bool AllowsCurrentInput(BotOwner bot)
            => input == null || !ReferenceEquals(BossPlayers.Instance?.GetFollower(bot)?.GetBoss(), input.Boss) ||
               input.Gesture || AllowsPhrase(bot, input.Phrase);

        private sealed class InputScope : IDisposable
        {
            internal readonly pitAIBossPlayer Boss;
            internal readonly EPhraseTrigger Phrase;
            internal readonly bool Gesture;
            private readonly InputScope previous;
            internal InputScope(pitAIBossPlayer boss, EPhraseTrigger phrase, bool gesture)
            { Boss = boss; Phrase = phrase; Gesture = gesture; previous = input; input = this; }
            public void Dispose() { input = previous; }
        }
    }
}
