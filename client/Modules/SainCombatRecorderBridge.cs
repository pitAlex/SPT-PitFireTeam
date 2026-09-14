using System;
using System.Diagnostics;
using EFT;
using UnityEngine;

namespace pitTeam.Modules;

// Data only: core has no SAIN assembly dependency and the recorder never drives AI.
public sealed class SainCombatSnapshot
{
    public bool InCombat;
    public bool ControlsMovement;
    public bool HasPath, Moving, Running, Arrived;
    public Vector3? Destination;
    public object? Cover;
    public object? Details;
}

public static class SainCombatRecorderBridge
{
    private static Func<BotOwner, SainCombatSnapshot?>? capture;
    private static Func<BotOwner, bool>? isActive;
    private static bool reportedFailure;
    public static bool IsRecording => BattleRecorder.IsAddonRecording();

    [Conditional("DEBUG")]
    public static void Register(Func<BotOwner, SainCombatSnapshot?> provider, Func<BotOwner, bool> active)
    {
        capture = provider;
        isActive = active;
        reportedFailure = false;
    }

    [Conditional("DEBUG")]
    public static void Unregister(Func<BotOwner, SainCombatSnapshot?> provider, Func<BotOwner, bool> active)
    {
        if (capture == provider) capture = null;
        if (isActive == active) isActive = null;
    }

    public static bool IsActive(BotOwner owner)
    {
        try { return isActive?.Invoke(owner) == true; }
        catch { return false; }
    }

    public static SainCombatSnapshot? Capture(BotOwner owner)
    {
        try { return capture?.Invoke(owner); }
        catch (Exception ex)
        {
            if (!reportedFailure) { reportedFailure = true; Logger.LogError($"[SAIN] Recorder snapshot failed: {ex}"); }
            return null;
        }
    }

    [Conditional("DEBUG")]
    public static void RecordState(BotOwner owner, bool active, string reason)
    {
        try { BattleRecorder.RecordAddonCombatState(owner, active, reason); }
        catch (Exception ex)
        {
            if (!reportedFailure) { reportedFailure = true; Logger.LogError($"[SAIN] Recorder state failed: {ex}"); }
        }
    }

    [Conditional("DEBUG")]
    public static void RecordEvent(BotOwner owner, string kind, object details)
    {
        // Diagnostic callbacks must never abort SAIN's event delivery or movement.
        try { BattleRecorder.RecordAddonEvent(owner, kind, details); }
        catch (Exception ex)
        {
            if (!reportedFailure) { reportedFailure = true; Logger.LogError($"[SAIN] Recorder event failed: {ex}"); }
        }
    }
}
