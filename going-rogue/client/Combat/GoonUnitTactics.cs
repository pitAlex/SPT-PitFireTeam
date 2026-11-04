using System.Collections.Generic;
using System.Linq;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;

namespace pitTeam.GoingRogue.Combat;

// Composition changes the runtime baseline, never the player's command override.
// The combat drivers still own orders, treatment, firing and movement publication.
internal static class GoonUnitTactics
{
    private sealed class Membership(BotFollowerPlayer follower, BotOwner knight)
    {
        internal readonly BotFollowerPlayer Follower = follower;
        internal readonly BotOwner Knight = knight;
        internal bool Applied;
    }
    private static readonly Dictionary<BotOwner, Membership> Members = new();
    private static readonly Dictionary<BotOwner, BotFollowerPlayer> MovementMembers = new();

    internal static void RefreshAll()
    {
        if (GameplayModeAddons.Current != GameplayMode.Allegiance) { Reset(); return; }
        var registered = BossPlayers.GetFollowers().Where(f => f.GetBoss()?.realPlayer?.IsYourPlayer == true &&
            f.GetBot() is { } bot && IsGoon(bot)).ToArray();
        var actors = registered.Where(f => f.GetBot() is { } bot &&
            !bot.IsDead && bot.BotState == EBotState.Active && bot.GetPlayer?.HealthController?.IsAlive == true &&
            IsGoon(bot) && GoonCombatLayer.IsOwned(bot, bot.Profile.Info.Settings.Role.ToString())).ToArray();
        var desired = new Dictionary<BotOwner, Membership>();
        foreach (var follower in actors)
        {
            var leader = follower.GetBoss();
            var knight = actors.FirstOrDefault(f => ReferenceEquals(f.GetBoss(), leader) &&
                f.GetBot().Profile.Info.Settings.Role == WildSpawnType.bossKnight)?.GetBot();
            if (knight == null || !actors.Any(f => ReferenceEquals(f.GetBoss(), leader) && f.GetBot() != knight)) continue;
            desired[follower.GetBot()] = new Membership(follower, knight);
        }
        foreach (var entry in Members.ToArray())
        {
            var old = entry.Value;
            if (desired.TryGetValue(entry.Key, out var next) && ReferenceEquals(next.Follower, old.Follower) && next.Knight == old.Knight)
            {
                if (!old.Applied) Apply(old);
                old.Applied = true;
                continue;
            }
            // Loading, loss of ownership or mode exit is not a confirmed death.
            bool knightDied = old.Knight.IsDead || old.Knight.GetPlayer?.HealthController?.IsAlive == false;
            if (entry.Key != old.Knight && knightDied)
                old.Follower.ResetCombatPercentageOverrides("KnightDied");
            if (old.Applied) old.Follower.ClearCompanionCombatPreferences();
            old.Applied = false;
            // Retain only the leader identity during a temporary gap. This catches a
            // later confirmed death without keeping the unit stance or renewing orders.
            if (!knightDied && next == null && GameplayModeAddons.Current == GameplayMode.Allegiance &&
                !entry.Key.IsDead && entry.Key.GetPlayer?.HealthController?.IsAlive != false &&
                registered.Contains(old.Follower) && registered.Any(f => f.GetBot() == old.Knight &&
                    ReferenceEquals(f.GetBoss(), old.Follower.GetBoss()))) continue;
            Members.Remove(entry.Key);
        }
        foreach (var entry in desired)
        {
            if (Members.ContainsKey(entry.Key)) continue;
            Apply(entry.Value);
            entry.Value.Applied = true;
            Members[entry.Key] = entry.Value;
        }
        // Movement succession is independent of Knight's combat-percentage baseline.
        // It also applies when a commander was never brought into this raid.
        foreach (var old in MovementMembers.ToArray())
            if (!actors.Contains(old.Value))
            { old.Value.SetCompanionCombatLeader(null); MovementMembers.Remove(old.Key); }
        foreach (var follower in actors)
        {
            var human = follower.GetBoss();
            var commander = actors.FirstOrDefault(f => ReferenceEquals(f.GetBoss(), human) &&
                f.GetBot().Profile.Info.Settings.Role == WildSpawnType.bossKnight)?.GetBot() ??
                actors.FirstOrDefault(f => ReferenceEquals(f.GetBoss(), human) &&
                f.GetBot().Profile.Info.Settings.Role == WildSpawnType.followerBigPipe)?.GetBot();
            if (commander == follower.GetBot() && !desired.ContainsKey(commander)) commander = null;
            // A self-anchor keeps the commander following the human. Pipe's remaining
            // members need positional cohesion without reapplying the 70/100 baseline.
            follower.SetCompanionCombatLeader(commander, controlsPosition: commander != null &&
                commander != follower.GetBot() && commander.Profile.Info.Settings.Role == WildSpawnType.followerBigPipe);
            MovementMembers[follower.GetBot()] = follower;
        }
    }

    private static void Apply(Membership membership)
    {
        membership.Follower.SetCompanionCombatPreferences(70f, 100f);
    }

    private static bool IsGoon(BotOwner bot) => bot.Profile?.Info?.Settings?.Role is
        WildSpawnType.bossKnight or WildSpawnType.followerBigPipe or WildSpawnType.followerBirdEye;

    internal static void Release(BotOwner owner)
    {
        if (owner == null) return;
        if (MovementMembers.TryGetValue(owner, out var follower))
        { follower.SetCompanionCombatLeader(null); MovementMembers.Remove(owner); }
        if (!Members.TryGetValue(owner, out var membership)) return;
        membership.Follower.ClearCompanionCombatPreferences();
        membership.Applied = false;
    }
    internal static void Reset()
    {
        foreach (var member in Members.Values) member.Follower.ClearCompanionCombatPreferences();
        Members.Clear();
        foreach (var follower in MovementMembers.Values) follower.SetCompanionCombatLeader(null);
        MovementMembers.Clear();
    }
}
