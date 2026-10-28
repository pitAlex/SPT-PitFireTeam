using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.GoingRogue.Combat;
using UnityEngine;
using static Program;

internal static class LeadershipChecks
{
    private sealed class Squad
    {
        internal pitAIBossPlayer Human = new();
        internal BotOwner Knight, Pipe;
        internal EnemyInfo Enemy;
        internal int Assessments;
        internal bool Answer;
        internal Squad(bool knight = true)
        {
            GoonPushLeadership.Reset(); BossPlayers.Followers.Clear(); KnightCombatLayer.Owned.Clear(); Time.time = 100;
            BotOwner Add(WildSpawnType role)
            {
                var bot = new BotOwner(); bot.Profile.Info.Settings.Role = role; bot.GetPlayer.AIData.BotOwner = bot;
                BossPlayers.Followers.Add(new BotFollowerPlayer { Bot = bot, Leader = Human }); return bot;
            }
            Pipe = Add(WildSpawnType.followerBigPipe);
            if (knight) Knight = Add(WildSpawnType.bossKnight);
            var enemy = new BotOwner(); enemy.GetPlayer.AIData.BotOwner = enemy;
            Enemy = new EnemyInfo { ProfileId = enemy.ProfileId, Person = enemy.GetPlayer };
            Pipe.Memory.GoalEnemy = Enemy;
            if (Knight != null) Knight.Memory.GoalEnemy = Enemy;
        }
        internal bool? Order(BotOwner actor, int sequence = 1) => GoonPushLeadership.Evaluate(new(
            actor, Human.realPlayer, Enemy, sequence, (owner, target) =>
            { Check(owner == Knight && target.ProfileId == Enemy.ProfileId, "shared verdict uses Knight's own admitted target and assessment"); Assessments++; return Answer; }));
        internal BotFollowerPlayer Follower => BossPlayers.Instance.GetFollower(Pipe);
        internal void PublishPush() { Follower.PushEnemyIssueSequence++; Follower.HasOrderedPushTargetLock = true; }
        internal KnightSquadCoordinator Coordinate()
        {
            Knight.Boss.BossLogic = new BossKnight { _owner = Knight, Publish = true };
            KnightCombatLayer.Owned.Add(Knight);
            var result = new KnightSquadCoordinator(Knight); result.Refresh(); return result;
        }
    }
    internal static void Run()
    {
        foreach (bool answer in new[] { false, true })
        foreach (bool pipeFirst in new[] { false, true })
        {
            var s = new Squad { Answer = answer };
            var first = pipeFirst ? s.Pipe : s.Knight; var second = pipeFirst ? s.Knight : s.Pipe;
            Check(s.Order(first) == answer && s.Order(second) == answer && s.Assessments == 1,
                "same command/target has one shared Knight verdict regardless of follower iteration order");
            Check(s.Order(s.Pipe) == answer && s.Assessments == 1, "repeated evaluation cannot reroll the same command");
            s.Answer = !answer;
            Check(s.Order(s.Pipe, 2) == !answer && s.Assessments == 2, "a new command may make a new assessment");
        }
        var squad = new Squad(false);
        Check(squad.Order(squad.Pipe) == true && squad.Assessments == 0, "Pipe accepts without Knight");
        foreach (string absent in new[] { "dead", "inactive", "foreign" })
        {
            squad = new Squad();
            if (absent == "dead") squad.Knight.IsDead = true;
            if (absent == "inactive") squad.Knight.BotState = EBotState.Inactive;
            if (absent == "foreign") BossPlayers.Instance.GetFollower(squad.Knight).Leader = new pitAIBossPlayer();
            Check(squad.Order(squad.Pipe) == true && squad.Assessments == 0, "Knight cannot govern another local push when " + absent);
        }
        foreach (float knightDistance in new[] { 49f, 50f, 50.01f, 80f })
        foreach (float pipeDistance in new[] { 29f, 30f, 30.01f })
        {
            squad = new Squad(); squad.Knight.Position = new(knightDistance, 0, 0); squad.Pipe.Position = new(pipeDistance, 0, 0);
            bool local = knightDistance > 50 && pipeDistance <= 30;
            Check(squad.Order(squad.Pipe) == local, "distance delegation honors exact 30m/50m boundaries");
            Check(squad.Assessments == (local ? 0 : 1), "delegated orders do not consume Knight's confidence roll");
        }
        foreach (string busy in new[] { "visible", "recent", "underFire", "stale" })
        {
            squad = new Squad(); squad.Knight.Memory.GoalEnemy = new EnemyInfo { ProfileId = "different", Person = new Player() };
            squad.Knight.EnemiesController.EnemyInfos[1] = squad.Enemy;
            if (busy == "visible") squad.Knight.Memory.GoalEnemy.IsVisible = true;
            if (busy == "recent") squad.Knight.Memory.GoalEnemy.PersonalLastSeenTime = 99;
            if (busy == "underFire") squad.Knight.Memory.IsUnderFire = true;
            Check(squad.Order(squad.Pipe) == (busy != "stale"), "only active different-target engagement delegates: " + busy);
        }
        squad = new Squad(); squad.Knight.Memory.GoalEnemy = null;
        Check(squad.Order(squad.Pipe) == true && squad.Knight.EnemiesController.EnemyInfos.Count == 0,
            "Knight cannot veto a nearby player's target using knowledge he does not have");
        squad = new Squad(); squad.Pipe.Profile.Info.Settings.Role = WildSpawnType.pmcUSEC;
        Check(squad.Order(squad.Pipe) == null, "ordinary teammates retain Core command policy");

        squad = new Squad { Answer = true };
        using (var coordinator = squad.Coordinate())
        {
            Check(squad.Order(squad.Pipe) == true, "player push admitted before testing handoff"); squad.PublishPush();
            Check(!KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _), "pre-existing Knight assignment does not cancel a new player order");
            squad.Knight.Position = new(80, 0, 0); squad.Knight.Boss.BossLogic.UpdatePositions(true);
            Check(!KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _), "routine recalculation and distance changes cannot churn accepted push ownership");
            coordinator.RequestMemberPositionRefresh(squad.Pipe.Id);
            Check(!KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _), "a requested recalculation is not yet an actionable position");
            squad.Knight.Boss.BossLogic.UpdatePositions(true);
            Check(KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _), "published fresh member directive can supersede the player push");
            squad.Knight.IsDead = true;
            Check(!KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _), "dead Knight cannot publish a stale takeover");
            squad.Knight.IsDead = false;
            squad.Knight.BotState = EBotState.Inactive;
            Check(!KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _), "inactive Knight cannot publish a stale takeover");
            squad.Knight.BotState = EBotState.Active;
            squad.Human.Logic.IsHitted = true;
            Check(!KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _), "player Need Help/under-attack protection beats Knight reassignment"); squad.Human.Logic.IsHitted = false;
            squad.Follower.Command = FollowerCommandType.Regroup;
            Check(!KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _), "player Regroup is never superseded"); squad.Follower.Command = FollowerCommandType.None;
            squad.Follower.CombatRegroupUsesBossAnchor = true;
            Check(!KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _) &&
                !KnightSquadCoordinator.TryGetMemberPosition(squad.Pipe, out _), "consumed Regroup still protects the player-directed objective");
            squad.Follower.CombatRegroupUsesBossAnchor = false;
            squad.Follower.HasOrderedPushCancelRequest = true;
            Check(!KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _), "pending player push cancellation keeps priority"); squad.Follower.HasOrderedPushCancelRequest = false;
            squad.Follower.PushEnemyIssueSequence++;
            Check(!KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _), "old delegation cannot cancel a newer push"); squad.Follower.PushEnemyIssueSequence--;
            GoonPushLeadership.Complete(squad.Pipe);
            Check(!KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _), "completed ownership cannot resurrect a player order");
        }
        squad = new Squad { Answer = true };
        using (var coordinator = squad.Coordinate())
        {
            coordinator.RequestMemberPositionRefresh(squad.Pipe.Id);
            squad.Order(squad.Pipe); squad.PublishPush();
            squad.Knight.Boss.BossLogic.UpdatePositions(true);
            Check(!KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _),
                "delayed publication of a directive issued before the player order cannot supersede it");
            var other = new EnemyInfo { ProfileId = "new-directive-target", Person = new Player() };
            squad.Knight.Memory.GoalEnemy = other; Time.time = 101; squad.Knight.Boss.BossLogic.UpdatePositions(true);
            Check(!KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out _, out _), "Knight cannot command Pipe onto an unadmitted target");
            squad.Pipe.EnemiesController.EnemyInfos[1] = other;
            Check(KnightSquadCoordinator.TryGetPushOverride(squad.Pipe, out var target, out _) && target == other,
                "a new Knight engagement can issue a directive against Pipe's already-known target");
        }
    }
}
