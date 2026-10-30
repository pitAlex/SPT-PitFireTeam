using System;
using System.Collections.Generic;
using HarmonyLib;
using EFT;
using UnityEngine;
using pitTeam.Modules;
using pitTeam.Patches;

public static class HeadLookChecks
{
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static BotHeadData Create(string label, bool registered = true)
    {
        var owner = new BotOwner { Label = label };
        BossPlayers.Instance = registered ? new BossPlayers { Registered = new Follower(owner) } : null;
        var head = new BotHeadData(owner);
        head.TryChangeState(BotHeadData.EHeadState.Patrol);
        MyExtensions.RandomCalls = 0;
        return head;
    }
    public static void Main()
    {
        var harmony = new Harmony("pitTeam.fixture.head-look");
        var target = AccessTools.Method(typeof(BotHeadData), nameof(BotHeadData.UpdatePatrolState));
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(FollowerPatrolHeadLookPatch), nameof(FollowerPatrolHeadLookPatch.PatchPrefix)));
        foreach (var role in new[] { "saved PMC", "recruited PMC", "recruited Scav", "Knight", "Big Pipe", "Birdeye", "SAINGrunt", "SAINShooter" })
        foreach (float angle in new[] { -25f, 25f })
        {
            var head = Create(role); var owner = head._owner;
            owner.GetPlayer.HeadRotation = new Vector3(0, angle, 0);
            head._targetHorizontalRotation = 40f;
            float speed = head._rotationSpeed;
            head.ManualUpdate();
            Check(head._targetHorizontalRotation == 0 && MyExtensions.RandomCalls == 0, role + ": random head scan suppressed");
            Check(Math.Abs(owner.GetPlayer.HeadRotation.y) < Math.Abs(angle) && owner.GetPlayer.HeadRotation.y != 0,
                role + ": native rotation centres smoothly rather than snapping/freeze");
            Check(owner.Memory.IsPeace && head.State == BotHeadData.EHeadState.Patrol && head._rotationSpeed == speed,
                role + ": peaceful state and native rotation speed preserved");
            for (int i = 0; i < 40; i++) head.ManualUpdate();
            Check(Math.Abs(owner.GetPlayer.HeadRotation.y) <= 1f && MyExtensions.RandomCalls == 0,
                role + ": repeated moving/idle updates do not restart scanning");
        }
        {
            var head = Create("ordinary bot", false); head.ManualUpdate();
            Check(MyExtensions.RandomCalls > 0 && head._targetHorizontalRotation != 0, "ordinary bot keeps native random scanning");
        }
        {
            var head = Create("dismissed follower"); head.ManualUpdate(); BossPlayers.Instance = null; head.ManualUpdate();
            Check(MyExtensions.RandomCalls > 0 && head._targetHorizontalRotation != 0, "dismissal immediately restores native patrol scanning");
        }
        {
            var head = Create("new enemy");
            head._owner.EnemiesController.BestObservedEnemy = new EnemyInfo { VisibilityLevel = 0.5f, CurrPosition = new Vector3(10, 0, 0) };
            head.ManualUpdate();
            Check(head.State == BotHeadData.EHeadState.EnemyVisibility && MyExtensions.RandomCalls == 0,
                "native visibility threshold still hands head ownership to the observed enemy");
            head.ManualUpdate();
            Check(head._targetHorizontalRotation != 0 && head._owner.GetPlayer.LookCalls > 0,
                "native enemy head targeting and animated rotation still execute");
        }
        {
            var head = Create("combat transition"); head._owner.Memory.IsPeace = false; head.ManualUpdate();
            Check(head.State == BotHeadData.EHeadState.Disabled, "native transition out of peaceful patrol remains owned by EFT");
        }
        foreach (string exclusion in new[] { "dead", "inactive", "no boss", "foreign identity", "no look settings", "no enemy controller", "non-patrol" })
        {
            var head = Create(exclusion); head._targetHorizontalRotation = 35f;
            switch (exclusion)
            {
                case "dead": head._owner.IsDead = true; break;
                case "inactive": head._owner.BotState = EBotState.NonActive; break;
                case "no boss": head._owner.BotFollower.HaveBoss = false; break;
                case "foreign identity": BossPlayers.Instance.Registered = new Follower(new BotOwner()); break;
                case "no look settings": head._owner.Settings.FileSettings.Look = null; break;
                case "no enemy controller": head._owner.EnemiesController = null; break;
                case "non-patrol": head.State = BotHeadData.EHeadState.EnemyVisibility; break;
            }
            Check(FollowerPatrolHeadLookPatch.PatchPrefix(head) && head._targetHorizontalRotation == 35f,
                "excluded state retains native ownership: " + exclusion);
        }
        Check(FollowerPatrolHeadLookPatch.PatchPrefix(null), "missing actor safely retains native ownership");
        harmony.UnpatchSelf();
        var restored = Create("patch removed"); restored.ManualUpdate();
        Check(MyExtensions.RandomCalls > 0, "unpatch restores the original native head controller");
        Console.WriteLine("PASS: " + checks + " production-prefix/native-head-controller checks; controlled animation inputs, not live Unity presentation.");
    }
}

namespace EFT
{
    public enum EBotState { Active, NonActive }
    public class BotOwner
    {
        public string Label; public bool IsDead; public EBotState BotState = EBotState.Active;
        public BotFollower BotFollower = new BotFollower(); public Memory Memory = new Memory();
        public Settings Settings = new Settings(); public Enemies EnemiesController = new Enemies();
        public Player GetPlayer = new Player(); public Vector3 LookDirection = new Vector3(0, 0, 1);
        public Sensor LookSensor = new Sensor(); public Aiming AimingManager = new Aiming();
    }
    public class BotFollower { public bool HaveBoss = true; }
    public class Memory { public bool IsPeace = true; }
    public class Settings { public FileSettings FileSettings = new FileSettings(); }
    public class FileSettings { public LookSettings Look = new LookSettings(); public BotGlobalPatrolSettings Patrol = new BotGlobalPatrolSettings(); }
    public class LookSettings { public float VISIBILITY_LEVEL_TO_TURN_HEAD = 0.5f, HEAD_ROTATE_SPEED = 20f; }
    public class Enemies { public EnemyInfo BestObservedEnemy; }
    public class Sensor { public Vector3 HeadPoint; }
    public class Aiming { public Aim CurrentAiming = new Aim(); }
    public class Aim { public bool HardAim; }
    public class Player
    {
        public Vector3 HeadRotation; public int LookCalls;
        public void Look(float y, float x, bool withReturn) { LookCalls++; HeadRotation.y -= y; }
    }
}
public class BotGlobalPatrolSettings { public float HEAD_TURN_SPEED = 10f, HEAD_FRONT_PERIOD_TIME = 1f, HEAD_TURN_PERIOD_TIME = 1f, HEAD_ANG_ROTATE = 40f; }
public class EnemyInfo { public float VisibilityLevel; public Vector3 CurrPosition; }
public abstract class BotData { public BotOwner _owner; protected BotData(BotOwner owner) { _owner = owner; } }
public static class MyExtensions
{
    public static int RandomCalls;
    public static float GreateRandom(float value) { RandomCalls++; return value; }
    public static bool IsTrue100(float value) { RandomCalls++; return true; }
}
namespace JetBrains.Annotations { public sealed class NotNullAttribute : Attribute { } }
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static float SignedAngle(Vector3 a, Vector3 b, Vector3 axis) { return (float)((Math.Atan2(b.x, b.z) - Math.Atan2(a.x, a.z)) * 180 / Math.PI); }
    }
    public static class Time { public static float time = 100f, deltaTime = 0.1f; }
    public static class Mathf { public static float Sign(float v) { return Math.Sign(v); } }
    public static class Debug { public static void Log(object value) { } }
}
namespace pitTeam.Modules
{
    public sealed class Follower { readonly BotOwner owner; public Follower(BotOwner owner) { this.owner = owner; } public BotOwner GetBot() { return owner; } }
    public class BossPlayers { public static BossPlayers Instance; public Follower Registered; public Follower GetFollower(BotOwner owner) { return Registered; } }
}
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract System.Reflection.MethodBase GetTargetMethod(); }
    public sealed class PatchPrefixAttribute : Attribute { }
}
