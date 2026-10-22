using System;
using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using pitTeam.BigBrain;

internal static class MagazineSupplyScopeFixture
{
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static bool Run(BotReload reload) => (bool)typeof(pitTeam.Patches.FollowerMagazineSupplyPatch)
        .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { reload, new Weapon(), new Magazine() });
    public static void Main()
    {
        var bot = new BotOwner(); var reload = new BotReload { _owner = bot, NoAmmoForReloadCached = true };
        Check(Run(reload) && FollowerOutOfCombatReloadPolicy.Calls == 0, "Ordinary bots keep the original refill");
        bot.Follower = true;
        Check(!Run(reload) && FollowerOutOfCombatReloadPolicy.Calls == 1, "Followers use the bounded shared refill");
        Check(!reload.NoAmmoForReloadCached, "A queued real refill clears stale no-ammo state");
        bot.IsDead = true;
        Check(!Run(reload) && FollowerOutOfCombatReloadPolicy.Calls == 1, "Dead followers cannot begin refills");
        bot.IsDead = false; bot.GetPlayer.HealthController.IsAlive = false;
        Check(!Run(reload) && FollowerOutOfCombatReloadPolicy.Calls == 1, "Dead health state remains protected");
        bot.GetPlayer.HealthController.IsAlive = true; FollowerOutOfCombatReloadPolicy.Throw = true;
        Check(!Run(reload) && pitTeam.Modules.Logger.Errors == 1, "A failed allocation never falls back to unbounded native filling");
        Check(Run(new BotReload()), "Missing/ordinary owner does not take over native behavior");
        Console.WriteLine("PASS: " + checks + " production native refill-scope checks; not a live transaction test.");
    }
}
public class BotReload { public BotOwner _owner; public bool NoAmmoForReloadCached; public void AddAmmoToMag(Weapon weapon, Magazine foundMag) { } }
namespace EFT
{
    public class BotOwner { public bool Follower, IsDead; public Player GetPlayer = new Player(); }
    public class Player { public Health HealthController = new Health(); }
    public class Health { public bool IsAlive = true; }
}
namespace EFT.InventoryLogic { public class Weapon { } public class Magazine { } }
namespace pitTeam.Modules
{
    public static class BossPlayers { public static bool IsFollower(BotOwner bot) => bot.Follower; }
    public static class Logger { public static int Errors; public static void LogError(string value) { Errors++; } }
}
namespace pitTeam.BigBrain
{
    public static class FollowerOutOfCombatReloadPolicy
    {
        public static int Calls; public static bool Throw;
        public static bool TryFillMagazineWithLooseAmmo(BotOwner bot, Magazine magazine, Weapon weapon)
        { Calls++; if (Throw) throw new InvalidOperationException("Allocation failed"); return true; }
    }
}
namespace HarmonyLib
{
    public class PatchPrefix : Attribute { }
    public static class AccessTools { public static MethodBase Method(Type type, string name) => type.GetMethod(name); }
}
namespace SPT.Reflection.Patching { public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); } }
