using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain
{
    /// <summary>
    /// Owns Balanced/Rifleman policy for choosing autonomous engagement, local hold, or regroup.
    /// It authorizes a fight; existing push/search helpers still own execution and commitment.
    /// </summary>
    internal sealed class FollowerCombatRiflemanEngagement
    {
        private const float ReferenceDistance = 80f;
        private const float CacheSeconds = 0.5f;
        private const float PositionToleranceSqr = 4f;
        private const float EnemyGroupRadius = 17f;
        private const float MaxRequiredAggression = 150f;
        private const float EquipmentThreatScale = 10f;
        private const float MinEquipmentThreatAdjustment = -6f;
        private const float MaxEquipmentThreatAdjustment = 12f;
        private const float RoleThreatScale = 12f;
        private const float MaxRoleThreatAdjustment = 12f;
        private const float IsolatedEnemyAdjustment = -5f;
        private const float TwoEnemyAdjustment = 2f;
        private const float ThreeEnemyAdjustment = 7f;
        private const float GroupEnemyAdjustment = 12f;
        private const float CautiousWeaponAdjustment = 4f;
        private const float BlockedWeaponAdjustment = 12f;
        private const float MinThreatAdjustment = -12f;
        private const float MaxThreatAdjustment = 30f;
        private const float LowThreatStyleThreshold = -2f;
        private const float MinPlayerPullFactor = 0.35f;
        private const float PlayerPullDistanceFloor = 10f;
        private const float PlayerPullPenaltyScale = 0.5f;
        private const float PlayerApproachBonusScale = 0.25f;
        private const float MaxPlayerPullPenalty = 20f;
        private const float MaxPlayerApproachBonus = 8f;
        private const float NavMeshSampleRadius = 2f;

        private readonly BotOwner botOwner;
        private readonly FollowerCombatCommon combatCommon;
        private readonly NavMeshPath path = new NavMeshPath();
        private Evaluation cachedEvaluation;
        private string cachedEnemyProfileId = string.Empty;
        private Vector3 cachedBotPosition;
        private Vector3 cachedEnemyPosition;
        private Vector3 cachedBossPosition;
        private float cachedAggression = -1f;
        private float cachedAt;
        private bool hasCachedEvaluation;
        private bool cachedEnemyVisible;
        private bool cachedEnemyShootable;
        private bool cachedCombatIndependent;
        private bool cachedSafetyBlocked;
        private bool cachedMemoryOnly;
        private bool cachedHasReliableLocation;
        private string lastRecordedSignature = string.Empty;

        public FollowerCombatRiflemanEngagement(BotOwner botOwner, FollowerCombatCommon combatCommon)
        {
            this.botOwner = botOwner;
            this.combatCommon = combatCommon;
        }

        public enum Outcome
        {
            Engage,
            Hold,
            Regroup
        }

        public readonly struct Evaluation
        {
            public Evaluation(
                Outcome outcome,
                string reason,
                float effectiveAggression,
                float requiredAggression,
                float enemyRouteDistance,
                float distanceRequirement,
                float threatAdjustment,
                float playerPullAdjustment,
                float currentBossDistance,
                float projectedBossDistance,
                float regroupTriggerDistance,
                int enemyGroupSize,
                float equipmentPowerRatio,
                float roleThreatMultiplier,
                FollowerCombatCommon.AutoPushWeaponThreatPolicy weaponThreatPolicy,
                bool pathsComplete)
            {
                Result = outcome;
                Reason = reason;
                EffectiveAggression = effectiveAggression;
                RequiredAggression = requiredAggression;
                EnemyRouteDistance = enemyRouteDistance;
                DistanceRequirement = distanceRequirement;
                ThreatAdjustment = threatAdjustment;
                PlayerPullAdjustment = playerPullAdjustment;
                CurrentBossDistance = currentBossDistance;
                ProjectedBossDistance = projectedBossDistance;
                RegroupTriggerDistance = regroupTriggerDistance;
                EnemyGroupSize = enemyGroupSize;
                EquipmentPowerRatio = equipmentPowerRatio;
                RoleThreatMultiplier = roleThreatMultiplier;
                WeaponThreatPolicy = weaponThreatPolicy;
                PathsComplete = pathsComplete;
            }

            public Outcome Result { get; }
            public string Reason { get; }
            public float EffectiveAggression { get; }
            public float RequiredAggression { get; }
            public float EnemyRouteDistance { get; }
            public float DistanceRequirement { get; }
            public float ThreatAdjustment { get; }
            public float PlayerPullAdjustment { get; }
            public float CurrentBossDistance { get; }
            public float ProjectedBossDistance { get; }
            public float RegroupTriggerDistance { get; }
            public int EnemyGroupSize { get; }
            public float EquipmentPowerRatio { get; }
            public float RoleThreatMultiplier { get; }
            public FollowerCombatCommon.AutoPushWeaponThreatPolicy WeaponThreatPolicy { get; }
            public bool PathsComplete { get; }
            public bool AllowsEngagement => Result == Outcome.Engage;
            public bool IsLowThreat => ThreatAdjustment < LowThreatStyleThreshold;
        }

        public Evaluation Evaluate(EnemyInfo goalEnemy)
        {
            float effectiveAggression = combatCommon.GetAggression01() * 100f;
            Vector3 botPosition = botOwner.Position;
            Vector3 enemyPosition = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            Vector3 bossPosition = combatCommon.GetRealBossPosition();
            string enemyProfileId = goalEnemy?.ProfileId ?? string.Empty;
            bool combatIndependent = FollowerCombatAnchor.IsCombatIndependent(botOwner);
            bool safetyBlocked = combatCommon.IsFollowerCriticallyWounded() ||
                                 combatCommon.HasActiveOrPendingHealWork() ||
                                 botOwner.Memory.IsUnderFire ||
                                 FollowerCombatCommon.WasHitRecently(botOwner, 1f);
            bool memoryOnly = goalEnemy == null || Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(goalEnemy);
            bool hasReliableLocation = goalEnemy != null && combatCommon.HasReliablePersonalEnemyLocation(goalEnemy);

            if (CanReuse(
                    enemyProfileId,
                    botPosition,
                    enemyPosition,
                    bossPosition,
                    effectiveAggression,
                    goalEnemy?.IsVisible == true,
                    goalEnemy?.CanShoot == true,
                    combatIndependent,
                    safetyBlocked,
                    memoryOnly,
                    hasReliableLocation))
            {
                return cachedEvaluation;
            }

            Evaluation evaluation = Calculate(
                goalEnemy,
                botPosition,
                enemyPosition,
                bossPosition,
                effectiveAggression,
                combatIndependent,
                safetyBlocked,
                memoryOnly,
                hasReliableLocation);

            cachedEvaluation = evaluation;
            cachedEnemyProfileId = enemyProfileId;
            cachedBotPosition = botPosition;
            cachedEnemyPosition = enemyPosition;
            cachedBossPosition = bossPosition;
            cachedAggression = effectiveAggression;
            cachedAt = Time.time;
            cachedEnemyVisible = goalEnemy?.IsVisible == true;
            cachedEnemyShootable = goalEnemy?.CanShoot == true;
            cachedCombatIndependent = combatIndependent;
            cachedSafetyBlocked = safetyBlocked;
            cachedMemoryOnly = memoryOnly;
            cachedHasReliableLocation = hasReliableLocation;
            hasCachedEvaluation = true;
            RecordTransition(goalEnemy, evaluation);
            return evaluation;
        }

        private bool CanReuse(
            string enemyProfileId,
            Vector3 botPosition,
            Vector3 enemyPosition,
            Vector3 bossPosition,
            float effectiveAggression,
            bool enemyVisible,
            bool enemyShootable,
            bool combatIndependent,
            bool safetyBlocked,
            bool memoryOnly,
            bool hasReliableLocation)
        {
            float cacheAge = Time.time - cachedAt;
            return hasCachedEvaluation &&
                   cacheAge >= 0f &&
                   cacheAge <= CacheSeconds &&
                   string.Equals(cachedEnemyProfileId, enemyProfileId, StringComparison.Ordinal) &&
                   (cachedBotPosition - botPosition).sqrMagnitude <= PositionToleranceSqr &&
                   (cachedEnemyPosition - enemyPosition).sqrMagnitude <= PositionToleranceSqr &&
                   (cachedBossPosition - bossPosition).sqrMagnitude <= PositionToleranceSqr &&
                   Mathf.Abs(cachedAggression - effectiveAggression) <= 0.01f &&
                   cachedEnemyVisible == enemyVisible &&
                   cachedEnemyShootable == enemyShootable &&
                   cachedCombatIndependent == combatIndependent &&
                   cachedSafetyBlocked == safetyBlocked &&
                   cachedMemoryOnly == memoryOnly &&
                   cachedHasReliableLocation == hasReliableLocation;
        }

        private Evaluation Calculate(
            EnemyInfo? goalEnemy,
            Vector3 botPosition,
            Vector3 enemyPosition,
            Vector3 bossPosition,
            float effectiveAggression,
            bool combatIndependent,
            bool safetyBlocked,
            bool memoryOnly,
            bool hasReliableLocation)
        {
            float directBossDistance = FollowerCombatCommon.IsFinite(bossPosition)
                ? Vector3.Distance(botPosition, bossPosition)
                : 0f;
            float regroupTriggerDistance = GetEffectiveRegroupTriggerDistance();
            float currentBossPathDistance = 0f;
            bool currentBossPathComplete = combatIndependent ||
                                           TryGetPathDistance(botPosition, bossPosition, out currentBossPathDistance);
            float currentBossDistance = combatIndependent
                ? 0f
                : currentBossPathComplete
                    ? FollowerCombatCommon.GetSafeRegroupDistance(currentBossPathDistance, directBossDistance)
                    : directBossDistance;
            bool urbanDetour = !combatIndependent &&
                               currentBossPathComplete &&
                               CombatDistanceConfiguration.Instance.IsUrbanDetourRegroup(directBossDistance, currentBossPathDistance) &&
                               FollowerCombatRegroupObjective.IsSameBossLevel(botPosition, bossPosition);
            bool outsideRegroupEnvelope = !combatIndependent &&
                                          !urbanDetour &&
                                          currentBossDistance > regroupTriggerDistance;

            if (goalEnemy == null ||
                !combatCommon.HasActiveCombatEnemy(goalEnemy) ||
                !FollowerCombatCommon.IsFinite(enemyPosition))
            {
                return CreateUnavailable(
                    outsideRegroupEnvelope ? Outcome.Regroup : Outcome.Hold,
                    "enemyUnavailable",
                    effectiveAggression,
                    currentBossDistance,
                    directBossDistance,
                    regroupTriggerDistance,
                    pathsComplete: false);
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return CreateUnavailable(
                    Outcome.Engage,
                    "immediateFire",
                    effectiveAggression,
                    currentBossDistance,
                    directBossDistance,
                    regroupTriggerDistance,
                    pathsComplete: currentBossPathComplete);
            }

            bool enemyPathComplete = TryGetPathDistance(botPosition, enemyPosition, out float enemyRouteDistance);
            float projectedBossPathDistance = 0f;
            bool projectedBossPathComplete = combatIndependent ||
                                             TryGetPathDistance(enemyPosition, bossPosition, out projectedBossPathDistance);
            bool pathsComplete = enemyPathComplete && currentBossPathComplete && projectedBossPathComplete;
            if (!enemyPathComplete)
            {
                enemyRouteDistance = Vector3.Distance(botPosition, enemyPosition);
            }

            if (!projectedBossPathComplete)
            {
                projectedBossPathDistance = Vector3.Distance(enemyPosition, bossPosition);
            }
            else if (!combatIndependent)
            {
                projectedBossPathDistance = FollowerCombatCommon.GetSafeRegroupDistance(
                    projectedBossPathDistance,
                    Vector3.Distance(enemyPosition, bossPosition));
            }

            float distanceRequirement = enemyRouteDistance / ReferenceDistance * 100f;
            float threatAdjustment = CalculateThreatAdjustment(
                goalEnemy,
                out int enemyGroupSize,
                out float equipmentPowerRatio,
                out float roleThreatMultiplier,
                out FollowerCombatCommon.AutoPushWeaponThreatPolicy weaponThreatPolicy);
            float playerPullAdjustment = combatIndependent || !pathsComplete
                ? 0f
                : CalculatePlayerPullAdjustment(enemyRouteDistance, currentBossDistance, projectedBossPathDistance);
            float requiredAggression = Mathf.Clamp(
                distanceRequirement + threatAdjustment + playerPullAdjustment,
                0f,
                MaxRequiredAggression);

            string blockReason = GetBlockReason(
                goalEnemy,
                safetyBlocked,
                memoryOnly,
                hasReliableLocation,
                pathsComplete,
                weaponThreatPolicy);
            bool allowsEngagement = string.IsNullOrEmpty(blockReason) && effectiveAggression >= requiredAggression;
            Outcome outcome;
            string reason;
            if (allowsEngagement)
            {
                outcome = Outcome.Engage;
                reason = "scorePassed";
            }
            else if (outsideRegroupEnvelope &&
                     !combatCommon.ShouldDeferAutonomousRegroupAfterRecentFight(
                         goalEnemy,
                         currentBossDistance,
                         regroupTriggerDistance))
            {
                outcome = Outcome.Regroup;
                reason = string.IsNullOrEmpty(blockReason) ? "scoreFailedOutsideEscort" : blockReason;
            }
            else
            {
                outcome = Outcome.Hold;
                reason = string.IsNullOrEmpty(blockReason) ? "scoreFailedInsideEscort" : blockReason;
            }

            return new Evaluation(
                outcome,
                reason,
                effectiveAggression,
                requiredAggression,
                enemyRouteDistance,
                distanceRequirement,
                threatAdjustment,
                playerPullAdjustment,
                currentBossDistance,
                projectedBossPathDistance,
                regroupTriggerDistance,
                enemyGroupSize,
                equipmentPowerRatio,
                roleThreatMultiplier,
                weaponThreatPolicy,
                pathsComplete);
        }

        private static Evaluation CreateUnavailable(
            Outcome outcome,
            string reason,
            float effectiveAggression,
            float currentBossDistance,
            float projectedBossDistance,
            float regroupTriggerDistance,
            bool pathsComplete)
        {
            return new Evaluation(
                outcome,
                reason,
                effectiveAggression,
                outcome == Outcome.Engage ? 0f : MaxRequiredAggression,
                0f,
                0f,
                0f,
                0f,
                currentBossDistance,
                projectedBossDistance,
                regroupTriggerDistance,
                1,
                1f,
                1f,
                FollowerCombatCommon.AutoPushWeaponThreatPolicy.Normal,
                pathsComplete);
        }

        private float GetEffectiveRegroupTriggerDistance()
        {
            float triggerDistance = CombatDistanceConfiguration.Instance.GetBossRegroupTriggerDistance(botOwner);
            return triggerDistance * Mathf.Lerp(
                PickupFollowerPersonality.RegroupMaxTriggerMultiplier,
                1f,
                combatCommon.GetBossProtectionWillingness01());
        }

        private bool TryGetPathDistance(Vector3 start, Vector3 destination, out float distance)
        {
            distance = 0f;
            if (!FollowerCombatCommon.IsFinite(start) ||
                !FollowerCombatCommon.IsFinite(destination) ||
                !NavMesh.SamplePosition(start, out NavMeshHit startHit, NavMeshSampleRadius, -1) ||
                !NavMesh.SamplePosition(destination, out NavMeshHit destinationHit, NavMeshSampleRadius, -1))
            {
                return false;
            }

            return Utils.Utils.TryGetCompletePathDistance(
                startHit.position,
                destinationHit.position,
                out distance,
                path);
        }

        private float CalculateThreatAdjustment(
            EnemyInfo goalEnemy,
            out int enemyGroupSize,
            out float equipmentPowerRatio,
            out float roleThreatMultiplier,
            out FollowerCombatCommon.AutoPushWeaponThreatPolicy weaponThreatPolicy)
        {
            float followerPower = botOwner.AIData?.PowerOfEquipment ?? 0f;
            float enemyPower = goalEnemy.Person?.AIData?.PowerOfEquipment ?? 0f;
            equipmentPowerRatio = followerPower > 1f && enemyPower > 0f
                ? enemyPower / followerPower
                : 1f;
            float equipmentAdjustment = Mathf.Clamp(
                (equipmentPowerRatio - 1f) * EquipmentThreatScale,
                MinEquipmentThreatAdjustment,
                MaxEquipmentThreatAdjustment);

            WildSpawnType role = goalEnemy.Person?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;
            roleThreatMultiplier = GetCombatRoleThreatMultiplier(role);
            float roleAdjustment = Mathf.Clamp(
                (roleThreatMultiplier - 1f) * RoleThreatScale,
                0f,
                MaxRoleThreatAdjustment);

            enemyGroupSize = Enemy.GetNearbyLivingGroupMemberCount(
                goalEnemy,
                FollowerCombatCommon.GetEnemyAnchor(goalEnemy),
                EnemyGroupRadius);
            float groupAdjustment = enemyGroupSize switch
            {
                <= 1 => IsolatedEnemyAdjustment,
                2 => TwoEnemyAdjustment,
                3 => ThreeEnemyAdjustment,
                _ => GroupEnemyAdjustment
            };

            weaponThreatPolicy = combatCommon.GetAutoPushWeaponThreatPolicy(goalEnemy);
            float weaponAdjustment = weaponThreatPolicy switch
            {
                FollowerCombatCommon.AutoPushWeaponThreatPolicy.Cautious => CautiousWeaponAdjustment,
                FollowerCombatCommon.AutoPushWeaponThreatPolicy.VeryCloseOrOrderedOnly => BlockedWeaponAdjustment,
                _ => 0f
            };

            return Mathf.Clamp(
                equipmentAdjustment + roleAdjustment + groupAdjustment + weaponAdjustment,
                MinThreatAdjustment,
                MaxThreatAdjustment);
        }

        private static float GetCombatRoleThreatMultiplier(WildSpawnType role)
        {
            float multiplier = FollowerDeathEscapeResolver.GetRouteThreatRoleMultiplier(role);
            if (multiplier > 0f)
            {
                return role == WildSpawnType.marksman ? Mathf.Max(multiplier, 1.15f) : multiplier;
            }

            string roleName = role.ToString();
            if (roleName.StartsWith("boss", StringComparison.OrdinalIgnoreCase))
            {
                return 1.5f;
            }

            return roleName.StartsWith("follower", StringComparison.OrdinalIgnoreCase) ? 1.3f : 1f;
        }

        private static float CalculatePlayerPullAdjustment(
            float enemyRouteDistance,
            float currentBossDistance,
            float projectedBossDistance)
        {
            float extraBossDistance = projectedBossDistance - currentBossDistance;
            if (extraBossDistance <= 0f)
            {
                return Mathf.Max(-MaxPlayerApproachBonus, extraBossDistance * PlayerApproachBonusScale);
            }

            float distanceFactor = Mathf.Lerp(
                MinPlayerPullFactor,
                1f,
                Mathf.Clamp01(
                    (enemyRouteDistance - PlayerPullDistanceFloor) /
                    (ReferenceDistance - PlayerPullDistanceFloor)));
            return Mathf.Min(
                MaxPlayerPullPenalty,
                extraBossDistance * PlayerPullPenaltyScale * distanceFactor);
        }

        private static string GetBlockReason(
            EnemyInfo goalEnemy,
            bool safetyBlocked,
            bool memoryOnly,
            bool hasReliableLocation,
            bool pathsComplete,
            FollowerCombatCommon.AutoPushWeaponThreatPolicy weaponThreatPolicy)
        {
            if (safetyBlocked)
            {
                return "survivalOrMedical";
            }

            if (memoryOnly || (!goalEnemy.IsVisible && !hasReliableLocation))
            {
                return "unreliableEnemyLocation";
            }

            if (!pathsComplete)
            {
                return "incompleteNavPath";
            }

            return weaponThreatPolicy == FollowerCombatCommon.AutoPushWeaponThreatPolicy.VeryCloseOrOrderedOnly &&
                   Enemy.Distance(goalEnemy) > Enemy.EnemyDistance.VeryClose
                ? "weaponThreat"
                : string.Empty;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordTransition(EnemyInfo? goalEnemy, Evaluation evaluation)
        {
#if DEBUG
            if (!BattleRecorder.IsRecordingFor(botOwner, requireRecordedCombat: true))
            {
                return;
            }

            string signature = $"{goalEnemy?.ProfileId}|{evaluation.Result}|{evaluation.Reason}";
            if (string.Equals(lastRecordedSignature, signature, StringComparison.Ordinal))
            {
                return;
            }

            lastRecordedSignature = signature;
            BattleRecorder.RecordObjectiveDiagnostic(
                botOwner,
                "FollowerCombatDefault",
                "autonomousEngagement",
                evaluation.Reason,
                () => new
                {
                    targetProfileId = goalEnemy?.ProfileId,
                    outcome = evaluation.Result.ToString(),
                    effectiveAggression = evaluation.EffectiveAggression,
                    requiredAggression = evaluation.RequiredAggression,
                    enemyRouteDistance = evaluation.EnemyRouteDistance,
                    distanceRequirement = evaluation.DistanceRequirement,
                    threatAdjustment = evaluation.ThreatAdjustment,
                    playerPullAdjustment = evaluation.PlayerPullAdjustment,
                    currentPlayerDistance = evaluation.CurrentBossDistance,
                    projectedPlayerDistance = evaluation.ProjectedBossDistance,
                    regroupTriggerDistance = evaluation.RegroupTriggerDistance,
                    enemyGroupSize = evaluation.EnemyGroupSize,
                    equipmentPowerRatio = evaluation.EquipmentPowerRatio,
                    roleThreatMultiplier = evaluation.RoleThreatMultiplier,
                    weaponThreatPolicy = evaluation.WeaponThreatPolicy.ToString(),
                    pathsComplete = evaluation.PathsComplete
                });
#endif
        }
    }
}
