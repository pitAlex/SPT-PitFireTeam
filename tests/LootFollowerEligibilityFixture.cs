using System;
using System.Collections.Generic;
using System.Linq;
using pitTeam.Modules;
using EFT;
using EFT.Interactive;
using UnityEngine;

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
        CheckKnightCommands();
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

    private static void CheckKnightCommands()
    {
        BossPlayers.Instance = new BossPlayers();
        var boss = new pitTeam.Components.pitAIBossPlayer();
        BossPlayers.CurrentBoss = boss;
        var knightBot = new BotOwner("knight"); knightBot.Profile.Role = "bossKnight";
        var knight = Add(boss, knightBot, false);
        var leader = new Player { ProfileId = "leader" };
        var owner = new GamePlayerOwner { Player = leader };
        var corpse = new Corpse();
        GameplayModeAddons.Rogue = true;
        Check(new CompanionGearPolicy(true, true, 0.1f).AllowBodyContainerLootCommands, "existing three-argument addon API retains default command permission");
        Check(!knight.CanHandleBodyContainerLootCommands && !knight.AllowsBodyContainerLootCommands, "Knight cannot fall back into recruit body/container looting");
        Check(!LootPromptHarness.CanShow(owner, corpse), "Knight-only squad has no corpse CMD action rows");
        Check(FollowerLootCommandPolicy.HasRestrictedOnlyFollowers(boss.Followers), "Knight-only squad hides body/container quick prompts");
        var panel = new QuickPanel();
        LootPromptHarness.Quick(panel, leader, corpse, null, null, false, EPhraseTrigger.LootGeneric);
        Check(!panel.Flags[EPhraseTrigger.CheckHim] && !panel.Flags[EPhraseTrigger.LootBody], "Knight corpse quick commands hidden");
        LootPromptHarness.Quick(panel, leader, null, new LootableContainer(), null, true, EPhraseTrigger.LootGeneric);
        Check(!panel.Flags[EPhraseTrigger.LootContainer], "Knight container quick command hidden");
        foreach(var phrase in new[] { EPhraseTrigger.LootGeneric, EPhraseTrigger.LootKey, EPhraseTrigger.LootMoney, EPhraseTrigger.LootWeapon })
        {
            LootPromptHarness.Quick(panel, leader, null, null, new LootItem(), false, phrase);
            Check(panel.Flags[phrase], "Knight loose-loot quick command survives: " + phrase);
        }
        Check(FollowerLootCommandPolicy.TryRefuseBodyContainer(leader, leader, boss.Followers) && knightBot.BotTalk.Negatives == 1, "explicit body/container attempt receives one Negative");
        Check(!FollowerLootCommandPolicy.TryRefuseBodyContainer(new Player { ProfileId = "other" }, leader, boss.Followers) && knightBot.BotTalk.Negatives == 1, "another player's order cannot make Knight answer");
        var recruit = Add(boss, new BotOwner("recruit"), false);
        Check(recruit.CanHandleBodyContainerLootCommands && LootPromptHarness.CanShow(owner, corpse), "eligible recruit keeps corpse commands in a mixed Knight squad");
        LootPromptHarness.Quick(panel, leader, corpse, null, null, false, EPhraseTrigger.LootGeneric);
        Check(panel.Flags[EPhraseTrigger.CheckHim] && panel.Flags[EPhraseTrigger.LootBody], "mixed squad keeps body quick prompts for an eligible follower");
        LootPromptHarness.Quick(panel, leader, null, new LootableContainer(), null, true, EPhraseTrigger.LootGeneric);
        Check(panel.Flags[EPhraseTrigger.LootContainer], "mixed squad keeps container quick prompt for an eligible follower");
        Check(!FollowerLootCommandPolicy.TryRefuseBodyContainer(leader, leader, boss.Followers) && knightBot.BotTalk.Negatives == 1, "Knight does not refuse an order another follower can perform");
        boss.Followers.Remove(recruit.GetBot());
        knightBot.IsDead = true;
        Check(!FollowerLootCommandPolicy.TryRefuseBodyContainer(leader, leader, boss.Followers), "dead Knight never answers");
        knightBot.IsDead = false; GameplayModeAddons.Rogue = false;
        Check(knight.CanHandleBodyContainerLootCommands, "Knight's addon restriction does not leak to other modes");
        Check(!LootPromptHarness.CanShow(owner, new LootableContainer()), "container native actions never receive corpse-only CMD entries");
        Check(!LootPromptHarness.CanShow(owner, new LootItem()), "loose loot retains its existing quick-command presentation");
        BossPlayers.CurrentBoss = null;
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
    public EBotState BotState => Active ? EBotState.Active : EBotState.NonActive;
    public Vector3 Position => new Vector3(Distance);
    public Talk BotTalk = new Talk();
    public BotOwner(string id) { ProfileId = id; Profile.Id = id; }
}

internal sealed class Profile
{
    public string ProfileId = string.Empty;
    public string Id;
    public string AccountId;
    public string Role = "pmcUSEC";
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
        private string _botRole => _bot.Profile.Role;
        public BotOwner GetBot() => _bot;
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
        public static pitTeam.Components.pitAIBossPlayer CurrentBoss;
        public static pitTeam.Components.pitAIBossPlayer GetBoss(string id) => id == "leader" ? CurrentBoss : null;
        public static BossPlayers Instance;
        public readonly Dictionary<BotOwner, pitTeam.Components.BotFollowerPlayer> Followers = new();
        public pitTeam.Components.BotFollowerPlayer GetFollower(BotOwner bot) =>
            Followers.TryGetValue(bot, out var follower) ? follower : null;
    }

    internal static class GameplayModeAddons
    {
        public static bool Rogue;
        public static CompanionGearPolicy GetCompanionGearPolicy(string role) => Rogue ? pitTeam.GoingRogue.GoonGearPolicy.ForRole(role) : null;
    }

    internal static class FollowerTransitStateCache
    {
        public static readonly HashSet<string> Profiles = new HashSet<string>();
        public static bool IsTransitSpawnProfile(string id) => Profiles.Contains(id);
    }
}

public enum EBotState { Active, NonActive }
public class Talk { public int Negatives; public void TrySay(EPhraseTrigger phrase, bool delay) { if(phrase != EPhraseTrigger.Negative) throw new Exception("unexpected positive acknowledgement"); Negatives++; } }
namespace UnityEngine
{
    public struct Vector3 { public float x; public Vector3(float value) { x=value; } public float sqrMagnitude=>x*x; public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x); }
}
namespace EFT
{
    public interface IPlayer { string ProfileId { get; } Vector3 Position { get; } }
    public class Player : IPlayer { public string ProfileId { get; set; } public Vector3 Position => default; public bool IsYourPlayer=true; public Health HealthController=new Health(); }
    public class Health { public bool IsAlive=true; }
    public class GamePlayerOwner { public Player Player; }
    public enum EPhraseTrigger { Negative, LootKey, LootMoney, LootWeapon, LootGeneric, LootBody, CheckHim, LootContainer }
}
namespace EFT.Interactive { public interface IInteractive {} public class Corpse:IInteractive {} public class LootableContainer:IInteractive {} public class LootItem:IInteractive {} }
class QuickPanel { public Dictionary<EPhraseTrigger,bool> Flags=new(); public void SetPhraseActive(EPhraseTrigger p,bool enabled){Flags[p]=enabled;} }
static class LootPromptHarness
{
    /* CMD_VISIBILITY */
    internal static void Quick(QuickPanel __instance, Player player, Corpse corpse, LootableContainer lootContainer, LootItem lootItem, bool canLootContainer, EPhraseTrigger lootPhrase)
    {
        bool flag=lootItem!=null && lootPhrase==EPhraseTrigger.LootKey;
        bool flag2=lootItem!=null && lootPhrase==EPhraseTrigger.LootMoney;
        bool flag3=lootItem!=null && lootPhrase==EPhraseTrigger.LootWeapon;
        /* QUICK_VISIBILITY */
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
