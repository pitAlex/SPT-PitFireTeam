using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace pitTeam.Modules;

public static partial class SainGoalEnemyBridge
{
    private static Type trackingPlacesType;
    private static MethodInfo trackingSeenMethod, trackingHeardMethod;
    private static FieldInfo trackingPositionField, trackingSoundField, trackingPlaceField;
    private static object trackingDefaultSound, trackingHearingPlace;
    private static bool trackingPlacesReady;

    internal static bool TryReadTrackingReport(EnemyInfo expected, object nativeEnemy, out Vector3 point, out float at)
    {
        point = default; at = -1f;
        if (!pitFireTeam.IsSAINInstalled || nativeEnemy == null || !FollowerEnemyTracking.IsRealistic(expected)) return false;
        try
        {
            ResolveEnemyAccessors(nativeEnemy.GetType());
            if (!ReferenceEquals(enemyInfoProperty?.GetValue(nativeEnemy), expected) ||
                enemyProfileIdProperty?.GetValue(nativeEnemy) as string != expected.ProfileId) return false;
            var places = enemyKnownPlacesProperty?.GetValue(nativeEnemy);
            if (places == null) return false;
            placesLastKnownProperty ??= AccessTools.Property(places.GetType(), "LastKnownPlace");
            placesTimeProperty ??= AccessTools.Property(places.GetType(), "TimeLastKnownUpdated");
            var place = placesLastKnownProperty?.GetValue(places);
            if (place == null || placesTimeProperty?.GetValue(places) is not float time) return false;
            placePositionProperty ??= AccessTools.Property(place.GetType(), "Position");
            if (placePositionProperty?.GetValue(place) is not Vector3 position || !IsFinite(position) ||
                float.IsNaN(time) || float.IsInfinity(time) || time < 0f || time > Time.time) return false;
            point = position; at = time; return true;
        }
        catch (Exception ex) { LogAccessorFailureOnce(ex); return false; }
    }

    // Updates an existing accepted contact only. Unlike TrySyncEnemyState this does
    // not add/select an enemy, call a decision provider or broadcast a squad report.
    internal static bool TrySyncTrackingReport(EnemyInfo expected, Vector3 point, float observedAt,
        bool personalSight, object nativeEnemy = null, object soundType = null)
    {
        var owner = expected?.Owner;
        if (!pitFireTeam.IsSAINInstalled || !FollowerEnemyTracking.IsRealistic(expected) ||
            !FollowerEnemyTracking.IsFinite(point) || float.IsNaN(observedAt) || float.IsInfinity(observedAt) ||
            observedAt < 0f || observedAt > Time.time ||
            expected?.Person?.HealthController?.IsAlive != true ||
            owner?.EnemiesController?.EnemyInfos == null ||
            !owner.EnemiesController.EnemyInfos.TryGetValue(expected.Person, out var accepted) ||
            !ReferenceEquals(accepted, expected)) return false;
        try
        {
            if (nativeEnemy == null && !TryGetExactSainGoalEnemy(owner, expected, out nativeEnemy)) return false;
            ResolveEnemyAccessors(nativeEnemy.GetType());
            if (!ReferenceEquals(enemyInfoProperty?.GetValue(nativeEnemy), expected) ||
                enemyProfileIdProperty?.GetValue(nativeEnemy) as string != expected.ProfileId) return false;
            var places = enemyKnownPlacesProperty?.GetValue(nativeEnemy);
            if (places == null) return false;
            placesTimeProperty ??= AccessTools.Property(places.GetType(), "TimeLastKnownUpdated");
            if (placesTimeProperty?.GetValue(places) is not float nativeTime ||
                float.IsNaN(nativeTime) || float.IsInfinity(nativeTime)) return false;
            // Equal observations are not new events. Native hearing may already have
            // published this exact dispersed report before its reaction returns.
            if (nativeTime >= observedAt) return true;
            if (trackingPlacesType != places.GetType())
            {
                trackingPlacesReady = false;
                trackingPlacesType = places.GetType();
                trackingSeenMethod = AccessTools.Method(trackingPlacesType, "UpdateSeenPlace", new[] { typeof(Vector3), typeof(float) });
                trackingHeardMethod = AccessTools.Method(trackingPlacesType, "UpdatePersonalHeardPosition");
                var reportType = trackingHeardMethod?.GetParameters();
                if (reportType?.Length != 2 || reportType[1].ParameterType != typeof(float))
                    throw new MissingMethodException("SAIN heard-place update signature changed");
                trackingPositionField = AccessTools.Field(reportType[0].ParameterType, "position");
                trackingSoundField = AccessTools.Field(reportType[0].ParameterType, "soundType");
                trackingPlaceField = AccessTools.Field(reportType[0].ParameterType, "placeType");
                if (trackingSeenMethod == null || trackingPositionField?.FieldType != typeof(Vector3) ||
                    trackingSoundField?.FieldType.IsEnum != true || trackingPlaceField?.FieldType.IsEnum != true)
                    throw new MissingMemberException("SAIN tracking report layout changed");
                trackingDefaultSound = Enum.Parse(trackingSoundField.FieldType, "Conversation");
                trackingHearingPlace = Enum.Parse(trackingPlaceField.FieldType, "Hearing");
                trackingPlacesReady = true;
            }
            if (!trackingPlacesReady) return false;
            if (personalSight)
                trackingSeenMethod.Invoke(places, new object[] { point, observedAt });
            else
            {
                var report = Activator.CreateInstance(trackingPositionField.DeclaringType);
                trackingPositionField.SetValue(report, point);
                trackingSoundField.SetValue(report, soundType != null && soundType.GetType() == trackingSoundField.FieldType
                    ? soundType : trackingDefaultSound);
                trackingPlaceField.SetValue(report, trackingHearingPlace);
                trackingHeardMethod.Invoke(places, new[] { report, (object)observedAt });
            }
            return true;
        }
        catch (Exception ex) { LogAccessorFailureOnce(ex); return false; }
    }
}
