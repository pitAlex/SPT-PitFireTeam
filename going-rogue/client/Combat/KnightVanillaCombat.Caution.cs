using EFT;
using pitTeam.Utils;

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
            if (pursuit && Enemy.Distance(enemy) < Enemy.EnemyDistance.Far &&
                !common.HasRecoveryPressure(1f) &&
                GoonCombatThreat.IsLowThreat(_owner, enemy, common)) return false;

            if (common.CanShootFromCurrentCover(out _))
            {
                decision = new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                    BotLogicDecision.shootFromCover, "knight.cautiousCoverFire");
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
