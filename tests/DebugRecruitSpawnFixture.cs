using System;
using System.Collections.Generic;

internal enum EPlayerSide { Usec, Bear, Savage }
internal enum WildSpawnType { pmcUSEC, pmcBEAR, assault, marksman }
internal sealed class Profile { internal ProfileInfo Info = new ProfileInfo(); }
internal sealed class ProfileInfo { internal EPlayerSide Side; internal ProfileSettings Settings = new ProfileSettings(); }
internal sealed class ProfileSettings { internal WildSpawnType Role; }
internal interface IPlayer { bool IsYourPlayer { get; } string ProfileId { get; } }
internal sealed class TestPlayer : IPlayer
{
    public bool IsYourPlayer { get; set; } = true;
    public string ProfileId { get; set; } = "human";
}
internal sealed class BotOwner
{
    internal string ProfileId = "bot";
    internal BotsGroup BotsGroup = new BotsGroup();
    internal bool Candidate = true;
}
internal sealed class BotsGroup
{
    internal event Action<BotOwner> OnMemberAdd;
    internal void Add(BotOwner bot) => OnMemberAdd?.Invoke(bot);
}
internal sealed class Greeting
{
    internal int Cancelled;
    internal void Cancel() => Cancelled++;
}
internal sealed class Interaction
{
    internal int Cancelled;
    internal void Cancel(BotOwner bot) => Cancelled++;
}
internal static class Fixture
{
    private sealed class Decision
    {
        internal BotOwner Bot;
        internal BotsGroup Group;
        internal bool Friendly;
        internal float SelectionChance;
        internal bool? RecruitmentAccepted;
        internal float? RecruitmentChance;
        internal Action<BotOwner> MemberAdded;
        internal readonly Greeting Greeting = new Greeting();
        internal readonly Greeting CombatGreeting = new Greeting();
        internal readonly Interaction Interaction = new Interaction();
    }
    private static readonly Dictionary<string, Decision> Decisions = new Dictionary<string, Decision>();
    private static IPlayer Human = new TestPlayer();
    private static bool IsCandidate(BotOwner bot) => bot.Candidate;
    /* SIDE POLICY */
    /* REGISTRATION POLICY */
    /* PROFILE POLICY */

    private static int checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception(label);
        checks++;
    }
    public static void Main()
    {
        foreach (EPlayerSide rawSide in Enum.GetValues(typeof(EPlayerSide)))
        foreach (WildSpawnType role in new[] { WildSpawnType.pmcUSEC, WildSpawnType.pmcBEAR, WildSpawnType.assault })
        {
            EPlayerSide expected = role == WildSpawnType.pmcUSEC ? EPlayerSide.Usec :
                role == WildSpawnType.pmcBEAR ? EPlayerSide.Bear : EPlayerSide.Savage;
            var profile = new Profile(); profile.Info.Side = rawSide; profile.Info.Settings.Role = role;
            Check(TryPrepareDebugRecruitProfile(profile, expected, role) == (role != WildSpawnType.assault || rawSide == EPlayerSide.Savage),
                "Raw PMC sides normalize from their role, while a real Scav must remain Savage");
            Check(profile.Info.Settings.Role == role && profile.Info.Side == (role == WildSpawnType.assault ? rawSide : expected),
                "Preparation never rewrites a bot role or converts a real Scav into a PMC");
            var wrong = new Profile(); wrong.Info.Side = rawSide; wrong.Info.Settings.Role = WildSpawnType.marksman;
            Check(!TryPrepareDebugRecruitProfile(wrong, expected, role) && wrong.Info.Side == rawSide && wrong.Info.Settings.Role == WildSpawnType.marksman,
                "A genuinely wrong generated role is rejected without disguising it");
        }
        Check(!TryPrepareDebugRecruitProfile(null, EPlayerSide.Bear, WildSpawnType.pmcBEAR), "Missing profile is rejected safely");
        Check(!TryPrepareDebugRecruitProfile(new Profile { Info = null }, EPlayerSide.Usec, WildSpawnType.pmcUSEC), "Missing profile info is rejected safely");
        Check(!TryPrepareDebugRecruitProfile(new Profile { Info = new ProfileInfo { Settings = null } }, EPlayerSide.Usec, WildSpawnType.pmcUSEC), "Missing profile settings are rejected safely");
        foreach (WildSpawnType expectedRole in new[] { WildSpawnType.pmcUSEC, WildSpawnType.pmcBEAR })
        {
            var scav = new Profile(); scav.Info.Side = EPlayerSide.Savage; scav.Info.Settings.Role = WildSpawnType.assault;
            Check(!TryPrepareDebugRecruitProfile(scav, expectedRole == WildSpawnType.pmcUSEC ? EPlayerSide.Usec : EPlayerSide.Bear, expectedRole) &&
                scav.Info.Side == EPlayerSide.Savage && scav.Info.Settings.Role == WildSpawnType.assault,
                "A real assault Scav can never pass a PMC request by having its side overwritten");
        }
        foreach (EPlayerSide leader in Enum.GetValues(typeof(EPlayerSide)))
        foreach (bool allegiance in new[] { false, true })
        foreach (bool chooseBear in new[] { false, true })
        {
            Check(ResolveDebugRecruitSide(leader, allegiance, false, chooseBear) ==
                (allegiance ? (chooseBear ? EPlayerSide.Bear : EPlayerSide.Usec) : leader),
                "PMC request uses mode/current faction or either Allegiance coin outcome");
            Check(ResolveDebugRecruitSide(leader, allegiance, true, chooseBear) == EPlayerSide.Savage,
                "Scav command always overrides mode, leader faction and coin");
        }

        var bot = new BotOwner();
        RegisterDebugRecruit(null, Human);
        RegisterDebugRecruit(bot, null);
        RegisterDebugRecruit(bot, new TestPlayer { IsYourPlayer = false });
        RegisterDebugRecruit(bot, new TestPlayer { ProfileId = "other-human" });
        bot.ProfileId = "";
        RegisterDebugRecruit(bot, Human);
        bot.ProfileId = "bot";
        bot.Candidate = false;
        RegisterDebugRecruit(bot, Human);
        Check(Decisions.Count == 0, "Invalid and non-local targets cannot acquire a Debug encounter decision");
        bot.Candidate = true;
        RegisterDebugRecruit(bot, Human);
        var first = Decisions[bot.ProfileId];
        Check(first.Bot == bot && first.Group == bot.BotsGroup && first.Friendly,
            "Selected identity retains exact bot and native group");
        Check(first.SelectionChance == 1f && first.RecruitmentAccepted == true && first.RecruitmentChance == 1f,
            "Test spawn cannot fail the friendly/acceptance roll");
        int callbacks = 0;
        first.MemberAdded = _ => callbacks++;
        first.Group.OnMemberAdd += first.MemberAdded;
        RegisterDebugRecruit(bot, Human);
        first.Group.Add(bot);
        Check(first.Greeting.Cancelled == 1 && first.Interaction.Cancelled == 1 && callbacks == 0,
            "Replacing an earlier encounter clears greeting, interaction and group subscription");
        Check(Decisions.Count == 1 && Decisions[bot.ProfileId] != first,
            "Repeat registration replaces rather than duplicates the raid-local decision");
        Human = null;
        RegisterDebugRecruit(new BotOwner { ProfileId = "late" }, new TestPlayer());
        Check(Decisions.Count == 1, "Raid teardown cannot register a late Debug recruit");
        Console.WriteLine("PASS: " + checks + " production Debug recruit mode and identity policy assertions; not Unity spawning.");
    }
}
