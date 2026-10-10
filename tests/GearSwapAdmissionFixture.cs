using System;
using System.Collections.Generic;
using System.Linq;

enum EBotState { Active, Inactive }
class Vector3 { public float X; public static float Distance(Vector3 a, Vector3 b) => Math.Abs(a.X - b.X); }
class Health { public bool IsAlive = true; }
class Inventory { public bool IsChangingWeapon; }
class Player { public Health HealthController = new Health(); public Vector3 Position = new Vector3(); public Inventory InventoryController = new Inventory(); }
class GamePlayerOwner { public Player Player = new Player(); }
class Memory { public bool HaveEnemy, IsUnderFire; }
class Selector { public bool IsChanging; }
class Grenades { public bool ThrowindNow; }
class Reload { public bool Reloading; }
class Info { public Reload Reload = new Reload(); }
class Weapons
{
    public Selector Selector = new Selector(); public Grenades Grenades = new Grenades();
    public Dictionary<string, Info> info = new Dictionary<string, Info>();
    public Info _currentWeaponInfo; public bool HandsAllowed = true; public int HandsChecks;
    public bool CanChangeHands() { HandsChecks++; return HandsAllowed; }
}
class BotOwner
{
    public bool IsDead; public EBotState BotState = EBotState.Active;
    public Player GetPlayer = new Player(); public Memory Memory = new Memory();
    public Weapons WeaponManager = new Weapons(); public bool HealPending, PickupPending;
}
class Boss { public Player realPlayer; }
class BotFollowerPlayer
{
    public Boss Boss = new Boss(); public bool KnownEnemy; public bool IsSpawnedSquadMate = true;
    public Boss GetBoss() => Boss; public bool HasKnownEnemy() => KnownEnemy;
}
static class TeammateBackpackInspection
{
    public static bool HasActiveOrPendingHealWork(BotOwner b) => b.HealPending;
    public static bool HasActiveOrPendingPickupWork(BotOwner b, BotFollowerPlayer f) => b.PickupPending;
}
static class Admission
{
    /* GATE */
    public static string Check(GamePlayerOwner o, BotOwner b, BotFollowerPlayer f, bool applying = false) => UnsafeReason(o, b, f, applying);
}
static class GearSwapAdmissionFixture
{
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
    static void Main(string[] args)
    {
        var owner = new GamePlayerOwner(); var bot = new BotOwner(); var follower = new BotFollowerPlayer();
        follower.Boss.realPlayer = owner.Player;
        var primary = new Info(); var secondary = new Info(); var pistol = new Info();
        bot.WeaponManager._currentWeaponInfo = primary;
        bot.WeaponManager.info.Add("primary", primary); bot.WeaponManager.info.Add("secondary", secondary); bot.WeaponManager.info.Add("holster", pistol);
        Check(Admission.Check(owner, bot, follower) == null, "owned idle follower admitted");
        pistol.Reload.Reloading = true;
        if (args.Length > 0)
        {
            Check(Admission.Check(owner, bot, follower) == "reloading", "old production gate reproduces stale stowed reload rejection");
            Console.WriteLine("Original stale-stowed reload rejection reproduced."); return;
        }
        follower.IsSpawnedSquadMate = false;
        int recruitHandsChecks = bot.WeaponManager.HandsChecks;
        Check(Admission.Check(owner, bot, follower) == "followerNotSpawnedSquadMate", "field recruit cannot open via direct or bound command");
        Check(Admission.Check(owner, bot, follower, true) == "followerNotSpawnedSquadMate", "field recruit remains denied during Apply revalidation");
        Check(bot.WeaponManager.HandsChecks == recruitHandsChecks, "recruit rejected before native hands query");
        follower.IsSpawnedSquadMate = true;
        Check(Admission.Check(owner, bot, follower, true) == null, "spawned follower Apply transition remains admitted");
        Check(Admission.Check(owner, bot, follower) == null, "stale holster flag does not block idle hands");
        secondary.Reload.Reloading = true;
        Check(Admission.Check(owner, bot, follower) == null, "both stowed flags ignored");
        primary.Reload.Reloading = true;
        int before = bot.WeaponManager.HandsChecks;
        Check(Admission.Check(owner, bot, follower) == "reloading", "active primary reload rejected");
        Check(bot.WeaponManager.HandsChecks == before, "blocked reload does not re-query hands");
        primary.Reload.Reloading = false; bot.WeaponManager._currentWeaponInfo = pistol;
        Check(Admission.Check(owner, bot, follower) == "reloading", "active pistol reload rejected");
        pistol.Reload.Reloading = false; bot.WeaponManager.HandsAllowed = false;
        Check(Admission.Check(owner, bot, follower) == "followerHandsBusy", "native hands restrictions preserved");
        bot.WeaponManager.HandsAllowed = true; follower.KnownEnemy = true;
        Check(Admission.Check(owner, bot, follower) == "knownEnemy", "known enemy preserved");
        follower.KnownEnemy = false; bot.Memory.HaveEnemy = true;
        Check(Admission.Check(owner, bot, follower) == "nativeEnemy", "native enemy preserved");
        bot.Memory.HaveEnemy = false; bot.Memory.IsUnderFire = true;
        Check(Admission.Check(owner, bot, follower) == "underFire", "under fire preserved");
        bot.Memory.IsUnderFire = false; bot.HealPending = true;
        Check(Admission.Check(owner, bot, follower) == "healing", "pending heal preserved");
        bot.HealPending = false; bot.PickupPending = true;
        Check(Admission.Check(owner, bot, follower) == "looting", "pending loot preserved");
        bot.PickupPending = false; bot.WeaponManager.Selector.IsChanging = true;
        Check(Admission.Check(owner, bot, follower) == "followerWeaponChanging", "selector change preserved");
        bot.WeaponManager.Selector.IsChanging = false; bot.WeaponManager.Grenades.ThrowindNow = true;
        Check(Admission.Check(owner, bot, follower) == "throwingGrenade", "grenade preserved");
        bot.WeaponManager.Grenades.ThrowindNow = false; owner.Player.InventoryController.IsChangingWeapon = true;
        Check(Admission.Check(owner, bot, follower) == "playerWeaponChanging", "player change preserved");
        owner.Player.InventoryController.IsChangingWeapon = false; bot.GetPlayer.Position.X = 3;
        Check(Admission.Check(owner, bot, follower).StartsWith("outOfRange:"), "range preserved");
        bot.GetPlayer.Position.X = 0; follower.Boss.realPlayer = new Player();
        Check(Admission.Check(owner, bot, follower) == "followerOwnershipChanged", "ownership preserved");
        follower.Boss.realPlayer = owner.Player; bot.BotState = EBotState.Inactive;
        Check(Admission.Check(owner, bot, follower) == "followerDeadOrInactive", "inactive bot preserved");
        bot.BotState = EBotState.Active; bot.IsDead = true;
        Check(Admission.Check(owner, bot, follower) == "followerDeadOrInactive", "dead bot preserved");
        bot.IsDead = false; owner.Player.HealthController.IsAlive = false;
        Check(Admission.Check(owner, bot, follower) == "playerDeadOrUnavailable", "dead player preserved");
        owner.Player.HealthController.IsAlive = true;
        Check(Admission.Check(owner, null, follower) == "followerUnavailable", "missing target safe");
        Console.WriteLine("Swap Gear admission: " + checks + " assertions passed.");
    }
}
