using System;
using EFT;
using pitTeam.Modules;
using pitTeam.Patches;

public enum BotStandByType { active, paused, goToSave, none }
public class BotStandBy
{
    public BotStandByType StandByType = BotStandByType.active;
    public bool CanDoStandBy = true, ThrowOnActivation;
    public int Activations;
    public void Activate()
    {
        if (ThrowOnActivation) throw new InvalidOperationException("activation failed");
        Activations++;
        StandByType = BotStandByType.active;
    }
}
namespace EFT
{
    public enum EBotState { Active, Inactive }
    public class Health { public bool IsAlive = true; }
    public class AIData { public BotOwner BotOwner; }
    public class Player { public Health HealthController = new Health(); public AIData AIData = new AIData(); }
    public class EnemyInfo { public Player Person; public BotOwner Owner; }
    public class BotMemory { public BotOwner Owner; public EnemyInfo GoalEnemy; }
    public class Group
    {
        public bool Enemy = true, PlayerEnemy;
        public bool IsEnemy(Player p) => Enemy;
        public bool IsPlayerEnemy(Player p) => PlayerEnemy;
    }
    public class BotOwner
    {
        public bool IsDead, Follower;
        public EBotState BotState = EBotState.Active;
        public BotMemory Memory = new BotMemory();
        public Group BotsGroup = new Group();
        public BotStandBy StandBy = new BotStandBy();
    }
}
namespace pitTeam
{
    public static class pitFireTeam { public static bool IsSAINInstalled = true; }
}
namespace pitTeam.Modules
{
    public static class BossPlayers { public static bool IsFollower(BotOwner b) => b.Follower; }
    public static class Logger { public static void LogError(Exception e) { throw e; } }
}
namespace pitTeam.Patches
{
    public class PatchPostfixAttribute : Attribute { }
    internal static class BotMemoryOwnerAccessor { public static BotOwner Get(BotMemory m) => m.Owner; }
    internal static class HostilePeacefulLayerInterrupt
    {
__WAKE__
    }
    internal class FollowerGoalEnemyClearRetentionPatch
    {
        internal static void Apply(BotMemory memory, EnemyInfo value, bool original = true) =>
            PatchPostfix(memory, value, original);
__POSTFIX__
    }
}
public static class AcceptedContactChecks
{
    private static int checks;
    private static void Check(bool result, string message)
    {
        checks++;
        if (!result) throw new Exception(message);
    }
    private static BotOwner Create(out BotOwner target)
    {
        target = new BotOwner();
        target.StandBy.StandByType = BotStandByType.paused;
        var owner = new BotOwner { Follower = true };
        owner.Memory.Owner = owner;
        owner.Memory.GoalEnemy = new EnemyInfo { Person = new Player(), Owner = owner };
        owner.Memory.GoalEnemy.Person.AIData.BotOwner = target;
        return owner;
    }
    private static void Apply(BotOwner owner, bool original = true) =>
        FollowerGoalEnemyClearRetentionPatch.Apply(owner.Memory, owner.Memory.GoalEnemy, original);
    public static int Run()
    {
        var owner = Create(out var target);
        var goal = owner.Memory.GoalEnemy;
        Apply(owner);
        Check(target.StandBy.StandByType == BotStandByType.active, "accepted hostile leaves paused standby");
        Check(ReferenceEquals(owner.Memory.GoalEnemy, goal), "goal identity is preserved");
        Check(target.StandBy.CanDoStandBy, "standby settings remain native");
        for (int i = 0; i < 100; i++) Apply(owner);
        Check(target.StandBy.Activations == 1, "repeated admitted goals do not repeat activation");
        owner = Create(out target); target.StandBy.StandByType = BotStandByType.goToSave; Apply(owner);
        Check(target.StandBy.StandByType == BotStandByType.active, "accepted hostile leaves goToSave");
        owner = Create(out target); Apply(owner, false);
        Check(target.StandBy.Activations == 0, "rejected setter cannot wake its unchanged current goal");
        owner = Create(out target);
        FollowerGoalEnemyClearRetentionPatch.Apply(owner.Memory, new EnemyInfo { Person = owner.Memory.GoalEnemy.Person });
        Check(target.StandBy.Activations == 0, "unsuccessful native setter cannot wake target");
        owner = Create(out target); owner.Memory.GoalEnemy.Owner = new BotOwner { Follower = true };
        owner.Memory.GoalEnemy.Owner.Memory.GoalEnemy = owner.Memory.GoalEnemy; Apply(owner);
        Check(target.StandBy.Activations == 0, "foreign EnemyInfo owner cannot activate through another bot's setter");
        owner = Create(out target); FollowerGoalEnemyClearRetentionPatch.Apply(owner.Memory, null);
        Check(target.StandBy.Activations == 0, "goal clear and Attention cannot wake target");
        owner = Create(out target); goal = owner.Memory.GoalEnemy; owner.Memory.GoalEnemy = null;
        Check(!FollowerAcceptedContactActivation.TryActivate(owner, goal) && target.StandBy.Activations == 0,
            "heard or shared knowledge without an accepted goal cannot wake target");
        owner = Create(out target); owner.BotsGroup.Enemy = false; Apply(owner);
        Check(target.StandBy.Activations == 0, "neutral target remains asleep");
        owner.BotsGroup.PlayerEnemy = true; Apply(owner);
        Check(target.StandBy.Activations == 1, "explicit player-enemy relationship qualifies");
        owner = Create(out target); owner.Memory.GoalEnemy.Person.HealthController.IsAlive = false; Apply(owner);
        Check(target.StandBy.Activations == 0, "dead contact is not activated");
        owner = Create(out target); target.BotState = EBotState.Inactive; Apply(owner);
        Check(target.StandBy.Activations == 0, "inactive target lifecycle is not forced active");
        owner = Create(out target); target.IsDead = true; Apply(owner);
        Check(target.StandBy.Activations == 0, "dead bot lifecycle is untouched");
        owner = Create(out target); target.Follower = true; Apply(owner);
        Check(target.StandBy.Activations == 0, "squadmate is not activated as an enemy");
        owner = Create(out target); owner.Follower = false; Apply(owner);
        Check(target.StandBy.Activations == 0, "ordinary bots are unaffected");
        owner = Create(out target); pitTeam.pitFireTeam.IsSAINInstalled = false; Apply(owner);
        Check(target.StandBy.Activations == 0, "SAIN absence is a no-op");
        pitTeam.pitFireTeam.IsSAINInstalled = true;
        owner = Create(out target); target.StandBy.StandByType = BotStandByType.none; Apply(owner);
        Check(target.StandBy.Activations == 0, "uninitialized standby stays native");
        owner = Create(out target); target.StandBy = null; Apply(owner);
        Check(target.StandBy == null, "missing standby is safe");
        owner = Create(out target); target.StandBy.ThrowOnActivation = true; Apply(owner);
        Check(target.StandBy.StandByType == BotStandByType.paused, "failed activation preserves native state");
        owner = Create(out target); owner.BotState = EBotState.Inactive; Apply(owner);
        Check(target.StandBy.Activations == 0, "inactive follower cannot activate a target");
        owner = Create(out target); owner.BotsGroup = null; Apply(owner);
        Check(target.StandBy.Activations == 0, "missing group is safe");
        owner = Create(out target); owner.Memory.GoalEnemy.Person.AIData.BotOwner = null; Apply(owner);
        Check(owner.Memory.GoalEnemy.Person.HealthController.IsAlive, "human target is untouched");
        return checks;
    }
}
