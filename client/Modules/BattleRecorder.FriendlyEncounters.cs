using System;
using System.Diagnostics;
using EFT;

namespace pitTeam.Modules
{
    internal static partial class BattleRecorder
    {
        [Conditional("DEBUG")]
        internal static void RecordFriendlyEncounter(BotOwner bot, string stage, Func<object> detailsFactory)
        {
            if (!IsRecording() || bot == null || string.IsNullOrEmpty(bot.ProfileId)) return;
            // Encounter candidates are ordinary bots, not recorder follower states.
            try { WriteEventInternal("friendlyEncounter", bot, new { stage, details = detailsFactory() }); }
            catch (Exception ex) { StopAfterFatalRecorderFailure("Friendly encounter recording failure.", ex); }
        }
    }
}
