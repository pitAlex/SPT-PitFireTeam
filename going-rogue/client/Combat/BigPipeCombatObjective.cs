using EFT;
using pitTeam.BigBrain;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat
{
    internal sealed class BigPipeCombatObjective : GoonCombatObjective
    {
        private float nextLauncherProbe;
        internal BigPipeCombatObjective(BotOwner owner, FollowerCombatCommon common)
            : base(owner, common, new BigPipeVanillaCombat(owner), "bigPipe") { }

        public override void Reset() { base.Reset(); nextLauncherProbe = 0f; }

        protected override bool TryGetRoleSupportDecision(EnemyInfo enemy,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            if (Time.time < nextLauncherProbe || !BigPipeDecisionPolicy.InLauncherRange(FollowerEnemyTracking.Distance(enemy)) ||
                Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(enemy) ||
                !FollowerCombatCommon.HasUsableEquippedGrenadeLauncher(BotOwner)) return false;
            nextLauncherProbe = Time.time + 1f;
            if (CombatCommon.IsGrenadeLauncherSuppressCooldownActive(ordered: false, out _) ||
                CombatCommon.HasPendingLauncherPrimaryFallback() ||
                !CombatCommon.HasAutonomousGrenadeLauncherTarget(enemy, out _)) return false;
            decision = FollowerCombatGrenadierObjective.CreateAutonomousActivationDecision();
            nextLauncherProbe = Time.time + FollowerCombatGrenadierObjective.OpportunityWindowSeconds + 1f;
            return true;
        }
    }
}
