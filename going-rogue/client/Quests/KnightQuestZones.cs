using System;
using System.IO;
using System.Linq;
using EFT;
using EFT.Interactive;
using EFT.Quests;
using HarmonyLib;
using Newtonsoft.Json;
using pitTeam.Modules;
using UnityEngine;
using Object = UnityEngine.Object;
using Logger = pitTeam.Modules.Logger;

namespace pitTeam.GoingRogue.Quests;

internal static class KnightQuestZones
{
    private static GameWorld owner;
    private static GameObject ownedZone;

    internal static void Ensure(GameWorld world)
    {
        var player = world?.MainPlayer;
        if (!GameplayModeRuntime.IsAllegiance || player?.IsYourPlayer != true || player.Side == EPlayerSide.Savage ||
            !string.Equals(player.Location, "shoreline", StringComparison.OrdinalIgnoreCase) ||
            player.Profile?.QuestsData?.Any(q => q.Id == KnightQuestPolicy.OffLimits &&
                (q.Status == EQuestStatus.Started || q.Status == EQuestStatus.AvailableForFinish)) != true) return;
        if (ReferenceEquals(owner, world) && ownedZone != null) return;
        Reset();
        // A map or another plugin may already supply this native zone. Reuse it
        // without claiming ownership or destroying it during addon cleanup.
        if (Object.FindObjectsOfType<TriggerWithId>().Any(zone => zone.isActiveAndEnabled && zone.Id == KnightQuestPolicy.PierZone))
        {
            owner = world;
            Logger.LogInfo("[GoingRogueQuests] Reusing native pier zone=" + KnightQuestPolicy.PierZone);
            return;
        }
        int layer = LayerMask.NameToLayer("Triggers");
        if (layer < 0) throw new InvalidOperationException("Native Triggers layer is missing");
        using var stream = typeof(KnightQuestZones).Assembly.GetManifestResourceStream(
            "pitFireTeam.GoingRogue.Resources.quests.FriendlyPMC_Zones.json")
            ?? throw new InvalidDataException("Missing embedded Knight quest zones");
        using var reader = new StreamReader(stream);
        var definition = JsonConvert.DeserializeObject<ZoneDefinition[]>(reader.ReadToEnd())
            .Single(zone => zone.ZoneId == KnightQuestPolicy.PierZone && zone.ZoneType == "botkillzone" &&
                string.Equals(zone.ZoneLocation, player.Location, StringComparison.OrdinalIgnoreCase));
        owner = world;
        ownedZone = new GameObject("GoingRogue." + definition.ZoneId);
        try
        {
            ownedZone.SetActive(false);
            ownedZone.layer = layer;
            ownedZone.transform.SetParent(world.transform, false);
            ownedZone.transform.position = definition.Position.Vector;
            ownedZone.transform.rotation = new Quaternion(definition.Rotation.X, definition.Rotation.Y, definition.Rotation.Z, definition.Rotation.W);
            ownedZone.transform.localScale = definition.Scale.Vector;
            var collider = ownedZone.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = Vector3.one;
            collider.isTrigger = true;
            ownedZone.AddComponent<QuestTrigger>().SetId(definition.ZoneId);
            ownedZone.SetActive(true);
            Logger.LogInfo("[GoingRogueQuests] Created native pier zone=" + definition.ZoneId);
        }
        catch { Reset(); throw; }
    }

    internal static void Reset(GameWorld world = null)
    {
        if (!ReferenceEquals(world, null) && !ReferenceEquals(owner, world)) return;
        if (ownedZone != null)
        {
            ownedZone.SetActive(false);
            Object.Destroy(ownedZone);
        }
        ownedZone = null;
        owner = null;
    }

    private sealed class ZoneDefinition
    {
        public string ZoneId { get; set; }
        public string ZoneLocation { get; set; }
        public string ZoneType { get; set; }
        public ZoneVector Position { get; set; }
        public ZoneVector Rotation { get; set; }
        public ZoneVector Scale { get; set; }
    }
    private sealed class ZoneVector
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }
        internal Vector3 Vector => new Vector3(X, Y, Z);
    }
}

[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
internal static class KnightQuestZoneStartPatch
{
    private static void Postfix(GameWorld __instance)
    {
        try { KnightQuestZones.Ensure(__instance); }
        catch (Exception ex) { Logger.LogError("[GoingRogueQuests] Pier zone setup failed: " + ex); }
    }
}

[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.Dispose), new Type[] { })]
internal static class KnightQuestZoneDisposePatch
{
    private static void Prefix(GameWorld __instance) => KnightQuestZones.Reset(__instance);
}
