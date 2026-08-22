using EFT;
using System;

namespace pitTeam.Modules
{
    internal static class FollowerGoalEnemyTracker
    {
        [ThreadStatic]
        private static GoalEnemySetContext? currentContext;

        public static GoalEnemySetScope Begin(string source, string reason)
        {
            GoalEnemySetContext? previous = currentContext;
            currentContext = new GoalEnemySetContext(source, reason);
            return new GoalEnemySetScope(previous);
        }

        public static string CurrentReason => currentContext?.Reason ?? "unscopedSetter";

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordSetter(
            BotOwner? botOwner,
            EnemyInfo? previous,
            EnemyInfo? next,
            bool allowed,
            string? blockedReason = null)
        {
            if (!BattleRecorder.IsRecordingFor(botOwner))
            {
                return;
            }

            if (allowed &&
                string.Equals(previous?.ProfileId, next?.ProfileId, StringComparison.Ordinal))
            {
                return;
            }

            GoalEnemySetContext? context = currentContext;
            string source = context?.Source ?? "unscopedSetter";
            string reason = blockedReason ?? context?.Reason ?? "unscopedSetter";

            BattleRecorder.RecordGoalEnemyTransition(
                botOwner,
                previous,
                next,
                source,
                reason,
                allowed);
        }

        internal sealed class GoalEnemySetContext
        {
            public GoalEnemySetContext(string source, string reason)
            {
                Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source;
                Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
            }

            public string Source { get; }
            public string Reason { get; }
        }

        public readonly struct GoalEnemySetScope : IDisposable
        {
            private readonly GoalEnemySetContext? previousContext;

            internal GoalEnemySetScope(GoalEnemySetContext? previousContext)
            {
                this.previousContext = previousContext;
            }

            public void Dispose()
            {
                currentContext = previousContext;
            }
        }
    }
}
