using pitTeam.SAINAddon;
using SAIN.Models.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

public static partial class CombatChecks
{
    private static void TestRegroupFireSafety()
    {
        var owner = Spawn("regroupFireSafety");
        var bot = owner.Sain;
        bot.ManualShoot.Bot = bot; bot.Suppression.Bot = bot; bot.Shoot.Bot = bot;
        bot.Transform.WeaponData.PointDirection = new Vector3(1, 0, 0);
        var enemy = new Enemy(); var point = new Vector3(30, 1, 0);
        var action = new SAINFollowerSquadRegroupAction(owner); action.Start();
        Check(SainRegroupFireSafety.IsSafe(bot, point), "clear regroup action-local lane is safe");
        owner.FriendlyInLane = true;
        Check(!SainRegroupFireSafety.IsSafe(bot, point), "regroup early hold checks wide target lane");
        owner.FriendlyInLane = false; owner.FriendlyInAim = true;
        Check(!SainRegroupFireSafety.IsSafe(bot, point), "regroup early hold independently checks muzzle lane");
        owner.FriendlyInAim = false;
        bot.ManualShoot.TryShoot(enemy, point, true, EShootReason.Suppress);
        bot.Suppression.SuppressingTarget = true; owner.FriendlyInAim = true;
        action.Update(null);
        Check(!owner.ShootData.Shooting && !bot.Suppression.SuppressingTarget && bot.ManualShoot.Reason == EShootReason.None,
            "regroup update clears cached suppression when ally enters muzzle lane");
        owner.FriendlyInAim = false;
        bot.ManualShoot.TryShoot(enemy, point, true, EShootReason.Suppress);
        owner.FriendlyInLane = true; action.Update(null);
        Check(!owner.ShootData.Shooting, "regroup update releases blocked target lane before movement throttle");
        owner.FriendlyInLane = false;
        Check(!SainRegroupFireSafety.IsSafe(bot, new Vector3(float.NaN, 0, 0)), "invalid regroup target fails closed");
        bot.Transform.WeaponData.PointDirection = Vector3.zero;
        Check(!SainRegroupFireSafety.IsSafe(bot, point), "invalid regroup direction fails closed");
        bot.Transform.WeaponData.PointDirection = new Vector3(1,0,0);
        bot.ManualShoot.TryShoot(enemy, point, true, EShootReason.Suppress); action.Stop();
        Check(!owner.ShootData.Shooting && bot.ManualShoot.Reason == EShootReason.None, "regroup stop releases suppression for successor");
        int calls = bot.ManualShoot.Calls;
        for (int i = 0; i < 1000; i++) SainRegroupFireSafety.CheckActiveBurst(bot);
        Check(bot.ManualShoot.Calls == calls, "passive early holds never invoke shooting or target providers");
        // Shared Core normal/manual/automatic coverage lives in Verify-FollowerFireSafety.ps1.
    }
}
