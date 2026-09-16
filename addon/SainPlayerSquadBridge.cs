using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using UnityEngine;
using Logger = pitTeam.Modules.Logger;
using pitTeam.Modules;
using SAIN;
using SAIN.BotController.Classes;
using SAIN.Components;
using SAIN.SAINComponent.Classes.Info;

namespace pitTeam.SAINAddon
{
    // Addon-owned player squad identity and membership;
    // it does not install combat layers, publish decisions, or change enemy/proficiency state.
    internal static class SainPlayerSquadBridge
    {
        private sealed class PlayerSquad
        {
            public Squad Squad;
            public BotSquads Manager;
            public pitAIBossPlayer Boss;
            public BotsGroup Group;
        }

        private static readonly Dictionary<Squad, PlayerSquad> Squads = new Dictionary<Squad, PlayerSquad>();
        private static readonly Dictionary<BotOwner, BotComponent> BoundBots = new Dictionary<BotOwner, BotComponent>();
        private static readonly HashSet<BotOwner> Detaching = new HashSet<BotOwner>();
        private static bool _available;
        public static bool IsEnabled { get; private set; }

        private static Action<BotSquadContainer, Squad> _setSquad;
        private static Action<Squad> _findLeader;
        private static Action<BotSquads, Squad> _removeSquad;

        internal static void ApplyPatches(Harmony harmony)
        {
            if (_available) return;
            // Only private SAIN members need cached reflection/delegates.
            _setSquad = AccessTools.MethodDelegate<Action<BotSquadContainer, Squad>>(
                AccessTools.PropertySetter(typeof(BotSquadContainer), nameof(BotSquadContainer.SquadInfo)));
            var election = RequiredMethod(typeof(Squad), "findSquadLeader");
            _findLeader = AccessTools.MethodDelegate<Action<Squad>>(election);
            _removeSquad = AccessTools.MethodDelegate<Action<BotSquads, Squad>>(RequiredMethod(typeof(BotSquads), "RemoveSquad", typeof(Squad)));
            Patch(harmony, RequiredMethod(typeof(BotSquads), nameof(BotSquads.GetSquad), typeof(BotOwner)), nameof(GetSquadPrefix), nameof(GetSquadPostfix));
            Patch(harmony, RequiredGetter(typeof(Squad), nameof(Squad.LeaderId)), nameof(LeaderIdPrefix));
            Patch(harmony, RequiredGetter(typeof(Squad), nameof(Squad.LeaderIsDeadorNull)), nameof(LeaderDeadPrefix));
            Patch(harmony, election, nameof(ElectLeaderPrefix));
            Patch(harmony, RequiredMethod(typeof(Squad), "assignSquadLeader", typeof(BotComponent)), nameof(AssignLeaderPrefix));
            Patch(harmony, RequiredGetter(typeof(BotSquadContainer), nameof(BotSquadContainer.DistanceToSquadLeader)), nameof(LeaderDistancePrefix));
            Patch(harmony, RequiredGetter(typeof(BotSquadContainer), nameof(BotSquadContainer.BotInGroup)), nameof(InGroupPrefix));
            Patch(harmony, RequiredMethod(typeof(Squad), nameof(Squad.Dispose)), postfix: nameof(DisposePostfix));
            Patch(harmony, RequiredMethod(typeof(Squad), nameof(Squad.RemoveMember), typeof(string)), postfix: nameof(RemoveMemberPostfix));
            _available = true;
        }
        internal static void Reset() => _available = false;

        public static bool Enable()
        {
            if (IsEnabled) return true;
            if (!_available || !pitFireTeam.IsSAINInstalled) return false;
            IsEnabled = true;
            SainAddonBridge.RegisterSquadSnapshot(GetDebugSnapshot);
            SainAddonBridge.OnFollowerLifecycleEvent += OnFollowerLifecycleEvent;
            SainAddonBridge.OnBossGroupStaticUpdate += SynchronizeBoss;
            foreach (var follower in BossPlayers.GetFollowers()) Synchronize(follower?.GetBot());
            return true;
        }

        public static void Disable()
        {
            IsEnabled = false;
            SainAddonBridge.UnregisterSquadSnapshot(GetDebugSnapshot);
            SainAddonBridge.OnFollowerLifecycleEvent -= OnFollowerLifecycleEvent;
            SainAddonBridge.OnBossGroupStaticUpdate -= SynchronizeBoss;
            foreach (BotOwner owner in BoundBots.Keys.ToArray()) Release(owner, true);
            ClearRaid();
        }

        internal static void ClearRaid()
        {
            foreach (BotOwner owner in BoundBots.Keys.ToArray()) Release(owner, false);
            foreach (PlayerSquad context in Squads.Values.ToArray())
            {
                try
                {
                    // A bot can have joined through native initialization before the first boss sync.
                    // Remove members individually: SAIN Dispose enumerates its live MemberInfos keys.
                    foreach (BotComponent bot in context.Squad.Members.Values.ToArray())
                    {
                        BotSquadContainer container = bot.Squad;
                        BotOwner owner = container.BotOwner;
                        if (ReferenceEquals(container.SquadInfo, context.Squad)) _setSquad(container, null);
                        RemoveMember(context.Squad, owner.ProfileId);
                    }
                    if (Squads.ContainsKey(context.Squad)) _removeSquad(context.Manager, context.Squad);
                }
                catch (Exception ex) { Logger.LogError(ex); }
            }
            Squads.Clear();
            BoundBots.Clear();
            Detaching.Clear();
        }

        public static bool TryGetPlayerLeader(BotOwner owner, out Player leader)
        {
            leader = null;
            if (!IsEnabled || owner == null || !BoundBots.TryGetValue(owner, out BotComponent bot)) return false;
            BotSquadContainer container = bot.Squad;
            if (!TryGetContext(container, out PlayerSquad context)) return false;
            leader = context.Boss.realPlayer;
            return leader != null;
        }

        public static object? GetDebugSnapshot(BotOwner owner)
        {
            if (!TryGetPlayerLeader(owner, out Player leader)) return null;
            Squad squad = BoundBots[owner].Squad.SquadInfo;
            return new
            {
                squadId = squad.GUID, playerLeaderId = leader.ProfileId,
                leaderAlive = leader.HealthController?.IsAlive == true,
                distanceToLeader = Vector3.Distance(owner.Position, leader.Position),
                botMemberCount = squad.Members.Count,
                addonCombatEnabled = pitFireTeam.UseSainFollowerCombat(owner)
            };
        }

        private static void SynchronizeBoss(pitAIBossPlayer boss)
        {
            if (!IsEnabled || boss == null) return;
            // The core dispatches this at most twice a second; no per-frame scans or allocations.
            for (int i = 0; i < boss.Followers.Count; i++) Synchronize(boss.Followers[i]);
        }

        private static void OnFollowerLifecycleEvent(BotOwner owner, FollowerLifecycleEvent kind)
        {
            if (kind == FollowerLifecycleEvent.OnRecruited) Synchronize(owner);
            else if (kind == FollowerLifecycleEvent.OnRaidEnd) ClearRaid();
            else if (kind == FollowerLifecycleEvent.OnDismiss) Release(owner, true);
        }

        private static bool TryGetBoss(BotOwner owner, out pitAIBossPlayer boss)
        {
            boss = owner?.BotFollower?.BossToFollow as pitAIBossPlayer;
            return IsEnabled && owner != null && !Detaching.Contains(owner) &&
                SainAddonBridge.IsSainManSelected(owner) && boss?.realPlayer != null && boss.bossGroup != null &&
                ReferenceEquals(owner.BotsGroup, boss.bossGroup);
        }

        private static void Synchronize(BotOwner owner)
        {
            if (owner == null) return;
            try
            {
                if (owner.IsDead || !TryGetBoss(owner, out pitAIBossPlayer boss))
                {
                    Release(owner, !owner.IsDead);
                    return;
                }
                SAINEnableClass.GetSAIN(owner.ProfileId, out BotComponent bot);
                BotSquads manager = BotManagerComponent.Instance?.BotSquads;
                if (bot == null || manager == null) return; // Staggered SAIN initialization: retry on the next boss tick.
                BotSquadContainer container = bot.Squad;
                if (container == null) return;
                Squad target = GetPlayerSquad(manager, boss);
                Squad previous = container.SquadInfo;
                bool changed = !ReferenceEquals(previous, target);
                if (changed)
                {
                    // Redirect before removal: native removal/disposal must not leave this bot pointing at an empty squad.
                    _setSquad(container, target);
                    RemoveMember(previous, owner.ProfileId);
                }
                if (!target.Members.ContainsKey(owner.ProfileId)) target.AddMember(bot);
                bool firstBinding = !BoundBots.ContainsKey(owner);
                if (changed || firstBinding) RefreshPreviousLeadership(manager, bot);
                BoundBots[owner] = bot;
                if (changed || firstBinding)
                    Logger.LogInfo($"[SAIN] Player squad bound: follower={owner.ProfileId} leader={boss.realPlayer.ProfileId} squad={target.GUID} members={target.Members.Count} combat=core");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SAIN] Player squad binding failed for {owner.ProfileId}; combat remains core-owned.");
                Logger.LogError(ex);
            }
        }

        private static void Release(BotOwner owner, bool restoreNative)
        {
            if (owner == null || !BoundBots.TryGetValue(owner, out BotComponent bot)) return;
            BoundBots.Remove(owner);
            Detaching.Add(owner);
            try
            {
                BotSquadContainer container = bot.Squad;
                Squad squad = container.SquadInfo;
                if (squad != null && Squads.ContainsKey(squad))
                {
                    _setSquad(container, null);
                    RemoveMember(squad, owner.ProfileId);
                }
                if (restoreNative && !owner.IsDead && owner.BotsGroup != null && BotManagerComponent.Instance?.BotSquads is BotSquads manager)
                {
                    Squad native = manager.GetSquad(owner);
                    _setSquad(container, native);
                    if (!native.Members.ContainsKey(owner.ProfileId)) native.AddMember(bot);
                }
                Logger.LogInfo($"[SAIN] Player squad released: follower={owner.ProfileId}");
            }
            catch (Exception ex) { Logger.LogError(ex); }
            finally { Detaching.Remove(owner); }
        }

        private static void RemoveMember(Squad squad, string id)
        {
            if (squad != null && squad.Members.ContainsKey(id)) squad.RemoveMember(id);
        }

        private static void RefreshPreviousLeadership(BotSquads manager, BotComponent bot)
        {
            // Native RemoveMember does not clear a living departed leader. The EFT group callback
            // may already have redirected SquadInfo before recruitment reaches our lifecycle event.
            // Ask native election to replace only this recruit in squads it has actually left.
            foreach (Squad squad in manager.Squads.Values.ToArray())
            {
                if (Squads.ContainsKey(squad) || !ReferenceEquals(squad.LeaderComponent, bot)) continue;
                var members = squad.Members;
                if (members.Count > 0 && !members.ContainsKey(bot.BotOwner.ProfileId))
                    _findLeader(squad);
            }
        }
        private static Squad GetPlayerSquad(BotSquads manager, pitAIBossPlayer boss)
        {
            foreach (PlayerSquad context in Squads.Values)
                if (ReferenceEquals(context.Manager, manager) && ReferenceEquals(context.Group, boss.bossGroup) &&
                    ReferenceEquals(context.Boss, boss)) return context.Squad;
            Squad squad = CreateRegisteredSquad(manager);
            Squads.Add(squad, new PlayerSquad { Squad = squad, Manager = manager, Boss = boss, Group = boss.bossGroup });
            return squad;
        }

        private static Squad CreateRegisteredSquad(BotSquads manager)
        {
            // Same registration and empty-squad cleanup as SAIN 4.5.1 BotSquads.GetSquad.
            var squad = new Squad();
            // Bind the native cleanup method itself so its -= in RemoveSquad still matches.
            var cleanup = (Action<Squad>)Delegate.CreateDelegate(typeof(Action<Squad>), manager,
                RequiredMethod(typeof(BotSquads), "RemoveSquad", typeof(Squad)));
            squad.OnSquadEmpty += cleanup;
            manager.Squads.Add(squad.GUID, squad);
            manager.SquadArray.Add(squad);
            return squad;
        }

        private static bool TryGetContext(BotSquadContainer container, out PlayerSquad context)
        {
            context = null;
            if (!IsEnabled || container == null) return false;
            Squad squad = container.SquadInfo;
            if (squad == null || !Squads.TryGetValue(squad, out context)) return false;
            BotOwner owner = container.BotOwner;
            return TryGetBoss(owner, out pitAIBossPlayer boss) && ReferenceEquals(boss, context.Boss) &&
                ReferenceEquals(owner.BotsGroup, context.Group);
        }

        private static bool GetSquadPrefix(BotSquads __instance, BotOwner botOwner, ref Squad __result)
        {
            if (!TryGetBoss(botOwner, out pitAIBossPlayer boss)) return true;
            __result = GetPlayerSquad(__instance, boss);
            return false;
        }

        private static void GetSquadPostfix(BotSquads __instance, BotOwner botOwner, ref Squad __result)
        {
            if (__result == null || !Squads.ContainsKey(__result) || TryGetBoss(botOwner, out _)) return;
            // Native selection can find a stale peer during a group transfer. Never lend a player-led squad to ordinary AI.
            BotsGroup group = botOwner.BotsGroup;
            if (group != null)
            {
                for (int i = 0; i < group.MembersCount; i++)
                {
                    BotOwner peer = group.Member(i);
                    if (peer == null || peer == botOwner || !ReferenceEquals(peer.BotsGroup, group)) continue;
                    SAINEnableClass.GetSAIN(peer.ProfileId, out BotComponent bot);
                    BotSquadContainer container = bot?.Squad;
                    Squad candidate = container?.SquadInfo;
                    if (candidate != null && !Squads.ContainsKey(candidate)) { __result = candidate; return; }
                }
            }
            __result = CreateRegisteredSquad(__instance);
        }

        private static bool LeaderIdPrefix(Squad __instance, ref string __result)
        {
            if (!IsEnabled || !Squads.TryGetValue(__instance, out PlayerSquad context)) return true;
            __result = context.Boss.realPlayer?.ProfileId;
            return false;
        }

        private static bool LeaderDeadPrefix(Squad __instance, ref bool __result)
        {
            if (!IsEnabled || !Squads.TryGetValue(__instance, out PlayerSquad context)) return true;
            __result = context.Boss.realPlayer?.HealthController?.IsAlive != true;
            return false;
        }

        private static bool ElectLeaderPrefix(Squad __instance) => !IsEnabled || !Squads.ContainsKey(__instance);

        private static bool AssignLeaderPrefix(Squad __instance) => !IsEnabled || !Squads.ContainsKey(__instance);

        private static bool LeaderDistancePrefix(BotSquadContainer __instance, ref float __result)
        {
            if (!TryGetContext(__instance, out PlayerSquad context)) return true;
            __result = Vector3.Distance(__instance.BotOwner.Position, context.Boss.realPlayer.Position);
            return false;
        }

        private static bool InGroupPrefix(BotSquadContainer __instance, ref bool __result)
        {
            if (!TryGetContext(__instance, out _)) return true;
            __result = true;
            return false;
        }

        private static void DisposePostfix(Squad __instance) => Squads.Remove(__instance);

        private static void RemoveMemberPostfix(Squad __instance, string __0)
        {
            // Native death/disposal can remove a follower without a core Dismiss notification.
            // A transfer has already redirected SquadInfo and must retain its new binding.
            BotOwner removed = null;
            foreach (var entry in BoundBots)
            {
                if (entry.Key.ProfileId == __0 && ReferenceEquals(entry.Value.Squad.SquadInfo, __instance) &&
                    !__instance.Members.ContainsKey(__0)) { removed = entry.Key; break; }
            }
            if (removed != null) BoundBots.Remove(removed);
        }

        private static MethodInfo RequiredMethod(Type type, string name, params Type[] args) =>
            AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
        private static MethodInfo RequiredGetter(Type type, string name) =>
            AccessTools.PropertyGetter(type, name) ?? throw new MissingMemberException(type.FullName, name);
        private static void Patch(Harmony harmony, MethodInfo target, string prefix = null, string postfix = null) =>
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(SainPlayerSquadBridge), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(SainPlayerSquadBridge), postfix));

    }
}
