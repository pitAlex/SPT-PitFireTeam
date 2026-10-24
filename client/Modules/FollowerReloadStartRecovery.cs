using System.Runtime.CompilerServices;
using EFT;

namespace pitTeam.Modules
{
    // Carries a proven native skipped-start result into the existing patrol retry budget.
    // No timers, decisions, animation resets or live reload-flag mutations belong here.
    internal static class FollowerReloadStartRecovery
    {
        private sealed class SkippedStart
        {
            internal string WeaponId;
            internal string Reason;
        }

        private static readonly ConditionalWeakTable<BotOwner, SkippedStart> Pending =
            new ConditionalWeakTable<BotOwner, SkippedStart>();

        internal static void Record(BotOwner bot, string weaponId, string reason)
        {
            Pending.Remove(bot);
            Pending.Add(bot, new SkippedStart { WeaponId = weaponId, Reason = reason });
        }

        internal static void Clear(BotOwner bot) => Pending.Remove(bot);

        internal static bool TryConsume(BotOwner bot, string weaponId, out string reason)
        {
            reason = null;
            if (!Pending.TryGetValue(bot, out SkippedStart pending)) return false;
            Pending.Remove(bot);
            if (pending.WeaponId != weaponId) return false;
            reason = pending.Reason;
            return true;
        }
    }
}
