#if DEBUG
using System;
using EFT;

namespace pitTeam.Modules
{
    internal static partial class BattleRecorder
    {
        // Explicit handover events only. No inventory polling or decision-provider calls.
        internal static void RecordGiveGear(BotOwner bot, string attemptId, object details)
        {
            if (!IsRecording() || bot == null || string.IsNullOrEmpty(bot.ProfileId)) return;
            try { WriteEventInternal("giveGear", bot, new { attemptId, details }); }
            catch (Exception ex) { StopAfterFatalRecorderFailure("Give Gear recording failure.", ex); }
        }
    }
}
#endif
