using EFT;
using pitTeam.Utils;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat
{
    internal sealed partial class KnightVanillaCombat
    {
        internal bool TryAdaptCautiousDecision(
            EnemyInfo enemy,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> native,
            out AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            decision = default;
            bool pursuit = native.Action == BotLogicDecision.runToEnemy ||
                           native.Action == BotLogicDecision.goToEnemy;
            bool exposedHold = native.Action == BotLogicDecision.holdPosition && !_owner.Memory.IsInCover;
            if (!pursuit && !exposedHold) return false;

            // The old Knight layer gated autonomous pursuit on distance and local threat.
            // Boss-relative ordinary-target confidence retains that distance/pressure gate.
            // Explicit Push has a separate objective and does not pass through this adapter.
            bool safeAdvance = !common.HasRecoveryPressure(1f) &&
                (FollowerCombatAnchor.IsCombatIndependent(_owner) ||
                 Enemy.Distance(enemy) < Enemy.EnemyDistance.Far && GoonCombatThreat.IsLowThreat(_owner, enemy, common));
            if (pursuit && safeAdvance) return false;

            if (common.CanShootFromCurrentCover(out _))
            {
                decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                    BotLogicDecision.shootFromCover, "knight.cautiousCoverFire");
                return true;
            }

            if (exposedHold && safeAdvance)
            {
                // A calm native wait with no cover must not become a recovery loop.
                // Core searches the admitted remembered point and owns path/shot safety.
                decision = common.CreateMemoryOnlyEnemySearchDecision(enemy, "knight.pressureSearch");
                if (decision.Action == BotLogicDecision.holdPosition)
                {
                    RetryUntil = Time.time + 1f;
                    common.HoldFor(1f);
                }
                return true;
            }

            // Native periodWait ends immediately outside cover, then selects itself again.
            // Shared recovery owns both travel/arrival and a bounded no-cover fallback.
            if (!common.TryGetCommittedRecoveryDecision(enemy, out decision))
                decision = common.CreateNoCoverRecoveryDecision(enemy);
            return true;
        }
    }
}
