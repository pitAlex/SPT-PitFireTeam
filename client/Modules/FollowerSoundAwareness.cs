using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using pitTeam.Components;
using UnityEngine;

namespace pitTeam.Modules;

// Core peaceful orientation. The addon retains its native heard-preparation owner.
// Sound grants a bearing, never an enemy, visibility, a route, or permission to fire.
internal static class FollowerSoundAwareness
{
    internal const float LocalRange = 25f;
    private sealed class State
    {
        public State() { }
        internal Player Contact;
        internal Vector3 Point, AttentionAnchor;
        internal float Until;
        internal bool Shot;
        internal readonly HashSet<string> Ignored = new();
    }
    private static ConditionalWeakTable<BotOwner, State> states = new();
    internal static void ClearRaid() => states = new();
    internal static bool Owns(BotOwner owner) => owner != null && BossPlayers.IsFollower(owner) &&
        !pitFireTeam.UseSainFollowerCombat(owner);
    private static bool CanOrient(BotOwner owner, out BotFollowerPlayer follower)
    {
        follower = BossPlayers.Instance?.GetFollower(owner);
        if (!Owns(owner) || follower == null || owner.IsDead || owner.BotState != EBotState.Active ||
            SainAddonBridge.HasAcceptedGoalEnemy(owner) || SainAddonBridge.IsUsingMedical(owner) ||
            follower.IsBackpackInspectionActive || FollowerEnemyEnforceSuppression.IsSuppressed(owner)) return false;
        return !follower.TryPeekActiveCommand(out var command, out _, out _) ||
            command is FollowerCommandType.HoldPosition or FollowerCommandType.MoveToPoint or
                FollowerCommandType.ComeCloser or FollowerCommandType.RegroupNearBoss;
    }
    private static bool Hostile(BotOwner owner, Player source) => source?.HealthController?.IsAlive == true &&
        source.gameObject.activeInHierarchy && !BossPlayers.IsPlayerBoss(source.ProfileId) &&
        !BossPlayers.IsFollowerProfileId(source.ProfileId) &&
        (owner.BotsGroup?.IsEnemy(source) == true || owner.BotsGroup?.IsPlayerEnemy(source) == true ||
         Utils.FollowerAwareness.IsHostileToBossGroupForReaction(owner, source));

    internal static bool IsIgnored(BotOwner owner, Player source)
    {
        if (!states.TryGetValue(owner, out var state)) return false;
        if (owner.BotFollower?.BossToFollow is pitAIBossPlayer boss)
        {
            float radius = CombatDistanceConfiguration.Instance.GetRegroupBossMoveRefreshDistance();
            if ((boss.realPlayer.Position - state.AttentionAnchor).sqrMagnitude > radius * radius) state.Ignored.Clear();
        }
        return source != null && state.Ignored.Contains(source.ProfileId);
    }

    internal static void Attention(BotOwner owner)
    {
        if (!Owns(owner) || owner.BotFollower?.BossToFollow is not pitAIBossPlayer boss) return;
        var state = states.GetOrCreateValue(owner);
        IsIgnored(owner, state.Contact);
        state.AttentionAnchor = boss.realPlayer.Position;
        if (state.Contact != null && state.Until > Time.time) state.Ignored.Add(state.Contact.ProfileId);
        if (owner.EnemiesController?.EnemyInfos != null)
            foreach (var enemy in owner.EnemiesController.EnemyInfos.Values)
                if (enemy != null) state.Ignored.Add(enemy.ProfileId);
        state.Contact = null;
        state.Until = 0f;
    }

    internal static void Observe(BotOwner owner, Player source, Vector3 position, bool shot, bool audible)
    {
        if (!audible || !CanOrient(owner, out _) || !Hostile(owner, source) ||
            !FollowerEnemyTracking.IsFinite(position)) return;
        if (shot ? !FacesSquad(owner, position, source.LookDirection) :
            (position - owner.Position).sqrMagnitude > LocalRange * LocalRange || IsIgnored(owner, source)) return;
        var state = states.GetOrCreateValue(owner);
        if (state.Until > Time.time && ((state.Shot && !shot) ||
            (state.Contact != source && state.Shot == shot &&
             (position - owner.Position).sqrMagnitude > (state.Point - owner.Position).sqrMagnitude + 16f))) return;
        state.Contact = source;
        state.Point = position + (shot ? Vector3.zero : Vector3.up);
        state.Shot = shot;
        state.Until = Time.time + 3f;
    }

    internal static bool Faces(Vector3 source, Vector3 facing, Vector3 target)
    {
        Vector3 delta = target - source;
        return delta.sqrMagnitude >= 0.01f && facing.sqrMagnitude >= 0.01f &&
            Vector3.Dot(facing.normalized, delta.normalized) >= 0.8660254f;
    }
    private static bool FacesSquad(BotOwner owner, Vector3 source, Vector3 facing)
    {
        if (Faces(source, facing, owner.Position + Vector3.up)) return true;
        if (owner.BotFollower?.BossToFollow is not pitAIBossPlayer boss) return false;
        if (Faces(source, facing, boss.realPlayer.Position + Vector3.up)) return true;
        foreach (var member in boss.Followers)
            if (member != null && !member.IsDead && member.BotState == EBotState.Active &&
                Faces(source, facing, member.Position + Vector3.up)) return true;
        return false;
    }
    internal static void ApplyLook(BotOwner owner)
    {
        if (owner == null || !states.TryGetValue(owner, out var state) || state.Until <= Time.time) return;
        if (!CanOrient(owner, out var follower) || !Hostile(owner, state.Contact) ||
            (!state.Shot && IsIgnored(owner, state.Contact)))
        { state.Contact = null; state.Until = 0f; return; }
        if (follower.TryGetCommandLookOverride(out _) || owner.Mover?.Sprinting == true) return;
        owner.Steering.LookToPoint(state.Point);
    }
}
