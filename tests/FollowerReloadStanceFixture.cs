using System;
using System.Reflection;
using EFT;
using pitTeam.Patches;
using UnityEngine;

namespace HarmonyLib
{
    public static class AccessTools { public static MethodInfo PropertySetter(Type t, string n) => t.GetProperty(n).SetMethod; }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
    public class PatchPostfix : Attribute { }
}
namespace UnityEngine { public static class Time { public static float time; } }
namespace EFT
{
    public class ShootData { public int Stops; public void EndShoot() { Stops++; } }
    public class MovementContext
    {
        public bool _isInPatrol, BlockFirearms, AnimatorPatrol;
        public int Changes;
        public LeftStanceController LeftStanceController = new LeftStanceController();
        public void SetPatrol(bool b) { _isInPatrol = b; AnimatorPatrol = b || BlockFirearms; Changes++; }
    }
    public class LeftStanceController { public bool LeftStance; public void ToggleLeftStance() { LeftStance = !LeftStance; } }
    public class Player { public object HandsController; public MovementContext MovementContext = new MovementContext(); }
    public class BotWeaponManager { public BotReload Reload; public object ShootController; }
    public class BotOwner
    {
        public bool IsDead, Follower = true;
        public Player GetPlayer = new Player();
        public BotWeaponManager WeaponManager = new BotWeaponManager();
        public ShootData ShootData = new ShootData();
    }
    public class BotReload
    {
        public BotOwner _owner;
        bool _reloading;
        public float _reloadStartTime;
        /* NATIVE RELOADING */
    }
}
namespace pitTeam.Modules
{
    public static class BossPlayers { public static bool IsFollower(BotOwner b) => b.Follower; }
    public static class Logger { public static void LogError(string s) { throw new Exception(s); } }
    public static class BattleRecorder { public static void RecordAmmoRefill(BotOwner b, string s, string r, Func<object> f) { } }
}
class StanceHarness
{
    public Player Player;
    public BotReload Reload;
    public bool WantPatrol = true;
    float _nextChangeStanceTime;
    const float CHANGE_STANCE_INTERVAL = 0.5f;
    bool _wantLeftStance;
    bool WantsPatrolStance() => WantPatrol;
    bool CanSetPatrol() => !Reload.Reloading;
    /* SAIN UPDATE STANCE */
    public void Tick(float time) => UpdateStance(time);
}
static class FollowerReloadStanceFixture
{
    static int checks;
    static readonly MethodInfo Postfix = typeof(FollowerReloadStancePatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
    static BotOwner Create()
    {
        var bot = new BotOwner();
        bot.WeaponManager.Reload = new BotReload { _owner = bot };
        bot.GetPlayer.HandsController = bot.WeaponManager.ShootController = new object();
        bot.GetPlayer.MovementContext.SetPatrol(true);
        return bot;
    }
    static void Start(BotReload reload, bool patch = true)
    {
        UnityEngine.Time.time = 42;
        reload.Reloading = true;
        if (patch) Postfix.Invoke(null, new object[] { reload, true });
    }
    static void Main()
    {
        var target = (MethodBase)typeof(FollowerReloadStancePatch).GetMethod("GetTargetMethod", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(new FollowerReloadStancePatch(), null);
        Check(target.Name == "set_Reloading" && target.DeclaringType == typeof(BotReload), "patch targets native reload admission setter");
        var old = Create(); Start(old.WeaponManager.Reload, false);
        var sain = new StanceHarness { Player = old.GetPlayer, Reload = old.WeaponManager.Reload };
        sain.Tick(1); sain.Tick(2);
        Check(old.GetPlayer.MovementContext._isInPatrol, "unchanged SAIN UpdateStance reproduces already-relaxed reload trap");
        sain.WantPatrol = false; sain.Tick(3);
        Check(!old.GetPlayer.MovementContext._isInPatrol, "changed sprint/combat intent releases the old trap");

        var bot = Create(); var reload = bot.WeaponManager.Reload; var movement = bot.GetPlayer.MovementContext;
        Start(reload);
        Check(!movement._isInPatrol && !movement.AnimatorPatrol, "active follower exits relaxed stance synchronously at reload start");
        Check(reload.Reloading && reload._reloadStartTime == 42 && bot.ShootData.Stops == 1, "native reload flag, timing and stop-shooting remain owned by EFT");
        sain = new StanceHarness { Player = bot.GetPlayer, Reload = reload };
        sain.Tick(1); sain.Tick(2);
        Check(!movement._isInPatrol, "SAIN cannot restore relaxed stance during reload once initial latch is cleared");
        reload.Reloading = false; Postfix.Invoke(null, new object[] { reload, false }); sain.Tick(3);
        Check(movement._isInPatrol, "normal relaxed stance resumes after native completion");

        Action<Action<BotOwner>, string> excluded = (change, label) =>
        {
            var b = Create(); var r = b.WeaponManager.Reload; change(b); Start(r);
            Check(b.GetPlayer.MovementContext._isInPatrol, label);
        };
        excluded(b => b.Follower = false, "ordinary bots unchanged");
        excluded(b => b.IsDead = true, "dead followers unchanged");
        excluded(b => b.WeaponManager.Reload = new BotReload(), "stowed reload object cannot change active hands stance");
        excluded(b => b.GetPlayer.HandsController = new object(), "medical or other hands cannot be changed");
        excluded(b => b.WeaponManager.ShootController = null, "missing firearm controller ignored");
        bot = Create(); bot.GetPlayer.MovementContext.BlockFirearms = true; Start(bot.WeaponManager.Reload);
        Check(!bot.GetPlayer.MovementContext._isInPatrol && bot.GetPlayer.MovementContext.AnimatorPatrol, "native BlockFirearms still owns its animation restriction");
        bot = Create(); movement = bot.GetPlayer.MovementContext; movement.SetPatrol(false); int before = movement.Changes;
        Start(bot.WeaponManager.Reload);
        Check(movement.Changes == before, "raised weapon needs no stance write");
        Console.WriteLine(checks + " reload stance assertions passed; native source reproduced the SAIN latch, no Unity animation claim.");
    }
}
