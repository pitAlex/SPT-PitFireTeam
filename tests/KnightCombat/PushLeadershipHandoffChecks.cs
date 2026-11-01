using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.GoingRogue.Combat;
using UnityEngine;

internal static class PushLeadershipHandoffChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (string blocked in new[] { "none", "medical", "dogfight", "healWork", "danger", "underFire",
            "reload", "nativeReload", "weaponDraw", "weaponNotReady", "throw", "readyGrenade", "visibleFire",
            "noPosition", "noAdmission", "noTransition", "notPush" })
        {
            Time.time = 200;
            var bot = new BotOwner();
            bot.Memory.GoalEnemy.Person.AIData.BotOwner = new BotOwner();
            var follower = new BotFollowerPlayer { Owner = bot, Command = FollowerCommandType.PushEnemy };
            BossPlayers.Instance.Followers[bot] = follower;
            var logic = new BigPipeCombatLogic(bot);
            logic.ProbeBeginPush();
            var common = logic.ProbeCommon;
            common.FiringPoint = true;
            KnightSquadCoordinator.FreshDirective = true;
            KnightSquadCoordinator.Point = new(25, 0, 0);
            int completed = GoonPushLeadership.Completed;
            var current = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, "orderedPushWait");
            switch (blocked)
            {
                case "medical": current.Action = BotLogicDecision.heal; break;
                case "dogfight": current.Action = BotLogicDecision.dogFight; break;
                case "healWork": common.HealWork = true; break;
                case "danger": common.Danger = true; break;
                case "underFire": bot.Memory.IsUnderFire = true; break;
                case "reload": bot.WeaponManager.Reload.Reloading = true; break;
                case "nativeReload": bot.WeaponManager.ShootController.Reloading = true; break;
                case "weaponDraw": bot.WeaponManager.Selector.IsChanging = true; break;
                case "weaponNotReady": bot.WeaponManager.IsWeaponReady = false; break;
                case "throw": bot.WeaponManager.Grenades.ThrowindNow = true; break;
                case "readyGrenade": bot.WeaponManager.Grenades.ReadyToThrow = true; break;
                case "visibleFire": bot.Memory.GoalEnemy.IsVisible = bot.Memory.GoalEnemy.CanShoot = true; break;
                case "noPosition": common.FiringPoint = false; break;
                case "noAdmission": common.Admission = false; break;
                case "noTransition": common.TransitionAllowed = false; break;
                case "notPush": logic = new BigPipeCombatLogic(bot); break;
            }
            bool accepted = blocked == "none";
            var end = logic.ShallEndCurrentDecision(current);
            check(end.Value == accepted, "Knight takeover protects " + blocked);
            check(follower.Cleared == (accepted ? 1 : 0) && follower.AggressionCleared == (accepted ? 1 : 0),
                "player push/aggression are cancelled only after a prepared successor: " + blocked);
            check(GoonPushLeadership.Completed == completed + (accepted ? 1 : 0), "ownership ends only on successful takeover: " + blocked);
            if (accepted)
            {
                check(!logic.ProbeInPush && common.PreparedTransition.HasValue && bot.GoToSomePointData.Point.x == 25,
                    "takeover leaves push objective with a concrete published successor");
                Time.time++;
                check(!logic.ShallEndCurrentDecision(current).Value && follower.Cleared == 1,
                    "superseded player push does not resume or repeatedly cancel");
            }
            else if (blocked != "notPush") check(logic.ProbeInPush, "blocked handoff preserves current push: " + blocked);
            BossPlayers.Instance.Followers.Remove(bot);
        }
        KnightSquadCoordinator.FreshDirective = false;
    }
}
