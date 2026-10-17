#nullable disable
#pragma warning disable CS0649,CS0414,CS8632
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using pitTeam.Utils;
using pitTeam.Modules;
using UnityEngine;
namespace UnityEngine { public static class Time { public static float time; } }
namespace EFT.InventoryLogic { public class Meds { } }
namespace EFT.HealthSystem { }
namespace EFT
{
    public enum EBodyPart { Head, Chest, Stomach, LeftArm, RightArm, LeftLeg, RightLeg }
    public enum EBotState { Active, Inactive }
    public enum Slot { Main, Second }
    public struct ValueStruct { public float Current, Maximum; public float Normalized => Current / Maximum; }
    public class Health
    {
        public bool Bleeding; public HashSet<EBodyPart> Broken = new HashSet<EBodyPart>(), Destroyed = new HashSet<EBodyPart>();
        public Dictionary<EBodyPart, ValueStruct> Parts = new Dictionary<EBodyPart, ValueStruct>();
        public Health() { foreach (EBodyPart p in Enum.GetValues(typeof(EBodyPart))) Parts[p] = new ValueStruct { Current = 65, Maximum = 65 }; }
        public ValueStruct GetBodyPartHealth(EBodyPart p, bool rounded) => Parts[p];
        public bool IsBodyPartBroken(EBodyPart p) => Broken.Contains(p);
        public bool IsBodyPartDestroyed(EBodyPart p) => Destroyed.Contains(p);
        public bool CanApplyItem(Meds med, EBodyPart p) => med != null && Parts[p].Current < Parts[p].Maximum;
    }
    public class AIData { public BotOwner BotOwner; }
    public class Player
    {
        public Health HealthController = new Health(); public AIData AIData = new AIData(); public object HandsController;
        public class FirearmController { }
        public class BaseGrenadeHandsController { }
        public class GrenadeHandsController : BaseGrenadeHandsController
        {
            public object CurrentGrenadeOperation; public bool Destroyed; public Action<GrenadeHandsController> _onUsedCallback;
            public class Operation { public virtual void OnDropFinishedAction() { } }
            public class HighThrowOperation : Operation
            {
                public GrenadeHandsController Controller; enum EThrowState { Throwing, Threw }
                EThrowState _throwState; Action _onControllerDestroy;
                /* NATIVE FINISH */
            }
        }
    }
    public class BotFirstAid
    {
        public BotOwner _owner; public bool Using, Damaged; public EBodyPart? _bodyPartToHeal;
        public Meds CurUsingMeds = new Meds();
        public EBodyPart[] parts = (EBodyPart[])Enum.GetValues(typeof(EBodyPart));
    }
    public class Medicine { public BotFirstAid FirstAid; public bool OtherUse; public bool Using => OtherUse || FirstAid.Using; }
    public class Enemy { }
    public class Memory { public Enemy GoalEnemy; }
    public class Mind { public float PART_PERCENT_TO_HEAL = 0.8f; }
    public class FileSettings { public Mind Mind = new Mind(); }
    public class Settings { public FileSettings FileSettings = new FileSettings(); }
    public class Reload { public bool Reloading; }
    public class Selector
    {
        public bool IsWeaponReady = true; public Slot LastEquipmentSlot = Slot.Second, _mainWeapon = Slot.Main; public int MainRequests;
        public void TakeMainWeapon() { MainRequests++; LastEquipmentSlot = _mainWeapon; }
    }
    public class Grenades { public bool ThrowindNow = true; public int Ends; public void EndAll() { Ends++; ThrowindNow = false; } }
    public class Weapons { public Selector Selector = new Selector(); public Grenades Grenades = new Grenades(); public Reload Reload = new Reload(); }
    public class BotOwner
    {
        public Player GetPlayer = new Player(); public bool IsDead; public EBotState BotState = EBotState.Active;
        public Medicine Medecine = new Medicine(); public Memory Memory = new Memory(); public Settings Settings = new Settings(); public Weapons WeaponManager = new Weapons();
        public BotOwner() { GetPlayer.AIData.BotOwner = this; Medecine.FirstAid = new BotFirstAid { _owner = this }; }
    }
}
namespace pitTeam.Components { static class BotFollowerPlayer { public static bool IsEnemyInfoAlive(Enemy e) => e != null; } }
namespace pitTeam.Modules
{
    static class Logger { public static void LogError(string message) { throw new Exception(message); } }
    static class BattleRecorder { public static void RecordGrenadeEvent(BotOwner b, string action, string reason) { } }
}
namespace pitTeam.Utils
{
    static class FollowerMedical
    {
        public static bool HasActiveBleeding(Player p) => p.HealthController.Bleeding;
        /* PROGRESS SNAPSHOT */
        /* PROGRESS METHOD */
        public static bool Progress(float oldHp, float newHp, float oldMax, float newMax, bool oldPending = false, bool newPending = false) =>
            HasMedicalProgressed(new MedicalProgressSnapshot(null, oldPending, false, false, oldHp, oldMax),
                new MedicalProgressSnapshot(null, newPending, false, false, newHp, newMax));
    }
}
public static class MedicalRecoveryChecks
{
    static int count;
    static void Check(bool pass, string label) { if (!pass) throw new Exception(label); count++; }
    static void Hp(BotOwner b, EBodyPart p, float hp) { b.GetPlayer.HealthController.Parts[p] = new ValueStruct { Current = hp, Maximum = 65 }; }
    static void Attempt(BotOwner b, EBodyPart p)
    {
        var fa = b.Medecine.FirstAid; fa._bodyPartToHeal = p; fa.Damaged = true;
        FollowerFirstAidRetry.Begin(fa); fa.Using = true;
    }
    public static void Main()
    {
        Check(!FollowerMedical.Progress(100, 99, 440, 440), "poison damage is not medical progress");
        Check(FollowerMedical.Progress(100, 101, 440, 440), "healing is progress");
        Check(!FollowerMedical.Progress(100, 100, 440, 400), "lost maximum health is not progress");
        Check(!FollowerMedical.Progress(100, 100, 440, 440, false, true), "new pending work is not completed treatment");
        Check(FollowerMedical.Progress(100, 100, 440, 440, true, false), "completed pending work can indicate an effect cure");

        var b = new BotOwner(); var fa = b.Medecine.FirstAid; Time.time = 100; Hp(b, EBodyPart.LeftLeg, 20);
        Attempt(b, EBodyPart.LeftLeg); Time.time = 101; FollowerFirstAidRetry.Observe(b);
        Check(FollowerFirstAidRetry.CanAttempt(b.GetPlayer, EBodyPart.LeftLeg), "live treatment is not counted as a failure");
        fa.Using = false; FollowerFirstAidRetry.Observe(b);
        Check(!FollowerFirstAidRetry.CanAttempt(b.GetPlayer, EBodyPart.LeftLeg), "first ineffective native treatment gets retry delay");
        FollowerFirstAidRetry.ValidateSelection(fa); Check(!fa.Damaged && fa._bodyPartToHeal == null, "native stale target is withheld during retry");
        Hp(b, EBodyPart.RightArm, 10); fa.Damaged = true; fa._bodyPartToHeal = EBodyPart.LeftLeg;
        FollowerFirstAidRetry.ValidateSelection(fa); Check(fa.Damaged && fa._bodyPartToHeal == EBodyPart.RightArm, "another injured part retains native priority");
        Time.time = 104; Check(FollowerFirstAidRetry.CanAttempt(b.GetPlayer, EBodyPart.LeftLeg), "first retry becomes available");
        Attempt(b, EBodyPart.LeftLeg); Hp(b, EBodyPart.LeftLeg, 19); fa.Using = false; Time.time = 105; FollowerFirstAidRetry.Observe(b);
        Time.time = 110; Check(!FollowerFirstAidRetry.CanAttempt(b.GetPlayer, EBodyPart.LeftLeg), "second ineffective use blocks repeated treatment");
        Hp(b, EBodyPart.LeftLeg, 18); Time.time = 120; FollowerFirstAidRetry.Observe(b);
        Check(!FollowerFirstAidRetry.CanAttempt(b.GetPlayer, EBodyPart.LeftLeg), "more toxin damage cannot rearm ineffective retries");
        b.Memory.GoalEnemy = new Enemy(); Check(FollowerFirstAidRetry.CanAttempt(b.GetPlayer, EBodyPart.LeftLeg), "real combat retains urgent treatment"); b.Memory.GoalEnemy = null;
        b.GetPlayer.HealthController.Bleeding = true; Check(FollowerFirstAidRetry.CanAttempt(b.GetPlayer, EBodyPart.LeftLeg), "bleeding retains emergency priority"); b.GetPlayer.HealthController.Bleeding = false;
        b.GetPlayer.HealthController.Broken.Add(EBodyPart.LeftLeg); Check(FollowerFirstAidRetry.CanAttempt(b.GetPlayer, EBodyPart.LeftLeg), "fracture treatment remains available"); b.GetPlayer.HealthController.Broken.Clear();
        var other = new BotOwner(); Check(FollowerFirstAidRetry.CanAttempt(other.GetPlayer, EBodyPart.LeftLeg), "retry budget is follower-local");
        Time.time = 135; Check(FollowerFirstAidRetry.CanAttempt(b.GetPlayer, EBodyPart.LeftLeg), "bounded backoff expires");
        Attempt(b, EBodyPart.LeftLeg); Hp(b, EBodyPart.LeftLeg, 30); fa.Using = false; Time.time = 136; FollowerFirstAidRetry.Observe(b);
        Check(FollowerFirstAidRetry.CanAttempt(b.GetPlayer, EBodyPart.LeftLeg), "successful healing clears failures");
        Attempt(b, EBodyPart.LeftLeg); fa.Using = false; FollowerFirstAidRetry.Observe(b);
        Check(!FollowerFirstAidRetry.CanAttempt(b.GetPlayer, EBodyPart.LeftLeg), "new ineffective attempt owns retry backoff");
        FollowerFirstAidRetry.Clear(b.GetPlayer);
        Check(FollowerFirstAidRetry.CanAttempt(b.GetPlayer, EBodyPart.LeftLeg), "explicit heal clears old per-part retry state");

        CheckGrenadeRecovery(false); CheckGrenadeRecovery(true);
        Console.WriteLine("Passed " + count + " production medical progress/retry and native blocked-grenade completion checks.");
    }
    static void CheckGrenadeRecovery(bool animationFinishesFirst)
    {
        Time.time = 200; var b = new BotOwner(); var hands = new Player.GrenadeHandsController();
        var op = new Player.GrenadeHandsController.HighThrowOperation { Controller = hands }; hands.CurrentGrenadeOperation = op;
        hands._onUsedCallback = _ => { b.WeaponManager.Grenades.EndAll(); b.GetPlayer.HandsController = new Player.FirearmController(); };
        b.GetPlayer.HandsController = hands; FollowerBlockedGrenadeRecovery.Record(b, hands);
        FollowerBlockedGrenadeRecovery.Update(b); Check(b.WeaponManager.Grenades.Ends == 0, "veto does not reenter executing release");
        if (animationFinishesFirst) op.OnDropFinishedAction();
        Time.time = 200.21f; FollowerBlockedGrenadeRecovery.Update(b);
        Check(b.WeaponManager.Grenades.Ends == 1 && !b.WeaponManager.Grenades.ThrowindNow, "native completion closes vetoed throw exactly once");
        Check(b.WeaponManager.Selector.MainRequests == 0, "native previous-weapon draw can settle first");
        Time.time = 200.5f; FollowerBlockedGrenadeRecovery.Update(b);
        Check(b.WeaponManager.Selector.MainRequests == 1, "blocked release returns to main weapon");
        Time.time = 201; FollowerBlockedGrenadeRecovery.Update(b); op.OnDropFinishedAction();
        Check(b.WeaponManager.Selector.MainRequests == 1 && b.WeaponManager.Grenades.Ends == 1, "late animation completion and update are idempotent");

        b = new BotOwner(); hands = new Player.GrenadeHandsController(); op = new Player.GrenadeHandsController.HighThrowOperation { Controller = hands }; hands.CurrentGrenadeOperation = op;
        b.GetPlayer.HandsController = hands; Time.time = 210; FollowerBlockedGrenadeRecovery.Record(b, hands);
        b.GetPlayer.HandsController = new object(); Time.time = 210.3f; FollowerBlockedGrenadeRecovery.Update(b);
        Check(b.WeaponManager.Selector.MainRequests == 0 && b.WeaponManager.Grenades.Ends == 0, "another hands owner is preserved");

        b = new BotOwner(); hands = new Player.GrenadeHandsController(); op = new Player.GrenadeHandsController.HighThrowOperation { Controller = hands }; hands.CurrentGrenadeOperation = op;
        b.GetPlayer.HandsController = hands; Time.time = 220; FollowerBlockedGrenadeRecovery.Record(b, hands);
        b.Medecine.OtherUse = true; Time.time = 220.3f; FollowerBlockedGrenadeRecovery.Update(b);
        Check(b.WeaponManager.Grenades.Ends == 0 && b.WeaponManager.Selector.MainRequests == 0, "pending medicine is not displaced by blocked-throw recovery");

        b = new BotOwner(); hands = new Player.GrenadeHandsController(); op = new Player.GrenadeHandsController.HighThrowOperation { Controller = hands }; hands.CurrentGrenadeOperation = op;
        b.GetPlayer.HandsController = hands; Time.time = 230; FollowerBlockedGrenadeRecovery.Record(b, hands);
        Time.time = 230.3f; FollowerBlockedGrenadeRecovery.Update(b);
        Check(b.WeaponManager.Grenades.Ends == 1, "missing on-used callback receives native EndAll fallback");
        b.WeaponManager.Selector.IsWeaponReady = false; Time.time = 230.6f; FollowerBlockedGrenadeRecovery.Update(b);
        Check(b.WeaponManager.Selector.MainRequests == 0, "busy weapon transition is preserved");
        Time.time = 236; FollowerBlockedGrenadeRecovery.Update(b); b.WeaponManager.Selector.IsWeaponReady = true;
        Time.time = 237; FollowerBlockedGrenadeRecovery.Update(b);
        Check(b.WeaponManager.Selector.MainRequests == 0, "expired recovery cannot disturb later activity");
    }
}
