using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace pitTeam.Modules
{
    // Optional plugins register at startup. Core never references their assemblies.
    public static class GameplayModeAddons
    {
        private static readonly HashSet<GameplayMode> Registered = new HashSet<GameplayMode>();
        private static readonly Dictionary<GameplayMode, string> ModeCards = new Dictionary<GameplayMode, string>();
        private static Func<Task> sessionRefresh;
        public static void RegisterSessionRefresh(GameplayMode mode, Func<Task> refresh)
        {
            if (mode != GameplayMode.GoingRogue || !Registered.Contains(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            sessionRefresh = refresh;
        }
        internal static bool HasSessionRefresh => Registered.Contains(GameplayMode.GoingRogue) && sessionRefresh != null;
        internal static Task RefreshSessionAsync() => HasSessionRefresh
            ? sessionRefresh() : Task.CompletedTask;
        public static void RegisterModeCard(GameplayMode mode, string path)
        {
            if (!Registered.Contains(mode) || string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Registered mode artwork is required.");
            ModeCards[mode] = path;
        }
        internal static string ModeCardPath(GameplayMode mode) => Registered.Contains(mode) && ModeCards.TryGetValue(mode, out var path) ? path : null;
        private static Func<bool> rogueCompanionsPresent;
        private static Func<bool> goonCompanionsPresent;
        private static bool exUsecFriendshipEnabled;
        private static Action<string> resolveSpawnSelection;
        private static Func<string, string, bool> spawnReady;
        private static Action<string> abortSpawn;
        private static Func<string, CompanionGearPolicy> companionGearPolicy;
        private static Func<string, CompanionCommandPolicy> companionCommandPolicy;
        private static Func<CompanionAdvanceOrderContext, bool?> advanceOrderPolicy;
        private static Func<string, bool> companionCombatPolicy;
        private static Func<string, string, bool> questCompanionPolicy;
        private static Func<string, string, Func<string, bool>, bool> questCompanionProgressPolicy;

        public static void RegisterQuestCompanionPolicy(GameplayMode mode, Func<string, string, bool> policy)
        {
            if (mode != GameplayMode.GoingRogue || !Registered.Contains(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            questCompanionPolicy = policy;
            questCompanionProgressPolicy = null;
        }
        public static void RegisterQuestCompanionProgressPolicy(GameplayMode mode, Func<string, string, Func<string, bool>, bool> policy)
        {
            if (mode != GameplayMode.GoingRogue || !Registered.Contains(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            questCompanionProgressPolicy = policy;
            questCompanionPolicy = null;
        }
        internal static bool CanSpawnQuestCompanion(string quest, string location, Func<string, bool> objectiveCompleted = null)
        {
            if (Current != GameplayMode.GoingRogue || !Registered.Contains(Current)) return false;
            return questCompanionProgressPolicy != null
                ? objectiveCompleted != null && questCompanionProgressPolicy(quest, location, objectiveCompleted)
                : questCompanionPolicy?.Invoke(quest, location) == true;
        }
        public static bool OwnsCompanionCombat(string role) =>
            Registered.Contains(Current) && companionCombatPolicy?.Invoke(role) == true;
        public static GameplayMode Current => GameplayModeRuntime.Current;
        public static bool HasSelectedBossCompanions => Utils.SpawnHelper.spawnMemberIdsBoss.Count > 0 ||
            (Current == GameplayMode.GoingRogue && Registered.Contains(Current) && goonCompanionsPresent?.Invoke() == true);
        public static bool IsRogueRaid => Current == GameplayMode.GoingRogue && Registered.Contains(Current) && rogueCompanionsPresent?.Invoke() == true;
        public static bool IsExUsecFriendlyRaid => IsRogueRaid && exUsecFriendshipEnabled;
        public static void Register(GameplayMode mode, Func<bool> companionsPresent = null,
            Func<bool> goonsPresent = null, bool exUsecFriendly = false,
            Action<string> resolveSelection = null, Func<string, string, bool> companionReady = null,
            Action<string> abortCompanion = null, Func<string, CompanionGearPolicy> gearPolicy = null,
            Func<string, bool> ownsCombat = null)
        {
            if (mode != GameplayMode.GoingRogue) throw new ArgumentOutOfRangeException(nameof(mode));
            Registered.Add(mode);
            ModeCards.Remove(mode);
            sessionRefresh = null;
            rogueCompanionsPresent = companionsPresent;
            goonCompanionsPresent = goonsPresent;
            exUsecFriendshipEnabled = exUsecFriendly;
            resolveSpawnSelection = resolveSelection;
            spawnReady = companionReady;
            abortSpawn = abortCompanion;
            companionGearPolicy = gearPolicy;
            companionCombatPolicy = ownsCombat;
            companionCommandPolicy = null;
            advanceOrderPolicy = null;
            questCompanionPolicy = null;
            questCompanionProgressPolicy = null;
        }
        public static void Unregister(GameplayMode mode)
        {
            questCompanionPolicy = null;
            questCompanionProgressPolicy = null;
            Registered.Remove(mode);
            ModeCards.Remove(mode);
            sessionRefresh = null;
            rogueCompanionsPresent = goonCompanionsPresent = null;
            exUsecFriendshipEnabled = false;
            resolveSpawnSelection = null;
            spawnReady = null;
            abortSpawn = null;
            companionGearPolicy = null;
            companionCombatPolicy = null;
            companionCommandPolicy = null;
            advanceOrderPolicy = null;
        }
        public static void RegisterCommandPolicy(GameplayMode mode, Func<string, CompanionCommandPolicy> policy)
        {
            if (mode != GameplayMode.GoingRogue || !Registered.Contains(mode))
                throw new InvalidOperationException("The gameplay mode must be registered before its command policy");
            companionCommandPolicy = policy;
        }
        public static void RegisterAdvanceOrderPolicy(GameplayMode mode, Func<CompanionAdvanceOrderContext, bool?> policy)
        {
            if (mode != GameplayMode.GoingRogue || !Registered.Contains(mode))
                throw new InvalidOperationException("The gameplay mode must be registered before its advance-order policy");
            advanceOrderPolicy = policy;
        }
        internal static bool? EvaluateAdvanceOrder(CompanionAdvanceOrderContext context)
        {
            if (!Registered.Contains(Current) || advanceOrderPolicy == null) return null;
            try { return advanceOrderPolicy(context); }
            catch (Exception ex) { Logger.LogError($"[CompanionCommands] Advance policy failed: {ex}"); return false; }
        }
        internal static void ConfirmSpawnSelection(string json)
        {
            if (Current == GameplayMode.GoingRogue && (!Registered.Contains(Current) || resolveSpawnSelection == null))
                throw new InvalidOperationException("Companion selection provider is unavailable");
            if (Registered.Contains(Current)) resolveSpawnSelection?.Invoke(json);
        }
        internal static bool IsSpawnCombatReady(string profileId, string role) =>
            Current != GameplayMode.GoingRogue || (Registered.Contains(Current) && spawnReady?.Invoke(profileId, role) == true);
        internal static void AbortSpawn(string profileId) { if (Registered.Contains(Current)) abortSpawn?.Invoke(profileId); }
        internal static CompanionGearPolicy GetCompanionGearPolicy(string role) =>
            Registered.Contains(Current) ? companionGearPolicy?.Invoke(role) : null;
        internal static CompanionCommandPolicy GetCompanionCommandPolicy(string role) =>
            Registered.Contains(Current) ? companionCommandPolicy?.Invoke(role) : null;
        public static bool IsAvailable(GameplayMode mode) =>
            mode == GameplayMode.GunsForHire || mode == GameplayMode.Allegiance || Registered.Contains(mode);
    }
}
