using System;
using System.Collections.Generic;
using System.Linq;
using pitTeam.Modules;

internal static class LootFollowerEligibilityFixture
{
    private static int checks;

    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    public static int Main()
    {
        BossPlayers.Instance = new BossPlayers();
        var boss = new pitTeam.Components.pitAIBossPlayer();
        var recruitBot = new BotOwner("recruit");
        var recruit = Add(boss, recruitBot, false);
        Check(recruit.CanHandleBodyContainerLootCommands, "Recruit-only squad can loot");
        Check(!recruit.IsSpawnedSquadMate, "Loot permission must not change recruit identity");

        var spawnedBot = new BotOwner("spawned");
        pitTeam.Utils.SpawnHelper.spawnMemberIds.Add("spawned");
        var spawned = Add(boss, spawnedBot, true);
        Check(spawned.CanHandleBodyContainerLootCommands, "Spawned teammate can loot");
        Check(!recruit.CanHandleBodyContainerLootCommands, "Living spawned teammate excludes recruits");
        spawnedBot.Busy = true;
        spawnedBot.Distance = 1000;
        spawnedBot.Active = false;
        Check(!recruit.CanHandleBodyContainerLootCommands, "Busy, distant or inactive living teammate still counts");

        spawnedBot.IsDead = true;
        Check(recruit.CanHandleBodyContainerLootCommands, "Last spawned death immediately enables recruit");
        Check(!recruit.IsSpawnedSquadMate, "Fallback does not enable saved-squad ownership");
        spawnedBot.IsDead = false;
        Check(!recruit.CanHandleBodyContainerLootCommands, "Living spawned teammate restores exclusion");
        boss.Followers.Remove(spawnedBot);
        Check(recruit.CanHandleBodyContainerLootCommands, "Departed teammate does not block recruits");

        var otherBoss = new pitTeam.Components.pitAIBossPlayer();
        Add(otherBoss, spawnedBot, true);
        Check(recruit.CanHandleBodyContainerLootCommands, "Another player's spawned teammate does not block recruits");

        var accountBot = new BotOwner("unmarked-profile");
        accountBot.Profile.AccountId = "spawned-account";
        pitTeam.Utils.SpawnHelper.spawnMemberIdsScav.Add("spawned-account");
        var accountMate = Add(boss, accountBot, true);
        Check(accountMate.IsSpawnedSquadMate, "Scav/account spawn identity is preserved");
        Check(!recruit.CanHandleBodyContainerLootCommands, "Living account-identified teammate blocks recruit");

        var transitBot = new BotOwner("transit-profile");
        FollowerTransitStateCache.Profiles.Add("transit-profile");
        var transitMate = Add(boss, transitBot, true);
        Check(transitMate.IsSpawnedSquadMate, "Transit teammate retains spawned identity");
        accountBot.IsDead = true;
        Check(!recruit.CanHandleBodyContainerLootCommands, "Another living spawned teammate still blocks fallback");
        transitBot.IsDead = true;
        Check(recruit.CanHandleBodyContainerLootCommands, "All spawned teammates dead enables fallback");

        var recruitedWithSpawnId = Add(boss, new BotOwner("spawned"), false);
        Check(!recruitedWithSpawnId.IsSpawnedSquadMate, "Spawn IDs alone do not turn recruits into saved teammates");
        Check(recruit.CanHandleBodyContainerLootCommands, "Another recruit cannot block recruit fallback");
        boss.Followers.Add(null);
        Check(recruit.CanHandleBodyContainerLootCommands, "Null follower entry is ignored");
        boss.Followers = null;
        Check(!recruit.CanHandleBodyContainerLootCommands, "Missing follower collection fails closed");
        var orphan = new pitTeam.Components.BotFollowerPlayer(recruitBot, null, false);
        Check(!orphan.CanHandleBodyContainerLootCommands, "Missing boss fails closed");
        boss.Followers = new List<BotOwner> { recruitBot };
        BossPlayers.Instance = null;
        Check(!recruit.CanHandleBodyContainerLootCommands, "Missing follower registry fails closed");

        Console.WriteLine($"Loot follower eligibility fixture passed: {checks} assertions.");
        return 0;
    }

    private static pitTeam.Components.BotFollowerPlayer Add(
        pitTeam.Components.pitAIBossPlayer boss, BotOwner bot, bool squad)
    {
        var follower = new pitTeam.Components.BotFollowerPlayer(bot, boss, squad);
        boss.Followers.Add(bot);
        BossPlayers.Instance.Followers[bot] = follower;
        return follower;
    }
}

internal sealed class BotOwner
{
    public string ProfileId = string.Empty;
    public Profile Profile = new Profile();
    public bool IsDead;
    public bool Busy;
    public float Distance;
    public bool Active = true;
    public BotOwner(string id) { ProfileId = id; Profile.Id = id; }
}

internal sealed class Profile
{
    public string ProfileId = string.Empty;
    public string Id;
    public string AccountId;
}

namespace pitTeam.Components
{
    internal sealed class pitAIBossPlayer
    {
        public List<BotOwner> Followers = new List<BotOwner>();
    }

    internal sealed class BotFollowerPlayer
    {
        private readonly BotOwner _bot;
        private readonly pitAIBossPlayer _player;
        private readonly bool _IsSquadMate;
        public BotFollowerPlayer(BotOwner bot, pitAIBossPlayer player, bool squad)
        {
            _bot = bot;
            _player = player;
            _IsSquadMate = squad;
        }

        /* LOOT_ELIGIBILITY_MEMBERS */
    }
}

namespace pitTeam.Modules
{
    internal sealed class BossPlayers
    {
        public static BossPlayers Instance;
        public readonly Dictionary<BotOwner, pitTeam.Components.BotFollowerPlayer> Followers = new();
        public pitTeam.Components.BotFollowerPlayer GetFollower(BotOwner bot) =>
            Followers.TryGetValue(bot, out var follower) ? follower : null;
    }

    internal static class FollowerTransitStateCache
    {
        public static readonly HashSet<string> Profiles = new HashSet<string>();
        public static bool IsTransitSpawnProfile(string id) => Profiles.Contains(id);
    }
}

namespace pitTeam.Utils
{
    internal static class SpawnHelper
    {
        public static readonly HashSet<string> spawnMemberIds = new HashSet<string>();
        public static readonly HashSet<string> spawnMemberIdsScav = new HashSet<string>();
    }
}
