#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using EFT.HealthSystem;
using pitTeam.Modules;
using Modules = pitTeam.Modules;

namespace EFT
{
    public enum EBodyPart { Common, Head, Chest, LeftLeg, RightLeg }
    public struct ValueStruct { public float Current, Maximum; }
    public class Player
    {
        public string ProfileId = "leader";
        public bool RetryBlocked;
        public ActiveHealthController ActiveHealthController = new ActiveHealthController();
        public ActiveHealthController HealthController => ActiveHealthController;
    }
    public class BotOwner
    {
        public Player GetPlayer = new Player();
        public ActiveHealthController HealthController => GetPlayer?.HealthController;
        public AiData AIData;
        public string ProfileId = Guid.NewGuid().ToString();
        public int Cancellations, EmergencyRecoveries, Refreshes, HandsRecoveries;
        public bool WeaponReady, MovementRefreshed, MovementAllowed;
    }
    public class AiData { public Player Player; }
}
namespace EFT.HealthSystem
{
    public static class HealthHelper
    {
        public static EBodyPart[] RealBodyParts = { EBodyPart.Head, EBodyPart.Chest, EBodyPart.LeftLeg, EBodyPart.RightLeg };
    }
    public static class DamageHelper { public static object MedKitUse = new object(); }
    public class ActiveHealthController
    {
        public bool IsAlive = true, Fractured = true, Poisoned = true, Bleeding = true, PositiveBuff = true;
        public int NegativeResets, AntidoteResets, MedsConsumed;
        public Dictionary<EBodyPart, ValueStruct> Parts = new Dictionary<EBodyPart, ValueStruct>();
        public HashSet<EBodyPart> Destroyed = new HashSet<EBodyPart> { EBodyPart.LeftLeg };
        public ActiveHealthController()
        {
            foreach (var part in HealthHelper.RealBodyParts)
                Parts[part] = new ValueStruct { Current = part == EBodyPart.LeftLeg ? 0 : 10, Maximum = 65 };
        }
        // Native RemoveNegativeEffects excludes permanent ordinary intoxication.
        public void RemoveNegativeEffects(EBodyPart part) { NegativeResets++; Fractured = Bleeding = false; }
        public void RemoveIntoxication() { AntidoteResets++; Poisoned = false; }
        public bool IsBodyPartDestroyed(EBodyPart part) => Destroyed.Contains(part);
        public bool IsBodyPartBroken(EBodyPart part) => Fractured;
        public void FullRestoreBodyPart(EBodyPart part)
        {
            Destroyed.Remove(part);
            var value = Parts[part]; value.Current = value.Maximum; Parts[part] = value;
        }
        public ValueStruct GetBodyPartHealth(EBodyPart part, bool rounded) => Parts[part];
        public void ChangeHealth(EBodyPart part, float amount, object reason)
        {
            var value = Parts[part]; value.Current += amount; Parts[part] = value;
        }
    }
}
namespace pitTeam.Modules
{
    public static class Logger { public static void LogInfo(string message) { } }
    public class Follower { public BotOwner Bot; public BotOwner GetBot() => Bot; }
    public class BossPlayers
    {
        public static BossPlayers Instance = new BossPlayers();
        public static List<Follower> Followers = new List<Follower>();
        public static List<Follower> GetFollowersByBoss(string id) => Followers;
    }
}
namespace pitTeam.Utils
{
    public static class FollowerFirstAidRetry { public static void Clear(Player player) { player.RetryBlocked = false; } }
    public static class FloatExtensions { public static bool Positive(this float value) => value > 0; }
    public static class FollowerMedical
    {
        private static readonly Dictionary<string, object> HandsWatchStates = new Dictionary<string, object>(), PostCombatFullHealStates = new Dictionary<string, object>();
        private static readonly ConditionalWeakTable<Player, object> FirstAidTopOffStates = new ConditionalWeakTable<Player, object>();
        private static string GetBotKey(BotOwner bot) => bot.ProfileId;
        public static void SeedResetTracking(BotOwner bot)
        {
            HandsWatchStates[bot.ProfileId] = new object(); PostCombatFullHealStates[bot.ProfileId] = new object();
            FirstAidTopOffStates.GetValue(bot.GetPlayer, _ => new object()); bot.GetPlayer.RetryBlocked = true;
            if (bot.AIData?.Player != null && bot.AIData.Player != bot.GetPlayer)
            { FirstAidTopOffStates.GetValue(bot.AIData.Player, _ => new object()); bot.AIData.Player.RetryBlocked = true; }
        }
        public static bool HasResetTracking(BotOwner bot) => HandsWatchStates.ContainsKey(bot.ProfileId) || PostCombatFullHealStates.ContainsKey(bot.ProfileId) ||
            FirstAidTopOffStates.TryGetValue(bot.GetPlayer, out _) || bot.GetPlayer.RetryBlocked ||
            bot.AIData?.Player != null && (FirstAidTopOffStates.TryGetValue(bot.AIData.Player, out _) || bot.AIData.Player.RetryBlocked);
__MEDICAL__
        private static void CancelActiveMedical(BotOwner bot) { bot.Cancellations++; }
        private static void FinishExplicitHealHands(Player player) { }
        private static void ClearFollowerPoison(Player player) { player.ActiveHealthController.Poisoned = false; }
        private static void ReconcileCancelledMedicalOwnership(BotOwner bot) { }
        private static void CancelAllHealing(BotOwner bot, bool recoverDestroyedSurgeryParts)
        {
            CancelActiveMedical(bot);
            if (recoverDestroyedSurgeryParts) bot.EmergencyRecoveries++;
        }
        private static void RemoveBleedingEffects(Player player) { player.ActiveHealthController.Bleeding = false; }
        private static void RefreshMedicalWork(BotOwner bot) { bot.Refreshes++; }
        private static void RefreshBotMovementAfterHealing(BotOwner bot)
        {
            bot.MovementRefreshed = true;
            bot.MovementAllowed = !IsMovementDamagedLeg(bot.GetPlayer, EBodyPart.LeftLeg) &&
                                 !IsMovementDamagedLeg(bot.GetPlayer, EBodyPart.RightLeg);
        }
        private static void TryRecoverStuckMedicalHands(BotOwner bot, string reason) { bot.HandsRecoveries++; }
        private static void TryReturnToMainWeapon(BotOwner bot) { bot.WeaponReady = true; }
    }
}
public static class GamePlayerOwner { public static Player MyPlayer = new Player(); }
public class PluginHarness
{
    public void HealKey() => _BotHeal();
__HOTKEY__
}
public static class ForceHealChecks
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++; Console.WriteLine("PASS " + message);
    }
    private static bool Full(Player player)
    {
        foreach (var part in HealthHelper.RealBodyParts)
        {
            var value = player.HealthController.GetBodyPartHealth(part, false);
            if (value.Current != value.Maximum || player.HealthController.IsBodyPartDestroyed(part)) return false;
        }
        return true;
    }
    public static void Run()
    {
        var core = new BotOwner(); var sain = new BotOwner();
        core.AIData = new AiData { Player = core.GetPlayer };
        sain.AIData = new AiData { Player = new Player() };
        pitTeam.Utils.FollowerMedical.SeedResetTracking(core); pitTeam.Utils.FollowerMedical.SeedResetTracking(sain);
        BossPlayers.Followers = new List<Follower> { new Follower { Bot = core }, new Follower { Bot = sain } };
        new PluginHarness().HealKey();
        foreach (var bot in new[] { core, sain })
        {
            var health = bot.GetPlayer.HealthController;
            Check(!health.Fractured && !health.Poisoned && !health.Bleeding, "hotkey cures fracture, toxin and bleeding");
            Check(Full(bot.GetPlayer), "hotkey restores HP and blacked limb");
            Check(bot.Cancellations == 1 && bot.EmergencyRecoveries == 0, "hotkey cancels once without emergency surgery penalty");
            Check(bot.HandsRecoveries == 0 && !pitTeam.Utils.FollowerMedical.HasResetTracking(bot), "explicit key clears actual medical tracking and both aliases without broad hands recovery");
            Check(bot.MovementRefreshed && bot.MovementAllowed && bot.WeaponReady, "movement refresh follows real cure and returns weapon");
            Check(health.MedsConsumed == 0 && health.PositiveBuff, "explicit reset needs no antidote and preserves beneficial buff");
        }
        Check(core.GetPlayer.HealthController.NegativeResets == 1, "same player alias reset only once");
        Check(Full(sain.AIData.Player) && !sain.AIData.Player.HealthController.Poisoned && !sain.AIData.Player.HealthController.Fractured,
            "distinct AI player alias receives full reset");
        new PluginHarness().HealKey();
        Check(core.GetPlayer.HealthController.Parts[EBodyPart.LeftLeg].Maximum == 65 && core.EmergencyRecoveries == 0,
            "repeated key does not reduce limb maximum");
        var recovery = new BotOwner();
        pitTeam.Utils.FollowerMedical.ForceHeal(recovery);
        Check(recovery.GetPlayer.HealthController.Poisoned && recovery.GetPlayer.HealthController.Fractured,
            "automatic recovery retains existing toxin and fracture treatment rules");
        Check(recovery.EmergencyRecoveries == 1 && recovery.GetPlayer.HealthController.NegativeResets == 0,
            "automatic recovery keeps existing emergency recovery path");
        Check(!recovery.MovementAllowed, "HP recovery cannot hide an untreated fracture penalty");
        var dead = new BotOwner(); dead.HealthController.IsAlive = false;
        pitTeam.Utils.FollowerMedical.ForceHeal(dead, true);
        Check(dead.Cancellations == 0 && !dead.WeaponReady, "full reset never revives dead follower");
        pitTeam.Utils.FollowerMedical.ForceHeal(null, true);
        var noPlayer = new BotOwner { GetPlayer = null };
        pitTeam.Utils.FollowerMedical.ForceHeal(noPlayer, true);
        Check(noPlayer.Cancellations == 0, "missing owner or player safely ignored");
        GamePlayerOwner.MyPlayer.HealthController.IsAlive = false;
        int before = core.Cancellations; new PluginHarness().HealKey();
        Check(core.Cancellations == before, "dead player cannot trigger key");
        Console.WriteLine("Passed " + checks + " production force-heal checks.");
    }
}
