using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.BigBrain;
using pitTeam.Modules;
using pitTeam.Utils;

namespace pitTeam.GoingRogue.Combat
{
    public abstract class GoonCombatLayer : CustomLayer
    {
        public const int LayerPriority = 76;
        private static readonly Dictionary<BotOwner, GoonCombatLayer> Layers = new();
        private readonly string role, brain, actor;
        private FollowerCombatLayer driver;
        private GoonLayerLease lease;
        private bool started, suspended, faulted, spawnReady;
        private int positionRevision;
        private float nextFaultLogAt;
        protected GoonCombatLayer(BotOwner owner, int priority, string nativeRole, string nativeBrain, string actorName)
            : base(owner, priority)
        {
            role = nativeRole; brain = nativeBrain; actor = actorName;
            if (Layers.TryGetValue(owner, out var previous)) previous.Release();
            Layers[owner] = this;
        }
        public override string GetName() => "pitTeam.GoingRogue." + actor + "Combat";
        internal static bool IsSpawnReady(string profileId, string role) => Layers.Values.Any(layer =>
            layer.BotOwner?.ProfileId == profileId && string.Equals(layer.role, role, StringComparison.OrdinalIgnoreCase) &&
            layer.spawnReady && !layer.faulted && layer.IsEligible());
        internal static bool IsOwned(BotOwner owner, string role) => owner != null && Layers.TryGetValue(owner, out var layer) &&
            string.Equals(layer.role, role, StringComparison.OrdinalIgnoreCase) && layer.lease != null && layer.IsEligible();
        internal static int PositionRevision(BotOwner owner) => Layers.TryGetValue(owner, out var layer) ? layer.positionRevision : 0;
        internal static void RequestPositionRefresh(BotOwner owner)
        { if (Layers.TryGetValue(owner, out var layer)) layer.positionRevision++; }
        internal static void AbortSpawn(string profileId)
        {
            foreach (var pair in Layers.Where(pair => pair.Key.ProfileId == profileId).ToArray())
            { pair.Value.Release(); Layers.Remove(pair.Key); }
        }
        private bool IsEligible()
        {
            if (!GoingRoguePlugin.CombatEnabled || BotOwner == null) return false;
            var follower = BossPlayers.Instance?.GetFollower(BotOwner);
            return GoonCombatScope.Matches(role, brain, GameplayModeAddons.Current.ToString(),
                BotOwner.Profile?.Info?.Settings?.Role.ToString(), BotOwner.Brain?.BaseBrain?.ShortName(),
                ReferenceEquals(follower?.GetBot(), BotOwner), BotOwner.BotState == EBotState.Active && !BotOwner.IsDead &&
                BotOwner.GetPlayer?.HealthController?.IsAlive == true, follower?.GetBoss()?.realPlayer?.IsYourPlayer == true);
        }
        internal abstract FollowerCombatLogicBase CreateLogic(BotOwner owner);
        internal virtual bool InitializeRole() => true;
        internal virtual void RefreshRole() { }
        internal virtual void ReleaseRole() { }
        internal virtual Action CreateAction(BotOwner owner, AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
            => FollowerCombatLayer.CreateBigBrainAction(owner, decision);
        // Outside native strategy iteration: removals cannot shift its current active index.
        internal static void RefreshAll()
        {
            foreach (var pair in Layers.ToArray())
            {
                var layer = pair.Value;
                if (pair.Key == null || pair.Key.IsDead || pair.Key.BotState == EBotState.Disposed ||
                    layer.lease != null && !ReferenceEquals(pair.Key.Brain?.BaseBrain, layer.lease.Brain))
                { layer.Release(); Layers.Remove(pair.Key); continue; }
                try { layer.Refresh(); }
                catch (Exception ex)
                {
                    Logger.LogError($"[GoingRogue] {layer.actor} ownership failed: {ex}");
                    layer.faulted = true; layer.Release();
                }
            }
            GoonUnitTactics.RefreshAll();
        }
        internal static void ReleaseAll()
        { foreach (var layer in Layers.Values.ToArray()) layer.Release(); Layers.Clear(); GoonUnitTactics.Reset(); }
        private void Refresh()
        {
            if (!IsEligible()) { Release(); faulted = false; return; }
            if (faulted) return;
            if (lease == null)
            {
                if (!InitializeRole()) return;
                driver = new FollowerCombatLayer(BotOwner, Priority, CreateLogic, CreateAction, GetName());
                lease = new GoonLayerLease(BotOwner);
            }
            lease.Refresh(); RefreshRole();
            if (!spawnReady) Logger.LogInfo($"[GoingRogue] {actor} combat claimed follower={BotOwner.ProfileId}");
            spawnReady = true;
            if (suspended && !driver.IsActive()) { driver.Stop(); suspended = false; }
        }
        private void Release()
        {
            spawnReady = false;
            GoonUnitTactics.Release(BotOwner);
            Cleanup(() => { if (started) BotOwner?.Brain?.Agent?.Deactivate(GetName()); }, "action"); started = false;
            Cleanup(() => driver?.Stop(), "driver"); suspended = false;
            Cleanup(ReleaseRole, "role coordination");
            Cleanup(() => lease?.Dispose(), "layer restoration"); lease = null; driver = null;
        }
        private void Cleanup(System.Action cleanup, string stage)
        {
            try { cleanup(); }
            catch (Exception ex) { Logger.LogError($"[GoingRogue] {actor} cleanup ({stage}): {ex}"); }
        }
        public override bool IsActive() => IsEligible() && driver != null && driver.IsActive();
        public override void Start()
        {
            if (driver == null) return;
            if (!suspended) driver.Start(); suspended = false; started = true;
        }
        public override void Stop()
        {
            if (!started) return; started = false;
            if (IsEligible() && driver.IsActive())
            {
                suspended = true; FollowerRecovery.StopShooting(BotOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(BotOwner);
            }
            else driver.Stop();
        }
        public override bool IsCurrentActionEnding()
        {
            try { return !IsEligible() || driver == null || driver.IsCurrentActionEnding(); }
            catch (Exception ex) { ReportCombatFault("decision end", ex); throw; }
        }
        public override Action GetNextAction()
        {
            try { return driver.GetNextAction(); }
            catch (Exception ex) { ReportCombatFault("decision/action", ex); throw; }
        }
        private void ReportCombatFault(string stage, Exception ex)
        {
            if (UnityEngine.Time.time < nextFaultLogAt) return;
            nextFaultLogAt = UnityEngine.Time.time + 5f;
            // Read existing state only; diagnostics never invoke a selector or medical provider.
            Logger.LogError($"[GoingRogue] {actor} {stage} follower={BotOwner?.ProfileId} " +
                $"goal={BotOwner?.Memory?.GoalEnemy?.ProfileId} ready={spawnReady} suspended={suspended}: {ex}");
        }
        public override void BuildDebugText(StringBuilder builder) => driver?.BuildDebugText(builder);
    }
}
