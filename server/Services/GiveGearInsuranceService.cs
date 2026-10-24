using pitTeam.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Commerce;

namespace pitTeam.Server.Services;

// Permanent gifts cancel existing coverage; this never creates items, policies or mail.
[Injectable]
public sealed class GiveGearInsuranceService(FriendlyTeammateStorage storage, SaveServer saves, InsuranceService insurance)
{
    public void Cancel(MongoId sessionId, IEnumerable<string> itemIds)
    {
        var ids = itemIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return;
        var profile = saves.GetProfile(sessionId) ?? throw new InvalidOperationException("Missing gift insurance profile.");
        var pmc = profile.CharacterData?.PmcData;
        if (pmc?.InsuredItems != null)
            pmc.InsuredItems.RemoveAll(policy => policy.ItemId != null && ids.Contains(policy.ItemId.Value.ToString()));
        foreach (var package in profile.InsuranceList ?? [])
            package.Items?.RemoveAll(item => ids.Contains(item.Id.ToString()));
        // Native loss processing may have staged the same IDs before our late raid report arrived.
        if (insurance.InsuranceDictionaryExists(sessionId))
            foreach (var items in insurance.GetInsurance(sessionId)?.Values.AsEnumerable() ?? [])
                items.RemoveAll(item => ids.Contains(item.Id.ToString()));

        // An Immersive follower's insured item can have passed through the player first.
        // Remove its original policy too, without touching unrelated squad equipment.
        foreach (var member in storage.ReadProfiles(sessionId))
        {
            if (member.Aid == null) continue;
            string document = $"{member.Aid}-settings.json";
            var settings = storage.Read<FriendlyTeammateSettings>(sessionId, document);
            if (settings?.InsuredItems.RemoveAll(policy => ids.Contains(policy.ItemId)) > 0)
                storage.Write(sessionId, document, settings);
        }
        // Repeat saves are intentional: an earlier failed save may already have removed the
        // policy from the in-memory profile. Retrying must still make that deletion durable.
        saves.SaveProfileAsync(sessionId).GetAwaiter().GetResult();
    }
}
