using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Modules;
using pitTeam.Patches;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain
{
    /// <summary>
    /// Keeps a confirmed live tripwire above ordinary follow/combat after the native escape.
    /// Decisions execute through the same action mapping as follower combat.
    /// </summary>
    internal sealed class FollowerTripwireLayer : CustomLayer
    {
        // Supported brains use native AvoidDanger at 80, or 100 for ExUsec.
        // Unsafe positions and other immediate dangers explicitly yield back to it.
        internal const int LayerPriority = 101;
        private GrenadeDangerPoint? currentDanger;
        private BotLogicDecision? currentDecision;
        private float nextMedicalRefreshAt;

        public FollowerTripwireLayer(BotOwner botOwner, int priority) : base(botOwner, priority) { }
        public override string GetName() => "pitTeam.FollowerTripwire";

        public override bool IsActive()
        {
            bool active = TrySelectResponse(out _, out _);
            if (!active && currentDecision == BotLogicDecision.heal && HasLiveDanger())
            {
                FollowerMedical.CancelActiveMedical(BotOwner);
            }
            return active;
        }

        public override Action GetNextAction()
        {
            TrySelectResponse(out currentDanger, out BotLogicDecision decision);
            currentDecision = decision;
            BotOwner.StopMove();
            if (decision != BotLogicDecision.heal)
            {
                FollowerMedical.CancelActiveMedical(BotOwner);
            }

            string reason = decision == BotLogicDecision.dogFight ? "tripwireDogFight" :
                decision == BotLogicDecision.shootFromPlace ? "tripwireShoot" :
                decision == BotLogicDecision.heal ? "tripwireHealInCover" : "tripwireWait";
            Modules.Logger.LogInfo($"[TripwireAwareness] {BotOwner.ProfileId}: {reason}");
            return FollowerCombatLayer.CreateBigBrainAction(BotOwner,
                new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(decision, reason, null),
                decision == BotLogicDecision.dogFight ? IsMovementSafe : null);
        }

        public override bool IsCurrentActionEnding()
        {
            bool active = TrySelectResponse(out GrenadeDangerPoint? danger, out BotLogicDecision decision);
            bool ending = !active || !ReferenceEquals(danger, currentDanger) || decision != currentDecision;
            if (ending && currentDecision == BotLogicDecision.heal && HasLiveDanger())
            {
                FollowerMedical.CancelActiveMedical(BotOwner);
            }
            return ending;
        }

        public override void Stop()
        {
            if (currentDecision == BotLogicDecision.heal && HasLiveDanger())
            {
                FollowerMedical.CancelActiveMedical(BotOwner);
            }
            BotOwner.StopMove();
            currentDanger = null;
            currentDecision = null;
            base.Stop();
        }

        private bool HasLiveDanger()
        {
            var danger = BotOwner?.BewareGrenade?.GrenadeDangerPoint;
            return danger != null && !danger.ShallDestroy();
        }

        private bool TrySelectResponse(out GrenadeDangerPoint? danger, out BotLogicDecision decision)
        {
            danger = BotOwner?.BewareGrenade?.GrenadeDangerPoint;
            decision = BotLogicDecision.holdPosition;
            if (BotOwner == null || BotOwner.IsDead || BotOwner.BotState != EBotState.Active ||
                BotOwner.GetPlayer == null || BotOwner.Memory == null || !BossPlayers.IsFollower(BotOwner) ||
                danger == null || danger.ShallDestroy() || !danger.IsActive() ||
                !FollowerTripwireAwarenessPatch.IsKnown(BotOwner, danger.Grenade) ||
                BotOwner.BewareGrenade.IgnoreGrenade(danger.Grenade) || BotOwner.BewareGrenade.IsIgnoreByPeriod())
            {
                return false;
            }

            if (BotOwner.WeaponManager?.Grenades == null || BotOwner.ArtilleryDangerPlace == null ||
                BotOwner.BewareBTR == null || BotOwner.BotTurnAwayLight == null || BotOwner.FlashGrenade == null ||
                BotOwner.WeaponManager.Grenades.ThrowindNow || BotOwner.ArtilleryDangerPlace.ShallRunAway() ||
                BotOwner.BewareBTR.ShallRunAway() || BotOwner.BotTurnAwayLight.IsActive || BotOwner.FlashGrenade.IsFlashed)
            {
                return false;
            }

            bool safeCover = IsInSafeCover(danger);
            if (danger.ShallRunAway() && !safeCover) return false;

            var enemy = BotOwner.Memory.GoalEnemy;
            bool hasEnemy = FollowerCombatCommon.HasActiveCombatEnemy(BotOwner, enemy);
            bool underAttack = BotOwner.Memory.IsUnderFire || FollowerCombatCommon.WasHitRecently(BotOwner, 1.5f);
            if (hasEnemy && underAttack)
            {
                decision = BotLogicDecision.dogFight;
            }
            else if (hasEnemy && enemy.IsVisible && enemy.CanShoot)
            {
                decision = BotLogicDecision.shootFromPlace;
            }
            else if (safeCover && !underAttack &&
                (!hasEnemy || (!enemy.IsVisible && Time.time - enemy.PersonalLastSeenTime > 3f)) && HasHealWork())
            {
                decision = BotLogicDecision.heal;
            }
            return true;
        }

        private bool IsInSafeCover(GrenadeDangerPoint danger)
        {
            var cover = BotOwner.Memory.CurCustomCoverPoint;
            return BotOwner.Memory.IsInCover && cover != null && !cover.IsSpotted && cover.IsFreeById(BotOwner.Id) &&
                (!danger.ShallRunAway() || cover.IsGoodForGrenade(danger, BotOwner));
        }

        private bool HasHealWork()
        {
            if (BotOwner.Medecine?.FirstAid == null || BotOwner.Medecine.SurgicalKit == null) return false;
            if (FollowerMedical.IsUsingMedical(BotOwner)) return true;
            if (Time.time >= nextMedicalRefreshAt)
            {
                nextMedicalRefreshAt = Time.time + 0.5f;
                FollowerMedical.RefreshMedicalWork(BotOwner);
            }
            return BotOwner.Medecine.FirstAid.ShallStartUse() || BotOwner.Medecine.SurgicalKit.ShallStartUse() ||
                FollowerMedical.CanStartFirstAidTopOff(BotOwner);
        }

        private bool IsMovementSafe(Vector3 destination)
        {
            var danger = currentDanger;
            if (danger == null || danger.ShallDestroy() ||
                !ReferenceEquals(danger, BotOwner.BewareGrenade.GrenadeDangerPoint)) return false;
            // Retain shielding inside the radius; dogfight can still aim and fire in place.
            if (danger.ShallRunAway()) return false;
            float safeDistance = Mathf.Sqrt(BotInternalSettingsController.Core.DELTA_GRENADE_SAFE_DIST_SQRT);
            return !Covers.IsPathTooCloseToEnemy(BotOwner.Position, destination, danger.DangerPoint, safeDistance);
        }
    }
}