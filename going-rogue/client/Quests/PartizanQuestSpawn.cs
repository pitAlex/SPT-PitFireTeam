using System;
using System.Linq;
using EFT;
using EFT.Quests;
using HarmonyLib;
using pitTeam.Modules;

namespace pitTeam.GoingRogue.Quests;

// Change a raid-local copy before native Init parses triggers and prepares bot profiles.
// Native placement, generation and Partizan's combat brain remain their existing owners.
internal static class PartizanQuestSpawn
{
    private static bool active;
    internal static void Reset() => active = false;

    internal static void Select(Profile profile, RaidSettings settings)
    {
        active = GameplayModeRuntime.IsAllegiance && settings?.IsPmc == true &&
            profile?.QuestsData?.Any(q => q.Id == KnightQuestPolicy.AFavor && q.Status == EQuestStatus.Started &&
                KnightQuestPolicy.NeedsCompanion(q.Id, settings.LocationId,
                    objective => q.CompletedConditions?.Contains(new MongoID(objective)) == true)) == true;
    }

    internal static BossLocationSpawn[] Prepare(BossLocationSpawn[] waves)
    {
        if (!active || !GameplayModeRuntime.IsAllegiance || waves == null) return waves;
        int index = Array.FindIndex(waves, wave => wave?.BossName == "bossPartisan");
        if (index < 0)
        {
            Logger.LogError("[GoingRogueQuests] Partizan hunt has no native Partizan wave; target was not injected.");
            return waves;
        }
        var result = (BossLocationSpawn[])waves.Clone();
        var source = waves[index];
        var target = source.Copy();
        target.PerfectPos = source.PerfectPos;
        target.ShowOnTarkovMap = source.ShowOnTarkovMap;
        target.ShowOnTarkovMapPvE = source.ShowOnTarkovMapPvE;
        target.BossChance = 100;
        target.DependKarma = false;
        target.TriggerName = "none";
        target.TriggerId = "";
        target.Time = -1;
        target.Delay = 0;
        target.ForceSpawn = true;
        result[index] = target;
        Logger.LogInfo("[GoingRogueQuests] Prepared native Partizan quest wave on Lighthouse; karma/event spawn gates bypassed for this raid.");
        return result;
    }
}

[HarmonyPatch(typeof(EftClientBackendSession), nameof(EftClientBackendSession.SendRaidSettings))]
internal static class PartizanQuestSelectionPatch
{
    private static void Postfix(EftClientBackendSession __instance, RaidSettings settings)
    {
        PartizanQuestSpawn.Reset();
        try { PartizanQuestSpawn.Select(__instance.Profile, settings); }
        catch (Exception ex) { Logger.LogError("[GoingRogueQuests] Partizan raid selection failed: " + ex); }
    }
}

[HarmonyPatch(typeof(BossSpawnScenario), nameof(BossSpawnScenario.Init))]
internal static class PartizanQuestWavePatch
{
    private static void Prefix(ref BossLocationSpawn[] bossWaves)
    {
        try { bossWaves = PartizanQuestSpawn.Prepare(bossWaves); }
        catch (Exception ex) { Logger.LogError("[GoingRogueQuests] Partizan wave preparation failed: " + ex); }
    }
}
