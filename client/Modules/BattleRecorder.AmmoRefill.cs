using System;
using System.Diagnostics;
using EFT;

namespace pitTeam.Modules
{
    internal static partial class BattleRecorder
    {
        [Conditional("DEBUG")]
        internal static void RecordAmmoRefill(BotOwner bot, string stage, string reason, Func<object> details)
        {
            if (!CanRecordBot(bot)) return;
            object payload;
            try { payload = details(); }
            catch { return; } // A transient inventory view must not stop recording or affect the bot.
            try { WriteEventInternal("ammoRefill", bot, new { stage, reason, details = payload }); }
            catch (Exception ex) { StopAfterFatalRecorderFailure("Ammo refill recording failure.", ex); }
        }

    }
}
