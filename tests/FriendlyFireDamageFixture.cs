using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using pitTeam.Modules;
using pitTeam.Patches;

internal static class FriendlyFireDamageChecks
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
    private static Player Human(string id) => new Player { ProfileId = id };
    private static Player Recruit(string id, Player leader)
    {
        var player = new Player { ProfileId = id, IsAI = true };
        var boss = new Boss { realPlayer = leader };
        var bot = new BotOwner { GetPlayer = player, BotFollower = new BotFollower { BossToFollow = boss } };
        player.AIData = new AIData { BotOwner = bot };
        BossPlayers.Followers[id] = new Follower { Bot = bot, Boss = boss };
        return player;
    }
    private static DamageInfo Hit(Player attacker, float damage = 80f, EDamageType type = EDamageType.Bullet) =>
        new DamageInfo { Player = attacker == null ? null : new Bridge { iPlayer = attacker }, Damage = damage,
            DamageType = type, ArmorDamage = 25f, PenetrationPower = 40f, HeavyBleedingDelta = 0.2f };
    private static float Apply(Player victim, DamageInfo hit)
    {
        victim.ApplyDamageInfo(hit, EBodyPart.Chest, EBodyPartColliderType.Ribcage, 0f);
        return victim.Last.Damage;
    }
    public static void Main()
    {
        new FollowerFriendlyFireDamagePatch().Enable();
        var player = Human("player");
        var otherPlayer = Human("otherPlayer");
        var teammate = Recruit("saved", player);
        var recruit = Recruit("recruit", player);
        var outsider = Recruit("outside", otherPlayer);
        var hit = Hit(teammate);
        Check(Apply(player, hit) == 40f, "follower to own player halves health input");
        Check(hit.Damage == 80f, "Harmony ref mutates only the victim call's value-copy");
        Check(Apply(teammate, Hit(player)) == 40f, "player to own saved teammate halves health input");
        Check(Apply(recruit, Hit(player)) == 40f && Apply(player, Hit(recruit)) == 40f, "recruits covered in both directions");
        Check(player.Last.ArmorDamage == 25f && player.Last.PenetrationPower == 40f && player.Last.HeavyBleedingDelta == 0.2f,
            "penetration, armor and injury metadata unchanged");
        Check(ReferenceEquals(player.Last.Player.iPlayer, recruit), "original aggressor retained");
        Check(Apply(teammate, Hit(player, 18f)) == 9f, "post-armor residual is halved");
        hit = Hit(player, 100f, EDamageType.Explosion);
        Check(Apply(teammate, hit) == 50f && Apply(recruit, hit) == 50f && Apply(outsider, hit) == 100f,
            "shared explosion packet independently scales only own followers");
        Check(Apply(player, Hit(teammate, 60f, EDamageType.GrenadeFragment)) == 30f, "attributed grenade fragment covered");
        Check(Apply(recruit, Hit(player, 32f, EDamageType.Melee)) == 16f, "attributed melee covered");
        Check(Apply(recruit, Hit(teammate)) == 80f, "follower-to-follower outside requested reduction");
        Check(Apply(outsider, Hit(player)) == 80f && Apply(player, Hit(outsider)) == 80f, "other player's follower excluded both ways");
        Check(Apply(otherPlayer, Hit(player)) == 80f, "human-to-human excluded");
        Check(Apply(player, Hit(player)) == 80f, "self damage excluded");
        Check(Apply(teammate, Hit(null)) == 80f, "environmental or unattributed damage excluded");
        Check(Apply(player, new DamageInfo { Player = new Bridge(), Damage = 80f }) == 80f, "missing attacker player tolerated");
        BossPlayers.Followers.Remove(recruit.ProfileId);
        Check(Apply(player, Hit(recruit)) == 80f && Apply(recruit, Hit(player)) == 80f, "dismissed follower excluded both ways");
        var staleBoss = teammate.AIData.BotOwner.BotFollower.BossToFollow;
        teammate.AIData.BotOwner.BotFollower.BossToFollow = null;
        Check(Apply(teammate, Hit(player)) == 80f, "stale registry without live boss ownership excluded");
        teammate.AIData.BotOwner.BotFollower.BossToFollow = staleBoss;
        var replacement = new Player { ProfileId = teammate.ProfileId, IsAI = true, AIData = teammate.AIData };
        Check(Apply(player, Hit(replacement)) == 80f, "reused identity cannot impersonate registered bot player");
        var oldBot = teammate.AIData.BotOwner;
        teammate.AIData.BotOwner = new BotOwner { GetPlayer = teammate, BotFollower = oldBot.BotFollower };
        Check(Apply(player, Hit(teammate)) == 80f, "replaced bot cannot use stale follower registration");
        teammate.AIData.BotOwner = oldBot;
        var missingData = teammate.AIData;
        teammate.AIData = null;
        Check(Apply(player, Hit(teammate)) == 80f, "missing bot data fails open");
        teammate.AIData = missingData;
        Check(Apply(player, Hit(teammate, 0f)) == 0f && Apply(player, Hit(teammate, -2f)) == -2f, "zero and negative unchanged");
        Check(float.IsNaN(Apply(player, Hit(teammate, float.NaN))) &&
            float.IsPositiveInfinity(Apply(player, Hit(teammate, float.PositiveInfinity))), "nonfinite inputs untouched");
        player.HealthController.IsAlive = false;
        Check(Apply(player, Hit(teammate)) == 80f, "dead victim does not enter policy");
        player.HealthController.IsAlive = true;
        BossPlayers.Throw = true;
        Check(Apply(player, Hit(teammate)) == 80f, "lookup exception preserves native damage");
        BossPlayers.Throw = false;
        int calls = player.Calls;
        Apply(player, Hit(teammate));
        Check(player.Calls == calls + 1, "original damage handler executes once");
        Console.WriteLine(checks + " friendly-fire damage checks passed.");
    }
}

public enum EBodyPart { Chest }
public enum EBodyPartColliderType { Ribcage }
public enum EDamageType { Bullet, Explosion, GrenadeFragment, Melee }
namespace EFT
{
    public interface IPlayer { bool IsAI { get; } string ProfileId { get; } AIData AIData { get; } }
    public class Health { public bool IsAlive = true; }
    public class AIData { public BotOwner BotOwner; }
    public class Boss { public Player realPlayer; }
    public class BotFollower { public Boss BossToFollow; }
    public class BotOwner { public Player GetPlayer; public BotFollower BotFollower; }
    public class Bridge { public IPlayer iPlayer; }
    public class Player : IPlayer
    {
        public bool IsAI { get; set; }
        public string ProfileId { get; set; }
        public AIData AIData { get; set; }
        public Health HealthController = new Health();
        public DamageInfo Last;
        public int Calls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual void ApplyDamageInfo(DamageInfo damageInfo, EBodyPart bodyPartType, EBodyPartColliderType colliderType, float absorbed)
        { Last = damageInfo; Calls++; }
    }
}
namespace EFT.Ballistics
{
    public struct DamageInfo
    {
        public Bridge Player;
        public float Damage, ArmorDamage, PenetrationPower, HeavyBleedingDelta;
        public EDamageType DamageType;
    }
}
namespace pitTeam.Modules
{
    public class Follower { public BotOwner Bot; public Boss Boss; public BotOwner GetBot() => Bot; public Boss GetBoss() => Boss; }
    public static class BossPlayers
    {
        public static Dictionary<string, Follower> Followers = new Dictionary<string, Follower>();
        public static bool Throw;
        public static Follower GetFollowerByProfileId(string id)
        { if (Throw) throw new Exception(); return id != null && Followers.TryGetValue(id, out var f) ? f : null; }
    }
}
namespace SPT.Reflection.Patching
{
    [AttributeUsage(AttributeTargets.Method)] public class PatchPrefixAttribute : Attribute { }
    public abstract class ModulePatch
    {
        protected abstract MethodBase GetTargetMethod();
        public void Enable() => new Harmony("pitTeam.friendlyFire.fixture").Patch(GetTargetMethod(),
            prefix: new HarmonyMethod(AccessTools.Method(GetType(), "PatchPrefix")));
    }
}
