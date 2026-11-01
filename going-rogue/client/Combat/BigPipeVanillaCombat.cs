using EFT;
using pitTeam.BigBrain;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat
{
    // Native Big Pipe mid-range fire/suppression/search policy, using shared execution.
    // No native artillery publication, unplanned launcher switch or hidden live target transform.
    internal sealed class BigPipeVanillaCombat : IGoonCombatSelector
    {
        private readonly BotOwner owner;
        private FollowerCombatCommon common;
        private float nextSuppressAt;
        private Vector3 lastSuppressPoint;
        private bool hasSuppressPoint;
        private string staleHoldEnemy;
        private float staleHoldReport;
        private float nextLeadershipCheck;
        public BotLogicDecision? LastSelectedDecision { get; set; }
        public float RetryUntil { get; private set; }

        internal BigPipeVanillaCombat(BotOwner bot) { owner = bot; }
        public void Begin(FollowerCombatCommon context) { common = context; LastSelectedDecision = null; nextLeadershipCheck = 0f; }
        public void InvalidatePosition() { }
        public void Dispose() { common = null; LastSelectedDecision = null; nextSuppressAt = 0f; hasSuppressPoint = false; staleHoldEnemy = null; }

        public AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision()
        {
            var enemy = owner.Memory.GoalEnemy;
            if (!common.HasActiveCombatEnemy(enemy)) return Hold("bigPipe.noEnemy", 1f);
            // Native Activate can draw the launcher before the addon claims the actor.
            // A planned opportunity has already handed off to Core's grenadier objective;
            // ordinary rifle combat must return any remaining support launcher safely.
            common.RequestLauncherPrimaryFallback("bigPipe.rifleCombat");
            if (common.TryCreatePendingLauncherPrimaryFallbackDecision(out var primaryFallback)) return primaryFallback;
            var bossSettings = owner.Settings.FileSettings.Boss;
            float groupReport = enemy.GroupInfo?.EnemyLastSeenTimeReal ?? enemy.TimeLastSeenReal;
            if (enemy.IsVisible) staleHoldEnemy = null;
            var choice = BigPipeDecisionPolicy.Select(enemy.IsVisible, enemy.CanShoot,
                Time.time - enemy.TimeLastSeenReal, bossSettings.KILLA_START_SEARCH_SEC,
                owner.WeaponManager.Reload.Reloading, owner.Memory.IsInCover,
                Time.time - owner.Memory.ComeToCoverTime, bossSettings.KILLA_HOLD_DELAY,
                Time.time - groupReport);
            if (choice != BigPipeFightChoice.Shoot && common.HasRecoveryPressure(1f))
            {
                if (common.TryGetCommittedRecoveryDecision(enemy, out var recovery)) return recovery;
                return common.CreateNoCoverRecoveryDecision(enemy);
            }
            if (choice == BigPipeFightChoice.Search)
                return Search(enemy, "bigPipe.native.search");
            if (choice == BigPipeFightChoice.ReloadHold) return Hold("bigPipe.native.waitReload", 1f);
            if (choice == BigPipeFightChoice.Shoot)
                return Decision(BotLogicDecision.shootFromPlace, "bigPipe.native.ShootFromPl8");
            if (choice == BigPipeFightChoice.CoverHold)
                return Hold("bigPipe.native.bossKillaHo", bossSettings.KILLA_HOLD_DELAY - (Time.time - owner.Memory.ComeToCoverTime));
            if (choice == BigPipeFightChoice.StaleContactHold)
            {
                if (string.Equals(staleHoldEnemy, enemy.ProfileId, System.StringComparison.Ordinal) && staleHoldReport == groupReport)
                    return Search(enemy, "bigPipe.staleContactSearch");
                staleHoldEnemy = enemy.ProfileId; staleHoldReport = groupReport;
                return Hold("bigPipe.native.KILLA_CLOSEATTACK_TIMES", bossSettings.KILLA_CLOSEATTACK_DELAY);
            }

            if (common.CanShootFromCurrentCover(out _))
                return Decision(BotLogicDecision.shootFromCover, "bigPipe.coverFire");

            var anchor = FollowerCombatCommon.GetEnemyAnchor(enemy);
            var cover = common.GetClosestShootCover(Vector3.Lerp(owner.Position, anchor, 0.5f), 40f,
                maxDistanceFromBot: 40f, avoidBossFireLane: true);
            if (cover != null && !common.IsWithinCommittedCoverArrivalHoldDistance(cover) &&
                common.TryCommitSelectedCombatCoverWithAction(enemy, cover, "bigPipe.cover.toSFC", BotLogicDecision.runToCover))
                return common.CreateMoveToCommittedCoverDecision("bigPipe.cover.toSFC");

            // Native repeats a burst after 20s and a meaningful target-position change.
            // Common owns target admission, obstruction classification and every actual shot.
            if (Time.time >= nextSuppressAt && (!hasSuppressPoint || (anchor - lastSuppressPoint).sqrMagnitude >= 4f) &&
                !Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(enemy) && !enemy.IsSuppressed() && enemy.ShallISuppress() &&
                common.TryCreateSuppressFromPlaceDecision(enemy, "autoSuppress.bigPipe", out var suppress,
                    allowSoftObstructedSuppression: true))
            {
                nextSuppressAt = Time.time + 20f; lastSuppressPoint = anchor; hasSuppressPoint = true;
                return suppress;
            }

            if (common.HasRecoveryPressure(1f) || !FollowerCombatAnchor.IsCombatIndependent(owner) &&
                (Enemy.Distance(enemy) >= Enemy.EnemyDistance.Far || !GoonCombatThreat.IsLowThreat(owner, enemy, common)))
            {
                if (common.TryGetCommittedRecoveryDecision(enemy, out var recovery)) return recovery;
                return common.CreateNoCoverRecoveryDecision(enemy);
            }
            return Search(enemy, "bigPipe.pressureSearch");
        }

        public AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current)
        {
            var enemy = owner.Memory.GoalEnemy;
            if (!common.HasActiveCombatEnemy(enemy)) return new AICoreActionEnd("bigPipe.enemyGone", true);
            if ((current.Action == BotLogicDecision.search || current.Action == BotLogicDecision.goToEnemy) &&
                current.Reason?.StartsWith("bigPipe.", System.StringComparison.Ordinal) == true && Time.time >= nextLeadershipCheck)
            {
                nextLeadershipCheck = Time.time + 0.25f;
                if (!GoonPushLeadership.CanAutonomousAdvance(owner, enemy) &&
                    common.TryPrepareDecisionTransition(current, "bigPipe.knightLead", Hold("bigPipe.knightLead", 1f)))
                    return new AICoreActionEnd("bigPipe.knightLead", true);
            }
            if (current.Action == BotLogicDecision.holdPosition &&
                current.Reason != null && current.Reason.StartsWith("bigPipe.", System.StringComparison.Ordinal) &&
                current.Reason.IndexOf("search", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return new AICoreActionEnd("bigPipe.searchRetry", Time.time >= RetryUntil || enemy.IsVisible && enemy.CanShoot);
            if (current.Action == BotLogicDecision.holdPosition)
                return new AICoreActionEnd("bigPipe.holdReady", Time.time >= RetryUntil ||
                    common.HasRecoveryPressure(1f) || enemy.IsVisible && enemy.CanShoot && !owner.WeaponManager.Reload.Reloading);
            return common.ShallEndCurrentDecision(current);
        }

        private AICoreActionResult<BotLogicDecision, CoreActionResultParams> Search(EnemyInfo enemy, string reason)
        {
            if (!GoonPushLeadership.CanAutonomousAdvance(owner, enemy)) return Hold("bigPipe.knightLead", 1f);
            var decision = common.CreateMemoryOnlyEnemySearchDecision(enemy, reason);
            if (decision.Action == BotLogicDecision.holdPosition && decision.Reason.StartsWith("bigPipe.", System.StringComparison.Ordinal))
            {
                RetryUntil = Time.time + 1f;
                common.HoldFor(1f);
            }
            return decision;
        }

        private AICoreActionResult<BotLogicDecision, CoreActionResultParams> Hold(string reason, float seconds)
        {
            float delay = Mathf.Clamp(seconds, 0.25f, 3f);
            RetryUntil = Time.time + delay; common.HoldFor(delay);
            return Decision(BotLogicDecision.holdPosition, reason);
        }
        private static AICoreActionResult<BotLogicDecision, CoreActionResultParams> Decision(BotLogicDecision action, string reason)
            => new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(action, reason);
    }
}
