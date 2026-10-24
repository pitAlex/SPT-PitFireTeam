using System.Reflection;
using System.Text.Json;
using pitTeam.Server.Models;
using pitTeam.Server.Services;

internal static class DebugRecruitInviteTests
{
    internal static void Run(Action<bool, string> check)
    {
        var service = typeof(FriendlyRecruitService);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var force = service.GetMethod("IsDebugForcedInvite", flags);
        var prefer = service.GetMethod("PreferDebugRecruitCandidates", flags);
#if DEBUG
        check(force != null && prefer != null, "Debug server contains command-scoped invitation test policy");
        bool Forced(FriendlyRecruitPickupCandidate candidate) => (bool)force!.Invoke(null, [candidate])!;
        foreach (string side in new[] { "Usec", "Bear", "usec", "bear" })
        {
            var candidate = new FriendlyRecruitPickupCandidate { Side = side };
            check(!Forced(candidate), "Normal PMC recruits never gain the Debug invitation override");
            candidate.DebugForceInvite = true;
            check(Forced(candidate), "Only explicitly marked Debug PMCs bypass invitation chance");
            var copy = JsonSerializer.Deserialize<FriendlyRecruitPickupCandidate>(JsonSerializer.Serialize(candidate))!;
            check(Forced(copy), "Debug recruit marker survives the client/server JSON boundary");
        }
        foreach (string side in new[] { "Savage", "", "invalid" })
            check(!Forced(new() { Side = side, DebugForceInvite = true }), "Debug Scavs/invalid factions cannot force PMC invitations");
        var normal = new FriendlyRecruitRequestEntry { ProfileId = "normal" };
        var first = new FriendlyRecruitRequestEntry { ProfileId = "debug-a", GivenEquipmentSlots = ["Headwear"] };
        var second = new FriendlyRecruitRequestEntry { ProfileId = "debug-b" };
        var candidates = new List<FriendlyRecruitRequestEntry> { normal, first, second };
        List<FriendlyRecruitRequestEntry> Prefer(HashSet<string> ids) =>
            (List<FriendlyRecruitRequestEntry>)prefer!.Invoke(null, [candidates, ids])!;
        check(ReferenceEquals(Prefer([]), candidates), "No marked command recruit preserves the normal candidate pool");
        check(ReferenceEquals(Prefer(["missing"]), candidates), "Stale Debug identities do not replace normal candidates");
        check(Prefer(["debug-a"]).SequenceEqual([first]), "Normal candidates cannot crowd out the requested Debug recruit");
        check(Prefer(["debug-a", "debug-b"]).SequenceEqual([first, second]), "Multiple Debug recruits retain normal single-pick candidate selection");
        check(first.GivenEquipmentSlots.SequenceEqual(["Headwear"]) && !first.DebugForceInvite,
            "Candidate prioritization neither invents slot credit nor persists a forced marker in the pending entry");
#else
        check(force == null && prefer == null, "Release server has no Debug invitation-bypass methods");
        check(typeof(FriendlyRecruitPickupCandidate).GetProperty("DebugForceInvite") == null,
            "Release request model cannot deserialize a Debug invitation marker");
        var candidate = JsonSerializer.Deserialize<FriendlyRecruitPickupCandidate>("{\"Side\":\"Usec\",\"DebugForceInvite\":true}")!;
        check(candidate.Side == "Usec" && !JsonSerializer.Serialize(candidate).Contains("DebugForceInvite"),
            "Injected Debug JSON is ignored by the real Release model");
#endif
    }
}
