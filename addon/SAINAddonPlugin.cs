using BepInEx;
using pitTeam.Modules;
using HarmonyLib;

namespace pitTeam.SAINAddon
{
    [BepInPlugin("xyz.pit.fireteam.sainaddon", "PitAlex-PitFireTeamSAINAddon", "0.8.6")]
    [BepInDependency("xyz.pit.fireteam", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("xyz.drakia.bigbrain", BepInDependency.DependencyFlags.HardDependency)]
    public class SAINAddonPlugin : BaseUnityPlugin
    {
        internal static SAINAddonPlugin? Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            var harmony = new Harmony("xyz.pit.fireteam.sainaddon");
            Logger.LogInfo("[Init] pitFireTeam SAIN addon loaded.");

            SainAddonBridge.RegisterRuntimeCallbacks(
                SAINFollowerRuntimeBridge.IsReadyForPatrolAfterCombat,
                SAINFollowerRuntimeBridge.ForceReleaseFollowerCombatState,
                SAINFollowerRuntimeBridge.TrySyncFollowerEnemyState,
                SAINFollowerRuntimeBridge.TryResetFollowerDecisionState);

            // Register lifecycle event handler for follower cache management.
            SainAddonBridge.OnFollowerLifecycleEvent += SAINFollowerRecoilPatch.OnFollowerLifecycleEvent;
            SainAddonBridge.OnFollowerLifecycleEvent += SAINFollowerRuntimeBridge.OnFollowerLifecycleEvent;
            SainAddonBridge.OnBossGroupStaticUpdate += SAINFollowerRuntimeBridge.OnBossGroupStaticUpdate;

            // Placeholder bootstrap for future SAIN regroup layer/action registration.
            // Keep this as the dedicated integration point so core plugin can remain vanilla-safe.
            SAINRegroupBootstrap.Initialize(harmony, Logger);
        }

        private void OnDestroy()
        {
            SainAddonBridge.UnregisterRuntimeCallbacks(
                SAINFollowerRuntimeBridge.IsReadyForPatrolAfterCombat,
                SAINFollowerRuntimeBridge.ForceReleaseFollowerCombatState,
                SAINFollowerRuntimeBridge.TrySyncFollowerEnemyState,
                SAINFollowerRuntimeBridge.TryResetFollowerDecisionState);

            SainAddonBridge.OnFollowerLifecycleEvent -= SAINFollowerRecoilPatch.OnFollowerLifecycleEvent;
            SainAddonBridge.OnFollowerLifecycleEvent -= SAINFollowerRuntimeBridge.OnFollowerLifecycleEvent;
            SainAddonBridge.OnBossGroupStaticUpdate -= SAINFollowerRuntimeBridge.OnBossGroupStaticUpdate;
        }
    }
}
