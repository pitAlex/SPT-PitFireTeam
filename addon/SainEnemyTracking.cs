using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using pitTeam.Modules;
using SAIN.Classes.Bot.Search;
using SAIN.Components.BotComponentSpace.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.SAINAddon;

// Only tactical consumers receive the projection. Native sensors, report creation,
// squad transmission, suppression and genuine known places remain untouched.
internal static partial class SainEnemyTracking
{
    private static readonly FieldInfo PlacesEnemy = AccessTools.Field(typeof(EnemyKnownPlaces), "Enemy");
    private static readonly FieldInfo CheckerEnemy = AccessTools.Field(typeof(EnemyKnownChecker), "Enemy");
    private static readonly Dictionary<MethodInfo, MethodInfo> Reads = new()
    {
        [AccessTools.PropertyGetter(typeof(Enemy), nameof(Enemy.LastKnownPosition))] = AccessTools.Method(typeof(SainEnemyTracking), nameof(Position)),
        [AccessTools.PropertyGetter(typeof(EnemyKnownPlaces), nameof(EnemyKnownPlaces.LastKnownPosition))] = AccessTools.Method(typeof(SainEnemyTracking), nameof(KnownPosition)),
        [AccessTools.PropertyGetter(typeof(EnemyPlace), nameof(EnemyPlace.Position))] = AccessTools.Method(typeof(SainEnemyTracking), nameof(PlacePosition)),
        [AccessTools.PropertyGetter(typeof(EnemyPlace), nameof(EnemyPlace.HasArrivedPersonal))] = AccessTools.Method(typeof(SainEnemyTracking), nameof(ArrivedPersonal)),
        [AccessTools.PropertyGetter(typeof(EnemyPlace), nameof(EnemyPlace.HasArrivedSquad))] = AccessTools.Method(typeof(SainEnemyTracking), nameof(ArrivedSquad)),
        [AccessTools.PropertyGetter(typeof(EnemyPlace), nameof(EnemyPlace.DistanceToBot))] = AccessTools.Method(typeof(SainEnemyTracking), nameof(PlaceDistance)),
        [AccessTools.Method(typeof(EnemyPlace), nameof(EnemyPlace.EnemyHeadAtPosition))] = AccessTools.Method(typeof(SainEnemyTracking), nameof(PlaceHead)),
        [AccessTools.Method(typeof(EnemyPlace), nameof(EnemyPlace.Distance))] = AccessTools.Method(typeof(SainEnemyTracking), nameof(PlaceDistanceTo)),
        [AccessTools.Method(typeof(EnemyPlace), nameof(EnemyPlace.DistanceSqr))] = AccessTools.Method(typeof(SainEnemyTracking), nameof(PlaceDistanceSqrTo)),
    };

    private static Vector3? KnownPosition(EnemyKnownPlaces places) =>
        PlacesEnemy.GetValue(places) is Enemy enemy && Simple(enemy) ? enemy.EnemyPosition : places.LastKnownPosition;
    private static Vector3 PlacePosition(EnemyPlace place) => Simple(place.PlaceData.OwnerEnemy)
        ? place.PlaceData.OwnerEnemy.EnemyPosition : place.Position;
    private static float PlaceDistance(EnemyPlace place) => Simple(place.PlaceData.OwnerEnemy)
        ? (PlacePosition(place) - place.PlaceData.Owner.Position).magnitude : place.DistanceToBot;
    private static bool ArrivedPersonal(EnemyPlace place) => !Simple(place.PlaceData.OwnerEnemy) && place.HasArrivedPersonal;
    private static bool ArrivedSquad(EnemyPlace place) => !Simple(place.PlaceData.OwnerEnemy) && place.HasArrivedSquad;
    private static Vector3 PlaceHead(EnemyPlace place) => Simple(place.PlaceData.OwnerEnemy)
        ? PlacePosition(place) + Vector3.up * 1.5f : place.EnemyHeadAtPosition();
    private static float PlaceDistanceTo(EnemyPlace place, Vector3 point) => Simple(place.PlaceData.OwnerEnemy)
        ? (PlacePosition(place) - point).magnitude : place.Distance(point);
    private static float PlaceDistanceSqrTo(EnemyPlace place, Vector3 point) => Simple(place.PlaceData.OwnerEnemy)
        ? (PlacePosition(place) - point).sqrMagnitude : place.DistanceSqr(point);

    internal static void Apply(Harmony harmony)
    {
        if (PlacesEnemy == null || CheckerEnemy == null || Reads.Keys.Any(x => x == null))
            throw new MissingMemberException("SAIN tracking metadata changed.");
        // Explicit tactical owners; intentionally exclude Vision, Hearing, WeaponFunction,
        // EnemyKnownPlaces, reports and Enemy's blind-fire target production.
        string[] types = {
            "SAIN.SAINComponent.Classes.EnemyClasses.SAINEnemyPath",
            "SAIN.Classes.Bot.Search.SearchDecider", "SAIN.Classes.Bot.Search.SearchPathFinder",
            "SAIN.Classes.Bot.Search.SearchClass",
            "SAIN.SAINComponent.Classes.Mover.DogFight",
            "SAIN.SAINComponent.Classes.Mover.SAINSteeringClass",
            "SAIN.SAINComponent.Classes.Mover.RandomLookClass",
            "SAIN.SAINComponent.Classes.Decision.FiringPositionFinder",
            "SAIN.Components.CoverFinder.CoverFinderComponent",
            "SAIN.Layers.Combat.Solo.SearchAction", "SAIN.Layers.Combat.Solo.MoveToEngageAction",
            "SAIN.Layers.Combat.Solo.RushEnemyAction", "SAIN.Layers.Combat.Solo.DogFightAction",
            "SAIN.Layers.Combat.Solo.Cover.SeekCoverAction"
        };
        foreach (string name in types)
        {
            var type = typeof(Enemy).Assembly.GetType(name, true);
            PatchType(harmony, type);
        }
        harmony.Patch(AccessTools.Method(typeof(EnemyKnownChecker), "ShallKnowEnemy"),
            prefix: new HarmonyMethod(typeof(SainEnemyTracking), nameof(KnownPrefix)));
        harmony.Patch(AccessTools.Method(typeof(SearchPathFinder), "checkFinishedSearch"),
            prefix: new HarmonyMethod(typeof(SainEnemyTracking), nameof(FinishSearchPrefix)));
        harmony.Patch(AccessTools.Method(typeof(Enemy), "FindLookPoint"),
            transpiler: new HarmonyMethod(typeof(SainEnemyTracking), nameof(ProjectReads)));
    }

    private static void PatchType(Harmony harmony, Type type)
    {
        foreach (var method in AccessTools.GetDeclaredMethods(type))
        {
            if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
            // Harmony's instruction reader handles iterator MoveNext as well as ordinary methods.
            if (PatchProcessor.GetOriginalInstructions(method).Any(i => i.operand is MethodInfo m && Reads.ContainsKey(m)))
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(SainEnemyTracking), nameof(ProjectReads)));
        }
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) PatchType(harmony, nested);
    }

    private static IEnumerable<CodeInstruction> ProjectReads(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                instruction.operand is MethodInfo member && Reads.TryGetValue(member, out var projection))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = projection;
            }
            yield return instruction;
        }
    }

    private static bool KnownPrefix(EnemyKnownChecker __instance, float currentTime, ref float forgetEnemyTime,
        ref bool __result)
    {
        if (CheckerEnemy.GetValue(__instance) is not Enemy enemy || !pitFireTeam.UseSainFollowerCombat(enemy.BotOwner)) return true;
        forgetEnemyTime = FollowerEnemyTracking.RememberSeconds;
        if (FollowerEnemyTracking.Mode != EnemyTrackingMode.Simple ||
            !ReferenceEquals(enemy.EnemyInfo, enemy.BotOwner.Memory?.GoalEnemy)) return true;
        __result = Enemy.IsEnemyActive(enemy) && enemy.KnownPlaces.LastKnownPlace != null &&
            currentTime - enemy.KnownPlaces.TimeLastKnownUpdated <= forgetEnemyTime;
        return false;
    }

    private static bool FinishSearchPrefix(Enemy enemy) => !Simple(enemy);
}
