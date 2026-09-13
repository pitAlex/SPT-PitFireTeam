using System;
using BepInEx;
using DrakiaXYZ.BigBrain.Brains;
using pitTeam.BigBrain;
using pitTeam.Modules;

namespace pitTeam.SAINAddon
{
    [BepInPlugin("xyz.pit.fireteam.sainaddon", "PitAlex-PitFireTeamSAINAddon", "1.0.0")]
    [BepInDependency("xyz.pit.fireteam", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("xyz.drakia.bigbrain", BepInDependency.DependencyFlags.HardDependency)]
    public class SAINAddonPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            try
            {
                SAINActionTypes.Validate();
                SainManPersonality.Initialize();
                if (!SainPlayerSquadBridge.Enable()) throw new InvalidOperationException("Player squad bridge is unavailable.");
                if (!SainSquadDecisionBridge.IsAvailable) throw new InvalidOperationException("Player squad decision bridge is unavailable.");
                var brains = FollowerLayerRegistry.GetSupportedBrains();
                BrainManager.AddCustomLayer(typeof(SAINFollowerSquadCombatLayer), brains, SAINFollowerSquadCombatLayer.LayerPriority);
                BrainManager.AddCustomLayer(typeof(SAINFollowerSoloCombatLayer), brains, SAINFollowerSoloCombatLayer.LayerPriority);
                SAINFollowerRuntime.Enable();
                Logger.LogInfo("[Init] SainMan selects addon SAIN solo/squad combat replicas, Chad personality, and player squad leadership. Other tactics retain core combat.");
            }
            catch (Exception ex)
            {
                SAINFollowerRuntime.Disable();
                SainPlayerSquadBridge.Disable();
                Logger.LogError($"[Init] SAIN addon unavailable; core fallback remains enabled. {ex}");
            }
        }

        private void OnDestroy()
        {
            SAINFollowerRuntime.Disable();
            SainPlayerSquadBridge.Disable();
        }
    }
}