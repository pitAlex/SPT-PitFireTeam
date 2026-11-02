using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using EFT.Quests;
using HarmonyLib;
using pitTeam.GoingRogue.Quests;
using pitTeam.Modules;

internal static class PartizanChecks
{
    public static void Main()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        new Harmony("fixture.partizan").PatchAll(typeof(PartizanQuestSpawn).Assembly);
        var session = new EftClientBackendSession();
        var quest = new QuestData { Id = KnightQuestPolicy.AFavor, Status = EQuestStatus.Started };
        session.Profile.QuestsData.Add(quest);
        var settings = new RaidSettings { IsPmc = true, LocationId = "lighthouse" };
        var partizan = new BossLocationSpawn { BossName = "bossPartisan", BossChance = 5, DependKarma = true,
            TriggerName = "botEvent", TriggerId = "PARTISAN_TRIGGER", Time = -1, Delay = 3, PerfectPos = 7,
            ShowOnTarkovMap = true, ShowOnTarkovMapPvE = true, BossZone = "native-zone" };
        var other = new BossLocationSpawn { BossName = "bossKojaniy", BossChance = 20 };
        var waves = new[] { partizan, other };
        Check(ReferenceEquals(PartizanQuestSpawn.Prepare(waves), waves), "inactive state must preserve source array");
        GameplayModeRuntime.IsAllegiance = true;
        session.SendRaidSettings(settings);
        var scenario = new BossSpawnScenario(); scenario.Init(waves);
        var prepared = scenario.Waves[0];
        Check(!ReferenceEquals(prepared, partizan) && !ReferenceEquals(scenario.Waves, waves), "native Init receives independent quest wave and array");
        Check(prepared.ShallSpawn && prepared.TriggerType == "none" && !prepared.DependKarma && prepared.ForceSpawn && prepared.Delay == 0 && prepared.Time == -1,
            "override reaches initialization before chance and trigger parsing");
        Check(partizan.BossChance == 5 && partizan.DependKarma && partizan.TriggerName == "botEvent" && partizan.TriggerId == "PARTISAN_TRIGGER" && !partizan.ForceSpawn,
            "cached native source must remain untouched");
        Check(prepared.BossZone == "native-zone" && prepared.PerfectPos == 7 && prepared.ShowOnTarkovMap && prepared.ShowOnTarkovMapPvE,
            "native placement/display data preserved");
        Check(ReferenceEquals(scenario.Waves[1], other) && other.BossChance == 20, "other bosses remain unmodified");
        Check(PartizanQuestSpawn.Prepare(scenario.Waves).Length == 2, "repeated preparation cannot duplicate targets");
        foreach (var status in new[] { EQuestStatus.Success, EQuestStatus.Fail, EQuestStatus.AvailableForFinish })
        {
            quest.Status = status; session.SendRaidSettings(settings);
            Check(ReferenceEquals(PartizanQuestSpawn.Prepare(waves), waves), "inactive status clears previous quest selection: " + status);
        }
        quest.Status = EQuestStatus.Started; quest.CompletedConditions.Add(new MongoID(KnightQuestPolicy.AFavorKills));session.SendRaidSettings(settings);
        Check(ReferenceEquals(PartizanQuestSpawn.Prepare(waves), waves), "finished map objective cannot force target");
        quest.CompletedConditions.Clear(); settings.LocationId = "woods";session.SendRaidSettings(settings);
        Check(ReferenceEquals(PartizanQuestSpawn.Prepare(waves), waves), "other map clears previous selection");
        settings.LocationId = "lighthouse"; settings.IsPmc = false; session.SendRaidSettings(settings);
        Check(ReferenceEquals(PartizanQuestSpawn.Prepare(waves), waves), "Scav raids remain native");
        settings.IsPmc = true; GameplayModeRuntime.IsAllegiance = false; session.SendRaidSettings(settings);
        Check(ReferenceEquals(PartizanQuestSpawn.Prepare(waves), waves), "other mode remains native");
        GameplayModeRuntime.IsAllegiance = true; session.SendRaidSettings(settings);
        var missing = new[] { other };
        Check(ReferenceEquals(PartizanQuestSpawn.Prepare(missing), missing) && Logger.Errors == 1, "missing native target diagnoses without fabricating a wave");
        PartizanQuestSpawn.Reset();Check(ReferenceEquals(PartizanQuestSpawn.Prepare(waves), waves), "unload reset clears override");
        Console.WriteLine("PASS: " + checks + " Partizan quest selection/wave ownership Harmony checks; native spawning needs EFT qualification.");
    }
}
public struct MongoID
{
    private string value;
    public MongoID(string value) { this.value = value; }
}
namespace EFT
{
    public class Profile { public List<QuestData> QuestsData = new List<QuestData>(); }
    public class QuestData { public string Id; public EQuestStatus Status; public HashSet<MongoID> CompletedConditions = new HashSet<MongoID>(); }
    public class RaidSettings { public bool IsPmc; public string LocationId; }
    public class EftClientBackendSession
    {
        public Profile Profile = new Profile();
        [MethodImpl(MethodImplOptions.NoInlining)] public void SendRaidSettings(RaidSettings settings) { }
    }
}
namespace EFT.Quests { public enum EQuestStatus { Started, Success, Fail, AvailableForFinish } }
public class BossLocationSpawn
{
    public string BossName, BossZone, TriggerName, TriggerId, TriggerType;
    public float BossChance, Time, Delay;
    public bool DependKarma, ForceSpawn, ShowOnTarkovMap, ShowOnTarkovMapPvE, ShallSpawn;
    public int? PerfectPos;
    public BossLocationSpawn Copy() => (BossLocationSpawn)MemberwiseClone();
}
public class BossSpawnScenario
{
    public BossLocationSpawn[] Waves;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Init(BossLocationSpawn[] bossWaves)
    {
        Waves = bossWaves;
        foreach (var wave in Waves) { wave.ShallSpawn = wave.BossChance == 100; wave.TriggerType = wave.TriggerName; }
    }
}
namespace pitTeam.Modules
{
    internal static class GameplayModeRuntime { internal static bool IsAllegiance; }
    internal static class Logger { internal static int Errors; internal static void LogInfo(string text) { } internal static void LogError(string text) { Errors++; } }
}
