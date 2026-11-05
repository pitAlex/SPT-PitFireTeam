using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.UI.Gestures;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;

namespace pitTeam.Patches
{
    internal class TeamSplitMenuAvailabilityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(GesturesMenu), nameof(GesturesMenu.Show));
        [PatchPrefix]
        private static void PatchPrefix(GesturesMenu __instance)
        {
            CompanionTeamCommands.Refresh();
            var boss = BossPlayers.GetFollowers().Select(f => f.GetBoss())
                .FirstOrDefault(b => b?.realPlayer?.IsYourPlayer == true);
            bool eligible = CompanionTeamCommands.Available(boss);
            var phrases = (HashSet<EPhraseTrigger>)AccessTools.Field(typeof(GesturesMenu), "_availablePhrases").GetValue(__instance);
            // Availability controls rows only. Native persistent Binds are never edited.
            foreach (var id in new[] { CustomPhrases.TeamSplit, CustomPhrases.TeamCombine })
                if (eligible) phrases.Add((EPhraseTrigger)id); else phrases.Remove((EPhraseTrigger)id);
        }
    }
}
