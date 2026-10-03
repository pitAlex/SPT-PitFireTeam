using System;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN.Classes.Bot.Sense.Hearing;
using SAIN.Components;
using SAIN.Components.PlayerComponentSpace;
using SAIN.Extensions;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.SAINAddon;

// Orientation only. Observe SAIN's actual audibility result, before its separate
// chase-gunshot policy. Never rerun hearing, acquire a goal or issue movement.
internal sealed class SainFollowerSoundAwareness
{
    internal const float LocalRange = 25f;
    private const float HoldSeconds = 3f;
    private const float FacingDot = 0.8660254f; // 30-degree half cone, including elevation.
    private readonly BotComponent bot;
    private Enemy contact;
    private Vector3 point;
    private float until, nextRecord;
    private bool directedShot, reportedFailure;
    internal Enemy? Contact => until > Time.time ? contact : null;

    internal SainFollowerSoundAwareness(BotComponent bot) => this.bot = bot;

    internal static void Install(Harmony harmony)
    {
        var heard = AccessTools.Method(typeof(HearingAnalysis), nameof(HearingAnalysis.CheckIfSoundHeard),
            new[] { typeof(AISoundData) });
        if (heard?.ReturnType != typeof(bool) || heard.IsStatic)
            throw new MissingMethodException("SAIN personal audibility boundary changed.");
        harmony.Patch(heard, postfix: new HarmonyMethod(typeof(SainFollowerSoundAwareness), nameof(AfterHearing)));

        foreach (string name in new[] { "FollowAction", "GestureCommandAction" })
        {
            Type type = typeof(pitFireTeam).Assembly.GetType("pitTeam.BigBrain.Actions." + name, true);
            var update = AccessTools.DeclaredMethod(type, "Update", new[] { typeof(CustomLayer.ActionData) });
            if (update?.ReturnType != typeof(void) || !typeof(CustomLogic).IsAssignableFrom(type))
                throw new MissingMethodException("Core peaceful action update boundary changed: " + name);
            harmony.Patch(update, postfix: new HarmonyMethod(typeof(SainFollowerSoundAwareness), nameof(AfterPeacefulUpdate)));
        }
    }

    private static void AfterHearing(AISoundData __0, bool __result) =>
        SAINFollowerRuntime.GetSoundAwareness(__0.Bot?.BotOwner)?.Observe(__0, __result);

    private static void AfterPeacefulUpdate(CustomLogic __instance) =>
        SAINFollowerRuntime.GetSoundAwareness(__instance.BotOwner)?.ApplyLook();

    internal static bool IsLocalSound(SAINSoundType? type) => type is
        SAINSoundType.Conversation or SAINSoundType.Pain or SAINSoundType.Breathing or
        SAINSoundType.FootStep or SAINSoundType.Sprint or SAINSoundType.Prone or
        SAINSoundType.Jump or SAINSoundType.Land or SAINSoundType.GearSound or SAINSoundType.Bush;

    private bool CanOrient(out BotFollowerPlayer follower)
    {
        BotOwner owner = bot.BotOwner;
        follower = BossPlayers.Instance?.GetFollower(owner);
        if (follower == null || !owner.IsBotActive() || bot.SAINLayersActive ||
            SAINFollowerCombatHandoff.AllowsEnemyCombat(owner) ||
            SainAddonBridge.IsUsingMedical(owner) || follower.IsBackpackInspectionActive ||
            FollowerEnemyEnforceSuppression.IsSuppressed(owner)) return false;
        if (!follower.TryPeekActiveCommand(out var command, out _, out _)) return true;
        return command is FollowerCommandType.HoldPosition or FollowerCommandType.MoveToPoint or
            FollowerCommandType.ComeCloser or FollowerCommandType.RegroupNearBoss;
    }

    internal void Observe(AISoundData sound, bool audible)
    {
        try
        {
            if (!ReferenceEquals(sound.Bot, bot) || (!sound.IsGunShot && !IsLocalSound(sound.SoundType))) return;
            Enemy enemy = sound.Enemy;
            if (enemy?.EnemyPlayer?.HealthController?.IsAlive != true || !Enemy.IsEnemyActive(enemy) ||
                bot.BotOwner.BotsGroup == null ||
                !(bot.BotOwner.BotsGroup.IsEnemy(enemy.EnemyPlayer) || bot.BotOwner.BotsGroup.IsPlayerEnemy(enemy.EnemyPlayer))) return;
            if (!audible) { Record(sound, "inaudible"); return; }
            if (!CanOrient(out _)) { Record(sound, "otherOwner"); return; }
            if (!sound.IsGunShot && (sound.PlayerDistance > LocalRange ||
                (sound.Position - bot.Position).sqrMagnitude > LocalRange * LocalRange))
            { Record(sound, "outsideLocalRange"); return; }
            if (!sound.IsGunShot && SAINFollowerRuntime.IsAttentionContactIgnored(bot.BotOwner, enemy))
            { Record(sound, "attentionIgnored"); return; }
            if (sound.IsGunShot && !FacesSquad(sound.Position, enemy.EnemyPlayer.LookDirection))
            { Record(sound, "shotAwayFromSquad"); return; }

            // A burst keeps its bearing; incidental movement cannot distract from it.
            if (until > Time.time && ((directedShot && !sound.IsGunShot) ||
                (!ReferenceEquals(contact, enemy) && directedShot == sound.IsGunShot &&
                 (sound.Position - bot.Position).sqrMagnitude > (point - bot.Position).sqrMagnitude + 16f)))
            { Record(sound, "higherPrioritySound"); return; }
            contact = enemy;
            // Use the emitted position, never follow the unheard actor's live position.
            point = sound.Position + (sound.IsGunShot ? Vector3.zero : Vector3.up);
            until = Time.time + HoldSeconds;
            directedShot = sound.IsGunShot;
            Record(sound, "facingHold");
        }
        catch (Exception ex) { Fail(ex); }
    }

    private bool FacesSquad(Vector3 source, Vector3 facing)
    {
        if (Faces(source, facing, bot.Position + Vector3.up)) return true;
        if (SainPlayerSquadBridge.TryGetPlayerLeader(bot.BotOwner, out Player leader) &&
            Faces(source, facing, leader.Position + Vector3.up)) return true;
        if (bot.BotOwner.BotFollower?.BossToFollow is pitAIBossPlayer boss)
            foreach (BotOwner member in boss.Followers)
                if (member != null && member != bot.BotOwner && member.IsBotActive() &&
                    Faces(source, facing, member.Position + Vector3.up)) return true;
        return false;
    }

    internal static bool Faces(Vector3 source, Vector3 facing, Vector3 target)
    {
        Vector3 delta = target - source;
        if (delta.sqrMagnitude < 0.01f || facing.sqrMagnitude < 0.01f) return false;
        return Vector3.Dot(facing.normalized, delta.normalized) >= FacingDot;
    }

    internal void ApplyLook()
    {
        try
        {
            if (until <= Time.time || contact == null) return;
            if (contact.EnemyPlayer?.HealthController?.IsAlive != true || !Enemy.IsEnemyActive(contact) ||
                !CanOrient(out var follower) ||
                bot.BotOwner.BotsGroup == null ||
                !(bot.BotOwner.BotsGroup.IsEnemy(contact.EnemyPlayer) || bot.BotOwner.BotsGroup.IsPlayerEnemy(contact.EnemyPlayer)) ||
                (!directedShot && SAINFollowerRuntime.IsAttentionContactIgnored(bot.BotOwner, contact)))
            { Clear(); return; }
            if (follower.TryGetCommandLookOverride(out _) || bot.BotOwner.Mover?.Sprinting == true) return;
            bot.BotOwner.Steering.LookToPoint(point);
        }
        catch (Exception ex) { Fail(ex); }
    }

    internal void Clear() { contact = null; until = 0f; directedShot = false; }

    private void Record(AISoundData sound, string reason)
    {
        if (!SainCombatRecorderBridge.IsRecording || Time.time < nextRecord) return;
        nextRecord = Time.time + 1f;
        SainCombatRecorderBridge.RecordEvent(bot.BotOwner, "sainSoundReaction", new {
            reason, sound = sound.SoundType.ToString(), enemyId = sound.Enemy?.EnemyProfileId,
            distance = sound.PlayerDistance, position = SAINFollowerRecorder.Point(sound.Position),
            until = reason == "facingHold" ? until : 0f
        });
    }

    private void Fail(Exception ex)
    {
        Clear();
        if (reportedFailure) return;
        reportedFailure = true;
        pitTeam.Modules.Logger.LogError($"[SAIN] Sound facing failed; preserving normal action ownership. {ex}");
    }
}
