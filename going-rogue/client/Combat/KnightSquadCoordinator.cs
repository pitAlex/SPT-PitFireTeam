using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat;

// Native boss coordination gets a view of the human's managed Goons. It does not
// reparent their BotFollower links or recruit unrelated bots from the raid.
internal sealed class KnightSquadCoordinator : IDisposable
{
    private static readonly Dictionary<BotOwner, KnightSquadCoordinator> Owners = new();
    private readonly BotOwner owner;
    private readonly BossKnight boss;
    private readonly Dictionary<BotOwner, Vector3> previousPositions;
    private readonly Dictionary<BotOwner, FollowerCombatCommon> contacts = new();
    private float nextRefresh;
    private bool refreshPositions;
    private readonly Dictionary<BotOwner, (string Enemy, Vector3 Point, int Directive, float PublishedAt)> orders = new();
    private readonly Dictionary<BotOwner, int> pendingDirectives = new();
    private int nextDirective;
    internal readonly List<BotOwner> Members = new();

    internal KnightSquadCoordinator(BotOwner knight)
    {
        owner = knight;
        boss = (BossKnight)knight.Boss.BossLogic;
        previousPositions = new Dictionary<BotOwner, Vector3>(boss.FollowersPositions);
        boss.FollowersPositions.Clear();
        Owners[knight] = this;
        boss.OnPositionsRecalculated += OnPositionsRecalculated;
    }

    internal static bool TryGet(BotOwner knight, out KnightSquadCoordinator coordinator)
    {
        coordinator = null;
        return knight != null && KnightCombatLayer.IsOwned(knight) && Owners.TryGetValue(knight, out coordinator);
    }

    internal static bool TryGetMemberPosition(BotOwner member, out Vector3 position)
    {
        position = default;
        foreach (var pair in Owners)
            if (KnightCombatLayer.IsOwned(pair.Key) && pair.Value.IsMember(member) &&
                BossPlayers.Instance.GetFollower(member).CombatRegroupUsesBossAnchor != true &&
                BossPlayers.Instance.GetFollower(member).GetBoss()?.GetBossLogic()?.IsHitted != true &&
                !(BossPlayers.Instance.GetFollower(member).TryPeekActiveCommand(out var command, out _, out _) && command != FollowerCommandType.None) &&
                pair.Value.orders.TryGetValue(member, out var order) &&
                order.Enemy == pair.Key.Memory.GoalEnemy?.ProfileId && order.Enemy == member.Memory.GoalEnemy?.ProfileId &&
                pair.Key.Memory.GoalEnemy?.Person?.HealthController?.IsAlive == true &&
                FollowerCombatCommon.IsFinite(order.Point))
            { position = order.Point; return true; }
        return false;
    }

    internal static bool HasLocalKnight() => BossPlayers.GetFollowers().Any(follower =>
        follower.GetBoss()?.realPlayer?.IsYourPlayer == true &&
        follower.GetBot() is BotOwner bot && bot.BotState == EBotState.Active && !bot.IsDead &&
        bot.GetPlayer?.HealthController?.IsAlive == true &&
        bot.Profile?.Info?.Settings?.Role == WildSpawnType.bossKnight);

    internal static int DirectiveRevision(BotOwner member)
    {
        foreach (var coordinator in Owners.Values)
            if (coordinator.IsMember(member))
                return coordinator.pendingDirectives.TryGetValue(member, out var pending) ? pending :
                    coordinator.orders.TryGetValue(member, out var order) ? order.Directive : 0;
        return 0;
    }

    internal static bool TryGetPushOverride(BotOwner member, out EnemyInfo target, out Vector3 point)
    {
        target = null; point = default;
        foreach (var pair in Owners)
        {
            var coordinator = pair.Value;
            if (!KnightCombatLayer.IsOwned(pair.Key) || !coordinator.IsMember(member) ||
                !coordinator.orders.TryGetValue(member, out var order) || order.Enemy != pair.Key.Memory.GoalEnemy?.ProfileId ||
                !FollowerCombatCommon.IsFinite(order.Point) ||
                !GoonPushLeadership.CanSupersede(member, order.Enemy, order.Directive, order.PublishedAt)) continue;
            target = GoonPushLeadership.KnownTarget(member, order.Enemy);
            if (target == null) continue;
            point = order.Point;
            return true;
        }
        return false;
    }

    private bool IsMember(BotOwner member) => !owner.IsDead && owner.BotState == EBotState.Active &&
        owner.GetPlayer?.HealthController?.IsAlive == true && member != null && !member.IsDead &&
        member.BotState == EBotState.Active && member.GetPlayer?.HealthController?.IsAlive == true &&
        Members.Contains(member) && BossPlayers.Instance?.GetFollower(owner)?.GetBoss() is { } leader &&
        ReferenceEquals(BossPlayers.Instance.GetFollower(member)?.GetBoss(), leader);

    internal bool RequestMemberPositionRefresh(int memberId)
    {
        var member = Members.FirstOrDefault(bot => bot.Id == memberId && IsMember(bot));
        if (member == null) return false;
        pendingDirectives[member] = ++nextDirective;
        orders.Remove(member);
        refreshPositions = true;
        return true;
    }

    internal void Refresh()
    {
        if (Time.time < nextRefresh) return;
        nextRefresh = Time.time + 1f;
        var human = BossPlayers.Instance?.GetFollower(owner)?.GetBoss();
        var members = BossPlayers.GetFollowers().Where(f => ReferenceEquals(f.GetBoss(), human))
            .Select(f => f.GetBot()).Where(bot => bot != null && !bot.IsDead && bot.BotState == EBotState.Active &&
                bot.GetPlayer?.HealthController?.IsAlive == true &&
                (bot.Profile.Info.Settings.Role == WildSpawnType.followerBigPipe ||
                 bot.Profile.Info.Settings.Role == WildSpawnType.followerBirdEye))
            .OrderBy(bot => bot.Profile.Info.Settings.Role).ToList();
        foreach (var removed in Members.Where(bot => !members.Contains(bot)).ToArray())
        {
            boss.FollowersPositions.Remove(removed);
            contacts.Remove(removed);
            orders.Remove(removed);
            pendingDirectives.Remove(removed);
            GoonPushLeadership.Complete(removed);
            boss._shooterPosGood.Remove(removed.Id);
        }
        Members.Clear();
        Members.AddRange(members);
        foreach (var member in Members)
        {
            if (!contacts.ContainsKey(member)) contacts[member] = new FollowerCombatCommon(member);
            if (!boss.FollowersPositions.ContainsKey(member))
            { boss.FollowersPositions[member] = member.Position; refreshPositions = true; }
            boss.SetFightPosition(member.Memory.IsInCover && member.Memory.GoalEnemy?.CanShoot == true, member);
        }
        ShareAdmittedTarget();
        if (refreshPositions)
        {
            refreshPositions = false;
            boss.UpdatePositions(true);
        }
    }

    private void OnPositionsRecalculated()
    {
        var previous = new Dictionary<BotOwner, (string Enemy, Vector3 Point, int Directive, float PublishedAt)>(orders);
        orders.Clear();
        var goal = owner.Memory.GoalEnemy;
        if (goal?.Person?.HealthController?.IsAlive != true) return;
        foreach (var member in Members)
            if (IsMember(member) && boss.FollowersPositions.TryGetValue(member, out var point) && FollowerCombatCommon.IsFinite(point))
                orders[member] = (goal.ProfileId, point,
                    pendingDirectives.TryGetValue(member, out var revision) ? revision :
                        previous.TryGetValue(member, out var old) ? old.Directive : 0, Time.time);
        pendingDirectives.Clear();
        ShareAdmittedTarget();
    }

    private void ShareAdmittedTarget()
    {
        var goal = owner.Memory.GoalEnemy;
        var enemy = goal?.Person?.AIData?.BotOwner;
        if (enemy == null || goal.Person.HealthController?.IsAlive != true) return;
        foreach (var member in Members)
        {
            var follower = BossPlayers.Instance?.GetFollower(member);
            if (follower == null || follower.TryPeekActiveCommand(out var command, out _, out _) && command != FollowerCommandType.None ||
                follower.GetBoss()?.GetBossLogic()?.IsHitted == true || follower.HasOrderedPushTargetLock || follower.CombatRegroupUsesBossAnchor ||
                member.Medecine.Using || member.Memory.IsUnderFire || member.Memory.GoalEnemy?.IsVisible == true ||
                (member.Memory.GoalEnemy != null && Time.time - member.Memory.GoalEnemy.PersonalLastSeenTime < 2.5f)) continue;
            if (!member.EnemiesController.EnemyInfos.Values.Any(info => info.ProfileId == enemy.ProfileId)) continue;
            // Uses the same existing-contact admission and goal arbitration as teammate support;
            // never fabricates sight, shoots from a position report, or clears a living enemy.
            contacts[member].TryUseSupportGoalEnemy(enemy, "knight.bossCommand", out _);
        }
    }

    public void Dispose()
    {
        boss.OnPositionsRecalculated -= OnPositionsRecalculated;
        foreach (var member in Members)
        {
            GoonPushLeadership.Complete(member);
            boss.FollowersPositions.Remove(member);
            boss._shooterPosGood.Remove(member.Id);
        }
        foreach (var old in previousPositions)
            if (old.Key != null && !old.Key.IsDead && ReferenceEquals(old.Key.BotFollower?.BossToFollow, owner.Boss))
                boss.FollowersPositions[old.Key] = old.Value;
        Members.Clear();
        contacts.Clear();
        orders.Clear();
        pendingDirectives.Clear();
        Owners.Remove(owner);
    }
}
