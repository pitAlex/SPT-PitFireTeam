using HarmonyLib;
using pitTeam.SAINAddon;
using SAIN.Models.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

public static partial class CombatChecks
{
    private static void TestRegroupFireSafety()
    {
        {
            var owner = Spawn("regroupFireSafety");
            var bot = owner.Sain;
            bot.ManualShoot.Bot = bot; bot.Suppression.Bot = bot; bot.Shoot.Bot = bot;
            bot.Transform.WeaponData.FirePort = Vector3.zero;
            bot.Transform.WeaponData.PointDirection = new Vector3(1, 0, 0);
            var enemy = new Enemy(); var point = new Vector3(30, 1, 0);
            var action = new SAINFollowerSquadRegroupAction(owner); action.Start();
            Check(bot.ManualShoot.TryShoot(enemy, point, true, EShootReason.Suppress), "safe regroup suppression reaches native trigger");
            int calls = bot.ManualShoot.Calls;
            owner.FriendlyInLane = true;
            Check(!bot.ManualShoot.TryShoot(enemy, point, true, EShootReason.Suppress) && bot.ManualShoot.Calls == calls,
                "regroup friendly target lane blocks before native trigger");
            Check(!owner.ShootData.Shooting && bot.ManualShoot.Reason == EShootReason.None,
                "blocked regroup fire clears manual burst state");
            owner.FriendlyInLane = false; owner.FriendlyInAim = true;
            Check(!bot.ManualShoot.TryShoot(enemy, point, true, EShootReason.Suppress) && bot.ManualShoot.Calls == calls,
                "regroup muzzle crossing blocks even with clear target lane");
            owner.FriendlyInAim = false;
            Check(bot.ManualShoot.TryShoot(enemy, point, true, EShootReason.Suppress), "regroup resumes through native trigger when lane clears");
            bot.Suppression.SuppressingTarget = true;
            owner.FriendlyInAim = true;
            action.Update(null);
            Check(!owner.ShootData.Shooting && !bot.Suppression.SuppressingTarget,
                "regroup update cancels cached native burst when ally enters muzzle lane");
            owner.FriendlyInAim = false;
            bot.ManualShoot.TryShoot(enemy, point, true, EShootReason.Suppress);
            owner.FriendlyInLane = true;
            action.Update(null);
            Check(!owner.ShootData.Shooting, "regroup update cancels burst when ally enters suppression target lane");
            owner.FriendlyInLane = false;
            Check(!bot.ManualShoot.TryShoot(enemy, new Vector3(float.NaN, 0, 0), true, EShootReason.Suppress),
                "invalid regroup suppression point fails closed");
            bot.Transform.WeaponData.PointDirection = Vector3.zero;
            Check(!bot.ManualShoot.TryShoot(enemy, point, true, EShootReason.Suppress), "invalid regroup muzzle direction fails closed");
            bot.Transform.WeaponData.PointDirection = new Vector3(1, 0, 0);
            bot.ManualShoot.TryShoot(enemy, point, true, EShootReason.Suppress);
            action.Stop();
            Check(!owner.ShootData.Shooting && bot.ManualShoot.Reason == EShootReason.None,
                "regroup stop releases native suppression before successor action");
            bot.CurrentAction = null; owner.FriendlyInAim = true;
            calls = bot.ManualShoot.Calls;
            Check(bot.ManualShoot.TryShoot(enemy, point, true, EShootReason.Suppress) && bot.ManualShoot.Calls == calls + 1,
                "ordinary native action bypasses addon regroup guard");
            bot.CurrentAction = action;
            Check(bot.ManualShoot.TryShoot(enemy, point, true, EShootReason.None), "non suppression manual fire retains native policy");
            owner.FriendlyInAim = false;
            for (int i = 0; i < 1000; i++) SainRegroupFireSafety.CheckActiveBurst(bot);
            Check(bot.ManualShoot.Calls == calls + 2, "passive safety checks never invoke shooting or target providers");
        }
    }
}
