using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.GoingRogue.Combat;
using pitTeam.Modules;
using UnityEngine;

internal static class IndependentPursuitChecks
{
    internal static void Run(Action<bool, string> check)
    {
        (BotOwner bot, FollowerCombatCommon common, BotFollowerPlayer follower, GoonIndependentPursuit pursuit) Setup(WildSpawnType role)
        {
            Time.time = 100; FollowerCombatAnchor.Independent = true; GoonPushLeadership.AdvanceAllowed = true;
            FollowerCombatCommon.RecentHitProbe = false;
            GoonPushLeadership.GroupSearch = false; KnightSquadCoordinator.Assigned = false;
            var bot = new BotOwner(); bot.Profile.Info.Settings.Role = role;
            bot.Memory.GoalEnemy.Distance = 300; bot.Memory.GoalEnemy.Anchor = new(300, 0, 0);
            var common = new FollowerCombatCommon { ContactAdvance = true, LowThreat = false };
            var follower = new BotFollowerPlayer { Owner = bot }; BossPlayers.Instance.Followers[bot] = follower;
            return (bot, common, follower, new(bot, common, "independent", role == WildSpawnType.followerBirdEye ? 60 : 10));
        }
        foreach (var role in new[] { WildSpawnType.bossKnight, WildSpawnType.followerBigPipe, WildSpawnType.followerBirdEye })
        {
            var (bot, c, _, pursuit) = Setup(role);
            check(pursuit.TryGetDecision(out var decision) && decision.Action == BotLogicDecision.goToPoint,
                "independent distant admitted contact creates bounded pursuit: " + role);
            check(c.AdvanceStop == (role == WildSpawnType.followerBirdEye ? 60 : 10), "sniper retains native standoff: " + role);
            check(decision.Reason.EndsWith(".runToPoint") && c.Calls.Contains("commitContactAdvance"), "safe distant pursuit can run: " + role);
            check(!c.Calls.Contains("lowThreat") && !c.Calls.Contains("medicalProvider"), "pursuit does not rerun threat/medical providers: " + role);
            pursuit.DecisionChanged(decision);
            check(FollowerEnemyTracking.GroupSearching, "only published pursuit activates search tracking: " + role);
            pursuit.Reset(); check(!FollowerEnemyTracking.GroupSearching, "interruption releases search without exhausting memory: " + role);
            FollowerCombatAnchor.Independent = false;
            check(!pursuit.TryGetDecision(out _), "normal escort mode retains its original selector: " + role);
        }
        foreach (string gate in new[] { "heal", "using", "pressure", "danger", "underFire", "hit", "reload", "hands", "drawing", "unready", "empty",
            "throw", "readyThrow", "shot", "unknown", "blocked", "regroup", "consumedRegroup", "push", "hold" })
        {
            var (bot, c, f, pursuit) = Setup(WildSpawnType.bossKnight);
            switch (gate)
            {
                case "heal": c.HealWork = true; break;
                case "using": bot.Medecine.Using = true; break;
                case "pressure": c.RecoveryPressure = true; break;
                case "danger": c.Danger = true; break;
                case "underFire": bot.Memory.IsUnderFire = true; break;
                case "hit": FollowerCombatCommon.RecentHitProbe = true; break;
                case "reload": bot.WeaponManager.Reload.Reloading = true; break;
                case "hands": bot.WeaponManager.ShootController.Reloading = true; break;
                case "drawing": bot.WeaponManager.Selector.IsChanging = true; break;
                case "unready": bot.WeaponManager.IsWeaponReady = false; break;
                case "empty": bot.WeaponManager.HaveBullets = false; break;
                case "throw": bot.WeaponManager.Grenades.ThrowindNow = true; break;
                case "readyThrow": bot.WeaponManager.Grenades.ReadyToThrow = true; break;
                case "shot": bot.Memory.GoalEnemy.IsVisible = bot.Memory.GoalEnemy.CanShoot = true; break;
                case "unknown": bot.Memory.GoalEnemy.Known = false; break;
                case "blocked": c.ContactAdvance = false; break;
                case "regroup": f.Command = FollowerCommandType.Regroup; break;
                case "consumedRegroup": f.CombatRegroupUsesBossAnchor = true; break;
                case "push": f.HasOrderedPushTargetLock = true; break;
                case "hold": c.HoldAggression = true; break;
            }
            check(!pursuit.TryGetDecision(out _) && !c.Calls.Contains("commitContactAdvance"), "independence preserves urgent/admission gate: " + gate);
        }
        foreach (var role in new[] { WildSpawnType.followerBigPipe, WildSpawnType.followerBirdEye })
        {
            var (_, _, _, pursuit) = Setup(role); GoonPushLeadership.AdvanceAllowed = false;
            check(!pursuit.TryGetDecision(out _), "Knight still leads independent follower advances: " + role);
        }
        foreach (bool prepare in new[] { false, true })
        {
            var (_, c, _, pursuit) = Setup(WildSpawnType.bossKnight); c.TransitionAllowed = prepare;
            var hold = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, "idle");
            pursuit.DecisionChanged(hold);
            check(!pursuit.TryReleaseHold(hold, out _), "new hold retains arrival grace");
            Time.time = 102.5f;
            check(pursuit.TryReleaseHold(hold, out var end) == prepare, "idle hold requires a prepared successor: " + prepare);
            check(c.Calls.Contains("clearCover") == prepare && c.Calls.Contains("commitContactAdvance") == prepare,
                "failed transition cannot clear commitments or publish movement: " + prepare);
            if (prepare) check(end.Value && c.PreparedTransition.HasValue, "successful idle handoff has one prepared successor");
            if (!prepare)
            {
                int calls = c.Calls.Count; Time.time = 102.6f; pursuit.TryReleaseHold(hold, out _);
                check(c.Calls.Count == calls, "failed pursuit hold probe is throttled");
            }
        }
        {
            var (bot, c, _, _) = Setup(WildSpawnType.bossKnight);
            var objective = new KnightCombatObjective(bot, c); objective.StartDecision();
            var decision = objective.GetDecision(bot.Memory.GoalEnemy);
            check(decision.Reason.StartsWith("knight.onYourOwn.pursuit"), "Knight objective routes independent pursuit");
            objective.DecisionChanged(null, decision);
            check(GoonPushLeadership.PublishedAdvance, "Knight publishes the independent advance for his followers");
            var native = new KnightVanillaCombat(bot, 76); native.Begin(c);
            check(!native.TryAdaptCautiousDecision(bot.Memory.GoalEnemy, new(BotLogicDecision.runToEnemy, "native"), out _),
                "independence removes ordinary far/high-threat pursuit veto");
            c.RecoveryPressure = true;
            check(native.TryAdaptCautiousDecision(bot.Memory.GoalEnemy, new(BotLogicDecision.runToEnemy, "native"), out _),
                "independence retains pressured recovery");
            objective.Deactivate();
        }
        {
            var (bot,c,_,_)=Setup(WildSpawnType.bossKnight);
            c.Engagement=true;
            var objective=new KnightCombatObjective(bot,c);objective.StartDecision();
            check(objective.GetDecision(bot.Memory.GoalEnemy).Reason=="allyEngagement",
                "independent Knight can help an ally through the common engagement planner");
            check(!c.Calls.Contains("planContactAdvance"),"concrete ally support precedes a fresh independent assault");
            objective.Deactivate();
        }
        {
            var (bot, c, follower, _) = Setup(WildSpawnType.followerBirdEye);
            follower.Boss.Logic.IsHitted = true;
            var objective = new BirdEyeCombatObjective(bot, c); objective.StartDecision();
            check(objective.GetDecision(bot.Memory.GoalEnemy).Reason.StartsWith("sniper.birdEye.onYourOwn.pursuit"),
                "independent Birdeye pursues through his sniper callback without automatic player-protection diversion");
        }
        foreach (bool prepare in new[] { false, true })
        {
            var (bot, c, _, _) = Setup(WildSpawnType.bossKnight);
            c.LongGunPreparation = true; c.TransitionAllowed = prepare; c.Settled = true;
            var objective = new KnightCombatObjective(bot, c); objective.StartDecision();
            var end = objective.ShallEndCurrentDecision(new(BotLogicDecision.holdPosition, FollowerCombatCommon.RecoveryNoCoverThreatHoldReason));
            check(end.Value == prepare && !c.Calls.Contains("planContactAdvance"),
                "weapon recovery owns the transition before pursuit, even when preparation cannot publish: " + prepare);
        }
        {
            var (bot, c, _, _) = Setup(WildSpawnType.followerBigPipe);
            bot.Memory.GoalEnemy.TimeLastSeenReal = Time.time;
            bot.Memory.GoalEnemy.GroupInfo.EnemyLastSeenTimeReal = Time.time;
            var native = new BigPipeVanillaCombat(bot); native.Begin(c);
            check(native.GetDecision().Reason.StartsWith("bigPipe.pressureSearch"),
                "independent Pipe native fallback removes distant/high-threat escort veto");
        }
        FollowerCombatAnchor.Independent = false; GoonPushLeadership.AdvanceAllowed = true;
        FollowerEnemyTracking.GroupSearching = false;
    }
}
