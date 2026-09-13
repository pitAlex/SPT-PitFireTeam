using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using UnityEngine;

namespace pitTeam.Modules
{
    // Core owns the external-SAIN boundary. This service changes squad identity/membership only;
    // it does not install combat layers, publish decisions, or change enemy/proficiency state.
    public static class SainPlayerSquadBridge
    {
        private sealed class PlayerSquad
        {
            public object Squad;
            public object Manager;
            public pitAIBossPlayer Boss;
            public BotsGroup Group;
        }

        private static readonly Dictionary<object, PlayerSquad> Squads = new Dictionary<object, PlayerSquad>();
        private static readonly Dictionary<BotOwner, object> BoundBots = new Dictionary<BotOwner, object>();
        private static readonly HashSet<BotOwner> Detaching = new HashSet<BotOwner>();
        private static bool _available;
        public static bool IsEnabled { get; private set; }

        private static Type _squadType;
        private static Func<BotOwner, object> _getBot;
        private static Func<object> _getManager;
        private static Func<object, object> _getContainer;
        private static Func<object, object> _getSquad;
        private static Action<object, object> _setSquad;
        private static Func<object, BotOwner> _getOwner;
        private static Func<object, IDictionary> _getMembers;
        private static Func<object, string> _getGuid;
        private static Func<object, object> _getLeader;
        private static MethodInfo _findLeader;
        private static Func<object, IDictionary> _getManagerSquads;
        private static Func<object, object> _getSquadArray;
        private static MethodInfo _addToArray;
        private static MethodInfo _addMember;
        private static MethodInfo _removeMember;
        private static MethodInfo _removeSquad;
        private static MethodInfo _getNativeSquad;
        private static EventInfo _emptyEvent;

        internal static void ApplyPatches()
        {
            if (_available || !pitFireTeam.IsSAINInstalled) return;
            var harmony = new Harmony("xyz.pit.fireteam.sainleadership");
            try
            {
                Type botType = RequiredType("SAIN.Components.BotComponent");
                Type containerType = RequiredType("SAIN.SAINComponent.Classes.Info.BotSquadContainer");
                Type managerType = RequiredType("SAIN.BotController.Classes.BotSquads");
                Type controllerType = RequiredType("SAIN.Components.BotManagerComponent");
                _squadType = RequiredType("SAIN.BotController.Classes.Squad");
                MethodInfo getBot = RequiredMethod(RequiredType("SAIN.SAINEnableClass"), "GetSAIN", typeof(string), botType.MakeByRefType());
                var owner = Expression.Parameter(typeof(BotOwner), "owner");
                var foundBot = Expression.Variable(botType, "foundBot");
                _getBot = Expression.Lambda<Func<BotOwner, object>>(Expression.Block(new[] { foundBot },
                    Expression.Call(getBot, Expression.Property(owner, "ProfileId"), foundBot),
                    Expression.Convert(foundBot, typeof(object))), owner).Compile();
                var controller = Expression.Property(null, AccessTools.Property(controllerType, "Instance"));
                _getManager = Expression.Lambda<Func<object>>(Expression.Condition(
                    Expression.Equal(controller, Expression.Constant(null, controllerType)),
                    Expression.Constant(null, typeof(object)),
                    Expression.Convert(Expression.Property(controller, "BotSquads"), typeof(object)))).Compile();
                _getContainer = Getter<object>(botType, "Squad");
                _getSquad = Getter<object>(containerType, "SquadInfo");
                _setSquad = Setter<object>(containerType, "SquadInfo");
                _getOwner = Getter<BotOwner>(containerType, "BotOwner");
                _getMembers = Getter<IDictionary>(_squadType, "Members");
                _getGuid = Getter<string>(_squadType, "GUID");
                _getLeader = Getter<object>(_squadType, "LeaderComponent");
                _getManagerSquads = Getter<IDictionary>(managerType, "Squads");
                _getSquadArray = Getter<object>(managerType, "SquadArray");
                _addToArray = RequiredMethod(AccessTools.Property(managerType, "SquadArray").PropertyType, "Add", _squadType);
                _addMember = RequiredMethod(_squadType, "AddMember", botType);
                _removeMember = RequiredMethod(_squadType, "RemoveMember", typeof(string));
                _removeSquad = RequiredMethod(managerType, "RemoveSquad", _squadType);
                _getNativeSquad = RequiredMethod(managerType, "GetSquad", typeof(BotOwner));
                _emptyEvent = _squadType.GetEvent("OnSquadEmpty") ?? throw new MissingMemberException("Squad.OnSquadEmpty");
                if (_squadType.GetConstructor(Type.EmptyTypes) == null) throw new MissingMethodException("Squad constructor");

                // Resolve the complete patch set before modifying any external method.
                MethodInfo leaderId = RequiredGetter(_squadType, "LeaderId");
                MethodInfo dead = RequiredGetter(_squadType, "LeaderIsDeadorNull");
                _findLeader = RequiredMethod(_squadType, "findSquadLeader");
                MethodInfo assignment = RequiredMethod(_squadType, "assignSquadLeader", botType);
                MethodInfo distance = RequiredGetter(containerType, "DistanceToSquadLeader");
                MethodInfo inGroup = RequiredGetter(containerType, "BotInGroup");
                MethodInfo dispose = RequiredMethod(_squadType, "Dispose");
                Patch(harmony, _getNativeSquad, nameof(GetSquadPrefix), nameof(GetSquadPostfix));
                Patch(harmony, leaderId, nameof(LeaderIdPrefix));
                Patch(harmony, dead, nameof(LeaderDeadPrefix));
                Patch(harmony, _findLeader, nameof(ElectLeaderPrefix));
                Patch(harmony, assignment, nameof(AssignLeaderPrefix));
                Patch(harmony, distance, nameof(LeaderDistancePrefix));
                Patch(harmony, inGroup, nameof(InGroupPrefix));
                Patch(harmony, dispose, postfix: nameof(DisposePostfix));
                Patch(harmony, _removeMember, postfix: nameof(RemoveMemberPostfix));
                _available = true;
            }
            catch (Exception ex)
            {
                harmony.UnpatchSelf();
                Logger.LogError("[SAIN] Player squad adapter unavailable; follower combat remains core-owned.");
                Logger.LogError(ex);
            }
        }

        public static bool Enable()
        {
            if (IsEnabled) return true;
            if (!_available || !pitFireTeam.IsSAINInstalled) return false;
            IsEnabled = true;
            SainAddonBridge.OnFollowerLifecycleEvent += OnFollowerLifecycleEvent;
            SainAddonBridge.OnBossGroupStaticUpdate += SynchronizeBoss;
            foreach (var follower in BossPlayers.GetFollowers()) Synchronize(follower?.GetBot());
            return true;
        }

        public static void Disable()
        {
            IsEnabled = false;
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
                    foreach (object bot in _getMembers(context.Squad).Values.Cast<object>().ToArray())
                    {
                        object container = _getContainer(bot);
                        BotOwner owner = _getOwner(container);
                        if (ReferenceEquals(_getSquad(container), context.Squad)) _setSquad(container, null);
                        RemoveMember(context.Squad, owner.ProfileId);
                    }
                    if (Squads.ContainsKey(context.Squad)) _removeSquad.Invoke(context.Manager, new[] { context.Squad });
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
            if (!IsEnabled || owner == null || !BoundBots.TryGetValue(owner, out object bot)) return false;
            object container = _getContainer(bot);
            if (!TryGetContext(container, out PlayerSquad context)) return false;
            leader = context.Boss.realPlayer;
            return leader != null;
        }

        public static object GetDebugSnapshot(BotOwner owner)
        {
            if (!TryGetPlayerLeader(owner, out Player leader)) return null;
            object squad = _getSquad(_getContainer(BoundBots[owner]));
            return new
            {
                squadId = _getGuid(squad), playerLeaderId = leader.ProfileId,
                leaderAlive = leader.HealthController?.IsAlive == true,
                distanceToLeader = Vector3.Distance(owner.Position, leader.Position),
                botMemberCount = _getMembers(squad).Count,
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
                object bot = _getBot(owner);
                object manager = _getManager();
                if (bot == null || manager == null) return; // Staggered SAIN initialization: retry on the next boss tick.
                object container = _getContainer(bot);
                if (container == null) return;
                object target = GetPlayerSquad(manager, boss);
                object previous = _getSquad(container);
                bool changed = !ReferenceEquals(previous, target);
                if (changed)
                {
                    // Redirect before removal: native removal/disposal must not leave this bot pointing at an empty squad.
                    _setSquad(container, target);
                    RemoveMember(previous, owner.ProfileId);
                }
                if (!_getMembers(target).Contains(owner.ProfileId)) _addMember.Invoke(target, new[] { bot });
                bool firstBinding = !BoundBots.ContainsKey(owner);
                if (changed || firstBinding) RefreshPreviousLeadership(manager, bot);
                BoundBots[owner] = bot;
                if (changed || firstBinding)
                    Logger.LogInfo($"[SAIN] Player squad bound: follower={owner.ProfileId} leader={boss.realPlayer.ProfileId} squad={_getGuid(target)} members={_getMembers(target).Count} combat=core");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SAIN] Player squad binding failed for {owner.ProfileId}; combat remains core-owned.");
                Logger.LogError(ex);
            }
        }

        private static void Release(BotOwner owner, bool restoreNative)
        {
            if (owner == null || !BoundBots.TryGetValue(owner, out object bot)) return;
            BoundBots.Remove(owner);
            Detaching.Add(owner);
            try
            {
                object container = _getContainer(bot);
                object squad = _getSquad(container);
                if (squad != null && Squads.ContainsKey(squad))
                {
                    _setSquad(container, null);
                    RemoveMember(squad, owner.ProfileId);
                }
                if (restoreNative && !owner.IsDead && owner.BotsGroup != null && _getManager() is object manager)
                {
                    object native = _getNativeSquad.Invoke(manager, new object[] { owner });
                    _setSquad(container, native);
                    if (!_getMembers(native).Contains(owner.ProfileId)) _addMember.Invoke(native, new[] { bot });
                }
                Logger.LogInfo($"[SAIN] Player squad released: follower={owner.ProfileId}");
            }
            catch (Exception ex) { Logger.LogError(ex); }
            finally { Detaching.Remove(owner); }
        }

        private static void RemoveMember(object squad, string id)
        {
            if (squad != null && _getMembers(squad).Contains(id)) _removeMember.Invoke(squad, new object[] { id });
        }

        private static void RefreshPreviousLeadership(object manager, object bot)
        {
            // Native RemoveMember does not clear a living departed leader. The EFT group callback
            // may already have redirected SquadInfo before recruitment reaches our lifecycle event.
            // Ask native election to replace only this recruit in squads it has actually left.
            foreach (object squad in _getManagerSquads(manager).Values.Cast<object>().ToArray())
            {
                if (Squads.ContainsKey(squad) || !ReferenceEquals(_getLeader(squad), bot)) continue;
                IDictionary members = _getMembers(squad);
                if (members.Count > 0 && !members.Contains(_getOwner(_getContainer(bot)).ProfileId))
                    _findLeader.Invoke(squad, null);
            }
        }
        private static object GetPlayerSquad(object manager, pitAIBossPlayer boss)
        {
            foreach (PlayerSquad context in Squads.Values)
                if (ReferenceEquals(context.Manager, manager) && ReferenceEquals(context.Group, boss.bossGroup) &&
                    ReferenceEquals(context.Boss, boss)) return context.Squad;
            object squad = CreateRegisteredSquad(manager);
            Squads.Add(squad, new PlayerSquad { Squad = squad, Manager = manager, Boss = boss, Group = boss.bossGroup });
            return squad;
        }

        private static object CreateRegisteredSquad(object manager)
        {
            // Same registration and empty-squad cleanup as SAIN 4.5.1 BotSquads.GetSquad.
            object squad = Activator.CreateInstance(_squadType);
            Delegate cleanup = Delegate.CreateDelegate(_emptyEvent.EventHandlerType, manager, _removeSquad);
            _emptyEvent.AddEventHandler(squad, cleanup);
            _getManagerSquads(manager).Add(_getGuid(squad), squad);
            _addToArray.Invoke(_getSquadArray(manager), new[] { squad });
            return squad;
        }

        private static bool TryGetContext(object container, out PlayerSquad context)
        {
            context = null;
            if (!IsEnabled || container == null) return false;
            object squad = _getSquad(container);
            if (squad == null || !Squads.TryGetValue(squad, out context)) return false;
            BotOwner owner = _getOwner(container);
            return TryGetBoss(owner, out pitAIBossPlayer boss) && ReferenceEquals(boss, context.Boss) &&
                ReferenceEquals(owner.BotsGroup, context.Group);
        }

        private static bool GetSquadPrefix(object __instance, BotOwner botOwner, ref object __result)
        {
            if (!TryGetBoss(botOwner, out pitAIBossPlayer boss)) return true;
            __result = GetPlayerSquad(__instance, boss);
            return false;
        }

        private static void GetSquadPostfix(object __instance, BotOwner botOwner, ref object __result)
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
                    object bot = _getBot(peer);
                    object container = bot != null ? _getContainer(bot) : null;
                    object candidate = container != null ? _getSquad(container) : null;
                    if (candidate != null && !Squads.ContainsKey(candidate)) { __result = candidate; return; }
                }
            }
            __result = CreateRegisteredSquad(__instance);
        }

        private static bool LeaderIdPrefix(object __instance, ref string __result)
        {
            if (!IsEnabled || !Squads.TryGetValue(__instance, out PlayerSquad context)) return true;
            __result = context.Boss.realPlayer?.ProfileId;
            return false;
        }

        private static bool LeaderDeadPrefix(object __instance, ref bool __result)
        {
            if (!IsEnabled || !Squads.TryGetValue(__instance, out PlayerSquad context)) return true;
            __result = context.Boss.realPlayer?.HealthController?.IsAlive != true;
            return false;
        }

        private static bool ElectLeaderPrefix(object __instance) => !IsEnabled || !Squads.ContainsKey(__instance);

        private static bool AssignLeaderPrefix(object __instance, object sain)
        {
            if (IsEnabled && Squads.ContainsKey(__instance)) return false;
            // Core tactics retain the existing follower guard even in a mixed-tactic player group.
            if (sain != null)
            {
                object container = _getContainer(sain);
                BotOwner owner = container != null ? _getOwner(container) : null;
                if (BossPlayers.IsFollower(owner) && (!IsEnabled || !SainAddonBridge.IsSainManSelected(owner))) return false;
            }
            return true;
        }

        private static bool LeaderDistancePrefix(object __instance, ref float __result)
        {
            if (!TryGetContext(__instance, out PlayerSquad context)) return true;
            __result = Vector3.Distance(_getOwner(__instance).Position, context.Boss.realPlayer.Position);
            return false;
        }

        private static bool InGroupPrefix(object __instance, ref bool __result)
        {
            if (!TryGetContext(__instance, out _)) return true;
            __result = true;
            return false;
        }

        private static void DisposePostfix(object __instance) => Squads.Remove(__instance);

        private static void RemoveMemberPostfix(object __instance, string __0)
        {
            // Native death/disposal can remove a follower without a core Dismiss notification.
            // A transfer has already redirected SquadInfo and must retain its new binding.
            BotOwner removed = null;
            foreach (var entry in BoundBots)
            {
                if (entry.Key.ProfileId == __0 && ReferenceEquals(_getSquad(_getContainer(entry.Value)), __instance) &&
                    !_getMembers(__instance).Contains(__0)) { removed = entry.Key; break; }
            }
            if (removed != null) BoundBots.Remove(removed);
        }

        private static Type RequiredType(string name) => Type.GetType(name + ", SAIN", true);
        private static MethodInfo RequiredMethod(Type type, string name, params Type[] args) =>
            AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
        private static MethodInfo RequiredGetter(Type type, string name) =>
            AccessTools.PropertyGetter(type, name) ?? throw new MissingMemberException(type.FullName, name);
        private static void Patch(Harmony harmony, MethodInfo target, string prefix = null, string postfix = null) =>
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(SainPlayerSquadBridge), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(SainPlayerSquadBridge), postfix));

        private static Func<object, T> Getter<T>(Type type, string name)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            return Expression.Lambda<Func<object, T>>(Expression.Convert(
                Expression.Call(Expression.Convert(instance, type), RequiredGetter(type, name)), typeof(T)), instance).Compile();
        }

        private static Action<object, T> Setter<T>(Type type, string name)
        {
            MethodInfo setter = AccessTools.PropertySetter(type, name) ?? throw new MissingMemberException(type.FullName, name);
            var instance = Expression.Parameter(typeof(object), "instance");
            var value = Expression.Parameter(typeof(T), "value");
            return Expression.Lambda<Action<object, T>>(Expression.Call(Expression.Convert(instance, type), setter,
                Expression.Convert(value, setter.GetParameters()[0].ParameterType)), instance, value).Compile();
        }
    }
}