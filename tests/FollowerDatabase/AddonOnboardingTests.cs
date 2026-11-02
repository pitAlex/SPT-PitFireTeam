using System.Collections;
using System.Reflection;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

internal static class AddonOnboardingTests
{
    internal static void Run(FriendlyTeammateStorage storage, FriendlyServerSettingsService settings, JsonUtil json, Action<bool,string> check)
    {
        const string document = "squad-onboarding.json";
        var service = new FriendlySquadOnboardingService(storage, settings, null!, json);
        var registrations = (IDictionary)typeof(FriendlyGameplayModeAddons).GetField("Modes", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var addon = registrations["Allegiance"]!;
        foreach (bool installed in new[] { true, false })
        {
            if (installed) registrations["Allegiance"] = addon; else registrations.Remove("Allegiance");
            foreach (string mode in new[] { "GunsForHire", "Allegiance" })
            {
                settings.SaveAndApply(new() { GameplayMode = mode });
                var fresh = new MongoId();
                check(service.GetStatus(fresh).ModeChoices.SequenceEqual(new[] { "GunsForHire", "Allegiance" }), "first-time users always get two cards: " + installed + "/" + mode);
                var existing = new MongoId();
                storage.WriteSharedDocument(existing, document, new FriendlySquadOnboardingState { FirstTimeVisit = true, GameplayMode = mode, WelcomeState = "delivered" });
                check(service.GetStatus(existing).ModeChoices.Count == 0, "installing/removing addon never offers another mode prompt");
                var before = storage.ReadSharedDocument<FriendlySquadOnboardingState>(existing, document)!;
                service.Complete(existing, mode, ["GunsForHire", "Allegiance"]);
                check(json.Serialize(storage.ReadSharedDocument<FriendlySquadOnboardingState>(existing, document)) == json.Serialize(before), "existing onboarding and welcome state remain unchanged");
            }
        }
        registrations["Allegiance"] = addon;
    }
}
