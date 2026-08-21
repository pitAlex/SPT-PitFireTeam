using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AI;

using Comfort.Common;
using UnityDiagnostics;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatCommon
    {
        private const float StartSupportSuppressDistance = 30f;
        private const float MinorFirstAidFightDeferDistance = 35f;
        private const float MinorFirstAidFightDeferRecentContactSeconds = 0.75f;
        private const float MinorFirstAidFightDeferMaxMissingHealth = 25f;
        private const float ShootCoverSuperiorNavImprovementFactor = 0.7f;
        private const float StableShootCoverRefreshInterval = 1.2f;
        private const float UnstableShootCoverRefreshInterval = 0.6f;
        private const float HaveCoverToShootDebounceSeconds = 0.15f;
        private const float ShootLaneUpgradeHysteresisSeconds = 0.2f;
        private const float PointToShootUpdateMinDistance = 1.5f;
        private const float WeakEnemyPushDefaultMaxDistance = 80f;
        private const float WeakEnemyPushProtectorMaxDistance = 60f;
        private const float WeakEnemyPushMarksmanMaxDistance = 150f;
        private const float WeakEnemyPushBossDistanceBuffer = 12f;
        private const float BossLeashedSearchTriggerMargin = 1f;
        private const float WeakEnemyPushMaxRoleThreatMultiplier = 1.1f;
        private const int LowPenetrationAutoPushThreshold = 30;
        private const int CautiousPenetrationAutoPushThreshold = 38;
        private const int LowPenetrationNormalPmcGameplayLevel = 22;
        private const int CautiousPenetrationNormalPmcGameplayLevel = 28;
        private const int HighCapacityLowPenetrationMinimum = 26;
        private const int HighCapacityRifleMinimum = 45;
        private const int HighCapacityRifleDrumMinimum = 60;
        private const int HighCapacitySmallCaliberMinimum = 50;
        private const int HighCapacitySmallCaliberDrumMinimum = 70;
        private const int HighCapacityArmorDamageMinimum = 45;
        private const int HighCapacityLowPenArmorDamageMinimum = 55;
        private const float HighCapacityCautiousNormalizeWearScore = 700f;
        private const float HighCapacityBossNormalizeWearScore = 950f;
        private const float HighCapacityLowPenCautiousWearScore = 850f;
        private const float HighCapacitySmallCaliberNormalizeWearScore = 1050f;
        private const float StableVisibleImmediateFireSeconds = 0.3f;
        private const float CoverCommitLockSeconds = 2.5f;
        private const float CoverSearchCooldownSeconds = 0.35f;
        private const float FailedRecoveryCoverBlacklistSeconds = 6f;
        private const int CombatCoverEvaluationMaxCandidates = 30;
        private const int ThreatCoverPhysicsProbeMaxPerFrame = 8;
        private const int ThreatCoverProbeCacheMaxEntries = 64;
        private const float ThreatCoverProbeCacheSeconds = 1f;
        private const float ThreatCoverProbeThreatMoveToleranceSqr = 4f;
        private const float ThreatCoverProbePointMoveToleranceSqr = 0.25f;
        private const float CombatCoverCenterDistanceWeight = 0.35f;
        private const float CombatCoverTeamSpacing = 1.5f;
        private const float CombatCoverDestinationSpacing = 2.5f;
        private const float CombatCoverDestinationClaimTtlSeconds = 3f;
        private const float CombatCoverClaimReleaseTolerance = 0.5f;
        private const float RunToCoverProgressMinDistance = 0.35f;
        private const float RunToCoverStallSeconds = 4f;
        private const float PushCoverNoPathStallSeconds = 1.25f;
        private const float PushCoverBlacklistSeconds = 10f;
        private const float PushCoverBlacklistEnemyMoveToleranceSqr = 4f * 4f;
        private const int PushCoverBlacklistMaxEntries = 8;
        private const float CommittedCoverArrivalHoldDistance = 3f;
        private const float TacticalPointProgressMinDistance = 0.35f;
        private const float TacticalPointStallSeconds = 4f;
        private const float TacticalPointBlacklistSeconds = 10f;
        private const float TacticalPointBlacklistRadius = 1.5f;
        private const float HealRetreatProgressMinDistance = 0.35f;
        private const float HealRetreatStallSeconds = 4f;
        private const float TacticalPointArrivalDistance = 1.25f;
        private const float StandingCoverShotProbeHeight = 1.45f;
        private const float HealCoverMinNavDistance = 2f;
        private const float HealCoverMinEnemyDistanceGain = -2f;
        private const float EnemyFrontCrossGuardMaxDistance = 35f;
        private const float BossFireLaneCandidateRadius = 0.9f;
        private const float BossFireLanePathRadius = 1.1f;
        private const float BossFireLaneStartPadding = 0.75f;
        private const float BossFireLaneEndPadding = 2f;
        private const float BossFireLaneSoftPenalty = 24f;
        private const float FireSupportPathEnemyMinDistance = 12f;
        private const float DogFightOutOfRangeCooldownSeconds = 1.25f;
        private const float DogFightOpeningCommitmentSeconds = 1f;
        private const float DecisionTransitionMaxAgeSeconds = 2f;
        private const float FailedDecisionTransitionRetrySeconds = 1f;
        private const float ExposedFirePointBlankDistance = 8f;
        private const float ExposedFireNoReturnLeaseSeconds = 0.55f;
        private const float ExposedFireReturnedLeaseSeconds = 0.9f;
        private const float ExposedFireRecoveryRetrySeconds = 0.35f;
        private const float MemoryOnlySearchArrivalDistanceSqr = 4f;
        private const float MemoryOnlySearchRefreshDistanceSqr = 4f;
        private const float TargetHandoffScanDurationSeconds = 0.4f;
        private const float TargetHandoffProbeIntervalSeconds = 0.1f;
        private const float TargetHandoffRecentPersonalContactSeconds = 3f;
        private const float TargetHandoffFailedRetrySeconds = 1f;
        private const int TargetHandoffMaxCandidates = 4;
        private const int TargetHandoffMaxProbes = 4;
        private const string TargetHandoffScanReason = "targetHandoffScan";
        private const float NoEnemyRecoverySecondaryThreatRecentSeconds = 4f;
        private const float NoEnemyRecoverySecondaryThreatMinDistance = 12f;
        private const float NoEnemyRecoverySecondaryThreatMinApproach = 2f;
        private const int NoEnemyRecoverySecondaryThreatMaxCount = 4;
        private const float PointBlankRetreatBlockDistance = 8f;
        private const float PointBlankContactDogFightDistance = 3f;
        private const float PointBlankContactMaxAnchorDistance = 4.5f;
        private const float PointBlankDogFightLostContactGraceSeconds = 0.75f;
        private const float CloseVisibleThreatBreakDistance = 18f;
        private const float CloseVisibleDogFightStartDistance = CloseVisibleThreatBreakDistance;
        private const float CloseVisibleDogFightEndDistance = CloseVisibleThreatBreakDistance;
        private const float CloseThreatDogFightDistance = 8f;
        private const float CloseThreatAdvanceBreakDistance = 18f;
        private const float CloseThreatRecentSeenSeconds = 0.75f;
        private const float CloseRecentContactFireSeconds = 1f;
        private const float FollowerRegularGrenadeMinDistance = 15f;
        private const float FollowerRegularGrenadeMaxDistance = 40f;
        private const float FollowerRegularTimedGrenadeMinFuseSeconds = 1.5f;
        private const float FollowerRegularTimedGrenadeMaxFuseSeconds = 3.5f;
        private const float FollowerRegularTimedGrenadeMinFuseMaxDistance = 28f;
        private const float FollowerRegularGrenadeImpactDelayThreshold = 0.25f;
        private const float FollowerRegularGrenadeUnsafeRadius = FollowerShotSafety.RegularGrenadeUnsafeRadius;
        private const float FollowerRegularGrenadeFreshContactDelaySeconds = 0.75f;
        private const float FollowerRegularGrenadeRejectRecordSeconds = 2f;
        private const float FollowerRegularGrenadeTargetHeight = 0.25f;
        private const float FollowerRegularGrenadeAirburstFuseMarginSeconds = 0.45f;
        private const float ReloadRetreatThreatDistance = 18f;
        private const float ReloadRetreatAmmoRatio = 0.25f;
        private const int ReloadRetreatMinMagazineAmmo = 5;
        private const float CombatReloadRetryCooldownSeconds = 3f;
        private const float CombatLongGunReloadRejectedCooldownSeconds = 30f;
        private const float CombatLongGunReloadTransitionSeconds = 0.25f;
        private const int PushLongGunMinLoadedRounds = 10;
        private const int PushShotgunMinLoadedRounds = 6;
        private const int AutomaticSecondaryMaxPenetrationDeficit = 15;
        private const float AmmoProfileCacheMaxAgeSeconds = 1f;
        private const float HealContactThreatDistance = 25f;
        private const float HealContactRetreatMaxNavDistance = 10f;
        private const float DogFightInjuredSuppressRetreatRecentSeenSeconds = 3f;
        private const float HealCoverStallBlacklistSeconds = 10f;
        private const float HealHidePointMinDistance = 4f;
        private const float HealHidePointMaxNavDistance = 35f;
        private const float HealHidePointEnemyDistanceGain = -1f;
        private const float DefaultCommittedCoverHoldSeconds = 3f;
        private const float RetreatCommittedCoverHoldSeconds = 3.5f;
        private const float ShootCommittedCoverHoldSeconds = 2.5f;
        private const float BossCommittedCoverHoldSeconds = 3f;
        private const float CombatComeBossCoverMinimumProgress = 1f;
        private const float DefaultCommittedPositionHoldSeconds = 1.25f;
        private const float HealingCommittedHoldSeconds = 12f;
        private const float DefaultFireWhileMovingPushVisibleBreakSeconds = 0.6f;
        private const float ShootFromCoverLosFlickerGraceSeconds = 0.5f;
        private const float AutoSuppressMinSeconds = 0.75f;
        private const float AutoSuppressMaxSeconds = 3f;
        internal const string RecoveryNoCoverFightReason = "recovery.noCoverFight";
        internal const string RecoveryNoCoverSuppressReason = "recovery.noCoverSuppress";
        internal const string RecoveryNoCoverThreatHoldReason = "recovery.noCoverThreatHold";
        private const float RecoveryNoCoverCommitSeconds = 2.5f;
        private const float RecoveryNoCoverPointBlankBreakDistance = 5f;
        private const float OrderedSuppressMinSeconds = 2f;
        private const float OrderedWeaponSuppressMaxSeconds = 2f;
        private const float CloseSuppressFoliageProbeRadius = 0.45f;
        private const float CloseSuppressRecentContactSeconds = 0.6f;
        private const float AutonomousRegroupRecentFightGraceSeconds = 4f;
        private const float AutonomousRegroupExtremeDistanceMultiplier = 1.6f;
        private const float MarksmanSupportSameLevelTolerance = 1.75f;
        private const float SupportPointSameLevelTolerance = 1.75f;
        private const float MarksmanSupportSeparatedVerticalDistance = 4f;
        private const float MarksmanSupportSeparatedDirectDistance = 45f;
        private const float MarksmanSupportCandidateBossPathMaxDistance = 55f;
        private const float MarksmanSupportCandidateBossPathMaxExtra = 24f;
        private const float SuppressFromMinDistance = 3f;
        private const float SuppressFromSearchRadius = 35f;
        private const float GrenadeLauncherOrderedUnsafeRadius = 12f;
        private const float GrenadeLauncherAutoUnsafeRadius = 18f;
        private const float GrenadeLauncherMinTargetDistance = 10f;
        private const float GrenadeLauncherArmingDistance = 27f;
        private const float GrenadeLauncherUnarmedImpactUnsafeRadius = 2f;
        private const float GrenadeLauncherMaxTargetDistance = 130f;
        private const float GrenadeLauncherRecentKnownTargetSeconds = 5f;
        private const float FirstPrimaryLauncherNormalFireContactGraceSeconds = 2f;
        private const float GrenadeLauncherAimGravity = 9.81f;
        private const float GrenadeLauncherAimFallbackInitialSpeed = 76f;
        private const float GrenadeLauncherAimMinCompensationDistance = 25f;
        private const float GrenadeLauncherAimMaxCompensationHeight = 14f;
        private const float GrenadeLauncherArcLaneSampleMeters = 4f;
        private const float GrenadeLauncherArcImpactFallbackTolerance = 1.5f;
        private const int GrenadeLauncherArcLaneMinSamples = 4;
        private const int GrenadeLauncherArcLaneMaxSamples = 18;
        private const int GrenadeLauncherArcLaneMaxHitsPerSegment = 12;
        private const int SoftSuppressionLaneMaxHits = 24;
        private const float SoftSuppressionLaneTargetIgnoreDistance = 1.5f;
        private const float OrderedLauncherRayScanDistance = 120f;
        private const float OrderedLauncherRayMaxPerpendicularDistance = 35f;
        private const float GrenadeLauncherSuppressEventSeconds = 10f;
        private const float GrenadeLauncherSuppressReloadWaitSeconds = 6f;
        private const float GrenadeLauncherSuppressAimSettleSeconds = 2f;
        private const float GrenadeLauncherSuppressMinCommitmentSeconds = 4.5f;
        private const int GrenadeLauncherEmergencyLowLoadedRounds = 2;
        private const float OrderedGrenadeLauncherSuppressCooldownSeconds = 10f;
        private const float AutoGrenadeLauncherSuppressCooldownSeconds = 25f;
        private const string GrenadeLauncherSuppressReasonToken = ".launcher";

        private static readonly EquipmentSlot[] LauncherLooseAmmoReloadSlots =
        {
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Pockets,
            EquipmentSlot.Backpack,
            EquipmentSlot.SecuredContainer
        };
        private static readonly string[] DefaultBossObjectiveCoverBreakReasons =
        {
            "coverHold",
            "bossHold",
            "bossHold.open",
            "shootCover",
            "safeCover",
            "retreatShootCover",
            "retreatSafeCover",
            "retreatWeakCover",
            "bossCover",
            "committedFire"
        };
        private static readonly Dictionary<int, CoverCommitIntent> coverCommitIntents = new Dictionary<int, CoverCommitIntent>();
        private readonly BotOwner botOwner;
        private readonly List<MedsItemClass> stimSearchBuffer = new List<MedsItemClass>();
        private readonly Collider[] closeSuppressFoliageBuffer = new Collider[8];

        // Shared commitment state. Tactics decide why a commitment should break; common only
        // stores the latch, validates basic enemy/arrival state, and hands the latched decision
        // back to the tactic router. Keep new cross-tactic latches here instead of duplicating
        // them in Default/Sniper.
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? initialDecision;
        private float healBlockUntil;
        private float healStartedAt;
        private float stimStartedAt;
        private float nextCombatHealWorkRefreshAt;
        private float combatReloadRetryAt;
        private float combatLongGunReloadRetryAt;
        private float combatLongGunSwitchRetryAt;
        private EquipmentSlot? pendingCombatLongGunReloadSlot;
        private readonly Dictionary<EquipmentSlot, CombatLongGunReloadFailureState> combatLongGunReloadFailures =
            new Dictionary<EquipmentSlot, CombatLongGunReloadFailureState>();
        private CustomNavigationPoint? committedHealCover;
        private Vector3 committedHealPoint;
        private bool hasCommittedHealPoint;
        private BotLogicDecision committedHealMoveAction;
        private string? committedHealMoveReason;
        private int blockedHealCoverId = -1;
        private float blockedHealCoverUntil;
        private int blockedRecoveryCoverId = -1;
        private float blockedRecoveryCoverUntil;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? committedGrenadeDecision;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? committedPushDecision;
        private string? committedPushEnemyProfileId;
        private FollowerCombatTargetMissionKind? committedPushMissionKind;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? committedMovementDecision;
        private string? committedMovementEnemyProfileId;
        private Vector3 committedMovementTarget;
        private int? committedMovementCoverId;
        private string? lastFollowerGrenadeRejectReason;
        private float nextFollowerGrenadeRejectRecordAt;
        private string? lastGrenadeLauncherSuppressRejectReason;
        private float nextGrenadeLauncherSuppressRejectRecordAt;
        private string? lastSupportFiringCoverRejectReason;
        private string? lastSupportFiringPositionRejectReason;

        private CustomNavigationPoint? committedCoverPoint;
        private bool hasCommittedCoverDestinationClaim;
        private Vector3 committedCoverClaimPosition;

        private CustomNavigationPoint? committedHoldCoverPoint;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? committedPositionDecision;
        private Vector3? committedPosition;
        private float committedPointTimer = 0f;
        private float committedPointSetAt = 0f;
        private string? committedPointReason;
        private BotLogicDecision committedCoverMoveAction;
        private string? committedCoverMoveReason;
        private float committedCoverUntil;
        private float committedCoverSetAt;
        private float nextCoverAcquireTime;
        private readonly Dictionary<string, CachedAmmoProfile> ammoProfileCache = new Dictionary<string, CachedAmmoProfile>(StringComparer.Ordinal);
        private int runToCoverProgressCoverId = -1;
        private float runToCoverBestDistance = float.MaxValue;
        private float runToCoverLastProgressTime;
        private float runToCoverNoPathSince;
        private readonly Dictionary<int, PushCoverBlockState> blockedPushCovers =
            new Dictionary<int, PushCoverBlockState>();
        private Vector3 tacticalPointProgressTarget;
        private float tacticalPointBestDistance = float.MaxValue;
        private float tacticalPointLastProgressTime;
        private Vector3 blockedTacticalPoint;
        private float blockedTacticalPointUntil;
        private Vector3 healRetreatProgressTarget;
        private float healRetreatBestDistance = float.MaxValue;
        private float healRetreatLastProgressTime;
        private bool holdActive;
        private float holdEndTime;
        private string? activeFollowerSuppressReason;
        private float activeFollowerSuppressStartedAt;
        private string? activeRecoveryNoCoverReason;
        private float recoveryNoCoverUntil;
        private string recoveryNoCoverEnemyProfileId = string.Empty;
        private int recoveryNoCoverDamageRevision;
        private int activeFollowerSuppressInitialRounds = -1;
        private bool activeFollowerSuppressShotDetected;
        private int activeLauncherSuppressInitialRounds = -1;
        private int activeLauncherSuppressLastRounds = -1;
        private int activeLauncherSuppressCapacity = 1;
        private float activeLauncherSuppressFirstShotAt;
        private float activeLauncherSuppressReloadStartedAt;
        private float nextLauncherSuppressReloadRequestAt;
        private float orderedGrenadeLauncherSuppressCooldownUntil;
        private float autoGrenadeLauncherSuppressCooldownUntil;
        private float nextGrenadeLauncherSuppressCooldownRecordAt;
        private float nextLauncherPrimaryFallbackRecordAt;
        private float nextLauncherHolsterFallbackRecordAt;
        private string? pendingLauncherPrimaryFallbackReason;
        private string? pendingFirstPrimaryLauncherHolsterFallbackReason;
        private bool activeLauncherSuppressReloadRequested;
        private bool activeLauncherSuppressCommitmentExpiredRecorded;
        private bool activeLauncherSuppressMultiShot;
        private bool activeLauncherSuppressShotDetected;
        private Vector3 orderedSuppressTarget;
        private bool ownsGrenadeLauncherSwitch;
        private float shootFromCoverGraceUntil;

        private float dangerTimer = 0f;
        private float nextShootCoverCheckTime;
        private float nextClosestShootCoverCheckTime;
        private float nextApproachableCoverCheckTime;
        private float dangerIgnoreEquipTimer = 0f;
        private bool dangerResult = false;
        private bool dangerIgnoreEquipResult = false;
        private CustomNavigationPoint? cachedClosestShootCover;
        private float inCoverSince = 0f;
        private bool pendingHaveCoverToShoot;
        private float pendingHaveCoverToShootSince;
        private float shootLaneUpgradeSince;
        private float dogFightBlockedUntil;
        private float dogFightOpeningStartedAt;
        private string? dogFightOpeningEnemyProfileId;
        private bool dogFightOpeningRetreatDeferredRecorded;
        private float pointBlankDogFightContactLostAt;
        private float runToEnemyBlockedUntil;
        private PreparedCombatDecisionTransition? preparedDecisionTransition;
        private DeferredCombatDecisionTransition? deferredDecisionTransition;
        private string exposedFireEnemyProfileId = string.Empty;
        private string exposedFireDecisionReason = string.Empty;
        private float exposedFireStartedAt;
        private float exposedFireInitialTriggerPressedAt;
        private int exposedFireInitialDamageRevision;
        private float exposedFireRecoveryRetryAt;
        private string completedMemorySearchEnemyProfileId = string.Empty;
        private Vector3 completedMemorySearchPoint;
        private float completedMemorySearchReportTime;
        private bool hasCompletedMemorySearch;
        private readonly List<TargetHandoffCandidate> targetHandoffCandidates = new List<TargetHandoffCandidate>(TargetHandoffMaxCandidates);
        private readonly List<Vector3> noEnemyRecoverySecondaryThreatPoints = new List<Vector3>(NoEnemyRecoverySecondaryThreatMaxCount);
        private bool targetHandoffScanActive;
        private float targetHandoffScanUntil;
        private float nextTargetHandoffProbeAt;
        private float targetHandoffRetryBlockedUntil;
        private int targetHandoffCandidateIndex;
        private int targetHandoffProbeCount;
        private int targetHandoffStartDamageRevision;
        private int targetHandoffScanSignature;
        private int failedTargetHandoffSignature;
        private int coverEvaluationFrame = -1;
        private bool coverEvaluationAttempted;
        private bool coverEvaluationExhausted;
        private List<CustomNavigationPoint>? coverEvaluationCandidates;
        private readonly Dictionary<int, float> coverEvaluationNavDistance = new Dictionary<int, float>();
        private readonly Dictionary<int, ThreatCoverProbeCacheEntry> threatCoverProbeCache = new Dictionary<int, ThreatCoverProbeCacheEntry>();
        private int threatCoverPhysicsProbeFrame = -1;
        private int threatCoverPhysicsProbeCount;
        private bool lastAssignedRetreatCoverWasWeak;

        private readonly struct PreparedCombatDecisionTransition
        {
            public PreparedCombatDecisionTransition(
                AICoreActionResultStruct<BotLogicDecision, GClass26> sourceDecision,
                string endReason,
                string enemyProfileId,
                float preparedAt,
                AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
            {
                SourceDecision = sourceDecision;
                EndReason = endReason;
                EnemyProfileId = enemyProfileId;
                PreparedAt = preparedAt;
                NextDecision = nextDecision;
            }

            public AICoreActionResultStruct<BotLogicDecision, GClass26> SourceDecision { get; }
            public string EndReason { get; }
            public string EnemyProfileId { get; }
            public float PreparedAt { get; }
            public AICoreActionResultStruct<BotLogicDecision, GClass26> NextDecision { get; }
        }

        private readonly struct DeferredCombatDecisionTransition
        {
            public DeferredCombatDecisionTransition(
                AICoreActionResultStruct<BotLogicDecision, GClass26> sourceDecision,
                string endReason,
                string enemyProfileId,
                float retryAt)
            {
                SourceDecision = sourceDecision;
                EndReason = endReason;
                EnemyProfileId = enemyProfileId;
                RetryAt = retryAt;
            }

            public AICoreActionResultStruct<BotLogicDecision, GClass26> SourceDecision { get; }
            public string EndReason { get; }
            public string EnemyProfileId { get; }
            public float RetryAt { get; }
        }

        private readonly struct TargetHandoffCandidate
        {
            public TargetHandoffCandidate(string enemyProfileId, Vector3 lookPoint, float score, float lastSeenTime)
            {
                EnemyProfileId = enemyProfileId;
                LookPoint = lookPoint;
                Score = score;
                LastSeenTime = lastSeenTime;
            }

            public string EnemyProfileId { get; }
            public Vector3 LookPoint { get; }
            public float Score { get; }
            public float LastSeenTime { get; }
        }

        private readonly struct CoverCommitIntent
        {
            public CoverCommitIntent(int coverId, bool isShootingCover)
            {
                CoverId = coverId;
                IsShootingCover = isShootingCover;
            }

            public int CoverId { get; }
            public bool IsShootingCover { get; }
        }

        private readonly struct PushCoverBlockState
        {
            public PushCoverBlockState(
                string enemyProfileId,
                Vector3 enemyAnchor,
                float until)
            {
                EnemyProfileId = enemyProfileId;
                EnemyAnchor = enemyAnchor;
                Until = until;
            }

            public string EnemyProfileId { get; }
            public Vector3 EnemyAnchor { get; }
            public float Until { get; }
        }

        private readonly struct ThreatCoverProbeCacheEntry
        {
            public ThreatCoverProbeCacheEntry(
                string? enemyProfileId,
                Vector3 threatPosition,
                Vector3 coverPosition,
                float evaluatedAt,
                bool isHardCover)
            {
                EnemyProfileId = enemyProfileId;
                ThreatPosition = threatPosition;
                CoverPosition = coverPosition;
                EvaluatedAt = evaluatedAt;
                IsHardCover = isHardCover;
            }

            public string? EnemyProfileId { get; }
            public Vector3 ThreatPosition { get; }
            public Vector3 CoverPosition { get; }
            public float EvaluatedAt { get; }
            public bool IsHardCover { get; }
        }

        private enum CoverSearchIntent
        {
            Attack,
            AttackMoving,
            RunToCover,
            ForCover
        }

        private enum LauncherPrimaryFallbackOpportunity
        {
            None,
            Safe,
            Tactical,
            Emergency
        }

        private enum PreparedLongGunReloadStartResult
        {
            Started,
            Deferred,
            Rejected
        }

        private readonly struct CombatLongGunReloadFailureState
        {
            public CombatLongGunReloadFailureState(string weaponId, int loadedRounds, float retryAt)
            {
                WeaponId = weaponId;
                LoadedRounds = loadedRounds;
                RetryAt = retryAt;
            }

            public string WeaponId { get; }
            public int LoadedRounds { get; }
            public float RetryAt { get; }
        }

        public enum AutoPushWeaponThreatPolicy
        {
            Normal,
            Cautious,
            VeryCloseOrOrderedOnly
        }

        private readonly struct AutoPushAmmoProfile
        {
            public AutoPushAmmoProfile(int penetrationPower, int armorDamage, string? caliber, int magazineCapacity)
            {
                PenetrationPower = penetrationPower;
                ArmorDamage = armorDamage;
                Caliber = caliber ?? string.Empty;
                MagazineCapacity = magazineCapacity;
            }

            public int PenetrationPower { get; }

            public int ArmorDamage { get; }

            public string Caliber { get; }

            public int MagazineCapacity { get; }

            public float ArmorWearScore => PenetrationPower * (ArmorDamage / 100f) * MagazineCapacity;
        }

        private readonly struct CachedAmmoProfile
        {
            public CachedAmmoProfile(string signature, AutoPushAmmoProfile profile, float cachedAt)
            {
                Signature = signature;
                Profile = profile;
                CachedAt = cachedAt;
            }

            public string Signature { get; }

            public AutoPushAmmoProfile Profile { get; }

            public float CachedAt { get; }
        }

        public sealed class GrenadeLauncherFirePlan
        {
            internal GrenadeLauncherFirePlan(
                string reasonPrefix,
                bool ordered,
                float unsafeRadius,
                List<Vector3> targets,
                CustomNavigationPoint? suppressFrom)
            {
                ReasonPrefix = reasonPrefix;
                Ordered = ordered;
                UnsafeRadius = unsafeRadius;
                Targets = targets;
                SuppressFrom = suppressFrom;
            }

            internal string ReasonPrefix { get; }

            internal bool Ordered { get; }

            internal float UnsafeRadius { get; }

            internal List<Vector3> Targets { get; }

            internal CustomNavigationPoint? SuppressFrom { get; }

            public Vector3 FirstTarget => Targets.Count > 0 ? Targets[0] : Vector3.zero;

            public bool HasSuppressFrom => SuppressFrom != null;

            public Vector3? SuppressFromPosition => SuppressFrom?.Position;

            internal string DecisionReason => $"{ReasonPrefix}{GrenadeLauncherSuppressReasonToken}";
        }

        public FollowerCombatCommon(BotOwner botOwner)
        {
            this.botOwner = botOwner;
        }

        public bool HasInitialDecision => initialDecision.HasValue;
        public string? LastSupportFiringCoverRejectReason => lastSupportFiringCoverRejectReason;
        public string? LastSupportFiringPositionRejectReason => lastSupportFiringPositionRejectReason;

        public void ClearInitialDecision()
        {
            initialDecision = null;
        }

        public void SetInitialDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            initialDecision = decision;
        }

        private CoverSearchType SetCoverTacticAndGetSearchType(
            BotsGroup.BotCurrentTactic tactic,
            CoverShootType shootType,
            CoverSearchIntent searchIntent)
        {
            SetCoverTactic(tactic);

            return searchIntent switch
            {
                CoverSearchIntent.Attack => botOwner.Tactic.SubTactic.SearchTypeAttack(shootType),
                CoverSearchIntent.AttackMoving => botOwner.Tactic.SubTactic.SearchTypeAttackMoving(shootType),
                CoverSearchIntent.RunToCover => botOwner.Tactic.SubTactic.SearchRunToCover(shootType),
                CoverSearchIntent.ForCover => botOwner.Tactic.SubTactic.SearchTypeForCover(shootType),
                _ => botOwner.Tactic.SubTactic.SearchTypeForCover(shootType),
            };
        }

        private void SetCoverTactic(BotsGroup.BotCurrentTactic tactic)
        {
            if (botOwner.Tactic.ShallReturnToAttack && tactic != BotsGroup.BotCurrentTactic.Ambush)
            {
                botOwner.Tactic.ShallReturnToAttack = false;
                botOwner.Tactic.ReturnToAttackTime = 0f;
            }

            botOwner.Tactic.SetTactic(tactic);
        }

        public void Reset()
        {
            initialDecision = null;
            healBlockUntil = 0f;
            healStartedAt = 0f;
            stimStartedAt = 0f;
            nextCombatHealWorkRefreshAt = 0f;
            combatReloadRetryAt = 0f;
            combatLongGunReloadRetryAt = 0f;
            combatLongGunSwitchRetryAt = 0f;
            pendingCombatLongGunReloadSlot = null;
            combatLongGunReloadFailures.Clear();
            committedHealCover = null;
            committedHealPoint = Vector3.zero;
            hasCommittedHealPoint = false;
            committedHealMoveAction = default;
            committedHealMoveReason = null;
            ResetHealRetreatProgress();
            ResetTacticalPointProgress();
            blockedTacticalPoint = Vector3.zero;
            blockedTacticalPointUntil = 0f;
            blockedHealCoverId = -1;
            blockedHealCoverUntil = 0f;
            blockedRecoveryCoverId = -1;
            blockedRecoveryCoverUntil = 0f;
            blockedPushCovers.Clear();
            ClearCommittedGrenade();
            ClearCommittedPushDecision();
            ClearCommittedMovement();
            ClearCommittedPosition();
            ResetCommittedCover();
            holdActive = false;
            holdEndTime = 0f;
            activeFollowerSuppressReason = null;
            activeFollowerSuppressStartedAt = 0f;
            activeRecoveryNoCoverReason = null;
            recoveryNoCoverUntil = 0f;
            recoveryNoCoverEnemyProfileId = string.Empty;
            recoveryNoCoverDamageRevision = 0;
            ClearWeaponSuppressFireProfile();
            ClearLauncherSuppressFireProfile();
            HaveCoverToShoot = false;
            PointToShoot = null;
            cachedClosestShootCover = null;
            nextClosestShootCoverCheckTime = 0f;
            nextApproachableCoverCheckTime = 0f;
            pendingHaveCoverToShoot = false;
            pendingHaveCoverToShootSince = 0f;
            shootLaneUpgradeSince = 0f;
            dogFightBlockedUntil = 0f;
            ResetDogFightOpeningCommitment();
            pointBlankDogFightContactLostAt = 0f;
            runToEnemyBlockedUntil = 0f;
            ClearDecisionTransition();
            ResetExposedFireLease();
            ClearCompletedMemorySearch();
            ClearTargetHandoffScan("combatReset");
            targetHandoffRetryBlockedUntil = 0f;
            failedTargetHandoffSignature = 0;
            coverEvaluationFrame = -1;
            coverEvaluationAttempted = false;
            coverEvaluationExhausted = false;
            coverEvaluationCandidates = null;
            coverEvaluationNavDistance.Clear();
            threatCoverProbeCache.Clear();
            threatCoverPhysicsProbeFrame = -1;
            threatCoverPhysicsProbeCount = 0;
            lastAssignedRetreatCoverWasWeak = false;
            orderedGrenadeLauncherSuppressCooldownUntil = 0f;
            autoGrenadeLauncherSuppressCooldownUntil = 0f;
            nextGrenadeLauncherSuppressCooldownRecordAt = 0f;
            nextLauncherPrimaryFallbackRecordAt = 0f;
            nextLauncherHolsterFallbackRecordAt = 0f;
            pendingLauncherPrimaryFallbackReason = null;
            pendingFirstPrimaryLauncherHolsterFallbackReason = null;
            TryReleaseOwnedGrenadeLauncher();
        }

        public void RepairGoalEnemyMemory()
        {
            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return;
            }

            Vector3 enemyPosition = IsFinite(goalEnemy.CurrPosition)
                ? goalEnemy.CurrPosition
                : goalEnemy.PersonalLastPos;

            if (!IsFinite(enemyPosition) || enemyPosition.sqrMagnitude <= 0.01f)
            {
                return;
            }

            Enemy.RepairPersonalMemory(
                goalEnemy,
                enemyPosition,
                Enemy.HasDirectPersonalContact(goalEnemy));
        }

        public void HandleSharedDecisionChanged(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            UpdateExposedFireLease(nextDecision);
            BotLogicDecision action = nextDecision.Action;
            if (action != BotLogicDecision.shootFromStationary &&
                action != BotLogicDecision.debugStationary &&
                action != BotLogicDecision.debugStationaryInstantTake &&
                botOwner.WeaponManager.Stationary.Taken)
            {
                botOwner.WeaponManager.Stationary.DropCurWeapon(false, true);
            }

            if (action != BotLogicDecision.suppressFire ||
                !IsGrenadeLauncherSuppressReason(nextDecision.Reason))
            {
                TryApplyPendingLauncherPrimaryFallback(nextDecision);
            }
        }

        /// <summary>
        /// Tracks decisions that should keep using the same selected cover instead of re-picking.
        /// </summary>
        public void HandleCommittedCoverDecisionChanged(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            BotLogicDecision action = nextDecision.Action;
            if (IsCoverAffinedDecision(action) && botOwner.Memory?.CurCustomCoverPoint != null)
            {
                CommitCover(botOwner.Memory.CurCustomCoverPoint, action, nextDecision.Reason);
            }

            if (!IsCoverAffinedDecision(action) && committedCoverUntil < Time.time)
            {
                ClearCommittedCover();
            }
        }

        public void HandleFollowerSuppressDecisionChanged(AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (nextDecision.Action == BotLogicDecision.suppressFire && IsFollowerSuppressReason(nextDecision.Reason))
            {
                if (!string.Equals(activeFollowerSuppressReason, nextDecision.Reason, StringComparison.Ordinal))
                {
                    activeFollowerSuppressReason = nextDecision.Reason;
                    activeFollowerSuppressStartedAt = Time.time;
                    if (IsGrenadeLauncherSuppressReason(nextDecision.Reason))
                    {
                        ClearWeaponSuppressFireProfile();
                        StartLauncherSuppressFireProfile(nextDecision.Reason);
                    }
                    else
                    {
                        ClearLauncherSuppressFireProfile();
                        StartWeaponSuppressFireProfile();
                    }
                }

                return;
            }

            ClearFollowerSuppressState();
        }

        public void ClearFollowerSuppressState()
        {
            if (IsGrenadeLauncherSuppressReason(activeFollowerSuppressReason))
            {
                TryReleaseGrenadeLauncherSuppressEvent("suppressStateClear");
            }

            activeFollowerSuppressReason = null;
            activeFollowerSuppressStartedAt = 0f;
            ClearWeaponSuppressFireProfile();
            ClearLauncherSuppressFireProfile();
            orderedSuppressTarget = Vector3.zero;
        }

        public void PrepareLauncherSuppressWeaponFallback()
        {
            if (IsGrenadeLauncherSuppressReason(activeFollowerSuppressReason))
            {
                TryReleaseGrenadeLauncherSuppressEvent("launcherFallbackWeapon");
            }

            RequestLauncherPrimaryFallback("launcherFallbackWeapon");
            activeFollowerSuppressReason = null;
            activeFollowerSuppressStartedAt = 0f;
            ClearWeaponSuppressFireProfile();
            ClearLauncherSuppressFireProfile();
        }

        public void RequestLauncherPrimaryFallback(string reason)
        {
            if (!IsSupportGrenadeLauncherSelectedOrActive())
            {
                pendingLauncherPrimaryFallbackReason = null;
                return;
            }

            pendingLauncherPrimaryFallbackReason = reason;
            TryApplyPendingLauncherPrimaryFallback();
        }

        public bool HasPendingLauncherPrimaryFallback()
        {
            return pendingLauncherPrimaryFallbackReason != null &&
                   IsSupportGrenadeLauncherSelectedOrActive();
        }

        public bool TryCreatePendingLauncherPrimaryFallbackDecision(
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!HasPendingLauncherPrimaryFallback())
            {
                return false;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26> fallbackDecision =
                new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    "launcherFallback.pending");
            TryApplyPendingLauncherPrimaryFallback(fallbackDecision);
            if (!HasPendingLauncherPrimaryFallback())
            {
                return false;
            }

            HoldFor(0.15f);
            decision = fallbackDecision;
            return true;
        }

        public bool TryApplyPendingLauncherPrimaryFallback(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? decision = null)
        {
            if (pendingLauncherPrimaryFallbackReason == null)
            {
                return false;
            }

            if (!IsSupportGrenadeLauncherSelectedOrActive())
            {
                pendingLauncherPrimaryFallbackReason = null;
                return false;
            }

            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (!TryGetLauncherPrimaryFallbackOpportunity(
                    botOwner,
                    goalEnemy,
                    decision,
                    pendingLauncherPrimaryFallbackReason,
                    out LauncherPrimaryFallbackOpportunity opportunity,
                    out string waitReason))
            {
                RecordLauncherPrimaryFallbackWait(waitReason, goalEnemy, decision);
                return false;
            }

            BotWeaponSelector? selector = botOwner?.WeaponManager?.Selector;
            bool switchRequested = selector?.TryChangeToMain() == true;
            RecordLauncherPrimaryFallbackSwitch(switchRequested, opportunity, goalEnemy, decision);
            if (switchRequested && !IsSupportGrenadeLauncherSelectedOrActive())
            {
                pendingLauncherPrimaryFallbackReason = null;
                ownsGrenadeLauncherSwitch = false;
            }

            return switchRequested;
        }

        public static bool TrySwitchSelectedGrenadeLauncherToPrimaryForOpportunity(
            BotOwner? owner,
            EnemyInfo? goalEnemy,
            string? reason,
            bool tacticalIntent,
            out string waitReason)
        {
            waitReason = string.Empty;
            BotWeaponManager? weaponManager = owner?.WeaponManager;
            BotWeaponSelector? selector = weaponManager?.Selector;
            if (selector == null)
            {
                waitReason = "missingSelector";
                return false;
            }

            if (!IsSupportGrenadeLauncherSelectedOrActive(owner))
            {
                waitReason = IsFirstPrimaryGrenadeLauncherSelectedOrActive(owner)
                    ? "firstPrimaryLauncher"
                    : string.Empty;
                return false;
            }

            if (!TryGetLauncherPrimaryFallbackOpportunity(
                    owner,
                    goalEnemy,
                    decision: null,
                    pendingReason: tacticalIntent ? reason : null,
                    out _,
                    out waitReason))
            {
                return false;
            }

            return selector.TryChangeToMain();
        }

        public bool IsPendingLauncherPrimaryFallbackWeaponSelected()
        {
            return pendingLauncherPrimaryFallbackReason != null &&
                   IsSupportGrenadeLauncherSelectedOrActive();
        }

        public void RequestFirstPrimaryLauncherHolsterFallback(string reason)
        {
            pendingFirstPrimaryLauncherHolsterFallbackReason = null;
            if (!HasActiveCombatEnemy(botOwner.Memory?.GoalEnemy) ||
                !IsFirstPrimaryGrenadeLauncherSelectedOrActive(botOwner) ||
                !TryGetLoadedHolsterFallbackWeapon(botOwner, out _))
            {
                return;
            }

            pendingFirstPrimaryLauncherHolsterFallbackReason = reason;
            TryApplyPendingFirstPrimaryLauncherHolsterFallback();
        }

        public bool TryCreatePendingFirstPrimaryLauncherHolsterFallbackDecision(
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!HasPendingFirstPrimaryLauncherHolsterFallback())
            {
                return false;
            }

            TryApplyPendingFirstPrimaryLauncherHolsterFallback();
            if (!HasPendingFirstPrimaryLauncherHolsterFallback())
            {
                return false;
            }

            HoldFor(0.15f);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                "launcherHolsterFallback.pending");
            return true;
        }

        private bool HasPendingFirstPrimaryLauncherHolsterFallback()
        {
            if (pendingFirstPrimaryLauncherHolsterFallbackReason == null)
            {
                return false;
            }

            if (!TryGetLoadedHolsterFallbackWeapon(botOwner, out Weapon? holsterWeapon))
            {
                pendingFirstPrimaryLauncherHolsterFallbackReason = null;
                return false;
            }

            if (IsHolsterWeaponSelectedOrActive(botOwner, holsterWeapon))
            {
                pendingFirstPrimaryLauncherHolsterFallbackReason = null;
                return false;
            }

            BotWeaponSelector? selector = botOwner.WeaponManager?.Selector;
            if (!IsFirstPrimaryGrenadeLauncherSelectedOrActive(botOwner) &&
                selector?.IsChanging != true &&
                selector?.IsWeaponReady != false)
            {
                // Another valid weapon handoff won the race; do not override it with the pistol.
                pendingFirstPrimaryLauncherHolsterFallbackReason = null;
                return false;
            }

            return true;
        }

        private bool TryApplyPendingFirstPrimaryLauncherHolsterFallback()
        {
            if (!HasPendingFirstPrimaryLauncherHolsterFallback())
            {
                return false;
            }

            BotWeaponManager? weaponManager = botOwner.WeaponManager;
            BotWeaponSelector? selector = weaponManager?.Selector;
            string waitReason = string.Empty;
            if (selector == null)
            {
                waitReason = "missingSelector";
            }
            else if (selector.IsChanging)
            {
                waitReason = "selectorChanging";
            }
            else if (weaponManager?.Reload?.Reloading == true)
            {
                waitReason = "reloading";
            }
            else if (!selector.IsWeaponReady || weaponManager?.IsWeaponReady == false)
            {
                waitReason = "weaponNotReady";
            }

            if (!string.IsNullOrEmpty(waitReason))
            {
                RecordFirstPrimaryLauncherHolsterFallback("launcherHolsterFallbackWait", waitReason);
                return false;
            }

            bool switched = selector!.TryChangeToSlot(EquipmentSlot.Holster, false);
            RecordFirstPrimaryLauncherHolsterFallback(
                "launcherHolsterFallbackSwitch",
                $"switched={switched}");
            return switched;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordFirstPrimaryLauncherHolsterFallback(string action, string detail)
        {
            if (!BattleRecorder.IsRecordingFor(botOwner, requireRecordedCombat: true))
            {
                return;
            }

            if (Time.time < nextLauncherHolsterFallbackRecordAt)
            {
                return;
            }

            nextLauncherHolsterFallbackRecordAt = Time.time + 0.5f;
            BattleRecorder.RecordGrenadeEvent(
                botOwner,
                action,
                $"{pendingFirstPrimaryLauncherHolsterFallbackReason ?? "launcherRejected"}:{detail}",
                goalEnemy: botOwner.Memory?.GoalEnemy);
        }

        private static bool TryGetLoadedHolsterFallbackWeapon(BotOwner? owner, out Weapon? holsterWeapon)
        {
            holsterWeapon = owner?.GetPlayer?.InventoryController?.Inventory?.Equipment
                ?.GetSlot(EquipmentSlot.Holster)?.ContainedItem as Weapon;
            return holsterWeapon != null &&
                   !IsGrenadeLauncherWeapon(holsterWeapon) &&
                   CountLoadedRounds(holsterWeapon) > 0;
        }

        private static bool IsHolsterWeaponSelectedOrActive(BotOwner? owner, Weapon holsterWeapon)
        {
            BotWeaponSelector? selector = owner?.WeaponManager?.Selector;
            Weapon? activeWeapon = owner?.WeaponManager?.ShootController?.Item ??
                                   owner?.WeaponManager?.CurrentWeapon;
            return IsSameWeapon(activeWeapon, holsterWeapon) ||
                   (selector?.LastEquipmentSlot == EquipmentSlot.Holster &&
                    (activeWeapon == null || selector.IsChanging));
        }

        private static bool TryGetLauncherPrimaryFallbackOpportunity(
            BotOwner? owner,
            EnemyInfo? goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26>? decision,
            string? pendingReason,
            out LauncherPrimaryFallbackOpportunity opportunity,
            out string waitReason)
        {
            opportunity = LauncherPrimaryFallbackOpportunity.None;
            waitReason = string.Empty;
            BotWeaponManager? weaponManager = owner?.WeaponManager;
            BotWeaponSelector? selector = weaponManager?.Selector;
            if (selector == null)
            {
                waitReason = "missingSelector";
                return false;
            }

            if (selector.IsChanging)
            {
                waitReason = "selectorChanging";
                return false;
            }

            if (weaponManager?.Reload?.Reloading == true)
            {
                waitReason = "reloading";
                return false;
            }

            if (weaponManager?.IsWeaponReady == false)
            {
                waitReason = "weaponNotReady";
                return false;
            }

            if (owner == null || !HasActiveCombatEnemy(owner, goalEnemy))
            {
                opportunity = LauncherPrimaryFallbackOpportunity.Safe;
                return true;
            }

            if (owner?.Memory?.IsInCover == true)
            {
                opportunity = LauncherPrimaryFallbackOpportunity.Safe;
                return true;
            }

            if (IsEmergencyLauncherPrimaryFallback(owner, goalEnemy))
            {
                opportunity = LauncherPrimaryFallbackOpportunity.Emergency;
                return true;
            }

            if (IsTacticalLauncherPrimaryFallback(decision, pendingReason))
            {
                opportunity = LauncherPrimaryFallbackOpportunity.Tactical;
                return true;
            }

            if (!IsImmediateLauncherFallbackThreat(owner, goalEnemy!))
            {
                opportunity = LauncherPrimaryFallbackOpportunity.Safe;
                return true;
            }

            waitReason = "unsafeThreat";
            return false;
        }

        private static bool IsImmediateLauncherFallbackThreat(BotOwner owner, EnemyInfo goalEnemy)
        {
            return (goalEnemy.IsVisible && goalEnemy.CanShoot) ||
                   owner.Memory?.IsUnderFire == true ||
                   WasHitRecently(owner, 0.75f) ||
                   FollowerAwareness.WasRecentlyThreatened(owner);
        }

        private static bool IsEmergencyLauncherPrimaryFallback(BotOwner owner, EnemyInfo? goalEnemy)
        {
            if (!HasActiveCombatEnemy(owner, goalEnemy))
            {
                return false;
            }

            if (goalEnemy!.IsVisible &&
                goalEnemy.CanShoot &&
                goalEnemy.Distance <= ReloadRetreatThreatDistance)
            {
                return true;
            }

            Weapon? activeWeapon = owner.WeaponManager?.ShootController?.Item ??
                                   owner.WeaponManager?.CurrentWeapon;
            int loadedRounds = CountLoadedRounds(activeWeapon);
            return loadedRounds <= GrenadeLauncherEmergencyLowLoadedRounds &&
                   goalEnemy.Distance <= GrenadeLauncherArmingDistance &&
                   (goalEnemy.IsVisible || goalEnemy.CanShoot);
        }

        private static bool IsTacticalLauncherPrimaryFallback(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? decision,
            string? pendingReason)
        {
            if (IsTacticalLauncherPrimaryFallbackReason(pendingReason))
            {
                return true;
            }

            if (!decision.HasValue)
            {
                return false;
            }

            BotLogicDecision action = decision.Value.Action;
            string? reason = decision.Value.Reason;
            if (IsGrenadeLauncherSuppressReason(reason))
            {
                return false;
            }

            if (IsTacticalLauncherPrimaryFallbackReason(reason))
            {
                return true;
            }

            return action == BotLogicDecision.shootFromPlace ||
                   action == BotLogicDecision.shootFromCover ||
                   action == BotLogicDecision.dogFight ||
                   action == BotLogicDecision.suppressFire ||
                   action == BotLogicDecision.attackMoving ||
                   action == BotLogicDecision.attackMovingWithSuppress ||
                   action == BotLogicDecision.goToEnemy ||
                   action == BotLogicDecision.runToEnemy ||
                   action == BotLogicDecision.runToCover ||
                   action == BotLogicDecision.goToPoint;
        }

        private static bool IsTacticalLauncherPrimaryFallbackReason(string? reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                   (reason.IndexOf("reload", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("lowAmmo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("launcherFallback", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("weaponSwitchToPrimary", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("orderedWeaponSuppress", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("regroup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("push", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private bool IsSupportGrenadeLauncherSelectedOrActive()
        {
            return IsSupportGrenadeLauncherSelectedOrActive(botOwner);
        }

        internal static bool IsSupportGrenadeLauncherSelectedOrActive(BotOwner? owner)
        {
            BotWeaponSelector? selector = owner?.WeaponManager?.Selector;
            Weapon? secondPrimary = GetSecondPrimaryWeapon(owner);
            Weapon? activeWeapon = owner?.WeaponManager?.ShootController?.Item ??
                                   owner?.WeaponManager?.CurrentWeapon;
            return IsGrenadeLauncherWeapon(secondPrimary) &&
                   (IsSameWeapon(activeWeapon, secondPrimary) ||
                    (selector?.LastEquipmentSlot == EquipmentSlot.SecondPrimaryWeapon &&
                     (activeWeapon == null || selector.IsChanging)));
        }

        internal static bool IsFirstPrimaryGrenadeLauncherSelectedOrActive(BotOwner? owner)
        {
            BotWeaponSelector? selector = owner?.WeaponManager?.Selector;
            Weapon? firstPrimary = GetFirstPrimaryWeapon(owner);
            Weapon? activeWeapon = owner?.WeaponManager?.ShootController?.Item ??
                                   owner?.WeaponManager?.CurrentWeapon;
            return IsGrenadeLauncherWeapon(firstPrimary) &&
                   (IsSameWeapon(activeWeapon, firstPrimary) ||
                    (selector?.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon &&
                     (activeWeapon == null || selector.IsChanging)));
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordLauncherPrimaryFallbackWait(
            string waitReason,
            EnemyInfo? goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26>? decision)
        {
            if (!BattleRecorder.IsRecordingFor(botOwner, requireRecordedCombat: true))
            {
                return;
            }

            if (Time.time < nextLauncherPrimaryFallbackRecordAt)
            {
                return;
            }

            nextLauncherPrimaryFallbackRecordAt = Time.time + 2f;
            BattleRecorder.RecordGrenadeEvent(
                botOwner,
                "launcherFallbackWait",
                $"{pendingLauncherPrimaryFallbackReason ?? "launcherFallback"}:{waitReason}:{decision?.Reason ?? "noDecision"}",
                goalEnemy: goalEnemy);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordLauncherPrimaryFallbackSwitch(
            bool switched,
            LauncherPrimaryFallbackOpportunity opportunity,
            EnemyInfo? goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26>? decision)
        {
            if (!BattleRecorder.IsRecordingFor(botOwner, requireRecordedCombat: true))
            {
                return;
            }

            BattleRecorder.RecordGrenadeEvent(
                botOwner,
                "launcherFallbackSwitch",
                $"{pendingLauncherPrimaryFallbackReason ?? "launcherFallback"}:switched={switched}:opportunity={opportunity}:{decision?.Reason ?? "noDecision"}",
                goalEnemy: goalEnemy);
        }

        public bool IsGrenadeLauncherSuppressCooldownActive(bool ordered, out float remainingSeconds)
        {
            float cooldownUntil = ordered
                ? orderedGrenadeLauncherSuppressCooldownUntil
                : autoGrenadeLauncherSuppressCooldownUntil;
            remainingSeconds = cooldownUntil - Time.time;
            return remainingSeconds > 0f;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public void RecordGrenadeLauncherSuppressCooldownSkip(bool ordered, string reason)
        {
            if (!BattleRecorder.IsRecordingFor(botOwner, requireRecordedCombat: true))
            {
                return;
            }

            if (!IsGrenadeLauncherSuppressCooldownActive(ordered, out float remainingSeconds))
            {
                return;
            }

            if (Time.time < nextGrenadeLauncherSuppressCooldownRecordAt)
            {
                return;
            }

            nextGrenadeLauncherSuppressCooldownRecordAt = Time.time + 2f;
            BattleRecorder.RecordGrenadeEvent(
                botOwner,
                "launcherCooldown",
                $"{(ordered ? "ordered" : "auto")}:{reason}:remaining={remainingSeconds:0.0}",
                goalEnemy: botOwner.Memory?.GoalEnemy);
        }

        public void StartGrenadeLauncherSuppressCooldown(bool ordered, string reason)
        {
            float seconds = ordered
                ? OrderedGrenadeLauncherSuppressCooldownSeconds
                : AutoGrenadeLauncherSuppressCooldownSeconds;
            float cooldownUntil = Time.time + seconds;
            if (ordered)
            {
                orderedGrenadeLauncherSuppressCooldownUntil = Mathf.Max(
                    orderedGrenadeLauncherSuppressCooldownUntil,
                    cooldownUntil);
            }
            else
            {
                autoGrenadeLauncherSuppressCooldownUntil = Mathf.Max(
                    autoGrenadeLauncherSuppressCooldownUntil,
                    cooldownUntil);
            }

            BattleRecorder.RecordGrenadeEvent(
                botOwner,
                "launcherCooldown",
                $"{(ordered ? "ordered" : "auto")}:{reason}:seconds={seconds:0.0}",
                goalEnemy: botOwner.Memory?.GoalEnemy);
        }

        private void StartWeaponSuppressFireProfile()
        {
            Weapon? activeWeapon = botOwner.WeaponManager?.ShootController?.Item ??
                                   botOwner.WeaponManager?.CurrentWeapon;
            activeFollowerSuppressInitialRounds = CountLoadedRounds(activeWeapon);
            activeFollowerSuppressShotDetected = botOwner.ShootData?.Shooting == true;
        }

        /// <summary>
        /// Starts a new per-frame cover evaluation budget. The first cover request obtains one
        /// broad local candidate pool; every later combat branch in the same frame reuses that
        /// pool, including the empty result, instead of asking EFT to enumerate cover again.
        /// </summary>
        public void BeginCoverEvaluationCycle()
        {
            int frame = Time.frameCount;
            if (coverEvaluationFrame == frame)
            {
                return;
            }

            coverEvaluationFrame = frame;
            coverEvaluationAttempted = false;
            coverEvaluationExhausted = false;
            coverEvaluationCandidates = null;
            coverEvaluationNavDistance.Clear();
            threatCoverPhysicsProbeFrame = frame;
            threatCoverPhysicsProbeCount = 0;
        }

        private void UpdateWeaponSuppressShotDetection()
        {
            if (activeFollowerSuppressShotDetected)
            {
                return;
            }

            if (botOwner.ShootData?.Shooting == true)
            {
                activeFollowerSuppressShotDetected = true;
                return;
            }

            Weapon? activeWeapon = botOwner.WeaponManager?.ShootController?.Item ??
                                   botOwner.WeaponManager?.CurrentWeapon;
            int currentRounds = CountLoadedRounds(activeWeapon);
            if (activeFollowerSuppressInitialRounds >= 0 &&
                currentRounds < activeFollowerSuppressInitialRounds)
            {
                activeFollowerSuppressShotDetected = true;
            }
        }

        private void ClearWeaponSuppressFireProfile()
        {
            activeFollowerSuppressInitialRounds = -1;
            activeFollowerSuppressShotDetected = false;
        }

        private void StartLauncherSuppressFireProfile(string? reason)
        {
            Weapon? launcher = GetActiveOrEquippedGrenadeLauncher();
            activeLauncherSuppressInitialRounds = CountLoadedRounds(launcher);
            activeLauncherSuppressLastRounds = activeLauncherSuppressInitialRounds;
            activeLauncherSuppressCapacity = GetLoadedCapacity(launcher, activeLauncherSuppressInitialRounds);
            activeLauncherSuppressMultiShot =
                activeLauncherSuppressCapacity > 1 ||
                activeLauncherSuppressInitialRounds > 1;
            activeLauncherSuppressFirstShotAt = 0f;
            activeLauncherSuppressShotDetected = false;

            BattleRecorder.RecordGrenadeEvent(
                botOwner,
                "launcherFireProfile",
                $"{reason ?? "launcher"}:{(activeLauncherSuppressMultiShot ? "multi" : "single")}:loaded={activeLauncherSuppressInitialRounds}:capacity={activeLauncherSuppressCapacity}",
                goalEnemy: botOwner.Memory?.GoalEnemy);
        }

        private void ClearLauncherSuppressFireProfile()
        {
            activeLauncherSuppressInitialRounds = -1;
            activeLauncherSuppressLastRounds = -1;
            activeLauncherSuppressCapacity = 1;
            activeLauncherSuppressFirstShotAt = 0f;
            activeLauncherSuppressReloadStartedAt = 0f;
            nextLauncherSuppressReloadRequestAt = 0f;
            activeLauncherSuppressReloadRequested = false;
            activeLauncherSuppressCommitmentExpiredRecorded = false;
            activeLauncherSuppressMultiShot = false;
            activeLauncherSuppressShotDetected = false;
        }

        public bool IsGrenadeLauncherSuppressCommitmentExpired(string? reason, float suppressElapsed)
        {
            return IsGrenadeLauncherSuppressReason(reason) &&
                   !activeLauncherSuppressShotDetected &&
                   suppressElapsed >= GrenadeLauncherSuppressMinCommitmentSeconds;
        }

        private float GetLauncherSuppressEffectiveElapsed(float suppressElapsed)
        {
            if (activeLauncherSuppressReloadStartedAt <= 0f)
            {
                return suppressElapsed;
            }

            float reloadPauseSeconds = Mathf.Clamp(
                Time.time - activeLauncherSuppressReloadStartedAt,
                0f,
                GrenadeLauncherSuppressReloadWaitSeconds);
            return Mathf.Max(0f, suppressElapsed - reloadPauseSeconds);
        }

        private bool HasActiveLauncherSuppressTarget(EnemyInfo? goalEnemy)
        {
            return HasActiveCombatEnemy(goalEnemy) &&
                   (goalEnemy!.IsVisible || goalEnemy.CanShoot);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordLauncherSuppressCommitmentExpired(string? reason, EnemyInfo? goalEnemy)
        {
            if (!BattleRecorder.IsRecordingFor(botOwner, requireRecordedCombat: true))
            {
                return;
            }

            if (activeLauncherSuppressCommitmentExpiredRecorded)
            {
                return;
            }

            activeLauncherSuppressCommitmentExpiredRecorded = true;
            BattleRecorder.RecordGrenadeEvent(
                botOwner,
                "launcherCommitmentExpired",
                $"{reason ?? "launcher"}:targetActive={HasActiveLauncherSuppressTarget(goalEnemy)}",
                goalEnemy: goalEnemy);
        }

        private bool TryGetLauncherSuppressFireEndReason(bool suppressComplete, float suppressElapsed, out string endReason)
        {
            endReason = string.Empty;

            Weapon? launcher = GetActiveOrEquippedGrenadeLauncher();
            if (activeLauncherSuppressInitialRounds < 0)
            {
                StartLauncherSuppressFireProfile(activeFollowerSuppressReason);
            }

            int currentRounds = CountLoadedRounds(launcher);
            if (activeLauncherSuppressLastRounds >= 0 &&
                currentRounds < activeLauncherSuppressLastRounds)
            {
                int fired = activeLauncherSuppressLastRounds - currentRounds;
                activeLauncherSuppressShotDetected = true;
                if (activeLauncherSuppressFirstShotAt <= 0f)
                {
                    activeLauncherSuppressFirstShotAt = Time.time;
                }

                BattleRecorder.RecordGrenadeEvent(
                    botOwner,
                    "launcherShotObserved",
                    $"loaded={currentRounds}:fired={fired}:initial={activeLauncherSuppressInitialRounds}:capacity={activeLauncherSuppressCapacity}",
                    goalEnemy: botOwner.Memory?.GoalEnemy);
            }

            activeLauncherSuppressLastRounds = currentRounds;

            if (activeLauncherSuppressInitialRounds <= 0 && currentRounds <= 0)
            {
                if (TryKeepEmptyLauncherSuppressReloading(launcher, out endReason))
                {
                    return false;
                }

                endReason = "launcherNoLoadedRounds";
                return true;
            }

            if (!activeLauncherSuppressMultiShot)
            {
                if (activeLauncherSuppressShotDetected ||
                    currentRounds < activeLauncherSuppressInitialRounds)
                {
                    endReason = "launcherSingleShotFired";
                    return true;
                }

                if (suppressComplete)
                {
                    endReason = "launcherSingleShotComplete";
                    return true;
                }

                return false;
            }

            if (activeLauncherSuppressShotDetected)
            {
                if (currentRounds <= 0)
                {
                    endReason = "launcherMultiShotEmpty";
                    return true;
                }

                if (activeLauncherSuppressFirstShotAt > 0f &&
                    Time.time - activeLauncherSuppressFirstShotAt >= OrderedWeaponSuppressMaxSeconds)
                {
                    endReason = "launcherMultiShotTimedOut";
                    return true;
                }

                return false;
            }

            if (suppressComplete)
            {
                endReason = "launcherMultiShotCompleteNoShot";
                return true;
            }

            return false;
        }

        private bool TryKeepEmptyLauncherSuppressReloading(Weapon? launcher, out string endReason)
        {
            endReason = string.Empty;
            if (launcher == null ||
                botOwner.WeaponManager?.Reload == null)
            {
                endReason = "launcherReloadUnavailable";
                return false;
            }

            if (IsSingleUseLauncherWeapon(launcher))
            {
                endReason = "launcherNoLoadedRounds";
                return false;
            }

            if (!HasUsableEquippedGrenadeLauncher(botOwner))
            {
                endReason = "launcherNoLoadedRounds";
                return false;
            }

            if (activeLauncherSuppressReloadStartedAt <= 0f)
            {
                activeLauncherSuppressReloadStartedAt = Time.time;
                BattleRecorder.RecordGrenadeEvent(
                    botOwner,
                    "launcherReloadWait",
                    $"{activeFollowerSuppressReason ?? "launcher"}:loaded=0",
                    goalEnemy: botOwner.Memory?.GoalEnemy);
            }

            if (Time.time - activeLauncherSuppressReloadStartedAt > GrenadeLauncherSuppressReloadWaitSeconds)
            {
                endReason = "launcherReloadTimedOut";
                return false;
            }

            if (!IsEquippedGrenadeLauncherSelectedAndActive(botOwner))
            {
                // Empty cylinder launchers can trigger EFT's automatic holster fallback before
                // the reload request reaches an idle hands state. Keep this suppress attempt alive
                // and recover the equipped launcher inside the same bounded reload window.
                if (TrySelectEquippedGrenadeLauncher(
                        botOwner,
                        out bool changedToLauncher,
                        out EquipmentSlot launcherSlot) &&
                    changedToLauncher &&
                    launcherSlot == EquipmentSlot.SecondPrimaryWeapon)
                {
                    ownsGrenadeLauncherSwitch = true;
                }

                return true;
            }

            bool reloadStarted = TryStartActiveGrenadeLauncherLooseAmmoReload(
                botOwner,
                launcher,
                out string blockReason);
            if (!activeLauncherSuppressReloadRequested)
            {
                activeLauncherSuppressReloadRequested = true;
                BattleRecorder.RecordGrenadeEvent(
                    botOwner,
                    "launcherReloadStart",
                    $"{activeFollowerSuppressReason ?? "launcher"}:started={reloadStarted}:block={blockReason}",
                    goalEnemy: botOwner.Memory?.GoalEnemy);
            }

            return true;
        }

        private Weapon? GetActiveOrEquippedGrenadeLauncher()
        {
            return GetActiveOrEquippedGrenadeLauncher(botOwner);
        }

        internal static Weapon? GetActiveOrEquippedGrenadeLauncher(BotOwner? owner)
        {
            Weapon? activeWeapon = owner?.WeaponManager?.ShootController?.Item ??
                                   owner?.WeaponManager?.CurrentWeapon;
            if (IsGrenadeLauncherWeapon(activeWeapon))
            {
                return activeWeapon;
            }

            Weapon? firstPrimary = GetFirstPrimaryWeapon(owner);
            if (IsGrenadeLauncherWeapon(firstPrimary))
            {
                return firstPrimary;
            }

            Weapon? secondPrimary = GetSecondPrimaryWeapon(owner);
            return IsGrenadeLauncherWeapon(secondPrimary) ? secondPrimary : null;
        }

        internal static Vector3 GetGrenadeLauncherSuppressAimPoint(
            BotOwner? owner,
            Vector3 fireOrigin,
            Vector3 impactTarget)
        {
            Weapon? launcher = GetActiveOrEquippedGrenadeLauncher(owner);
            float speed = GetGrenadeLauncherMuzzleVelocity(launcher);
            return GetGrenadeLauncherSuppressAimPoint(fireOrigin, impactTarget, speed);
        }

        private static Vector3 GetGrenadeLauncherSuppressAimPoint(
            Vector3 fireOrigin,
            Vector3 impactTarget,
            float speed)
        {
            if (!IsFinite(fireOrigin) || !IsFinite(impactTarget))
            {
                return impactTarget;
            }

            Vector3 flatOffset = impactTarget - fireOrigin;
            flatOffset.y = 0f;
            float horizontalDistance = flatOffset.magnitude;
            if (horizontalDistance < GrenadeLauncherAimMinCompensationDistance ||
                speed <= 1f)
            {
                return impactTarget;
            }

            float heightDelta = impactTarget.y - fireOrigin.y;
            float speedSquared = speed * speed;
            float discriminant =
                speedSquared * speedSquared -
                GrenadeLauncherAimGravity *
                (GrenadeLauncherAimGravity * horizontalDistance * horizontalDistance + 2f * heightDelta * speedSquared);

            float raise;
            if (discriminant > 0f)
            {
                float lowArcTan = (speedSquared - Mathf.Sqrt(discriminant)) /
                                  (GrenadeLauncherAimGravity * horizontalDistance);
                raise = lowArcTan * horizontalDistance - heightDelta;
            }
            else
            {
                float travelTime = horizontalDistance / speed;
                raise = 0.5f * GrenadeLauncherAimGravity * travelTime * travelTime;
            }

            if (float.IsNaN(raise) || float.IsInfinity(raise))
            {
                return impactTarget;
            }

            raise = Mathf.Clamp(raise, 0f, GrenadeLauncherAimMaxCompensationHeight);
            return impactTarget + Vector3.up * raise;
        }

        private static float GetGrenadeLauncherMuzzleVelocity(Weapon? launcher)
        {
            if (launcher == null)
            {
                return GrenadeLauncherAimFallbackInitialSpeed;
            }

            float velocity = launcher.TotalVelocity;
            if (velocity <= 1f)
            {
                velocity = launcher.VelocityBase * launcher.SpeedFactor;
            }

            if (velocity <= 1f)
            {
                velocity = launcher.CurrentAmmoTemplate?.InitialSpeed ?? GrenadeLauncherAimFallbackInitialSpeed;
            }

            return velocity > 1f ? velocity : GrenadeLauncherAimFallbackInitialSpeed;
        }

        public static int CountLoadedRounds(Weapon? weapon)
        {
            if (weapon == null)
            {
                return 0;
            }

            int count = 0;
            MagazineItemClass? magazine = weapon.GetCurrentMagazine();
            if (magazine is CylinderMagazineItemClass cylinderMagazine)
            {
                count += Math.Max(0, cylinderMagazine.Count);
            }
            else if (magazine?.Cartridges != null)
            {
                count += Math.Max(0, magazine.Cartridges.Count);
            }
            else if (magazine != null)
            {
                count += Math.Max(0, magazine.Count);
            }

            if (weapon.Chambers != null)
            {
                for (int i = 0; i < weapon.Chambers.Length; i++)
                {
                    if (weapon.Chambers[i]?.ContainedItem != null)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static int GetLoadedCapacity(Weapon? weapon, int loadedRounds)
        {
            if (weapon == null)
            {
                return Math.Max(1, loadedRounds);
            }

            int capacity = Math.Max(loadedRounds, weapon.GetMaxMagazineCount());
            MagazineItemClass? magazine = weapon.GetCurrentMagazine();
            if (magazine is CylinderMagazineItemClass cylinderMagazine)
            {
                capacity = Math.Max(capacity, cylinderMagazine.MaxCount);
            }
            else if (magazine != null)
            {
                capacity = Math.Max(capacity, magazine.MaxCount);
            }

            if (weapon.Chambers != null)
            {
                capacity = Math.Max(capacity, weapon.Chambers.Length);
            }

            return Math.Max(1, capacity);
        }

        public void SetOrderedSuppressTarget(Vector3 target)
        {
            orderedSuppressTarget = IsFinite(target) ? target : Vector3.zero;
        }

        public void CommitPushDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            EnemyInfo? committedEnemy = botOwner.Memory?.GoalEnemy;
            if (committedEnemy != null &&
                IsAutoPushMissionDecision(decision) &&
                Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(committedEnemy))
            {
                return;
            }

            committedPushDecision = decision;
            committedPushEnemyProfileId = botOwner.Memory?.GoalEnemy?.ProfileId;
            committedPushMissionKind = null;
            if (committedEnemy != null &&
                !string.IsNullOrEmpty(committedEnemy.ProfileId) &&
                IsAutoPushMissionDecision(decision) &&
                !FollowerCombatTargetCommitments.IsActiveTemporaryTarget(botOwner, committedEnemy))
            {
                committedPushMissionKind = FollowerCombatTargetMissionKind.AutoPush;
                FollowerCombatTargetCommitments.SetMission(
                    botOwner,
                    committedEnemy,
                    FollowerCombatTargetMissionKind.AutoPush,
                    decision.Reason ?? "commitAutoPush");
            }

            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "push",
                "commit",
                decision.Reason,
                decision);
        }

        public void ClearCommittedPushDecision(string? reason = null)
        {
            if (committedPushDecision.HasValue)
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "push",
                    "clear",
                    reason ?? "clear",
                    committedPushDecision);
            }

            committedPushDecision = null;
            committedPushEnemyProfileId = null;
            if (committedPushMissionKind.HasValue)
            {
                FollowerCombatTargetCommitments.ClearMission(
                    botOwner,
                    committedPushMissionKind.Value,
                    reason ?? "clearCommittedPush");
            }

            committedPushMissionKind = null;
        }

        public bool HasCommittedPushDecision()
        {
            return committedPushDecision.HasValue;
        }

        public bool TryGetCommittedPushDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!committedPushDecision.HasValue)
            {
                return false;
            }

            if (!HasActiveCombatEnemy(goalEnemy) &&
                !TryRestoreMissionTargetIfReady("committedPushRestoreMission", out goalEnemy) &&
                !TryRestoreCommittedPushEnemy(out goalEnemy))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(committedPushEnemyProfileId) &&
                !string.Equals(goalEnemy.ProfileId, committedPushEnemyProfileId, StringComparison.Ordinal))
            {
                if (FollowerCombatTargetCommitments.IsActiveTemporaryTarget(botOwner, goalEnemy))
                {
                    return false;
                }

                return false;
            }

            decision = committedPushDecision.Value;
            return true;
        }

        public bool TryRestoreCommittedPushEnemy(out EnemyInfo? goalEnemy)
        {
            goalEnemy = botOwner.Memory?.GoalEnemy;
            if (!committedPushDecision.HasValue)
            {
                return false;
            }

            if ((!FollowerContactEnemyRetention.TryRestore(botOwner, out EnemyInfo? restored) || restored == null) &&
                !TryRestoreMissionTargetIfReady("restoreCommittedPushMission", out restored))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(committedPushEnemyProfileId) &&
                !string.Equals(restored.ProfileId, committedPushEnemyProfileId, StringComparison.Ordinal))
            {
                return false;
            }

            goalEnemy = restored;
            return IsTrackedEnemyAlive(restored);
        }

        public bool TryRestoreMissionTargetIfReady(string reason, out EnemyInfo? restored)
        {
            return FollowerCombatTargetCommitments.TryRestoreMissionIfTemporaryExpired(
                botOwner,
                reason,
                out restored);
        }

        public bool IsTemporaryEngagementTarget(EnemyInfo? goalEnemy)
        {
            return FollowerCombatTargetCommitments.IsActiveTemporaryTarget(botOwner, goalEnemy);
        }

        public bool IsCurrentGoalTemporaryEngagementTarget()
        {
            return FollowerCombatTargetCommitments.IsCurrentGoalTemporaryTarget(botOwner);
        }

        public void RefreshCommittedPushEnemyRetention()
        {
            if (!committedPushDecision.HasValue)
            {
                return;
            }

            FollowerContactEnemyRetention.RegisterCurrentGoal(botOwner, prioritized: true);
        }

        public bool IsCommittedPushEnemyChanged(EnemyInfo goalEnemy)
        {
            return !string.IsNullOrEmpty(committedPushEnemyProfileId) &&
                   !string.Equals(goalEnemy.ProfileId, committedPushEnemyProfileId, StringComparison.Ordinal);
        }

        public bool IsCommittedPushPausedByTemporaryTarget(EnemyInfo? goalEnemy)
        {
            return goalEnemy != null &&
                   IsCommittedPushEnemyChanged(goalEnemy) &&
                   IsTemporaryEngagementTarget(goalEnemy);
        }

        private static bool IsAutoPushMissionDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (decision.Reason == null ||
                decision.Reason.StartsWith("push.ordered", StringComparison.Ordinal))
            {
                return false;
            }

            return FollowerCombatPush.IsPushReason(decision.Reason) ||
                   FollowerCombatPush.IsStartWeakEnemyPushReason(decision.Reason);
        }

        public void CommitGrenadeDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            committedGrenadeDecision = decision;
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "grenade",
                "commit",
                decision.Reason,
                decision);
        }

        public void ClearCommittedGrenade(string? reason = null)
        {
            if (committedGrenadeDecision.HasValue)
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "grenade",
                    "clear",
                    reason ?? "clear",
                    committedGrenadeDecision);
            }

            committedGrenadeDecision = null;
        }

        public bool TryGetCommittedGrenadeDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!committedGrenadeDecision.HasValue)
            {
                return false;
            }

            BotGrenadeController? grenades = botOwner.WeaponManager?.Grenades;
            BotRequest? currentRequest = botOwner.BotRequestController?.CurRequest;
            bool grenadeSequenceActive =
                grenades != null &&
                (grenades.ThrowindNow || grenades.ReadyToThrow);
            bool grenadeRequestActive =
                currentRequest?.BotRequestType == BotRequestType.throwGrenade ||
                currentRequest?.BotRequestType == BotRequestType.throwGrenadeFromPlace;
            bool suppressActive = botOwner.SuppressGrenade != null && !botOwner.SuppressGrenade.Complete;

            if (!grenadeSequenceActive && !grenadeRequestActive && !suppressActive)
            {
                ClearCommittedGrenade("inactive");
                return false;
            }

            decision = committedGrenadeDecision.Value;
            return true;
        }

        public void CommitMovement(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            committedMovementDecision = decision;
            committedMovementEnemyProfileId = botOwner.Memory?.GoalEnemy?.ProfileId;
            committedMovementTarget = Vector3.zero;
            committedMovementCoverId = null;

            bool explicitPointDestination = UsesExplicitPointDestination(decision);
            bool healCoverDestination =
                IsReasonOrSubreason(decision.Reason, "runToHeal") ||
                IsReasonOrSubreason(decision.Reason, "moveToHeal");
            if (explicitPointDestination &&
                botOwner.GoToSomePointData?.HaveTarget() == true &&
                IsFinite(botOwner.GoToSomePointData.Point))
            {
                committedMovementTarget = botOwner.GoToSomePointData.Point;
            }
            else if (healCoverDestination && committedHealCover != null)
            {
                committedMovementTarget = committedHealCover.Position;
                committedMovementCoverId = committedHealCover.Id;
            }
            else if (!explicitPointDestination && committedCoverPoint != null)
            {
                committedMovementTarget = committedCoverPoint.Position;
                committedMovementCoverId = committedCoverPoint.Id;
            }
            else if (botOwner.GoToSomePointData?.HaveTarget() == true &&
                     IsFinite(botOwner.GoToSomePointData.Point))
            {
                committedMovementTarget = botOwner.GoToSomePointData.Point;
            }

            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "movement",
                "commit",
                decision.Reason,
                decision,
                IsFinite(committedMovementTarget) ? committedMovementTarget : null,
                committedMovementCoverId);
        }

        private static bool UsesExplicitPointDestination(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (decision.Action == BotLogicDecision.goToPoint ||
                decision.Action == BotLogicDecision.goToPointTactical)
            {
                return true;
            }

            if (decision.Action != BotLogicDecision.attackMoving &&
                decision.Action != BotLogicDecision.attackMovingWithSuppress)
            {
                return false;
            }

            return IsReasonOrSubreason(decision.Reason, "moveToHealPoint") ||
                   FollowerCombatRegroupObjective.IsRegroupReason(decision.Reason);
        }

        public void ClearCommittedMovement(string? reason = null)
        {
            if (committedMovementDecision.HasValue)
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "movement",
                    "clear",
                    reason ?? "clear",
                    committedMovementDecision,
                    IsFinite(committedMovementTarget) ? committedMovementTarget : null,
                    committedMovementCoverId);
            }

            committedMovementDecision = null;
            committedMovementEnemyProfileId = null;
            committedMovementTarget = Vector3.zero;
            committedMovementCoverId = null;
        }

        public bool HasCommittedMovement()
        {
            return committedMovementDecision.HasValue;
        }

        public bool IsSameCommittedMovement(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return committedMovementDecision.HasValue &&
                   committedMovementDecision.Value.Action == decision.Action &&
                   string.Equals(committedMovementDecision.Value.Reason, decision.Reason, StringComparison.Ordinal);
        }

        public bool ShouldCommitMovementDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool isPushDecision)
        {
            return IsMovementDecision(decision) &&
                   !isPushDecision &&
                   !IsCommittedHolderReason(decision.Reason);
        }

        public bool TryGetCommittedMovementDecision(
            EnemyInfo goalEnemy,
            bool hasExplicitRegroupOrder,
            bool hasActivePushOrder,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!committedMovementDecision.HasValue)
            {
                return false;
            }

            if (!HasActiveCombatEnemy(goalEnemy) ||
                hasExplicitRegroupOrder ||
                hasActivePushOrder)
            {
                ClearCommittedMovement(!HasActiveCombatEnemy(goalEnemy) ? "enemyMissing" : hasExplicitRegroupOrder ? "explicitRegroup" : "activePushOrder");
                return false;
            }

            if (!string.IsNullOrEmpty(committedMovementEnemyProfileId) &&
                !string.Equals(goalEnemy.ProfileId, committedMovementEnemyProfileId, StringComparison.Ordinal))
            {
                ClearCommittedMovement("enemyChanged");
                return false;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26> committed = committedMovementDecision.Value;
            if (ShouldInterruptCommittedMovement(goalEnemy, committed, hasActivePushOrder) ||
                HasCommittedMovementArrived(committed))
            {
                ClearCommittedMovement(HasCommittedMovementArrived(committed) ? "arrived" : "interrupted");
                return false;
            }

            decision = committed;
            return true;
        }

        private bool ShouldInterruptCommittedMovement(
            EnemyInfo goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool hasActivePushOrder)
        {
            if (HasImmediateExplosiveDanger())
            {
                return true;
            }

            // A synthetic heal hide point is a survival commitment. Incoming fire and ordinary
            // visibility are reasons to shoot while moving, not reasons to discard the route.
            // Only a true point-blank contact should force local self-defense before arrival.
            if (IsReasonOrSubreason(decision.Reason, "moveToHealPoint"))
            {
                return IsPointBlankVisibleShootableThreat(goalEnemy);
            }

            if (botOwner.Memory.IsUnderFire ||
                WasHitRecently(botOwner, 0.5f) ||
                FollowerAwareness.WasRecentlyHit(botOwner))
            {
                return true;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            if (goalEnemy.IsVisible &&
                goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetClosePushDistance())
            {
                return true;
            }

            if (ShouldBreakForBossUnderAttack(goalEnemy, hasActivePushOrder))
            {
                return true;
            }

            return decision.Action == BotLogicDecision.runToEnemy &&
                   !CanSprintForCombatMovement();
        }

        public bool HasImmediateExplosiveDanger()
        {
            if (botOwner == null)
            {
                return false;
            }

            if (botOwner.BewareGrenade?.ShallRunAway() == true ||
                botOwner.BewareBTR?.ShallRunAway() == true)
            {
                return true;
            }

            BotLogicDecision currentDecision = botOwner.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            return currentDecision == BotLogicDecision.runAwayGrenade ||
                   currentDecision == BotLogicDecision.runAwayBTR;
        }

        private bool HasCommittedMovementArrived(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return decision.Action switch
            {
                BotLogicDecision.runToCover => IsAtCommittedMovementDestination(),
                BotLogicDecision.goToPoint or BotLogicDecision.goToPointTactical => IsAtCommittedMovementDestination(),
                BotLogicDecision.attackMoving or BotLogicDecision.attackMovingWithSuppress => IsAtCommittedMovementDestination(),
                var action when action == (BotLogicDecision)CustomBotDecisions.attackRetreat => IsAtCommittedMovementDestination(),
                BotLogicDecision.runToEnemy or BotLogicDecision.goToEnemy => botOwner.Memory.GoalEnemy?.IsVisible == true &&
                                                                             botOwner.Memory.GoalEnemy.CanShoot,
                _ => false,
            };
        }

        private bool IsAtCommittedMovementDestination()
        {
            if (committedMovementCoverId.HasValue &&
                botOwner.Memory?.IsInCover == true &&
                botOwner.Memory.CurCustomCoverPoint != null &&
                botOwner.Memory.CurCustomCoverPoint.Id == committedMovementCoverId.Value)
            {
                return true;
            }

            if (!IsFinite(committedMovementTarget) ||
                committedMovementTarget.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            return (botOwner.Position - committedMovementTarget).sqrMagnitude <= 2f * 2f ||
                   HasCurrentGoToPointArrivedAt(committedMovementTarget);
        }

        private bool HasCurrentGoToPointArrivedAt(Vector3 destination)
        {
            return botOwner.GoToSomePointData?.HaveTarget() == true &&
                   botOwner.GoToSomePointData.IsCome() &&
                   IsFinite(botOwner.GoToSomePointData.Point) &&
                   (botOwner.GoToSomePointData.Point - destination).sqrMagnitude <=
                       TacticalPointBlacklistRadius * TacticalPointBlacklistRadius;
        }


        public bool HasCommittedPosition(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (committedPointTimer <= Time.time)
            {
                ClearCommittedPosition("expired");
                return false;
            }

            if (!committedPositionDecision.HasValue)
            {
                ClearCommittedPosition("missingDecision");
                return false;
            }

            if (!committedPosition.HasValue && committedHoldCoverPoint == null)
            {
                ClearCommittedPosition("missingTarget");
                return false;
            }

            if (ShouldBreakCommittedPositionHold())
            {
                ClearCommittedPosition("break");
                return false;
            }

            if (committedHoldCoverPoint != null)
            {
                if (!IsCommittedHoldCoverStillValid())
                {
                    ClearCommittedPosition("coverInvalid");
                    return false;
                }
            }

            HoldFor(Mathf.Max(0.1f, committedPointTimer - Time.time));
            decision = committedPositionDecision.Value;
            return true;
        }

        public bool InHoldingCover(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return HasCommittedPosition(out decision) && committedHoldCoverPoint != null;
        }

        public bool InHoldingPosition(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return HasCommittedPosition(out decision) && committedPosition != null;
        }

        public bool IsCommittedHolderReason(string? reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                   (reason.StartsWith("committedCoverHold", StringComparison.Ordinal) ||
                    reason.StartsWith("committedPositionHold", StringComparison.Ordinal));
        }

        public bool IsCommittedHolderTimerActive()
        {
            return committedPositionDecision.HasValue &&
                   committedPointTimer > Time.time;
        }

        public void ClearCommittedPosition(string? reason = null)
        {
            if (committedPositionDecision.HasValue)
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    committedHoldCoverPoint != null ? "arrivalHold.cover" : "arrivalHold.position",
                    "clear",
                    reason ?? "clear",
                    committedPositionDecision,
                    committedHoldCoverPoint != null ? committedHoldCoverPoint.Position : committedPosition,
                    committedHoldCoverPoint?.Id);
            }

            committedPosition = null;
            committedPointTimer = 0f;
            committedPointSetAt = 0f;
            committedHoldCoverPoint = null;
            committedPositionDecision = null;
            committedPointReason = null;
        }

        public void SetCommittedCover(CustomNavigationPoint cover, AICoreActionResultStruct<BotLogicDecision, GClass26> decision, float coverDuration = 0f)
        {
            ClearCommittedPosition("replace");
            committedHoldCoverPoint = cover;
            committedPositionDecision = decision;
            committedPointReason = decision.Reason;
            committedPointSetAt = Time.time;
            committedPointTimer = Time.time + (coverDuration > 0f ? coverDuration : GetCommittedCoverHoldDuration(decision.Reason));
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "arrivalHold.cover",
                "commit",
                decision.Reason,
                decision,
                cover.Position,
                cover.Id,
                true,
                committedPointTimer);
        }

        public void SetCommittedPosition(Vector3 position, AICoreActionResultStruct<BotLogicDecision, GClass26> decision, float positionDuration = 0f)
        {
            ClearCommittedPosition("replace");
            committedPosition = position;
            committedPositionDecision = decision;
            committedPointReason = decision.Reason;
            committedPointSetAt = Time.time;
            committedPointTimer = Time.time + (positionDuration > 0f ? positionDuration : GetCommittedPositionHoldDuration(decision.Reason));
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "arrivalHold.position",
                "commit",
                decision.Reason,
                decision,
                position,
                null,
                false,
                committedPointTimer);
        }

        public bool HasCommittedHolderSettled(float seconds)
        {
            return committedPositionDecision.HasValue &&
                   committedPointSetAt > 0f &&
                   Time.time - committedPointSetAt >= seconds;
        }

        public void ArmCommittedArrivalHold(string? reason, bool preferCover = true)
        {
            // Arrival hold is the anti-churn bridge between "I reached the destination" and
            // "I am allowed to plan again". It does not force the bot to leave cover when the
            // timer expires; it only stops immediate re-selection of another movement action.
            string holdReason = CreateCommittedHoldReason(reason, preferCover);
            AICoreActionResultStruct<BotLogicDecision, GClass26> holdDecision =
                new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, holdReason);

            if (preferCover)
            {
                CustomNavigationPoint? cover = committedCoverPoint ?? botOwner.Memory?.CurCustomCoverPoint;
                if (IsValidArrivalHoldCover(cover))
                {
                    SetCommittedCover(cover, holdDecision, GetCommittedCoverHoldDuration(reason));
                    return;
                }
            }

            Vector3 position = botOwner.Position;
            if (botOwner.GoToSomePointData?.HaveTarget() == true && IsFinite(botOwner.GoToSomePointData.Point))
            {
                position = botOwner.GoToSomePointData.Point;
            }

            SetCommittedPosition(position, holdDecision, GetCommittedPositionHoldDuration(reason));
        }

        public void ArmCommittedRecoveryArrivalHold(string? reason)
        {
            ArmCommittedArrivalHold(CreateRecoveryManeuverReason(reason), preferCover: true);
        }

        private bool IsValidArrivalHoldCover(CustomNavigationPoint? cover)
        {
            if (cover == null || !IsFinite(cover.Position))
            {
                return false;
            }

            if (botOwner.Memory.IsInCover &&
                botOwner.Memory.CurCustomCoverPoint != null &&
                botOwner.Memory.CurCustomCoverPoint.Id == cover.Id)
            {
                return true;
            }

            return IsWithinCommittedCoverArrivalHoldDistance(cover);
        }

        private bool ShouldBreakCommittedPositionHold()
        {
            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (botOwner.Memory.IsUnderFire ||
                WasHitRecently(botOwner, 0.75f) ||
                FollowerAwareness.WasRecentlyHit(botOwner))
            {
                // A recovery move that has actually reached its committed cover needs a short,
                // stable firing window. Releasing it immediately under the same incoming pressure
                // only reselects the already-reached cover and churns the action every frame.
                if (IsRecoveryManeuverReason(committedPointReason) &&
                    IsValidArrivalHoldCover(committedHoldCoverPoint))
                {
                    return false;
                }

                return true;
            }

            if (IsCommittedHoldEnemyContact(goalEnemy))
            {
                return true;
            }

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            if (followerData != null &&
                followerData.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) &&
                (command == FollowerCommandType.PushEnemy ||
                 command == FollowerCommandType.RegroupNearBoss ||
                 command == FollowerCommandType.SuppressEnemy ||
                 command == FollowerCommandType.CombatComeToBossCover ||
                 command == FollowerCommandType.CombatMoveToPointTactical))
            {
                return true;
            }

            return false;
        }

        private bool IsCommittedHoldEnemyContact(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            if (goalEnemy.IsVisible && goalEnemy.Distance <= CloseVisibleThreatBreakDistance)
            {
                return true;
            }

            return goalEnemy.CanShoot &&
                   (goalEnemy.IsVisible || Enemy.IsVisible(botOwner, goalEnemy));
        }

        private bool IsCommittedHoldCoverStillValid()
        {
            if (committedHoldCoverPoint == null)
            {
                return false;
            }

            if (botOwner.Memory.IsInCover &&
                botOwner.Memory.CurCustomCoverPoint != null &&
                botOwner.Memory.CurCustomCoverPoint.Id == committedHoldCoverPoint.Id)
            {
                return true;
            }

            return IsWithinCommittedCoverArrivalHoldDistance(committedHoldCoverPoint);
        }

        private static string CreateCommittedHoldReason(string? reason, bool cover)
        {
            string prefix = cover ? "committedCoverHold" : "committedPositionHold";
            return string.IsNullOrEmpty(reason)
                ? prefix
                : $"{prefix}.{reason}";
        }

        private static float GetCommittedCoverHoldDuration(string? reason)
        {
            if (IsReasonOrSubreason(reason, "runToHeal") || IsReasonOrSubreason(reason, "moveToHeal"))
            {
                return HealingCommittedHoldSeconds;
            }

            if (IsReasonOrSubreason(reason, "shootCover") || IsReasonOrSubreason(reason, "retreatShootCover"))
            {
                return ShootCommittedCoverHoldSeconds;
            }

            if (IsReasonOrSubreason(reason, "retreatSafeCover") ||
                IsReasonOrSubreason(reason, "retreatWeakCover") ||
                IsReasonOrSubreason(reason, "safeCover"))
            {
                return RetreatCommittedCoverHoldSeconds;
            }

            if (IsReasonOrSubreason(reason, "bossCover") || IsReasonOrSubreason(reason, "protectBossCover"))
            {
                return BossCommittedCoverHoldSeconds;
            }

            return DefaultCommittedCoverHoldSeconds;
        }

        private static float GetCommittedPositionHoldDuration(string? reason)
        {
            if (IsReasonOrSubreason(reason, "runToHeal") || IsReasonOrSubreason(reason, "moveToHeal"))
            {
                return HealingCommittedHoldSeconds;
            }

            return DefaultCommittedPositionHoldSeconds;
        }

        public static bool IsReasonOrSubreason(string? reason, string baseReason)
        {
            return string.Equals(reason, baseReason, StringComparison.Ordinal) ||
                   (!string.IsNullOrEmpty(reason) &&
                    reason.StartsWith(baseReason + ".", StringComparison.Ordinal));
        }

        public static bool IsMedicalRetreatMovementReason(string? reason)
        {
            return IsReasonOrSubreason(reason, "runToHeal") ||
                   IsReasonOrSubreason(reason, "moveToHeal") ||
                   IsReasonOrSubreason(reason, "moveToHealPoint");
        }

        private static bool IsMedicalCombatFallbackReason(string? reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                   reason.StartsWith("healRetreat", StringComparison.Ordinal);
        }

        /// <summary>
        /// Classifies the decision's tactical intent, not only the BigBrain action that executes it.
        /// Generic movement actions can be medical retreat phases and must retain medical end routing.
        /// </summary>
        public static bool IsMedicalDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return decision.Action == BotLogicDecision.heal ||
                   decision.Action == BotLogicDecision.healStimulators ||
                   IsMedicalRetreatMovementReason(decision.Reason) ||
                   IsMedicalCombatFallbackReason(decision.Reason);
        }

        public bool IsInFight(BotLogicDecision decision)
        {
            bool engaged = decision switch
            {
                BotLogicDecision.shootFromStationary or
                BotLogicDecision.shootFromCover or
                BotLogicDecision.shootFromPlace or
                BotLogicDecision.suppressGrenade => true,
                _ => false
            };

            if (!engaged && decision == BotLogicDecision.suppressFire && IsEnemyVisibleAndShootable())
            {
                engaged = true;
            }

            return engaged;
        }

        public bool HasActiveCombatGestureOrder()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   (command == FollowerCommandType.CombatComeToBossCover ||
                    command == FollowerCommandType.CombatMoveToPointTactical);
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> CreateRegroupObjectiveDecision()
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.standBy,
                FollowerCombatRegroupObjective.ActivateRegroupReason);
        }
        /// <summary>
        /// Returns the active tactic so combat branches can bias toward protection or ranged play.
        /// </summary>
        public FollowerCombatTactic GetFollowerTactic()
        {
            return BossPlayers.Instance?.GetFollower(botOwner)?.CombatTactic ?? FollowerCombatTactic.Balanced;
        }

        /// <summary>
        /// Reads the configured follower aggression as a normalized 0-1 value.
        /// </summary>
        public float GetAggression01()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            float aggression = followerData?.EffectiveCombatAggression ?? 50f;
            aggression = FollowerWeaponAggressionOverrides.Apply(botOwner, aggression);
            return Mathf.Clamp01(aggression / 100f);
        }

        public float GetBossProtectionWillingness01()
        {
            return Mathf.Clamp01(BossPlayers.Instance?.GetFollower(botOwner)?.BossProtectionWillingness01 ?? 1f);
        }

        public bool IsTemporaryHoldPositionAggressionActive()
        {
            return BossPlayers.Instance?.GetFollower(botOwner)?.IsTemporaryHoldPositionAggressionActive == true;
        }

        public bool HaveCoverToShoot { get; private set; }
        public CustomNavigationPoint? PointToShoot { get; private set; }

        public bool IsEnemyVisibleAndShootable()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            return HasActiveCombatEnemy(goalEnemy) && goalEnemy.CanShoot && goalEnemy.IsVisible;
        }

        public bool HasActiveCombatEnemy()
        {
            return HasActiveCombatEnemy(botOwner.Memory.GoalEnemy);
        }

        public bool HasActiveCombatEnemy(EnemyInfo? goalEnemy)
        {
            if (!botOwner.Memory.HaveEnemy || goalEnemy == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                Player? alivePlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(goalEnemy.ProfileId);
                return alivePlayer?.HealthController?.IsAlive == true;
            }

            return goalEnemy.Person?.HealthController?.IsAlive == true;
        }

        private bool HasActiveOrRetainedGoalEnemy(out EnemyInfo? goalEnemy)
        {
            goalEnemy = botOwner.Memory.GoalEnemy;
            if (HasActiveCombatEnemy(goalEnemy))
            {
                return true;
            }

            return FollowerContactEnemyRetention.TryRestore(botOwner, out goalEnemy) && goalEnemy != null;
        }

        public bool HasAnyActiveCombatEnemy()
        {
            if (botOwner?.EnemiesController?.EnemyInfos == null)
            {
                return false;
            }

            foreach (EnemyInfo enemyInfo in botOwner.EnemiesController.EnemyInfos.Values)
            {
                if (IsTrackedEnemyAlive(enemyInfo))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// After GoalEnemy disappears, briefly rechecks only living enemies for which this
        /// follower has recent personal contact. The scan turns toward frozen personal
        /// last-known positions; it never uses an unseen player's live position for steering.
        /// </summary>
        public bool TryGetTargetHandoffScanDecision(
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (HasActiveCombatGestureOrder())
            {
                ClearTargetHandoffScan("combatGestureOrder");
                return false;
            }

            if (HasActiveCombatEnemy())
            {
                ClearTargetHandoffScan("goalEnemyRestored");
                return false;
            }

            if (targetHandoffScanActive)
            {
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    TargetHandoffScanReason);
                return true;
            }

            if (!TryBuildTargetHandoffCandidates(out int signature))
            {
                return false;
            }

            if (Time.time < targetHandoffRetryBlockedUntil &&
                signature == failedTargetHandoffSignature)
            {
                targetHandoffCandidates.Clear();
                return false;
            }

            targetHandoffScanActive = true;
            targetHandoffScanUntil = Time.time + TargetHandoffScanDurationSeconds;
            nextTargetHandoffProbeAt = Time.time + TargetHandoffProbeIntervalSeconds;
            targetHandoffCandidateIndex = 0;
            targetHandoffProbeCount = 0;
            targetHandoffStartDamageRevision = FollowerAwareness.GetDamageRevision(botOwner);
            targetHandoffScanSignature = signature;

            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                TargetHandoffScanReason);
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "targetHandoffScan",
                "begin",
                $"candidates:{targetHandoffCandidates.Count}",
                decision,
                targetHandoffCandidates[0].LookPoint,
                untilTime: targetHandoffScanUntil);
            SetCurrentTargetHandoffLookPoint();
            return true;
        }

        public void ClearTargetHandoffScan(string reason)
        {
            FinishTargetHandoffScan(reason, blockUnchangedRetry: false);
        }

        private AICoreActionEndStruct EndTargetHandoffScan()
        {
            if (!targetHandoffScanActive)
            {
                FollowerAwareness.ClearTargetHandoffLookPoint(botOwner);
                return new AICoreActionEndStruct("targetHandoffInactive", true);
            }

            if (HasActiveCombatEnemy())
            {
                FinishTargetHandoffScan("goalEnemyAcquired", blockUnchangedRetry: false);
                return new AICoreActionEndStruct("targetHandoffAcquired", true);
            }

            if (FollowerAwareness.GetDamageRevision(botOwner) != targetHandoffStartDamageRevision)
            {
                FinishTargetHandoffScan("damagedDuringScan", blockUnchangedRetry: true);
                return new AICoreActionEndStruct("targetHandoffDamaged", true);
            }

            if (Time.time >= nextTargetHandoffProbeAt &&
                targetHandoffProbeCount < TargetHandoffMaxProbes &&
                TryProbeCurrentTargetHandoffCandidate())
            {
                FinishTargetHandoffScan("directContactPromoted", blockUnchangedRetry: false);
                return new AICoreActionEndStruct("targetHandoffPromoted", true);
            }

            if (Time.time >= targetHandoffScanUntil ||
                targetHandoffProbeCount >= TargetHandoffMaxProbes)
            {
                FinishTargetHandoffScan("graceExpired", blockUnchangedRetry: true);
                return new AICoreActionEndStruct("targetHandoffExpired", true);
            }

            return Continue();
        }

        private bool TryBuildTargetHandoffCandidates(out int signature)
        {
            signature = 17;
            targetHandoffCandidates.Clear();
            if (botOwner?.EnemiesController?.EnemyInfos == null)
            {
                return false;
            }

            foreach (var item in botOwner.EnemiesController.EnemyInfos)
            {
                EnemyInfo? enemyInfo = item.Value;
                if (!IsTrackedEnemyAlive(enemyInfo) ||
                    !Enemy.HasPersonalContactRecord(enemyInfo))
                {
                    continue;
                }

                string enemyProfileId = enemyInfo.ProfileId ?? item.Key?.ProfileId ?? string.Empty;
                if (string.IsNullOrEmpty(enemyProfileId))
                {
                    continue;
                }

                float lastSeenTime = Mathf.Max(enemyInfo.PersonalSeenTime, enemyInfo.PersonalLastSeenTime);
                bool directContact = Enemy.HasDirectPersonalContact(enemyInfo);
                if (!directContact &&
                    (lastSeenTime <= 0f || Time.time - lastSeenTime > TargetHandoffRecentPersonalContactSeconds))
                {
                    continue;
                }

                Vector3 personalLastPosition = enemyInfo.PersonalLastPos;
                if (!IsFinite(personalLastPosition) || personalLastPosition.sqrMagnitude <= 0.01f)
                {
                    continue;
                }

                Vector3 lookPoint = personalLastPosition + Vector3.up * 0.8f;
                Vector3 toLookPoint = lookPoint - botOwner.Position;
                float distance = toLookPoint.magnitude;
                float turnAngle = toLookPoint.sqrMagnitude > 0.01f
                    ? Vector3.Angle(botOwner.LookDirection, toLookPoint.normalized)
                    : 180f;
                float age = lastSeenTime > 0f
                    ? Mathf.Clamp(Time.time - lastSeenTime, 0f, TargetHandoffRecentPersonalContactSeconds)
                    : TargetHandoffRecentPersonalContactSeconds;
                float score =
                    (enemyInfo.CanShoot ? 1000f : enemyInfo.IsVisible ? 850f : 0f) +
                    (TargetHandoffRecentPersonalContactSeconds - age) * 100f -
                    Mathf.Min(distance, 200f) * 0.5f -
                    turnAngle * 0.5f;

                InsertTargetHandoffCandidate(new TargetHandoffCandidate(
                    enemyProfileId,
                    lookPoint,
                    score,
                    lastSeenTime));
            }

            if (targetHandoffCandidates.Count == 0)
            {
                return false;
            }

            unchecked
            {
                foreach (TargetHandoffCandidate candidate in targetHandoffCandidates)
                {
                    signature = signature * 31 + candidate.EnemyProfileId.GetHashCode();
                    signature = signature * 31 + Mathf.RoundToInt(candidate.LastSeenTime * 10f);
                    signature = signature * 31 + Mathf.RoundToInt(candidate.LookPoint.x * 2f);
                    signature = signature * 31 + Mathf.RoundToInt(candidate.LookPoint.z * 2f);
                }
            }

            return true;
        }

        private void InsertTargetHandoffCandidate(TargetHandoffCandidate candidate)
        {
            int insertAt = 0;
            while (insertAt < targetHandoffCandidates.Count &&
                   targetHandoffCandidates[insertAt].Score >= candidate.Score)
            {
                insertAt++;
            }

            if (insertAt >= TargetHandoffMaxCandidates)
            {
                return;
            }

            targetHandoffCandidates.Insert(insertAt, candidate);
            if (targetHandoffCandidates.Count > TargetHandoffMaxCandidates)
            {
                targetHandoffCandidates.RemoveAt(TargetHandoffMaxCandidates);
            }
        }

        private bool TryProbeCurrentTargetHandoffCandidate()
        {
            if (targetHandoffCandidates.Count == 0)
            {
                targetHandoffProbeCount = TargetHandoffMaxProbes;
                return false;
            }

            TargetHandoffCandidate candidate = targetHandoffCandidates[targetHandoffCandidateIndex];
            bool promoted = false;
            string result = "notTracked";
            if (TryGetTrackedEnemy(candidate.EnemyProfileId, out EnemyInfo? enemyInfo))
            {
                if (!IsTrackedEnemyAlive(enemyInfo))
                {
                    result = "dead";
                }
                else if (FollowerEnemyInfoCorrection.RefreshDirectContactForAcquisition(botOwner, enemyInfo) &&
                         Enemy.HasDirectPersonalContact(enemyInfo))
                {
                    promoted = TryPromoteTrackedEnemyAsGoal(candidate.EnemyProfileId);
                    result = promoted ? "promoted" : "promotionRejected";
                }
                else
                {
                    result = "notDirect";
                }
            }

            targetHandoffProbeCount++;
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "targetHandoffScan",
                "probe",
                $"{candidate.EnemyProfileId}:{result}",
                target: candidate.LookPoint,
                untilTime: targetHandoffScanUntil);
            if (promoted)
            {
                return true;
            }

            targetHandoffCandidateIndex =
                (targetHandoffCandidateIndex + 1) % targetHandoffCandidates.Count;
            nextTargetHandoffProbeAt = Time.time + TargetHandoffProbeIntervalSeconds;
            if (targetHandoffProbeCount < TargetHandoffMaxProbes)
            {
                SetCurrentTargetHandoffLookPoint();
            }

            return false;
        }

        private bool TryGetTrackedEnemy(string enemyProfileId, out EnemyInfo? enemyInfo)
        {
            enemyInfo = null;
            if (botOwner?.EnemiesController?.EnemyInfos == null)
            {
                return false;
            }

            foreach (var item in botOwner.EnemiesController.EnemyInfos)
            {
                string trackedProfileId = item.Value?.ProfileId ?? item.Key?.ProfileId ?? string.Empty;
                if (string.Equals(trackedProfileId, enemyProfileId, StringComparison.Ordinal))
                {
                    enemyInfo = item.Value;
                    return enemyInfo != null;
                }
            }

            return false;
        }

        private void SetCurrentTargetHandoffLookPoint()
        {
            if (!targetHandoffScanActive || targetHandoffCandidates.Count == 0)
            {
                return;
            }

            TargetHandoffCandidate candidate = targetHandoffCandidates[targetHandoffCandidateIndex];
            FollowerAwareness.SetTargetHandoffLookPoint(
                botOwner,
                candidate.LookPoint,
                targetHandoffScanUntil);
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "targetHandoffScan",
                "look",
                candidate.EnemyProfileId,
                target: candidate.LookPoint,
                untilTime: targetHandoffScanUntil);
        }

        private void FinishTargetHandoffScan(string reason, bool blockUnchangedRetry)
        {
            if (!targetHandoffScanActive)
            {
                return;
            }

            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "targetHandoffScan",
                "end",
                reason,
                untilTime: targetHandoffScanUntil);
            if (blockUnchangedRetry)
            {
                failedTargetHandoffSignature = targetHandoffScanSignature;
                targetHandoffRetryBlockedUntil = Time.time + TargetHandoffFailedRetrySeconds;
            }
            else
            {
                failedTargetHandoffSignature = 0;
                targetHandoffRetryBlockedUntil = 0f;
            }

            targetHandoffScanActive = false;
            targetHandoffScanUntil = 0f;
            nextTargetHandoffProbeAt = 0f;
            targetHandoffCandidateIndex = 0;
            targetHandoffProbeCount = 0;
            targetHandoffStartDamageRevision = 0;
            targetHandoffScanSignature = 0;
            targetHandoffCandidates.Clear();
            FollowerAwareness.ClearTargetHandoffLookPoint(botOwner);
        }

        private static bool IsTrackedEnemyAlive(EnemyInfo? enemyInfo)
        {
            if (enemyInfo == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(enemyInfo.ProfileId))
            {
                Player? alivePlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(enemyInfo.ProfileId);
                return alivePlayer?.HealthController?.IsAlive == true;
            }

            return enemyInfo.Person?.HealthController?.IsAlive == true;
        }

        private static bool HasActiveCombatEnemy(BotOwner botOwner, EnemyInfo? goalEnemy)
        {
            if (botOwner?.Memory?.HaveEnemy != true || goalEnemy == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                Player? alivePlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(goalEnemy.ProfileId);
                return alivePlayer?.HealthController?.IsAlive == true;
            }

            return goalEnemy.Person?.HealthController?.IsAlive == true;
        }

        /// <summary>
        /// Promotes an already-tracked enemy to the follower's current goal without forcing a new acquire path.
        /// </summary>
        public bool TryPromoteTrackedEnemyAsGoal(string enemyProfileId)
        {
            if (string.IsNullOrEmpty(enemyProfileId) || botOwner?.EnemiesController?.EnemyInfos == null)
            {
                return false;
            }

            foreach (var item in botOwner.EnemiesController.EnemyInfos)
            {
                if (item.Key?.ProfileId != enemyProfileId)
                {
                    continue;
                }

                if (Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(item.Value))
                {
                    return false;
                }

                item.Value.PriorityIndex = 0;
                Enemy.RepairPersonalMemory(item.Value, item.Key.Position, Enemy.HasDirectPersonalContact(item.Value));
                if (FollowerCombatTargetCommitments.HasMission(botOwner) &&
                    !FollowerCombatTargetCommitments.IsMissionTarget(botOwner, item.Value) &&
                    !FollowerCombatTargetCommitments.TryRegisterTemporaryTarget(
                        botOwner,
                        item.Value,
                        "trackedEnemyPromotion",
                        out _))
                {
                    return false;
                }

                using (FollowerGoalEnemyTracker.Begin("FollowerCombatCommon.TryPromoteTrackedEnemyAsGoal", "trackedEnemyPromotion"))
                {
                    botOwner.Memory.GoalEnemy = item.Value;
                }
                return true;
            }

            return false;
        }

        public bool TryForceGoalEnemy(string enemyProfileId, string reason, out EnemyInfo? forcedEnemy)
        {
            forcedEnemy = null;
            if (string.IsNullOrEmpty(enemyProfileId) || botOwner?.Memory == null)
            {
                return false;
            }

            Player? enemyPlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(enemyProfileId);
            if (enemyPlayer?.HealthController?.IsAlive != true)
            {
                return false;
            }

            EnemyInfo? enemyInfo = Enemy.MakeEnemy(
                botOwner,
                enemyPlayer,
                EBotEnemyCause.checkAddTODO,
                countSharedSeenAsPersonal: false);
            if (enemyInfo == null)
            {
                return false;
            }

            EnemyInfo? currentGoal = botOwner.Memory.GoalEnemy;
            bool alreadyGoal = string.Equals(currentGoal?.ProfileId, enemyProfileId, StringComparison.Ordinal);
            if (!alreadyGoal)
            {
                // Explicit retarget orders need a stronger hand-off than priority scoring. Clear
                // the current goal and retention once, then install the requested enemy as the new
                // goal so vanilla/group sorting cannot immediately bounce us back to the old target.
                FollowerContactEnemyRetention.ClearAndAllowNextGoalClear(botOwner);
                using (FollowerGoalEnemyTracker.Begin("FollowerCombatCommon.TryForceGoalEnemy", $"clearPrevious:{reason}"))
                {
                    botOwner.Memory.GoalEnemy = null;
                }
                botOwner.Memory.LastEnemy = null;
            }

            enemyInfo.PriorityIndex = 0;
            enemyInfo.IgnoreUntilAggression = false;
            enemyInfo.SetVisible(enemyInfo.IsVisible);
            Enemy.RepairPersonalMemory(enemyInfo, enemyPlayer.Position, Enemy.HasDirectPersonalContact(enemyInfo));
            botOwner.Memory.IsPeace = false;
            using (FollowerGoalEnemyTracker.Begin("FollowerCombatCommon.TryForceGoalEnemy", reason))
            {
                botOwner.Memory.GoalEnemy = enemyInfo;
            }
            FollowerContactEnemyRetention.Register(botOwner, enemyPlayer, enemyInfo.IsVisible || enemyInfo.CanShoot, prioritized: true);
            forcedEnemy = enemyInfo;
            return HasActiveCombatEnemy(enemyInfo);
        }

        public bool TryForceGoalEnemy(BotOwner enemyBot, string reason, out EnemyInfo? forcedEnemy)
        {
            forcedEnemy = null;
            if (enemyBot?.GetPlayer?.HealthController?.IsAlive != true || string.IsNullOrEmpty(enemyBot.ProfileId))
            {
                return false;
            }

            return TryForceGoalEnemy(enemyBot.ProfileId, reason, out forcedEnemy);
        }

        public bool TryUseSupportGoalEnemy(BotOwner enemyBot, string reason, out EnemyInfo? supportEnemy)
        {
            supportEnemy = null;
            if (enemyBot?.GetPlayer?.HealthController?.IsAlive != true || string.IsNullOrEmpty(enemyBot.ProfileId))
            {
                return false;
            }

            if (!FollowerCombatTargetCommitments.HasMission(botOwner) ||
                FollowerCombatTargetCommitments.IsMissionTarget(botOwner, enemyBot.ProfileId))
            {
                return TryForceGoalEnemy(enemyBot.ProfileId, reason, out supportEnemy);
            }

            return TryUseTemporaryGoalEnemy(enemyBot.ProfileId, reason, out supportEnemy);
        }

        private bool TryUseTemporaryGoalEnemy(string enemyProfileId, string reason, out EnemyInfo? temporaryEnemy)
        {
            temporaryEnemy = null;
            if (string.IsNullOrEmpty(enemyProfileId) || botOwner?.Memory == null)
            {
                return false;
            }

            Player? enemyPlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(enemyProfileId);
            if (enemyPlayer?.HealthController?.IsAlive != true)
            {
                return false;
            }

            EnemyInfo? enemyInfo = Enemy.MakeEnemy(
                botOwner,
                enemyPlayer,
                EBotEnemyCause.checkAddTODO,
                countSharedSeenAsPersonal: false);
            if (enemyInfo == null)
            {
                return false;
            }

            enemyInfo.PriorityIndex = 0;
            enemyInfo.IgnoreUntilAggression = false;
            enemyInfo.SetVisible(enemyInfo.IsVisible);
            Enemy.RepairPersonalMemory(enemyInfo, enemyPlayer.Position, Enemy.HasDirectPersonalContact(enemyInfo));

            if (!FollowerCombatTargetCommitments.TryRegisterTemporaryTarget(
                    botOwner,
                    enemyInfo,
                    reason,
                    out _))
            {
                return false;
            }

            botOwner.Memory.IsPeace = false;
            using (FollowerGoalEnemyTracker.Begin("FollowerCombatCommon.TryUseTemporaryGoalEnemy", reason))
            {
                botOwner.Memory.GoalEnemy = enemyInfo;
            }

            temporaryEnemy = botOwner.Memory.GoalEnemy;
            return string.Equals(temporaryEnemy?.ProfileId, enemyInfo.ProfileId, StringComparison.Ordinal) &&
                   HasActiveCombatEnemy(temporaryEnemy);
        }

        /// <summary>
        /// Applies the default follower aggression-to-threat mapping used by the core combat path.
        /// </summary>
        public bool IsEnemyLowThreat(EnemyInfo goalEnemy, float aggression01)
        {
            bool ignoreEquip = aggression01 >= 0.4f;
            float maximumEnemies = aggression01 >= 0.7f ? 3f : aggression01 >= 0.4f ? 2f : 1f;
            return IsEnemyLowThreat(goalEnemy, ignoreEquip, maximumEnemies);
        }

        public AutoPushWeaponThreatPolicy GetAutoPushWeaponThreatPolicy(EnemyInfo goalEnemy)
        {
            if (!IsArmorRelevantAutoPushTarget(goalEnemy) ||
                !TryGetCurrentAutoPushAmmoProfile(out AutoPushAmmoProfile ammoProfile))
            {
                return AutoPushWeaponThreatPolicy.Normal;
            }

            bool bossOrRaider = IsBossOrRaiderAutoPushTarget(goalEnemy);
            int gameplayLevel = GetBossGameplayLevel();

            if (ammoProfile.PenetrationPower < LowPenetrationAutoPushThreshold)
            {
                if (!bossOrRaider && gameplayLevel <= LowPenetrationNormalPmcGameplayLevel)
                {
                    return AutoPushWeaponThreatPolicy.Normal;
                }

                return CanHighCapacitySoftenLowPenetration(ammoProfile, bossOrRaider)
                    ? AutoPushWeaponThreatPolicy.Cautious
                    : AutoPushWeaponThreatPolicy.VeryCloseOrOrderedOnly;
            }

            if (ammoProfile.PenetrationPower <= CautiousPenetrationAutoPushThreshold)
            {
                if (!bossOrRaider && gameplayLevel <= CautiousPenetrationNormalPmcGameplayLevel)
                {
                    return AutoPushWeaponThreatPolicy.Normal;
                }

                return CanHighCapacityNormalizeCautiousPenetration(ammoProfile, bossOrRaider)
                    ? AutoPushWeaponThreatPolicy.Normal
                    : AutoPushWeaponThreatPolicy.Cautious;
            }

            return AutoPushWeaponThreatPolicy.Normal;
        }

        public bool ShouldBlockProactiveAutoPushForWeaponThreat(EnemyInfo goalEnemy)
        {
            return GetAutoPushWeaponThreatPolicy(goalEnemy) == AutoPushWeaponThreatPolicy.VeryCloseOrOrderedOnly &&
                   Enemy.Distance(goalEnemy) > Enemy.EnemyDistance.VeryClose;
        }

        public bool ShouldUseCautiousWeaponThreatStyle(EnemyInfo goalEnemy)
        {
            return GetAutoPushWeaponThreatPolicy(goalEnemy) == AutoPushWeaponThreatPolicy.Cautious;
        }

        private bool TryGetCurrentAmmoPenetration(out int penetrationPower)
        {
            Weapon? activeWeapon = botOwner?.WeaponManager?.ShootController?.Item;
            return TryGetCachedAmmoPenetration(activeWeapon, out penetrationPower);
        }

        private bool TryGetCurrentAutoPushAmmoProfile(out AutoPushAmmoProfile ammoProfile)
        {
            ammoProfile = default;
            Weapon? weapon = GetAutoPushWeaponThreatSource();
            if (weapon == null)
            {
                return false;
            }

            return TryBuildLoadedAmmoProfileCached(weapon, out ammoProfile);
        }

        private Weapon? GetAutoPushWeaponThreatSource()
        {
            Weapon? activeWeapon = botOwner?.WeaponManager?.ShootController?.Item ??
                                   botOwner?.WeaponManager?.CurrentWeapon;
            if (activeWeapon == null || IsAutomaticWeapon(activeWeapon))
            {
                return activeWeapon;
            }

            // Riflemen may switch from a non-auto first primary into a loaded automatic second
            // primary for push pressure. Threat scoring must evaluate that available push weapon;
            // otherwise a low-capacity sniper/DMR primary can incorrectly suppress aggression even
            // though the bot has a suitable automatic secondary ready.
            Weapon? secondaryWeapon = GetSecondPrimaryWeapon(botOwner);
            return IsAutomaticSecondaryUsableForPushCached(activeWeapon, secondaryWeapon)
                ? secondaryWeapon
                : activeWeapon;
        }

        private static bool CanHighCapacitySoftenLowPenetration(AutoPushAmmoProfile ammoProfile, bool bossOrRaider)
        {
            if (ammoProfile.PenetrationPower < HighCapacityLowPenetrationMinimum ||
                ammoProfile.ArmorDamage < HighCapacityLowPenArmorDamageMinimum)
            {
                return false;
            }

            if (IsSmallAutoPushCaliber(ammoProfile.Caliber))
            {
                return !bossOrRaider &&
                       ammoProfile.MagazineCapacity >= HighCapacitySmallCaliberDrumMinimum &&
                       ammoProfile.ArmorWearScore >= HighCapacitySmallCaliberNormalizeWearScore;
            }

            return IsArmorWearAutoPushCaliber(ammoProfile.Caliber) &&
                   ammoProfile.MagazineCapacity >= HighCapacityRifleDrumMinimum &&
                   ammoProfile.ArmorWearScore >= HighCapacityLowPenCautiousWearScore;
        }

        private static bool CanHighCapacityNormalizeCautiousPenetration(AutoPushAmmoProfile ammoProfile, bool bossOrRaider)
        {
            if (ammoProfile.ArmorDamage < HighCapacityArmorDamageMinimum)
            {
                return false;
            }

            if (IsSmallAutoPushCaliber(ammoProfile.Caliber))
            {
                return !bossOrRaider &&
                       ammoProfile.MagazineCapacity >= HighCapacitySmallCaliberMinimum &&
                       ammoProfile.ArmorWearScore >= HighCapacitySmallCaliberNormalizeWearScore;
            }

            if (!IsArmorWearAutoPushCaliber(ammoProfile.Caliber))
            {
                return false;
            }

            if (bossOrRaider)
            {
                return ammoProfile.MagazineCapacity >= HighCapacityRifleDrumMinimum &&
                       ammoProfile.ArmorWearScore >= HighCapacityBossNormalizeWearScore;
            }

            return ammoProfile.MagazineCapacity >= HighCapacityRifleMinimum &&
                   ammoProfile.ArmorWearScore >= HighCapacityCautiousNormalizeWearScore;
        }

        private static bool IsArmorWearAutoPushCaliber(string? caliber)
        {
            if (string.IsNullOrWhiteSpace(caliber))
            {
                return false;
            }

            return ContainsCaliber(caliber, "545x39") ||
                   ContainsCaliber(caliber, "556x45") ||
                   ContainsCaliber(caliber, "762x39") ||
                   ContainsCaliber(caliber, "762x51") ||
                   ContainsCaliber(caliber, "762x54") ||
                   ContainsCaliber(caliber, "68x51") ||
                   ContainsCaliber(caliber, "6.8x51") ||
                   ContainsCaliber(caliber, "300Blackout") ||
                   ContainsCaliber(caliber, "9x39") ||
                   ContainsCaliber(caliber, "366TKM");
        }

        private static bool IsSmallAutoPushCaliber(string? caliber)
        {
            if (string.IsNullOrWhiteSpace(caliber))
            {
                return false;
            }

            return ContainsCaliber(caliber, "9x18") ||
                   ContainsCaliber(caliber, "9x19") ||
                   ContainsCaliber(caliber, "9x21") ||
                   ContainsCaliber(caliber, "46x30") ||
                   ContainsCaliber(caliber, "57x28") ||
                   ContainsCaliber(caliber, "1143x23") ||
                   ContainsCaliber(caliber, "762x25");
        }

        private static bool ContainsCaliber(string caliber, string value)
        {
            return caliber.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private int GetBossGameplayLevel()
        {
            try
            {
                int? bossLevel = botOwner?.BotFollower?.BossToFollow?.Player()?.Profile?.Info?.Level;
                if (bossLevel.HasValue && bossLevel.Value > 0)
                {
                    return bossLevel.Value;
                }

                int? mainPlayerLevel = Singleton<GameWorld>.Instance?.MainPlayer?.Profile?.Info?.Level;
                if (mainPlayerLevel.HasValue && mainPlayerLevel.Value > 0)
                {
                    return mainPlayerLevel.Value;
                }
            }
            catch
            {
            }

            return 1;
        }

        private static bool IsArmorRelevantAutoPushTarget(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            WildSpawnType role = goalEnemy.Person?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;
            if (role == WildSpawnType.pmcBEAR ||
                role == WildSpawnType.pmcUSEC ||
                role == WildSpawnType.infectedPmc ||
                IsBossOrRaiderRole(role))
            {
                return true;
            }

            string roleName = role.ToString();
            return roleName.StartsWith("pmc", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBossOrRaiderAutoPushTarget(EnemyInfo goalEnemy)
        {
            WildSpawnType role = goalEnemy?.Person?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;
            return IsBossOrRaiderRole(role);
        }

        private static bool IsBossOrRaiderRole(WildSpawnType role)
        {
            if (role == WildSpawnType.pmcBot ||
                role == WildSpawnType.exUsec)
            {
                return true;
            }

            string roleName = role.ToString();
            return roleName.StartsWith("boss", StringComparison.OrdinalIgnoreCase) ||
                   roleName.StartsWith("follower", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Decides whether a visible enemy should force the bot into cover before trading shots.
        /// </summary>
        public bool ShouldTakeVisibleCover(EnemyInfo goalEnemy, float? aggressionOverride01 = null)
        {
            if (botOwner.Memory.IsInCover)
            {
                return false;
            }

            if (IsFollowerCriticallyWounded() || botOwner.Memory.IsUnderFire || WasHitRecently(botOwner, 0.75f))
            {
                return true;
            }

            float aggression = aggressionOverride01 ?? GetAggression01();
            float standAndTradeDistance = botOwner.LookSensor.MaxShootDist * 0.5f;
            return aggression < 0.45f && goalEnemy.Distance > standAndTradeDistance && PointToShoot != null;
        }

        /// <summary>
        /// Shared aggression gate for pushes so tactic variants can reuse the same advance logic
        /// while overriding aggression or distance policy where needed.
        /// </summary>
        public bool ShouldAdvance(
            EnemyInfo goalEnemy,
            float? aggressionOverride01 = null,
            FollowerCombatTactic? tacticOverride = null,
            Enemy.EnemyDistance? maxPushDistanceOverride = null)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            if (IsFollowerCriticallyWounded() ||
                botOwner.Memory.IsUnderFire ||
                WasHitRecently(botOwner, 1f))
            {
                return false;
            }

            float aggression = aggressionOverride01 ?? GetAggression01();
            FollowerCombatTactic tactic = tacticOverride ?? GetFollowerTactic();
            float pushThreshold = goalEnemy.IsVisible ? 0.35f : 0.45f;

            if (ShouldBlockProactiveAutoPushForWeaponThreat(goalEnemy))
            {
                return false;
            }

            if (tactic == FollowerCombatTactic.Protector)
            {
                pushThreshold += 0.15f;
            }
            else if (tactic == FollowerCombatTactic.Marksman)
            {
                pushThreshold += 0.3f;
            }

            Enemy.EnemyDistance maxPushDistance = maxPushDistanceOverride ?? GetMaxPushDistance(aggression, tactic);

            if (!IsEnemyLowThreat(goalEnemy, aggression))
            {
                return aggression >= 0.7f && Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.Close;
            }

            if (!goalEnemy.IsVisible && !HasReliablePersonalEnemyLocation(goalEnemy))
            {
                return false;
            }

            Enemy.EnemyDistance distance = Enemy.Distance(goalEnemy);
            if (distance > maxPushDistance)
            {
                return false;
            }

            if (aggression >= 0.5f &&
                !goalEnemy.IsVisible &&
                distance <= Enemy.EnemyDistance.Distant)
            {
                return true;
            }

            return aggression >= pushThreshold && ProtectWantKill(goalEnemy.Distance * 1.2f);
        }

        /// <summary>
        /// Chooses the movement mode used to reach a committed combat cover point.
        /// </summary>
        public BotLogicDecision SelectCommittedCoverMoveAction(EnemyInfo goalEnemy)
        {
            return SelectCommittedCoverMoveAction(goalEnemy, botOwner.Memory.CurCustomCoverPoint);
        }

        public BotLogicDecision SelectCommittedCoverMoveAction(EnemyInfo goalEnemy, CustomNavigationPoint? targetCover)
        {
            // If this is heal cover and bot has healed enough/threat changed, clear it and return to combat
            if (targetCover == committedHealCover && ShouldClearHealCover(goalEnemy, out string? clearReason))
            {
                if (pitFireTeam.IsDebugBuild)
                {
                    Modules.Logger.LogInfo($"[HealCover] follower={botOwner.name ?? botOwner.Profile?.Nickname ?? "unknown"} reason={clearReason ?? "unknown"}");
                }

                ClearCommittedHealCover();
                // Fall through to threat-based move decision; likely need to rejoin combat or regroup
                bool canSprintPlayer = CanSprintForCombatMovement();
                return canSprintPlayer
                    ? BotLogicDecision.runToCover
                    : BotLogicDecision.attackMoving;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                if (IsRetreatCover(goalEnemy, targetCover))
                {
                    return (BotLogicDecision)CustomBotDecisions.attackRetreat;
                }

                return BotLogicDecision.attackMoving;
            }

            if (!goalEnemy.IsVisible && botOwner.Memory.IsUnderFire)
            {
                if (IsRetreatCover(goalEnemy, targetCover))
                {
                    return (BotLogicDecision)CustomBotDecisions.attackRetreat;
                }

                return BotLogicDecision.attackMovingWithSuppress;
            }

            return CanSprintForCombatMovement()
                ? BotLogicDecision.runToCover
                : BotLogicDecision.attackMoving;
        }

        private bool IsRetreatCover(EnemyInfo goalEnemy, CustomNavigationPoint? targetCover)
        {
            if (targetCover == null)
            {
                return false;
            }

            Vector3 enemyPosition = goalEnemy.CurrPosition;
            float currentEnemyDistanceSqr = (botOwner.Position - enemyPosition).sqrMagnitude;
            float coverEnemyDistanceSqr = (targetCover.Position - enemyPosition).sqrMagnitude;
            return coverEnemyDistanceSqr > currentEnemyDistanceSqr + 2f * 2f;
        }

        /// <summary>
        /// Pushes the selected cover point into EFT cover memory so movement actions use it.
        /// </summary>
        public void AssignCover(CustomNavigationPoint? cover)
        {
            SetCover(cover);
            if (cover != null && cover.IsFreeById(botOwner.Id))
            {
                cover.SetOwner(botOwner);
            }
        }

        /// <summary>
        /// Assigns the already-committed cover point back into EFT memory before reissuing movement.
        /// </summary>
        public void AssignCommittedCover()
        {
            AssignCover(committedCoverPoint);
        }

        /// <summary>
        /// Finds and commits a single combat cover using the default follower cover preference order.
        /// </summary>
        public bool TryCommitCombatCover(
            EnemyInfo goalEnemy,
            bool requireShootLane,
            float bossCoverSearchRadius,
            out string reason,
            bool avoidBossFireLane = false,
            bool recoveryManeuver = false)
        {
            reason = requireShootLane ? "shootCover" : "safeCover";

            if (HasCommittedCover())
            {
                reason = GetCommittedCoverReason();
                if (!recoveryManeuver)
                {
                    return true;
                }

                CustomNavigationPoint existingCover = committedCoverPoint!;
                string existingBaseReason = GetCoverCommitBaseReason(reason);
                if (TryValidateSelectedCombatCover(
                        goalEnemy,
                        existingCover,
                        GetRecoveryValidationReason(existingBaseReason),
                        recoveryManeuver: true,
                        out BotLogicDecision recoveryMoveAction))
                {
                    if (IsRecoveryManeuverReason(reason))
                    {
                        return true;
                    }

                    // A firing-position commitment is not automatically a survival cover. Reuse it
                    // only after it passes the same route, claim, and point-blank checks as a fresh
                    // recovery candidate, then re-commit it with the recovery movement contract.
                    ClearCommittedCover("requalifyForRecovery");
                    CommitValidatedCombatCover(
                        goalEnemy,
                        existingCover,
                        existingBaseReason,
                        recoveryManeuver: true,
                        recoveryMoveAction);
                    reason = GetCommittedCoverReason();
                    return true;
                }

                ClearCommittedCover("rejectExistingRecoveryCover");
                nextCoverAcquireTime = 0f;
            }

            if (!CanAcquireCommittedCover())
            {
                return false;
            }

            CustomNavigationPoint? cover = null;
            if (requireShootLane &&
                IsCoverUsable(PointToShoot) &&
                (!recoveryManeuver || !IsBlockedRecoveryCover(PointToShoot)) &&
                (!avoidBossFireLane || !IsBossFireLaneMovementRisk(PointToShoot.Position, goalEnemy, includePath: true)))
            {
                cover = PointToShoot;
                reason = "shootCover";
            }

            if (cover == null &&
                TryAssignRetreatAttackCover(goalEnemy, requireShootLane, GetCombatCoverMaxDistanceSqr(), false))
            {
                CustomNavigationPoint? retreatCover = botOwner.Memory.CurCustomCoverPoint;
                if ((!recoveryManeuver || !IsBlockedRecoveryCover(retreatCover)) &&
                    (!avoidBossFireLane ||
                     retreatCover == null ||
                     !IsBossFireLaneMovementRisk(retreatCover.Position, goalEnemy, includePath: true)))
                {
                    cover = retreatCover;
                    reason = requireShootLane
                        ? "retreatShootCover"
                        : lastAssignedRetreatCoverWasWeak
                            ? "retreatWeakCover"
                            : "retreatSafeCover";
                }
            }

            if (cover == null &&
                !requireShootLane &&
                IsCoverUsable(PointToShoot) &&
                (!recoveryManeuver || !IsBlockedRecoveryCover(PointToShoot)) &&
                (!avoidBossFireLane || !IsBossFireLaneMovementRisk(PointToShoot.Position, goalEnemy, includePath: true)))
            {
                cover = PointToShoot;
                Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
                reason = IsFinite(enemyAnchor) &&
                         IsHardThreatCover(PointToShoot!, enemyAnchor, goalEnemy.ProfileId)
                    ? "safeCover"
                    : "retreatWeakCover";
            }

            if (cover == null && TryFindBossCover(goalEnemy, bossCoverSearchRadius, out CustomNavigationPoint? bossCover))
            {
                if ((!recoveryManeuver || !IsBlockedRecoveryCover(bossCover)) &&
                    (!avoidBossFireLane ||
                     bossCover == null ||
                     !IsBossFireLaneMovementRisk(bossCover.Position, goalEnemy, includePath: true)))
                {
                    cover = bossCover;
                    reason = "bossCover";
                }
            }

            return TryCommitSelectedCombatCover(goalEnemy, cover, reason, recoveryManeuver);
        }

        /// <summary>
        /// Finds and commits a firing-position cover. Tactics can use this when they want their
        /// own selection policy but still need the same sticky-cover movement behavior.
        /// </summary>
        public bool TryCommitFiringPositionCover(
            EnemyInfo goalEnemy,
            string reason,
            out string committedReason,
            bool preferPointToShoot = true,
            bool preferInbetween = false,
            bool enforceMarksmanPositionPolicy = false,
            bool avoidBossFireLane = false)
        {
            committedReason = reason;
            if (HasCommittedCover())
            {
                committedReason = GetCommittedCoverReason();
                return true;
            }

            if (!CanAcquireCommittedCover())
            {
                return false;
            }

            CustomNavigationPoint? cover = preferPointToShoot &&
                                           IsCoverUsable(PointToShoot) &&
                                           (!avoidBossFireLane || !IsBossFireLaneMovementRisk(PointToShoot.Position, goalEnemy, includePath: true))
                ? PointToShoot
                : null;

            cover ??= preferInbetween
                ? GetApproachableCover(inbetween: true, avoidBossFireLane: avoidBossFireLane) ??
                  GetApproachableCover(avoidBossFireLane: avoidBossFireLane)
                : GetApproachableCover(avoidBossFireLane: avoidBossFireLane);

            if (enforceMarksmanPositionPolicy &&
                cover != null &&
                !IsMarksmanFiringPositionAllowed(goalEnemy, cover.Position))
            {
                return false;
            }

            return TryCommitSelectedCombatCover(goalEnemy, cover, committedReason);
        }

        /// <summary>
        /// Atomically replaces a marksman firing-cover commitment only when the already-scanned
        /// candidate is a meaningful, shoot-capable upgrade. The current cover remains committed
        /// when validation fails, avoiding an end-and-reselect gap.
        /// </summary>
        public bool TryReplaceCommittedFiringCover(
            EnemyInfo goalEnemy,
            CustomNavigationPoint? candidate,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool enforceMarksmanPositionPolicy = false)
        {
            decision = default;
            CustomNavigationPoint? current = committedCoverPoint;
            if (current == null ||
                candidate == null ||
                current.Id == candidate.Id ||
                (enforceMarksmanPositionPolicy &&
                 !IsMarksmanFiringPositionAllowed(goalEnemy, candidate.Position)))
            {
                return false;
            }

            bool candidateShootLaneStable = candidate.CanIShootToEnemy && HaveCoverToShoot;
            if (!ShouldCommitRefreshedShootCover(
                    current,
                    candidate,
                    GetBossPosition(),
                    requireShootLane: true,
                    candidateShootLaneStable) ||
                !TryValidateSelectedCombatCover(
                    goalEnemy,
                    candidate,
                    reason,
                    recoveryManeuver: false,
                    out BotLogicDecision moveAction))
            {
                return false;
            }

            ClearCommittedCover("replaceFiringCover");
            CommitValidatedCombatCover(
                goalEnemy,
                candidate,
                reason,
                recoveryManeuver: false,
                moveAction);
            decision = CreateCommittedCoverMoveDecision();
            return true;
        }

        public bool TryCommitMarksmanSupportCover(
            EnemyInfo goalEnemy,
            Vector3 pushOwnerPosition,
            Vector3 enemyPosition,
            Vector3 watchedDestination,
            string reason,
            out string committedReason)
        {
            committedReason = reason;
            if (!CanAcquireCommittedCover())
            {
                return false;
            }

            CustomNavigationPoint? cover = FindPushSupportCover(goalEnemy, pushOwnerPosition, enemyPosition, requireEnemyShootLane: true, keepBehindBoss: true);
            if (cover == null)
            {
                cover = FindPushSupportCover(goalEnemy, pushOwnerPosition, watchedDestination, requireEnemyShootLane: false, keepBehindBoss: true);
                committedReason += ".watchDestination";
            }
            else
            {
                committedReason += ".shootEnemy";
            }

            if (cover != null)
            {
                return TryCommitSelectedCombatCover(goalEnemy, cover, committedReason);
            }

            return false;
        }

        public bool TryGetActivePushEvent(out CombatEvents.PushEvent pushEvent)
        {
            pushEvent = default;
            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            return boss.CombatEvents.TryGetActivePushFor(botOwner, out pushEvent);
        }

        public bool TryGetActivePushEventForCurrentEnemy(out CombatEvents.PushEvent pushEvent)
        {
            if (!TryGetActivePushEvent(out pushEvent))
            {
                return false;
            }

            return IsCurrentGoalEnemy(pushEvent.EnemyProfileId);
        }

        // Helper eligibility for Rifleman-style push support. We require both straight-line and
        // nav-distance proximity so a follower across a wall/building does not "join" a push that
        // is tactically nearby but unreachable without a long detour.
        public bool TryGetNearbyActivePushEvent(
            float maxStraightDistance,
            float maxNavDistance,
            out CombatEvents.PushEvent pushEvent)
        {
            if (!TryGetActivePushEvent(out pushEvent))
            {
                return false;
            }

            if (!IsFinite(pushEvent.Owner.Position))
            {
                return false;
            }

            float straightDistance = Vector3.Distance(botOwner.Position, pushEvent.Owner.Position);
            if (straightDistance > maxStraightDistance)
            {
                return false;
            }

            float navDistance = Utils.Utils.GetNavDistance(botOwner.Position, pushEvent.Owner.Position);
            return !IsFinite(navDistance) || navDistance <= maxNavDistance;
        }

        public bool TryGetNearbyActivePushEventForCurrentEnemy(
            float maxStraightDistance,
            float maxNavDistance,
            out CombatEvents.PushEvent pushEvent)
        {
            if (!TryGetNearbyActivePushEvent(maxStraightDistance, maxNavDistance, out pushEvent))
            {
                return false;
            }

            return IsCurrentGoalEnemy(pushEvent.EnemyProfileId);
        }

        private bool IsCurrentGoalEnemy(string enemyProfileId)
        {
            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            return !string.IsNullOrEmpty(enemyProfileId) &&
                   HasActiveCombatEnemy(goalEnemy) &&
                   string.Equals(goalEnemy.ProfileId, enemyProfileId, StringComparison.Ordinal);
        }

        public bool HasActivePushFromOther()
        {
            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            return boss.CombatEvents.HasActivePushFromOther(botOwner);
        }

        public bool HasActiveGrenadeLauncherSuppressNearCurrentEnemy()
        {
            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss ||
                !boss.CombatEvents.TryGetActiveLauncherSuppressFor(botOwner, out CombatEvents.LauncherSuppressEvent suppressEvent))
            {
                return false;
            }

            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            return HasActiveCombatEnemy(goalEnemy) &&
                   boss.CombatEvents.IsNearLauncherSuppressArea(GetEnemyAnchor(goalEnemy), suppressEvent);
        }

        public bool TryCommitSupportFiringCover(
            EnemyInfo supportEnemy,
            string reason,
            out string committedReason,
            bool preferBackline,
            bool enforceMarksmanPositionPolicy = false,
            float maxSearchRadius = 35f)
        {
            committedReason = reason;
            lastSupportFiringCoverRejectReason = null;
            if (!CanAcquireCommittedCover())
            {
                lastSupportFiringCoverRejectReason = "cannotAcquireCommittedCover";
                return false;
            }

            if (!TryGetSupportCoverForEnemy(supportEnemy, out CustomNavigationPoint? supportCover, out _, maxSearchRadius))
            {
                lastSupportFiringCoverRejectReason = "noSupportCover";
                return false;
            }

            if (preferBackline)
            {
                Vector3 bossPosition = GetBossPosition();
                Vector3 enemyAnchor = GetEnemyAnchorOrFallback(supportEnemy, Vector3.zero);
                if (IsFinite(bossPosition) &&
                    IsFinite(enemyAnchor) &&
                    !IsSupportPositionBehindBossLine(supportCover!.Position, bossPosition, enemyAnchor))
                {
                    lastSupportFiringCoverRejectReason = "notBehindBossLine";
                    return false;
                }
            }

            if (enforceMarksmanPositionPolicy &&
                supportCover != null &&
                !IsMarksmanFiringPositionAllowed(supportEnemy, supportCover.Position))
            {
                lastSupportFiringCoverRejectReason = "marksmanPolicyRejected";
                return false;
            }

            if (enforceMarksmanPositionPolicy &&
                supportCover != null &&
                IsMarksmanSupportSeparatedFromBoss(supportCover.Position))
            {
                lastSupportFiringCoverRejectReason = "bossSeparated";
                return false;
            }

            if (!TryCommitSelectedCombatCover(supportEnemy, supportCover, committedReason))
            {
                lastSupportFiringCoverRejectReason = "commitSelectedCoverFailed";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true when the current cover commitment still exists and should be managed.
        /// </summary>
        public bool HasCommittedCover()
        {
            if (committedCoverPoint == null)
            {
                return false;
            }

            if (committedCoverUntil < Time.time && !IsBotInCommittedCover())
            {
                ClearCommittedCover("expired");
                return false;
            }

            return IsCommittedCoverStillUsable(committedCoverPoint);
        }

        /// <summary>
        /// Drops invalid committed cover before it can keep feeding stale movement.
        /// </summary>
        public void ValidateCommittedCover()
        {
            if (!HasCommittedCover())
            {
                ClearCommittedCover("invalid");
            }
        }

        /// <summary>
        /// Treats the bot as arrived when EFT marks the cover active or the bot is physically close.
        /// </summary>
        public bool IsBotInCommittedCover()
        {
            if (committedCoverPoint == null)
            {
                return false;
            }

            if (botOwner.Memory.IsInCover &&
                botOwner.Memory.CurCustomCoverPoint != null &&
                botOwner.Memory.CurCustomCoverPoint.Id == committedCoverPoint.Id)
            {
                return true;
            }

            return (botOwner.Position - committedCoverPoint.Position).sqrMagnitude <= 2f * 2f;
        }

        /// <summary>
        /// Keeps a working cover commitment alive while the bot is actively using it.
        /// </summary>
        public void ExtendCommittedCover()
        {
            if (committedCoverPoint == null)
            {
                return;
            }

            committedCoverUntil = Mathf.Max(committedCoverUntil, Time.time + 0.75f);
        }

        /// <summary>
        /// Drops the current combat-cover commitment so the next decision can select fresh cover.
        /// </summary>
        public void ClearCommittedCover(string? reason = null)
        {
            if (committedCoverPoint != null)
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "cover",
                    "clear",
                    reason ?? "clear",
                    committedCoverMoveAction != default
                        ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(committedCoverMoveAction, committedCoverMoveReason ?? "cover")
                        : null,
                    committedCoverPoint.Position,
                    committedCoverPoint.Id);
                coverCommitIntents.Remove(botOwner.Id);
            }

            committedCoverPoint = null;
            committedCoverMoveAction = default;
            committedCoverMoveReason = null;
            committedCoverSetAt = 0f;
            committedCoverUntil = 0f;
            ReleaseCombatCoverDestinationClaim();
            ResetRunToCoverProgress();
            ResetTacticalPointProgress();
        }

        public void BlockCommittedRecoveryCover(string reason)
        {
            if (committedCoverPoint == null)
            {
                return;
            }

            blockedRecoveryCoverId = committedCoverPoint.Id;
            blockedRecoveryCoverUntil = Time.time + FailedRecoveryCoverBlacklistSeconds;
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "recovery",
                "blockCover",
                reason,
                target: committedCoverPoint.Position,
                coverId: committedCoverPoint.Id,
                untilTime: blockedRecoveryCoverUntil);
        }

        private bool IsBlockedRecoveryCover(CustomNavigationPoint? cover)
        {
            return cover != null &&
                   cover.Id == blockedRecoveryCoverId &&
                   Time.time < blockedRecoveryCoverUntil;
        }

        /// <summary>
        /// Keeps a failed or already-completed push cover out of the next pressure scan. The block
        /// is scoped to the same enemy geometry, so a changed target or materially moved enemy can
        /// make the cover useful again without waiting for the timeout.
        /// </summary>
        private void BlockPushCover(
            CustomNavigationPoint? cover,
            EnemyInfo? goalEnemy,
            string reason)
        {
            if (cover == null || !IsFinite(cover.Position))
            {
                return;
            }

            if (!blockedPushCovers.ContainsKey(cover.Id) &&
                blockedPushCovers.Count >= PushCoverBlacklistMaxEntries)
            {
                int oldestCoverId = -1;
                float oldestUntil = float.MaxValue;
                foreach (KeyValuePair<int, PushCoverBlockState> pair in blockedPushCovers)
                {
                    if (pair.Value.Until < oldestUntil)
                    {
                        oldestUntil = pair.Value.Until;
                        oldestCoverId = pair.Key;
                    }
                }

                if (oldestCoverId >= 0)
                {
                    blockedPushCovers.Remove(oldestCoverId);
                }
            }

            Vector3 enemyAnchor = goalEnemy != null
                ? GetEnemyAnchor(goalEnemy)
                : Vector3.zero;
            float blockedUntil = Time.time + PushCoverBlacklistSeconds;
            blockedPushCovers[cover.Id] = new PushCoverBlockState(
                goalEnemy?.ProfileId ?? string.Empty,
                enemyAnchor,
                blockedUntil);
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "pushCover",
                "block",
                reason,
                target: cover.Position,
                coverId: cover.Id,
                untilTime: blockedUntil);
        }

        public bool IsBlockedPushCover(
            CustomNavigationPoint? cover,
            EnemyInfo? goalEnemy,
            string? reason)
        {
            if (cover == null ||
                !IsPushCoverMovementReason(reason) ||
                !blockedPushCovers.TryGetValue(cover.Id, out PushCoverBlockState blocked))
            {
                return false;
            }

            if (Time.time >= blocked.Until ||
                goalEnemy == null ||
                !string.Equals(blocked.EnemyProfileId, goalEnemy.ProfileId, StringComparison.Ordinal))
            {
                blockedPushCovers.Remove(cover.Id);
                return false;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (IsFinite(blocked.EnemyAnchor) &&
                IsFinite(enemyAnchor) &&
                (enemyAnchor - blocked.EnemyAnchor).sqrMagnitude >= PushCoverBlacklistEnemyMoveToleranceSqr)
            {
                blockedPushCovers.Remove(cover.Id);
                return false;
            }

            return true;
        }

        public void BlockCommittedPushCoverForReplan(string? reason)
        {
            if (committedCoverPoint == null || !IsPushCoverMovementReason(reason))
            {
                return;
            }

            BlockPushCover(
                committedCoverPoint,
                botOwner.Memory?.GoalEnemy,
                $"completed:{reason}");
            ClearCommittedCover("completedPushCover");
            nextCoverAcquireTime = 0f;
        }

        public static bool IsPushCoverMovementReason(string? reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return false;
            }

            const string committedCoverHoldPrefix = "committedCoverHold.";
            if (reason.StartsWith(committedCoverHoldPrefix, StringComparison.Ordinal))
            {
                reason = reason.Substring(committedCoverHoldPrefix.Length);
            }

            return FollowerCombatPush.IsPushReason(reason) ||
                   FollowerCombatPush.IsStartWeakEnemyPushReason(reason);
        }

        /// <summary>
        /// Clears committed cover and the search cooldown used when selecting a fresh cover point.
        /// </summary>
        public void ResetCommittedCover()
        {
            ClearCommittedCover();
            nextCoverAcquireTime = 0f;
        }

        /// <summary>
        /// Returns how long the current cover point has been committed.
        /// </summary>
        public float CommittedCoverAge => committedCoverSetAt <= 0f ? 0f : Time.time - committedCoverSetAt;

        public int? CommittedCoverId => committedCoverPoint?.Id;

        public string? CommittedCoverReason => committedCoverMoveReason;

        public string? CommittedPositionReason => committedPointReason;

        public bool IsCommittedCoverLockExpired => CommittedCoverAge >= CoverCommitLockSeconds;

        public bool ShouldBreakCommittedPushForVisibility(
            EnemyInfo goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentPush,
            ref float actionableVisibleSince,
            float fireWhileMovingVisibleBreakSeconds = DefaultFireWhileMovingPushVisibleBreakSeconds)
        {
            bool actionableVisible = HasFreshVisibleShootableContact(goalEnemy, CloseThreatRecentSeenSeconds);
            bool closeVisible = HasFreshVisibleContact(goalEnemy, CloseThreatRecentSeenSeconds) &&
                                goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetClosePushDistance();
            if (!actionableVisible && !closeVisible)
            {
                actionableVisibleSince = 0f;
                return false;
            }

            if (IsDirectEnemyPush(currentPush.Action))
            {
                // Direct ordered/automatic advances used to stop on the first visible sensor
                // sample, then restart as soon as that sample flickered. Require the same short,
                // push-local visibility lease used by fire-while-moving. Point-blank dogfight is
                // checked separately before this gate and remains immediate.
                if (actionableVisibleSince <= 0f)
                {
                    actionableVisibleSince = Time.time;
                    return false;
                }

                return Time.time - actionableVisibleSince >= fireWhileMovingVisibleBreakSeconds;
            }

            if (!IsFireWhileMovingPush(currentPush.Action) && !IsDirectEnemyPush(currentPush.Action))
            {
                actionableVisibleSince = 0f;
                return true;
            }

            if (actionableVisibleSince <= 0f)
            {
                actionableVisibleSince = Time.time;
                return false;
            }

            return Time.time - actionableVisibleSince >= fireWhileMovingVisibleBreakSeconds;
        }

        public static bool IsFireWhileMovingPush(BotLogicDecision action)
        {
            return action == BotLogicDecision.attackMoving ||
                   action == BotLogicDecision.attackMovingWithSuppress;
        }

        public static bool IsDirectEnemyPush(BotLogicDecision action)
        {
            return action == BotLogicDecision.runToEnemy ||
                   action == BotLogicDecision.goToEnemy;
        }

        public static bool IsMovementDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            return decision.Action == BotLogicDecision.runToEnemy ||
                   decision.Action == BotLogicDecision.goToEnemy ||
                   decision.Action == BotLogicDecision.runToCover ||
                   decision.Action == BotLogicDecision.attackMoving ||
                   decision.Action == BotLogicDecision.attackMovingWithSuppress ||
                   decision.Action == (BotLogicDecision)CustomBotDecisions.attackRetreat ||
                   decision.Action == BotLogicDecision.goToPoint ||
                   decision.Action == BotLogicDecision.goToPointTactical;
        }

        public bool ShouldBreakForBossUnderAttack(
            EnemyInfo goalEnemy,
            bool hasActivePushOrder = false,
            float stalePersonalEnemySeconds = 2.5f)
        {
            if (FollowerCombatAnchor.IsCombatIndependent(botOwner))
            {
                return false;
            }

            if (GetBossProtectionWillingness01() < PickupFollowerPersonality.ProtectBossMinWillingness)
            {
                return false;
            }

            if (hasActivePushOrder)
            {
                return false;
            }

            // A live personal shot remains valid support; keep taking it.
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            float sinceLastSeen = Time.time - goalEnemy.PersonalLastSeenTime;
            if (botOwner.Memory.HaveEnemy && sinceLastSeen > stalePersonalEnemySeconds)
            {
                return false;
            }

            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            AIBossPlayerLogic? bossLogic = boss.GetBossLogic();
            if (bossLogic == null || !bossLogic.IsHitted)
            {
                return false;
            }

            BotOwner? bossEnemy = boss.ClosestEnemy();
            return bossEnemy != null && bossEnemy.GetPlayer?.HealthController?.IsAlive == true;
        }

        public bool ShouldBreakCommittedCoverForBossObjective(
            EnemyInfo goalEnemy,
            bool shouldRegroupForBossDistance,
            bool hasActivePushOrder = false,
            bool hasImmediateShot = false,
            bool allowMovingCommittedCoverBreak = false)
        {
            if (hasActivePushOrder || hasImmediateShot)
            {
                return false;
            }

            if (!shouldRegroupForBossDistance)
            {
                return false;
            }

            // Give committed cover time to be reached and used before escort pressure can pull it out.
            if (HasCommittedCover())
            {
                if (!IsBotInCommittedCover())
                {
                    return allowMovingCommittedCoverBreak && IsCommittedCoverLockExpired;
                }

                if (!IsCommittedCoverLockExpired)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsBossDistanceProtectedCommitmentReason(string? reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return false;
            }

            return reason.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   reason.IndexOf("recovery", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   reason.IndexOf("push", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   reason.IndexOf("support", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   reason.IndexOf("protect", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsRecoveryManeuverReason(string? reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                   (reason.StartsWith("recovery.", StringComparison.Ordinal) ||
                    reason.StartsWith("committedCoverHold.recovery.", StringComparison.Ordinal) ||
                    reason.StartsWith("committedPositionHold.recovery.", StringComparison.Ordinal));
        }

        public static bool IsCommittedCoverReason(string reason, IEnumerable<string>? committedCoverReasons = null)
        {
            IEnumerable<string> reasons = committedCoverReasons ?? DefaultBossObjectiveCoverBreakReasons;
            foreach (string coverReason in reasons)
            {
                if (IsReasonOrSubreason(reason, coverReason))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsBossHoldReason(string? reason)
        {
            return string.Equals(reason, "bossHold", StringComparison.Ordinal) ||
                   string.Equals(reason, "bossHoldOpen", StringComparison.Ordinal) ||
                   string.Equals(reason, "bossHold.open", StringComparison.Ordinal);
        }

        private string GetCommittedCoverReason()
        {
            return !string.IsNullOrEmpty(committedCoverMoveReason)
                ? committedCoverMoveReason!
                : "commitCover";
        }

        /// <summary>
        /// Converts the current committed cover into the stable movement action needed to reach it.
        /// </summary>
        public AICoreActionResultStruct<BotLogicDecision, GClass26> CreateMoveToCommittedCoverDecision(string reason)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                committedCoverMoveReason = reason;
            }

            return CreateCommittedCoverMoveDecision();
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> CreateCommittedCoverMoveDecision()
        {
            BotLogicDecision moveAction = committedCoverMoveAction != default
                ? committedCoverMoveAction
                : (CanSprintForCombatMovement() ? BotLogicDecision.runToCover : BotLogicDecision.attackMoving);
            string reason = GetCommittedCoverReason();
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(moveAction, reason);
        }

        public bool TryCreateSuppressDecision(
            EnemyInfo goalEnemy,
            string reasonPrefix,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool allowObstructedSuppression = false)
        {
            decision = default;
            if (string.IsNullOrEmpty(reasonPrefix) ||
                !HasActiveCombatEnemy(goalEnemy) ||
                botOwner.SuppressShoot == null)
            {
                return false;
            }

            if (!TryGetSuppressTarget(goalEnemy, out Vector3 suppressTarget))
            {
                return false;
            }

            return TryCreateSuppressDecisionAtTarget(suppressTarget, reasonPrefix, out decision, allowObstructedSuppression);
        }

        public bool TryCreateOrderedSuppressWeaponFallbackDecision(
            EnemyInfo goalEnemy,
            string reasonPrefix,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (string.IsNullOrEmpty(reasonPrefix) ||
                !HasActiveCombatEnemy(goalEnemy) ||
                botOwner.SuppressShoot == null)
            {
                return false;
            }

            RequestLauncherPrimaryFallback($"{reasonPrefix}:orderedWeaponSuppress");
            if (HasPendingLauncherPrimaryFallback())
            {
                BattleRecorder.RecordGrenadeEvent(
                    botOwner,
                    "weaponFallback",
                    $"{reasonPrefix}:waitForPrimarySwitch",
                    goalEnemy: goalEnemy);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    $"{reasonPrefix}.weaponSwitchToPrimary");
                return true;
            }

            Vector3 suppressTarget = IsFinite(orderedSuppressTarget) && orderedSuppressTarget.sqrMagnitude > 0.01f
                ? orderedSuppressTarget
                : Vector3.zero;
            if (!IsFinite(suppressTarget) || suppressTarget.sqrMagnitude <= 0.01f)
            {
                if (!TryGetSuppressTarget(goalEnemy, out suppressTarget))
                {
                    return false;
                }
            }

            string weaponReasonPrefix = $"{reasonPrefix}.weapon";
            return TryCreateSuppressDecisionAtTarget(suppressTarget, weaponReasonPrefix, out decision, allowObstructedSuppression: true) ||
                   TryCreateOrderedSuppressAreaDecision(suppressTarget, weaponReasonPrefix, out decision);
        }

        private bool TryCreateSuppressDecisionAtTarget(
            Vector3 suppressTarget,
            string reasonPrefix,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool allowObstructedSuppression)
        {
            decision = default;
            if (!IsFinite(suppressTarget) ||
                string.IsNullOrEmpty(reasonPrefix) ||
                !CanCurrentWeaponSuppress())
            {
                return false;
            }

            ShootPointClass shootPoint = new ShootPointClass(suppressTarget, 1f);
            Vector3 fireOrigin = GetCurrentSuppressionFireOrigin(botOwner);

            if (Utils.Utils.CanShootToTarget(shootPoint, fireOrigin, botOwner.LookSensor.Mask, false) &&
                !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget) &&
                botOwner.SuppressShoot.InitToPoint(suppressTarget, null))
            {
                botOwner.Steering.LookToPoint(suppressTarget);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    $"{reasonPrefix}.place");
                return true;
            }

            Vector3 standingOrigin = GetStandingSuppressionFireOrigin(botOwner);
            if ((standingOrigin - fireOrigin).sqrMagnitude > 0.04f &&
                Utils.Utils.CanShootToTarget(shootPoint, standingOrigin, botOwner.LookSensor.Mask, false) &&
                !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, standingOrigin, suppressTarget) &&
                botOwner.SuppressShoot.InitToPoint(suppressTarget, null))
            {
                botOwner.SetPose(1f);
                botOwner.Steering.LookToPoint(suppressTarget);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    $"{reasonPrefix}.standPlace");
                return true;
            }

            if (TryFindSuppressFromPoint(suppressTarget, out CustomNavigationPoint? suppressFrom))
            {
                if (botOwner.SuppressShoot.InitToPoint(suppressTarget, suppressFrom))
                {
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.suppressFire,
                        $"{reasonPrefix}.move");
                    return true;
                }
            }

            if (allowObstructedSuppression &&
                IsSoftObstructedSuppressionLane(fireOrigin, suppressTarget, botOwner.LookSensor.Mask) &&
                !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget) &&
                botOwner.SuppressShoot.InitToPoint(suppressTarget, null))
            {
                botOwner.Steering.LookToPoint(suppressTarget);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    $"{reasonPrefix}.softObstructedPlace");
                return true;
            }

            if (allowObstructedSuppression &&
                (standingOrigin - fireOrigin).sqrMagnitude > 0.04f &&
                IsSoftObstructedSuppressionLane(standingOrigin, suppressTarget, botOwner.LookSensor.Mask) &&
                !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, standingOrigin, suppressTarget) &&
                botOwner.SuppressShoot.InitToPoint(suppressTarget, null))
            {
                botOwner.SetPose(1f);
                botOwner.Steering.LookToPoint(suppressTarget);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    $"{reasonPrefix}.standSoftObstructedPlace");
                return true;
            }

            return false;
        }

        private bool TryCreateOrderedSuppressAreaDecision(
            Vector3 suppressTarget,
            string reasonPrefix,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!IsFinite(suppressTarget) ||
                string.IsNullOrEmpty(reasonPrefix) ||
                botOwner.SuppressShoot == null ||
                !CanCurrentWeaponSuppress())
            {
                return false;
            }

            Vector3 fireOrigin = GetCurrentSuppressionFireOrigin(botOwner);
            Vector3 standingOrigin = GetStandingSuppressionFireOrigin(botOwner);
            bool currentLaneFriendlyClear = !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget);
            bool standingLaneFriendlyClear =
                (standingOrigin - fireOrigin).sqrMagnitude > 0.04f &&
                !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, standingOrigin, suppressTarget);

            if (!currentLaneFriendlyClear && !standingLaneFriendlyClear)
            {
                return false;
            }

            string suffix = "area";
            if (!currentLaneFriendlyClear && standingLaneFriendlyClear)
            {
                botOwner.SetPose(1f);
                suffix = "standArea";
            }

            if (!botOwner.SuppressShoot.InitToPoint(suppressTarget, null))
            {
                return false;
            }

            botOwner.Steering.LookToPoint(suppressTarget);
            BattleRecorder.RecordGrenadeEvent(
                botOwner,
                "weaponSuppressArea",
                $"{reasonPrefix}.{suffix}",
                goalEnemy: botOwner.Memory?.GoalEnemy,
                target: suppressTarget);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.suppressFire,
                $"{reasonPrefix}.{suffix}");
            return true;
        }

        public bool TryCreateGrenadeLauncherFireDecision(
            EnemyInfo goalEnemy,
            string reasonPrefix,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool ordered)
        {
            decision = default;
            if (!TryPrepareGrenadeLauncherFirePlan(goalEnemy, reasonPrefix, ordered, out GrenadeLauncherFirePlan? plan))
            {
                return false;
            }

            return TryStartGrenadeLauncherFireDecision(goalEnemy, plan, out decision);
        }

        public bool TryPrepareGrenadeLauncherFirePlan(
            EnemyInfo goalEnemy,
            string reasonPrefix,
            bool ordered,
            out GrenadeLauncherFirePlan? plan)
        {
            plan = null;
            if (string.IsNullOrEmpty(reasonPrefix))
            {
                return RejectGrenadeLauncherSuppress("missingReasonPrefix", goalEnemy);
            }

            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return RejectGrenadeLauncherSuppress($"{reasonPrefix}:noActiveEnemy", goalEnemy);
            }

            if (!HasUsableEquippedGrenadeLauncher(botOwner))
            {
                return RejectGrenadeLauncherSuppress($"{reasonPrefix}:noUsableEquippedLauncher", goalEnemy);
            }

            float unsafeRadius = ordered ? GrenadeLauncherOrderedUnsafeRadius : GrenadeLauncherAutoUnsafeRadius;
            string? targetRejectReason;
            List<Vector3> targets = ordered
                ? CollectOrderedGrenadeLauncherTargets(goalEnemy, unsafeRadius, out targetRejectReason)
                : CollectAutonomousGrenadeLauncherTargets(goalEnemy, unsafeRadius, out targetRejectReason);
            if (targets.Count == 0)
            {
                return RejectGrenadeLauncherSuppress(
                    string.IsNullOrEmpty(targetRejectReason) ? $"{reasonPrefix}:noValidTargets" : $"{reasonPrefix}:{targetRejectReason}",
                    goalEnemy);
            }

            Vector3 firstTarget = targets[0];
            Vector3 fireOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.2f;
            CustomNavigationPoint? suppressFrom = null;
            if (!TryCanFireGrenadeLauncherAtTarget(fireOrigin, firstTarget, unsafeRadius, out string directLaneRejectReason) &&
                !TryFindSuppressFromPoint(
                    firstTarget,
                    out suppressFrom,
                    allowSoftFoliageLane: true,
                    allowLauncherArcLane: true,
                    launcherUnsafeRadius: unsafeRadius))
            {
                return RejectGrenadeLauncherSuppress($"{reasonPrefix}:{directLaneRejectReason}:noSuppressFromPoint", goalEnemy);
            }

            if (suppressFrom != null)
            {
                Vector3 suppressFireOrigin = suppressFrom.Position + Vector3.up * 1.2f;
                if (!TryCanFireGrenadeLauncherAtTarget(suppressFireOrigin, firstTarget, unsafeRadius, out string suppressFromRejectReason))
                {
                    return RejectGrenadeLauncherSuppress($"{reasonPrefix}:suppressFrom:{suppressFromRejectReason}", goalEnemy);
                }
            }

            plan = new GrenadeLauncherFirePlan(reasonPrefix, ordered, unsafeRadius, targets, suppressFrom);
            BattleRecorder.RecordGrenadeEvent(
                botOwner,
                "launcherPlan",
                plan.DecisionReason,
                goalEnemy: goalEnemy,
                target: firstTarget,
                suppressFrom: suppressFrom?.Position);
            return true;
        }

        public bool HasAutonomousGrenadeLauncherTarget(EnemyInfo goalEnemy, out string? rejectReason)
        {
            rejectReason = null;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                rejectReason = "noActiveEnemy";
                return false;
            }

            if (!HasUsableEquippedGrenadeLauncher(botOwner))
            {
                rejectReason = "noUsableEquippedLauncher";
                return false;
            }

            // Do not require the rifle-oriented CanShoot flag here. A launcher can have a safe
            // low ballistic arc over the obstruction which made the straight weapon lane fail.
            // Target collection still requires visible or very recent personal contact, and the
            // grenadier objective performs the full impact, friendly-lane, and arc validation.
            return CollectAutonomousGrenadeLauncherTargets(
                goalEnemy,
                GrenadeLauncherAutoUnsafeRadius,
                out rejectReason).Count > 0;
        }

        public bool IsFirstPrimaryLauncherTargetTooCloseForCombat(EnemyInfo? goalEnemy)
        {
            if (!HasActiveCombatEnemy(goalEnemy) ||
                goalEnemy?.IsVisible != true ||
                !IsFirstPrimaryGrenadeLauncherSelectedOrActive(botOwner))
            {
                return false;
            }

            Vector3 target = GetEnemyCurrentPosition(goalEnemy);
            return IsFinite(target) &&
                   (target - botOwner.Position).sqrMagnitude <
                   GrenadeLauncherMinTargetDistance * GrenadeLauncherMinTargetDistance;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> CreateGrenadeLauncherMoveDecision(GrenadeLauncherFirePlan plan)
        {
            if (plan.SuppressFrom != null)
            {
                botOwner.Steering.LookToPoint(plan.FirstTarget);
                botOwner.SetPose(1f);
                botOwner.GoToSomePointData.SetPoint(plan.SuppressFrom.Position);
            }

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.goToPoint,
                $"{plan.ReasonPrefix}.launcherMove");
        }

        public bool IsAtGrenadeLauncherFirePosition(GrenadeLauncherFirePlan? plan)
        {
            if (plan?.SuppressFrom == null)
            {
                return true;
            }

            if (botOwner.GoToSomePointData?.IsCome() == true &&
                botOwner.GoToSomePointData.HaveTarget() &&
                (botOwner.GoToSomePointData.Point - plan.SuppressFrom.Position).sqrMagnitude <= 1.5f * 1.5f)
            {
                return true;
            }

            return (botOwner.Position - plan.SuppressFrom.Position).sqrMagnitude <= 1.5f * 1.5f;
        }

        public bool TryStartGrenadeLauncherFireDecision(
            EnemyInfo goalEnemy,
            GrenadeLauncherFirePlan plan,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (plan == null || plan.Targets.Count == 0 || !IsFinite(plan.FirstTarget))
            {
                return RejectGrenadeLauncherSuppress("preparedLauncherPlanInvalid", goalEnemy);
            }

            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return RejectGrenadeLauncherSuppress($"{plan.ReasonPrefix}:noActiveEnemy", goalEnemy);
            }

            if (!HasUsableEquippedGrenadeLauncher(botOwner))
            {
                return RejectGrenadeLauncherSuppress($"{plan.ReasonPrefix}:noUsableEquippedLauncher", goalEnemy);
            }

            Vector3 fireOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.2f;
            if (!TryCanFireGrenadeLauncherAtTarget(fireOrigin, plan.FirstTarget, plan.UnsafeRadius, out string laneRejectReason))
            {
                return RejectGrenadeLauncherSuppress($"{plan.ReasonPrefix}:{laneRejectReason}:atLaunchPosition", goalEnemy);
            }

            if (!TrySelectEquippedGrenadeLauncher(
                    botOwner,
                    out bool changedToLauncher,
                    out EquipmentSlot launcherSlot))
            {
                return RejectGrenadeLauncherSuppress($"{plan.ReasonPrefix}:weaponSwitchFailed", goalEnemy);
            }

            if (changedToLauncher && launcherSlot == EquipmentSlot.SecondPrimaryWeapon)
            {
                ownsGrenadeLauncherSwitch = true;
            }

            if (!TryCanUseGrenadeLauncherNormalFire(
                    botOwner,
                    goalEnemy,
                    plan.Ordered,
                    out Vector3 normalFireTarget,
                    out string normalFireRejectReason))
            {
                TryReleaseOwnedGrenadeLauncher();
                return RejectGrenadeLauncherSuppress(
                    $"{plan.ReasonPrefix}:normalFireRejected:{normalFireRejectReason}",
                    goalEnemy);
            }

            // The objective owns only launcher selection and explosive safety. Actual aiming,
            // trigger cadence, and combat reload now run through EFT's ordinary shoot-from-place
            // node so a cylinder launcher can keep firing throughout the engagement.
            botOwner.Steering.LookToPoint(normalFireTarget);
            botOwner.BotTalk?.TrySay(EPhraseTrigger.GetInCover, true);
            WarnGrenadeLauncherImpacts(new List<Vector3> { normalFireTarget });
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.shootFromPlace,
                plan.DecisionReason);
            TryEmitGrenadeLauncherSuppressEvent(goalEnemy, normalFireTarget, decision.Reason);
            BattleRecorder.RecordGrenadeEvent(
                botOwner,
                "launcherInit",
                decision.Reason,
                goalEnemy: goalEnemy,
                target: normalFireTarget,
                suppressFrom: plan.SuppressFromPosition);
            return true;
        }

        internal static bool TryCanUseGrenadeLauncherNormalFire(
            BotOwner owner,
            EnemyInfo? goalEnemy,
            bool ordered,
            out Vector3 target,
            out string rejectReason)
        {
            target = Vector3.zero;
            if (owner == null || goalEnemy?.Person?.HealthController?.IsAlive != true)
            {
                rejectReason = "enemyMissingOrDead";
                return false;
            }

            if (!goalEnemy.IsVisible)
            {
                rejectReason = "enemyNotVisible";
                return false;
            }

            target = GetEnemyCurrentPosition(goalEnemy);
            return TryValidateGrenadeLauncherNormalFireTarget(
                owner,
                target,
                ordered,
                out rejectReason);
        }

        /// <summary>
        /// Keeps a first-primary launcher committed to the last personally seen impact point long
        /// enough for EFT's ordinary aiming worker to become ready. Support-slot launchers remain
        /// suppression weapons and deliberately do not use this visual-flicker grace path.
        /// </summary>
        internal static bool TryContinueFirstPrimaryGrenadeLauncherNormalFire(
            BotOwner owner,
            EnemyInfo? goalEnemy,
            bool ordered,
            out Vector3 target,
            out string rejectReason)
        {
            target = Vector3.zero;
            if (owner == null || goalEnemy?.Person?.HealthController?.IsAlive != true)
            {
                rejectReason = "enemyMissingOrDead";
                return false;
            }

            if (!IsFirstPrimaryGrenadeLauncherSelectedOrActive(owner))
            {
                rejectReason = "notFirstPrimaryLauncher";
                return false;
            }

            if (goalEnemy.IsVisible)
            {
                rejectReason = "enemyVisibleRequiresLiveTarget";
                return false;
            }

            if (!HasRecentPersonalContact(goalEnemy, FirstPrimaryLauncherNormalFireContactGraceSeconds))
            {
                rejectReason = "enemyVisualGraceExpired";
                return false;
            }

            target = IsFinite(goalEnemy.EnemyLastPositionReal) &&
                     goalEnemy.EnemyLastPositionReal.sqrMagnitude > 0.01f
                ? goalEnemy.EnemyLastPositionReal
                : GetEnemyAnchor(goalEnemy);

            return TryValidateGrenadeLauncherNormalFireTarget(
                owner,
                target,
                ordered,
                out rejectReason);
        }

        private static bool TryValidateGrenadeLauncherNormalFireTarget(
            BotOwner owner,
            Vector3 target,
            bool ordered,
            out string rejectReason)
        {
            if (!IsFinite(target))
            {
                rejectReason = "enemyPositionInvalid";
                return false;
            }

            float targetDistanceSqr = (target - owner.Position).sqrMagnitude;
            if (targetDistanceSqr < GrenadeLauncherMinTargetDistance * GrenadeLauncherMinTargetDistance)
            {
                rejectReason = "targetTooClose";
                return false;
            }

            if (targetDistanceSqr > GrenadeLauncherMaxTargetDistance * GrenadeLauncherMaxTargetDistance)
            {
                rejectReason = "targetTooFar";
                return false;
            }

            float unsafeRadius = ordered
                ? GrenadeLauncherOrderedUnsafeRadius
                : GrenadeLauncherAutoUnsafeRadius;
            Vector3 fireOrigin = owner.WeaponRoot != null
                ? owner.WeaponRoot.position
                : owner.Position + Vector3.up * 1.2f;
            float effectiveUnsafeRadius = GetGrenadeLauncherImpactUnsafeRadius(
                fireOrigin,
                target,
                unsafeRadius);
            if (FollowerShotSafety.IsFriendlyNearImpact(owner, target, effectiveUnsafeRadius))
            {
                rejectReason = "launcherImpactUnsafe";
                return false;
            }

            return TryCanFireGrenadeLauncherAtTarget(
                owner,
                fireOrigin,
                target,
                unsafeRadius,
                out rejectReason);
        }

        private void TryEmitGrenadeLauncherSuppressEvent(EnemyInfo goalEnemy, Vector3 target, string? reason)
        {
            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss ||
                string.IsNullOrEmpty(goalEnemy?.ProfileId))
            {
                return;
            }

            boss.CombatEvents.TryEmitLauncherSuppress(
                botOwner,
                goalEnemy.ProfileId,
                target,
                reason ?? string.Empty,
                IsAutonomousSuppressReason(reason) ? GrenadeLauncherAutoUnsafeRadius : GrenadeLauncherOrderedUnsafeRadius,
                GrenadeLauncherSuppressEventSeconds);
        }

        private void TryReleaseGrenadeLauncherSuppressEvent(string reason)
        {
            if (botOwner.BotFollower?.BossToFollow is pitAIBossPlayer boss)
            {
                boss.CombatEvents.TryReleaseLauncherSuppress(botOwner, reason);
            }
        }

        private List<Vector3> CollectOrderedGrenadeLauncherTargets(
            EnemyInfo goalEnemy,
            float unsafeRadius,
            out string? rejectReason)
        {
            string? rayRejectReason;
            List<Vector3> rayTargets = CollectOrderedGrenadeLauncherRayTargets(unsafeRadius, out rejectReason);
            if (rayTargets.Count > 0)
            {
                return rayTargets;
            }

            rayRejectReason = rejectReason;
            if (TryGetGrenadeLauncherTarget(
                    goalEnemy,
                    unsafeRadius,
                    requireVisible: false,
                    out Vector3 fallbackTarget,
                    out string? fallbackRejectReason))
            {
                BattleRecorder.RecordGrenadeEvent(
                    botOwner,
                    "launcherTargetFallback",
                    $"orderedGoalEnemyFallback:{rayRejectReason ?? "orderedRayNoTargets"}",
                    goalEnemy: goalEnemy,
                    target: fallbackTarget);
                rejectReason = null;
                return new List<Vector3> { fallbackTarget };
            }

            rejectReason = $"{rayRejectReason ?? "orderedRayNoTargets"};goalEnemyFallback:{fallbackRejectReason ?? "rejected"}";
            return new List<Vector3>();
        }

        private List<Vector3> CollectAutonomousGrenadeLauncherTargets(
            EnemyInfo goalEnemy,
            float unsafeRadius,
            out string? rejectReason)
        {
            rejectReason = null;
            List<Vector3> targets = new List<Vector3>();
            if (botOwner.EnemiesController?.EnemyInfos == null)
            {
                rejectReason = "missingEnemyInfos";
                return targets;
            }

            int rejectedTargets = 0;
            string? firstRejectReason = null;
            int requiredTargets = 1;
            foreach (EnemyInfo enemyInfo in botOwner.EnemiesController.EnemyInfos.Values)
            {
                if (!TryGetGrenadeLauncherTarget(
                        enemyInfo,
                        unsafeRadius,
                        requireVisible: true,
                        out Vector3 target,
                        out string? enemyRejectReason))
                {
                    rejectedTargets++;
                    if (firstRejectReason == null)
                    {
                        firstRejectReason = enemyRejectReason;
                    }

                    continue;
                }

                if (!targets.Exists(existing => (existing - target).sqrMagnitude <= 1f))
                {
                    targets.Add(target);
                }
            }

            if (targets.Count >= requiredTargets)
            {
                return targets;
            }

            rejectReason = targets.Count > 0
                ? $"tooFewValidTargets:{targets.Count}/{requiredTargets}"
                : $"noValidTargets:{firstRejectReason ?? "noneEvaluated"}:{rejectedTargets}";
            targets.Clear();
            return targets;
        }

        private bool TryGetGrenadeLauncherTarget(
            EnemyInfo enemyInfo,
            float unsafeRadius,
            bool requireVisible,
            out Vector3 target,
            out string? rejectReason)
        {
            target = Vector3.zero;
            rejectReason = null;
            if (!IsTrackedEnemyAlive(enemyInfo))
            {
                rejectReason = "targetNotAlive";
                return false;
            }

            if (requireVisible &&
                !enemyInfo.IsVisible &&
                !HasRecentPersonalContact(enemyInfo, GrenadeLauncherRecentKnownTargetSeconds))
            {
                rejectReason = "targetNotVisible";
                return false;
            }

            if (IsFriendlyGrenadeLauncherTarget(enemyInfo))
            {
                rejectReason = "targetFriendly";
                return false;
            }

            if (!TryGetGrenadeTargetPosition(enemyInfo, out Vector3 enemyAnchor))
            {
                rejectReason = "targetPositionUnknown";
                return false;
            }

            float targetDistanceSqr = (enemyAnchor - botOwner.Position).sqrMagnitude;
            if (targetDistanceSqr < GrenadeLauncherMinTargetDistance * GrenadeLauncherMinTargetDistance)
            {
                rejectReason = "targetTooClose";
                return false;
            }

            if (targetDistanceSqr > GrenadeLauncherMaxTargetDistance * GrenadeLauncherMaxTargetDistance)
            {
                rejectReason = "targetTooFar";
                return false;
            }

            float effectiveUnsafeRadius = GetGrenadeLauncherImpactUnsafeRadius(
                botOwner.Position,
                enemyAnchor,
                unsafeRadius);
            if (FollowerShotSafety.IsFriendlyNearImpact(botOwner, enemyAnchor, effectiveUnsafeRadius))
            {
                rejectReason = "friendlyNearImpact";
                return false;
            }

            target = enemyAnchor;
            return true;
        }

        private List<Vector3> CollectOrderedGrenadeLauncherRayTargets(float unsafeRadius, out string? rejectReason)
        {
            rejectReason = null;
            List<Vector3> targets = new List<Vector3>();
            List<float> targetScores = new List<float>();
            if (!TryGetOrderedSuppressRay(out Ray ray, out rejectReason))
            {
                return targets;
            }

            RaycastHit[] hits = new RaycastHit[20];
            int hitCount = Physics.SphereCastNonAlloc(
                ray,
                OrderedLauncherRayScanDistance * 0.5f,
                hits,
                OrderedLauncherRayScanDistance * 0.5f,
                LayerMaskClass.PlayerMask);

            if (hitCount <= 0)
            {
                rejectReason = "orderedRayNoHits";
                return targets;
            }

            int rejectedTargets = 0;
            string? firstRejectReason = null;
            for (int i = 0; i < hitCount; i++)
            {
                Player enemy = hits[i].collider?.gameObject?.GetComponentInParent<Player>();
                if (!TryGetOrderedGrenadeLauncherPlayerTarget(enemy, unsafeRadius, out Vector3 target, out string? targetRejectReason))
                {
                    rejectedTargets++;
                    if (firstRejectReason == null)
                    {
                        firstRejectReason = targetRejectReason;
                    }

                    continue;
                }

                if (!TryGetOrderedLauncherRayScore(ray, target, out float score, out targetRejectReason))
                {
                    rejectedTargets++;
                    if (firstRejectReason == null)
                    {
                        firstRejectReason = targetRejectReason;
                    }

                    continue;
                }

                if (!targets.Exists(existing => (existing - target).sqrMagnitude <= 1f))
                {
                    InsertOrderedLauncherTarget(targets, targetScores, target, score);
                }
            }

            if (targets.Count == 0)
            {
                rejectReason = $"orderedRayNoTargets:{firstRejectReason ?? "noneEvaluated"}:{rejectedTargets}";
            }

            return targets;
        }

        private static bool TryGetOrderedLauncherRayScore(
            Ray ray,
            Vector3 target,
            out float score,
            out string? rejectReason)
        {
            score = 0f;
            rejectReason = null;

            Vector3 rayDirection = Vector3.ProjectOnPlane(ray.direction, Vector3.up);
            if (rayDirection.sqrMagnitude <= 0.001f)
            {
                rejectReason = "orderedRayDirectionInvalid";
                return false;
            }

            rayDirection.Normalize();
            Vector3 offset = Vector3.ProjectOnPlane(target - ray.origin, Vector3.up);
            if (offset.sqrMagnitude <= 0.001f)
            {
                rejectReason = "orderedRayTargetAtOrigin";
                return false;
            }

            float alongRay = Vector3.Dot(offset, rayDirection);
            if (alongRay < 0f)
            {
                rejectReason = "targetBehindOrderRay";
                return false;
            }

            if (alongRay > OrderedLauncherRayScanDistance)
            {
                rejectReason = "targetBeyondOrderRay";
                return false;
            }

            Vector3 closestPoint = rayDirection * alongRay;
            float perpendicularDistanceSqr = (offset - closestPoint).sqrMagnitude;
            if (perpendicularDistanceSqr >
                OrderedLauncherRayMaxPerpendicularDistance * OrderedLauncherRayMaxPerpendicularDistance)
            {
                rejectReason = "targetOffOrderRay";
                return false;
            }

            score = perpendicularDistanceSqr + alongRay * 0.01f;
            return true;
        }

        private static void InsertOrderedLauncherTarget(
            List<Vector3> targets,
            List<float> targetScores,
            Vector3 target,
            float score)
        {
            for (int i = 0; i < targetScores.Count; i++)
            {
                if (score < targetScores[i])
                {
                    targets.Insert(i, target);
                    targetScores.Insert(i, score);
                    return;
                }
            }

            targets.Add(target);
            targetScores.Add(score);
        }

        private bool TryGetOrderedSuppressRay(out Ray ray, out string? rejectReason)
        {
            ray = default;
            rejectReason = null;

            if (!IsFinite(orderedSuppressTarget) || orderedSuppressTarget.sqrMagnitude <= 0.01f)
            {
                rejectReason = "orderedRayMissing";
                return false;
            }

            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                rejectReason = "orderedRayNoBoss";
                return false;
            }

            Vector3 origin = boss.realPlayer.Transform != null
                ? boss.realPlayer.Transform.position
                : boss.realPlayer.Position;
            Vector3 direction = orderedSuppressTarget - origin;
            if (!IsFinite(direction) || direction.sqrMagnitude <= 0.01f)
            {
                rejectReason = "orderedRayInvalid";
                return false;
            }

            ray = new Ray(origin, direction.normalized);
            return true;
        }

        private bool TryGetOrderedGrenadeLauncherPlayerTarget(
            Player? enemy,
            float unsafeRadius,
            out Vector3 target,
            out string? rejectReason)
        {
            target = Vector3.zero;
            rejectReason = null;
            if (enemy == null)
            {
                rejectReason = "rayHitNoPlayer";
                return false;
            }

            if (enemy.HealthController?.IsAlive != true)
            {
                rejectReason = "targetNotAlive";
                return false;
            }

            if (!IsOrderedGrenadeLauncherEnemy(enemy))
            {
                rejectReason = "targetFriendly";
                return false;
            }

            Vector3 enemyPosition = enemy.Transform != null ? enemy.Transform.position : enemy.Position;
            if (!IsFinite(enemyPosition) || enemyPosition.sqrMagnitude <= 0.01f)
            {
                rejectReason = "targetPositionInvalid";
                return false;
            }

            float targetDistanceSqr = (enemyPosition - botOwner.Position).sqrMagnitude;
            if (targetDistanceSqr < GrenadeLauncherMinTargetDistance * GrenadeLauncherMinTargetDistance)
            {
                rejectReason = "targetTooClose";
                return false;
            }

            if (targetDistanceSqr > GrenadeLauncherMaxTargetDistance * GrenadeLauncherMaxTargetDistance)
            {
                rejectReason = "targetTooFar";
                return false;
            }

            float effectiveUnsafeRadius = GetGrenadeLauncherImpactUnsafeRadius(
                botOwner.Position,
                enemyPosition,
                unsafeRadius);
            if (FollowerShotSafety.IsFriendlyNearImpact(botOwner, enemyPosition, effectiveUnsafeRadius))
            {
                rejectReason = "friendlyNearImpact";
                return false;
            }

            target = enemyPosition;
            return true;
        }

        private bool IsOrderedGrenadeLauncherEnemy(Player enemy)
        {
            if (enemy == null ||
                string.Equals(enemy.ProfileId, botOwner.ProfileId, StringComparison.Ordinal) ||
                BossPlayers.IsPlayerBoss(enemy.ProfileId))
            {
                return false;
            }

            if (botOwner.BotFollower?.BossToFollow is pitAIBossPlayer boss)
            {
                if (boss.Followers != null)
                {
                    for (int i = 0; i < boss.Followers.Count; i++)
                    {
                        BotOwner follower = boss.Followers[i];
                        if (follower != null && string.Equals(follower.ProfileId, enemy.ProfileId, StringComparison.Ordinal))
                        {
                            return false;
                        }
                    }
                }

                if (boss.bossGroup?.IsEnemy(enemy) == true ||
                    boss.bossGroup?.IsPlayerEnemy(enemy) == true)
                {
                    return true;
                }
            }

            BotOwner? enemyOwner = enemy.AIData?.BotOwner;
            return enemyOwner?.BotsGroup != null &&
                   (enemyOwner.BotsGroup.IsEnemy(botOwner.GetPlayer) ||
                    enemyOwner.BotsGroup.IsPlayerEnemy(botOwner.GetPlayer) ||
                    enemyOwner.Memory?.GoalEnemy?.ProfileId == botOwner.ProfileId);
        }

        private bool TryGetGrenadeTargetPosition(EnemyInfo enemyInfo, out Vector3 target)
        {
            target = Vector3.zero;
            if (enemyInfo == null)
            {
                return false;
            }

            if (enemyInfo.IsVisible || enemyInfo.CanShoot)
            {
                target = GetEnemyAnchor(enemyInfo);
                return IsFinite(target) && target.sqrMagnitude > 0.01f;
            }

            Vector3 sharedLastPosition = enemyInfo.EnemyLastPositionReal;
            if (IsFinite(sharedLastPosition) &&
                sharedLastPosition.sqrMagnitude > 0.01f &&
                (sharedLastPosition - botOwner.Position).sqrMagnitude > 0.01f &&
                (enemyInfo.HaveSeen || enemyInfo.PersonalLastSeenTime > 0f))
            {
                target = sharedLastPosition;
                return true;
            }

            return Enemy.TryGetReliableKnownPosition(botOwner, enemyInfo, out target);
        }

        private bool RejectGrenadeLauncherSuppress(string reason, EnemyInfo? goalEnemy = null)
        {
            RecordGrenadeLauncherSuppressReject(reason, goalEnemy);
            return false;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordGrenadeLauncherSuppressReject(string reason, EnemyInfo? goalEnemy)
        {
            if (!BattleRecorder.IsRecordingFor(botOwner, requireRecordedCombat: true))
            {
                return;
            }

            if (string.Equals(lastGrenadeLauncherSuppressRejectReason, reason, StringComparison.Ordinal) &&
                Time.time < nextGrenadeLauncherSuppressRejectRecordAt)
            {
                return;
            }

            lastGrenadeLauncherSuppressRejectReason = reason;
            nextGrenadeLauncherSuppressRejectRecordAt = Time.time + 2f;
            BattleRecorder.RecordGrenadeEvent(botOwner, "launcherReject", reason, goalEnemy: goalEnemy);
        }

        private bool IsFriendlyGrenadeLauncherTarget(EnemyInfo enemyInfo)
        {
            string? profileId = enemyInfo.ProfileId;
            if (string.IsNullOrEmpty(profileId))
            {
                profileId = enemyInfo.Person?.ProfileId;
            }

            if (string.IsNullOrEmpty(profileId))
            {
                return false;
            }

            if (string.Equals(profileId, botOwner.ProfileId, StringComparison.Ordinal) ||
                BossPlayers.IsPlayerBoss(profileId))
            {
                return true;
            }

            if (botOwner.BotFollower?.BossToFollow is pitAIBossPlayer boss && boss.Followers != null)
            {
                for (int i = 0; i < boss.Followers.Count; i++)
                {
                    BotOwner follower = boss.Followers[i];
                    if (follower != null && string.Equals(follower.ProfileId, profileId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            BotOwner? enemyOwner = enemyInfo.Person?.AIData?.BotOwner;
            if (enemyOwner != null && BossPlayers.IsFollower(enemyOwner))
            {
                return true;
            }

            WildSpawnType? role = enemyInfo.Person?.Profile?.Info?.Settings?.Role;
            return role.HasValue && Props.friendlyBotTypes.Contains(role.Value);
        }

        private bool CanFireGrenadeLauncherAtTarget(Vector3 fireOrigin, Vector3 target)
        {
            return TryCanFireGrenadeLauncherAtTarget(fireOrigin, target, GrenadeLauncherOrderedUnsafeRadius, out _);
        }

        private bool TryCanFireGrenadeLauncherAtTarget(Vector3 fireOrigin, Vector3 target, out string rejectReason)
        {
            return TryCanFireGrenadeLauncherAtTarget(fireOrigin, target, GrenadeLauncherOrderedUnsafeRadius, out rejectReason);
        }

        private bool TryCanFireGrenadeLauncherAtTarget(Vector3 fireOrigin, Vector3 target, float unsafeRadius, out string rejectReason)
        {
            return TryCanFireGrenadeLauncherAtTarget(botOwner, fireOrigin, target, unsafeRadius, out rejectReason);
        }

        internal static bool TryCanFireGrenadeLauncherAtTarget(
            BotOwner botOwner,
            Vector3 fireOrigin,
            Vector3 target,
            float unsafeRadius,
            out string rejectReason)
        {
            return TryCanFireGrenadeLauncherAtTarget(
                botOwner,
                fireOrigin,
                target,
                unsafeRadius,
                out rejectReason,
                out _);
        }

        internal static bool TryCanFireGrenadeLauncherAtTarget(
            BotOwner botOwner,
            Vector3 fireOrigin,
            Vector3 target,
            float unsafeRadius,
            out string rejectReason,
            out Vector3 acceptedFireOrigin)
        {
            acceptedFireOrigin = fireOrigin;
            if (!IsFinite(fireOrigin) || !IsFinite(target))
            {
                rejectReason = "launcherLaneInvalid";
                return false;
            }

            if (TryCanFireGrenadeLauncherFromOrigin(botOwner, fireOrigin, target, unsafeRadius, out rejectReason))
            {
                return true;
            }

            Vector3 standingFireOrigin = GetStandingSuppressionFireOrigin(botOwner);
            if ((standingFireOrigin - fireOrigin).sqrMagnitude > 0.04f &&
                TryCanFireGrenadeLauncherFromOrigin(botOwner, standingFireOrigin, target, unsafeRadius, out _))
            {
                acceptedFireOrigin = standingFireOrigin;
                rejectReason = string.Empty;
                return true;
            }

            return false;
        }

        private static bool TryCanFireGrenadeLauncherFromOrigin(
            BotOwner botOwner,
            Vector3 fireOrigin,
            Vector3 target,
            float unsafeRadius,
            out string rejectReason)
        {
            ShootPointClass shootPoint = new ShootPointClass(target, 1f);
            bool hasDirectLane = Utils.Utils.CanShootToTarget(shootPoint, fireOrigin, botOwner.LookSensor.Mask, false);
            bool hasSoftFoliageLane = !hasDirectLane &&
                                      IsSoftObstructedSuppressionLane(fireOrigin, target, botOwner.LookSensor.Mask);
            string arcRejectDetail = string.Empty;
            bool hasLauncherArcLane = !hasDirectLane &&
                                      !hasSoftFoliageLane &&
                                      TryHasGrenadeLauncherArcLane(
                                          botOwner,
                                          fireOrigin,
                                          target,
                                          botOwner.LookSensor.Mask,
                                          unsafeRadius,
                                          out arcRejectDetail);
            if (!hasDirectLane && !hasSoftFoliageLane && !hasLauncherArcLane)
            {
                rejectReason = string.IsNullOrEmpty(arcRejectDetail)
                    ? "launcherArcLaneBlocked"
                    : $"launcherArcLaneBlocked:{arcRejectDetail}";
                return false;
            }

            if (FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, target))
            {
                rejectReason = "friendlyInLauncherLane";
                return false;
            }

            rejectReason = string.Empty;
            return true;
        }

        private static bool TryHasGrenadeLauncherArcLane(
            BotOwner botOwner,
            Vector3 fireOrigin,
            Vector3 target,
            LayerMask mask,
            float unsafeRadius,
            out string rejectDetail)
        {
            rejectDetail = string.Empty;
            Vector3 aimPoint = GetGrenadeLauncherSuppressAimPoint(botOwner, fireOrigin, target);
            if (!IsFinite(aimPoint) || (aimPoint - target).sqrMagnitude <= 0.04f)
            {
                rejectDetail = "invalidAimPoint";
                return false;
            }

            Weapon? launcher = GetActiveOrEquippedGrenadeLauncher(botOwner);
            float speed = GetGrenadeLauncherMuzzleVelocity(launcher);
            Vector3 aimOffset = aimPoint - fireOrigin;
            float aimDistance = aimOffset.magnitude;
            if (aimDistance <= 0.001f || speed <= 1f)
            {
                rejectDetail = $"invalidAim:distance={aimDistance:0.0}:speed={speed:0.0}";
                return false;
            }

            Vector3 aimDirection = aimOffset / aimDistance;
            Vector3 planarAimDirection = aimDirection;
            planarAimDirection.y = 0f;
            float horizontalSpeed = planarAimDirection.magnitude * speed;
            Vector3 planarTargetOffset = target - fireOrigin;
            planarTargetOffset.y = 0f;
            float horizontalDistance = planarTargetOffset.magnitude;
            if (horizontalSpeed <= 1f || horizontalDistance <= 0.001f)
            {
                rejectDetail = $"invalidTravel:distance={horizontalDistance:0.0}:speed={horizontalSpeed:0.0}";
                return false;
            }

            float travelTime = horizontalDistance / horizontalSpeed;
            if (float.IsNaN(travelTime) || float.IsInfinity(travelTime) || travelTime <= 0f)
            {
                rejectDetail = $"invalidTravelTime:{travelTime}";
                return false;
            }

            int samples = Mathf.Clamp(
                Mathf.CeilToInt(horizontalDistance / GrenadeLauncherArcLaneSampleMeters),
                GrenadeLauncherArcLaneMinSamples,
                GrenadeLauncherArcLaneMaxSamples);
            float impactTolerance = GetGrenadeLauncherArcImpactTolerance(launcher, unsafeRadius);
            float ignoreImpactDistanceSqr = impactTolerance * impactTolerance;
            RaycastHit[] hits = new RaycastHit[GrenadeLauncherArcLaneMaxHitsPerSegment];
            Vector3 previous = fireOrigin;
            for (int i = 1; i <= samples; i++)
            {
                float t = travelTime * i / samples;
                Vector3 current = fireOrigin + aimDirection * speed * t;
                current.y -= 0.5f * GrenadeLauncherAimGravity * t * t;
                if (!IsFinite(current))
                {
                    rejectDetail = $"invalidSample:{i}/{samples}";
                    return false;
                }

                Vector3 segment = current - previous;
                float segmentDistance = segment.magnitude;
                if (segmentDistance <= 0.001f)
                {
                    previous = current;
                    continue;
                }

                int hitCount = Physics.RaycastNonAlloc(
                    new Ray(previous, segment / segmentDistance),
                    hits,
                    segmentDistance,
                    mask);
                if (HasHardLauncherArcObstruction(
                        hits,
                        hitCount,
                        target,
                        ignoreImpactDistanceSqr,
                        out RaycastHit obstruction))
                {
                    Collider collider = obstruction.collider;
                    string colliderName = collider?.gameObject?.name ?? collider?.name ?? "unknown";
                    string layerName = collider != null
                        ? LayerMask.LayerToName(collider.gameObject.layer)
                        : "unknown";
                    float impactOffset = Vector3.Distance(obstruction.point, target);
                    float aimRaise = aimPoint.y - target.y;
                    rejectDetail =
                        $"hard={colliderName}:layer={layerName}:impactOffset={impactOffset:0.0}:" +
                        $"tolerance={impactTolerance:0.0}:sample={i}/{samples}:speed={speed:0.0}:raise={aimRaise:0.0}";
                    return false;
                }

                previous = current;
            }

            return true;
        }

        private static float GetGrenadeLauncherArcImpactTolerance(Weapon? launcher, float unsafeRadius)
        {
            float explosionRadius = launcher?.CurrentAmmoTemplate?.MaxExplosionDistance ?? 0f;
            if (explosionRadius <= 0.1f)
            {
                return GrenadeLauncherArcImpactFallbackTolerance;
            }

            // Launcher targets are enemy root positions on the ground. A terrain contact close enough
            // to damage that target is an impact, not a blocked arc. Keep the accepted offset inside
            // the existing friendly-clear radius so a shifted impact cannot expand the danger area.
            float friendlySafetyBudget = Mathf.Max(0.25f, unsafeRadius - explosionRadius);
            return Mathf.Min(explosionRadius, friendlySafetyBudget);
        }

        private void WarnGrenadeLauncherImpacts(List<Vector3> targets)
        {
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            float delay = targets.Count * 2f;
            for (int i = 0; i < targets.Count; i++)
            {
                Singleton<BotEventHandler>.Instance.ArtilleryStart(targets[i], 20f, delay);
            }
        }

        public bool TryPrepareGrenadeLauncherWeaponForSuppress(
            string reasonPrefix,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            out bool ready,
            out string failReason)
        {
            decision = default;
            ready = false;
            failReason = string.Empty;

            BotWeaponManager? weaponManager = botOwner?.WeaponManager;
            BotWeaponSelector? selector = weaponManager?.Selector;
            Weapon? activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
            Weapon? launcher = GetEquippedGrenadeLauncher(botOwner, out EquipmentSlot launcherSlot);
            if (launcher == null)
            {
                failReason = "noUsableEquippedLauncher";
                return false;
            }

            bool selectedLauncher = selector?.LastEquipmentSlot == launcherSlot;
            bool activeLauncher = IsSameWeapon(activeWeapon, launcher);
            bool reloadableEmptyLauncher =
                CountLoadedRounds(launcher) <= 0 &&
                !IsSingleUseLauncherWeapon(launcher);

            // EFT may automatically leave an empty cylinder launcher for the holster. Start the
            // bounded reload window from the equipped launcher itself so selector recovery remains
            // possible even when the launcher is no longer the active hands item.
            if (reloadableEmptyLauncher && activeLauncherSuppressReloadStartedAt <= 0f)
            {
                activeLauncherSuppressReloadStartedAt = Time.time;
                activeLauncherSuppressReloadRequested = false;
                BattleRecorder.RecordGrenadeEvent(
                    botOwner,
                    "launcherReloadWait",
                    $"{reasonPrefix}.reload:loaded=0",
                    goalEnemy: botOwner.Memory?.GoalEnemy);
            }

            bool reloadWindowActive =
                reloadableEmptyLauncher &&
                Time.time - activeLauncherSuppressReloadStartedAt <= GrenadeLauncherSuppressReloadWaitSeconds;
            if (reloadableEmptyLauncher && !reloadWindowActive)
            {
                failReason = "launcherReloadTimedOut";
                return false;
            }

            if (!selectedLauncher)
            {
                if (!TrySelectEquippedGrenadeLauncher(
                        botOwner,
                        out bool changedToLauncher,
                        out launcherSlot))
                {
                    if (reloadWindowActive)
                    {
                        HoldFor(0.15f);
                        decision = CreateLauncherPreparationHold($"{reasonPrefix}.launcherSwitch");
                        return true;
                    }

                    failReason = "weaponSwitchFailed";
                    return false;
                }

                if (changedToLauncher && launcherSlot == EquipmentSlot.SecondPrimaryWeapon)
                {
                    ownsGrenadeLauncherSwitch = true;
                }

                HoldFor(0.15f);
                decision = CreateLauncherPreparationHold($"{reasonPrefix}.launcherSwitch");
                return true;
            }

            if (selector?.IsChanging == true ||
                !activeLauncher ||
                activeWeapon == null)
            {
                HoldFor(0.15f);
                decision = CreateLauncherPreparationHold($"{reasonPrefix}.launcherSwitch");
                return true;
            }

            int loadedRounds = CountLoadedRounds(activeWeapon);
            if (loadedRounds > 0)
            {
                ready = true;
                ClearLauncherSuppressReloadTracking();
                return true;
            }

            if (IsSingleUseLauncherWeapon(activeWeapon))
            {
                failReason = "launcherNoLoadedRounds";
                return false;
            }

            BotReload? reload = weaponManager?.Reload;
            if (reload == null)
            {
                failReason = "launcherReloadUnavailable";
                return false;
            }

            if (Time.time - activeLauncherSuppressReloadStartedAt > GrenadeLauncherSuppressReloadWaitSeconds)
            {
                failReason = "launcherReloadTimedOut";
                return false;
            }

            if (!reload.Reloading &&
                Time.time >= nextLauncherSuppressReloadRequestAt)
            {
                nextLauncherSuppressReloadRequestAt = Time.time + 0.5f;
                bool reloadStarted = TryStartActiveGrenadeLauncherLooseAmmoReload(
                    botOwner,
                    activeWeapon,
                    out string blockReason);
                BattleRecorder.RecordGrenadeEvent(
                    botOwner,
                    activeLauncherSuppressReloadRequested ? "launcherReloadRetry" : "launcherReloadStart",
                    $"{reasonPrefix}.reload:started={reloadStarted}:block={blockReason}",
                    goalEnemy: botOwner.Memory?.GoalEnemy);
                activeLauncherSuppressReloadRequested = true;
            }

            HoldFor(0.15f);
            decision = CreateLauncherPreparationHold($"{reasonPrefix}.launcherReload");
            return true;
        }

        private static bool HasHardLauncherArcObstruction(
            RaycastHit[] hits,
            int hitCount,
            Vector3 target,
            float ignoreImpactDistanceSqr,
            out RaycastHit obstruction)
        {
            obstruction = default;
            int count = Mathf.Min(hitCount, hits.Length);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                Collider collider = hit.collider;
                if (collider == null)
                {
                    continue;
                }

                if ((hit.point - target).sqrMagnitude <= ignoreImpactDistanceSqr)
                {
                    continue;
                }

                if (!IsSoftFoliageCollider(collider))
                {
                    obstruction = hit;
                    return true;
                }
            }

            return false;
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> CreateLauncherPreparationHold(string reason)
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                reason);
        }

        private void ClearLauncherSuppressReloadTracking()
        {
            activeLauncherSuppressReloadStartedAt = 0f;
            nextLauncherSuppressReloadRequestAt = 0f;
            activeLauncherSuppressReloadRequested = false;
        }

        public static bool TryStartActiveGrenadeLauncherLooseAmmoReload(
            BotOwner? owner,
            Weapon? launcher,
            out string blockReason)
        {
            blockReason = string.Empty;

            if (owner?.WeaponManager == null ||
                launcher == null)
            {
                blockReason = "unavailable";
                return false;
            }

            BotWeaponManager weaponManager = owner.WeaponManager;
            BotReload? reload = weaponManager.Reload;
            IFirearmHandsController? shootController = weaponManager.ShootController;
            if (reload == null || shootController == null)
            {
                blockReason = "reloadUnavailable";
                return false;
            }

            if (reload.Reloading)
            {
                blockReason = "reloading";
                return true;
            }

            if (weaponManager.Selector?.IsChanging == true)
            {
                blockReason = "selectorChanging";
                return false;
            }

            if (!weaponManager.IsWeaponReady)
            {
                blockReason = "weaponNotReady";
                return false;
            }

            if (!ReferenceEquals(shootController.Item, launcher))
            {
                blockReason = "launcherNotActive";
                return false;
            }

            if (launcher is RevolverItemClass && !weaponManager.InIdleState())
            {
                blockReason = "notIdle";
                return false;
            }

            if (!shootController.CanStartReload())
            {
                blockReason = "cannotStartReload";
                return false;
            }

            if (!TryCollectLooseLauncherAmmo(owner, launcher, out List<AmmoItemClass> ammoToLoad))
            {
                blockReason = "noCompatibleLooseAmmo";
                return false;
            }

            MongoID ammoTemplateId = ammoToLoad[0].TemplateId;
            int stockCount = ammoToLoad[0].StackObjectsCount;
            Callback callback = new Callback(result =>
            {
                reload.NextReloadTime = Time.time + 0.5f;
                reload.Reloading = false;
                reload.AddAmmoToPockets(ammoTemplateId, stockCount);
            });

            try
            {
                reload.ReloadType = BotReload.EReloadType.AmmoReload;
                reload.Reloading = true;
                AmmoPackReloadingClass ammoPack = new AmmoPackReloadingClass(ammoToLoad);
                owner.BotTalk?.Say(EPhraseTrigger.OnWeaponReload, false, null);

                if (launcher.ReloadMode == Weapon.EReloadMode.OnlyBarrel)
                {
                    shootController.ReloadBarrels(ammoPack, null, callback);
                }
                else if (launcher is RevolverItemClass)
                {
                    shootController.ReloadCylinderMagazine(ammoPack, callback, false);
                }
                else
                {
                    shootController.ReloadWithAmmo(ammoPack, callback);
                }

                blockReason = "started";
                return true;
            }
            catch (Exception ex)
            {
                reload.Reloading = false;
                blockReason = "exception";
                pitFireTeam.Log?.LogError($"[Combat][Launcher] Failed to start loose-ammo reload: {ex}");
                return false;
            }
        }

        public static bool CanReloadGrenadeLauncherFromLooseAmmo(BotOwner? owner, Weapon? launcher)
        {
            return owner != null &&
                   launcher != null &&
                   IsGrenadeLauncherWeapon(launcher) &&
                   !IsSingleUseLauncherWeapon(launcher) &&
                   GetMissingLooseLauncherRounds(launcher) > 0 &&
                   TryCollectLooseLauncherAmmo(owner, launcher, out _);
        }

        private static bool TryCollectLooseLauncherAmmo(
            BotOwner owner,
            Weapon launcher,
            out List<AmmoItemClass> ammoToLoad)
        {
            ammoToLoad = new List<AmmoItemClass>();
            int missingRounds = GetMissingLooseLauncherRounds(launcher);
            if (missingRounds <= 0)
            {
                return false;
            }

            List<AmmoItemClass> candidates = new List<AmmoItemClass>();
            InventoryController inventoryController = owner.GetPlayer.InventoryController;
            Predicate<AmmoItemClass> predicate = ammo =>
                ammo != null &&
                ammo.StackObjectsCount > 0 &&
                inventoryController.Examined(ammo) &&
                ammo.CheckAction(null).Succeeded &&
                CanActiveLauncherAcceptLooseAmmo(launcher, ammo);

            // The launcher-loot policy fills normal storage before using secure storage. Search
            // those same destinations explicitly so cylinder launchers can consume rounds from a
            // backpack or the secure fallback instead of being limited to vanilla fast-access slots.
            inventoryController.GetAcceptableItemsNonAlloc(
                LauncherLooseAmmoReloadSlots,
                candidates,
                predicate,
                null);

            for (int i = 0; i < candidates.Count && missingRounds > 0; i++)
            {
                AmmoItemClass ammo = candidates[i];
                ammoToLoad.Add(ammo);
                missingRounds -= ammo.StackObjectsCount;
            }

            return ammoToLoad.Count > 0;
        }

        private static int GetMissingLooseLauncherRounds(Weapon launcher)
        {
            if (launcher.ReloadMode == Weapon.EReloadMode.OnlyBarrel)
            {
                int emptyChambers = 0;
                Slot[] chambers = launcher.Chambers;
                for (int i = 0; i < chambers.Length; i++)
                {
                    if (chambers[i].ContainedItem == null)
                    {
                        emptyChambers++;
                    }
                }

                return emptyChambers;
            }

            MagazineItemClass? magazine = launcher.GetCurrentMagazine();
            if (magazine != null)
            {
                int missing = magazine.MaxCount - magazine.Count;
                if (launcher.ChamberAmmoCount == 0 && !launcher.HasChambers)
                {
                    return missing;
                }

                return Math.Max(0, missing);
            }

            return launcher.ChamberAmmoCount == 0 ? 1 : 0;
        }

        private static bool CanActiveLauncherAcceptLooseAmmo(Weapon launcher, AmmoItemClass ammo)
        {
            MagazineItemClass? magazine = launcher.GetCurrentMagazine();
            if (magazine != null && magazine.CheckCompatibility(ammo))
            {
                return true;
            }

            Slot[] chambers = launcher.Chambers;
            for (int i = 0; i < chambers.Length; i++)
            {
                if (chambers[i].CanAccept(ammo))
                {
                    return true;
                }
            }

            return false;
        }

        private void TryReleaseOwnedGrenadeLauncher()
        {
            if (!ownsGrenadeLauncherSwitch)
            {
                return;
            }

            RequestLauncherPrimaryFallback("ownedLauncherRelease");
            if (!IsSupportGrenadeLauncherSelectedOrActive())
            {
                ownsGrenadeLauncherSwitch = false;
            }
        }

        public bool TryCreateSuppressFromPlaceDecision(
            EnemyInfo goalEnemy,
            string reasonPrefix,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool allowSoftObstructedSuppression = false)
        {
            decision = default;
            if (string.IsNullOrEmpty(reasonPrefix) ||
                !HasActiveCombatEnemy(goalEnemy) ||
                botOwner.SuppressShoot == null ||
                !CanCurrentWeaponSuppress() ||
                !TryGetSuppressTarget(goalEnemy, out Vector3 suppressTarget))
            {
                return false;
            }

            ShootPointClass shootPoint = new ShootPointClass(suppressTarget, 1f);
            Vector3 fireOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.2f;

            if (Utils.Utils.CanShootToTarget(shootPoint, fireOrigin, botOwner.LookSensor.Mask, false) &&
                !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget) &&
                botOwner.SuppressShoot.InitToPoint(suppressTarget, null))
            {
                botOwner.Steering.LookToPoint(suppressTarget);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    $"{reasonPrefix}.place");
                return true;
            }

            if (allowSoftObstructedSuppression &&
                IsSoftObstructedSuppressionLane(fireOrigin, suppressTarget, botOwner.LookSensor.Mask) &&
                !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget) &&
                botOwner.SuppressShoot.InitToPoint(suppressTarget, null))
            {
                botOwner.Steering.LookToPoint(suppressTarget);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    $"{reasonPrefix}.softObstructedPlace");
                return true;
            }

            return false;
        }

        public bool TryCreateSoftObstructedSuppressDecision(
            EnemyInfo goalEnemy,
            string reasonPrefix,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            if (TryCreateSuppressDecision(goalEnemy, reasonPrefix, out decision))
            {
                return true;
            }

            decision = default;
            if (string.IsNullOrEmpty(reasonPrefix) ||
                !HasActiveCombatEnemy(goalEnemy) ||
                botOwner.SuppressShoot == null ||
                !CanCurrentWeaponSuppress() ||
                !TryGetSuppressTarget(goalEnemy, out Vector3 suppressTarget))
            {
                return false;
            }

            Vector3 fireOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.2f;

            if (!IsSoftObstructedSuppressionLane(fireOrigin, suppressTarget, botOwner.LookSensor.Mask) ||
                FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget) ||
                !botOwner.SuppressShoot.InitToPoint(suppressTarget, null))
            {
                return false;
            }

            botOwner.Steering.LookToPoint(suppressTarget);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.suppressFire,
                $"{reasonPrefix}.softObstructedPlace");
            return true;
        }

        internal bool IsSoftObstructedSuppressionLane(Vector3 fireOrigin, Vector3 suppressTarget)
        {
            return IsSoftObstructedSuppressionLane(fireOrigin, suppressTarget, botOwner.LookSensor.Mask);
        }

        internal static bool IsSoftObstructedSuppressionLane(Vector3 fireOrigin, Vector3 suppressTarget, LayerMask mask)
        {
            Vector3 direction = suppressTarget - fireOrigin;
            float distance = direction.magnitude;
            if (distance <= 0.001f)
            {
                return false;
            }

            RaycastHit[] softObstructionHits = new RaycastHit[SoftSuppressionLaneMaxHits];
            int hitCount = Physics.RaycastNonAlloc(
                new Ray(fireOrigin, direction / distance),
                softObstructionHits,
                distance,
                mask);
            if (hitCount <= 0)
            {
                return false;
            }

            bool foundSoftObstruction = false;
            float targetIgnoreDistanceSqr =
                SoftSuppressionLaneTargetIgnoreDistance *
                SoftSuppressionLaneTargetIgnoreDistance;
            int count = Mathf.Min(hitCount, softObstructionHits.Length);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = softObstructionHits[i];
                Collider collider = hit.collider;
                if (collider == null)
                {
                    continue;
                }

                if ((hit.point - suppressTarget).sqrMagnitude <= targetIgnoreDistanceSqr)
                {
                    continue;
                }

                if (!IsSoftFoliageCollider(collider))
                {
                    return false;
                }

                foundSoftObstruction = true;
            }

            return foundSoftObstruction;
        }

        private static bool IsSoftFoliageCollider(Collider collider)
        {
            GameObject gameObject = collider.gameObject;
            if (IsLayerInMask(gameObject.layer, LayerMaskClass.Grass) ||
                IsLayerInMask(gameObject.layer, LayerMaskClass.Foliage))
            {
                return true;
            }

            if (ContainsSoftFoliageToken(gameObject.name) ||
                ContainsSoftFoliageToken(collider.name))
            {
                return true;
            }

            Transform parent = collider.transform?.parent;
            while (parent != null)
            {
                if (ContainsSoftFoliageToken(parent.name))
                {
                    return true;
                }

                parent = parent.parent;
            }

            return false;
        }

        private static bool IsLayerInMask(int layer, LayerMask mask)
        {
            return (mask.value & (1 << layer)) != 0;
        }

        private static bool ContainsSoftFoliageToken(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            return value.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("foliage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("shrub", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("reed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("leaves", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("branch", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryFindSuppressFromPoint(
            Vector3 suppressTarget,
            out CustomNavigationPoint? suppressFrom,
            bool allowSoftFoliageLane = false,
            bool allowLauncherArcLane = false,
            float launcherUnsafeRadius = 0f)
        {
            suppressFrom = null;
            ShootPointClass shootPoint = new ShootPointClass(suppressTarget, 1f);
            float minDistanceSqr = SuppressFromMinDistance * SuppressFromMinDistance;

            CustomNavigationPoint? cover = SelectBestEvaluatedCover(
                botOwner.Position,
                SuppressFromSearchRadius,
                CoverSearchType.distToToCenter,
                point =>
                {
                    if (!IsCoverUsable(point, true))
                    {
                        return false;
                    }

                    if ((point.Position - botOwner.Position).sqrMagnitude < minDistanceSqr)
                    {
                        return false;
                    }

                    Vector3 fireOrigin = point.Position + Vector3.up * 1.2f;
                    bool hasDirectLane = Utils.Utils.CanShootToTarget(shootPoint, fireOrigin, botOwner.LookSensor.Mask, false);
                    bool hasSoftFoliageLane = !hasDirectLane &&
                                              allowSoftFoliageLane &&
                                              IsSoftObstructedSuppressionLane(fireOrigin, suppressTarget, botOwner.LookSensor.Mask);
                    bool hasLauncherArcLane = !hasDirectLane &&
                                              !hasSoftFoliageLane &&
                                              allowLauncherArcLane &&
                                              TryCanFireGrenadeLauncherFromOrigin(
                                                  botOwner,
                                                  fireOrigin,
                                                  suppressTarget,
                                                  launcherUnsafeRadius,
                                                  out _);
                    if (!hasDirectLane &&
                        !hasSoftFoliageLane &&
                        !hasLauncherArcLane)
                    {
                        return false;
                    }

                    point.CanIShootToEnemy = true;
                    return !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget);
                });

            if (!IsCoverUsable(cover, true))
            {
                return false;
            }

            suppressFrom = cover;
            return true;
        }

        private static Vector3 GetCurrentSuppressionFireOrigin(BotOwner owner)
        {
            if (owner == null)
            {
                return Vector3.zero;
            }

            return owner.WeaponRoot != null
                ? owner.WeaponRoot.position
                : owner.Position + Vector3.up * 1.2f;
        }

        private static Vector3 GetStandingSuppressionFireOrigin(BotOwner owner)
        {
            return owner != null
                ? owner.Position + Vector3.up * StandingCoverShotProbeHeight
                : Vector3.zero;
        }

        public bool TryGetSuppressTarget(EnemyInfo goalEnemy, out Vector3 suppressTarget)
        {
            suppressTarget = Vector3.zero;
            if (goalEnemy == null)
            {
                return false;
            }

            ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            if (shootPoint != null && IsFinite(shootPoint.Point))
            {
                suppressTarget = shootPoint.Point;
                return true;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor) || enemyAnchor.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            suppressTarget = enemyAnchor + Vector3.up * 0.8f;
            return true;
        }

        private bool CanAcquireCommittedCover()
        {
            if (Time.time < nextCoverAcquireTime)
            {
                return false;
            }

            nextCoverAcquireTime = Time.time + CoverSearchCooldownSeconds;
            return true;
        }

        public bool TryCommitSelectedCombatCover(
            EnemyInfo goalEnemy,
            CustomNavigationPoint? cover,
            string reason,
            bool recoveryManeuver = false)
        {
            if (!TryValidateSelectedCombatCover(
                    goalEnemy,
                    cover,
                    reason,
                    recoveryManeuver,
                    out BotLogicDecision moveAction))
            {
                return false;
            }

            CommitValidatedCombatCover(goalEnemy, cover!, reason, recoveryManeuver, moveAction);
            return true;
        }

        private bool TryValidateSelectedCombatCover(
            EnemyInfo goalEnemy,
            CustomNavigationPoint? cover,
            string reason,
            bool recoveryManeuver,
            out BotLogicDecision moveAction)
        {
            moveAction = default;
            if (!IsCoverUsable(cover) ||
                (recoveryManeuver && IsBlockedRecoveryCover(cover)) ||
                IsBlockedPushCover(cover, goalEnemy, reason) ||
                IsUnsafeFireSupportPath(goalEnemy, cover!, reason))
            {
                return false;
            }

            if (recoveryManeuver && IsUnsafeRecoveryCoverPath(goalEnemy, cover!))
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "recovery",
                    "rejectCover",
                    "unsafeThreatPath",
                    target: cover!.Position,
                    coverId: cover.Id);
                return false;
            }

            if (HasCombatCoverDestinationClaimConflict(cover))
            {
                return false;
            }

            moveAction = recoveryManeuver
                ? SelectRecoveryCoverMoveAction(goalEnemy, cover!)
                : SelectCommittedCoverMoveAction(goalEnemy, cover);
            return moveAction != (BotLogicDecision)CustomBotDecisions.attackRetreat ||
                   !IsPointBlankVisibleShootableThreat(goalEnemy);
        }

        private void CommitValidatedCombatCover(
            EnemyInfo goalEnemy,
            CustomNavigationPoint cover,
            string reason,
            bool recoveryManeuver,
            BotLogicDecision moveAction)
        {
            if (recoveryManeuver)
            {
                reason = CreateRecoveryManeuverReason(reason);
            }

            if (moveAction == BotLogicDecision.runToCover)
            {
                reason += ".run";
                SetRunToCoverTactic(cover, reason);
            }
            else if (moveAction == BotLogicDecision.attackMovingWithSuppress)
            {
                reason += ".suppress";
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            }
            else if (moveAction == (BotLogicDecision)CustomBotDecisions.attackRetreat)
            {
                reason += ".retreat";
                SetCoverTactic(BotsGroup.BotCurrentTactic.Protect);
                if (!goalEnemy.IsVisible)
                {
                    botOwner.Steering.LookToPoint(GetEnemyAnchor(goalEnemy) + Vector3.up * 0.8f);
                }
            }
            else if (moveAction == BotLogicDecision.attackMoving)
            {
                reason += ".walk";
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            }

            CommitCover(cover, moveAction, reason);
            AssignCover(cover);
        }

        private static string GetRecoveryValidationReason(string reason)
        {
            const string RecoveryPrefix = "recovery.";
            return reason.StartsWith(RecoveryPrefix, StringComparison.Ordinal)
                ? reason.Substring(RecoveryPrefix.Length)
                : reason;
        }

        private static string GetCoverCommitBaseReason(string reason)
        {
            string[] movementSuffixes = { ".run", ".suppress", ".retreat", ".walk" };
            for (int i = 0; i < movementSuffixes.Length; i++)
            {
                string suffix = movementSuffixes[i];
                if (reason.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return reason.Substring(0, reason.Length - suffix.Length);
                }
            }

            return reason;
        }

        /// <summary>
        /// Damage recovery commits to one of two readable responses: a short sprint to nearby cover,
        /// or a threat-facing suppressive move to farther cover. The latter uses the exact committed
        /// cover destination, so it cannot turn the bot's back while a generic attack-moving node
        /// searches for a different point.
        /// </summary>
        private BotLogicDecision SelectRecoveryCoverMoveAction(
            EnemyInfo goalEnemy,
            CustomNavigationPoint cover)
        {
            const float NearbyRecoverySprintDistance = 12f;
            float navDistance = GetCoverNavDistance(cover);
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            bool pathExposed = IsFinite(enemyAnchor) &&
                               Covers.IsPathExposedToEnemy(
                                   botOwner.Position,
                                   cover.Position,
                                   enemyAnchor,
                                   botOwner.LookSensor.Mask,
                                   sampleCount: 5);
            if (CanSprintForCombatMovement() &&
                navDistance <= NearbyRecoverySprintDistance &&
                !pathExposed)
            {
                return BotLogicDecision.runToCover;
            }

            return (BotLogicDecision)CustomBotDecisions.attackRetreat;
        }

        private bool IsUnsafeRecoveryCoverPath(EnemyInfo goalEnemy, CustomNavigationPoint cover)
        {
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            float closeQuarterDistance = CombatDistanceConfiguration.Instance.GetCloseQuarterDistance();
            if (Covers.IsPathTooCloseToEnemy(
                    botOwner.Position,
                    cover.Position,
                    enemyAnchor,
                    closeQuarterDistance))
            {
                return true;
            }

            float currentEnemyDistance = Vector3.Distance(botOwner.Position, enemyAnchor);
            float coverEnemyDistance = Vector3.Distance(cover.Position, enemyAnchor);
            bool movesTowardEnemy = coverEnemyDistance + 2f < currentEnemyDistance;
            return movesTowardEnemy &&
                   Covers.IsPathExposedToEnemy(
                       botOwner.Position,
                       cover.Position,
                       enemyAnchor,
                       botOwner.LookSensor.Mask,
                       sampleCount: 5);
        }

        private static string CreateRecoveryManeuverReason(string? reason)
        {
            string baseReason = string.IsNullOrWhiteSpace(reason) ? "cover" : reason!;
            return IsRecoveryManeuverReason(baseReason)
                ? baseReason
                : $"recovery.{baseReason}";
        }

        private void SetRunToCoverTactic(CustomNavigationPoint? cover, string reason)
        {
            if (IsProtectCoverReason(reason))
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Protect);
                return;
            }

            if (IsAttackCoverReason(reason, cover))
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
                return;
            }

            SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
        }

        private static bool IsProtectCoverReason(string? reason)
        {
            return reason != null &&
                   (reason.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("protect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    reason.IndexOf("regroup", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsAttackCoverReason(string? reason, CustomNavigationPoint? cover)
        {
            return cover?.CanIShootToEnemy == true ||
                   (reason != null &&
                    (reason.IndexOf("shoot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     reason.IndexOf("support", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     reason.IndexOf("push", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     reason.IndexOf("sniper", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private bool IsUnsafeFireSupportPath(EnemyInfo goalEnemy, CustomNavigationPoint cover, string reason)
        {
            if (goalEnemy == null ||
                string.IsNullOrEmpty(reason) ||
                !reason.StartsWith("sniper.FireSupport", StringComparison.Ordinal))
            {
                return false;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            if (Covers.IsPathTooCloseToEnemy(
                    botOwner.Position,
                    cover.Position,
                    enemyAnchor,
                    FireSupportPathEnemyMinDistance))
            {
                return true;
            }

            return Covers.IsPathExposedToEnemy(
                botOwner.Position,
                cover.Position,
                enemyAnchor,
                botOwner.LookSensor.Mask,
                sampleCount: 6);
        }

        /// <summary>
        /// Emergency fallback for the frame where vanilla drops GoalEnemy while concrete incoming-fire
        /// awareness remains. Without a goal enemy, tactic stacks cannot pick normal recovery logic, so
        /// commit a hidden lateral/backward cover and keep pressure toward the recent threat while moving.
        /// </summary>
        public bool TryGetNoEnemyThreatCoverDecision(
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!HasRecoveryPressure(2f))
            {
                return false;
            }

            if (HasCommittedPosition(out decision))
            {
                return true;
            }

            if (HasCommittedCover())
            {
                if (IsNoEnemyThreatCoverReason(committedCoverMoveReason) &&
                    !IsBotInCommittedCover())
                {
                    AssignCommittedCover();
                    decision = CreateCommittedCoverMoveDecision();
                    return true;
                }

                ClearCommittedCover("noEnemyThreatReplan");
            }

            bool hasThreatPoint = FollowerAwareness.TryGetRecentThreatLookPoint(
                botOwner,
                out Vector3 threatPoint);
            Vector3 threatDirection = threatPoint - botOwner.Position;
            threatDirection.y = 0f;
            hasThreatPoint &= threatDirection.sqrMagnitude > 0.01f;
            if (hasThreatPoint)
            {
                threatDirection.Normalize();
            }

            CollectNoEnemyRecoverySecondaryThreatPoints(hasThreatPoint ? threatPoint : Vector3.zero);

            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Ambush,
                CoverShootType.hide,
                CoverSearchIntent.RunToCover);

            bool rejectedSecondaryThreatCover = false;
            bool IsEligible(CustomNavigationPoint candidate)
            {
                if (!IsCoverUsable(candidate, ignoreSpotted: true))
                {
                    return false;
                }

                if (!IsNoEnemyRecoveryCoverSafeFromSecondaryThreats(candidate.Position))
                {
                    rejectedSecondaryThreatCover = true;
                    return false;
                }

                return true;
            }

            Vector3 bossPosition = GetBossPosition();
            bool preferBossLevel = IsFinite(bossPosition) &&
                                   FollowerCombatRegroupObjective.IsSameBossLevel(botOwner.Position, bossPosition);

            bool IsEligibleOnBossLevel(CustomNavigationPoint candidate)
            {
                return IsEligible(candidate) &&
                       (!preferBossLevel ||
                        FollowerCombatRegroupObjective.IsSameBossLevel(candidate.Position, bossPosition));
            }

            float ScoreThreatCover(CustomNavigationPoint candidate)
            {
                float navDistance = GetEvaluatedCoverNavDistance(candidate);
                if (!hasThreatPoint)
                {
                    return navDistance;
                }

                Vector3 coverDirection = candidate.Position - botOwner.Position;
                coverDirection.y = 0f;
                float towardThreatPenalty = coverDirection.sqrMagnitude > 0.01f
                    ? Mathf.Max(0f, Vector3.Dot(coverDirection.normalized, threatDirection)) * 30f
                    : 30f;
                float exposedPathPenalty = Covers.IsPathExposedToEnemy(
                    botOwner.Position,
                    candidate.Position,
                    threatPoint,
                    botOwner.LookSensor.Mask,
                    sampleCount: 5)
                    ? 20f
                    : 0f;
                return navDistance + towardThreatPenalty + exposedPathPenalty;
            }

            bool usedWeakCover = false;
            bool usedCrossFloorFallback = false;
            CustomNavigationPoint? cover = null;
            if (preferBossLevel)
            {
                cover = hasThreatPoint
                    ? SelectBestThreatCover(
                        botOwner.Position,
                        50f,
                        searchType,
                        threatPoint,
                        null,
                        IsEligibleOnBossLevel,
                        ScoreThreatCover,
                        allowWeakFallback: true,
                        out usedWeakCover,
                        exhaustCycleOnMiss: false)
                    : SelectBestEvaluatedCover(
                        botOwner.Position,
                        50f,
                        searchType,
                        IsEligibleOnBossLevel,
                        ScoreThreatCover,
                        exhaustCycleOnMiss: false);
            }

            if (cover == null)
            {
                usedCrossFloorFallback = preferBossLevel;
                cover = hasThreatPoint
                    ? SelectBestThreatCover(
                        botOwner.Position,
                        50f,
                        searchType,
                        threatPoint,
                        null,
                        IsEligible,
                        ScoreThreatCover,
                        allowWeakFallback: true,
                        out usedWeakCover)
                    : SelectBestEvaluatedCover(
                        botOwner.Position,
                        50f,
                        searchType,
                        IsEligible,
                        ScoreThreatCover);
            }

            if (!IsCoverUsable(cover, ignoreSpotted: true))
            {
                return false;
            }

            if (HasCombatCoverDestinationClaimConflict(cover))
            {
                return false;
            }

            BotLogicDecision moveAction = hasThreatPoint
                ? BotLogicDecision.attackMovingWithSuppress
                : BotLogicDecision.runToCover;
            string reason = hasThreatPoint
                ? usedWeakCover
                    ? "recovery.noEnemyThreatCover.weak"
                    : "recovery.noEnemyThreatCover"
                : "recovery.noEnemyHitCover";
            if (usedCrossFloorFallback)
            {
                reason += ".crossFloorFallback";
            }
            if (rejectedSecondaryThreatCover)
            {
                reason += ".secondaryThreatVeto";
            }

            if (hasThreatPoint)
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            }

            CommitCover(cover, moveAction, reason);
            AssignCover(cover);
            decision = CreateCommittedCoverMoveDecision();
            return true;
        }

        /// <summary>
        /// Collects a bounded set of recent living group-memory contacts for recovery-cover safety.
        /// These frozen positions can reject an unsafe destination, but never promote, aim at, or fire on an enemy.
        /// </summary>
        private void CollectNoEnemyRecoverySecondaryThreatPoints(Vector3 primaryThreatPoint)
        {
            noEnemyRecoverySecondaryThreatPoints.Clear();
            if (botOwner.EnemiesController?.EnemyInfos == null)
            {
                return;
            }

            Vector3 botPosition = botOwner.Position;
            foreach (EnemyInfo enemyInfo in botOwner.EnemiesController.EnemyInfos.Values)
            {
                if (!IsTrackedEnemyAlive(enemyInfo) ||
                    !IsRecentTimestamp(enemyInfo.TimeLastSeen, NoEnemyRecoverySecondaryThreatRecentSeconds))
                {
                    continue;
                }

                Vector3 memoryPoint = enemyInfo.EnemyLastPosition;
                if (!IsFinite(memoryPoint) ||
                    memoryPoint.sqrMagnitude <= 0.01f ||
                    !FollowerCombatRegroupObjective.IsSameBossLevel(botPosition, memoryPoint) ||
                    (IsFinite(primaryThreatPoint) &&
                     primaryThreatPoint.sqrMagnitude > 0.01f &&
                     (memoryPoint - primaryThreatPoint).sqrMagnitude <= 2f * 2f))
                {
                    continue;
                }

                float distanceSqr = (memoryPoint - botPosition).sqrMagnitude;
                int insertAt = 0;
                while (insertAt < noEnemyRecoverySecondaryThreatPoints.Count &&
                       (noEnemyRecoverySecondaryThreatPoints[insertAt] - botPosition).sqrMagnitude <= distanceSqr)
                {
                    insertAt++;
                }

                if (noEnemyRecoverySecondaryThreatPoints.Count >= NoEnemyRecoverySecondaryThreatMaxCount)
                {
                    if (insertAt >= NoEnemyRecoverySecondaryThreatMaxCount)
                    {
                        continue;
                    }

                    noEnemyRecoverySecondaryThreatPoints.RemoveAt(NoEnemyRecoverySecondaryThreatMaxCount - 1);
                }

                noEnemyRecoverySecondaryThreatPoints.Insert(insertAt, memoryPoint);
            }
        }

        private bool IsNoEnemyRecoveryCoverSafeFromSecondaryThreats(Vector3 coverPosition)
        {
            Vector3 botPosition = botOwner.Position;
            foreach (Vector3 threatPoint in noEnemyRecoverySecondaryThreatPoints)
            {
                float currentDistance = DistanceXZ(botPosition, threatPoint);
                float coverDistance = DistanceXZ(coverPosition, threatPoint);
                if (coverDistance < NoEnemyRecoverySecondaryThreatMinDistance &&
                    coverDistance + NoEnemyRecoverySecondaryThreatMinApproach < currentDistance)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsNoEnemyThreatCoverReason(string? reason)
        {
            return IsReasonOrSubreason(reason, "noEnemyThreatCover") ||
                   IsReasonOrSubreason(reason, "recovery.noEnemyThreatCover") ||
                   (!string.IsNullOrEmpty(reason) &&
                    (reason.StartsWith("committedCoverHold.noEnemyThreatCover", StringComparison.Ordinal) ||
                     reason.StartsWith("committedPositionHold.noEnemyThreatCover", StringComparison.Ordinal) ||
                     reason.StartsWith("committedCoverHold.recovery.noEnemyThreatCover", StringComparison.Ordinal) ||
                     reason.StartsWith("committedPositionHold.recovery.noEnemyThreatCover", StringComparison.Ordinal)));
        }

        private void CommitCover(CustomNavigationPoint? cover, BotLogicDecision moveAction, string? reason)
        {
            if (cover == null)
            {
                return;
            }

            committedCoverPoint = cover;
            committedCoverMoveAction = moveAction;
            committedCoverMoveReason = reason;
            committedCoverSetAt = Time.time;
            committedCoverUntil = Time.time + CoverCommitLockSeconds;
            coverCommitIntents[botOwner.Id] = new CoverCommitIntent(cover.Id, IsCommittedShootingCoverReason(reason));
            ReserveCombatCoverDestination(cover.Position);
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "cover",
                "commit",
                reason,
                new AICoreActionResultStruct<BotLogicDecision, GClass26>(moveAction, reason ?? "cover"),
                cover.Position,
                cover.Id,
                null,
                committedCoverUntil);
        }

        private bool IsCommittedCoverStillUsable(CustomNavigationPoint? cover)
        {
            if (cover == null)
            {
                return false;
            }

            if (IsBotInCommittedCover())
            {
                return true;
            }

            return cover.IsFreeById(botOwner.Id);
        }

        private bool HasCombatCoverDestinationClaimConflict(CustomNavigationPoint? cover)
        {
            if (cover == null || IsBotNearCombatCoverDestination(cover.Position))
            {
                return false;
            }

            CombatEvents? combatEvents = GetBossCombatEvents();
            return combatEvents?.HasDestinationClaimConflict(botOwner, cover.Position, CombatCoverDestinationSpacing) == true;
        }

        private bool IsBotNearCombatCoverDestination(Vector3 position)
        {
            return (botOwner.Position - position).sqrMagnitude <= 1f * 1f;
        }

        private void ReserveCombatCoverDestination(Vector3 position)
        {
            CombatEvents? combatEvents = GetBossCombatEvents();
            if (combatEvents?.UpsertDestinationClaim(botOwner, position, CombatCoverDestinationClaimTtlSeconds) != true)
            {
                return;
            }

            hasCommittedCoverDestinationClaim = true;
            committedCoverClaimPosition = position;
        }

        private void ReleaseCombatCoverDestinationClaim()
        {
            if (!hasCommittedCoverDestinationClaim)
            {
                return;
            }

            GetBossCombatEvents()?.TryReleaseDestinationClaim(
                botOwner,
                committedCoverClaimPosition,
                CombatCoverClaimReleaseTolerance);
            hasCommittedCoverDestinationClaim = false;
            committedCoverClaimPosition = Vector3.zero;
        }

        private CombatEvents? GetBossCombatEvents()
        {
            return (botOwner?.BotFollower?.BossToFollow as pitAIBossPlayer)?.CombatEvents;
        }

        private static bool IsCoverAffinedDecision(BotLogicDecision decision)
        {
            return decision == BotLogicDecision.runToCover ||
                   decision == BotLogicDecision.attackMoving ||
                   decision == BotLogicDecision.attackMovingWithSuppress ||
                   decision == (BotLogicDecision)CustomBotDecisions.attackRetreat ||
                   decision == BotLogicDecision.shootFromCover;
        }

        public void RefreshShootCover()
        {
            if (nextShootCoverCheckTime >= Time.time)
            {
                return;
            }

            Vector3 bossPosition = GetBossPosition();
            CustomNavigationPoint? candidate = FindFollowerShootCover();
            bool pointChangedMeaningfully = IsPointMeaningfullyDifferent(PointToShoot, candidate);
            if (ShouldUpdatePointToShoot(PointToShoot, candidate))
            {
                PointToShoot = candidate;
            }

            if (!IsCoverUsable(candidate))
            {
                HaveCoverToShoot = UpdateDebouncedHaveCoverToShoot(false);
                ScheduleShootCoverRefresh(stable: false);
                return;
            }

            if (candidate == null)
            {
                HaveCoverToShoot = UpdateDebouncedHaveCoverToShoot(false);
                ScheduleShootCoverRefresh(stable: false);
                return;
            }

            bool requireShootLane = ProtectCareKill();
            bool candidateCanShoot = candidate.CanIShootToEnemy;
            bool candidateShootLaneStable = !requireShootLane || IsShootLaneUpgradeStable(candidateCanShoot);
            bool rawHaveCoverToShoot = !requireShootLane || candidateShootLaneStable;
            HaveCoverToShoot = UpdateDebouncedHaveCoverToShoot(rawHaveCoverToShoot);
            if (!HaveCoverToShoot)
            {
                ScheduleShootCoverRefresh(stable: false);
                return;
            }

            CustomNavigationPoint? current = botOwner.Memory.CurCustomCoverPoint;
            if (!ShouldCommitRefreshedShootCover(current, candidate, bossPosition, requireShootLane, candidateShootLaneStable))
            {
                bool stableSignal = !IsHaveCoverDebouncePending() && !pointChangedMeaningfully;
                ScheduleShootCoverRefresh(stableSignal);
                return;
            }

            if (current != null && current.Id == candidate.Id)
            {
                bool stableSignal = !IsHaveCoverDebouncePending() && !pointChangedMeaningfully;
                ScheduleShootCoverRefresh(stableSignal);
                return;
            }

            botOwner.Memory.BotCurrentCoverInfo.Spotted();
            botOwner.Memory.BotCurrentCoverInfo.SetCover(candidate, true);
            ScheduleShootCoverRefresh(stable: false);
        }

        private bool ShouldCommitRefreshedShootCover(
            CustomNavigationPoint? current,
            CustomNavigationPoint candidate,
            Vector3 bossPosition,
            bool requireShootLane,
            bool candidateShootLaneStable)
        {
            // Rule 1: no current cover or current cover is invalid.
            if (IsCurrentCoverInvalid(current, bossPosition))
            {
                return true;
            }

            if (current == null)
            {
                return true;
            }

            bool currentCanShoot = current.CanIShootToEnemy;
            bool candidateCanShoot = candidate.CanIShootToEnemy;

            // Rule 2: current cannot shoot and candidate can.
            if (!currentCanShoot && candidateCanShoot && candidateShootLaneStable)
            {
                return true;
            }

            bool currentUsable = IsCoverUsable(current);
            bool candidateUsable = IsCoverUsable(candidate);

            // Rule 3: current violates boss-distance/usability and candidate does not.
            if (!currentUsable && candidateUsable)
            {
                return true;
            }

            // Rule 4: meaningful superiority only; avoid reshuffle from already-valid shoot-capable cover.
            if (currentUsable && currentCanShoot)
            {
                return false;
            }

            if (requireShootLane && !candidateShootLaneStable)
            {
                return false;
            }

            return HasMeaningfulNavImprovement(current, candidate);
        }

        private bool IsCurrentCoverInvalid(CustomNavigationPoint? cover, Vector3 bossPosition)
        {
            return cover == null ||
                   !cover.IsFreeById(botOwner.Id) ||
                   cover.IsSpotted;
        }

        /// <summary>
        /// Basic validity gate for a candidate cover point.
        /// </summary>
        public bool IsCoverUsable(CustomNavigationPoint? cover, bool ignoreSpotted = false)
        {
            return cover != null &&
                   cover.IsFreeById(botOwner.Id) &&
                   (ignoreSpotted || !cover.IsSpotted);
        }

        /// <summary>
        /// Returns the mod-owned maximum combat cover search distance.
        /// </summary>
        public float GetCombatCoverMaxDistanceSqr()
        {
            return CombatDistanceConfiguration.Instance.GetCombatCoverMaxDistanceSqr();
        }

        private bool HasMeaningfulNavImprovement(CustomNavigationPoint current, CustomNavigationPoint candidate)
        {
            float currentNavDistance = GetCoverNavDistance(current);
            float candidateNavDistance = GetCoverNavDistance(candidate);

            if (!IsFinite(currentNavDistance) || !IsFinite(candidateNavDistance))
            {
                return false;
            }

            return candidateNavDistance <= currentNavDistance * ShootCoverSuperiorNavImprovementFactor;
        }

        private bool ShouldUpdatePointToShoot(CustomNavigationPoint? currentPoint, CustomNavigationPoint? candidate)
        {
            if (candidate == null)
            {
                return currentPoint == null;
            }

            if (currentPoint == null)
            {
                return true;
            }

            if (currentPoint.Id == candidate.Id)
            {
                return false;
            }

            float minDeltaSqr = PointToShootUpdateMinDistance * PointToShootUpdateMinDistance;
            return (currentPoint.Position - candidate.Position).sqrMagnitude >= minDeltaSqr;
        }

        private bool IsPointMeaningfullyDifferent(CustomNavigationPoint? currentPoint, CustomNavigationPoint? candidate)
        {
            if (currentPoint == null || candidate == null)
            {
                return currentPoint != candidate;
            }

            if (currentPoint.Id == candidate.Id)
            {
                return false;
            }

            float minDeltaSqr = PointToShootUpdateMinDistance * PointToShootUpdateMinDistance;
            return (currentPoint.Position - candidate.Position).sqrMagnitude >= minDeltaSqr;
        }

        private bool UpdateDebouncedHaveCoverToShoot(bool rawValue)
        {
            if (rawValue == HaveCoverToShoot)
            {
                pendingHaveCoverToShoot = rawValue;
                pendingHaveCoverToShootSince = 0f;
                return HaveCoverToShoot;
            }

            if (pendingHaveCoverToShootSince <= 0f || pendingHaveCoverToShoot != rawValue)
            {
                pendingHaveCoverToShoot = rawValue;
                pendingHaveCoverToShootSince = Time.time;
                return HaveCoverToShoot;
            }

            if (Time.time - pendingHaveCoverToShootSince < HaveCoverToShootDebounceSeconds)
            {
                return HaveCoverToShoot;
            }

            HaveCoverToShoot = rawValue;
            pendingHaveCoverToShootSince = 0f;
            return HaveCoverToShoot;
        }

        private bool IsHaveCoverDebouncePending()
        {
            return pendingHaveCoverToShootSince > 0f && pendingHaveCoverToShoot != HaveCoverToShoot;
        }

        private bool IsShootLaneUpgradeStable(bool candidateCanShoot)
        {
            if (!candidateCanShoot)
            {
                shootLaneUpgradeSince = 0f;
                return false;
            }

            if (shootLaneUpgradeSince <= 0f)
            {
                shootLaneUpgradeSince = Time.time;
            }

            return Time.time - shootLaneUpgradeSince >= ShootLaneUpgradeHysteresisSeconds;
        }

        private void ScheduleShootCoverRefresh(bool stable)
        {
            nextShootCoverCheckTime = Time.time + (stable ? StableShootCoverRefreshInterval : UnstableShootCoverRefreshInterval);
        }

        private IReadOnlyList<CustomNavigationPoint> GetCoverEvaluationCandidates(CoverSearchType searchType)
        {
            BeginCoverEvaluationCycle();
            if (coverEvaluationAttempted)
            {
                return coverEvaluationCandidates != null
                    ? coverEvaluationCandidates
                    : Array.Empty<CustomNavigationPoint>();
            }

            coverEvaluationAttempted = true;
            float searchRadius = CombatDistanceConfiguration.Instance.GetCombatCoverMaxDistance();
            coverEvaluationCandidates = Covers.GetCoverPoints(
                botOwner,
                botOwner.Position,
                searchRadius,
                iritations: CombatCoverEvaluationMaxCandidates,
                searchTypeOverride: searchType);
            return coverEvaluationCandidates;
        }

        /// <summary>
        /// Selects from the one candidate pool acquired for this frame. This method deliberately
        /// caches an empty pool as a completed evaluation, matching the old combat layer's
        /// covertried guard while still allowing later branches to apply another policy to the
        /// already-owned list without another EFT cover enumeration.
        /// </summary>
        private CustomNavigationPoint? SelectBestEvaluatedCover(
            Vector3 centerPosition,
            float searchRadius,
            CoverSearchType searchType,
            Func<CustomNavigationPoint, bool> eligibility,
            Func<CustomNavigationPoint, float>? score = null,
            bool exhaustCycleOnMiss = true)
        {
            BeginCoverEvaluationCycle();
            if (coverEvaluationExhausted)
            {
                return null;
            }

            IReadOnlyList<CustomNavigationPoint> candidates = GetCoverEvaluationCandidates(searchType);
            float searchRadiusSqr = searchRadius * searchRadius;
            float bestScore = float.MaxValue;
            CustomNavigationPoint? best = null;

            for (int i = 0; i < candidates.Count; i++)
            {
                CustomNavigationPoint candidate = candidates[i];
                if (candidate == null ||
                    (candidate.CoverLevel != CoverLevel.Sit && candidate.CoverLevel != CoverLevel.Stay) ||
                    (candidate.Position - centerPosition).sqrMagnitude > searchRadiusSqr ||
                    IsCoverTooCloseToTeam(candidate) ||
                    HasCombatCoverDestinationClaimConflict(candidate) ||
                    !eligibility(candidate))
                {
                    continue;
                }

                if (IsUrbanDetourCoverCandidate(candidate))
                {
                    continue;
                }

                float candidateScore = score != null
                    ? score(candidate)
                    : ScoreEvaluatedCover(candidate, centerPosition);
                if (!IsFinite(candidateScore) || candidateScore >= bestScore)
                {
                    continue;
                }

                best = candidate;
                bestScore = candidateScore;
            }

            if (best == null && exhaustCycleOnMiss)
            {
                coverEvaluationExhausted = true;
            }

            return best;
        }

        /// <summary>
        /// Applies a hard-cover-first policy to the already-cached EFT candidate pool. A weak
        /// fallback is optional and never performs another candidate enumeration.
        /// </summary>
        private CustomNavigationPoint? SelectBestThreatCover(
            Vector3 centerPosition,
            float searchRadius,
            CoverSearchType searchType,
            Vector3 threatPosition,
            string? enemyProfileId,
            Func<CustomNavigationPoint, bool> eligibility,
            Func<CustomNavigationPoint, float>? score,
            bool allowWeakFallback,
            out bool usedWeakFallback,
            bool exhaustCycleOnMiss = true)
        {
            usedWeakFallback = false;
            if (!IsFinite(threatPosition))
            {
                return allowWeakFallback
                    ? SelectBestEvaluatedCover(
                        centerPosition,
                        searchRadius,
                        searchType,
                        eligibility,
                        score,
                        exhaustCycleOnMiss)
                    : null;
            }

            CustomNavigationPoint? cover = SelectBestEvaluatedCover(
                centerPosition,
                searchRadius,
                searchType,
                point => eligibility(point) && IsHardThreatCover(point, threatPosition, enemyProfileId),
                score,
                exhaustCycleOnMiss: false);
            if (cover != null || !allowWeakFallback)
            {
                return cover;
            }

            cover = SelectBestEvaluatedCover(
                centerPosition,
                searchRadius,
                searchType,
                eligibility,
                score,
                exhaustCycleOnMiss);
            usedWeakFallback = cover != null;
            return cover;
        }

        private bool IsHardThreatCover(
            CustomNavigationPoint cover,
            Vector3 threatPosition,
            string? enemyProfileId)
        {
            if (cover == null || !IsFinite(cover.Position) || !IsFinite(threatPosition))
            {
                return false;
            }

            if (threatCoverProbeCache.TryGetValue(cover.Id, out ThreatCoverProbeCacheEntry cached) &&
                Time.time - cached.EvaluatedAt <= ThreatCoverProbeCacheSeconds &&
                string.Equals(cached.EnemyProfileId, enemyProfileId, StringComparison.Ordinal) &&
                (cached.ThreatPosition - threatPosition).sqrMagnitude <= ThreatCoverProbeThreatMoveToleranceSqr &&
                (cached.CoverPosition - cover.Position).sqrMagnitude <= ThreatCoverProbePointMoveToleranceSqr)
            {
                return cached.IsHardCover;
            }

            bool isHardCover;
            if (cover.CoverType == CoverType.Foliage)
            {
                isHardCover = false;
            }
            else
            {
                int frame = Time.frameCount;
                if (threatCoverPhysicsProbeFrame != frame)
                {
                    threatCoverPhysicsProbeFrame = frame;
                    threatCoverPhysicsProbeCount = 0;
                }

                if (threatCoverPhysicsProbeCount >= ThreatCoverPhysicsProbeMaxPerFrame)
                {
                    return false;
                }

                threatCoverPhysicsProbeCount++;
                isHardCover = Covers.IsHardCoverFromThreat(cover, threatPosition);
            }

            if (threatCoverProbeCache.Count >= ThreatCoverProbeCacheMaxEntries &&
                !threatCoverProbeCache.ContainsKey(cover.Id))
            {
                threatCoverProbeCache.Clear();
            }

            threatCoverProbeCache[cover.Id] = new ThreatCoverProbeCacheEntry(
                enemyProfileId,
                threatPosition,
                cover.Position,
                Time.time,
                isHardCover);
            return isHardCover;
        }

        private float ScoreEvaluatedCover(CustomNavigationPoint cover, Vector3 centerPosition)
        {
            float navDistance = GetEvaluatedCoverNavDistance(cover);
            float centerDistance = Vector3.Distance(cover.Position, centerPosition);
            return navDistance + centerDistance * CombatCoverCenterDistanceWeight;
        }

        private float GetEvaluatedCoverNavDistance(CustomNavigationPoint cover)
        {
            if (coverEvaluationNavDistance.TryGetValue(cover.Id, out float cachedDistance))
            {
                return cachedDistance;
            }

            float navDistance = Utils.Utils.GetNavDistance(botOwner.Position, cover.Position);
            if (!IsFinite(navDistance))
            {
                navDistance = float.PositiveInfinity;
            }

            coverEvaluationNavDistance[cover.Id] = navDistance;
            return navDistance;
        }

        private bool IsUrbanDetourCoverCandidate(CustomNavigationPoint cover)
        {
            if (!CombatDistanceConfiguration.Instance.IsUrbanDetourMode)
            {
                return false;
            }

            float directDistance = Vector3.Distance(botOwner.Position, cover.Position);
            float navDistance = GetEvaluatedCoverNavDistance(cover);
            return CombatDistanceConfiguration.Instance.IsUrbanDetourRegroup(directDistance, navDistance);
        }

        private bool IsCoverTooCloseToTeam(CustomNavigationPoint cover)
        {
            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            float minDistanceSqr = CombatCoverTeamSpacing * CombatCoverTeamSpacing;
            if (boss.realPlayer?.Transform != null &&
                (cover.Position - boss.realPlayer.Transform.position).sqrMagnitude < minDistanceSqr)
            {
                return true;
            }

            for (int i = 0; i < boss.Followers.Count; i++)
            {
                BotOwner follower = boss.Followers[i];
                if (follower == null || follower == botOwner || follower.GetPlayer?.Transform == null)
                {
                    continue;
                }

                if ((cover.Position - follower.GetPlayer.Transform.position).sqrMagnitude < minDistanceSqr)
                {
                    return true;
                }
            }

            return false;
        }

        private float GetCoverNavDistance(CustomNavigationPoint cover)
        {
            float navDistance = Utils.Utils.GetNavDistance(botOwner.Position, cover.Position);
            if (IsFinite(navDistance))
            {
                return navDistance;
            }

            return Vector3.Distance(botOwner.Position, cover.Position);
        }

        private CustomNavigationPoint? FindFollowerShootCover()
        {
            Vector3 bossPosition = GetBossPosition();
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return null;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            Vector3 targetDirection = enemyAnchor - bossPosition;
            targetDirection.y = 0f;
            if (!IsFinite(enemyAnchor) || targetDirection.sqrMagnitude <= 0.01f)
            {
                return null;
            }

            targetDirection.Normalize();
            ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            LayerMask mask = botOwner.LookSensor.Mask;
            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Attack,
                shootPoint != null ? CoverShootType.shoot : CoverShootType.hide,
                CoverSearchIntent.Attack);

            return SelectBestEvaluatedCover(
                bossPosition,
                25f,
                searchType,
                point =>
                {
                    if (!IsCoverUsable(point))
                    {
                        return false;
                    }

                    Vector3 pointDirection = point.Position - bossPosition;
                    pointDirection.y = 0f;
                    return pointDirection.sqrMagnitude > 0.01f &&
                           Vector3.Dot(pointDirection.normalized, targetDirection) >= 0.1f;
                },
                point =>
                {
                    bool canShoot = shootPoint != null &&
                                    Utils.Utils.CanShootToTarget(shootPoint, point, mask, false);
                    point.CanIShootToEnemy = canShoot;
                    return ScoreEvaluatedCover(point, bossPosition) + (canShoot ? 0f : 40f);
                },
                exhaustCycleOnMiss: false);
        }

        /// <summary>
        /// Old-plugin equivalent of GetClosestAttackCoverPoint/GetClosestShootCover.
        /// Finds a nearby cover point with a clear shot to the enemy target point.
        /// </summary>
        public CustomNavigationPoint? GetClosestShootCover(
            Vector3 centerPosition,
            float maxDistance = 150f,
            bool inbetween = false,
            float? maxDistanceFromBot = null,
            bool avoidCrossingEnemyFront = false,
            bool avoidBossFireLane = false)
        {
            ShootPointClass shootPointClass = botOwner.CurrentEnemyTargetPosition(true);
            if (shootPointClass == null)
            {
                cachedClosestShootCover = null;
                return null;
            }

            bool cachedCoverCrossesBossLane =
                avoidBossFireLane &&
                cachedClosestShootCover != null &&
                IsBossFireLaneMovementRisk(cachedClosestShootCover.Position, shootPointClass.Point, includePath: true);
            if (nextClosestShootCoverCheckTime > Time.time && !cachedCoverCrossesBossLane)
            {
                return cachedClosestShootCover;
            }

            nextClosestShootCoverCheckTime = Time.time + 1f;

            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Attack,
                CoverShootType.shoot,
                CoverSearchIntent.Attack);
            float weaponShootDistMaxSqr = botOwner.LookSensor.MaxShootDist * botOwner.LookSensor.MaxShootDist;
            float? maxDistanceFromBotSqr = maxDistanceFromBot.HasValue
                ? maxDistanceFromBot.Value * maxDistanceFromBot.Value
                : null;
            Func<CustomNavigationPoint, bool> eligibility = point =>
            {
                if (point == null || point.IsSpotted || !point.IsFreeById(botOwner.Id))
                {
                    return false;
                }

                if (maxDistanceFromBotSqr.HasValue &&
                    (point.Position - botOwner.Position).sqrMagnitude > maxDistanceFromBotSqr.Value)
                {
                    return false;
                }

                if (inbetween && !Covers.IsPointBetween(point.Position, botOwner.Position, centerPosition))
                {
                    return false;
                }

                if ((point.Position - shootPointClass.Point).sqrMagnitude >= weaponShootDistMaxSqr)
                {
                    return false;
                }

                if (avoidCrossingEnemyFront &&
                    ShouldAvoidCoverBecauseCrossesEnemyFront(point.Position, shootPointClass.Point))
                {
                    return false;
                }

                bool canShoot = Utils.Utils.CanShootToTarget(shootPointClass, point, botOwner.LookSensor.Mask, false);
                point.CanIShootToEnemy = canShoot;
                return canShoot;
            };

            cachedClosestShootCover = SelectBestEvaluatedCover(
                centerPosition,
                maxDistance,
                searchType,
                eligibility,
                point => ScoreEvaluatedCover(point, centerPosition) +
                         (avoidBossFireLane &&
                          IsBossFireLaneMovementRisk(point.Position, shootPointClass.Point, includePath: true)
                             ? 1000f
                             : 0f));

            if (cachedClosestShootCover != null)
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            }

            botOwner.Memory.SetCoverPoints(cachedClosestShootCover);
            return cachedClosestShootCover;
        }

        /// <summary>
        /// Old-plugin equivalent of GetApproachablePoint/GetApproachableCover.
        /// Picks a shooting cover around the midpoint between bot and enemy.
        /// </summary>
        public CustomNavigationPoint? GetApproachableCover(bool inbetween = false, bool avoidBossFireLane = false)
        {
            if (nextApproachableCoverCheckTime > Time.time && !avoidBossFireLane)
            {
                return cachedClosestShootCover;
            }

            nextApproachableCoverCheckTime = Time.time + 1f;
            nextClosestShootCoverCheckTime = 0f;

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                cachedClosestShootCover = null;
                return null;
            }

            Vector3 enemyPosition = IsFinite(goalEnemy.EnemyLastPositionReal)
                ? goalEnemy.EnemyLastPositionReal
                : goalEnemy.CurrPosition;

            Vector3 midpoint = (botOwner.Position + enemyPosition) * 0.5f;
            return GetClosestShootCover(
                midpoint,
                120f,
                inbetween,
                avoidCrossingEnemyFront: true,
                avoidBossFireLane: avoidBossFireLane);
        }

        public CustomNavigationPoint? GetWeakEnemyPushCover(bool avoidBossFireLane = false)
        {
            float maxDistance = GetWeakEnemyPushMaxDistance();
            float maxDistanceSqr = maxDistance * maxDistance;
            CustomNavigationPoint? approachCover = GetApproachableCover(maxDistance, avoidBossFireLane: avoidBossFireLane);
            if (approachCover == null)
            {
                return null;
            }

            return (approachCover.Position - botOwner.Position).sqrMagnitude <= maxDistanceSqr
                ? approachCover
                : null;
        }

        private CustomNavigationPoint? GetApproachableCover(float maxDistance, bool inbetween = false, bool avoidBossFireLane = false)
        {
            if (nextApproachableCoverCheckTime > Time.time && !avoidBossFireLane)
            {
                return cachedClosestShootCover != null &&
                       (cachedClosestShootCover.Position - botOwner.Position).sqrMagnitude <= maxDistance * maxDistance
                    ? cachedClosestShootCover
                    : null;
            }

            nextApproachableCoverCheckTime = Time.time + 1f;
            nextClosestShootCoverCheckTime = 0f;

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                cachedClosestShootCover = null;
                return null;
            }

            Vector3 enemyPosition = IsFinite(goalEnemy.EnemyLastPositionReal)
                ? goalEnemy.EnemyLastPositionReal
                : goalEnemy.CurrPosition;

            Vector3 midpoint = (botOwner.Position + enemyPosition) * 0.5f;
            return GetClosestShootCover(
                midpoint,
                maxDistance,
                inbetween,
                maxDistanceFromBot: maxDistance,
                avoidCrossingEnemyFront: true,
                avoidBossFireLane: avoidBossFireLane);
        }

        private bool ShouldAvoidCoverBecauseCrossesEnemyFront(Vector3 coverPosition, Vector3 enemyPosition)
        {
            Vector3 botPosition = botOwner.Position;
            Vector3 toEnemy = enemyPosition - botPosition;
            Vector3 toCover = coverPosition - botPosition;

            toEnemy.y = 0f;
            toCover.y = 0f;

            if (toEnemy.sqrMagnitude < 0.01f || toCover.sqrMagnitude < 0.01f)
            {
                return false;
            }

            // If candidate is not generally toward enemy direction, this check is irrelevant.
            if (Vector3.Dot(toCover.normalized, toEnemy.normalized) <= 0f)
            {
                return false;
            }

            float enemyDist = toEnemy.magnitude;
            float coverDist = toCover.magnitude;

            // At longer ranges, a quick crossing segment is usually acceptable and often safer
            // than over-constraining cover picks.
            if (enemyDist > EnemyFrontCrossGuardMaxDistance)
            {
                return false;
            }

            // Cover not beyond enemy depth usually does not force a frontal cross.
            if (coverDist <= enemyDist + 1.5f)
            {
                return false;
            }

            // If the straight path to candidate runs too close to enemy anchor, treat as frontal cross.
            float enemyDistToPath = DistancePointToSegmentXZ(enemyPosition, botPosition, coverPosition);
            return enemyDistToPath < 7f;
        }

        public bool IsBossFireLaneMovementRisk(Vector3 destination, EnemyInfo goalEnemy, bool includePath)
        {
            return IsBossFireLaneMovementRisk(destination, GetEnemyAnchor(goalEnemy), includePath);
        }

        public bool IsBossFireLaneMovementRisk(Vector3 destination, Vector3 enemyAnchor, bool includePath)
        {
            if (!IsFinite(destination) ||
                !IsFinite(enemyAnchor) ||
                FollowerCombatAnchor.IsCombatIndependent(botOwner))
            {
                return false;
            }

            Vector3 bossPosition = GetRealBossPosition();
            if (!IsFinite(bossPosition))
            {
                return false;
            }

            Vector3 bossToEnemy = enemyAnchor - bossPosition;
            bossToEnemy.y = 0f;
            if (bossToEnemy.sqrMagnitude < 4f)
            {
                return false;
            }

            bool botStartsInLane = IsPointInsideBossFireLane(botOwner.Position, bossPosition, enemyAnchor, BossFireLanePathRadius);
            if (IsPointInsideBossFireLane(destination, bossPosition, enemyAnchor, BossFireLaneCandidateRadius))
            {
                return true;
            }

            return includePath &&
                   !botStartsInLane &&
                   DistanceSegmentToSegmentXZ(botOwner.Position, destination, bossPosition, enemyAnchor) <= BossFireLanePathRadius;
        }

        private static bool IsPointInsideBossFireLane(Vector3 point, Vector3 bossPosition, Vector3 enemyAnchor, float radius)
        {
            Vector3 lane = enemyAnchor - bossPosition;
            lane.y = 0f;
            float laneLength = lane.magnitude;
            if (laneLength < 0.01f)
            {
                return false;
            }

            Vector3 direction = lane / laneLength;
            Vector3 bossToPoint = point - bossPosition;
            bossToPoint.y = 0f;
            float forward = Vector3.Dot(bossToPoint, direction);
            if (forward < -BossFireLaneStartPadding || forward > laneLength + BossFireLaneEndPadding)
            {
                return false;
            }

            Vector3 closest = bossPosition + direction * forward;
            closest.y = point.y;
            return (point - closest).sqrMagnitude <= radius * radius;
        }

        private static float DistancePointToSegmentXZ(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
        {
            Vector2 p = new Vector2(point.x, point.z);
            Vector2 a = new Vector2(segmentStart.x, segmentStart.z);
            Vector2 b = new Vector2(segmentEnd.x, segmentEnd.z);

            Vector2 ab = b - a;
            float abLenSqr = ab.sqrMagnitude;
            if (abLenSqr <= 0.0001f)
            {
                return Vector2.Distance(p, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLenSqr);
            Vector2 closest = a + ab * t;
            return Vector2.Distance(p, closest);
        }

        private static float DistanceXZ(Vector3 first, Vector3 second)
        {
            return Vector2.Distance(
                new Vector2(first.x, first.z),
                new Vector2(second.x, second.z));
        }

        private static float DistanceSegmentToSegmentXZ(Vector3 startA, Vector3 endA, Vector3 startB, Vector3 endB)
        {
            if (SegmentsIntersectXZ(startA, endA, startB, endB))
            {
                return 0f;
            }

            return Mathf.Min(
                Mathf.Min(DistancePointToSegmentXZ(startA, startB, endB), DistancePointToSegmentXZ(endA, startB, endB)),
                Mathf.Min(DistancePointToSegmentXZ(startB, startA, endA), DistancePointToSegmentXZ(endB, startA, endA)));
        }

        private static bool SegmentsIntersectXZ(Vector3 startA, Vector3 endA, Vector3 startB, Vector3 endB)
        {
            Vector2 a = new Vector2(startA.x, startA.z);
            Vector2 b = new Vector2(endA.x, endA.z);
            Vector2 c = new Vector2(startB.x, startB.z);
            Vector2 d = new Vector2(endB.x, endB.z);

            float Cross(Vector2 left, Vector2 right)
            {
                return left.x * right.y - left.y * right.x;
            }

            Vector2 ab = b - a;
            Vector2 cd = d - c;
            float denominator = Cross(ab, cd);
            if (Mathf.Abs(denominator) < 0.0001f)
            {
                return false;
            }

            Vector2 ac = c - a;
            float t = Cross(ac, cd) / denominator;
            float u = Cross(ac, ab) / denominator;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        private float GetWeakEnemyPushMaxDistance()
        {
            return GetFollowerTactic() switch
            {
                FollowerCombatTactic.Balanced => WeakEnemyPushDefaultMaxDistance,
                FollowerCombatTactic.Marksman => WeakEnemyPushMarksmanMaxDistance,
                FollowerCombatTactic.Protector => WeakEnemyPushProtectorMaxDistance,
                FollowerCombatTactic tactic => throw new ArgumentOutOfRangeException(nameof(tactic), tactic, "Unsupported follower combat tactic"),
            };
        }

        public Vector3 GetBossPosition()
        {
            return FollowerCombatAnchor.GetAnchorPosition(botOwner);
        }

        public Vector3 GetRealBossPosition()
        {
            return FollowerCombatAnchor.GetRealBossPosition(botOwner);
        }

        /// <summary>
        /// Returns boss distance using path distance first. Boss leash decisions should not use only
        /// straight-line distance because floors, doors, and building routes can make a nearby 3D
        /// position tactically far away.
        /// </summary>
        public float GetBossNavDistance(Vector3 bossPosition)
        {
            return Utils.Utils.GetNavDistance(botOwner.Position, bossPosition);
        }

        public static float GetSafeRegroupDistance(float navDistance, float directDistance)
        {
            bool navValid = !float.IsNaN(navDistance) &&
                            !float.IsInfinity(navDistance) &&
                            navDistance > 0.1f;
            if (!navValid)
            {
                return directDistance;
            }

            // Use the conservative larger value so a short NavMesh sample cannot complete or
            // suppress regroup while the follower is still farther away in world space.
            return Mathf.Max(navDistance, directDistance);
        }

        public bool ShouldDeferAutonomousRegroupAfterRecentFight(
            EnemyInfo? goalEnemy,
            float followerBossDistance,
            float regroupTriggerDistance)
        {
            if (!HasActiveCombatEnemy(goalEnemy) ||
                goalEnemy == null ||
                !IsFinite(followerBossDistance) ||
                !IsFinite(regroupTriggerDistance) ||
                regroupTriggerDistance <= 0f)
            {
                return false;
            }

            if (IsAutonomousRegroupDistanceExtreme(followerBossDistance, regroupTriggerDistance))
            {
                return false;
            }

            // GoalEnemy can change between squad members in the same frame that a visible shot
            // ends. Preserve the follower's own recent fire as combat evidence so a fresh memory-
            // only target cannot erase the bounded regroup grace.
            float lastTriggerPressedAt = botOwner.ShootData?.LastTriggerPressd ?? 0f;
            if (lastTriggerPressedAt > 0f &&
                Time.time - lastTriggerPressedAt <= AutonomousRegroupRecentFightGraceSeconds)
            {
                return true;
            }

            if (goalEnemy.IsVisible || goalEnemy.CanShoot)
            {
                return true;
            }

            if (botOwner.Memory.IsUnderFire ||
                WasHitRecently(botOwner, 1.5f) ||
                FollowerAwareness.WasRecentlyDamaged(botOwner))
            {
                return true;
            }

            return Time.time - goalEnemy.PersonalSeenTime <= AutonomousRegroupRecentFightGraceSeconds ||
                   Time.time - goalEnemy.PersonalLastSeenTime <= AutonomousRegroupRecentFightGraceSeconds;
        }

        public bool IsAutonomousRegroupDistanceExtreme(float followerBossDistance, float regroupTriggerDistance)
        {
            return IsFinite(followerBossDistance) &&
                   IsFinite(regroupTriggerDistance) &&
                   regroupTriggerDistance > 0f &&
                   followerBossDistance >= regroupTriggerDistance * AutonomousRegroupExtremeDistanceMultiplier;
        }

        /// <summary>
        /// Shared boss/follower/enemy spacing snapshot used by combat objective logic.
        /// This lets the higher-level combat tree compare who currently owns the forward line:
        /// the boss or the follower.
        /// </summary>
        public bool TryGetBossRelativeCombatSpacing(
            EnemyInfo goalEnemy,
            out Vector3 bossPosition,
            out Vector3 enemyAnchor,
            out float followerBossDistance,
            out float followerEnemyDistance,
            out float bossEnemyDistance)
        {
            bossPosition = GetBossPosition();
            enemyAnchor = GetEnemyAnchor(goalEnemy);
            followerBossDistance = 0f;
            followerEnemyDistance = 0f;
            bossEnemyDistance = 0f;

            if (!IsFinite(bossPosition) || !IsFinite(enemyAnchor))
            {
                return false;
            }

            followerBossDistance = GetBossNavDistance(bossPosition);
            followerEnemyDistance = Vector3.Distance(botOwner.Position, enemyAnchor);
            bossEnemyDistance = Vector3.Distance(bossPosition, enemyAnchor);
            return true;
        }

        /// <summary>
        /// Finds a step cover that moves the follower toward the boss while optionally requiring
        /// either a shoot lane or a hide lane from the active enemy.
        /// Used by the boss-relative combat objective so rejoin/retreat movement is cover-to-cover
        /// instead of a blind run straight at the boss.
        /// </summary>
        public bool TryFindCoverTowardBoss(
            EnemyInfo goalEnemy,
            Vector3 bossPosition,
            float searchRadius,
            bool requireShootLane,
            bool requireHideFromEnemy,
            out CustomNavigationPoint? cover)
        {
            return TryFindCoverTowardBoss(
                goalEnemy,
                bossPosition,
                searchRadius,
                requireShootLane,
                requireHideFromEnemy,
                keepBehindBoss: false,
                out cover);
        }

        public bool TryFindCoverTowardBoss(
            EnemyInfo goalEnemy,
            Vector3 bossPosition,
            float searchRadius,
            bool requireShootLane,
            bool requireHideFromEnemy,
            bool keepBehindBoss,
            out CustomNavigationPoint? cover)
        {
            cover = null;
            if (!IsFinite(bossPosition))
            {
                return false;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            ShootPointClass? shootPoint = requireShootLane ? botOwner.CurrentEnemyTargetPosition(true) : null;
            LayerMask mask = botOwner.LookSensor.Mask;
            BotsGroup.BotCurrentTactic tactic = requireShootLane
                ? BotsGroup.BotCurrentTactic.Attack
                : BotsGroup.BotCurrentTactic.Protect;
            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                tactic,
                requireShootLane ? CoverShootType.shoot : CoverShootType.hide,
                requireShootLane ? CoverSearchIntent.AttackMoving : CoverSearchIntent.RunToCover);

            Vector3 bosswardDirection = bossPosition - botOwner.Position;
            bosswardDirection.y = 0f;
            if (bosswardDirection.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            bosswardDirection.Normalize();
            CustomNavigationPoint? candidate = SelectBestEvaluatedCover(
                botOwner.Position,
                searchRadius,
                searchType,
                point =>
                {
                    if (!IsCoverUsable(point, true))
                    {
                        return false;
                    }

                    Vector3 pointDirection = point.Position - botOwner.Position;
                    pointDirection.y = 0f;
                    if (pointDirection.sqrMagnitude <= 0.01f ||
                        Vector3.Dot(pointDirection.normalized, bosswardDirection) < 0.1f)
                    {
                        return false;
                    }

                    if (requireHideFromEnemy &&
                        IsFinite(enemyAnchor) &&
                        !point.CanIHideFromPos(0f, true, false, enemyAnchor))
                    {
                        return false;
                    }

                    if (shootPoint != null)
                    {
                        bool canShoot = Utils.Utils.CanShootToTarget(shootPoint, point, mask, false);
                        point.CanIShootToEnemy = canShoot;
                        if (!canShoot)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        point.CanIShootToEnemy = false;
                    }

                    if (keepBehindBoss &&
                        !IsSupportPositionBehindBossLine(point.Position, bossPosition, enemyAnchor))
                    {
                        return false;
                    }

                    if (!IsCoverSafeFromAlternateThreats(point, goalEnemy.ProfileId, strict: keepBehindBoss))
                    {
                        return false;
                    }

                    return true;
                },
                point => GetEvaluatedCoverNavDistance(point) +
                         Vector3.Distance(point.Position, bossPosition) * 0.75f);

            if (candidate == null)
            {
                return false;
            }

            cover = candidate;
            return true;
        }

        public bool TryCommitPushSupportCover(
            EnemyInfo goalEnemy,
            Vector3 pushOwnerPosition,
            Vector3 enemyPosition,
            Vector3 watchedDestination,
            string reason,
            out string committedReason)
        {
            committedReason = reason;
            if (!CanAcquireCommittedCover())
            {
                return false;
            }

            CustomNavigationPoint? cover = FindPushSupportCover(
                goalEnemy,
                pushOwnerPosition,
                enemyPosition,
                requireEnemyShootLane: true,
                avoidBossFireLane: true);
            if (cover == null)
            {
                cover = FindPushSupportCover(
                    goalEnemy,
                    pushOwnerPosition,
                    watchedDestination,
                    requireEnemyShootLane: false,
                    avoidBossFireLane: true);
                committedReason += ".watchDestination";
            }
            else
            {
                committedReason += ".shootEnemy";
            }

            if (cover != null)
            {
                return TryCommitSelectedCombatCover(goalEnemy, cover, committedReason);
            }

            return TryCommitFiringPositionCover(
                goalEnemy,
                reason + ".fallbackFirePosition",
                out committedReason,
                preferPointToShoot: true,
                preferInbetween: true,
                avoidBossFireLane: true);
        }

        private CustomNavigationPoint? FindPushSupportCover(
            EnemyInfo goalEnemy,
            Vector3 pushOwnerPosition,
            Vector3 targetPosition,
            bool requireEnemyShootLane,
            bool keepBehindBoss = false,
            bool avoidBossFireLane = false)
        {
            if (!IsFinite(targetPosition))
            {
                return null;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            ShootPointClass targetPoint = new ShootPointClass(targetPosition + Vector3.up * 1.1f, 1f);
            LayerMask mask = botOwner.LookSensor.Mask;
            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Attack,
                CoverShootType.shoot,
                CoverSearchIntent.Attack);

            bool IsEligible(CustomNavigationPoint point, bool rejectBossFireLane)
            {
                if (!IsCoverUsable(point))
                {
                    return false;
                }

                if (!IsTeamSearchSupportPosition(point.Position, pushOwnerPosition, enemyAnchor))
                {
                    return false;
                }

                if (rejectBossFireLane &&
                    IsBossFireLaneMovementRisk(point.Position, enemyAnchor, includePath: true))
                {
                    return false;
                }

                if (requireEnemyShootLane &&
                    IsFinite(enemyAnchor) &&
                    !point.CanIHideFromPos(0f, true, false, enemyAnchor))
                {
                    return false;
                }

                if (keepBehindBoss &&
                    !IsSupportPositionBehindBossLine(point.Position, pushOwnerPosition, enemyAnchor))
                {
                    return false;
                }

                if (!IsCoverSafeFromAlternateThreats(point, goalEnemy.ProfileId, strict: keepBehindBoss))
                {
                    return false;
                }

                bool canShoot = Utils.Utils.CanShootToTarget(targetPoint, point, mask, false);
                point.CanIShootToEnemy = canShoot;
                return canShoot;
            }

            return SelectBestEvaluatedCover(
                botOwner.Position,
                60f,
                searchType,
                point => IsEligible(point, rejectBossFireLane: false),
                point => ScoreEvaluatedCover(point, pushOwnerPosition) +
                         (avoidBossFireLane &&
                          IsBossFireLaneMovementRisk(point.Position, enemyAnchor, includePath: true)
                             ? 1000f
                             : 0f));
        }

        public bool TryCreateTeamSearchSupportDecision(
            CombatEvents.PushEvent pushEvent,
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!TryFindTeamSearchSupportPoint(pushEvent.Owner.Position, GetEnemyAnchor(goalEnemy), out Vector3 supportPoint))
            {
                return false;
            }

            botOwner.GoToSomePointData.SetPoint(supportPoint);
            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPointTactical, reason);
            return true;
        }

        private bool TryFindTeamSearchSupportPoint(Vector3 pushOwnerPosition, Vector3 enemyAnchor, out Vector3 supportPoint)
        {
            supportPoint = default;
            if (!IsFinite(pushOwnerPosition) || !IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 forward = enemyAnchor - pushOwnerPosition;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
            {
                return false;
            }

            forward.Normalize();
            Vector3 side = new Vector3(-forward.z, 0f, forward.x);
            Vector3[] candidates =
            {
                pushOwnerPosition - forward * 8f,
                pushOwnerPosition - forward * 6f + side * 5f,
                pushOwnerPosition - forward * 6f - side * 5f,
                pushOwnerPosition - forward * 10f + side * 8f,
                pushOwnerPosition - forward * 10f - side * 8f,
                pushOwnerPosition + side * 8f,
                pushOwnerPosition - side * 8f
            };

            float bestScore = float.MaxValue;
            bool found = false;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!NavMesh.SamplePosition(candidates[i], out NavMeshHit hit, 4f, NavMesh.AllAreas))
                {
                    continue;
                }

                if (!IsTeamSearchSupportPosition(hit.position, pushOwnerPosition, enemyAnchor))
                {
                    continue;
                }

                float selfDistance = Vector3.Distance(botOwner.Position, hit.position);
                float ownerDistance = Vector3.Distance(pushOwnerPosition, hit.position);
                float lanePenalty = IsBossFireLaneMovementRisk(hit.position, enemyAnchor, includePath: true)
                    ? BossFireLaneSoftPenalty
                    : 0f;
                float score = selfDistance + ownerDistance * 0.35f + lanePenalty;
                if (score < bestScore)
                {
                    supportPoint = hit.position;
                    bestScore = score;
                    found = true;
                }
            }

            return found;
        }

        private static bool IsTeamSearchSupportPosition(Vector3 candidate, Vector3 pushOwnerPosition, Vector3 enemyAnchor)
        {
            if (!IsFinite(candidate) || !IsFinite(pushOwnerPosition) || !IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 ownerToEnemy = enemyAnchor - pushOwnerPosition;
            ownerToEnemy.y = 0f;
            if (ownerToEnemy.sqrMagnitude < 0.01f)
            {
                return false;
            }

            ownerToEnemy.Normalize();
            Vector3 ownerToCandidate = candidate - pushOwnerPosition;
            ownerToCandidate.y = 0f;
            float ahead = Vector3.Dot(ownerToCandidate, ownerToEnemy);
            if (ahead > 1.5f)
            {
                return false;
            }

            float ownerEnemyDistance = Vector3.Distance(pushOwnerPosition, enemyAnchor);
            float candidateEnemyDistance = Vector3.Distance(candidate, enemyAnchor);
            return candidateEnemyDistance >= ownerEnemyDistance - 2f;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> ConsumeInitialDecision()
        {
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision = initialDecision ??
                new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "missingInitialDecision");
            initialDecision = null;
            return decision;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? PreFightLogic()
        {
            if (ShouldPrioritizeEmergencyHeal())
            {
                AICoreActionResultStruct<BotLogicDecision, GClass26>? emergencyHealDecision = TryGetNeedHealDecision();
                if (emergencyHealDecision != null)
                {
                    initialDecision = null;
                    return emergencyHealDecision;
                }
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFightDecision = TryGetDogFightDecision();
            if (dogFightDecision != null)
            {
                initialDecision = null;
                return dogFightDecision;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? inFightDecision = InFightLogic();
            if (inFightDecision != null)
            {
                initialDecision = null;
                return inFightDecision;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = TryGetNeedHealDecision();
            if (healDecision != null)
            {
                initialDecision = null;
                return healDecision;
            }

            return null;
        }

        private bool ShouldPrioritizeEmergencyHeal()
        {
            if (botOwner.Medecine == null)
            {
                return false;
            }

            bool haveHealWork =
                botOwner.Medecine.FirstAid?.Have2Do == true ||
                botOwner.Medecine.SurgicalKit?.HaveWork == true ||
                botOwner.Medecine.FirstAid?.Using == true ||
                botOwner.Medecine.SurgicalKit?.Using == true;
            if (!haveHealWork)
            {
                return false;
            }

            ETagStatus? healthStatus = botOwner.GetPlayer?.HealthStatus;
            return healthStatus == ETagStatus.BadlyInjured ||
                   healthStatus == ETagStatus.Dying ||
                   IsFollowerCriticallyWounded();
        }

        /// <summary>
        /// Standalone in-cover ally support check.
        /// Allows follower to switch targets and support an actively engaged allied enemy
        /// when:
        /// 1. Follower is in cover and stably held position (≥1s)
        /// 2. Current goal enemy is not visible or does not exist
        /// 3. Not under direct fire
        /// 4. An ally is clearly engaging an enemy (visible, shootable)
        /// 5. Support cover for that engagement exists within reasonable distance
        /// 
        /// Prevents flip-flopping by:
        /// - Requiring minimum cover duration
        /// - Checking recent enemy-seen time (don't abandon hot targets)
        /// - Requiring good support cover availability
        /// </summary>
        public AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetAllyEngagementSupportDecision(bool selfSupport = false)
        {

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;

            if (selfSupport && goalEnemy == null)
            {
                return null;
            }

            // Gate 1: Must be in cover and have held it for stability
            if (!selfSupport)
            {
                if (!botOwner.Memory.IsInCover)
                {
                    inCoverSince = 0f;
                    return null;
                }

                if (inCoverSince <= 0f)
                {
                    inCoverSince = Time.time;
                }

                if (Time.time - inCoverSince < 1f)
                {
                    return null;
                }
            }


            // Gate 2: Current enemy conditions allow switching

            // If we can see current enemy, don't switch away
            if (goalEnemy != null && goalEnemy.IsVisible)
            {
                return null;
            }

            // If under active fire, need to stay focused on threat
            if (botOwner.Memory.IsUnderFire && !selfSupport)
            {
                return null;
            }

            // If current enemy was recently seen, maintain focus (avoid flip-flopping)
            if (goalEnemy != null && Time.time - goalEnemy.PersonalLastSeenTime < 2.5f && !selfSupport)
            {
                return null;
            }

            // Gate 3: An ally must be clearly engaging an enemy (visible + shootable = credible threat)
            string supportEnemyProfileId;
            Vector3 supportEnemyPosition;
            if (selfSupport)
            {
                if (goalEnemy == null)
                {
                    return null;
                }

                supportEnemyPosition = goalEnemy.CurrPosition;
                supportEnemyProfileId = goalEnemy.ProfileId;
            }
            else if (!TryGetAllyEngagementEnemy(out supportEnemyProfileId, out supportEnemyPosition))
            {
                return null;
            }

            if (!TrySelectPreferredSupportEnemy(supportEnemyProfileId, supportEnemyPosition, out EnemyInfo? selectedEnemy))
            {
                return null;
            }

            // Support should own a real committed cover, not a one-frame move order that the next
            // branch pass can immediately replace.
            bool preferBackline = GetFollowerTactic() is FollowerCombatTactic.Marksman or FollowerCombatTactic.Protector;
            bool enforceMarksmanPositionPolicy = GetFollowerTactic() == FollowerCombatTactic.Marksman;
            bool allowMarksmanBattlefieldPosition = GetFollowerTactic() == FollowerCombatTactic.Marksman;
            if (!TryCommitSupportFiringCover(
                    selectedEnemy,
                    "allySupportCover",
                    out string committedReason,
                    preferBackline,
                    enforceMarksmanPositionPolicy))
            {
                if (!TryCreateSupportFiringPositionDecision(
                        selectedEnemy,
                        supportEnemyPosition,
                        "allySupportPosition",
                        out AICoreActionResultStruct<BotLogicDecision, GClass26> positionDecision,
                        preferBackline,
                        enforceMarksmanPositionPolicy,
                        allowForwardPositions: false,
                        allowBattlefieldPositions: allowMarksmanBattlefieldPosition,
                        maxNavDistance: allowMarksmanBattlefieldPosition ? 90f : 45f))
                {
                    return null;
                }

                if (!string.IsNullOrEmpty(selectedEnemy.ProfileId))
                {
                    TryPromoteTrackedEnemyAsGoal(selectedEnemy.ProfileId);
                }

                return positionDecision;
            }

            if (!string.IsNullOrEmpty(selectedEnemy.ProfileId))
            {
                TryPromoteTrackedEnemyAsGoal(selectedEnemy.ProfileId);
            }

            return CreateMoveToCommittedCoverDecision(committedReason);
        }

        public bool TryCreateSupportFiringPositionDecision(
            EnemyInfo supportEnemy,
            Vector3 supportEnemyPosition,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool preferBackline,
            bool enforceMarksmanPositionPolicy = false,
            bool allowForwardPositions = false,
            bool allowBattlefieldPositions = false,
            float maxNavDistance = 45f)
        {
            decision = default;
            lastSupportFiringPositionRejectReason = null;
            if (!HasActiveCombatEnemy(supportEnemy))
            {
                lastSupportFiringPositionRejectReason = "noActiveEnemy";
                return false;
            }

            Vector3 enemyAnchor = GetEnemyAnchorOrFallback(supportEnemy, supportEnemyPosition);
            if (!IsFinite(enemyAnchor))
            {
                lastSupportFiringPositionRejectReason = "invalidEnemyAnchor";
                return false;
            }

            if (!TryFindSupportFiringPosition(
                    supportEnemy,
                    enemyAnchor,
                    preferBackline,
                    enforceMarksmanPositionPolicy,
                    allowForwardPositions,
                    allowBattlefieldPositions,
                    maxNavDistance,
                    out Vector3 supportPoint))
            {
                return false;
            }

            BotLogicDecision moveDecision;
            string moveReason;
            if (!TrySelectSupportFiringPositionMove(enemyAnchor, supportPoint, reason, out moveDecision, out moveReason))
            {
                lastSupportFiringPositionRejectReason = "moveSelectionRejected";
                return false;
            }

            botOwner.GoToSomePointData.SetPoint(supportPoint);
            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                moveDecision,
                moveReason);
            return true;
        }

        public bool TryCreateFiringPositionDecisionAt(
            EnemyInfo supportEnemy,
            Vector3 enemyPosition,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool preferBackline,
            bool enforceMarksmanPositionPolicy = false,
            bool allowForwardPositions = false,
            bool allowBattlefieldPositions = false,
            float maxNavDistance = 45f)
        {
            decision = default;
            lastSupportFiringPositionRejectReason = null;
            if (!HasActiveCombatEnemy(supportEnemy) || !IsFinite(enemyPosition))
            {
                lastSupportFiringPositionRejectReason = !HasActiveCombatEnemy(supportEnemy)
                    ? "noActiveEnemy"
                    : "invalidEnemyPosition";
                return false;
            }

            if (!TryFindSupportFiringPosition(
                    supportEnemy,
                    enemyPosition,
                    preferBackline,
                    enforceMarksmanPositionPolicy,
                    allowForwardPositions,
                    allowBattlefieldPositions,
                    maxNavDistance,
                    out Vector3 supportPoint))
            {
                return false;
            }

            if (!TrySelectSupportFiringPositionMove(enemyPosition, supportPoint, reason, out BotLogicDecision moveDecision, out string moveReason))
            {
                lastSupportFiringPositionRejectReason = "moveSelectionRejected";
                return false;
            }

            botOwner.GoToSomePointData.SetPoint(supportPoint);
            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                moveDecision,
                moveReason);
            return true;
        }

        private bool TrySelectSupportFiringPositionMove(
            Vector3 enemyAnchor,
            Vector3 supportPoint,
            string reason,
            out BotLogicDecision moveDecision,
            out string moveReason)
        {
            moveDecision = default;
            moveReason = string.Empty;

            float currentEnemyDistance = Vector3.Distance(botOwner.Position, enemyAnchor);
            float supportEnemyDistance = Vector3.Distance(supportPoint, enemyAnchor);
            bool increasesEnemyDistance = supportEnemyDistance >= currentEnemyDistance + 2f;
            float pointNavDistance = Utils.Utils.GetNavDistance(botOwner.Position, supportPoint);
            if (!IsFinite(pointNavDistance))
            {
                pointNavDistance = Vector3.Distance(botOwner.Position, supportPoint);
            }

            bool enemyClose = currentEnemyDistance <= 35f;
            bool pointClose = pointNavDistance <= 18f;
            bool pathSafe = IsSupportRunPathSafe(supportPoint, enemyAnchor);

            if (enemyClose && (pointClose || !pathSafe))
            {
                moveDecision = BotLogicDecision.goToPoint;
                moveReason = $"{reason}.goToPoint";
                return true;
            }

            if (increasesEnemyDistance &&
                CanSprintForCombatMovement() &&
                CanRunToEnemyNow() &&
                (!enemyClose || pathSafe))
            {
                moveDecision = BotLogicDecision.goToPoint;
                moveReason = $"{reason}.runToPoint";
                return true;
            }

            moveDecision = BotLogicDecision.goToPoint;
            moveReason = $"{reason}.goToPoint";
            return true;
        }

        private bool IsSupportRunPathSafe(Vector3 supportPoint, Vector3 enemyAnchor)
        {
            if (Covers.IsPathExposedToEnemy(botOwner.Position, supportPoint, enemyAnchor, botOwner.LookSensor.Mask, sampleCount: 5))
            {
                return false;
            }

            return !Covers.IsPathTooCloseToEnemy(
                botOwner.Position,
                supportPoint,
                enemyAnchor,
                CombatDistanceConfiguration.Instance.GetCloseQuarterDistance());
        }

        private bool TryFindSupportFiringPosition(
            EnemyInfo supportEnemy,
            Vector3 enemyAnchor,
            bool preferBackline,
            bool enforceMarksmanPositionPolicy,
            bool allowForwardPositions,
            bool allowBattlefieldPositions,
            float maxNavDistance,
            out Vector3 supportPoint)
        {
            supportPoint = Vector3.zero;
            Vector3 bossPosition = GetBossPosition();
            Vector3 anchor = IsFinite(bossPosition) ? bossPosition : botOwner.Position;
            Vector3 anchorToEnemy = enemyAnchor - anchor;
            anchorToEnemy.y = 0f;
            if (anchorToEnemy.sqrMagnitude < 0.01f)
            {
                anchorToEnemy = enemyAnchor - botOwner.Position;
                anchorToEnemy.y = 0f;
            }

            if (anchorToEnemy.sqrMagnitude < 0.01f)
            {
                lastSupportFiringPositionRejectReason = "invalidAnchorDirection";
                return false;
            }

            anchorToEnemy.Normalize();
            Vector3 side = new Vector3(-anchorToEnemy.z, 0f, anchorToEnemy.x);
            List<Vector3> candidates = new List<Vector3>
            {
                anchor - anchorToEnemy * 10f,
                anchor - anchorToEnemy * 14f,
                anchor - anchorToEnemy * 10f + side * 8f,
                anchor - anchorToEnemy * 10f - side * 8f,
                anchor - anchorToEnemy * 16f + side * 10f,
                anchor - anchorToEnemy * 16f - side * 10f,
                botOwner.Position + side * 8f,
                botOwner.Position - side * 8f,
                botOwner.Position - anchorToEnemy * 6f,
                allowForwardPositions ? anchor + anchorToEnemy * 6f : Vector3.positiveInfinity,
                allowForwardPositions ? anchor + anchorToEnemy * 10f : Vector3.positiveInfinity,
                allowForwardPositions ? anchor + anchorToEnemy * 8f + side * 7f : Vector3.positiveInfinity,
                allowForwardPositions ? anchor + anchorToEnemy * 8f - side * 7f : Vector3.positiveInfinity,
                allowForwardPositions ? botOwner.Position + anchorToEnemy * 6f : Vector3.positiveInfinity,
                allowForwardPositions ? botOwner.Position + anchorToEnemy * 6f + side * 6f : Vector3.positiveInfinity,
                allowForwardPositions ? botOwner.Position + anchorToEnemy * 6f - side * 6f : Vector3.positiveInfinity
            };

            if (allowBattlefieldPositions)
            {
                AddBattlefieldFiringCandidates(candidates, anchor, botOwner.Position, enemyAnchor, anchorToEnemy, side);
            }

            ShootPointClass shootPoint = new ShootPointClass(enemyAnchor + Vector3.up * 1.1f, 1f);
            Vector3 weaponOffset = Vector3.up * 1.2f;
            float bestScore = float.MaxValue;
            bool found = false;
            int sampled = 0;
            int rejectedClose = 0;
            int rejectedMarksmanPolicy = 0;
            int rejectedBossSeparation = 0;
            int rejectedBackline = 0;
            int rejectedAlternateThreat = 0;
            int rejectedNoShootLane = 0;
            int rejectedBlocked = 0;
            int rejectedWrongLevel = 0;
            int rejectedNoCompletePath = 0;
            int rejectedNavTooFar = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                if (!IsFinite(candidates[i]))
                {
                    continue;
                }

                if (!NavMesh.SamplePosition(candidates[i], out NavMeshHit hit, 4f, NavMesh.AllAreas))
                {
                    continue;
                }

                sampled++;
                Vector3 candidate = hit.position;
                if (IsBlockedTacticalPoint(candidate))
                {
                    rejectedBlocked++;
                    continue;
                }

                float followerVerticalDistance = Mathf.Abs(candidate.y - botOwner.Position.y);
                float bossVerticalDistance = IsFinite(bossPosition)
                    ? Mathf.Abs(candidate.y - bossPosition.y)
                    : float.MaxValue;
                if (followerVerticalDistance > SupportPointSameLevelTolerance &&
                    bossVerticalDistance > SupportPointSameLevelTolerance)
                {
                    rejectedWrongLevel++;
                    continue;
                }

                if (Vector3.Distance(candidate, enemyAnchor) < CombatDistanceConfiguration.Instance.GetCloseQuarterDistance())
                {
                    rejectedClose++;
                    continue;
                }

                if (enforceMarksmanPositionPolicy &&
                    !IsMarksmanFiringPositionAllowed(supportEnemy, candidate))
                {
                    rejectedMarksmanPolicy++;
                    continue;
                }

                if (enforceMarksmanPositionPolicy &&
                    IsMarksmanSupportSeparatedFromBoss(candidate))
                {
                    rejectedBossSeparation++;
                    continue;
                }

                if (preferBackline &&
                    IsFinite(bossPosition) &&
                    !IsSupportPositionBehindBossLine(candidate, bossPosition, enemyAnchor))
                {
                    rejectedBackline++;
                    continue;
                }

                if (!IsSupportPositionSafeFromAlternateThreats(candidate, supportEnemy.ProfileId, strict: preferBackline))
                {
                    rejectedAlternateThreat++;
                    continue;
                }

                if (!Utils.Utils.CanShootToTarget(shootPoint, candidate + weaponOffset, botOwner.LookSensor.Mask, false))
                {
                    rejectedNoShootLane++;
                    continue;
                }

                if (!Utils.Utils.TryGetCompletePathDistance(botOwner.Position, candidate, out float navDistance) ||
                    !IsFinite(navDistance))
                {
                    rejectedNoCompletePath++;
                    continue;
                }

                if (navDistance > maxNavDistance)
                {
                    rejectedNavTooFar++;
                    continue;
                }

                float bossDistance = IsFinite(bossPosition) ? Vector3.Distance(candidate, bossPosition) : 0f;
                float lanePenalty = IsBossFireLaneMovementRisk(candidate, enemyAnchor, includePath: true)
                    ? BossFireLaneSoftPenalty
                    : 0f;
                float score = navDistance + bossDistance * 0.35f + lanePenalty;
                if (score < bestScore)
                {
                    supportPoint = candidate;
                    bestScore = score;
                    found = true;
                }
            }

            if (!found)
            {
                lastSupportFiringPositionRejectReason =
                    $"noValidCandidates sampled={sampled} blocked={rejectedBlocked} wrongLevel={rejectedWrongLevel} noCompletePath={rejectedNoCompletePath} close={rejectedClose} marksmanPolicy={rejectedMarksmanPolicy} bossSeparated={rejectedBossSeparation} backline={rejectedBackline} alternateThreat={rejectedAlternateThreat} noShootLane={rejectedNoShootLane} navTooFar={rejectedNavTooFar}";
            }

            return found;
        }

        private bool IsMarksmanSupportSeparatedFromBoss(Vector3 candidate)
        {
            Vector3 bossPosition = GetBossPosition();
            if (!IsFinite(bossPosition))
            {
                return false;
            }

            float followerBossVertical = Mathf.Abs(botOwner.Position.y - bossPosition.y);
            float followerBossDirect = Vector3.Distance(botOwner.Position, bossPosition);
            if (followerBossVertical < MarksmanSupportSeparatedVerticalDistance &&
                followerBossDirect > MarksmanSupportSeparatedDirectDistance)
            {
                return false;
            }

            float candidateBossVertical = Mathf.Abs(candidate.y - bossPosition.y);
            if (followerBossVertical >= MarksmanSupportSeparatedVerticalDistance &&
                candidateBossVertical > MarksmanSupportSameLevelTolerance)
            {
                return true;
            }

            float candidateBossDirect = Vector3.Distance(candidate, bossPosition);
            if (candidateBossDirect > MarksmanSupportSeparatedDirectDistance)
            {
                return false;
            }

            if (!Utils.Utils.TryGetCompletePathDistance(candidate, bossPosition, out float candidateBossPath))
            {
                return followerBossVertical >= MarksmanSupportSeparatedVerticalDistance;
            }

            return candidateBossPath > MarksmanSupportCandidateBossPathMaxDistance &&
                   candidateBossPath > candidateBossDirect + MarksmanSupportCandidateBossPathMaxExtra;
        }

        private void AddBattlefieldFiringCandidates(
            List<Vector3> candidates,
            Vector3 anchor,
            Vector3 botPosition,
            Vector3 enemyAnchor,
            Vector3 anchorToEnemy,
            Vector3 side)
        {
            float safeFloor = CombatDistanceConfiguration.Instance.GetCloseQuarterDistance() + 8f;
            float anchorEnemyDistance = Vector3.Distance(anchor, enemyAnchor);
            float botEnemyDistance = Vector3.Distance(botPosition, enemyAnchor);

            AddForwardCandidateSet(candidates, anchor, anchorToEnemy, side, anchorEnemyDistance, safeFloor, 24f, 36f, 50f, 70f, 95f);
            AddForwardCandidateSet(candidates, botPosition, anchorToEnemy, side, botEnemyDistance, safeFloor, 24f, 40f, 60f, 85f, 115f);
        }

        private static void AddForwardCandidateSet(
            List<Vector3> candidates,
            Vector3 origin,
            Vector3 direction,
            Vector3 side,
            float enemyDistance,
            float safeFloor,
            params float[] forwardDistances)
        {
            for (int i = 0; i < forwardDistances.Length; i++)
            {
                float distance = forwardDistances[i];
                if (distance >= enemyDistance - safeFloor)
                {
                    continue;
                }

                Vector3 forwardPoint = origin + direction * distance;
                float sideOffset = Mathf.Clamp(distance * 0.2f, 8f, 16f);
                candidates.Add(forwardPoint);
                candidates.Add(forwardPoint + side * sideOffset);
                candidates.Add(forwardPoint - side * sideOffset);
            }
        }

        public bool IsMarksmanFiringPositionAllowed(EnemyInfo goalEnemy, Vector3 position)
        {
            if (IsEnemyMarksman(goalEnemy))
            {
                return true;
            }

            Vector3 enemyAnchor = GetEnemyAnchorOrFallback(goalEnemy, goalEnemy.CurrPosition);
            if (!IsFinite(position) || !IsFinite(enemyAnchor))
            {
                return false;
            }

            float currentEnemyDistance = Vector3.Distance(botOwner.Position, enemyAnchor);
            float positionEnemyDistance = Vector3.Distance(position, enemyAnchor);
            if (positionEnemyDistance + 1.5f >= currentEnemyDistance)
            {
                return true;
            }

            float safeFloor = CombatDistanceConfiguration.Instance.GetCloseQuarterDistance() + 5f;
            float aggression = GetAggression01();
            if (aggression > 0.55f)
            {
                return positionEnemyDistance >= safeFloor;
            }

            Vector3 bossPosition = GetBossPosition();
            if (IsFinite(bossPosition) &&
                IsSupportPositionBehindBossLine(position, bossPosition, enemyAnchor))
            {
                return true;
            }

            bool enemyClose = currentEnemyDistance <= 35f;
            float distanceReduction = currentEnemyDistance - positionEnemyDistance;
            return enemyClose &&
                   distanceReduction <= 6f &&
                   positionEnemyDistance >= safeFloor;
        }

        private bool IsSupportPositionSafeFromAlternateThreats(Vector3 position, string? primaryEnemyProfileId, bool strict)
        {
            if (botOwner.EnemiesController?.EnemyInfos == null)
            {
                return true;
            }

            Vector3 firePosition = position + Vector3.up * 1.2f;
            foreach (EnemyInfo enemyInfo in botOwner.EnemiesController.EnemyInfos.Values)
            {
                if (!HasActiveCombatEnemy(enemyInfo) ||
                    string.Equals(enemyInfo.ProfileId, primaryEnemyProfileId, StringComparison.Ordinal))
                {
                    continue;
                }

                Vector3 enemyAnchor = GetEnemyAnchor(enemyInfo);
                if (!IsFinite(enemyAnchor))
                {
                    continue;
                }

                bool dangerousThreat =
                    enemyInfo.CanShoot ||
                    enemyInfo.IsVisible ||
                    Time.time - enemyInfo.PersonalLastSeenTime <= 3f;
                if (!dangerousThreat)
                {
                    continue;
                }

                if (strict && Vector3.Distance(position, enemyAnchor) < CombatDistanceConfiguration.Instance.GetCloseQuarterDistance())
                {
                    return false;
                }

                ShootPointClass threatShootPoint = new ShootPointClass(enemyAnchor + Vector3.up * 1.1f, 1f);
                if (Utils.Utils.CanShootToTarget(threatShootPoint, firePosition, botOwner.LookSensor.Mask, false))
                {
                    return false;
                }
            }

            return true;
        }

        public bool TrySelectPreferredSupportEnemy(
            string requestedEnemyProfileId,
            Vector3 requestedEnemyPosition,
            out EnemyInfo? selectedEnemy,
            bool preferBackline = false,
            bool promoteSelected = true)
        {
            selectedEnemy = null;

            EnemyInfo? requestedEnemy = GetTrackedEnemyByProfileId(requestedEnemyProfileId);
            EnemyInfo? currentEnemy = botOwner.Memory?.GoalEnemy;

            float requestedScore = ScoreSupportEnemy(requestedEnemy, requestedEnemyPosition, preferBackline);
            float currentScore = ScoreSupportEnemy(currentEnemy, GetEnemyAnchorOrFallback(currentEnemy, requestedEnemyPosition), preferBackline);

            EnemyInfo? bestKnownEnemy = null;
            float bestKnownScore = float.MinValue;
            if (botOwner.EnemiesController?.EnemyInfos != null)
            {
                foreach (EnemyInfo enemyInfo in botOwner.EnemiesController.EnemyInfos.Values)
                {
                    float score = ScoreSupportEnemy(enemyInfo, GetEnemyAnchorOrFallback(enemyInfo, Vector3.zero), preferBackline);
                    if (score > bestKnownScore)
                    {
                        bestKnownEnemy = enemyInfo;
                        bestKnownScore = score;
                    }
                }
            }

            selectedEnemy = requestedScore >= currentScore ? requestedEnemy : currentEnemy;
            float selectedScore = Mathf.Max(requestedScore, currentScore);
            if (bestKnownScore > selectedScore + 1.5f)
            {
                selectedEnemy = bestKnownEnemy;
                selectedScore = bestKnownScore;
            }

            if (!HasActiveCombatEnemy(selectedEnemy))
            {
                return false;
            }

            if (promoteSelected && !string.IsNullOrEmpty(selectedEnemy.ProfileId))
            {
                TryPromoteTrackedEnemyAsGoal(selectedEnemy.ProfileId);
            }

            return true;
        }

        public void PrepareStartDecision(float aggression)
        {
            BeginCoverEvaluationCycle();
            initialDecision = null;

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return;
            }

            bool haveCover = TryGetGeneralStartCover(goalEnemy, out CustomNavigationPoint? startCover, out float startCoverNavDistance, out bool startCoverHasShootLane);
            bool closeCover = haveCover &&
                              startCoverNavDistance <= CombatDistanceConfiguration.Instance.GetStartCloseCoverDistance();
            bool farCover = haveCover && !closeCover;

            // Decision 1: enemy visible + close shooting cover -> attack-moving into that cover.
            // Marksman enemies are a special case: default riflemen should not generic
            // attack-move around them, because elevated marksmen often cannot be reached
            // safely. Let the tactic planner pick a firing position instead.
            if (!IsEnemyMarksman(goalEnemy) &&
                goalEnemy.IsVisible &&
                closeCover &&
                startCover != null &&
                startCover.CanIShootToEnemy)
            {
                SetCover(startCover);
                BotLogicDecision action = BotLogicDecision.attackMoving;
                initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    action,
                    CreateMovementReason("startVisCloseCover", action));
                return;
            }

            // Decision 2: enemy unseen + under fire.
            // If close cover exists -> move with suppressive fire.
            // Else if far cover exists -> run to cover.
            // Else -> hold lane with suppressive fire in place.
            if (!goalEnemy.IsVisible && botOwner.Memory.IsUnderFire)
            {
                if (closeCover)
                {
                    SetCover(startCover);
                    BotLogicDecision action = BotLogicDecision.attackMovingWithSuppress;
                    initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        action,
                        CreateMovementReason("startSuppressionCover", action));
                    return;
                }

                if (farCover)
                {
                    SetCover(startCover);
                    BotLogicDecision action = BotLogicDecision.runToCover;
                    initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        action,
                        CreateMovementReason("startUnderFireCover", action));
                    return;
                }

                initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    CreateMovementReason("startUnderFire", BotLogicDecision.suppressFire));
                return;
            }

            // Decision 3: enemy unseen, not under fire, and allies are actively engaging -> support from shooting cover.
            if (!goalEnemy.IsVisible && !botOwner.Memory.IsUnderFire && TryGetAllyEngagementEnemy(out string supportEnemyProfileId, out Vector3 supportEnemyPosition))
            {
                TryPromoteTrackedEnemyAsGoal(supportEnemyProfileId);

                if (TryGetSupportCover(supportEnemyPosition, out CustomNavigationPoint? supportCover, out float supportCoverNavDistance))
                {
                    SetCover(supportCover);
                    BotLogicDecision supportDecision = supportCoverNavDistance <= StartSupportSuppressDistance
                        ? BotLogicDecision.attackMovingWithSuppress
                        : BotLogicDecision.runToCover;
                    initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        supportDecision,
                        CreateMovementReason("startAllySupport", supportDecision));
                    return;
                }
            }

            // Decision 4: enemy unseen and low threat -> close pressure/push.
            if (!goalEnemy.IsVisible &&
                !Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(goalEnemy) &&
                !ShouldBlockProactiveAutoPushForWeaponThreat(goalEnemy) &&
                IsEnemyLowThreat(goalEnemy, aggression > 0.6f, aggression >= 0.8f ? 2f : 1f) &&
                IsWeakEnemyAutoPushRoleAllowed(goalEnemy))
            {

                initialDecision = EnemySearch(
                    ShouldUseCautiousWeaponThreatStyle(goalEnemy)
                        ? "startWeakEnemyPush.tactical.cautious"
                        : "startWeakEnemyPush.tactical",
                    true,
                    cautious: ShouldUseCautiousWeaponThreatStyle(goalEnemy));
                return;
            }

            // Decision 5: any far cover opportunity at combat start -> run to cover.
            if (farCover)
            {
                SetCover(startCover);
                BotLogicDecision action = BotLogicDecision.runToCover;
                initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    action,
                    CreateMovementReason(goalEnemy.IsVisible ? "startVisFarCover" : "startBlindFarCover", action));
            }
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? InFightLogic()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            AICoreActionResultStruct<BotLogicDecision, GClass26>? shootNowDecision = TryGetImmediateShootDecision("ShootImmediately");
            if (shootNowDecision != null)
            {
                return shootNowDecision;
            }

            if (CanShootFromCurrentCover(out string cause))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, cause);
            }

            if (TryGetLoadedWeaponRecentContactFireDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> recentContactFire))
            {
                return recentContactFire;
            }

            if (botOwner.NearDoorData.RecentlyClosedDoorCheckTime + 0.3f < Time.time &&
                botOwner.BotsGroup.EnemyLastSeenTimeReal + 7f >= Time.time &&
                goalEnemy != null &&
                EnemyPathCrossesRecentDoor(goalEnemy))
            {
                botOwner.Memory.Spotted(false, null, null);
            }

            return null;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetDogFightDecision()
        {
            EnemyInfo goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                ClearDecisionTransition();
                ClearDogFightState();
                return null;
            }

            if (ShouldSeekReloadRetreat(goalEnemy) &&
                !ShouldPreserveLoadedWeaponFire(goalEnemy))
            {
                ClearDogFightState();
                return null;
            }

            if (IsPointBlankContactWithoutHardSeparation(botOwner, goalEnemy))
            {
                ClearDecisionTransition();
            }

            if (TryGetDogFightInjuredSuppressRetreatDecision(
                    goalEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> injuredSuppressRetreat))
            {
                return injuredSuppressRetreat;
            }

            bool hasLiveVisibleDogFightContact = HasFreshVisibleShootableContact(goalEnemy, CloseThreatRecentSeenSeconds);
            if (!hasLiveVisibleDogFightContact)
            {
                if (IsPointBlankContactWithoutHardSeparation(botOwner, goalEnemy))
                {
                    SetDogFightState(BotDogFightStatus.dogFight);
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pointBlankContactDogFight");
                }

                if (!IsEnemyActivelyThreateningMe(goalEnemy, CloseThreatDogFightDistance, CloseThreatRecentSeenSeconds))
                {
                    ClearDogFightState();
                    return null;
                }

                SetDogFightState(BotDogFightStatus.dogFight);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "closeThreatDogFight");
            }

            BotDogFightStatus dogFightState = botOwner.DogFight?.DogFightState ?? BotDogFightStatus.none;
            bool canUseDogFight = CanUseDogFightNow(goalEnemy);
            if (Time.time < dogFightBlockedUntil)
            {
                SetDogFightState(BotDogFightStatus.shootFromPlace);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "cdgCooldownFire");
            }

            if (ShouldUseCloseVisibleDogFight(goalEnemy, dogFightState))
            {
                SetDogFightState(BotDogFightStatus.dogFight);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "closeVisibleDogFight");
            }

            if (dogFightState == BotDogFightStatus.dogFight)
            {
                if (canUseDogFight)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "cdg");
                }

                SetDogFightState(BotDogFightStatus.shootFromPlace);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "cdgOutOfRangeFire");
            }

            if (dogFightState == BotDogFightStatus.shootFromPlace)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "cdgfp");
            }

            if (TryPromoteDogFightState(goalEnemy, out dogFightState))
            {
                return dogFightState == BotDogFightStatus.dogFight
                    ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "cdg")
                    : new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "cdgfp");
            }

            if (HasFreshVisibleContact(goalEnemy, CloseThreatRecentSeenSeconds) &&
                goalEnemy.Distance < 18f &&
                goalEnemy.Distance > botOwner.Settings.FileSettings.Mind.DOG_FIGHT_IN)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "cdgNoPlace");
            }

            if (HasFreshVisibleShootableContact(goalEnemy, CloseThreatRecentSeenSeconds) &&
                Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.VeryClose &&
                canUseDogFight)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "enemyVeryClose");
            }

            return null;
        }

        private bool TryGetDogFightInjuredSuppressRetreatDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (IsPointBlankContactWithoutHardSeparation(botOwner, goalEnemy) ||
                !ShouldUseDogFightInjuredSuppressRetreat(goalEnemy) ||
                botOwner.SuppressShoot == null ||
                !TryGetSuppressTarget(goalEnemy, out Vector3 suppressTarget) ||
                !HasSafeDogFightInjuredSuppressLane(suppressTarget))
            {
                return false;
            }

            bool coverTried = false;
            CustomNavigationPoint? suppressFrom = null;
            if (TryAssignCloseSuppressedHealCover(goalEnemy, ref coverTried))
            {
                suppressFrom = botOwner.Memory?.CurCustomCoverPoint;
                if (suppressFrom != null)
                {
                    botOwner.GoToSomePointData?.SetPoint(suppressFrom.Position);
                }
            }

            if (!botOwner.SuppressShoot.InitToPoint(suppressTarget, suppressFrom))
            {
                return false;
            }

            ClearDogFightState();
            botOwner.Steering.LookToPoint(suppressTarget);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.suppressFire,
                suppressFrom != null
                    ? "autoSuppress.dogFightHealRetreat.move"
                    : "autoSuppress.dogFightHealRetreat.place");
            return true;
        }

        public bool TryConsumePreparedDecisionTransition(
            EnemyInfo? goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!preparedDecisionTransition.HasValue)
            {
                return false;
            }

            PreparedCombatDecisionTransition transition = preparedDecisionTransition.Value;
            preparedDecisionTransition = null;
            bool valid = HasActiveCombatEnemy(goalEnemy) &&
                         string.Equals(transition.EnemyProfileId, goalEnemy!.ProfileId, StringComparison.Ordinal) &&
                         Time.time - transition.PreparedAt <= DecisionTransitionMaxAgeSeconds;
            if (!valid)
            {
                deferredDecisionTransition = null;
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "decisionTransition",
                    "discard",
                    DescribeDecisionTransition(transition.SourceDecision, transition.EndReason),
                    transition.NextDecision);
                return false;
            }

            deferredDecisionTransition = null;
            decision = transition.NextDecision;
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "decisionTransition",
                "consume",
                DescribeDecisionTransition(transition.SourceDecision, transition.EndReason),
                decision);
            return true;
        }

        public void ClearDecisionTransition()
        {
            preparedDecisionTransition = null;
            deferredDecisionTransition = null;
        }

        public bool TryPrepareDecisionTransition(
            AICoreActionResultStruct<BotLogicDecision, GClass26> sourceDecision,
            string endReason,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            PrepareDecisionTransition(sourceDecision, endReason, goalEnemy!, nextDecision);
            return true;
        }

        public bool TryPrepareExposedFireRecoveryBreak(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision,
            out AICoreActionEndStruct end)
        {
            end = Continue();
            if (currentDecision.Action != BotLogicDecision.shootFromPlace ||
                IsRecoveryNoCoverReason(currentDecision.Reason) ||
                IsGrenadeLauncherCombatReason(currentDecision.Reason))
            {
                return false;
            }

            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy) ||
                goalEnemy == null ||
                botOwner.Memory.IsInCover ||
                !goalEnemy.IsVisible ||
                !goalEnemy.CanShoot ||
                goalEnemy.Distance <= ExposedFirePointBlankDistance)
            {
                return false;
            }

            string enemyProfileId = goalEnemy.ProfileId ?? string.Empty;
            string decisionReason = currentDecision.Reason ?? string.Empty;
            if (exposedFireStartedAt <= 0f ||
                !string.Equals(exposedFireEnemyProfileId, enemyProfileId, StringComparison.Ordinal) ||
                !string.Equals(exposedFireDecisionReason, decisionReason, StringComparison.Ordinal))
            {
                UpdateExposedFireLease(currentDecision);
                return false;
            }

            bool freshDamage = FollowerAwareness.GetDamageRevision(botOwner) != exposedFireInitialDamageRevision;
            if (!freshDamage && !SainGoalEnemyBridge.IsEnemyLookingAtFollower(botOwner, goalEnemy))
            {
                return false;
            }

            float lastTriggerPressedAt = botOwner.ShootData?.LastTriggerPressd ?? 0f;
            bool returnedFire = lastTriggerPressedAt > exposedFireInitialTriggerPressedAt + 0.001f;
            float leaseSeconds = returnedFire
                ? ExposedFireReturnedLeaseSeconds
                : ExposedFireNoReturnLeaseSeconds;
            if (!freshDamage && Time.time - exposedFireStartedAt < leaseSeconds)
            {
                return false;
            }

            if (Time.time < exposedFireRecoveryRetryAt)
            {
                return false;
            }

            // A failed cover pass must preserve the current fire action. Retry at a bounded cadence
            // and end only after the shared recovery contract has produced actual movement.
            exposedFireRecoveryRetryAt = Time.time + ExposedFireRecoveryRetrySeconds;
            if (!TryGetCommittedRecoveryDecision(
                    goalEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> recoveryDecision) ||
                !IsMovementDecision(recoveryDecision))
            {
                return false;
            }

            string breakReason = freshDamage
                ? "exposedFireFreshDamageRecovery"
                : returnedFire
                    ? "exposedFireLeaseRecovery"
                    : "exposedFireNoReturnRecovery";
            if (!TryPrepareDecisionTransition(currentDecision, breakReason, recoveryDecision))
            {
                return false;
            }

            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "exposedFire",
                "prepareRecovery",
                breakReason,
                recoveryDecision,
                untilTime: exposedFireStartedAt + leaseSeconds);
            end = new AICoreActionEndStruct(breakReason, true);
            return true;
        }

        private void UpdateExposedFireLease(
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (nextDecision.Action != BotLogicDecision.shootFromPlace)
            {
                ResetExposedFireLease();
                return;
            }

            exposedFireEnemyProfileId = botOwner.Memory?.GoalEnemy?.ProfileId ?? string.Empty;
            exposedFireDecisionReason = nextDecision.Reason ?? string.Empty;
            exposedFireStartedAt = Time.time;
            exposedFireInitialTriggerPressedAt = botOwner.ShootData?.LastTriggerPressd ?? 0f;
            exposedFireInitialDamageRevision = FollowerAwareness.GetDamageRevision(botOwner);
            exposedFireRecoveryRetryAt = 0f;
        }

        private void ResetExposedFireLease()
        {
            exposedFireEnemyProfileId = string.Empty;
            exposedFireDecisionReason = string.Empty;
            exposedFireStartedAt = 0f;
            exposedFireInitialTriggerPressedAt = 0f;
            exposedFireInitialDamageRevision = 0;
            exposedFireRecoveryRetryAt = 0f;
        }

        private void PrepareDecisionTransition(
            AICoreActionResultStruct<BotLogicDecision, GClass26> sourceDecision,
            string endReason,
            EnemyInfo goalEnemy,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            preparedDecisionTransition = new PreparedCombatDecisionTransition(
                sourceDecision,
                endReason,
                goalEnemy.ProfileId,
                Time.time,
                nextDecision);
            deferredDecisionTransition = null;
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "decisionTransition",
                "prepare",
                DescribeDecisionTransition(sourceDecision, endReason),
                nextDecision);
        }

        private bool CanAttemptDecisionTransition(
            AICoreActionResultStruct<BotLogicDecision, GClass26> sourceDecision,
            string endReason,
            EnemyInfo goalEnemy)
        {
            if (!deferredDecisionTransition.HasValue)
            {
                return true;
            }

            DeferredCombatDecisionTransition deferred = deferredDecisionTransition.Value;
            bool sameTransition = deferred.SourceDecision.Action == sourceDecision.Action &&
                                  string.Equals(deferred.SourceDecision.Reason, sourceDecision.Reason, StringComparison.Ordinal) &&
                                  string.Equals(deferred.EndReason, endReason, StringComparison.Ordinal) &&
                                  string.Equals(deferred.EnemyProfileId, goalEnemy.ProfileId, StringComparison.Ordinal);
            if (!sameTransition || Time.time >= deferred.RetryAt)
            {
                deferredDecisionTransition = null;
                return true;
            }

            return false;
        }

        private void DeferDecisionTransition(
            AICoreActionResultStruct<BotLogicDecision, GClass26> sourceDecision,
            string endReason,
            EnemyInfo goalEnemy)
        {
            float retryAt = Time.time + FailedDecisionTransitionRetrySeconds;
            deferredDecisionTransition = new DeferredCombatDecisionTransition(
                sourceDecision,
                endReason,
                goalEnemy.ProfileId,
                retryAt);
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "decisionTransition",
                "deferNoSuccessor",
                DescribeDecisionTransition(sourceDecision, endReason),
                sourceDecision,
                untilTime: retryAt);
        }

        private static string DescribeDecisionTransition(
            AICoreActionResultStruct<BotLogicDecision, GClass26> sourceDecision,
            string endReason)
        {
            return $"{sourceDecision.Action}:{sourceDecision.Reason}->{endReason}";
        }

        private bool ShouldUseDogFightInjuredSuppressRetreat(EnemyInfo goalEnemy)
        {
            if (!IsDogFightActive() ||
                !HasActiveCombatEnemy(goalEnemy) ||
                !HasActiveOrPendingHealWork() ||
                !CanCurrentWeaponSuppress())
            {
                return false;
            }

            bool recentContact =
                goalEnemy.IsVisible ||
                Time.time - goalEnemy.PersonalSeenTime <= DogFightInjuredSuppressRetreatRecentSeenSeconds ||
                Time.time - goalEnemy.PersonalLastSeenTime <= DogFightInjuredSuppressRetreatRecentSeenSeconds;
            if (!recentContact)
            {
                return false;
            }

            return HasUrgentHealWork() ||
                   IsFollowerCriticallyWounded() ||
                   IsFollowerInjured() ||
                   botOwner.Memory.IsUnderFire ||
                   WasHitRecently(botOwner, 1.5f) ||
                   FollowerAwareness.WasRecentlyDamaged(botOwner);
        }

        private bool ShouldKeepDogFightOpeningCommitment(EnemyInfo goalEnemy)
        {
            if (dogFightOpeningStartedAt <= 0f ||
                !string.Equals(dogFightOpeningEnemyProfileId, goalEnemy.ProfileId, StringComparison.Ordinal) ||
                Time.time - dogFightOpeningStartedAt >= DogFightOpeningCommitmentSeconds ||
                goalEnemy.Distance > CloseVisibleDogFightEndDistance ||
                !ShouldPreserveLoadedWeaponFire(goalEnemy))
            {
                return false;
            }

            if (!dogFightOpeningRetreatDeferredRecorded)
            {
                dogFightOpeningRetreatDeferredRecorded = true;
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "dogFightOpening",
                    "deferInjuredRetreat",
                    "loadedCloseContact",
                    untilTime: dogFightOpeningStartedAt + DogFightOpeningCommitmentSeconds);
            }

            return true;
        }

        private bool HasSafeDogFightInjuredSuppressLane(Vector3 suppressTarget)
        {
            Vector3 fireOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.2f;
            if (!FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, suppressTarget))
            {
                return true;
            }

            Vector3 standingOrigin = GetStandingSuppressionFireOrigin(botOwner);
            return (standingOrigin - fireOrigin).sqrMagnitude > 0.04f &&
                   !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, standingOrigin, suppressTarget);
        }

        /// <summary>
        /// Keeps a currently loaded weapon in the fight while the enemy is directly shootable or
        /// during the existing one-second, geometry-verified lost-visual continuity window. The
        /// push-ready ammunition threshold controls whether the bot may advance; it must not make
        /// the bot switch away from usable rounds during immediate self-defence.
        /// </summary>
        private bool ShouldPreserveLoadedWeaponFire(EnemyInfo? goalEnemy)
        {
            if (!HasActiveCombatEnemy(goalEnemy) ||
                botOwner.WeaponManager?.Reload?.Reloading == true)
            {
                return false;
            }

            Weapon? activeWeapon = botOwner.WeaponManager?.ShootController?.Item ??
                                   botOwner.WeaponManager?.CurrentWeapon;
            if (CountLoadedRounds(activeWeapon) <= 0)
            {
                return false;
            }

            if (goalEnemy!.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            if (!FollowerImmediateFirePolicy.CanUseLostVisualSuppress(goalEnemy))
            {
                return false;
            }

            Vector3 target = FollowerImmediateFirePolicy.GetLostVisualSuppressTarget(goalEnemy);
            return FollowerImmediateFirePolicy.HasDirectFireLane(botOwner, target) &&
                   !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, target);
        }

        private bool TryGetLoadedWeaponRecentContactFireDecision(
            EnemyInfo? goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (goalEnemy == null ||
                goalEnemy.IsVisible ||
                !ShouldPreserveLoadedWeaponFire(goalEnemy))
            {
                return false;
            }

            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.suppressFire,
                "loadedWeaponRecentContactFire");
            return true;
        }

        /// <summary>
        /// Restores a rifleman to a usable primary weapon before combat planning advances. This
        /// covers both the pistol fallback dead-zone and ordered-push preparation: a push may not
        /// begin on the holster weapon, and an empty/low primary is selected and reloaded before an
        /// ordered mission resumes.
        /// </summary>
        public bool TryGetCombatLongGunPreparationDecision(
            EnemyInfo? goalEnemy,
            bool orderedPush,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                pendingCombatLongGunReloadSlot = null;
                return false;
            }

            BotWeaponManager? weaponManager = botOwner.WeaponManager;
            BotWeaponSelector? selector = weaponManager?.Selector;
            if (weaponManager == null || selector == null)
            {
                return false;
            }

            if (ShouldPreserveLoadedWeaponFire(goalEnemy))
            {
                pendingCombatLongGunReloadSlot = null;
                return false;
            }

            // Do not reclaim an emergency secondary/holster change already started by EFT. Once
            // it settles, a loaded weapon can fight immediately and an empty one can return here.
            if (!pendingCombatLongGunReloadSlot.HasValue && selector.IsChanging)
            {
                return false;
            }

            if (pendingCombatLongGunReloadSlot.HasValue)
            {
                EquipmentSlot pendingSlot = pendingCombatLongGunReloadSlot.Value;
                Weapon? pendingWeapon = GetLongGunInSlot(botOwner, pendingSlot);
                if (pendingWeapon == null)
                {
                    pendingCombatLongGunReloadSlot = null;
                    return false;
                }

                if (!IsWeaponSlotSelectedOrActive(botOwner, pendingWeapon, pendingSlot))
                {
                    TryRequestCombatLongGunSwitch(selector, pendingSlot);
                    decision = CreateWeaponPreparationHold("weaponSwitchLongGun.reload");
                    return true;
                }

                if (selector.IsChanging || !selector.IsWeaponReady || !weaponManager.IsWeaponReady)
                {
                    decision = CreateWeaponPreparationHold("weaponSwitchLongGun.settle");
                    return true;
                }

                if (HasPushReadyAmmo(pendingWeapon))
                {
                    ClearCombatLongGunReloadFailure(pendingSlot);
                    pendingCombatLongGunReloadSlot = null;
                    return false;
                }

                PreparedLongGunReloadStartResult reloadStart = TryStartPreparedLongGunReload();
                if (reloadStart == PreparedLongGunReloadStartResult.Started)
                {
                    ClearCombatLongGunReloadFailure(pendingSlot);
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.holdPosition,
                        "reloadLongGun");
                    return true;
                }

                if (reloadStart == PreparedLongGunReloadStartResult.Deferred)
                {
                    // Hands/controller readiness can briefly lag after a slot change. Keep this
                    // exact slot pending and retry after a bounded wait without selecting another
                    // weapon or treating the transient state as missing ammunition.
                    DeferCombatLongGunReloadRetry();
                    decision = CreateWeaponPreparationHold("weaponPrepareLongGun.reloadWait");
                    return true;
                }

                // A settled long gun which cannot produce a real reload is not a reason to call
                // BotReload.TryReload(): vanilla turns that failure into a weapon-selection
                // fallback. Block this exact weapon/ammo state, then let another long gun qualify.
                BlockCombatLongGunReload(pendingSlot, pendingWeapon);
                pendingCombatLongGunReloadSlot = null;
                combatLongGunReloadRetryAt = Time.time + CombatLongGunReloadTransitionSeconds;
                decision = CreateWeaponPreparationHold("weaponPrepareLongGun.reloadRejected");
                return true;
            }

            bool holsterSelected = selector.LastEquipmentSlot == EquipmentSlot.Holster;
            Weapon? activeWeapon = weaponManager.ShootController?.Item ?? weaponManager.CurrentWeapon;
            bool activeIsLongGun = IsSameWeapon(activeWeapon, GetFirstPrimaryWeapon(botOwner)) ||
                                   IsSameWeapon(activeWeapon, GetSecondPrimaryWeapon(botOwner));
            bool needsHolsterRecovery = holsterSelected && !activeIsLongGun;
            if (!orderedPush && !needsHolsterRecovery)
            {
                return false;
            }

            if (TryGetPushReadyLongGun(botOwner, out Weapon? readyWeapon, out EquipmentSlot readySlot))
            {
                if (IsWeaponSlotSelectedOrActive(botOwner, readyWeapon!, readySlot) &&
                    !selector.IsChanging &&
                    selector.IsWeaponReady &&
                    weaponManager.IsWeaponReady)
                {
                    return false;
                }

                TryRequestCombatLongGunSwitch(selector, readySlot);
                decision = CreateWeaponPreparationHold(
                    orderedPush ? "weaponSwitchLongGun.orderedPush" : "weaponSwitchLongGun.pistolRecovery");
                return true;
            }

            if (!TryGetReloadableCombatLongGun(botOwner, out _, out EquipmentSlot reloadSlot))
            {
                if (!orderedPush)
                {
                    return false;
                }

                combatLongGunReloadRetryAt = GetCombatLongGunReloadRetryAt();
                decision = CreateWeaponPreparationHold("weaponPrepareLongGun.noneAvailable");
                return true;
            }

            pendingCombatLongGunReloadSlot = reloadSlot;
            TryRequestCombatLongGunSwitch(selector, reloadSlot);
            decision = CreateWeaponPreparationHold(
                orderedPush ? "weaponSwitchLongGun.orderedReload" : "weaponSwitchLongGun.pistolReload");
            return true;
        }

        public bool HasPushReadyLongGun()
        {
            return TryGetPushReadyLongGun(botOwner, out _, out _);
        }

        public static bool IsPushReadyLongGunActive(BotOwner? owner)
        {
            BotWeaponManager? weaponManager = owner?.WeaponManager;
            Weapon? activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
            if (!HasPushReadyAmmo(activeWeapon))
            {
                return false;
            }

            return IsSameWeapon(activeWeapon, GetFirstPrimaryWeapon(owner)) ||
                   IsSameWeapon(activeWeapon, GetSecondPrimaryWeapon(owner));
        }

        public bool TrySwitchToPushReadyLongGun()
        {
            return TrySwitchToPushReadyLongGun(botOwner);
        }

        public static bool TrySwitchToPushReadyLongGun(BotOwner? owner)
        {
            if (!TryGetPushReadyLongGun(owner, out Weapon? weapon, out EquipmentSlot slot))
            {
                return false;
            }

            BotWeaponSelector? selector = owner?.WeaponManager?.Selector;
            if (selector == null)
            {
                return false;
            }

            if (IsWeaponSlotSelectedOrActive(owner, weapon!, slot))
            {
                return true;
            }

            return TryRequestCombatLongGunSwitch(selector, slot);
        }

        public static bool IsWeaponPreparationHoldReason(string? reason)
        {
            return reason?.StartsWith("weaponSwitchLongGun", StringComparison.Ordinal) == true ||
                   reason?.StartsWith("weaponPrepareLongGun", StringComparison.Ordinal) == true;
        }

        public AICoreActionEndStruct EndWeaponPreparationHold(string? reason)
        {
            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                pendingCombatLongGunReloadSlot = null;
                return new AICoreActionEndStruct("weaponPrepareLongGun.enemyMissing", true);
            }

            if (ShouldPreserveLoadedWeaponFire(goalEnemy))
            {
                pendingCombatLongGunReloadSlot = null;
                return new AICoreActionEndStruct("weaponPrepareLongGun.loadedFire", true);
            }

            BotWeaponManager? weaponManager = botOwner.WeaponManager;
            BotWeaponSelector? selector = weaponManager?.Selector;
            if (weaponManager == null || selector == null)
            {
                pendingCombatLongGunReloadSlot = null;
                return new AICoreActionEndStruct("weaponPrepareLongGun.weaponManagerMissing", true);
            }

            if (selector?.IsChanging == true || selector?.IsWeaponReady == false ||
                weaponManager?.IsWeaponReady == false)
            {
                return Continue();
            }

            if (pendingCombatLongGunReloadSlot.HasValue)
            {
                EquipmentSlot pendingSlot = pendingCombatLongGunReloadSlot.Value;
                Weapon? pendingWeapon = GetLongGunInSlot(botOwner, pendingSlot);
                Weapon? activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
                if (pendingWeapon == null)
                {
                    pendingCombatLongGunReloadSlot = null;
                }
                else if (!IsSameWeapon(activeWeapon, pendingWeapon))
                {
                    if (selector != null && Time.time >= combatLongGunSwitchRetryAt)
                    {
                        selector.TryChangeToSlot(
                            pendingSlot,
                            pendingSlot == EquipmentSlot.FirstPrimaryWeapon);
                        combatLongGunSwitchRetryAt = Time.time + CombatLongGunReloadTransitionSeconds;
                    }

                    return Continue();
                }
            }

            bool retryBoundHold = string.Equals(
                                      reason,
                                      "weaponPrepareLongGun.reloadWait",
                                      StringComparison.Ordinal) ||
                                  string.Equals(
                                      reason,
                                      "weaponPrepareLongGun.reloadRejected",
                                      StringComparison.Ordinal) ||
                                  string.Equals(
                                      reason,
                                      "weaponPrepareLongGun.noneAvailable",
                                      StringComparison.Ordinal);
            if (retryBoundHold &&
                !RefreshCombatLongGunReloadFailures() &&
                Time.time < combatLongGunReloadRetryAt)
            {
                return Continue();
            }

            return new AICoreActionEndStruct($"{reason ?? "weaponPrepareLongGun"}Ready", true);
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> CreateWeaponPreparationHold(string reason)
        {
            HoldFor(0.25f);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                reason);
        }

        private static bool TryGetPushReadyLongGun(
            BotOwner? owner,
            out Weapon? weapon,
            out EquipmentSlot slot)
        {
            Weapon? firstPrimary = GetFirstPrimaryWeapon(owner);
            Weapon? secondPrimary = GetSecondPrimaryWeapon(owner);
            Weapon? activeWeapon = owner?.WeaponManager?.ShootController?.Item ??
                                   owner?.WeaponManager?.CurrentWeapon;
            if (HasPushReadyAmmo(activeWeapon) && IsSameWeapon(activeWeapon, firstPrimary))
            {
                weapon = firstPrimary;
                slot = EquipmentSlot.FirstPrimaryWeapon;
                return true;
            }

            if (HasPushReadyAmmo(activeWeapon) && IsSameWeapon(activeWeapon, secondPrimary))
            {
                weapon = secondPrimary;
                slot = EquipmentSlot.SecondPrimaryWeapon;
                return true;
            }

            if (HasPushReadyAmmo(firstPrimary))
            {
                weapon = firstPrimary;
                slot = EquipmentSlot.FirstPrimaryWeapon;
                return true;
            }

            if (HasPushReadyAmmo(secondPrimary))
            {
                weapon = secondPrimary;
                slot = EquipmentSlot.SecondPrimaryWeapon;
                return true;
            }

            weapon = null;
            slot = default;
            return false;
        }

        private bool TryGetReloadableCombatLongGun(
            BotOwner? owner,
            out Weapon? weapon,
            out EquipmentSlot slot)
        {
            Weapon? firstPrimary = GetFirstPrimaryWeapon(owner);
            Weapon? secondPrimary = GetSecondPrimaryWeapon(owner);
            Weapon? activeWeapon = owner?.WeaponManager?.ShootController?.Item ??
                                   owner?.WeaponManager?.CurrentWeapon;
            if (IsSameWeapon(activeWeapon, firstPrimary) &&
                !IsGrenadeLauncherWeapon(firstPrimary) &&
                !IsCombatLongGunReloadBlocked(EquipmentSlot.FirstPrimaryWeapon, firstPrimary))
            {
                weapon = firstPrimary;
                slot = EquipmentSlot.FirstPrimaryWeapon;
                return true;
            }

            if (IsSameWeapon(activeWeapon, secondPrimary) &&
                !IsGrenadeLauncherWeapon(secondPrimary) &&
                !IsCombatLongGunReloadBlocked(EquipmentSlot.SecondPrimaryWeapon, secondPrimary))
            {
                weapon = secondPrimary;
                slot = EquipmentSlot.SecondPrimaryWeapon;
                return true;
            }

            if (firstPrimary != null &&
                !IsGrenadeLauncherWeapon(firstPrimary) &&
                !IsCombatLongGunReloadBlocked(EquipmentSlot.FirstPrimaryWeapon, firstPrimary))
            {
                weapon = firstPrimary;
                slot = EquipmentSlot.FirstPrimaryWeapon;
                return true;
            }

            if (secondPrimary != null &&
                !IsGrenadeLauncherWeapon(secondPrimary) &&
                !IsCombatLongGunReloadBlocked(EquipmentSlot.SecondPrimaryWeapon, secondPrimary))
            {
                weapon = secondPrimary;
                slot = EquipmentSlot.SecondPrimaryWeapon;
                return true;
            }

            weapon = null;
            slot = default;
            return false;
        }

        private static bool HasPushReadyAmmo(Weapon? weapon)
        {
            if (weapon == null || IsGrenadeLauncherWeapon(weapon))
            {
                return false;
            }

            int requiredRounds = IsShotgunWeapon(weapon)
                ? PushShotgunMinLoadedRounds
                : PushLongGunMinLoadedRounds;
            return CountLoadedRounds(weapon) >= requiredRounds;
        }

        private static Weapon? GetLongGunInSlot(BotOwner? owner, EquipmentSlot slot)
        {
            return slot == EquipmentSlot.FirstPrimaryWeapon
                ? GetFirstPrimaryWeapon(owner)
                : slot == EquipmentSlot.SecondPrimaryWeapon
                    ? GetSecondPrimaryWeapon(owner)
                    : null;
        }

        private static bool IsWeaponSlotSelectedOrActive(
            BotOwner? owner,
            Weapon weapon,
            EquipmentSlot slot)
        {
            BotWeaponManager? weaponManager = owner?.WeaponManager;
            BotWeaponSelector? selector = weaponManager?.Selector;
            Weapon? activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
            return IsSameWeapon(activeWeapon, weapon) || selector?.LastEquipmentSlot == slot;
        }

        private static bool TryRequestCombatLongGunSwitch(BotWeaponSelector selector, EquipmentSlot slot)
        {
            if (selector.LastEquipmentSlot == slot || selector.IsChanging)
            {
                return true;
            }

            return selector.TryChangeToSlot(slot, false);
        }

        private void BlockCombatLongGunReload(EquipmentSlot slot, Weapon weapon)
        {
            combatLongGunReloadFailures[slot] = new CombatLongGunReloadFailureState(
                weapon.Id,
                CountLoadedRounds(weapon),
                Time.time + CombatLongGunReloadRejectedCooldownSeconds);
        }

        private void ClearCombatLongGunReloadFailure(EquipmentSlot slot)
        {
            combatLongGunReloadFailures.Remove(slot);
        }

        private bool IsCombatLongGunReloadBlocked(EquipmentSlot slot, Weapon? weapon)
        {
            if (weapon == null ||
                !combatLongGunReloadFailures.TryGetValue(slot, out CombatLongGunReloadFailureState failure))
            {
                return false;
            }

            if (!string.Equals(failure.WeaponId, weapon.Id, StringComparison.Ordinal) ||
                failure.LoadedRounds != CountLoadedRounds(weapon) ||
                Time.time >= failure.RetryAt)
            {
                combatLongGunReloadFailures.Remove(slot);
                return false;
            }

            return true;
        }

        private bool RefreshCombatLongGunReloadFailures()
        {
            bool changed = false;
            changed |= RefreshCombatLongGunReloadFailure(
                EquipmentSlot.FirstPrimaryWeapon,
                GetFirstPrimaryWeapon(botOwner));
            changed |= RefreshCombatLongGunReloadFailure(
                EquipmentSlot.SecondPrimaryWeapon,
                GetSecondPrimaryWeapon(botOwner));
            return changed;
        }

        private bool RefreshCombatLongGunReloadFailure(EquipmentSlot slot, Weapon? weapon)
        {
            if (!combatLongGunReloadFailures.TryGetValue(slot, out CombatLongGunReloadFailureState failure))
            {
                return false;
            }

            if (weapon != null &&
                string.Equals(failure.WeaponId, weapon.Id, StringComparison.Ordinal) &&
                failure.LoadedRounds == CountLoadedRounds(weapon) &&
                Time.time < failure.RetryAt)
            {
                return false;
            }

            combatLongGunReloadFailures.Remove(slot);
            return true;
        }

        private float GetCombatLongGunReloadRetryAt()
        {
            float retryAt = float.MaxValue;
            foreach (CombatLongGunReloadFailureState failure in combatLongGunReloadFailures.Values)
            {
                if (failure.RetryAt > Time.time && failure.RetryAt < retryAt)
                {
                    retryAt = failure.RetryAt;
                }
            }

            return retryAt < float.MaxValue
                ? retryAt
                : Time.time + CombatReloadRetryCooldownSeconds;
        }

        public bool TryGetReloadRetreatDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!ShouldSeekReloadRetreat(goalEnemy))
            {
                return false;
            }

            if (ShouldPreserveLoadedWeaponFire(goalEnemy))
            {
                return false;
            }

            bool reloadActive = botOwner.WeaponManager?.Reload?.Reloading == true;
            if (!reloadActive && Time.time < combatReloadRetryAt)
            {
                return false;
            }

            if (IsPendingLauncherPrimaryFallbackWeaponSelected())
            {
                RequestLauncherPrimaryFallback("tactical.lowAmmo");
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    "launcherFallback.tactical.lowAmmo");
                return true;
            }

            if (botOwner.Memory.IsInCover)
            {
                if (!TryStartCombatReload())
                {
                    DeferCombatReloadRetry();
                    return false;
                }

                HoldCoverForMaxDuration();
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    "reloadInCover");
                return true;
            }

            if (HasCommittedPosition(out decision))
            {
                return true;
            }

            if (HasCommittedCover())
            {
                if (IsBotInCommittedCover())
                {
                    if (!TryStartCombatReload())
                    {
                        DeferCombatReloadRetry();
                        return false;
                    }

                    AssignCommittedCover();
                    ExtendCommittedCover();
                    HoldCoverForMaxDuration();
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.holdPosition,
                        "reloadInCover");
                    return true;
                }

                AssignCommittedCover();
                decision = CreateCommittedCoverMoveDecision();
                return true;
            }

            if (TryCommitCombatCover(
                    goalEnemy,
                    requireShootLane: false,
                    CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius(),
                    out string coverReason,
                    avoidBossFireLane: true))
            {
                decision = CreateMoveToCommittedCoverDecision($"reloadRetreat.{coverReason}");
                return true;
            }

            if (ShouldReloadInPlaceWithoutCover())
            {
                if (!TryStartCombatReload())
                {
                    DeferCombatReloadRetry();
                    return false;
                }

                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    "reloadNoCover");
                return true;
            }

            return false;
        }

        public bool ShouldSeekReloadRetreat(EnemyInfo? goalEnemy)
        {
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            if (!IsUnsafeReloadThreat(goalEnemy))
            {
                return false;
            }

            return IsReloadingOrLowOnAmmo();
        }

        private bool IsUnsafeReloadThreat(EnemyInfo goalEnemy)
        {
            if (goalEnemy.IsVisible &&
                goalEnemy.CanShoot &&
                goalEnemy.Distance <= ReloadRetreatThreatDistance)
            {
                return true;
            }

            // Incoming pressure is authoritative even when GoalEnemy is a different squad member
            // or only a group/memory contact. Requiring that goal to be the nearby personally-seen
            // attacker lets exposed vanilla hold reloads bypass the cover-first reload policy.
            if (botOwner.Memory.IsUnderFire ||
                WasHitRecently(botOwner, 0.75f) ||
                FollowerAwareness.WasRecentlyThreatened(botOwner))
            {
                return true;
            }

            return false;
        }

        private bool IsReloadingOrLowOnAmmo()
        {
            if (IsPendingLauncherPrimaryFallbackWeaponSelected())
            {
                return true;
            }

            BotWeaponManager? weaponManager = botOwner.WeaponManager;
            Weapon? activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
            if (IsGrenadeLauncherWeapon(activeWeapon))
            {
                return weaponManager?.Reload?.Reloading == true || CountLoadedRounds(activeWeapon) <= 0;
            }

            if (weaponManager?.Reload == null)
            {
                return false;
            }

            if (weaponManager.Reload.Reloading || !weaponManager.HaveBullets)
            {
                return true;
            }

            int currentAmmo = weaponManager.Reload.BulletCount;
            int maxAmmo = weaponManager.Reload.MaxBulletCount;
            if (maxAmmo > 0 && currentAmmo > 0)
            {
                float ammoRatio = (float)currentAmmo / maxAmmo;
                if (ammoRatio <= ReloadRetreatAmmoRatio)
                {
                    return true;
                }
            }

            int? magazineCount = activeWeapon?.GetCurrentMagazine()?.Cartridges?.Count;
            return magazineCount.HasValue && magazineCount.Value <= ReloadRetreatMinMagazineAmmo;
        }

        private bool ShouldReloadInPlaceWithoutCover()
        {
            if (IsPendingLauncherPrimaryFallbackWeaponSelected())
            {
                return true;
            }

            BotWeaponManager? weaponManager = botOwner.WeaponManager;
            Weapon? activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
            if (IsGrenadeLauncherWeapon(activeWeapon))
            {
                return weaponManager?.Reload?.Reloading == true || CountLoadedRounds(activeWeapon) <= 0;
            }

            if (weaponManager?.Reload == null)
            {
                return false;
            }

            if (weaponManager.Reload.Reloading || !weaponManager.HaveBullets)
            {
                return true;
            }

            int? magazineCount = activeWeapon?.GetCurrentMagazine()?.Cartridges?.Count;
            return magazineCount.HasValue && magazineCount.Value <= 0;
        }

        private bool TryStartCombatReload()
        {
            BotWeaponManager? weaponManager = botOwner.WeaponManager;
            BotReload? reload = weaponManager?.Reload;
            if (weaponManager == null || reload == null)
            {
                return false;
            }

            if (reload.Reloading)
            {
                return true;
            }

            if (!weaponManager.HaveBullets && !reload.FightShallReload())
            {
                return false;
            }

            if (weaponManager.ShootController?.CanStartReload() != true)
            {
                return false;
            }

            return reload.TryReload();
        }

        private PreparedLongGunReloadStartResult TryStartPreparedLongGunReload()
        {
            BotWeaponManager? weaponManager = botOwner.WeaponManager;
            BotReload? reload = weaponManager?.Reload;
            if (weaponManager == null || reload == null)
            {
                return PreparedLongGunReloadStartResult.Deferred;
            }

            if (reload.Reloading)
            {
                return PreparedLongGunReloadStartResult.Started;
            }

            if (weaponManager.ShootController?.CanStartReload() != true)
            {
                return PreparedLongGunReloadStartResult.Deferred;
            }

            // TryReload() owns a fallback which selects another weapon when no reload resource is
            // found. This combat transaction already owns a specific primary slot, so prove the
            // reload first and invoke Reload() directly to keep a rejection from becoming a swap.
            if (!reload.CanReload(false))
            {
                return PreparedLongGunReloadStartResult.Rejected;
            }

            reload.Reload();
            return reload.Reloading
                ? PreparedLongGunReloadStartResult.Started
                : PreparedLongGunReloadStartResult.Deferred;
        }

        public static bool IsReloadHoldReason(string? reason)
        {
            return string.Equals(reason, "reloadInCover", StringComparison.Ordinal) ||
                   string.Equals(reason, "reloadNoCover", StringComparison.Ordinal) ||
                   string.Equals(reason, "reloadLongGun", StringComparison.Ordinal);
        }

        public AICoreActionEndStruct EndReloadHold(string reason)
        {
            if (botOwner.WeaponManager?.Reload?.Reloading == true)
            {
                return Continue();
            }

            if (string.Equals(reason, "reloadLongGun", StringComparison.Ordinal))
            {
                pendingCombatLongGunReloadSlot = null;
            }

            DeferCombatReloadRetry();
            return new AICoreActionEndStruct($"{reason}Finished", true);
        }

        private void DeferCombatReloadRetry()
        {
            combatReloadRetryAt = Time.time + CombatReloadRetryCooldownSeconds;
        }

        private void DeferCombatLongGunReloadRetry()
        {
            combatLongGunReloadRetryAt = Time.time + CombatReloadRetryCooldownSeconds;
        }

        public bool TryPrepareCloseVisibleDogFightDecision(EnemyInfo? goalEnemy, string reason)
        {
            if (!ShouldUseCloseVisibleDogFight(goalEnemy, botOwner.DogFight?.DogFightState ?? BotDogFightStatus.none))
            {
                return false;
            }

            SetDogFightState(BotDogFightStatus.dogFight);
            SetInitialDecision(new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, reason));
            return true;
        }

        public bool TryPreparePointBlankDogFightDecision(EnemyInfo? goalEnemy, string reason)
        {
            if (!IsPointBlankContactWithoutHardSeparation(botOwner, goalEnemy))
            {
                return false;
            }

            SetDogFightState(BotDogFightStatus.dogFight);
            SetInitialDecision(new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, reason));
            return true;
        }

        private bool CanUseDogFightNow(EnemyInfo goalEnemy)
        {
            return goalEnemy.Distance <= botOwner.Settings.FileSettings.Mind.DOG_FIGHT_OUT ||
                   botOwner.Memory.BotCurrentCoverInfo.UseDogFight(botOwner.Settings.FileSettings.Cover.DOG_FIGHT_AFTER_LEAVE);
        }

        private static bool ShouldUseCloseVisibleDogFight(EnemyInfo? goalEnemy, BotDogFightStatus dogFightState)
        {
            if (!HasFreshVisibleShootableContact(goalEnemy, CloseThreatRecentSeenSeconds))
            {
                return false;
            }

            float maxDistance = dogFightState == BotDogFightStatus.dogFight
                ? CloseVisibleDogFightEndDistance
                : CloseVisibleDogFightStartDistance;
            return goalEnemy.Distance <= maxDistance;
        }

        private static bool HasFreshVisibleContact(EnemyInfo? goalEnemy, float recentSeconds)
        {
            return goalEnemy != null &&
                   goalEnemy.IsVisible &&
                   FollowerEnemyInfoCorrection.HasFreshPersonalVisual(goalEnemy, recentSeconds);
        }

        private static bool HasFreshVisibleShootableContact(EnemyInfo? goalEnemy, float recentSeconds)
        {
            return goalEnemy != null &&
                   goalEnemy.IsVisible &&
                   goalEnemy.CanShoot &&
                   FollowerEnemyInfoCorrection.HasFreshPersonalVisual(goalEnemy, recentSeconds);
        }

        private void SetDogFightState(BotDogFightStatus state)
        {
            if (botOwner?.DogFight == null)
            {
                return;
            }

            if (state == BotDogFightStatus.dogFight)
            {
                string? enemyProfileId = botOwner.Memory?.GoalEnemy?.ProfileId;
                if (botOwner.DogFight.DogFightState != BotDogFightStatus.dogFight ||
                    !string.Equals(dogFightOpeningEnemyProfileId, enemyProfileId, StringComparison.Ordinal))
                {
                    dogFightOpeningStartedAt = Time.time;
                    dogFightOpeningEnemyProfileId = enemyProfileId;
                    dogFightOpeningRetreatDeferredRecorded = false;
                }
            }
            else
            {
                ResetDogFightOpeningCommitment();
            }

            botOwner.DogFight.DogFightState = state;
            botOwner.DogFight.PursuitInProgress = false;
        }

        private void ClearDogFightState()
        {
            ResetDogFightOpeningCommitment();
            pointBlankDogFightContactLostAt = 0f;
            deferredDecisionTransition = null;
            if (botOwner?.DogFight == null)
            {
                return;
            }

            botOwner.DogFight.DogFightState = BotDogFightStatus.none;
            botOwner.DogFight.PursuitInProgress = false;
        }

        private void ResetDogFightOpeningCommitment()
        {
            dogFightOpeningStartedAt = 0f;
            dogFightOpeningEnemyProfileId = null;
            dogFightOpeningRetreatDeferredRecorded = false;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetNeedHealDecision()
        {
            bool coverTried = false;

            if (botOwner.Medecine == null)
            {
                return null;
            }

            RefreshCombatHealWorkIfNeeded();

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            bool haveHealWork = botOwner.Medecine.FirstAid.Have2Do ||
                                botOwner.Medecine.SurgicalKit.HaveWork ||
                                botOwner.Medecine.FirstAid.Using ||
                                botOwner.Medecine.SurgicalKit.Using;
            var stims = botOwner.Medecine.Stimulators;
            bool shouldUseStim = stims?.HaveSmt == true &&
                                 Time.time - stims.LastEndUseTime > 3f &&
                                 stims.CanUseNow() &&
                                 botOwner.GetPlayer?.HealthStatus != ETagStatus.Healthy;

            if (botOwner.Medecine.Stimulators.Using)
            {
                if (stimStartedAt <= 0f)
                {
                    stimStartedAt = Time.time;
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.healStimulators, "healQuick");
            }

            if (TryGetBlackStomachPainStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> painStimDecision))
            {
                return painStimDecision;
            }

            if (ShouldDeferMinorFirstAidForActiveFight(goalEnemy))
            {
                ClearCommittedHealCover();
                return null;
            }

            if (!haveHealWork)
            {
                ClearCommittedHealCover();

                if (shouldUseStim &&
                    goalEnemy != null &&
                    !goalEnemy.IsVisible &&
                    Time.time - goalEnemy.PersonalLastSeenTime > 1.5f)
                {
                    stimStartedAt = Time.time;
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.healStimulators, "healQuick");
                }

                return null;
            }

            if (healBlockUntil >= Time.time)
            {
                return null;
            }

            if (CanHealAtCommittedHealCover(goalEnemy))
            {
                if (TryGetHealCoverStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> healCoverStimDecision))
                {
                    return healCoverStimDecision;
                }

                if (healStartedAt <= 0f)
                {
                    healStartedAt = Time.time;
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "healInCover");
            }

            if (TryGetVisibleHealContactDecision(goalEnemy, ref coverTried, out AICoreActionResultStruct<BotLogicDecision, GClass26> visibleContactDecision))
            {
                return visibleContactDecision;
            }

            if (TryGetNoSprintHealContactFireDecision(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> contactFireDecision))
            {
                return contactFireDecision;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? committedHealMove = TryGetCommittedHealMoveDecision(goalEnemy);
            if (committedHealMove != null)
            {
                return committedHealMove;
            }

            if (goalEnemy == null ||
                botOwner.Medecine.FirstAid.Using ||
                botOwner.Medecine.SurgicalKit.Using)
            {
                if (goalEnemy == null)
                {
                    healBlockUntil = Time.time;
                }

                if (healStartedAt <= 0f)
                {
                    healStartedAt = Time.time;
                }
                ClearCommittedHealCover();

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "healInCover");
            }

            float lastSeen = Time.time - goalEnemy.PersonalLastSeenTime;
            bool enemyVisible = goalEnemy.IsVisible;
            Enemy.ProxyDistance enemyProxyDistance = Enemy.DistanceProxy(botOwner, botOwner.Position);

            if (!enemyVisible && lastSeen > 3f)
            {
                if (botOwner.Memory.IsInCover && enemyProxyDistance > Enemy.ProxyDistance.VeryClose)
                {
                    if (healStartedAt <= 0f)
                    {
                        healStartedAt = Time.time;
                    }
                    ClearCommittedHealCover();
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "healInCover");
                }

                if (TryAssignHealCover(goalEnemy, ref coverTried))
                {
                    return CreateCommittedHealMoveDecision(goalEnemy);
                }

                if (TryGetNoCoverEmergencyStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> emergencyStimDecision))
                {
                    return emergencyStimDecision;
                }

                healBlockUntil = Time.time + 3f;
                return null;
            }

            if (!enemyVisible && lastSeen <= 3f)
            {
                if (enemyProxyDistance > Enemy.ProxyDistance.Close)
                {
                    if (botOwner.Memory.IsInCover)
                    {
                        if (healStartedAt <= 0f)
                        {
                            healStartedAt = Time.time;
                        }
                        ClearCommittedHealCover();
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "healInCover");
                    }

                    if (TryAssignHealCover(goalEnemy, ref coverTried))
                    {
                        return CreateCommittedHealMoveDecision(goalEnemy);
                    }

                    if (TryGetNoCoverEmergencyStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> closeRecentStimDecision))
                    {
                        return closeRecentStimDecision;
                    }

                    healBlockUntil = Time.time + 3f;
                    return null;
                }

                if (TryAssignHealCover(goalEnemy, ref coverTried))
                {
                    return CreateCommittedHealMoveDecision(goalEnemy);
                }

                if (TryGetNoCoverEmergencyStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> recentStimDecision))
                {
                    return recentStimDecision;
                }

                healBlockUntil = Time.time + 3f;
                return null;
            }

            if (TryAssignHealCover(goalEnemy, ref coverTried))
            {
                return CreateCommittedHealMoveDecision(goalEnemy);
            }

            if (TryGetNoCoverEmergencyStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> fallbackStimDecision))
            {
                return fallbackStimDecision;
            }

            healBlockUntil = Time.time + 3f;
            return null;
        }

        private bool TryGetVisibleHealContactDecision(
            EnemyInfo? goalEnemy,
            ref bool coverTried,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!IsCloseVisibleHealThreat(goalEnemy))
            {
                return false;
            }

            if (TryAssignCloseSuppressedHealCover(goalEnemy!, ref coverTried))
            {
                decision = CreateCommittedHealMoveDecision(goalEnemy);
                return true;
            }

            ClearCommittedHealCover();
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.shootFromPlace,
                "healRetreatFightItOut");
            return true;
        }

        public static bool IsEnemyMarksman(EnemyInfo? goalEnemy)
        {
            return goalEnemy?.Person?.Profile?.Info?.Settings?.Role == WildSpawnType.marksman;
        }

        private void RefreshCombatHealWorkIfNeeded()
        {
            if (botOwner?.Medecine == null ||
                botOwner.GetPlayer?.ActiveHealthController == null ||
                botOwner.HealthController?.IsAlive != true ||
                botOwner.Medecine.Using ||
                botOwner.Medecine.FirstAid?.Have2Do == true ||
                botOwner.Medecine.SurgicalKit?.HaveWork == true ||
                Time.time < nextCombatHealWorkRefreshAt)
            {
                return;
            }

            bool shouldRefresh = botOwner.GetPlayer.HealthStatus != ETagStatus.Healthy;
            if (!shouldRefresh)
            {
                foreach (EBodyPart part in GClass3058.RealBodyParts)
                {
                    if (botOwner.GetPlayer.ActiveHealthController.IsBodyPartDestroyed(part))
                    {
                        shouldRefresh = true;
                        break;
                    }
                }
            }

            if (!shouldRefresh)
            {
                return;
            }

            nextCombatHealWorkRefreshAt = Time.time + 1f;
            try
            {
                FollowerMedical.RefreshMedicalWork(botOwner);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"Combat heal-work refresh failed for {botOwner.Profile?.Nickname ?? botOwner.name ?? "unknown"}: {ex.Message}");
            }
        }

        private bool TryGetHealCoverStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!CanUseStimulatorNow(out GClass491 stims))
            {
                return false;
            }

            ETagStatus? healthStatus = botOwner.GetPlayer?.HealthStatus;
            if ((healthStatus == ETagStatus.BadlyInjured || healthStatus == ETagStatus.Dying) &&
                TrySelectPositiveHealthRateStimulator(stims))
            {
                stimStartedAt = Time.time;
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.healStimulators,
                    "healCoverHealthStim");
                return true;
            }

            if (ShouldUsePainStimForDestroyedPartAtHealCover() &&
                TrySelectPainStimulator(stims))
            {
                stimStartedAt = Time.time;
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.healStimulators,
                    "healCoverBlackLimbPainStim");
                return true;
            }

            return false;
        }

        private bool TryGetNoCoverEmergencyStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (TryGetHealCoverStimDecision(out decision))
            {
                botOwner.SetPose(0.5f);
                return true;
            }

            return false;
        }

        private bool TryGetBlackStomachPainStimDecision(out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            Player player = botOwner.GetPlayer;
            if (player == null ||
                player.ActiveHealthController?.IsBodyPartDestroyed(EBodyPart.Stomach) != true ||
                player.MovementContext?.PhysicalConditionIs(EPhysicalCondition.OnPainkillers) == true)
            {
                return false;
            }

            GClass491 stims = botOwner.Medecine.Stimulators;
            if (!CanUseStimulatorNow(out stims))
            {
                return false;
            }

            if (!TrySelectPainStimulator(stims))
            {
                return false;
            }

            stimStartedAt = Time.time;
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.healStimulators,
                "blackStomachPainStim");
            return true;
        }

        private bool CanUseStimulatorNow(out GClass491 stims)
        {
            stims = botOwner.Medecine?.Stimulators;
            return stims != null &&
                   !stims.Using &&
                   Time.time - stims.LastEndUseTime > 3f &&
                   stims.CanUseNow() &&
                   botOwner.WeaponManager?.Reload?.Reloading != true;
        }

        private bool ShouldUsePainStimForDestroyedPartAtHealCover()
        {
            Player player = botOwner.GetPlayer;
            if (player == null ||
                player.MovementContext?.PhysicalConditionIs(EPhysicalCondition.OnPainkillers) == true ||
                botOwner.Medecine?.SurgicalKit?.HaveWork != true)
            {
                return false;
            }

            EBodyPart? targetPart = botOwner.Medecine.SurgicalKit.Nullable_0;
            if (targetPart.HasValue)
            {
                return IsDestroyedPainManagedPart(player, targetPart.Value);
            }

            botOwner.Medecine.SurgicalKit.FindDamagedPart();
            targetPart = botOwner.Medecine.SurgicalKit.Nullable_0;
            if (targetPart.HasValue)
            {
                return IsDestroyedPainManagedPart(player, targetPart.Value);
            }

            return HasDestroyedPainManagedPart(player);
        }

        private static bool HasDestroyedPainManagedPart(Player player)
        {
            return IsDestroyedPainManagedPart(player, EBodyPart.Stomach) ||
                   IsDestroyedPainManagedPart(player, EBodyPart.LeftArm) ||
                   IsDestroyedPainManagedPart(player, EBodyPart.RightArm) ||
                   IsDestroyedPainManagedPart(player, EBodyPart.LeftLeg) ||
                   IsDestroyedPainManagedPart(player, EBodyPart.RightLeg);
        }

        private static bool IsDestroyedPainManagedPart(Player player, EBodyPart part)
        {
            return part != EBodyPart.Head &&
                   part != EBodyPart.Chest &&
                   player.ActiveHealthController?.IsBodyPartDestroyed(part) == true;
        }

        private bool TrySelectPainStimulator(GClass491 stims)
        {
            return TrySelectStimulator(stims, HasPainReliefEffect);
        }

        private bool TrySelectPositiveHealthRateStimulator(GClass491 stims)
        {
            return TrySelectStimulator(stims, HasPositiveHealthRateBuff);
        }

        private bool TrySelectStimulator(GClass491 stims, Func<StimulatorItemClass, bool> predicate)
        {
            Player player = botOwner.GetPlayer;
            if (player == null || player.InventoryController == null)
            {
                return false;
            }

            EquipmentSlot[] searchSlots = stims.Bool_2 ? BotMedecine.secureSlots : BotMedecine.anySlots;
            stimSearchBuffer.Clear();
            player.InventoryController.GetAcceptableItemsNonAlloc<MedsItemClass>(searchSlots, stimSearchBuffer, null, null);

            for (int i = 0; i < stimSearchBuffer.Count; i++)
            {
                if (stimSearchBuffer[i] is not StimulatorItemClass stimulator)
                {
                    continue;
                }

                if (!predicate(stimulator))
                {
                    continue;
                }

                stims.StimulatorItemClass = stimulator;
                stims.HaveSmt = true;
                return true;
            }

            stims.Refresh();
            return false;
        }

        private static bool HasPainReliefEffect(StimulatorItemClass stimulator)
        {
            HealthEffectsComponent effects = stimulator.HealthEffectsComponent;
            return effects?.DamageEffects?.ContainsKey(EDamageEffectType.Pain) == true;
        }

        private static bool HasPositiveHealthRateBuff(StimulatorItemClass stimulator)
        {
            HealthEffectsComponent effects = stimulator.HealthEffectsComponent;
            if (effects == null)
            {
                return false;
            }

            GClass3019.GClass3044.GClass3045[] buffs = effects.BuffSettings;
            for (int i = 0; i < buffs.Length; i++)
            {
                if (buffs[i].BuffType == EStimulatorBuffType.HealthRate &&
                    buffs[i].Value > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetImmediateShootDecision(string reason)
        {
            if (botOwner.WeaponManager?.Reload?.Reloading == true)
            {
                return null;
            }

            if (!ShouldShootImmediately())
            {
                return null;
            }

            FollowerContactEnemyRetention.RegisterCurrentGoal(botOwner, prioritized: true);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, reason);
        }

        private bool TryGetNoSprintHealContactFireDecision(
            EnemyInfo? goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            if (goalEnemy == null ||
                CanSprintForCombatMovement() ||
                botOwner.Memory.IsInCover ||
                botOwner.Medecine.FirstAid.Using ||
                botOwner.Medecine.SurgicalKit.Using)
            {
                return false;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                if (IsPointBlankVisibleShootableThreat(goalEnemy))
                {
                    decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.shootFromPlace,
                        "healRetreatPointBlankFire");
                    return true;
                }

                bool coverTried = false;
                if (TryAssignHealCover(goalEnemy, ref coverTried))
                {
                    decision = CreateCommittedHealMoveDecision(goalEnemy);
                    return true;
                }

                if (TryGetNoCoverEmergencyStimDecision(out decision))
                {
                    return true;
                }

                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.suppressFire,
                    "healRetreatVisibleSuppress");
                return true;
            }

            if (!FollowerImmediateFirePolicy.CanUseRecentContactSuppress(goalEnemy))
            {
                return false;
            }

            Vector3 suppressTarget = FollowerImmediateFirePolicy.GetRecentContactSuppressTarget(goalEnemy);
            if (FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, suppressTarget))
            {
                return false;
            }

            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.suppressFire, "healRetreatSuppress");
            return true;
        }

        private bool TryAssignHealCover(EnemyInfo goalEnemy, ref bool coverTried)
        {
            if (coverTried)
            {
                return false;
            }

            coverTried = true;

            if (IsCommittedHealCoverValid(goalEnemy))
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
                SetCover(committedHealCover);
                return true;
            }

            if (TryFindHealCover(goalEnemy, out CustomNavigationPoint? healCover))
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
                SetCover(healCover);
                committedHealCover = healCover;
                hasCommittedHealPoint = false;
                committedHealPoint = Vector3.zero;
                CommitHealMove(goalEnemy);
                return true;
            }

            float healCoverMaxNavDistance = CombatDistanceConfiguration.Instance.GetHealCoverMaxNavDistance();
            if (TryAssignRetreatAttackCover(
                    goalEnemy,
                    false,
                    healCoverMaxNavDistance * healCoverMaxNavDistance,
                    allowWeakCoverFallback: false))
            {
                if (!IsBlockedHealCover(botOwner.Memory.CurCustomCoverPoint))
                {
                    SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
                    committedHealCover = botOwner.Memory.CurCustomCoverPoint;
                    hasCommittedHealPoint = false;
                    committedHealPoint = Vector3.zero;
                    CommitHealMove(goalEnemy);
                    return true;
                }

                SetCover(null);
            }

            if (TryFindHealHidePoint(goalEnemy, out Vector3 healPoint))
            {
                committedHealCover = null;
                committedHealPoint = healPoint;
                hasCommittedHealPoint = true;
                botOwner.GoToSomePointData.SetPoint(healPoint);
                CommitHealPointMove();
                return true;
            }

            return false;
        }

        private bool TryAssignCloseSuppressedHealCover(EnemyInfo goalEnemy, ref bool coverTried)
        {
            if (coverTried)
            {
                return false;
            }

            coverTried = true;

            if (IsCommittedHealCoverValid(goalEnemy) &&
                IsHealCoverCloseEnoughForContact(committedHealCover))
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
                SetCover(committedHealCover);
                CommitSuppressedHealRetreat();
                return true;
            }

            if (TryFindHealCover(goalEnemy, out CustomNavigationPoint? healCover, HealContactRetreatMaxNavDistance))
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
                SetCover(healCover);
                committedHealCover = healCover;
                hasCommittedHealPoint = false;
                committedHealPoint = Vector3.zero;
                CommitSuppressedHealRetreat();
                return true;
            }

            float closeRetreatMaxDistanceSqr = HealContactRetreatMaxNavDistance * HealContactRetreatMaxNavDistance;
            if (TryAssignRetreatAttackCover(
                    goalEnemy,
                    false,
                    closeRetreatMaxDistanceSqr,
                    allowWeakCoverFallback: false) &&
                !IsBlockedHealCover(botOwner.Memory.CurCustomCoverPoint) &&
                IsHealCoverCloseEnoughForContact(botOwner.Memory.CurCustomCoverPoint))
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
                committedHealCover = botOwner.Memory.CurCustomCoverPoint;
                hasCommittedHealPoint = false;
                committedHealPoint = Vector3.zero;
                CommitSuppressedHealRetreat();
                return true;
            }

            SetCover(null);
            return false;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetCommittedHealMoveDecision(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null ||
                (botOwner.Memory.IsInCover && IsBotAtCommittedHealCover()))
            {
                ClearCommittedHealCover();
                return null;
            }

            if (committedHealCover != null)
            {
                if (!IsCommittedHealCoverValid(goalEnemy))
                {
                    ClearCommittedHealCover();
                    return null;
                }

                SetCover(committedHealCover);
                SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
            }
            else if (hasCommittedHealPoint)
            {
                if (!IsCommittedHealPointValid(goalEnemy))
                {
                    ClearCommittedHealCover();
                    return null;
                }

                botOwner.GoToSomePointData.SetPoint(committedHealPoint);
                SetCoverTactic(BotsGroup.BotCurrentTactic.Ambush);
            }
            else
            {
                return null;
            }

            return CreateCommittedHealMoveDecision(goalEnemy);
        }

        private void CommitHealMove(EnemyInfo? goalEnemy)
        {
            Enemy.ProxyDistance enemyProxyDistance = Enemy.DistanceProxy(botOwner, botOwner.Position);
            bool canSprintToHealCover = CanSprintForCombatMovement();
            if (canSprintToHealCover && enemyProxyDistance > Enemy.ProxyDistance.VeryClose)
            {
                committedHealMoveAction = BotLogicDecision.runToCover;
                committedHealMoveReason = "runToHeal";
                return;
            }

            committedHealMoveAction = (BotLogicDecision)CustomBotDecisions.attackRetreat;
            committedHealMoveReason = canSprintToHealCover ? "moveToHeal.retreat" : "moveToHeal.noSprintRetreat";
        }

        private void CommitSuppressedHealRetreat()
        {
            committedHealMoveAction = (BotLogicDecision)CustomBotDecisions.attackRetreat;
            committedHealMoveReason = "moveToHeal.retreatSuppress";
        }

        private void CommitHealPointMove()
        {
            committedHealMoveAction = (BotLogicDecision)CustomBotDecisions.attackRetreat;
            committedHealMoveReason = "moveToHealPoint.attackRetreat";
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> CreateCommittedHealMoveDecision(EnemyInfo? goalEnemy)
        {
            if (committedHealMoveAction == default)
            {
                if (hasCommittedHealPoint)
                {
                    CommitHealPointMove();
                }
                else
                {
                    CommitHealMove(goalEnemy);
                }
            }

            string reason = committedHealMoveReason ?? "runToHeal";
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(committedHealMoveAction, reason);
        }

        private bool CanHealAtCommittedHealCover(EnemyInfo? goalEnemy)
        {
            if (!IsBotAtCommittedHealCover())
            {
                return false;
            }

            if (botOwner.Memory.IsUnderFire)
            {
                return false;
            }

            if (goalEnemy == null)
            {
                return true;
            }

            if (committedHealCover != null && !IsCommittedHealCoverValid(goalEnemy))
            {
                return false;
            }

            if (hasCommittedHealPoint && !IsCommittedHealPointValid(goalEnemy))
            {
                return false;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return false;
            }

            Enemy.ProxyDistance enemyProxyDistance = Enemy.DistanceProxy(botOwner, botOwner.Position);
            if (goalEnemy.IsVisible && enemyProxyDistance <= Enemy.ProxyDistance.Close)
            {
                return false;
            }

            return true;
        }

        private bool IsBotAtCommittedHealCover()
        {
            if (committedHealCover == null)
            {
                if (!hasCommittedHealPoint)
                {
                    return false;
                }

                if ((botOwner.Position - committedHealPoint).sqrMagnitude <= 2f * 2f)
                {
                    return true;
                }

                return false;
            }

            if (botOwner.Memory.IsInCover &&
                botOwner.Memory.CurCustomCoverPoint != null &&
                botOwner.Memory.CurCustomCoverPoint.Id == committedHealCover.Id)
            {
                return true;
            }

            if ((botOwner.Position - committedHealCover.Position).sqrMagnitude <= 2f * 2f)
            {
                return true;
            }

            return false;
        }

        public bool CanSprintForCombatMovement()
        {
            if (!botOwner.CanSprintPlayer || botOwner.Mover?.NoSprint == true)
            {
                return false;
            }

            Player? player = botOwner.GetPlayer ?? botOwner.AIData?.Player;
            if (player?.HealthController == null)
            {
                return true;
            }

            return !player.HealthController.IsBodyPartBroken(EBodyPart.RightLeg) &&
                   !player.HealthController.IsBodyPartDestroyed(EBodyPart.RightLeg) &&
                   !player.HealthController.IsBodyPartBroken(EBodyPart.LeftLeg) &&
                   !player.HealthController.IsBodyPartDestroyed(EBodyPart.LeftLeg);
        }

        /// <summary>
        /// Expands push distance as aggression rises while still respecting follower tactics.
        /// </summary>
        public Enemy.EnemyDistance GetMaxPushDistance(float aggression, FollowerCombatTactic? tacticOverride = null)
        {
            Enemy.EnemyDistance defaultDistance;

            if (aggression <= 0.2f)
            {
                defaultDistance = Enemy.EnemyDistance.VeryClose;
            }

            else if (aggression <= 0.4f)
            {
                defaultDistance = Enemy.EnemyDistance.Close;
            }
            else if (aggression <= 0.65f)
            {
                defaultDistance = Enemy.EnemyDistance.Distant;
            }
            else
            {
                defaultDistance = Enemy.EnemyDistance.Far;
            }

            FollowerCombatTactic tactic = tacticOverride ?? GetFollowerTactic();
            return tactic switch
            {
                FollowerCombatTactic.Balanced => defaultDistance,
                FollowerCombatTactic.Protector => Enemy.EnemyDistance.Close,
                FollowerCombatTactic.Marksman => Enemy.EnemyDistance.VeryClose,
                _ => throw new ArgumentOutOfRangeException(nameof(tactic), tactic, "Unsupported follower combat tactic"),
            };
        }

        private bool TryFindHealCover(
            EnemyInfo goalEnemy,
            out CustomNavigationPoint? cover,
            float? maxNavDistanceOverride = null)
        {
            cover = null;
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 awayFromEnemy = botOwner.Position - enemyAnchor;
            awayFromEnemy.y = 0f;
            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                awayFromEnemy = GetBossPosition() - enemyAnchor;
                awayFromEnemy.y = 0f;
            }

            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                return false;
            }

            float healCoverRetreatDistance = CombatDistanceConfiguration.Instance.GetHealCoverRetreatDistance();
            float healCoverSearchRadius = CombatDistanceConfiguration.Instance.GetHealCoverSearchRadius();
            float healCoverMaxNavDistance = maxNavDistanceOverride ?? CombatDistanceConfiguration.Instance.GetHealCoverMaxNavDistance();
            CoverSearchType healSearchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Ambush,
                CoverShootType.hide,
                CoverSearchIntent.RunToCover);

            Vector3 retreatAnchor = botOwner.Position + awayFromEnemy.normalized * healCoverRetreatDistance;
            float currentEnemyDistance = Vector3.Distance(botOwner.Position, enemyAnchor);
            cover = SelectBestThreatCover(
                retreatAnchor,
                healCoverSearchRadius,
                healSearchType,
                enemyAnchor,
                goalEnemy.ProfileId,
                point =>
                {
                    if (!IsCoverUsable(point))
                    {
                        return false;
                    }

                    if (IsBlockedHealCover(point))
                    {
                        return false;
                    }

                    point.CanIShootToEnemy = false;

                    float navDistance = GetEvaluatedCoverNavDistance(point);
                    if (!IsFinite(navDistance) ||
                        navDistance < HealCoverMinNavDistance ||
                        navDistance > healCoverMaxNavDistance)
                    {
                        return false;
                    }

                    float candidateEnemyDistance = Vector3.Distance(point.Position, enemyAnchor);
                    return candidateEnemyDistance + HealCoverMinEnemyDistanceGain >= currentEnemyDistance;
                },
                point =>
                {
                    float navDistance = GetEvaluatedCoverNavDistance(point);
                    float enemyDistanceGain = Mathf.Max(
                        0f,
                        Vector3.Distance(point.Position, enemyAnchor) - currentEnemyDistance);
                    return navDistance +
                           Vector3.Distance(point.Position, retreatAnchor) * CombatCoverCenterDistanceWeight -
                           enemyDistanceGain * 0.5f;
                },
                allowWeakFallback: false,
                out _);

            return cover != null;
        }

        private bool IsHealCoverCloseEnoughForContact(CustomNavigationPoint? cover)
        {
            if (cover == null || !IsFinite(cover.Position))
            {
                return false;
            }

            float navDistance = Utils.Utils.GetNavDistance(botOwner.Position, cover.Position);
            return IsFinite(navDistance) && navDistance <= HealContactRetreatMaxNavDistance;
        }

        private bool TryFindHealHidePoint(EnemyInfo goalEnemy, out Vector3 point)
        {
            point = Vector3.zero;
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 awayFromEnemy = botOwner.Position - enemyAnchor;
            awayFromEnemy.y = 0f;
            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                awayFromEnemy = GetBossPosition() - enemyAnchor;
                awayFromEnemy.y = 0f;
            }

            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                return false;
            }

            awayFromEnemy.Normalize();
            Vector3 lateral = Vector3.Cross(Vector3.up, awayFromEnemy).normalized;
            float currentEnemyDistance = Vector3.Distance(botOwner.Position, enemyAnchor);
            float bestScore = float.MaxValue;
            bool found = false;

            float[] distances = { 6f, 10f, 14f, 18f, 24f };
            float[] lateralOffsets = { 0f, 4f, -4f, 8f, -8f };
            for (int d = 0; d < distances.Length; d++)
            {
                for (int l = 0; l < lateralOffsets.Length; l++)
                {
                    Vector3 candidate = botOwner.Position + awayFromEnemy * distances[d] + lateral * lateralOffsets[l];
                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                    {
                        continue;
                    }

                    Vector3 navPoint = hit.position;
                    float navDistance = Utils.Utils.GetNavDistance(botOwner.Position, navPoint);
                    if (!IsFinite(navDistance) ||
                        navDistance < HealHidePointMinDistance ||
                        navDistance > HealHidePointMaxNavDistance)
                    {
                        continue;
                    }

                    float candidateEnemyDistance = Vector3.Distance(navPoint, enemyAnchor);
                    if (candidateEnemyDistance + HealHidePointEnemyDistanceGain < currentEnemyDistance)
                    {
                        continue;
                    }

                    if (!IsPointHiddenFromEnemy(navPoint, enemyAnchor))
                    {
                        continue;
                    }

                    if (Covers.IsPathExposedToEnemy(botOwner.Position, navPoint, enemyAnchor, botOwner.LookSensor.Mask, sampleCount: 5) &&
                        candidateEnemyDistance < currentEnemyDistance + 4f)
                    {
                        continue;
                    }

                    float score = navDistance - Mathf.Max(0f, candidateEnemyDistance - currentEnemyDistance) * 0.5f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        point = navPoint;
                        found = true;
                    }
                }
            }

            return found;
        }

        private bool IsPointHiddenFromEnemy(Vector3 point, Vector3 enemyAnchor)
        {
            return Covers.IsHardCoverFromThreat(point, enemyAnchor);
        }

        private bool IsCommittedHealCoverValid(EnemyInfo? goalEnemy = null)
        {
            if (committedHealCover == null)
            {
                return false;
            }

            if (IsBlockedHealCover(committedHealCover) ||
                !committedHealCover.IsFreeById(botOwner.Id) ||
                committedHealCover.IsSpotted)
            {
                committedHealCover = null;
                return false;
            }

            if (goalEnemy != null)
            {
                Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
                if (IsFinite(enemyAnchor) &&
                    !IsHardThreatCover(committedHealCover, enemyAnchor, goalEnemy.ProfileId))
                {
                    committedHealCover = null;
                    return false;
                }
            }

            return true;
        }

        private bool IsBlockedHealCover(CustomNavigationPoint? cover)
        {
            return cover != null &&
                   blockedHealCoverId == cover.Id &&
                   Time.time < blockedHealCoverUntil;
        }

        private void BlockHealCover(CustomNavigationPoint? cover)
        {
            if (cover == null)
            {
                return;
            }

            blockedHealCoverId = cover.Id;
            blockedHealCoverUntil = Time.time + HealCoverStallBlacklistSeconds;
        }

        private bool IsCommittedHealPointValid(EnemyInfo? goalEnemy = null)
        {
            if (!hasCommittedHealPoint || !IsFinite(committedHealPoint))
            {
                return false;
            }

            if (goalEnemy == null)
            {
                return true;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            return !IsFinite(enemyAnchor) || IsPointHiddenFromEnemy(committedHealPoint, enemyAnchor);
        }

        private void ClearCommittedHealCover()
        {
            committedHealCover = null;
            committedHealPoint = Vector3.zero;
            hasCommittedHealPoint = false;
            committedHealMoveAction = default;
            committedHealMoveReason = null;
            ResetHealRetreatProgress();
        }

        /// <summary>
        /// Assign a retreat/attack cover point opposite the enemy relative to the boss anchor.
        /// Returns true when a valid cover was assigned to BotCurrentCoverInfo.
        /// </summary>
        public bool TryAssignRetreatAttackCover(
            EnemyInfo goalEnemy,
            bool requireShootLane,
            float maxBossDistanceSqr = 100f,
            bool allowSpotted = false,
            bool allowWeakCoverFallback = true)
        {
            lastAssignedRetreatCoverWasWeak = false;
            Vector3 bossPosition = GetBossPosition();
            Vector3 enemyPosition = IsFinite(goalEnemy.CurrPosition) ? goalEnemy.CurrPosition : goalEnemy.EnemyLastPositionReal;
            Vector3 awayFromEnemy = bossPosition - enemyPosition;
            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                awayFromEnemy = botOwner.Position - enemyPosition;
            }

            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                awayFromEnemy = Vector3.back;
            }

            Vector3 retreatAnchor = bossPosition + awayFromEnemy.normalized * 6f;
            ShootPointClass? shootPoint = requireShootLane ? botOwner.CurrentEnemyTargetPosition(true) : null;
            BotsGroup.BotCurrentTactic tactic = requireShootLane
                ? BotsGroup.BotCurrentTactic.Attack
                : BotsGroup.BotCurrentTactic.Ambush;
            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                tactic,
                requireShootLane ? CoverShootType.shoot : CoverShootType.hide,
                CoverSearchIntent.RunToCover);

            bool IsEligible(CustomNavigationPoint point)
            {
                if (!IsCoverUsable(point, allowSpotted))
                {
                    return false;
                }

                if ((point.Position - botOwner.Position).sqrMagnitude > maxBossDistanceSqr)
                {
                    return false;
                }

                if (shootPoint != null)
                {
                    bool canShoot = Utils.Utils.CanShootToTarget(shootPoint, point, botOwner.LookSensor.Mask, false);
                    point.CanIShootToEnemy = canShoot;
                    if (!canShoot)
                    {
                        return false;
                    }
                }
                else
                {
                    point.CanIShootToEnemy = false;
                }

                return true;
            }

            float Score(CustomNavigationPoint point)
            {
                return GetEvaluatedCoverNavDistance(point) +
                       Vector3.Distance(point.Position, retreatAnchor) * CombatCoverCenterDistanceWeight;
            }

            CustomNavigationPoint? retreatCover = requireShootLane
                ? SelectBestEvaluatedCover(retreatAnchor, 18f, searchType, IsEligible, Score)
                : SelectBestThreatCover(
                    retreatAnchor,
                    18f,
                    searchType,
                    enemyPosition,
                    goalEnemy.ProfileId,
                    IsEligible,
                    Score,
                    allowWeakCoverFallback,
                    out lastAssignedRetreatCoverWasWeak);

            if (retreatCover == null)
            {
                return false;
            }

            botOwner.Memory.BotCurrentCoverInfo.Spotted();
            botOwner.Memory.BotCurrentCoverInfo.SetCover(retreatCover, true);
            return true;
        }

        /// <summary>
        /// Finds a safe boss-local cover to use when the follower needs to reanchor or protect the boss.
        /// </summary>
        public bool TryFindBossCover(EnemyInfo goalEnemy, float searchRadius, out CustomNavigationPoint? cover)
        {
            return TryFindBossCover(goalEnemy, GetBossPosition(), searchRadius, out cover);
        }

        /// <summary>
        /// Finds a safe boss-local cover around the supplied boss anchor.
        /// </summary>
        public bool TryFindBossCover(EnemyInfo goalEnemy, Vector3 bossPosition, float searchRadius, out CustomNavigationPoint? cover)
        {
            return TryFindBossCover(goalEnemy, bossPosition, searchRadius, null, out cover);
        }

        /// <summary>
        /// Performs the single terminal regroup-cover scan around the boss itself. The ordinary
        /// per-frame candidate pool is intentionally centered on the follower and capped, which can
        /// omit boss-local points on dense maps just as the follower enters the regroup radius.
        /// The regroup objective owns the once-per-activation call boundary for this targeted scan.
        /// </summary>
        public bool TryFindBossCoverForRegroupArrival(
            EnemyInfo goalEnemy,
            Vector3 bossPosition,
            float searchRadius,
            out CustomNavigationPoint? cover)
        {
            cover = null;
            if (!IsFinite(bossPosition) || searchRadius <= 0f)
            {
                return false;
            }

            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Protect,
                CoverShootType.hide,
                CoverSearchIntent.ForCover);
            List<CustomNavigationPoint> candidates = Covers.GetCoverPoints(
                botOwner,
                bossPosition,
                searchRadius,
                iritations: CombatCoverEvaluationMaxCandidates,
                searchTypeOverride: searchType);
            if (candidates.Count == 0)
            {
                return false;
            }

            BeginCoverEvaluationCycle();
            float searchRadiusSqr = searchRadius * searchRadius;
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            CustomNavigationPoint? bestHardCover = null;
            CustomNavigationPoint? bestWeakCover = null;
            float bestHardScore = float.MaxValue;
            float bestWeakScore = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                CustomNavigationPoint candidate = candidates[i];
                if (candidate == null ||
                    (candidate.CoverLevel != CoverLevel.Sit && candidate.CoverLevel != CoverLevel.Stay) ||
                    IsCoverTooCloseToTeam(candidate) ||
                    HasCombatCoverDestinationClaimConflict(candidate) ||
                    IsUrbanDetourCoverCandidate(candidate) ||
                    !IsBossLocalCoverEligible(candidate, bossPosition, searchRadiusSqr, float.PositiveInfinity))
                {
                    continue;
                }

                float score = GetEvaluatedCoverNavDistance(candidate) * 0.5f +
                              Vector3.Distance(candidate.Position, bossPosition);
                if (!IsFinite(score))
                {
                    continue;
                }

                if (score < bestWeakScore)
                {
                    bestWeakCover = candidate;
                    bestWeakScore = score;
                }

                if (IsFinite(enemyAnchor) &&
                    IsHardThreatCover(candidate, enemyAnchor, goalEnemy.ProfileId) &&
                    score < bestHardScore)
                {
                    bestHardCover = candidate;
                    bestHardScore = score;
                }
            }

            cover = bestHardCover ?? bestWeakCover;
            return cover != null;
        }

        private bool TryFindBossCover(
            EnemyInfo goalEnemy,
            Vector3 bossPosition,
            float searchRadius,
            float? maxBossDistance,
            out CustomNavigationPoint? cover)
        {
            float searchRadiusSqr = searchRadius * searchRadius;
            float maxBossDistanceSqr = maxBossDistance.HasValue
                ? maxBossDistance.Value * maxBossDistance.Value
                : float.PositiveInfinity;
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Protect,
                CoverShootType.hide,
                CoverSearchIntent.ForCover);
            CustomNavigationPoint? candidate = SelectBestThreatCover(
                bossPosition,
                searchRadius,
                searchType,
                enemyAnchor,
                goalEnemy.ProfileId,
                point =>
                {
                    if (!IsBossLocalCoverEligible(
                            point,
                            bossPosition,
                            searchRadiusSqr,
                            maxBossDistanceSqr))
                    {
                        return false;
                    }

                    point.CanIShootToEnemy = false;
                    return true;
                },
                point => GetEvaluatedCoverNavDistance(point) * 0.5f +
                         Vector3.Distance(point.Position, bossPosition),
                allowWeakFallback: true,
                out _);

            if (candidate == null)
            {
                cover = null;
                return false;
            }

            if ((candidate.Position - bossPosition).sqrMagnitude > searchRadiusSqr)
            {
                cover = null;
                return false;
            }

            if ((candidate.Position - bossPosition).sqrMagnitude < 2f * 2f)
            {
                cover = null;
                return false;
            }

            cover = candidate;
            return true;
        }

        private bool IsBossLocalCoverEligible(
            CustomNavigationPoint? point,
            Vector3 bossPosition,
            float searchRadiusSqr,
            float maxBossDistanceSqr)
        {
            if (!IsCoverUsable(point, true))
            {
                return false;
            }

            float bossDistanceSqr = (point!.Position - bossPosition).sqrMagnitude;
            return bossDistanceSqr <= searchRadiusSqr &&
                   bossDistanceSqr <= maxBossDistanceSqr &&
                   bossDistanceSqr >= 2f * 2f;
        }

        public bool TryGetGeneralStartCover(EnemyInfo goalEnemy, out CustomNavigationPoint? cover, out float navDistance, out bool hasShootLane)
        {
            cover = null;
            navDistance = float.MaxValue;
            hasShootLane = false;

            if (goalEnemy == null)
            {
                return false;
            }

            Vector3 enemyPosition = goalEnemy.CurrPosition;
            if (!IsFinite(enemyPosition))
            {
                enemyPosition = goalEnemy.EnemyLastPositionReal;
            }

            return TryGetSupportCover(enemyPosition, out cover, out navDistance, out hasShootLane);
        }

        private bool TryGetSupportCover(Vector3 enemyPosition, out CustomNavigationPoint? cover, out float navDistance)
        {
            return TryGetSupportCover(enemyPosition, out cover, out navDistance, out _);
        }

        private bool TryGetSupportCover(Vector3 enemyPosition, out CustomNavigationPoint? cover, out float navDistance, out bool hasShootLane)
        {
            return TryGetSupportCover(enemyPosition, 35f, out cover, out navDistance, out hasShootLane);
        }

        private bool TryGetSupportCover(
            Vector3 enemyPosition,
            float searchRadius,
            out CustomNavigationPoint? cover,
            out float navDistance,
            out bool hasShootLane)
        {
            cover = null;
            navDistance = float.MaxValue;
            hasShootLane = false;

            if (!IsFinite(enemyPosition))
            {
                return false;
            }

            ShootPointClass shootPoint = new ShootPointClass(enemyPosition + Vector3.up * 1.1f, 1f);
            LayerMask mask = botOwner.LookSensor.Mask;
            CoverSearchType searchType = SetCoverTacticAndGetSearchType(
                BotsGroup.BotCurrentTactic.Attack,
                CoverShootType.shoot,
                CoverSearchIntent.ForCover);

            cover = SelectBestEvaluatedCover(
                botOwner.Position,
                searchRadius,
                searchType,
                point =>
                {
                    bool canShoot = point != null &&
                                    !point.IsSpotted &&
                                    point.IsFreeById(botOwner.Id) &&
                                    Utils.Utils.CanShootToTarget(shootPoint, point, mask, false);
                    if (point != null)
                    {
                        point.CanIShootToEnemy = canShoot;
                    }

                    return canShoot;
                },
                point => GetEvaluatedCoverNavDistance(point));

            if (cover == null)
            {
                return false;
            }

            navDistance = GetEvaluatedCoverNavDistance(cover);
            if (!IsFinite(navDistance))
            {
                navDistance = Vector3.Distance(botOwner.Position, cover.Position);
            }

            hasShootLane = true;
            return true;
        }

        /// <summary>
        /// Picks the best available enemy anchor for blind pushes and cover searches.
        /// </summary>
        public static Vector3 GetEnemyAnchor(EnemyInfo goalEnemy)
        {
            if (IsFinite(goalEnemy.CurrPosition) && goalEnemy.CurrPosition.sqrMagnitude > 0.01f)
            {
                return goalEnemy.CurrPosition;
            }

            return goalEnemy.EnemyLastPositionReal;
        }

        public static Vector3 GetEnemyCurrentPosition(EnemyInfo goalEnemy)
        {
            if (goalEnemy.Person != null &&
                IsFinite(goalEnemy.Person.Position) &&
                goalEnemy.Person.Position.sqrMagnitude > 0.01f)
            {
                return goalEnemy.Person.Position;
            }

            return GetEnemyAnchor(goalEnemy);
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? EnemyCoverSearch(
            string reason = "enemySearch",
            bool weakEnemy = false,
            bool avoidBossFireLane = false)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "enemySearchNoEnemy");
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            Vector3 searchPoint = enemyAnchor;

            // Prefer an approach cover with a clear shot from a nearby tactical point.
            CustomNavigationPoint? approachCover = weakEnemy
                ? GetWeakEnemyPushCover(avoidBossFireLane)
                : GetApproachableCover(avoidBossFireLane: avoidBossFireLane);

            if (IsBlockedPushCover(approachCover, goalEnemy, reason))
            {
                approachCover = null;
            }

            if (approachCover != null)
            {
                searchPoint = approachCover.Position;
                botOwner.GoToSomePointData.SetPoint(searchPoint);
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPointTactical, reason);
            }

            return null;

        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> EnemySimpleSearch(string reason = "enemySearch")
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            return EnemySimpleSearchAt(GetEnemyAnchor(goalEnemy), reason);
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> EnemySimpleSearchAt(
            Vector3 enemyAnchor,
            string reason)
        {
            Vector3 searchPoint = enemyAnchor;

            if (NavMesh.SamplePosition(enemyAnchor, out NavMeshHit hit, 8f, -1))
            {
                ShootPointClass shootPoint = new ShootPointClass(enemyAnchor + Vector3.up * 1.1f, 1f);
                Vector3 firePos = hit.position + Vector3.up * 1.2f;
                if (Utils.Utils.CanShootToTarget(shootPoint, firePos, botOwner.LookSensor.Mask, false))
                {
                    searchPoint = hit.position;
                }
            }

            botOwner.SearchData.SearchPoint = new BotSearchPoint(searchPoint, EBotSearchPoint.playerPosition);
            botOwner.SearchData.LastSearchPoint = null;
            botOwner.SearchData.NextPosibleCheckTime = Time.time + 10f;
            botOwner.SearchData.NextPosibleGoRefresh = 0f;
            botOwner.SearchData.Going = false;
            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.search, reason);
        }

        /// <summary>
        /// Builds the cautious automatic-search response for a memory-only acquisition. Unlike the
        /// generic search planner, this path must not use GoalEnemy.CurrPosition: EFT keeps that live
        /// transform available even when the follower has never personally located the target.
        /// </summary>
        public AICoreActionResultStruct<BotLogicDecision, GClass26> CreateMemoryOnlyEnemySearchDecision(
            EnemyInfo goalEnemy,
            string reason)
        {
            if (Utils.Enemy.TryGetReliableKnownPosition(botOwner, goalEnemy, out Vector3 knownPosition))
            {
                float reportTime = goalEnemy.GroupInfo?.EnemyLastSeenTimeReal ?? 0f;
                return CreateMemoryOnlyEnemySearchDecisionAt(
                    goalEnemy,
                    knownPosition,
                    reportTime,
                    reason,
                    allowRegroupFallback: true);
            }

            // The external SAIN mod retains its own exact GoalEnemy and last-known position while
            // EFT's mirrored EnemyInfo can temporarily have no usable position. Treat that point as
            // search memory only: it does not create an enemy, sight, aim permission, or fire permission.
            if (SainGoalEnemyBridge.TryGetRetainedSameGoalEnemy(botOwner, goalEnemy, out knownPosition))
            {
                return CreateMemoryOnlyEnemySearchDecisionAt(
                    goalEnemy,
                    knownPosition,
                    Time.time,
                    reason,
                    allowRegroupFallback: true);
            }

            return CreateBlockedEnemySearchDecision($"{reason}.noKnownPosition");
        }

        public bool TryCreateSainRetainedCloseMemorySearchDecision(
            EnemyInfo goalEnemy,
            float maxDistance,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (goalEnemy == null ||
                goalEnemy.IsVisible ||
                goalEnemy.CanShoot ||
                !Utils.Enemy.IsMemoryOnlyAcquisitionWithoutPersonalContact(goalEnemy) ||
                !SainGoalEnemyBridge.TryGetRetainedSameGoalEnemy(
                    botOwner,
                    goalEnemy,
                    out Vector3 knownPosition))
            {
                return false;
            }

            float distance = Vector3.Distance(botOwner.Position, knownPosition);
            if (!IsUsableDistance(distance) || distance > maxDistance)
            {
                return false;
            }

            decision = CreateMemoryOnlyEnemySearchDecisionAt(
                goalEnemy,
                knownPosition,
                Time.time,
                reason,
                allowRegroupFallback: false);
            return true;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> CreateMemoryOnlyEnemySearchDecisionAt(
            EnemyInfo goalEnemy,
            Vector3 knownPosition,
            float reportTime,
            string reason,
            bool allowRegroupFallback)
        {
            if (IsCompletedMemorySearchUnchanged(goalEnemy, knownPosition, reportTime))
            {
                return CreateBlockedEnemySearchDecision($"{reason}.completed", allowRegroupFallback);
            }

            Vector3 toKnownPosition = knownPosition - botOwner.Position;
            if (toKnownPosition.y < botOwner.Settings.FileSettings.Move.Y_APPROXIMATION)
            {
                toKnownPosition.y = 0f;
            }

            if (toKnownPosition.sqrMagnitude < MemoryOnlySearchArrivalDistanceSqr)
            {
                MarkMemorySearchCompleted(goalEnemy, knownPosition, reportTime, reason);
                return CreateBlockedEnemySearchDecision($"{reason}.completed", allowRegroupFallback);
            }

            float knownDistance = Vector3.Distance(botOwner.Position, knownPosition);
            if (!IsUsableDistance(knownDistance) || knownDistance >= 55f)
            {
                return CreateBlockedEnemySearchDecision($"{reason}.farHold", allowRegroupFallback);
            }

            // Do not use EnemyCoverSearch here. Its shooting-cover evaluation intentionally reads
            // the current enemy target and is therefore unsuitable for a memory-only contact.
            string searchReason = knownDistance < 31f ? reason : $"{reason}.walk";
            return EnemySimpleSearchAt(knownPosition, searchReason);
        }

        private bool IsCompletedMemorySearchUnchanged(
            EnemyInfo goalEnemy,
            Vector3 knownPosition,
            float reportTime)
        {
            if (!hasCompletedMemorySearch ||
                !string.Equals(completedMemorySearchEnemyProfileId, goalEnemy.ProfileId, StringComparison.Ordinal))
            {
                return false;
            }

            bool hasNewerReport = reportTime > completedMemorySearchReportTime + 0.001f;
            bool hasMateriallyMoved = (knownPosition - completedMemorySearchPoint).sqrMagnitude >=
                                      MemoryOnlySearchRefreshDistanceSqr;
            return !hasNewerReport || !hasMateriallyMoved;
        }

        private void MarkMemorySearchCompleted(
            EnemyInfo goalEnemy,
            Vector3 knownPosition,
            float reportTime,
            string reason)
        {
            completedMemorySearchEnemyProfileId = goalEnemy.ProfileId ?? string.Empty;
            completedMemorySearchPoint = knownPosition;
            completedMemorySearchReportTime = reportTime;
            hasCompletedMemorySearch = true;
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "memorySearch",
                "complete",
                reason,
                target: knownPosition);
        }

        private void ClearCompletedMemorySearch()
        {
            completedMemorySearchEnemyProfileId = string.Empty;
            completedMemorySearchPoint = Vector3.zero;
            completedMemorySearchReportTime = 0f;
            hasCompletedMemorySearch = false;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> EnemySearch(
            string reason = "enemySearch",
            bool weakEnemy = false,
            bool pushOrdered = false,
            bool cautious = false)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "enemySearchNoEnemy");
            }

            Enemy.EnemyDistance distance = Enemy.Distance(goalEnemy);
            if (distance <= Enemy.EnemyDistance.Close)
            {
                if (EnemyCoverSearch(reason, weakEnemy, avoidBossFireLane: !pushOrdered) is AICoreActionResultStruct<BotLogicDecision, GClass26> tacticalSearchResult)
                {
                    return tacticalSearchResult;
                }

                return EnemySimpleSearch(reason);
            }

            bool directSearchLeashBlocked =
                !pushOrdered &&
                (weakEnemy
                    ? ShouldBlockWeakEnemyRushForBossDistance(goalEnemy)
                    : ShouldBlockAutomaticSearchRushForBossDistance(goalEnemy));
            if (directSearchLeashBlocked)
            {
                if (!IsWithinBossRegroupTriggerDistance())
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                        BotLogicDecision.holdPosition,
                        FollowerCombatRegroupObjective.ActivateRegroupReason);
                }

                if (TryCreateBossLeashedSearchDecision(
                        goalEnemy,
                        $"{reason}.bounded",
                        out AICoreActionResultStruct<BotLogicDecision, GClass26> boundedSearchDecision))
                {
                    return boundedSearchDecision;
                }

                return CreateBlockedEnemySearchDecision($"{reason}.boundedUnavailable");
            }

            bool canSprintToSearch = !cautious && CanSprintForCombatMovement();
            canSprintToSearch &= CanRunToEnemyNow();
            if (canSprintToSearch)
            {
                reason += ".rush";
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToEnemy, reason);
            }

            if (pushOrdered && distance <= Enemy.EnemyDistance.Distant)
            {
                reason += ".walk";
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToEnemy, reason);
            }

            if (distance <= Enemy.EnemyDistance.Mid)
            {
                if (EnemyCoverSearch($"{reason}.walk", weakEnemy, avoidBossFireLane: !pushOrdered) is AICoreActionResultStruct<BotLogicDecision, GClass26> walkCoverResult)
                {
                    return walkCoverResult;
                }

                return EnemySimpleSearch($"{reason}.walk");
            }

            if (pushOrdered)
            {
                return EnemySimpleSearch($"{reason}.orderedSearch");
            }

            return CreateBlockedEnemySearchDecision($"{reason}.farHold");
        }

        /// <summary>
        /// Replaces a boss-leash veto of direct run-to-enemy movement with a useful tactical step.
        /// The leash applies to the selected follower destination, not to the enemy anchor itself.
        /// </summary>
        private bool TryCreateBossLeashedSearchDecision(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            Vector3 bossPosition = GetBossPosition();
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (!IsFinite(bossPosition) ||
                !IsFinite(enemyAnchor) ||
                !FollowerCombatRegroupObjective.IsSameBossLevel(botOwner.Position, bossPosition))
            {
                return false;
            }

            float regroupTriggerDistance =
                CombatDistanceConfiguration.Instance.GetBossRegroupTriggerDistance(botOwner);
            float maxBossDistance = regroupTriggerDistance + WeakEnemyPushBossDistanceBuffer;

            RefreshShootCover();
            CustomNavigationPoint? shootCover = PointToShoot;
            bool shootCoverAlreadyReached = IsWithinCommittedCoverArrivalHoldDistance(shootCover);
            if (shootCoverAlreadyReached)
            {
                BlockPushCover(shootCover, goalEnemy, $"alreadyReached:{reason}.cover");
                if (committedCoverPoint?.Id == shootCover?.Id)
                {
                    ClearCommittedCover("boundedSearchCoverAlreadyReached");
                    nextCoverAcquireTime = 0f;
                }
            }

            if (!shootCoverAlreadyReached &&
                IsCoverUsable(shootCover) &&
                shootCover!.CanIShootToEnemy &&
                IsBossLeashedSearchDestination(shootCover.Position, bossPosition, maxBossDistance) &&
                TryCommitSelectedCombatCover(goalEnemy, shootCover, $"{reason}.cover"))
            {
                decision = CreateCommittedCoverMoveDecision();
                return true;
            }

            Vector3 bossToEnemy = enemyAnchor - bossPosition;
            bossToEnemy.y = 0f;
            float bossToEnemyDistance = bossToEnemy.magnitude;
            if (!IsUsableDistance(bossToEnemyDistance))
            {
                return false;
            }

            // A real shooting cover may use the existing tactical buffer. A blind no-cover step stays
            // just inside the actual regroup trigger so its arrival cannot immediately ping-pong back
            // into regroup solely because this search moved there.
            float boundedBossDistance = Mathf.Min(
                bossToEnemyDistance,
                Mathf.Max(1f, regroupTriggerDistance - BossLeashedSearchTriggerMargin));
            Vector3 searchPoint = bossPosition + bossToEnemy / bossToEnemyDistance * boundedBossDistance;
            searchPoint.y = botOwner.Position.y;

            Vector3 botToSearchPoint = searchPoint - botOwner.Position;
            botToSearchPoint.y = 0f;
            float maxStepDistance = CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius();
            if (botToSearchPoint.sqrMagnitude > maxStepDistance * maxStepDistance)
            {
                searchPoint = botOwner.Position + botToSearchPoint.normalized * maxStepDistance;
            }

            if (NavMesh.SamplePosition(searchPoint, out NavMeshHit hit, 8f, -1) &&
                IsBossLeashedSearchDestination(hit.position, bossPosition, maxBossDistance))
            {
                searchPoint = hit.position;
            }
            else
            {
                searchPoint = botOwner.Position;
            }

            decision = EnemySimpleSearchAt(searchPoint, $"{reason}.step");
            return true;
        }

        private static bool IsBossLeashedSearchDestination(
            Vector3 destination,
            Vector3 bossPosition,
            float maxBossDistance)
        {
            return IsFinite(destination) &&
                   FollowerCombatRegroupObjective.IsSameBossLevel(destination, bossPosition) &&
                   Vector3.Distance(destination, bossPosition) <= maxBossDistance;
        }

        private bool IsWithinCommittedCoverArrivalHoldDistance(CustomNavigationPoint? cover)
        {
            return cover != null &&
                   IsFinite(cover.Position) &&
                   (botOwner.Position - cover.Position).sqrMagnitude <=
                       CommittedCoverArrivalHoldDistance * CommittedCoverArrivalHoldDistance;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> CreateBlockedEnemySearchDecision(
            string reason,
            bool allowRegroupFallback = true)
        {
            if (!allowRegroupFallback || IsWithinBossRegroupTriggerDistance())
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.holdPosition,
                    reason);
            }

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                FollowerCombatRegroupObjective.ActivateRegroupReason);
        }

        private bool IsWithinBossRegroupTriggerDistance()
        {
            Vector3 bossPosition = GetBossPosition();
            if (!IsFinite(bossPosition) ||
                !FollowerCombatRegroupObjective.IsSameBossLevel(botOwner.Position, bossPosition))
            {
                return false;
            }

            float triggerDistance = CombatDistanceConfiguration.Instance.GetBossRegroupTriggerDistance(botOwner);
            float directDistance = Vector3.Distance(botOwner.Position, bossPosition);
            if (directDistance <= triggerDistance)
            {
                return true;
            }

            float bossDistance = GetBossNavDistance(bossPosition);
            return IsUsableDistance(bossDistance) && bossDistance <= triggerDistance;
        }

        public bool ShouldBlockWeakEnemyRushForBossDistance(EnemyInfo goalEnemy)
        {
            if (!IsWeakEnemyAutoPushRoleAllowed(goalEnemy))
            {
                return true;
            }

            return ShouldBlockUnseenPushForBossDistance(goalEnemy);
        }

        public bool ShouldBlockAutomaticSearchRushForBossDistance(EnemyInfo goalEnemy)
        {
            return ShouldBlockUnseenPushForBossDistance(goalEnemy);
        }

        private bool ShouldBlockUnseenPushForBossDistance(EnemyInfo goalEnemy)
        {
            if (goalEnemy.IsVisible || goalEnemy.CanShoot)
            {
                return false;
            }

            if (!IsUsableDistance(goalEnemy.Distance))
            {
                return true;
            }

            Vector3 bossPosition = GetBossPosition();
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (!IsFinite(bossPosition) || !IsFinite(enemyAnchor))
            {
                return true;
            }

            float bossDistance = GetBossNavDistance(bossPosition);
            float directBossDistance = Vector3.Distance(botOwner.Position, bossPosition);
            if (!IsUsableDistance(bossDistance))
            {
                bossDistance = directBossDistance;
            }
            else
            {
                bossDistance = Mathf.Max(bossDistance, directBossDistance);
            }

            float triggerDistance = CombatDistanceConfiguration.Instance.GetBossRegroupTriggerDistance(botOwner);
            if (bossDistance > triggerDistance + WeakEnemyPushBossDistanceBuffer)
            {
                return true;
            }

            float distanceToEnemyAnchor = Vector3.Distance(botOwner.Position, enemyAnchor);
            if (!IsUsableDistance(distanceToEnemyAnchor) ||
                distanceToEnemyAnchor > GetWeakEnemyPushMaxDistance())
            {
                return true;
            }

            Vector3 predictedBossOffset = enemyAnchor - bossPosition;
            return predictedBossOffset.sqrMagnitude >
                   (triggerDistance + WeakEnemyPushBossDistanceBuffer) *
                   (triggerDistance + WeakEnemyPushBossDistanceBuffer);
        }

        private static bool IsWeakEnemyAutoPushRoleAllowed(EnemyInfo goalEnemy)
        {
            WildSpawnType role = goalEnemy?.Person?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;
            return FollowerDeathEscapeResolver.GetRouteThreatRoleMultiplier(role) <= WeakEnemyPushMaxRoleThreatMultiplier;
        }

        private static bool IsUsableDistance(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value) &&
                   value > 0.1f &&
                   value < float.MaxValue * 0.5f;
        }



        public bool TryCreateTacticalCoverDecision(
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool preferPointToShoot = true,
            bool preferInbetween = false)
        {
            decision = default;
            CustomNavigationPoint? cover = preferPointToShoot && IsCoverUsable(PointToShoot)
                ? PointToShoot
                : null;

            cover ??= preferInbetween
                ? GetApproachableCover(inbetween: true) ?? GetApproachableCover()
                : GetApproachableCover();

            if (!IsCoverUsable(cover))
            {
                return false;
            }

            AssignCover(cover);
            botOwner.GoToSomePointData.SetPoint(cover!.Position);
            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPointTactical, reason);
            return true;
        }

        public bool TryCreateCoverMoveDecision(
            EnemyInfo goalEnemy,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool preferInbetween = false)
        {
            decision = default;
            CustomNavigationPoint? cover = IsCoverUsable(PointToShoot)
                ? PointToShoot
                : null;

            cover ??= preferInbetween
                ? GetApproachableCover(inbetween: true) ?? GetApproachableCover()
                : GetApproachableCover();

            if (!IsCoverUsable(cover))
            {
                return false;
            }

            AssignCover(cover);
            BotLogicDecision moveAction = SelectCommittedCoverMoveAction(goalEnemy);
            if (moveAction == BotLogicDecision.runToCover)
            {
                SetRunToCoverTactic(cover, reason);
            }
            else if (moveAction == (BotLogicDecision)CustomBotDecisions.attackRetreat)
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Protect);
            }
            else if (moveAction == BotLogicDecision.attackMoving ||
                     moveAction == BotLogicDecision.attackMovingWithSuppress)
            {
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            }

            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                moveAction,
                CreateMovementReason(reason, moveAction));
            return true;
        }

        public bool TryCreateBossCoverAttackMovingDecision(
            EnemyInfo goalEnemy,
            float searchRadius,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            ResetCommittedCover();
            ClearCommittedMovement();

            Vector3 bossPosition = GetBossPosition();
            float currentBossDistance = Vector3.Distance(botOwner.Position, bossPosition);
            float maxComeCoverBossDistance = Mathf.Max(
                0f,
                currentBossDistance - CombatComeBossCoverMinimumProgress);
            if (!TryFindBossCover(
                    goalEnemy,
                    bossPosition,
                    searchRadius,
                    maxComeCoverBossDistance,
                    out CustomNavigationPoint? bossCover) ||
                !IsCoverUsable(bossCover, true))
            {
                if (!TryGetBossApproachFallbackPoint(bossPosition, out Vector3 fallbackPoint))
                {
                    return false;
                }

                botOwner.GoToSomePointData.SetPoint(fallbackPoint);
                SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.goToPointTactical,
                    $"{reason}.fallbackPoint");
                return true;
            }

            BotLogicDecision action = BotLogicDecision.attackMoving;
            string movementReason = CreateMovementReason(reason, action);
            if (HasCombatCoverDestinationClaimConflict(bossCover))
            {
                return false;
            }

            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            CommitCover(bossCover, action, movementReason);
            AssignCover(bossCover);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(action, movementReason);
            return true;
        }

        private bool TryGetBossApproachFallbackPoint(Vector3 bossPosition, out Vector3 fallbackPoint)
        {
            const float BossApproachStopDistance = 1.5f;
            const float BossApproachMaxDistance = 2f;

            fallbackPoint = default;
            if (!IsFinite(bossPosition))
            {
                return false;
            }

            if (!NavMesh.SamplePosition(bossPosition, out NavMeshHit bossHit, BossApproachMaxDistance, NavMesh.AllAreas))
            {
                return false;
            }

            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(botOwner.Position, bossHit.position, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete ||
                path.corners == null ||
                path.corners.Length == 0)
            {
                return false;
            }

            Vector3 target = GetPointBackFromPathEnd(path.corners, BossApproachStopDistance);
            if (!NavMesh.SamplePosition(target, out NavMeshHit targetHit, 1f, NavMesh.AllAreas))
            {
                return false;
            }

            if ((targetHit.position - bossHit.position).sqrMagnitude > BossApproachMaxDistance * BossApproachMaxDistance)
            {
                target = GetPointBackFromPathEnd(path.corners, 1f);
                if (!NavMesh.SamplePosition(target, out targetHit, 1f, NavMesh.AllAreas) ||
                    (targetHit.position - bossHit.position).sqrMagnitude > BossApproachMaxDistance * BossApproachMaxDistance)
                {
                    return false;
                }
            }

            fallbackPoint = targetHit.position;
            return IsFinite(fallbackPoint);
        }

        private static Vector3 GetPointBackFromPathEnd(Vector3[] corners, float distanceFromEnd)
        {
            Vector3 target = corners[corners.Length - 1];
            float remaining = Mathf.Max(0f, distanceFromEnd);

            for (int i = corners.Length - 2; i >= 0 && remaining > 0f; i--)
            {
                Vector3 previous = corners[i];
                Vector3 segment = previous - target;
                float segmentLength = segment.magnitude;
                if (segmentLength <= 0.01f)
                {
                    target = previous;
                    continue;
                }

                if (segmentLength >= remaining)
                {
                    return target + segment / segmentLength * remaining;
                }

                remaining -= segmentLength;
                target = previous;
            }

            return target;
        }

        public bool TryCreateBossCommandTacticalPointDecision(
            Vector3 target,
            string reason,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (!IsFinite(target))
            {
                return false;
            }

            ResetCommittedCover();
            ClearCommittedMovement();
            botOwner.GoToSomePointData.SetPoint(target);
            SetCoverTactic(BotsGroup.BotCurrentTactic.Attack);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.goToPointTactical,
                reason);
            return true;
        }

        public static string CreateMovementReason(string baseReason, BotLogicDecision moveAction)
        {
            return moveAction switch
            {
                BotLogicDecision.runToCover => $"{baseReason}.runToCover",
                BotLogicDecision.runToEnemy => $"{baseReason}.runToEnemy",
                BotLogicDecision.goToEnemy => $"{baseReason}.goToEnemy",
                BotLogicDecision.goToPoint => $"{baseReason}.goToPoint",
                BotLogicDecision.goToPointTactical => $"{baseReason}.goToPointTactical",
                BotLogicDecision.attackMoving => $"{baseReason}.attackMoving",
                BotLogicDecision.attackMovingWithSuppress => $"{baseReason}.attackMovingWithSuppress",
                var decision when decision == (BotLogicDecision)CustomBotDecisions.attackRetreat => $"{baseReason}.attackRetreat",
                BotLogicDecision.suppressFire => $"{baseReason}.suppressFire",
                _ => baseReason,
            };
        }

        private void SetCover(CustomNavigationPoint? cover)
        {
            if (cover == null)
            {
                return;
            }

            botOwner.Memory.BotCurrentCoverInfo.Spotted();
            botOwner.Memory.BotCurrentCoverInfo.SetCover(cover, true);
        }

        public bool TryGetAllyEngagementEnemy(out string enemyProfileId, out Vector3 enemyPosition)
        {
            enemyProfileId = string.Empty;
            enemyPosition = Vector3.zero;

            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            if (boss.IsPlayerEngaging(out string playerEnemyProfileId, out Vector3 playerEnemyPosition) &&
                !string.IsNullOrEmpty(playerEnemyProfileId) &&
                IsFinite(playerEnemyPosition))
            {
                enemyProfileId = playerEnemyProfileId;
                enemyPosition = playerEnemyPosition;
                return true;
            }

            foreach (BotOwner followerBot in boss.Followers)
            {
                if (followerBot == null || followerBot == botOwner || followerBot.IsDead || followerBot.Memory?.GoalEnemy == null)
                {
                    continue;
                }

                EnemyInfo followerEnemy = followerBot.Memory.GoalEnemy;
                if (!followerEnemy.IsVisible || !followerEnemy.CanShoot || string.IsNullOrEmpty(followerEnemy.ProfileId))
                {
                    continue;
                }

                BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(followerBot);
                if (followerData == null || !followerData.IsBotActivelyEngaging(followerEnemy.ProfileId))
                {
                    continue;
                }

                enemyProfileId = followerEnemy.ProfileId;
                enemyPosition = followerEnemy.CurrPosition;
                return IsFinite(enemyPosition);
            }

            return false;
        }

        private EnemyInfo? GetTrackedEnemyByProfileId(string enemyProfileId)
        {
            if (string.IsNullOrEmpty(enemyProfileId) || botOwner.EnemiesController?.EnemyInfos == null)
            {
                return null;
            }

            foreach (var item in botOwner.EnemiesController.EnemyInfos)
            {
                if (item.Key?.ProfileId == enemyProfileId)
                {
                    return item.Value;
                }
            }

            return null;
        }

        private bool TryPromoteDogFightState(EnemyInfo? goalEnemy, out BotDogFightStatus dogFightState)
        {
            dogFightState = botOwner.DogFight?.DogFightState ?? BotDogFightStatus.none;
            if (!HasFreshVisibleContact(goalEnemy, CloseThreatRecentSeenSeconds))
            {
                return false;
            }

            BotDogFight? dogFight = botOwner.DogFight;
            if (dogFight == null)
            {
                return false;
            }

            if (goalEnemy.Distance >= 18f)
            {
                return false;
            }

            if (CanUseDogFightNow(goalEnemy) && dogFight.method_1(out _))
            {
                dogFight.DogFightState = BotDogFightStatus.dogFight;
                dogFightState = BotDogFightStatus.dogFight;
                return true;
            }

            dogFight.DogFightState = BotDogFightStatus.shootFromPlace;
            dogFightState = BotDogFightStatus.shootFromPlace;
            return true;
        }

        private Vector3 GetEnemyAnchorOrFallback(EnemyInfo? enemyInfo, Vector3 fallback)
        {
            if (enemyInfo != null)
            {
                Vector3 anchor = GetEnemyAnchor(enemyInfo);
                if (IsFinite(anchor))
                {
                    return anchor;
                }
            }

            return fallback;
        }

        private float ScoreSupportEnemy(EnemyInfo? enemyInfo, Vector3 fallbackPosition, bool preferBackline)
        {
            if (!HasActiveCombatEnemy(enemyInfo))
            {
                return float.MinValue;
            }

            float score = 0f;
            Vector3 enemyAnchor = GetEnemyAnchorOrFallback(enemyInfo, fallbackPosition);
            float distance = IsFinite(enemyAnchor)
                ? Vector3.Distance(botOwner.Position, enemyAnchor)
                : float.MaxValue;

            if (enemyInfo!.IsVisible)
            {
                score += 5f;
            }

            if (enemyInfo.CanShoot)
            {
                score += 4f;
            }

            if (botOwner.Memory?.GoalEnemy != null &&
                string.Equals(botOwner.Memory.GoalEnemy.ProfileId, enemyInfo.ProfileId, StringComparison.Ordinal))
            {
                score += 2.5f;
            }

            float sinceLastSeen = Time.time - enemyInfo.PersonalLastSeenTime;
            if (sinceLastSeen <= 2.5f)
            {
                score += 2f;
            }

            if (distance < float.MaxValue)
            {
                score -= Mathf.Clamp(distance / 25f, 0f, 4f);
            }

            if (preferBackline && distance < CombatDistanceConfiguration.Instance.GetCloseQuarterDistance())
            {
                score -= 3f;
            }

            return score;
        }

        private bool TryGetSupportCoverForEnemy(
            EnemyInfo supportEnemy,
            out CustomNavigationPoint? supportCover,
            out float supportCoverNavDistance,
            float maxSearchRadius = 35f)
        {
            supportCover = null;
            supportCoverNavDistance = float.MaxValue;
            Vector3 enemyPosition = GetEnemyAnchorOrFallback(supportEnemy, Vector3.zero);
            if (!IsFinite(enemyPosition))
            {
                return false;
            }

            if (!TryGetSupportCover(enemyPosition, maxSearchRadius, out supportCover, out supportCoverNavDistance, out _))
            {
                return false;
            }

            bool strict = GetFollowerTactic() == FollowerCombatTactic.Marksman;
            if (!IsCoverSafeFromAlternateThreats(supportCover, supportEnemy.ProfileId, strict))
            {
                supportCover = null;
                supportCoverNavDistance = float.MaxValue;
                return false;
            }

            return true;
        }

        private bool IsCoverSafeFromAlternateThreats(CustomNavigationPoint? cover, string? primaryEnemyProfileId, bool strict)
        {
            if (!IsCoverUsable(cover))
            {
                return false;
            }

            if (botOwner.EnemiesController?.EnemyInfos == null)
            {
                return true;
            }

            foreach (EnemyInfo enemyInfo in botOwner.EnemiesController.EnemyInfos.Values)
            {
                if (!HasActiveCombatEnemy(enemyInfo) ||
                    string.Equals(enemyInfo.ProfileId, primaryEnemyProfileId, StringComparison.Ordinal))
                {
                    continue;
                }

                Vector3 enemyAnchor = GetEnemyAnchor(enemyInfo);
                if (!IsFinite(enemyAnchor))
                {
                    continue;
                }

                bool dangerousThreat =
                    enemyInfo.CanShoot ||
                    enemyInfo.IsVisible ||
                    Time.time - enemyInfo.PersonalLastSeenTime <= 3f;
                if (!dangerousThreat)
                {
                    continue;
                }

                if (!cover!.CanIHideFromPos(0f, true, false, enemyAnchor))
                {
                    if (strict)
                    {
                        return false;
                    }

                    float primaryDistance = botOwner.Memory?.GoalEnemy != null &&
                                            IsFinite(GetEnemyAnchor(botOwner.Memory.GoalEnemy))
                        ? Vector3.Distance(cover.Position, GetEnemyAnchor(botOwner.Memory.GoalEnemy))
                        : float.MaxValue;
                    float alternateDistance = Vector3.Distance(cover.Position, enemyAnchor);
                    if (alternateDistance <= primaryDistance + 5f)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsSupportPositionBehindBossLine(Vector3 candidate, Vector3 bossPosition, Vector3 enemyAnchor)
        {
            if (!IsFinite(candidate) || !IsFinite(bossPosition) || !IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 bossToEnemy = enemyAnchor - bossPosition;
            bossToEnemy.y = 0f;
            if (bossToEnemy.sqrMagnitude < 0.01f)
            {
                return true;
            }

            bossToEnemy.Normalize();
            Vector3 bossToCandidate = candidate - bossPosition;
            bossToCandidate.y = 0f;
            float forward = Vector3.Dot(bossToCandidate, bossToEnemy);
            return forward <= 1.5f;
        }

        /// <summary>
        /// Treats very recent visible contacts as an immediate-fire window so followers do not hesitate
        /// before taking their first shot.
        /// </summary>
        public bool ShouldShootImmediately()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            bool recentVisibleShoot =
                goalEnemy != null &&
                goalEnemy.IsVisible &&
                goalEnemy.CanShoot &&
                Time.time - goalEnemy.PersonalSeenTime < 1.5f;
            bool shootNow = ((goalEnemy != null && goalEnemy.Distance < botOwner.Settings.FileSettings.Shoot.SHOOT_IMMEDIATELY_DIST) ||
                             botOwner.BotsGroup.AnyBodyShootImmediately) &&
                            goalEnemy != null &&
                            goalEnemy.IsVisible &&
                            goalEnemy.CanShoot &&
                            Time.time - goalEnemy.AddTime < 5f;

            bool launcherActive = botOwner.WeaponManager.UnderbarrelLauncherController.IsActive;
            botOwner.BotsGroup.AnyBodyShootImmediately = shootNow || recentVisibleShoot || launcherActive;
            return botOwner.BotsGroup.AnyBodyShootImmediately;
        }

        /// <summary>
        /// A committed cover run should only break for immediate fire if the visible contact is stable
        /// enough to be real, not just a one-frame LOS flicker while crossing geometry.
        /// </summary>
        public bool ShouldBreakRunToCoverForImmediateFire()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (WasHitRecently(botOwner, 0.5f) && HasActiveCombatEnemy(goalEnemy))
            {
                return true;
            }

            if (!HasActiveCombatEnemy(goalEnemy) || !goalEnemy.IsVisible || !goalEnemy.CanShoot)
            {
                return IsEnemyActivelyThreateningMe(goalEnemy, CloseThreatAdvanceBreakDistance, CloseThreatRecentSeenSeconds);
            }

            // While a committed cover run is still inside its initial lock window, treat the move as
            // sticky unless the bot was actually hit. This avoids SAIN-unlike flicker breaks where a
            // transient LOS blip peels the follower off the chosen cover before arrival.
            if (HasCommittedCover() &&
                !IsBotInCommittedCover() &&
                !IsCommittedCoverLockExpired)
            {
                return false;
            }

            if (Enemy.Distance(goalEnemy) > Enemy.EnemyDistance.Close &&
                Time.time - goalEnemy.PersonalSeenTime < StableVisibleImmediateFireSeconds)
            {
                return false;
            }

            if (!botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            if (shootPoint == null)
            {
                return false;
            }

            return Utils.Utils.CanShootToTarget(shootPoint, botOwner.WeaponRoot.position, botOwner.LookSensor.Mask, false);
        }

        /// <summary>
        /// Returns true if the currently equipped weapon supports full-auto.
        /// </summary>
        public bool IsCurrentWeaponAutomatic()
        {
            Weapon? activeWeapon = botOwner?.WeaponManager?.ShootController?.Item;
            return IsAutomaticWeapon(activeWeapon);
        }

        /// <summary>
        /// Suppression is only useful with enough fire volume. Single-shot/small-mag weapons should
        /// keep using normal shoot/reposition decisions instead of wasting precise ammo into cover.
        /// </summary>
        public bool CanCurrentWeaponSuppress()
        {
            Weapon? activeWeapon = botOwner?.WeaponManager?.ShootController?.Item;
            return IsSuppressCapableWeapon(activeWeapon);
        }

        public static bool IsSuppressCapableWeapon(Weapon? weapon)
        {
            if (weapon == null)
            {
                return false;
            }

            if (IsAutomaticWeapon(weapon))
            {
                return true;
            }

            MagazineItemClass? magazine = weapon.GetCurrentMagazine();
            return magazine?.MaxCount >= 25;
        }

        /// <summary>
        /// Returns true if the bot already has a loaded automatic weapon equipped or can swap to a
        /// loaded automatic second primary for close combat.
        /// </summary>
        public bool HasAutomaticCloseCombatWeaponAvailable()
        {
            if (botOwner == null)
            {
                return false;
            }

            if (TryGetSelectedLoadedAutomaticPrimary(out _))
            {
                return true;
            }

            BotWeaponSelector? selector = botOwner.WeaponManager?.Selector;
            return selector?.CanChangeToSecondWeapons == true &&
                   HasLoadedAutomaticSecondaryForPush();
        }

        /// <summary>
        /// True only after a loaded automatic primary is both selected and active. A slot-change
        /// request is not enough: movement that depends on the close-combat weapon must wait for
        /// EFT's asynchronous selector callback and weapon-ready state.
        /// </summary>
        public bool IsAutomaticCloseCombatWeaponReady()
        {
            BotWeaponManager? weaponManager = botOwner?.WeaponManager;
            BotWeaponSelector? selector = weaponManager?.Selector;
            if (selector == null ||
                selector.IsChanging ||
                !selector.IsWeaponReady ||
                weaponManager?.IsWeaponReady == false ||
                !TryGetSelectedLoadedAutomaticPrimary(out Weapon? selectedWeapon))
            {
                return false;
            }

            Weapon? activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
            return IsSameWeapon(activeWeapon, selectedWeapon);
        }

        /// <summary>
        /// Requests the eligible automatic second primary. A true result means this caller owns
        /// one accepted asynchronous switch request; already-selected but unready weapons are not
        /// reported as a new request.
        /// </summary>
        public bool TryRequestAutomaticSecondaryForCloseCombat()
        {
            BotWeaponSelector? selector = botOwner?.WeaponManager?.Selector;
            if (selector == null || !HasAutomaticCloseCombatWeaponAvailable())
            {
                return false;
            }

            if (TryGetSelectedLoadedAutomaticPrimary(out _))
            {
                return false;
            }

            if (selector.IsChanging)
            {
                return false;
            }

            return selector.CanChangeToSecondWeapons && selector.ChangeToSecond();
        }

        /// <summary>
        /// Ordered marksman suppression is explicitly tied to the eligible automatic second
        /// primary, not merely to any weapon capable of sustained fire.
        /// </summary>
        public bool IsEligibleAutomaticSecondarySelectedAndReady()
        {
            BotWeaponManager? weaponManager = botOwner?.WeaponManager;
            BotWeaponSelector? selector = weaponManager?.Selector;
            Weapon? firstPrimary = GetFirstPrimaryWeapon(botOwner);
            Weapon? secondPrimary = GetSecondPrimaryWeapon(botOwner);
            if (selector == null ||
                selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon ||
                selector.IsChanging ||
                !selector.IsWeaponReady ||
                weaponManager?.IsWeaponReady == false ||
                !IsAutomaticSecondaryUsableForPush(firstPrimary, secondPrimary))
            {
                return false;
            }

            Weapon? activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
            return IsSameWeapon(activeWeapon, secondPrimary);
        }

        /// <summary>
        /// True only when EFT's selector and weapon manager have finished any in-flight hands
        /// transition. LastEquipmentSlot is not authoritative until this boundary is reached.
        /// </summary>
        public bool IsWeaponSelectionSettledForAutomaticSecondaryRequest()
        {
            BotWeaponManager? weaponManager = botOwner?.WeaponManager;
            BotWeaponSelector? selector = weaponManager?.Selector;
            return selector != null &&
                   !selector.IsChanging &&
                   selector.IsWeaponReady &&
                   weaponManager != null &&
                   weaponManager.IsWeaponReady;
        }

        /// <summary>
        /// Issues one switch to the eligible automatic second primary used by an automatic-secondary
        /// suppression order. This returns true only for a request accepted at a settled selector
        /// boundary; callers own and wait on that one asynchronous request.
        /// </summary>
        public bool TryRequestEligibleAutomaticSecondary()
        {
            BotWeaponSelector? selector = botOwner?.WeaponManager?.Selector;
            Weapon? firstPrimary = GetFirstPrimaryWeapon(botOwner);
            Weapon? secondPrimary = GetSecondPrimaryWeapon(botOwner);
            if (selector == null ||
                !IsWeaponSelectionSettledForAutomaticSecondaryRequest() ||
                !IsAutomaticSecondaryUsableForPush(firstPrimary, secondPrimary))
            {
                return false;
            }

            if (selector.LastEquipmentSlot == EquipmentSlot.SecondPrimaryWeapon)
            {
                return false;
            }

            if (!selector.CanChangeToSecondWeapons)
            {
                return false;
            }

            return selector.ChangeToSecond();
        }

        private bool TryGetSelectedLoadedAutomaticPrimary(out Weapon? weapon)
        {
            weapon = null;
            BotWeaponSelector? selector = botOwner?.WeaponManager?.Selector;
            if (selector == null)
            {
                return false;
            }

            if (selector.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon)
            {
                weapon = GetFirstPrimaryWeapon(botOwner);
                return IsAutomaticWeapon(weapon) && CountLoadedRounds(weapon) > 0;
            }

            if (selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon)
            {
                return false;
            }

            Weapon? firstPrimary = GetFirstPrimaryWeapon(botOwner);
            Weapon? secondPrimary = GetSecondPrimaryWeapon(botOwner);
            if (!IsAutomaticSecondaryUsableForPush(firstPrimary, secondPrimary))
            {
                return false;
            }

            weapon = secondPrimary;
            return true;
        }

        /// <summary>
        /// Marksman close-quarter helper: if the current weapon is not full-auto and the bot has
        /// a loaded full-auto secondary weapon, switch to it.
        /// </summary>
        public bool TrySwitchToAutomaticSecondaryForCloseCombat()
        {
            return TrySwitchToAutomaticSecondary(requireCloseQuarter: true, requireShotgunPrimary: false, out _);
        }

        /// <summary>
        /// Rifleman push helper: if the primary is not full-auto and the bot has a loaded full-auto
        /// secondary weapon, switch to it for the active push.
        /// </summary>
        public bool TrySwitchToAutomaticSecondaryForPush()
        {
            return TrySwitchToAutomaticSecondary(requireCloseQuarter: false, requireShotgunPrimary: false, out _);
        }

        public bool TrySwitchToAutomaticSecondaryForPush(out bool changedToSecondary)
        {
            return TrySwitchToAutomaticSecondary(requireCloseQuarter: false, requireShotgunPrimary: false, out changedToSecondary);
        }

        public bool HasLoadedAutomaticSecondaryForPush()
        {
            return HasLoadedAutomaticSecondaryForPush(botOwner);
        }

        public static bool HasLoadedAutomaticSecondaryForPush(BotOwner? owner)
        {
            Weapon? primaryWeapon = GetFirstPrimaryWeapon(owner);
            Weapon? secondaryWeapon = GetSecondPrimaryWeapon(owner);
            return IsAutomaticSecondaryUsableForPush(primaryWeapon, secondaryWeapon);
        }

        public bool TrySwitchToAutomaticSecondaryForShotgunDistance()
        {
            return TrySwitchToAutomaticSecondary(requireCloseQuarter: false, requireShotgunPrimary: true, out _);
        }

        public bool TrySwitchToAutomaticSecondaryForShotgunDistance(out bool changedToSecondary)
        {
            return TrySwitchToAutomaticSecondary(requireCloseQuarter: false, requireShotgunPrimary: true, out changedToSecondary);
        }

        public bool IsUsingAutomaticSecondaryOverNonAutomaticPrimary()
        {
            return IsUsingAutomaticSecondaryOverNonAutomaticPrimary(botOwner);
        }

        public bool TrySwitchBackToPrimaryFromAutomaticSecondary()
        {
            if (IsSelectedSecondPrimaryOverNonAutomaticPrimary(botOwner) &&
                botOwner?.WeaponManager?.Selector != null)
            {
                return botOwner.WeaponManager.Selector.TryChangeToMain();
            }

            return false;
        }

        public static bool IsAutomaticSecondaryPushReason(string? reason)
        {
            return reason != null &&
                   reason.IndexOf("push", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public bool HasShotgunPrimaryWithLoadedAutomaticSecondary()
        {
            return HasShotgunPrimaryWithLoadedAutomaticSecondary(botOwner);
        }

        public bool HasUsableEquippedGrenadeLauncher()
        {
            return HasUsableEquippedGrenadeLauncher(botOwner);
        }

        public bool CanCurrentWeaponSuppressOrUseGrenadeLauncher()
        {
            if (IsPendingLauncherPrimaryFallbackWeaponSelected())
            {
                return IsSuppressCapableWeapon(GetFirstPrimaryWeapon(botOwner));
            }

            return CanCurrentWeaponSuppress() || HasUsableEquippedGrenadeLauncher();
        }

        public bool IsUsingAutomaticSecondaryOverShotgunPrimary()
        {
            return IsUsingAutomaticSecondaryOverShotgunPrimary(botOwner);
        }

        public bool ShouldDisableAutomaticSecondaryForEmptyReload()
        {
            BotWeaponManager? weaponManager = botOwner?.WeaponManager;
            if (weaponManager == null ||
                !IsShotgunWeapon(GetFirstPrimaryWeapon(botOwner)) ||
                !IsAutomaticWeapon(GetSecondPrimaryWeapon(botOwner)))
            {
                return false;
            }

            BotWeaponInfo? secondaryInfo = weaponManager.SecondWeaponInfo;
            bool selectedSecondary = weaponManager.Selector?.LastEquipmentSlot == EquipmentSlot.SecondPrimaryWeapon;
            int loadedSecondaryAmmo = secondaryInfo?.BulletCount ??
                                      GetSecondPrimaryWeapon(botOwner)?.GetCurrentMagazine()?.Cartridges?.Count ??
                                      0;
            bool needsReload = loadedSecondaryAmmo <= 0 ||
                               selectedSecondary &&
                               (weaponManager.Reload == null ||
                                !weaponManager.HaveBullets ||
                                weaponManager.Reload.BulletCount <= 0);
            if (!needsReload)
            {
                return false;
            }

            return secondaryInfo?.CheckHaveAmmoForReload() != true;
        }

        public static bool IsUsingAutomaticSecondaryOverShotgunPrimary(BotOwner? owner)
        {
            return IsUsingAutomaticSecondaryOverNonAutomaticPrimary(owner) &&
                   IsShotgunWeapon(GetFirstPrimaryWeapon(owner));
        }

        public static bool HasShotgunPrimaryWithLoadedAutomaticSecondary(BotOwner? owner)
        {
            Weapon? primaryWeapon = GetFirstPrimaryWeapon(owner);
            Weapon? secondaryWeapon = GetSecondPrimaryWeapon(owner);
            return IsShotgunWeapon(primaryWeapon) &&
                   HasLoadedAutomaticSecondPrimary(secondaryWeapon) &&
                   IsAutomaticSecondaryPenetrationAcceptable(primaryWeapon, secondaryWeapon);
        }

        public static bool HasUsableEquippedGrenadeLauncher(BotOwner? owner)
        {
            return GetEquippedGrenadeLauncher(owner, out _) != null;
        }

        public static bool IsUsingAutomaticSecondaryOverNonAutomaticPrimary(BotOwner? owner)
        {
            var selector = owner?.WeaponManager?.Selector;
            if (selector == null ||
                selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon)
            {
                return false;
            }

            if (!HasLoadedAutomaticSecondPrimary(owner))
            {
                return false;
            }

            Weapon? primaryWeapon = GetFirstPrimaryWeapon(owner);
            return primaryWeapon != null && !IsAutomaticWeapon(primaryWeapon);
        }

        private static bool IsSelectedSecondPrimaryOverNonAutomaticPrimary(BotOwner? owner)
        {
            var selector = owner?.WeaponManager?.Selector;
            if (selector == null ||
                selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon)
            {
                return false;
            }

            Weapon? primaryWeapon = GetFirstPrimaryWeapon(owner);
            return primaryWeapon != null && !IsAutomaticWeapon(primaryWeapon);
        }

        public static bool IsSelectedSecondPrimaryOverShotgunPrimary(BotOwner? owner)
        {
            return IsSelectedSecondPrimaryOverNonAutomaticPrimary(owner) &&
                   IsShotgunWeapon(GetFirstPrimaryWeapon(owner)) &&
                   IsAutomaticWeapon(GetSecondPrimaryWeapon(owner));
        }

        private static bool HasLoadedAutomaticSecondPrimary(BotOwner? owner)
        {
            return HasLoadedAutomaticSecondPrimary(GetSecondPrimaryWeapon(owner));
        }

        private static bool HasLoadedAutomaticSecondPrimary(Weapon? secondaryWeapon)
        {
            return IsAutomaticWeapon(secondaryWeapon) &&
                   secondaryWeapon?.GetCurrentMagazine()?.Cartridges?.Count > 0;
        }

        private static bool IsAutomaticSecondaryUsableForPush(Weapon? primaryWeapon, Weapon? secondaryWeapon)
        {
            return primaryWeapon != null &&
                   !IsAutomaticWeapon(primaryWeapon) &&
                   HasLoadedAutomaticSecondPrimary(secondaryWeapon) &&
                   IsAutomaticSecondaryPenetrationAcceptable(primaryWeapon, secondaryWeapon);
        }

        private bool IsAutomaticSecondaryUsableForPushCached(Weapon? primaryWeapon, Weapon? secondaryWeapon)
        {
            return primaryWeapon != null &&
                   !IsAutomaticWeapon(primaryWeapon) &&
                   HasLoadedAutomaticSecondPrimary(secondaryWeapon) &&
                   IsAutomaticSecondaryPenetrationAcceptableCached(primaryWeapon, secondaryWeapon);
        }

        internal static bool HasUsableFirstPrimaryGrenadeLauncher(BotOwner? owner)
        {
            Weapon? firstPrimary = GetFirstPrimaryWeapon(owner);
            return IsUsableGrenadeLauncher(firstPrimary);
        }

        internal static Weapon? GetEquippedGrenadeLauncher(BotOwner? owner, out EquipmentSlot slot)
        {
            Weapon? firstPrimary = GetFirstPrimaryWeapon(owner);
            if (IsUsableGrenadeLauncher(firstPrimary))
            {
                slot = EquipmentSlot.FirstPrimaryWeapon;
                return firstPrimary;
            }

            Weapon? secondPrimary = GetSecondPrimaryWeapon(owner);
            if (IsUsableGrenadeLauncher(secondPrimary))
            {
                slot = EquipmentSlot.SecondPrimaryWeapon;
                return secondPrimary;
            }

            slot = EquipmentSlot.Scabbard;
            return null;
        }

        internal static bool TrySelectEquippedGrenadeLauncher(
            BotOwner? owner,
            out bool changedToLauncher,
            out EquipmentSlot launcherSlot)
        {
            changedToLauncher = false;
            Weapon? launcher = GetEquippedGrenadeLauncher(owner, out launcherSlot);
            BotWeaponSelector? selector = owner?.WeaponManager?.Selector;
            if (launcher == null || selector == null)
            {
                return false;
            }

            if (selector.LastEquipmentSlot == launcherSlot)
            {
                return true;
            }

            if (selector.IsChanging)
            {
                return false;
            }

            changedToLauncher = launcherSlot == EquipmentSlot.FirstPrimaryWeapon
                ? selector.TryChangeToMain()
                : selector.CanChangeToSecondWeapons && selector.ChangeToSecond();
            return changedToLauncher;
        }

        internal static bool IsEquippedGrenadeLauncherSelectedAndActive(BotOwner? owner)
        {
            Weapon? launcher = GetEquippedGrenadeLauncher(owner, out EquipmentSlot launcherSlot);
            BotWeaponSelector? selector = owner?.WeaponManager?.Selector;
            Weapon? activeWeapon = owner?.WeaponManager?.ShootController?.Item ??
                                   owner?.WeaponManager?.CurrentWeapon;
            return launcher != null &&
                   selector?.LastEquipmentSlot == launcherSlot &&
                   IsSameWeapon(activeWeapon, launcher);
        }

        private static bool IsUsableGrenadeLauncher(Weapon? weapon)
        {
            return IsGrenadeLauncherWeapon(weapon) &&
                   (CountLoadedRounds(weapon) > 0 || !IsSingleUseLauncherWeapon(weapon));
        }

        internal static Weapon? GetFirstPrimaryWeapon(BotOwner? owner)
        {
            Player? player = owner?.GetPlayer;
            return player?.InventoryController?.Inventory?.Equipment
                ?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon;
        }

        internal static Weapon? GetSecondPrimaryWeapon(BotOwner? owner)
        {
            var selector = owner?.WeaponManager?.Selector;
            Weapon? secondaryWeapon = selector?.SecondPrimaryWeaponItem as Weapon;
            if (secondaryWeapon != null)
            {
                return secondaryWeapon;
            }

            Player? player = owner?.GetPlayer;
            return player?.InventoryController?.Inventory?.Equipment
                ?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem as Weapon;
        }

        private static bool IsSameWeapon(Weapon? left, Weapon? right)
        {
            return left != null &&
                   right != null &&
                   (ReferenceEquals(left, right) ||
                    (!string.IsNullOrEmpty(left.Id) && string.Equals(left.Id, right.Id, StringComparison.Ordinal)));
        }

        private bool TrySwitchToAutomaticSecondary(
            bool requireCloseQuarter,
            bool requireShotgunPrimary,
            out bool changedToSecondary)
        {
            changedToSecondary = false;

            if (botOwner == null)
            {
                return false;
            }

            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                return false;
            }

            if (requireCloseQuarter &&
                goalEnemy.Distance > CombatDistanceConfiguration.Instance.GetCloseQuarterDistance())
            {
                return false;
            }

            if (!requireShotgunPrimary && IsCurrentWeaponAutomatic())
            {
                return true;
            }

            var selector = botOwner?.WeaponManager?.Selector;
            if (selector == null ||
                !selector.CanChangeToSecondWeapons ||
                selector.SecondPrimaryWeaponItem == null)
            {
                return false;
            }

            Weapon? secondaryWeapon = GetSecondPrimaryWeapon(botOwner);
            if (!HasLoadedAutomaticSecondPrimary(secondaryWeapon))
            {
                return false;
            }

            Weapon? primaryWeapon = GetFirstPrimaryWeapon(botOwner);
            if (primaryWeapon == null || IsAutomaticWeapon(primaryWeapon))
            {
                return false;
            }

            if (requireShotgunPrimary && !IsShotgunWeapon(primaryWeapon))
            {
                return false;
            }

            if (!IsAutomaticSecondaryUsableForPushCached(primaryWeapon, secondaryWeapon))
            {
                return false;
            }

            if (selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon)
            {
                changedToSecondary = selector.ChangeToSecond();
                return changedToSecondary;
            }

            return true;
        }

        private static bool IsAutomaticSecondaryPenetrationAcceptable(Weapon? primaryWeapon, Weapon? secondaryWeapon)
        {
            if (!TryGetCurrentAmmoPenetration(primaryWeapon, out int primaryPenetration) ||
                !TryGetCurrentAmmoPenetration(secondaryWeapon, out int secondaryPenetration))
            {
                return false;
            }

            return primaryPenetration - secondaryPenetration <= AutomaticSecondaryMaxPenetrationDeficit;
        }

        private static bool TryGetCurrentAmmoPenetration(Weapon? weapon, out int penetrationPower)
        {
            penetrationPower = 0;
            if (!TryBuildLoadedAmmoProfile(weapon, out AutoPushAmmoProfile ammoProfile))
            {
                return false;
            }

            penetrationPower = ammoProfile.PenetrationPower;
            return true;
        }

        private bool TryGetCachedAmmoPenetration(Weapon? weapon, out int penetrationPower)
        {
            penetrationPower = 0;
            if (!TryBuildLoadedAmmoProfileCached(weapon, out AutoPushAmmoProfile ammoProfile))
            {
                return false;
            }

            penetrationPower = ammoProfile.PenetrationPower;
            return true;
        }

        private bool IsAutomaticSecondaryPenetrationAcceptableCached(Weapon? primaryWeapon, Weapon? secondaryWeapon)
        {
            if (!TryGetCachedAmmoPenetration(primaryWeapon, out int primaryPenetration) ||
                !TryGetCachedAmmoPenetration(secondaryWeapon, out int secondaryPenetration))
            {
                return false;
            }

            return primaryPenetration - secondaryPenetration <= AutomaticSecondaryMaxPenetrationDeficit;
        }

        private bool TryBuildLoadedAmmoProfileCached(Weapon? weapon, out AutoPushAmmoProfile ammoProfile)
        {
            ammoProfile = default;
            if (weapon == null)
            {
                return false;
            }

            string cacheKey = weapon.Id ?? string.Empty;
            string signature = BuildAmmoProfileSignature(weapon);
            if (!string.IsNullOrEmpty(cacheKey) &&
                ammoProfileCache.TryGetValue(cacheKey, out CachedAmmoProfile cached) &&
                string.Equals(cached.Signature, signature, StringComparison.Ordinal) &&
                Time.time - cached.CachedAt <= AmmoProfileCacheMaxAgeSeconds)
            {
                ammoProfile = cached.Profile;
                return true;
            }

            if (!TryBuildLoadedAmmoProfile(weapon, out ammoProfile))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(cacheKey))
            {
                ammoProfileCache[cacheKey] = new CachedAmmoProfile(signature, ammoProfile, Time.time);
            }

            return true;
        }

        private static string BuildAmmoProfileSignature(Weapon weapon)
        {
            MagazineItemClass? magazine = weapon.GetCurrentMagazine();
            StackSlot cartridges = magazine?.Cartridges;
            Item topCartridge = cartridges?.Last;

            string signature =
                $"{weapon.Id}|{magazine?.Id}|{cartridges?.Count ?? 0}|{topCartridge?.TemplateId}|{topCartridge?.StackObjectsCount ?? 0}";

            if (weapon.Chambers == null)
            {
                return signature;
            }

            for (int i = 0; i < weapon.Chambers.Length; i++)
            {
                Item chamberItem = weapon.Chambers[i]?.ContainedItem;
                signature += $"|c{i}:{chamberItem?.TemplateId}:{chamberItem?.StackObjectsCount ?? 0}";
            }

            return signature;
        }

        private static bool TryBuildLoadedAmmoProfile(Weapon? weapon, out AutoPushAmmoProfile ammoProfile)
        {
            ammoProfile = default;
            if (weapon == null)
            {
                return false;
            }

            MagazineItemClass? magazine = weapon.GetCurrentMagazine();
            int magazineCapacity = magazine?.MaxCount ?? weapon.GetMaxMagazineCount();
            if (TryBuildAveragedAmmoProfile(weapon, magazine, magazineCapacity, out ammoProfile))
            {
                return true;
            }

            AmmoTemplate? ammoTemplate = weapon.CurrentAmmoTemplate;
            if (ammoTemplate == null)
            {
                return false;
            }

            ammoProfile = new AutoPushAmmoProfile(
                ammoTemplate.PenetrationPower,
                ammoTemplate.ArmorDamage,
                ammoTemplate.Caliber,
                magazineCapacity);
            return true;
        }

        private static bool TryBuildAveragedAmmoProfile(
            Weapon weapon,
            MagazineItemClass? magazine,
            int magazineCapacity,
            out AutoPushAmmoProfile ammoProfile)
        {
            ammoProfile = default;
            float penetrationTotal = 0f;
            float armorDamageTotal = 0f;
            int ammoCount = 0;
            string? caliber = null;

            if (weapon.Chambers != null)
            {
                for (int i = 0; i < weapon.Chambers.Length; i++)
                {
                    if (weapon.Chambers[i]?.ContainedItem is AmmoItemClass chamberAmmo)
                    {
                        AddAmmoTemplate(chamberAmmo.AmmoTemplate, 1, ref penetrationTotal, ref armorDamageTotal, ref ammoCount, ref caliber);
                    }
                }
            }

            if (magazine?.Cartridges?.Items != null)
            {
                foreach (Item item in magazine.Cartridges.Items)
                {
                    if (item is AmmoItemClass ammo)
                    {
                        AddAmmoTemplate(
                            ammo.AmmoTemplate,
                            Math.Max(1, ammo.StackObjectsCount),
                            ref penetrationTotal,
                            ref armorDamageTotal,
                            ref ammoCount,
                            ref caliber);
                    }
                }
            }

            if (ammoCount <= 0)
            {
                return false;
            }

            ammoProfile = new AutoPushAmmoProfile(
                Mathf.RoundToInt(penetrationTotal / ammoCount),
                Mathf.RoundToInt(armorDamageTotal / ammoCount),
                caliber,
                magazineCapacity);
            return true;
        }

        private static void AddAmmoTemplate(
            AmmoTemplate? ammoTemplate,
            int count,
            ref float penetrationTotal,
            ref float armorDamageTotal,
            ref int ammoCount,
            ref string? caliber)
        {
            if (ammoTemplate == null || count <= 0)
            {
                return;
            }

            penetrationTotal += ammoTemplate.PenetrationPower * count;
            armorDamageTotal += ammoTemplate.ArmorDamage * count;
            ammoCount += count;
            if (string.IsNullOrEmpty(caliber))
            {
                caliber = ammoTemplate.Caliber;
            }
        }

        public void TrySwitchBackToPrimaryAtRange(EnemyInfo goalEnemy, Enemy.EnemyDistance minDistance)
        {
            if (Enemy.Distance(goalEnemy) >= minDistance &&
                botOwner.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon)
            {
                botOwner.WeaponManager.Selector.TryChangeToMain();
            }
        }

        public bool IsHoldingPrimaryAtRange(EnemyInfo goalEnemy, Enemy.EnemyDistance minDistance)
        {
            return botOwner.WeaponManager.Selector.LastEquipmentSlot == EquipmentSlot.FirstPrimaryWeapon &&
                   Enemy.Distance(goalEnemy) > minDistance;
        }

        public bool IsCommittedCoverRetreatingFromEnemy(EnemyInfo goalEnemy)
        {
            return IsRetreatCover(goalEnemy, committedCoverPoint);
        }

        internal static bool IsAutomaticWeapon(Weapon? weapon)
        {
            return weapon != null &&
                   weapon.WeapFireType != null &&
                   System.Array.IndexOf(weapon.WeapFireType, Weapon.EFireMode.fullauto) >= 0;
        }

        internal static bool IsShotgunWeapon(Weapon? weapon)
        {
            return weapon != null &&
                   (weapon is ShotgunItemClass ||
                    weapon.GetType().Name.IndexOf("Shotgun", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static bool IsPrecisionRifleWeapon(Weapon? weapon)
        {
            string? weaponClass = weapon?.Template?.weapClass;
            if (string.IsNullOrWhiteSpace(weaponClass))
            {
                weaponClass = weapon?.WeapClass;
            }

            return string.Equals(weaponClass, "marksmanRifle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(weaponClass, "sniperRifle", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsGrenadeLauncherWeapon(Weapon? weapon)
        {
            return weapon?.IsGrenadeLauncher == true ||
                   weapon is RocketLauncherItemClass;
        }

        internal static bool IsSingleUseLauncherWeapon(Weapon? weapon)
        {
            return weapon is RocketLauncherItemClass;
        }

        internal static float GetGrenadeLauncherImpactUnsafeRadius(
            Vector3 fireOrigin,
            Vector3 target,
            float unsafeRadius)
        {
            Vector3 offset = target - fireOrigin;
            offset.y = 0f;
            if (offset.sqrMagnitude < GrenadeLauncherArmingDistance * GrenadeLauncherArmingDistance)
            {
                return Mathf.Min(unsafeRadius, GrenadeLauncherUnarmedImpactUnsafeRadius);
            }

            return unsafeRadius;
        }

        /// <summary>
        /// Push movement should only end for a firing transition when the shot is stable enough to
        /// capitalize on immediately, not on a brief visible/shootable flicker while advancing.
        /// </summary>
        public bool ShouldBreakAdvanceForImmediateFire()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy) || !goalEnemy.IsVisible || !goalEnemy.CanShoot)
            {
                return IsEnemyActivelyThreateningMe(goalEnemy, CloseThreatAdvanceBreakDistance, CloseThreatRecentSeenSeconds);
            }

            if (Enemy.Distance(goalEnemy) > Enemy.EnemyDistance.Close &&
                Time.time - goalEnemy.PersonalSeenTime < StableVisibleImmediateFireSeconds)
            {
                return false;
            }

            if (!botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            if (shootPoint == null)
            {
                return false;
            }

            return Utils.Utils.CanShootToTarget(shootPoint, botOwner.WeaponRoot.position, botOwner.LookSensor.Mask, false);
        }

        public bool IsEnemyActivelyThreateningMe(
            EnemyInfo? goalEnemy,
            float maxDistance,
            float recentSeenWindow)
        {
            if (!HasActiveCombatEnemy(goalEnemy) ||
                goalEnemy == null ||
                goalEnemy.Distance > maxDistance ||
                !SainGoalEnemyBridge.IsEnemyLookingAtFollower(botOwner, goalEnemy))
            {
                return false;
            }

            return goalEnemy.IsVisible ||
                   Time.time - goalEnemy.PersonalSeenTime <= recentSeenWindow ||
                   Time.time - goalEnemy.PersonalLastSeenTime <= recentSeenWindow;
        }

        internal static bool IsPointBlankContactWithoutHardSeparation(BotOwner? botOwner, EnemyInfo? goalEnemy)
        {
            if (botOwner == null ||
                !HasActiveCombatEnemy(botOwner, goalEnemy) ||
                goalEnemy == null ||
                goalEnemy.Distance > PointBlankContactDogFightDistance)
            {
                return false;
            }

            Vector3 enemyAnchor = GetEnemyCurrentPosition(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            if ((enemyAnchor - botOwner.Position).sqrMagnitude >
                PointBlankContactMaxAnchorDistance * PointBlankContactMaxAnchorDistance)
            {
                return false;
            }

            Vector3 weaponOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.25f;

            Vector3 chestTarget = enemyAnchor + Vector3.up * 1.05f;

            return HasNoHardObstruction(weaponOrigin, chestTarget);
        }

        internal static bool TryGetPointBlankContactFireTarget(
            BotOwner? botOwner,
            EnemyInfo? goalEnemy,
            out Vector3 target)
        {
            target = Vector3.zero;
            if (botOwner == null ||
                !HasActiveCombatEnemy(botOwner, goalEnemy) ||
                goalEnemy == null ||
                goalEnemy.Distance > PointBlankContactDogFightDistance)
            {
                return false;
            }

            Vector3 enemyAnchor = GetEnemyCurrentPosition(goalEnemy);
            if (!IsFinite(enemyAnchor) ||
                (enemyAnchor - botOwner.Position).sqrMagnitude >
                PointBlankContactMaxAnchorDistance * PointBlankContactMaxAnchorDistance)
            {
                return false;
            }

            Vector3 weaponOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.25f;

            Vector3 chestTarget = enemyAnchor + Vector3.up * 1.05f;

            if (TryAcceptPointBlankFireTarget(weaponOrigin, chestTarget, out target))
            {
                return true;
            }

            target = Vector3.zero;
            return false;
        }

        internal static bool TryGetCloseRecentContactFireTarget(
            BotOwner? botOwner,
            EnemyInfo? goalEnemy,
            out Vector3 target)
        {
            target = Vector3.zero;
            if (botOwner == null ||
                !HasActiveCombatEnemy(botOwner, goalEnemy) ||
                goalEnemy == null ||
                goalEnemy.Distance > CloseVisibleThreatBreakDistance ||
                !HasRecentPersonalContact(goalEnemy, CloseRecentContactFireSeconds))
            {
                return false;
            }

            Vector3 enemyAnchor = IsFinite(goalEnemy.EnemyLastPositionReal) &&
                                  goalEnemy.EnemyLastPositionReal.sqrMagnitude > 0.01f
                ? goalEnemy.EnemyLastPositionReal
                : GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor) || enemyAnchor.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            Vector3 weaponOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.25f;

            Vector3 chestTarget = enemyAnchor + Vector3.up * 1.05f;
            if (TryAcceptPointBlankFireTarget(weaponOrigin, chestTarget, out target))
            {
                return true;
            }

            target = Vector3.zero;
            return false;
        }

        private static bool HasRecentPersonalContact(EnemyInfo goalEnemy, float recentSeconds)
        {
            return IsRecentTimestamp(goalEnemy.PersonalSeenTime, recentSeconds) ||
                   IsRecentTimestamp(goalEnemy.PersonalLastSeenTime, recentSeconds);
        }

        private static bool IsRecentTimestamp(float timestamp, float recentSeconds)
        {
            if (!IsFinite(timestamp) || timestamp <= 0f)
            {
                return false;
            }

            float elapsed = Time.time - timestamp;
            return elapsed >= 0f && elapsed <= recentSeconds;
        }

        private static bool TryAcceptPointBlankFireTarget(Vector3 origin, Vector3 candidate, out Vector3 target)
        {
            target = Vector3.zero;
            if (!IsFinite(candidate) || candidate.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            if (!HasNoHardObstruction(origin, candidate))
            {
                return false;
            }

            target = candidate;
            return true;
        }

        private static bool HasNoHardObstruction(Vector3 origin, Vector3 target)
        {
            if (!IsFinite(origin) || !IsFinite(target))
            {
                return false;
            }

            Vector3 direction = target - origin;
            float distance = direction.magnitude;
            if (distance <= 0.05f)
            {
                return true;
            }

            if (!Physics.Raycast(origin, direction / distance, out RaycastHit hit, distance, LayerMaskClass.HighPolyWithTerrainMask))
            {
                return true;
            }

            return hit.collider != null && IsSoftFoliageCollider(hit.collider);
        }

        /// <summary>
        /// Verifies that the follower can actually fire from the current cover, with a direct line-of-sight
        /// fallback when EFT's cover cast says no but the shot is still physically clear.
        /// </summary>
        public bool CanShootFromCurrentCover(out string cause)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                cause = "noActiveEnemy";
                return false;
            }

            if (!goalEnemy.CanShoot || !goalEnemy.IsVisible)
            {
                cause = "enemyNotShootable";
                return false;
            }

            if (!botOwner.Memory.IsInCover)
            {
                cause = "IsInCover";
                return false;
            }

            if (botOwner.Memory.CurCustomCoverPoint == null)
            {
                cause = "noCurrentCoverPoint";
                return false;
            }

            if (!botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                cause = "EnoughDistToShoot";
                return false;
            }

            if (!botOwner.Memory.CurCustomCoverPoint.CanShootToTargetCast(
                    botOwner,
                    botOwner.Settings.FileSettings.Cover.DELTA_SEEN_FROM_COVE_LAST_POS))
            {
                ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
                Vector3 firePos = botOwner.WeaponRoot.position;
                if (shootPoint == null || !Utils.Utils.CanShootToTarget(shootPoint, firePos, botOwner.LookSensor.Mask, false))
                {
                    cause = "CanShootToTargetCast";
                    return false;
                }
            }

            if (botOwner.WeaponManager.Stationary.ShallEndShootFromCurrent())
            {
                cause = "EndSho";
                return false;
            }

            cause = "allFine";
            return true;
        }

        /// <summary>
        /// Detects the crouched-cover failure case where the enemy is visible but EFT does not mark
        /// the current pose as shootable even though a standing lane from the same cover is clear.
        /// </summary>
        public bool CanShootFromCurrentCoverIfStanding(out string cause)
        {
            return CanShootFromCurrentCoverIfStanding(botOwner, out cause);
        }

        public bool TryRaiseForStandingCoverShot(out string cause)
        {
            if (!HasCommittedShootingCoverIntent(botOwner, committedCoverPoint))
            {
                cause = "notShootingCoverIntent";
                return false;
            }

            return TryRaiseForStandingCoverShot(botOwner, out cause);
        }

        public bool CanShootFromCurrentCoverOrStandingIntent(out string cause)
        {
            if (CanShootFromCurrentCover(out cause))
            {
                return true;
            }

            return TryRaiseForStandingCoverShot(out cause);
        }

        public static bool TryRaiseForStandingCoverShot(
            BotOwner botOwner,
            out string cause,
            bool requireShootingCoverIntent = true)
        {
            if (requireShootingCoverIntent &&
                !HasCommittedShootingCoverIntent(botOwner, botOwner?.Memory?.CurCustomCoverPoint))
            {
                cause = "notShootingCoverIntent";
                return false;
            }

            return TryRaiseForStandingCoverShotUnchecked(botOwner, out cause);
        }

        private static bool TryRaiseForStandingCoverShotUnchecked(BotOwner botOwner, out string cause)
        {
            if (!CanShootFromCurrentCoverIfStanding(botOwner, out cause))
            {
                return false;
            }

            botOwner.SetPose(1f);

            ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(false);
            if (shootPoint != null)
            {
                botOwner.Steering.LookToPoint(shootPoint.Point);
            }
            else if (botOwner.Memory?.GoalEnemy != null)
            {
                botOwner.Steering.LookToPoint(botOwner.Memory.GoalEnemy.GetBodyPartPosition());
            }

            return true;
        }

        private static bool HasCommittedShootingCoverIntent(BotOwner? botOwner, CustomNavigationPoint? currentCover)
        {
            if (botOwner == null || currentCover == null)
            {
                return false;
            }

            return coverCommitIntents.TryGetValue(botOwner.Id, out CoverCommitIntent intent) &&
                   intent.IsShootingCover &&
                   intent.CoverId == currentCover.Id;
        }

        private static bool IsCommittedShootingCoverReason(string? reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                return false;
            }

            return reason.StartsWith("sniper.FireSupport", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.shootFromCover", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.reposition", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.protectBossShootCover", StringComparison.Ordinal) ||
                   reason.StartsWith("sniper.startPosition", StringComparison.Ordinal) ||
                   reason.StartsWith("shootCover", StringComparison.Ordinal) ||
                   reason.StartsWith("retreatShootCover", StringComparison.Ordinal) ||
                   reason.StartsWith("coverVisibleFire", StringComparison.Ordinal) ||
                   reason.StartsWith("committedFire", StringComparison.Ordinal);
        }

        public static bool CanShootFromCurrentCoverIfStanding(BotOwner botOwner, out string cause)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(botOwner, goalEnemy))
            {
                cause = "noActiveEnemy";
                return false;
            }

            if (!goalEnemy.IsVisible)
            {
                cause = "enemyNotVisible";
                return false;
            }

            if (!botOwner.Memory.IsInCover)
            {
                cause = "IsInCover";
                return false;
            }

            CustomNavigationPoint? cover = botOwner.Memory.CurCustomCoverPoint;
            if (cover == null)
            {
                cause = "noCurrentCoverPoint";
                return false;
            }

            if (cover.CoverLevel == CoverLevel.Lay)
            {
                cause = "layCover";
                return false;
            }

            if (!botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                cause = "EnoughDistToShoot";
                return false;
            }

            ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(false);
            shootPoint ??= new ShootPointClass(goalEnemy.GetBodyPartPosition(), 1f);

            Vector3 standingWeaponPosition = botOwner.Position + Vector3.up * StandingCoverShotProbeHeight;
            if (Utils.Utils.CanShootToTarget(shootPoint, standingWeaponPosition, botOwner.LookSensor.Mask, false))
            {
                cause = "standingPoseLane";
                return true;
            }

            Vector3 standingCoverFirePosition = cover.FirePosition + Vector3.up * StandingCoverShotProbeHeight;
            if (Utils.Utils.CanShootToTarget(shootPoint, standingCoverFirePosition, botOwner.LookSensor.Mask, false))
            {
                cause = "coverFirePositionStandingLane";
                return true;
            }

            cause = "standingLaneBlocked";
            return false;
        }

        private bool EnemyPathCrossesRecentDoor(EnemyInfo enemy)
        {
            NavMeshDoorLink nearestDoor = botOwner.NearDoorData.GetNearestDoor();
            if (nearestDoor == null)
            {
                return false;
            }

            Vector3 from = botOwner.Transform.position;
            Vector3 to = enemy.CurrPosition;
            GClass365 segment = new GClass365(from, to);
            Vector3 delta = nearestDoor.SegmentOpen.b - nearestDoor.SegmentOpen.a;
            Vector3 a = nearestDoor.SegmentOpen.a - delta * 0.1f;
            Vector3 b = nearestDoor.SegmentOpen.b + delta * 0.1f;
            return GClass369.GetCrossPoint(segment.a, segment.b, a, b) != null;
        }

        public static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// Check if the current enemy is low threat based on equipment, and number of nearby enemies.
        /// </summary>
        public bool IsEnemyLowThreat(EnemyInfo goalEnemy, bool ignoreEquip = false, float maximumEnemies = 2)
        {
            if (!ignoreEquip && dangerTimer > Time.time) return dangerResult;
            else if (ignoreEquip && dangerIgnoreEquipTimer > Time.time) return dangerIgnoreEquipResult;

            if (!ignoreEquip)
            {
                dangerTimer = Time.time + 1f;
                dangerResult = botOwner.Memory.AttackImmediately && Utils.Enemy.GetEnemiesAtLocation(botOwner, goalEnemy, goalEnemy.CurrPosition) <= maximumEnemies;

                return dangerResult;
            }
            else
            {
                dangerIgnoreEquipTimer = Time.time + 1f;
                dangerIgnoreEquipResult = Utils.Enemy.GetEnemiesAtLocation(botOwner, goalEnemy, goalEnemy.CurrPosition) < 3;

                return dangerIgnoreEquipResult;
            }
        }

        /// <summary>
        /// Close search is only safe when the current contact is actually isolated. This adds a
        /// non-cached recent-memory cluster check on top of the physical enemy count so target
        /// flicker inside a squad does not get treated as a weak single enemy.
        /// </summary>
        public bool IsSafeCloseSearchTarget(EnemyInfo goalEnemy, float aggression01, float clusterRadius = 22f)
        {
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return false;
            }

            if (!IsEnemyLowThreat(goalEnemy, aggression01))
            {
                return false;
            }

            Vector3 anchor = GetEnemyAnchor(goalEnemy);
            int allowedClusterEnemies = GetAllowedLowThreatEnemyCount(aggression01);
            if (Utils.Enemy.GetEnemiesAtLocation(botOwner, goalEnemy, anchor, clusterRadius) > allowedClusterEnemies)
            {
                return false;
            }

            return CountRecentKnownEnemiesNear(anchor, clusterRadius, 4f) <= allowedClusterEnemies;
        }

        private static int GetAllowedLowThreatEnemyCount(float aggression01)
        {
            if (aggression01 >= 0.7f)
            {
                return 3;
            }

            if (aggression01 >= 0.4f)
            {
                return 2;
            }

            return 1;
        }

        private int CountRecentKnownEnemiesNear(Vector3 position, float radius, float recentSeconds)
        {
            if (botOwner?.EnemiesController?.EnemyInfos == null)
            {
                return 0;
            }

            float radiusSqr = radius * radius;
            int count = 0;
            HashSet<string> counted = new HashSet<string>();
            foreach (var item in botOwner.EnemiesController.EnemyInfos)
            {
                IPlayer? player = item.Key;
                EnemyInfo info = item.Value;
                if (player == null ||
                    info == null ||
                    player.HealthController?.IsAlive != true ||
                    string.IsNullOrEmpty(player.ProfileId) ||
                    counted.Contains(player.ProfileId))
                {
                    continue;
                }

                bool recentlyKnown =
                    info.IsVisible ||
                    info.CanShoot ||
                    info.HaveSeen ||
                    Time.time - info.PersonalLastSeenTime <= recentSeconds;
                if (!recentlyKnown)
                {
                    continue;
                }

                Vector3 enemyPosition = player.Position;
                enemyPosition.y = position.y;
                Vector3 flatPosition = position;
                flatPosition.y = enemyPosition.y;
                if ((enemyPosition - flatPosition).sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                counted.Add(player.ProfileId);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Check if there is a reliable known position of the goal enemy from either personal or
        /// retained shared combat memory.
        /// </summary>
        public bool HasReliablePersonalEnemyLocation(EnemyInfo goalEnemy)
        {
            return Enemy.HasReliableKnownPosition(botOwner, goalEnemy);
        }

        /// <summary>
        /// Check if follower is critically wounded based on recent damage and hit frequency.
        /// Blocks aggressive pushes when critically injured.
        /// </summary>
        public bool IsFollowerCriticallyWounded()
        {
            bool multipleRecentHits = WasHitRecently(botOwner, 1.5f) && Time.time - botOwner.Memory.LastTimeHit - 0.5f > 0f;
            bool heavyFire = botOwner.Memory.IsUnderFire && WasHitRecently(botOwner, 3f);
            return multipleRecentHits || heavyFire;
        }

        public bool HasUrgentHealWork()
        {
            if (botOwner.Medecine == null)
            {
                return false;
            }

            ETagStatus? healthStatus = botOwner.GetPlayer?.HealthStatus;
            return botOwner.Medecine.SurgicalKit?.HaveWork == true ||
                   botOwner.Medecine.SurgicalKit?.Using == true ||
                   healthStatus == ETagStatus.BadlyInjured ||
                   healthStatus == ETagStatus.Dying;
        }

        public bool HasActiveOrPendingHealWork()
        {
            if (botOwner.Medecine == null)
            {
                return false;
            }

            RefreshCombatHealWorkIfNeeded();

            bool firstAidPending = botOwner.Medecine.FirstAid?.Have2Do == true;
            if (firstAidPending && ShouldDeferMinorFirstAidForActiveFight(botOwner.Memory?.GoalEnemy))
            {
                firstAidPending = false;
            }

            return firstAidPending ||
                   botOwner.Medecine.SurgicalKit?.HaveWork == true ||
                   botOwner.Medecine.FirstAid?.Using == true ||
                   botOwner.Medecine.SurgicalKit?.Using == true ||
                   botOwner.Medecine.Stimulators?.Using == true ||
                   HasUrgentHealWork();
        }

        private bool ShouldDeferMinorFirstAidForActiveFight(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null ||
                !HasActiveCombatEnemy(goalEnemy) ||
                goalEnemy.Distance > MinorFirstAidFightDeferDistance ||
                goalEnemy.PersonalLastSeenTime <= 0f ||
                Time.time - goalEnemy.PersonalLastSeenTime > MinorFirstAidFightDeferRecentContactSeconds ||
                (!botOwner.Memory.IsUnderFire && !WasHitRecently(botOwner, MinorFirstAidFightDeferRecentContactSeconds)) ||
                botOwner.GetPlayer?.HealthStatus != ETagStatus.Healthy ||
                botOwner.Medecine?.FirstAid?.Have2Do != true ||
                botOwner.Medecine.FirstAid.Using ||
                botOwner.Medecine.SurgicalKit?.HaveWork == true ||
                botOwner.Medecine.SurgicalKit?.Using == true ||
                botOwner.Medecine.Stimulators?.Using == true ||
                botOwner.GetPlayer?.HealthController == null ||
                botOwner.GetPlayer.ActiveHealthController == null ||
                FollowerMedical.HasActiveBleeding(botOwner.GetPlayer))
            {
                return false;
            }

            float missingHealth = 0f;
            foreach (EBodyPart part in GClass3058.RealBodyParts)
            {
                if (botOwner.GetPlayer.ActiveHealthController.IsBodyPartDestroyed(part))
                {
                    return false;
                }

                ValueStruct health = botOwner.GetPlayer.HealthController.GetBodyPartHealth(part, false);
                missingHealth += Mathf.Max(0f, health.Maximum - health.Current);
                if (missingHealth > MinorFirstAidFightDeferMaxMissingHealth)
                {
                    return false;
                }
            }

            return true;
        }

        public bool IsHealDecisionRetryBlocked => healBlockUntil >= Time.time;

        /// <summary>
        /// Check if follower is injured and should avoid aggressive advances.
        /// Prefers cover-holding or cautious movement when injured and under recent fire.
        /// </summary>
        public bool IsFollowerInjured()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            bool underThreat = botOwner.Memory.IsUnderFire || (goalEnemy != null && goalEnemy.IsVisible);
            return WasHitRecently(botOwner, 2.5f) && underThreat;
        }

        /// <summary>
        /// Check if boss/player wants to kill the current enemy (not just protect).
        /// </summary>
        public bool ProtectWantKill(float maxEnemyDistance = 50f)
        {
            return Time.time - botOwner.BotsGroup.EnemyLastSeenTimeReal <
                   botOwner.Settings.FileSettings.Mind.ATTACK_ENEMY_IF_PROTECT_DELTA_LAST_TIME_SEEN &&
                   botOwner.Memory.GoalEnemy != null &&
                   botOwner.Memory.GoalEnemy.Distance <= maxEnemyDistance;
        }

        /// <summary>
        /// Check if follower should care about protecting/holding boss position.
        /// </summary>
        public bool ProtectCareKill(float maxEnemyDistance = 50f)
        {
            float protectSeenTime = Time.time - botOwner.BotsGroup.EnemyLastSeenTimeReal;
            return protectSeenTime < botOwner.Settings.FileSettings.Mind.HOLD_IF_PROTECT_DELTA_LAST_TIME_SEEN &&
                   botOwner.Memory.GoalEnemy != null &&
                   botOwner.Memory.GoalEnemy.Distance <= maxEnemyDistance;
        }

        public static bool WasHitRecently(BotOwner bot, float seconds)
        {
            return Time.time - bot.Memory.LastTimeHit < seconds;
        }

        /// <summary>
        /// Shared dogfight-state probe used by both decision and end-condition logic.
        /// </summary>
        public bool IsDogFightActive() => botOwner.DogFight.DogFightState > BotDogFightStatus.none;

        // ──────────────────────────────────────────────────────────────────────────
        // End-condition dispatch
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Shared end-condition dispatcher.
        /// Keep this focused on decisions that are common across follower combat implementations,
        /// so specialized logic classes can override before/after this call without duplicating base behavior.
        /// </summary>
        public AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            return currentDecision.Action switch
            {
                BotLogicDecision.dogFight => EndDogFight(currentDecision),
                BotLogicDecision.shootToSmoke => EndImmediately(),
                BotLogicDecision.runToCover => EndRunToCover(currentDecision.Reason),
                BotLogicDecision.attackMoving => EndAttackMoving(currentDecision.Reason),
                BotLogicDecision.attackMovingWithSuppress => EndAttackMovingWithSuppress(currentDecision.Reason),
                var decision when decision == (BotLogicDecision)CustomBotDecisions.attackRetreat => EndAttackRetreat(currentDecision.Reason),
                BotLogicDecision.goToPointTactical => EndTacticalPoint(),
                BotLogicDecision.goToPoint => EndGoToPoint(),
                BotLogicDecision.runToEnemy => EndBaseGoToEnemy(),
                BotLogicDecision.goToEnemy => EndBaseGoToEnemy(),
                BotLogicDecision.shootFromPlace => EndShootFromPlace(currentDecision.Reason),
                BotLogicDecision.heal => EndHeal(),
                BotLogicDecision.healStimulators => EndStimulators(),
                BotLogicDecision.suppressFire => EndSuppressFire(currentDecision.Reason),
                BotLogicDecision.suppressGrenade => EndSuppressGrenade(),
                BotLogicDecision.shootFromCover => EndShootFromCover(),
                BotLogicDecision.search => EndEnemySearch(),
                _ => EndImmediately(),
            };
        }

        public AICoreActionEndStruct EndDogFight(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                ClearDecisionTransition();
                ClearDogFightState();
                return new AICoreActionEndStruct("enemyMissingOrDead", true);
            }

            const string reloadRetreatEndReason = "reloadRetreatNeeded";
            if (ShouldSeekReloadRetreat(goalEnemy) &&
                CanAttemptDecisionTransition(currentDecision, reloadRetreatEndReason, goalEnemy))
            {
                if (TryGetReloadRetreatDecision(
                        goalEnemy,
                        out AICoreActionResultStruct<BotLogicDecision, GClass26> reloadRetreatDecision))
                {
                    PrepareDecisionTransition(
                        currentDecision,
                        reloadRetreatEndReason,
                        goalEnemy,
                        reloadRetreatDecision);
                    ClearDogFightState();
                    return new AICoreActionEndStruct(reloadRetreatEndReason, true);
                }

                DeferDecisionTransition(currentDecision, reloadRetreatEndReason, goalEnemy);
            }

            if (goalEnemy != null &&
                !ShouldKeepDogFightOpeningCommitment(goalEnemy) &&
                TryGetDogFightInjuredSuppressRetreatDecision(
                    goalEnemy,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> injuredSuppressRetreat))
            {
                PrepareDecisionTransition(
                    currentDecision,
                    "dogFightInjuredSuppressRetreat",
                    goalEnemy,
                    injuredSuppressRetreat);
                return new AICoreActionEndStruct("dogFightInjuredSuppressRetreat", true);
            }

            if (ShouldReleasePointBlankDogFight(currentDecision.Reason, goalEnemy))
            {
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "pointBlankDogFight",
                    "release",
                    "pointBlankContactLost");
                ClearDogFightState();
                return new AICoreActionEndStruct("pointBlankContactLost", true);
            }

            if ((goalEnemy == null || goalEnemy.Distance > botOwner.Settings.FileSettings.Mind.DOG_FIGHT_OUT) &&
                !botOwner.WeaponManager.Reload.Reloading &&
                !botOwner.Memory.BotCurrentCoverInfo.UseDogFight(botOwner.Settings.FileSettings.Cover.DOG_FIGHT_AFTER_LEAVE))
            {
                dogFightBlockedUntil = Time.time + DogFightOutOfRangeCooldownSeconds;
                ClearDogFightState();
                return new AICoreActionEndStruct("dogFightOutOfRange", true);
            }

            return Continue();
        }

        /// <summary>
        /// Common run-to-cover stop conditions.
        /// Specialized logic can short-circuit this in its own dispatcher when needed.
        /// </summary>
        public AICoreActionEndStruct EndRunToCover(string? reason = null)
        {
            bool isRunToHeal = string.Equals(reason, "runToHeal", StringComparison.Ordinal);
            CustomNavigationPoint? targetCover = isRunToHeal
                ? committedHealCover
                : committedCoverPoint;
            targetCover ??= botOwner.Memory?.CurCustomCoverPoint;

            if (!isRunToHeal &&
                IsCommittedShootingCoverReason(reason) &&
                !HasActiveOrRetainedGoalEnemy(out _))
            {
                ClearCommittedCover();
                ClearCommittedMovement();
                return new AICoreActionEndStruct("shootCoverEnemyMissingOrDead", true);
            }

            if (!isRunToHeal && IsCloseVisibleShootableThreat(botOwner.Memory.GoalEnemy))
            {
                return new AICoreActionEndStruct("visibleCloseFireBreakCoverMove", true);
            }

            if (isRunToHeal && IsCloseVisibleHealThreat(botOwner.Memory.GoalEnemy))
            {
                ClearCommittedHealCover();
                return new AICoreActionEndStruct("healVisibleContactBreakCoverMove", true);
            }

            if ((!isRunToHeal && ShouldBreakRunToCoverForImmediateFire()) ||
                (isRunToHeal && IsPointBlankVisibleShootableThreat(botOwner.Memory.GoalEnemy)))
            {
                return new AICoreActionEndStruct("stableImmediateFire", true);
            }

            // EFT cover flags can lag while movement has already reached the selected cover point.
            // Only the action's exact destination can complete the move; being assigned to or
            // standing in some unrelated cover is not arrival.
            if (IsBotAtCoverDestination(targetCover))
            {
                if (isRunToHeal && !botOwner.Memory.IsInCover && botOwner.Memory.IsUnderFire)
                {
                    AICoreActionEndStruct healCoverStalled = EndRunToCoverIfStalled(reason);
                    return healCoverStalled.Value ? healCoverStalled : Continue();
                }

                if (!isRunToHeal)
                {
                    HoldCoverForMaxDuration();
                    ArmCommittedArrivalHold(reason, preferCover: true);
                }
                return new AICoreActionEndStruct("arrivedCommittedCover", true);
            }

            // Some move actions settle at the destination before IsInCover flips. Accept that path
            // completion only when the live point still matches this action's cover destination.
            if (targetCover != null && HasCurrentGoToPointArrivedAt(targetCover.Position))
            {
                if (isRunToHeal && !IsBotAtCommittedHealCover())
                {
                    AICoreActionEndStruct healStillTravelling = EndRunToCoverIfStalled(reason);
                    return healStillTravelling.Value ? healStillTravelling : Continue();
                }

                if (isRunToHeal && !botOwner.Memory.IsInCover && botOwner.Memory.IsUnderFire)
                {
                    AICoreActionEndStruct healPointStalled = EndRunToCoverIfStalled(reason);
                    return healPointStalled.Value ? healPointStalled : Continue();
                }

                if (!isRunToHeal)
                {
                    HoldCoverForMaxDuration();
                    ArmCommittedArrivalHold(reason, preferCover: committedCoverPoint != null || botOwner.Memory.CurCustomCoverPoint != null);
                }
                return new AICoreActionEndStruct("arrivedCoverPoint", true);
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightStarted", true);
            }

            if (!isRunToHeal &&
                botOwner.Memory.CurCustomCoverPoint != null &&
                botOwner.Memory.CurCustomCoverPoint.IsSpotted)
            {
                return new AICoreActionEndStruct("coverSpotted", true);
            }

            AICoreActionEndStruct stalled = EndRunToCoverIfStalled(reason);
            if (stalled.Value)
            {
                return stalled;
            }

            return Continue();
        }

        private AICoreActionEndStruct EndRunToCoverIfStalled(string? reason)
        {
            bool isRunToHeal = string.Equals(reason, "runToHeal", StringComparison.Ordinal);
            CustomNavigationPoint? targetCover = isRunToHeal
                ? committedHealCover
                : committedCoverPoint;
            targetCover ??= botOwner.Memory?.CurCustomCoverPoint;
            if (targetCover == null || !IsFinite(targetCover.Position))
            {
                ResetRunToCoverProgress();
                return Continue();
            }

            float distance = Vector3.Distance(botOwner.Position, targetCover.Position);
            if (!IsFinite(distance))
            {
                ResetRunToCoverProgress();
                return Continue();
            }

            if (runToCoverProgressCoverId != targetCover.Id)
            {
                runToCoverProgressCoverId = targetCover.Id;
                runToCoverBestDistance = distance;
                runToCoverLastProgressTime = Time.time;
                runToCoverNoPathSince = 0f;
                return Continue();
            }

            bool pushCoverMovement = IsPushCoverMovementReason(reason);
            if (pushCoverMovement && botOwner.Mover?.HasPathAndNoComplete != true)
            {
                if (runToCoverNoPathSince <= 0f)
                {
                    runToCoverNoPathSince = Time.time;
                }
                else if (Time.time - runToCoverNoPathSince >= PushCoverNoPathStallSeconds)
                {
                    BlockPushCover(targetCover, botOwner.Memory?.GoalEnemy, $"noPath:{reason}");
                    if (committedCoverPoint?.Id == targetCover.Id)
                    {
                        ClearCommittedCover("pushRunToCoverNoPath");
                    }
                    else
                    {
                        ResetRunToCoverProgress();
                    }

                    return new AICoreActionEndStruct("runToCoverNoPath", true);
                }
            }
            else
            {
                runToCoverNoPathSince = 0f;
            }

            if (distance <= runToCoverBestDistance - RunToCoverProgressMinDistance)
            {
                runToCoverBestDistance = distance;
                runToCoverLastProgressTime = Time.time;
                return Continue();
            }

            if (runToCoverLastProgressTime <= 0f ||
                Time.time - runToCoverLastProgressTime <= RunToCoverStallSeconds)
            {
                return Continue();
            }

            if (isRunToHeal)
            {
                BlockHealCover(targetCover);
                ClearCommittedHealCover();
                ResetRunToCoverProgress();
            }
            else if (committedCoverPoint != null && committedCoverPoint.Id == targetCover.Id)
            {
                if (pushCoverMovement)
                {
                    BlockPushCover(targetCover, botOwner.Memory?.GoalEnemy, $"stalled:{reason}");
                }

                ClearCommittedCover(pushCoverMovement ? "pushRunToCoverStalled" : null);
            }
            else
            {
                ResetRunToCoverProgress();
            }

            return new AICoreActionEndStruct("runToCoverStalled", true);
        }

        private void ResetRunToCoverProgress()
        {
            runToCoverProgressCoverId = -1;
            runToCoverBestDistance = float.MaxValue;
            runToCoverLastProgressTime = 0f;
            runToCoverNoPathSince = 0f;
        }

        public AICoreActionEndStruct EndTacticalPoint(
            bool endWhenCanShootFromCover = true,
            bool endWhenEnemyVisibleShootable = true)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null || !HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemyMissingOrDead", true);
            }

            if (endWhenCanShootFromCover && CanShootFromCurrentCover(out _))
            {
                HoldCoverForMaxDuration();
                ArmCommittedArrivalHold("tacticalShootCover", preferCover: true);
                return new AICoreActionEndStruct("foundShootCover", true);
            }

            if (endWhenEnemyVisibleShootable && goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return new AICoreActionEndStruct("enemyVisibleAndShootable", true);
            }

            if (botOwner.Memory.IsUnderFire)
            {
                return new AICoreActionEndStruct("underFire", true);
            }

            if (botOwner.GoToSomePointData.IsCome() || IsAtTacticalPoint())
            {

                if (botOwner.Memory.IsInCover || IsBotInCommittedCover())
                {
                    HoldCoverForMaxDuration();
                    ArmCommittedArrivalHold("tacticalPoint", preferCover: true);
                }
                else
                {
                    ArmCommittedArrivalHold("tacticalPoint", preferCover: false);
                }

                return new AICoreActionEndStruct("arrivedAtPoint", true);
            }

            AICoreActionEndStruct stalled = EndTacticalPointIfStalled();
            if (stalled.Value)
            {
                return stalled;
            }

            return default;
        }

        private bool IsAtTacticalPoint()
        {
            if (botOwner.GoToSomePointData == null ||
                !botOwner.GoToSomePointData.HaveTarget())
            {
                return false;
            }

            Vector3 target = botOwner.GoToSomePointData.Point;
            return IsFinite(target) &&
                   (botOwner.Position - target).sqrMagnitude <= TacticalPointArrivalDistance * TacticalPointArrivalDistance;
        }

        private AICoreActionEndStruct EndTacticalPointIfStalled()
        {
            if (!botOwner.GoToSomePointData.HaveTarget())
            {
                ResetTacticalPointProgress();
                return Continue();
            }

            Vector3 target = botOwner.GoToSomePointData.Point;
            float distance = Vector3.Distance(botOwner.Position, target);

            if ((target - tacticalPointProgressTarget).sqrMagnitude > 1f)
            {
                tacticalPointProgressTarget = target;
                tacticalPointBestDistance = distance;
                tacticalPointLastProgressTime = Time.time;
                return Continue();
            }

            if (distance <= tacticalPointBestDistance - TacticalPointProgressMinDistance)
            {
                tacticalPointBestDistance = distance;
                tacticalPointLastProgressTime = Time.time;
                return Continue();
            }

            if (tacticalPointLastProgressTime <= 0f ||
                Time.time - tacticalPointLastProgressTime <= TacticalPointStallSeconds)
            {
                return Continue();
            }

            BlockTacticalPoint(target);
            ClearCommittedMovement("tacticalPointStalled");
            ResetTacticalPointProgress();
            return new AICoreActionEndStruct("tacticalPointStalled", true);
        }

        private bool IsBlockedTacticalPoint(Vector3 point)
        {
            return Time.time < blockedTacticalPointUntil &&
                   (point - blockedTacticalPoint).sqrMagnitude <=
                       TacticalPointBlacklistRadius * TacticalPointBlacklistRadius;
        }

        private void BlockTacticalPoint(Vector3 point)
        {
            if (!IsFinite(point))
            {
                return;
            }

            blockedTacticalPoint = point;
            blockedTacticalPointUntil = Time.time + TacticalPointBlacklistSeconds;
        }

        private void ResetTacticalPointProgress()
        {
            tacticalPointProgressTarget = Vector3.zero;
            tacticalPointBestDistance = float.MaxValue;
            tacticalPointLastProgressTime = 0f;
        }

        public AICoreActionEndStruct EndGoToPoint(bool endWhenEnemyVisibleShootable = true)
        {
            return EndTacticalPoint(
                endWhenCanShootFromCover: false,
                endWhenEnemyVisibleShootable: endWhenEnemyVisibleShootable);
        }

        public AICoreActionEndStruct EndAttackMoving(string? reason = null)
        {
            bool isMoveToHeal = IsMedicalRetreatMovementReason(reason);

            if (isMoveToHeal)
            {
                if (IsDogFightActive())
                {
                    return new AICoreActionEndStruct("dogFightStarted", true);
                }

                EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
                if (IsPointBlankVisibleShootableThreat(goalEnemy))
                {
                    ClearCommittedHealCover();
                    return new AICoreActionEndStruct("healMovePointBlankVisibleThreat", true);
                }

                return EndCommittedHealMovement(goalEnemy);
            }

            RefreshShootCover();
            if (HaveCoverToShoot && botOwner.Memory.IsInCover)
            {
                if (!isMoveToHeal)
                {
                    HoldCoverForMaxDuration();
                    ArmCommittedArrivalHold(reason ?? "attackMovingShootCover", preferCover: true);
                }
                return new AICoreActionEndStruct("foundCoverToShoot", true);
            }

            return EndBaseAttackMoving(reason);
        }

        public AICoreActionEndStruct EndAttackMovingWithSuppress(string? reason = null)
        {
            return EndAttackMoving(reason);
        }

        public AICoreActionEndStruct EndAttackRetreat(string? reason = null)
        {
            bool isMoveToHeal = IsMedicalRetreatMovementReason(reason);
            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightStarted", true);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (IsPointBlankVisibleShootableThreat(goalEnemy))
            {
                return new AICoreActionEndStruct("retreatPointBlankVisibleThreat", true);
            }

            if (isMoveToHeal)
            {
                return EndCommittedHealMovement(goalEnemy);
            }

            if (IsBotInCommittedCover() || IsAtCommittedMovementDestination())
            {
                HoldCoverForMaxDuration();
                ArmCommittedArrivalHold(reason ?? "attackRetreat", preferCover: true);
                return new AICoreActionEndStruct("arrivedCommittedCover", true);
            }

            AICoreActionEndStruct stalled = EndRunToCoverIfStalled(reason);
            if (stalled.Value)
            {
                return stalled;
            }

            return Continue();
        }

        private bool ShouldReleasePointBlankDogFight(string? reason, EnemyInfo? goalEnemy)
        {
            if (!IsPointBlankDogFightReason(reason))
            {
                pointBlankDogFightContactLostAt = 0f;
                return false;
            }

            bool stillRequiresDogFight =
                IsPointBlankContactWithoutHardSeparation(botOwner, goalEnemy) ||
                HasFreshVisibleShootableContact(goalEnemy, CloseThreatRecentSeenSeconds) ||
                IsEnemyActivelyThreateningMe(
                    goalEnemy,
                    CloseThreatDogFightDistance,
                    CloseThreatRecentSeenSeconds);
            if (stillRequiresDogFight)
            {
                pointBlankDogFightContactLostAt = 0f;
                return false;
            }

            if (pointBlankDogFightContactLostAt <= 0f)
            {
                pointBlankDogFightContactLostAt = Time.time;
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "pointBlankDogFight",
                    "lostContactGrace",
                    reason,
                    untilTime: pointBlankDogFightContactLostAt + PointBlankDogFightLostContactGraceSeconds);
                return false;
            }

            return Time.time - pointBlankDogFightContactLostAt >= PointBlankDogFightLostContactGraceSeconds;
        }

        private static bool IsPointBlankDogFightReason(string? reason)
        {
            return string.Equals(reason, "pointBlankContactDogFight", StringComparison.Ordinal) ||
                   string.Equals(reason, "pushPointBlankContactDogFight", StringComparison.Ordinal);
        }

        private AICoreActionEndStruct EndCommittedHealMovement(EnemyInfo? goalEnemy)
        {
            // EFT movement nodes can report path arrival before Memory.IsInCover flips. Only the
            // exact committed heal cover/point can complete this action; stale path state cannot.
            if (IsBotAtCommittedHealCover())
            {
                bool exposedArrival =
                    botOwner.Memory.IsUnderFire ||
                    WasHitRecently(botOwner, 1.25f) ||
                    (goalEnemy?.IsVisible == true && goalEnemy.CanShoot);
                if (exposedArrival)
                {
                    if (committedHealCover != null)
                    {
                        BlockHealCover(committedHealCover);
                    }

                    ClearCommittedHealCover();
                    return new AICoreActionEndStruct("healRetreatArrivedExposed", true);
                }

                ResetHealRetreatProgress();
                return new AICoreActionEndStruct("healRetreatArrived", true);
            }

            AICoreActionEndStruct stalled = EndHealRetreatIfStalled();
            return stalled.Value ? stalled : Continue();
        }

        private AICoreActionEndStruct EndHealRetreatIfStalled()
        {
            Vector3 target;
            if (committedHealCover != null)
            {
                target = committedHealCover.Position;
            }
            else if (hasCommittedHealPoint)
            {
                target = committedHealPoint;
            }
            else
            {
                ResetHealRetreatProgress();
                return Continue();
            }

            if (!IsFinite(target))
            {
                ResetHealRetreatProgress();
                return Continue();
            }

            float distance = Vector3.Distance(botOwner.Position, target);
            if ((target - healRetreatProgressTarget).sqrMagnitude > 1f)
            {
                healRetreatProgressTarget = target;
                healRetreatBestDistance = distance;
                healRetreatLastProgressTime = Time.time;
                return Continue();
            }

            if (distance <= healRetreatBestDistance - HealRetreatProgressMinDistance)
            {
                healRetreatBestDistance = distance;
                healRetreatLastProgressTime = Time.time;
                return Continue();
            }

            if (healRetreatLastProgressTime <= 0f ||
                Time.time - healRetreatLastProgressTime <= HealRetreatStallSeconds)
            {
                return Continue();
            }

            if (committedHealCover != null)
            {
                BlockHealCover(committedHealCover);
            }

            ClearCommittedHealCover();
            return new AICoreActionEndStruct("healRetreatStalled", true);
        }

        private void ResetHealRetreatProgress()
        {
            healRetreatProgressTarget = Vector3.zero;
            healRetreatBestDistance = float.MaxValue;
            healRetreatLastProgressTime = 0f;
        }

        private static bool IsPointBlankVisibleShootableThreat(EnemyInfo? goalEnemy)
        {
            return goalEnemy != null &&
                   goalEnemy.IsVisible &&
                   goalEnemy.CanShoot &&
                   goalEnemy.Distance <= PointBlankRetreatBlockDistance;
        }

        private static bool IsCloseVisibleHealThreat(EnemyInfo? goalEnemy)
        {
            return goalEnemy != null &&
                   goalEnemy.IsVisible &&
                   goalEnemy.CanShoot &&
                   goalEnemy.Distance <= HealContactThreatDistance;
        }

        private static bool IsCloseVisibleShootableThreat(EnemyInfo? goalEnemy)
        {
            return goalEnemy != null &&
                   goalEnemy.IsVisible &&
                   goalEnemy.CanShoot &&
                   goalEnemy.Distance <= CloseVisibleThreatBreakDistance;
        }

        public bool CanRunToEnemyNow()
        {
            return Time.time >= runToEnemyBlockedUntil;
        }

        public void BlockRunToEnemy(float seconds)
        {
            runToEnemyBlockedUntil = Mathf.Max(runToEnemyBlockedUntil, Time.time + seconds);
        }

        public AICoreActionEndStruct EndShootFromPlace(string? reason = null)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                if (!FollowerImmediateFirePolicy.IsImmediateShootReason(reason) ||
                    !FollowerContactEnemyRetention.TryRestore(botOwner, out goalEnemy) ||
                    goalEnemy == null)
                {
                    return new AICoreActionEndStruct("enemyMissingOrDead", true);
                }
            }

            if (ShouldSeekReloadRetreat(goalEnemy) &&
                TryGetReloadRetreatDecision(goalEnemy, out _))
            {
                return new AICoreActionEndStruct("reloadRetreatNeeded", true);
            }

            if (botOwner.DogFight.ShallStartCauseHavePlace())
            {
                return new AICoreActionEndStruct("dogFightHavePlace", true);
            }

            if (FollowerImmediateFirePolicy.IsImmediateShootReason(reason) &&
                !goalEnemy.IsVisible &&
                !goalEnemy.CanShoot &&
                !CanContinueImmediateLostVisualFire(goalEnemy))
            {
                return new AICoreActionEndStruct("immediateLostVisualExpired", true);
            }

            if (!goalEnemy.CanShoot)
            {
                if (FollowerImmediateFirePolicy.IsImmediateShootReason(reason) &&
                    CanContinueImmediateLostVisualFire(goalEnemy))
                {
                    return Continue();
                }

                return new AICoreActionEndStruct("enemyCannotShoot", true);
            }

            if (ShouldShootImmediately())
            {
                return Continue();
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightStarted", true);
            }

            if (goalEnemy.Distance < 1f)
            {
                return new AICoreActionEndStruct("enemyTooClose", true);
            }

            if (botOwner.WeaponManager.Reload.Reloading)
            {
                return Continue();
            }

            return Continue();
        }

        private bool CanContinueImmediateLostVisualFire(EnemyInfo goalEnemy)
        {
            if (!FollowerImmediateFirePolicy.CanUseLostVisualSuppress(goalEnemy))
            {
                return false;
            }

            Vector3 target = FollowerImmediateFirePolicy.GetLostVisualSuppressTarget(goalEnemy);
            return FollowerImmediateFirePolicy.HasDirectFireLane(botOwner, target) &&
                   !FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, target);
        }

        public AICoreActionEndStruct EndHeal()
        {
            bool haveHealWork = botOwner.Medecine.FirstAid.Have2Do || botOwner.Medecine.SurgicalKit.HaveWork;
            bool activelyHealing = botOwner.Medecine.FirstAid.Using || botOwner.Medecine.SurgicalKit.Using;
            if (TryGetExposedHealInterruptReason(out string exposedReason))
            {
                CancelExposedHeal();
                return new AICoreActionEndStruct(exposedReason, true);
            }

            float timeout = botOwner.Medecine.SurgicalKit.Using ? 45f : 15f;
            if (activelyHealing)
            {
                if (healStartedAt > 0f && healStartedAt + timeout < Time.time)
                {
                    AbortActiveHeal();
                    return new AICoreActionEndStruct("healTimedOut", true);
                }

                return Continue();
            }

            if (!haveHealWork)
            {
                CompleteActiveHeal();
                return new AICoreActionEndStruct("healCompleted", true);
            }

            // If the heal action never transitions into active first-aid/surgery use, do not let the
            // bot sit in healInCover forever waiting on a stuck vanilla node.
            if (!activelyHealing &&
                healStartedAt > 0f &&
                healStartedAt + 3f < Time.time)
            {
                CompleteActiveHeal();
                return new AICoreActionEndStruct("healIdleTimedOut", true);
            }

            return Continue();
        }

        private bool TryGetExposedHealInterruptReason(out string reason)
        {
            reason = string.Empty;
            EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy) || goalEnemy == null)
            {
                return false;
            }

            bool protectedPosition = botOwner.Memory.IsInCover || IsBotAtCommittedHealCover();
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                reason = "healExposedVisibleThreat";
                return true;
            }

            bool hotPressure = botOwner.Memory.IsUnderFire ||
                               WasHitRecently(botOwner, 1.25f) ||
                               FollowerAwareness.WasRecentlyDamaged(botOwner);
            if (hotPressure && !protectedPosition)
            {
                reason = "healExposedUnderFire";
                return true;
            }

            return false;
        }

        private bool IsBotAtCoverDestination(CustomNavigationPoint? cover)
        {
            if (cover == null || !IsFinite(cover.Position))
            {
                return false;
            }

            if (botOwner.Memory.IsInCover &&
                botOwner.Memory.CurCustomCoverPoint != null &&
                botOwner.Memory.CurCustomCoverPoint.Id == cover.Id)
            {
                return true;
            }

            return (botOwner.Position - cover.Position).sqrMagnitude <= 2f * 2f;
        }

        private void CancelExposedHeal()
        {
            ClearCommittedHealCover();
            FollowerMedical.CancelActiveMedical(botOwner);
            FollowerMedical.RefreshMedicalWork(botOwner);
            healBlockUntil = Time.time + 0.75f;
            healStartedAt = 0f;
        }

        public AICoreActionEndStruct EndStimulators()
        {
            if (!botOwner.Medecine.Stimulators.Using)
            {
                stimStartedAt = 0f;
                FollowerMedical.RefreshMedicalWork(botOwner);
                return new AICoreActionEndStruct("stimsCompleted", true);
            }

            if (stimStartedAt > 0f && stimStartedAt + 5f < Time.time)
            {
                botOwner.Medecine.Stimulators.CancelCurrent();
                stimStartedAt = 0f;
                FollowerMedical.RefreshMedicalWork(botOwner);
                return new AICoreActionEndStruct("stimsTimedOut", true);
            }

            return Continue();
        }

        public AICoreActionEndStruct EndSuppressFire(string? reason = null)
        {
            if (IsFollowerSuppressReason(reason))
            {
                return EndFollowerSuppressFire(reason);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemyMissingOrDead", true);
            }

            if (ShouldShootImmediately())
            {
                return new AICoreActionEndStruct("shootImmediately", true);
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightStarted", true);
            }

            if (goalEnemy != null && FollowerImmediateFirePolicy.CanUseRecentContactSuppress(goalEnemy))
            {
                return Continue();
            }

            // If enemy cannot be shot (not visible or can't shoot), suppress fire ends
            if (goalEnemy != null && (!goalEnemy.CanShoot || !goalEnemy.IsVisible))
            {
                return new AICoreActionEndStruct("enemyNotShootable", true);
            }

            return Continue();
        }

        private AICoreActionEndStruct EndFollowerSuppressFire(string? reason)
        {
            bool ordered = IsOrderedSuppressReason(reason) ||
                           FollowerCombatSuppressionObjective.IsSuppressionObjectiveReason(reason) ||
                           FollowerCombatGrenadierObjective.IsOrderedGrenadierReason(reason);
            bool commandOwned = IsOrderedSuppressReason(reason);
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            if (commandOwned &&
                (followerData == null ||
                 !followerData.TryGetActiveCommand(out FollowerCommandType command, out _) ||
                 command != FollowerCommandType.SuppressEnemy))
            {
                return new AICoreActionEndStruct("orderedSuppressCommandMissing", true);
            }

            if (!commandOwned &&
                followerData != null &&
                followerData.TryGetActiveCommand(out FollowerCommandType activeCommand, out _) &&
                activeCommand != FollowerCommandType.SuppressEnemy)
            {
                return new AICoreActionEndStruct("explicitOrderBreakSuppress", true);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                followerData?.ClearCommand("SuppressEnemy:noActiveEnemy");
                return new AICoreActionEndStruct("followerSuppressEnemyMissingOrDead", true);
            }

            if (botOwner.SuppressShoot == null)
            {
                followerData?.ClearCommand("SuppressEnemy:missingSuppressShoot");
                return new AICoreActionEndStruct("followerSuppressMissingController", true);
            }

            float suppressElapsed = activeFollowerSuppressStartedAt > 0f
                ? Time.time - activeFollowerSuppressStartedAt
                : 0f;
            float protectedSeconds = GetFollowerSuppressProtectedSeconds(ordered);

            bool launcherSuppress = IsGrenadeLauncherSuppressReason(reason);
            if (!launcherSuppress)
            {
                UpdateWeaponSuppressShotDetection();
            }

            float effectiveSuppressElapsed = launcherSuppress
                ? GetLauncherSuppressEffectiveElapsed(suppressElapsed)
                : suppressElapsed;
            bool launcherOpeningAim =
                launcherSuppress &&
                !activeLauncherSuppressShotDetected &&
                effectiveSuppressElapsed < GrenadeLauncherSuppressAimSettleSeconds;
            if (launcherSuppress &&
                TryGetLauncherSuppressFireEndReason(
                    launcherOpeningAim ? false : botOwner.SuppressShoot.Complete,
                    suppressElapsed,
                    out string launcherEndReason))
            {
                followerData?.ClearCommand($"SuppressEnemy:{launcherEndReason}");
                return new AICoreActionEndStruct(launcherEndReason, true);
            }

            if (launcherSuppress &&
                IsGrenadeLauncherSuppressCommitmentExpired(reason, effectiveSuppressElapsed))
            {
                RecordLauncherSuppressCommitmentExpired(reason, goalEnemy);
                followerData?.ClearCommand("SuppressEnemy:launcherCommitmentExpired");
                return new AICoreActionEndStruct("launcherCommitmentExpired", true);
            }

            if (!ordered && effectiveSuppressElapsed >= AutoSuppressMaxSeconds)
            {
                return new AICoreActionEndStruct("autoSuppressTimedOut", true);
            }

            if (ordered && !launcherSuppress && suppressElapsed >= OrderedWeaponSuppressMaxSeconds)
            {
                followerData?.ClearCommand("SuppressEnemy:weaponSuppressTimedOut");
                return new AICoreActionEndStruct("orderedWeaponSuppressTimedOut", true);
            }

            if (!launcherSuppress &&
                suppressElapsed >= protectedSeconds &&
                (ordered || activeFollowerSuppressShotDetected) &&
                HasActiveOrPendingHealWork())
            {
                followerData?.ClearCommand("SuppressEnemy:needHeal");
                return new AICoreActionEndStruct("followerSuppressNeedHeal", true);
            }

            if (!launcherSuppress &&
                suppressElapsed >= protectedSeconds &&
                ShouldSeekReloadRetreat(goalEnemy))
            {
                followerData?.ClearCommand("SuppressEnemy:reloadRetreat");
                return new AICoreActionEndStruct("followerSuppressReloadRetreat", true);
            }

            if (botOwner.SuppressShoot.Complete)
            {
                if (launcherSuppress)
                {
                    return Continue();
                }

                if (!launcherSuppress && suppressElapsed < protectedSeconds)
                {
                    RestartFollowerSuppress(goalEnemy);
                    return Continue();
                }

                followerData?.ClearCommand("SuppressEnemy:complete");
                return new AICoreActionEndStruct("followerSuppressComplete", true);
            }

            Vector3? point = botOwner.SuppressShoot.GetPoint();
            if (!point.HasValue || !IsFinite(point.Value))
            {
                if (!launcherSuppress && suppressElapsed < protectedSeconds)
                {
                    RestartFollowerSuppress(goalEnemy);
                    return Continue();
                }

                followerData?.ClearCommand("SuppressEnemy:missingTarget");
                return new AICoreActionEndStruct("followerSuppressMissingTarget", true);
            }

            Vector3 fireOrigin = botOwner.WeaponRoot != null
                ? botOwner.WeaponRoot.position
                : botOwner.Position + Vector3.up * 1.2f;
            float launcherUnsafeRadius = IsAutonomousSuppressReason(reason) ? GrenadeLauncherAutoUnsafeRadius : GrenadeLauncherOrderedUnsafeRadius;
            Vector3 acceptedLauncherFireOrigin = fireOrigin;
            bool hasLauncherSuppressLane = false;
            string launcherLaneRejectReason = string.Empty;
            if (launcherSuppress)
            {
                hasLauncherSuppressLane = TryCanFireGrenadeLauncherAtTarget(
                    botOwner,
                    fireOrigin,
                    point.Value,
                    launcherUnsafeRadius,
                    out launcherLaneRejectReason,
                    out acceptedLauncherFireOrigin);
            }

            if (launcherSuppress &&
                FollowerShotSafety.IsFriendlyNearImpact(
                    botOwner,
                    point.Value,
                    launcherUnsafeRadius))
            {
                BattleRecorder.RecordGrenadeEvent(
                    botOwner,
                    "launcherReject",
                    $"{reason}:launcherImpactUnsafe",
                    goalEnemy: goalEnemy,
                    target: point.Value);
                followerData?.ClearCommand("SuppressEnemy:launcherImpactUnsafe");
                return new AICoreActionEndStruct("launcherImpactUnsafe", true);
            }

            Vector3 suppressFireOrigin = launcherSuppress ? acceptedLauncherFireOrigin : fireOrigin;
            if (!IsDogFightHealRetreatSuppressReason(reason) &&
                ShouldBreakFollowerSuppressForPointBlankContact(goalEnemy, point.Value, suppressFireOrigin))
            {
                followerData?.ClearCommand("SuppressEnemy:pointBlankNonFoliageContact");
                return new AICoreActionEndStruct("pointBlankNonFoliageContact", true);
            }

            if ((!launcherSuppress || !hasLauncherSuppressLane) &&
                FollowerShotSafety.IsFriendlyInSuppressionLane(botOwner, fireOrigin, point.Value))
            {
                if (suppressElapsed < protectedSeconds)
                {
                    return Continue();
                }

                followerData?.ClearCommand("SuppressEnemy:blockedLane");
                return new AICoreActionEndStruct("followerSuppressBlockedLane", true);
            }

            bool hasDirectSuppressLane = Utils.Utils.CanShootToTarget(
                    new ShootPointClass(point.Value, 1f),
                    fireOrigin,
                    botOwner.LookSensor.Mask,
                    false);
            bool hasAcceptableSuppressLane = launcherSuppress
                ? hasLauncherSuppressLane
                : hasDirectSuppressLane || IsSoftObstructedSuppressionLane(fireOrigin, point.Value);
            if (!hasAcceptableSuppressLane)
            {
                if (launcherSuppress &&
                    botOwner.SuppressShoot.PointToSuppressFrom != null &&
                    botOwner.GoToSomePointData?.HaveTarget() == true &&
                    !botOwner.GoToSomePointData.IsCome())
                {
                    return Continue();
                }

                if (launcherSuppress)
                {
                    if (launcherOpeningAim)
                    {
                        return Continue();
                    }

                    followerData?.ClearCommand($"SuppressEnemy:{launcherLaneRejectReason}");
                    return new AICoreActionEndStruct("followerSuppressHardBlockedLane", true);
                }

                if (suppressElapsed < protectedSeconds)
                {
                    return Continue();
                }

                followerData?.ClearCommand("SuppressEnemy:hardBlockedLane");
                return new AICoreActionEndStruct("followerSuppressHardBlockedLane", true);
            }

            return Continue();
        }

        private bool ShouldBreakFollowerSuppressForPointBlankContact(
            EnemyInfo goalEnemy,
            Vector3 suppressTarget,
            Vector3 fireOrigin)
        {
            if (!IsPointBlankContactWithoutHardSeparation(botOwner, goalEnemy) ||
                !HasRecentPointBlankContact(goalEnemy))
            {
                return false;
            }

            return !HasConfirmedCloseSuppressFoliage(goalEnemy, suppressTarget, fireOrigin);
        }

        private static bool HasRecentPointBlankContact(EnemyInfo goalEnemy)
        {
            return goalEnemy.IsVisible ||
                   Time.time - goalEnemy.PersonalSeenTime <= CloseSuppressRecentContactSeconds ||
                   Time.time - goalEnemy.PersonalLastSeenTime <= CloseSuppressRecentContactSeconds;
        }

        private bool HasConfirmedCloseSuppressFoliage(
            EnemyInfo goalEnemy,
            Vector3 suppressTarget,
            Vector3 fireOrigin)
        {
            LayerMask mask = botOwner.LookSensor?.Mask ?? LayerMaskClass.HighPolyWithTerrainMaskAI;
            if (IsSoftObstructedSuppressionLane(fireOrigin, suppressTarget, mask))
            {
                return true;
            }

            Vector3 enemyAnchor = GetEnemyCurrentPosition(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 capsuleStart = botOwner.Position + Vector3.up * 0.7f;
            Vector3 capsuleEnd = enemyAnchor + Vector3.up * 1.25f;
            int hitCount = Physics.OverlapCapsuleNonAlloc(
                capsuleStart,
                capsuleEnd,
                CloseSuppressFoliageProbeRadius,
                closeSuppressFoliageBuffer,
                LayerMaskClass.AI);

            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = closeSuppressFoliageBuffer[i];
                closeSuppressFoliageBuffer[i] = null;
                if (collider != null && IsSoftFoliageCollider(collider))
                {
                    return true;
                }
            }

            return false;
        }

        private static float GetFollowerSuppressProtectedSeconds(bool ordered)
        {
            return ordered ? OrderedSuppressMinSeconds : AutoSuppressMinSeconds;
        }

        private bool RestartFollowerSuppress(EnemyInfo goalEnemy)
        {
            if (botOwner.SuppressShoot == null ||
                !TryGetSuppressTarget(goalEnemy, out Vector3 suppressTarget))
            {
                return false;
            }

            CustomNavigationPoint? suppressFrom = botOwner.SuppressShoot.PointToSuppressFrom;
            return botOwner.SuppressShoot.InitToPoint(suppressTarget, suppressFrom);
        }

        public static bool IsFollowerSuppressReason(string? reason)
        {
            return IsOrderedSuppressReason(reason) ||
                   IsAutoSuppressReason(reason) ||
                   IsBossProtectionSuppressReason(reason) ||
                   IsRecoverySuppressReason(reason) ||
                   FollowerCombatSuppressionObjective.IsSuppressionObjectiveReason(reason) ||
                   FollowerCombatGrenadierObjective.IsGrenadierReason(reason);
        }

        public static bool IsRecoverySuppressReason(string? reason)
        {
            return reason != null &&
                   reason.StartsWith("recovery.noCoverSuppress", StringComparison.Ordinal);
        }

        public static bool IsRecoveryNoCoverReason(string? reason)
        {
            return string.Equals(reason, RecoveryNoCoverFightReason, StringComparison.Ordinal) ||
                   IsRecoverySuppressReason(reason) ||
                   string.Equals(reason, RecoveryNoCoverThreatHoldReason, StringComparison.Ordinal);
        }

        public static bool IsGrenadeLauncherSuppressReason(string? reason)
        {
            return IsFollowerSuppressReason(reason) &&
                   reason.IndexOf(GrenadeLauncherSuppressReasonToken, StringComparison.Ordinal) >= 0;
        }

        public static bool IsGrenadeLauncherCombatReason(string? reason)
        {
            return FollowerCombatGrenadierObjective.IsGrenadierReason(reason) &&
                   reason!.IndexOf(GrenadeLauncherSuppressReasonToken, StringComparison.Ordinal) >= 0;
        }

        public static bool IsOrderedSuppressReason(string? reason)
        {
            return reason != null && reason.StartsWith("orderedSuppress", StringComparison.Ordinal);
        }

        public static bool IsAutoSuppressReason(string? reason)
        {
            return reason != null && reason.StartsWith("autoSuppress", StringComparison.Ordinal);
        }

        public static bool IsBossProtectionSuppressReason(string? reason)
        {
            return reason != null &&
                   reason.StartsWith("protectBossSuppress", StringComparison.Ordinal);
        }

        private static bool IsDogFightHealRetreatSuppressReason(string? reason)
        {
            return reason != null &&
                   reason.StartsWith("autoSuppress.dogFightHealRetreat", StringComparison.Ordinal);
        }

        public static bool IsAutonomousSuppressReason(string? reason)
        {
            return IsAutoSuppressReason(reason) ||
                   IsBossProtectionSuppressReason(reason) ||
                   FollowerCombatGrenadierObjective.IsAutonomousGrenadierReason(reason);
        }

        public AICoreActionEndStruct EndSuppressGrenade()
        {
            BotGrenadeController? grenades = botOwner.WeaponManager?.Grenades;
            if (grenades == null)
            {
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                ClearCommittedGrenade();
                return new AICoreActionEndStruct("grenadeControllerMissing", true);
            }

            float lastPeriod = botOwner.Brain?.Agent?.LastPeriod ?? Time.time;
            if (Time.time - lastPeriod > 6f)
            {
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                ClearCommittedGrenade();
                return new AICoreActionEndStruct("suppressGrenadeTimeout", true);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!FollowerGrenadeRuntimeGate.HasReleasedThrow(botOwner) &&
                IsGrenadeThrowUnsafe(goalEnemy))
            {
                AbortPendingGrenadeThrow(grenades);
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                ClearCommittedGrenade();
                return new AICoreActionEndStruct("suppressGrenadeUnsafe", true);
            }

            if (!FollowerGrenadeRuntimeGate.HasReleasedThrow(botOwner) &&
                IsPendingGrenadeImpactUnsafe(grenades, includeMovementPrediction: true, out string impactRejectReason))
            {
                BattleRecorder.RecordGrenadeEvent(
                    botOwner,
                    "abort",
                    $"friendlyImpact:{impactRejectReason}",
                    goalEnemy: goalEnemy,
                    target: grenades.AIGreanageThrowData?.Target);
                AbortPendingGrenadeThrow(grenades);
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                ClearCommittedGrenade();
                return new AICoreActionEndStruct("suppressGrenadeFriendlyImpact", true);
            }

            if (!FollowerGrenadeRuntimeGate.HasReleasedThrow(botOwner) &&
                grenades.AIGreanageThrowData != null &&
                FollowerShotSafety.IsRegularGrenadeTrajectoryUnsafeForThrower(
                    botOwner,
                    grenades,
                    grenades.AIGreanageThrowData,
                    out string trajectoryRejectReason))
            {
                BattleRecorder.RecordGrenadeEvent(
                    botOwner,
                    "abort",
                    trajectoryRejectReason,
                    goalEnemy: goalEnemy,
                    target: grenades.AIGreanageThrowData?.Target);
                AbortPendingGrenadeThrow(grenades);
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                ClearCommittedGrenade();
                return new AICoreActionEndStruct("suppressGrenadeTrajectoryUnsafe", true);
            }

            if (!HasAnyActiveCombatEnemy() &&
                (grenades.ThrowindNow || grenades.ReadyToThrow) &&
                !FollowerGrenadeRuntimeGate.HasReleasedThrow(botOwner))
            {
                AbortPendingGrenadeThrow(grenades);
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                ClearCommittedGrenade();
                return new AICoreActionEndStruct("suppressGrenadeCanceledNoEnemies", true);
            }

            if (grenades.ThrowindNow || grenades.ReadyToThrow)
            {
                return Continue();
            }

            if (botOwner.SuppressGrenade != null && !botOwner.SuppressGrenade.Complete)
            {
                return Continue();
            }

            ClearCommittedGrenade();
            return new AICoreActionEndStruct("suppressGrenadeComplete", true);
        }

        public AICoreActionEndStruct EndEnemySearch()
        {

            if (!botOwner.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (botOwner.Memory.GoalEnemy.CanShoot && botOwner.LookSensor.EnoughDistToShoot(out var info))
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            if (Time.time - botOwner.Memory.LastTimeHit <= 1f)
            {
                return new AICoreActionEndStruct("enemy.ShotMe", true);
            }

            if (botOwner.SearchData.SearchPoint == null)
            {
                return new AICoreActionEndStruct("search.End", true);
            }

            return Continue();
        }

        /// <summary>
        /// Initializes the vanilla suppress-grenade flow when the target has a reliable known position,
        /// is not already a clean gunfight, and the throw is safe for the boss/followers.
        /// </summary>
        public bool TryActivateFollowerGrenade(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            if (!pitFireTeam.botGrenades.Value)
            {
                return RejectFollowerGrenade("disabled", goalEnemy: goalEnemy);
            }

            if (FollowerGrenadeRuntimeGate.ShouldBlockThrowAttempt(botOwner, out string runtimeGateRejectReason))
            {
                return RejectFollowerGrenade(runtimeGateRejectReason, goalEnemy: goalEnemy);
            }

            if (!TryGetFollowerGrenadeTarget(goalEnemy, out Vector3 goalTargetPosition, out string? targetRejectReason))
            {
                return RejectFollowerGrenade(targetRejectReason ?? "targetPositionUnknown", goalEnemy: goalEnemy);
            }

            if (!IsFollowerRegularGrenadeEnvelopeDistance(goalTargetPosition, out string? distanceRejectReason))
            {
                return RejectFollowerGrenade(distanceRejectReason ?? "distance", goalEnemy: goalEnemy);
            }

            if (botOwner.WeaponManager == null || botOwner.WeaponManager.IsMelee)
            {
                return RejectFollowerGrenade("weaponUnavailable", goalEnemy: goalEnemy);
            }

            if (botOwner.BotRequestController == null)
            {
                return RejectFollowerGrenade("requestControllerMissing", goalEnemy: goalEnemy);
            }

            if (botOwner.BotRequestController.HaveActivatedRequests())
            {
                return RejectFollowerGrenade("activeRequest", goalEnemy: goalEnemy);
            }

            if (botOwner.Medecine.Using)
            {
                return RejectFollowerGrenade("medicine", goalEnemy: goalEnemy);
            }

            if (!TryValidateSafeGrenadeThrowPosition(goalEnemy, goalTargetPosition, out string safePositionRejectReason))
            {
                return RejectFollowerGrenade(safePositionRejectReason, goalEnemy: goalEnemy);
            }

            if (!FollowerGrenadeCooldowns.TryReserveThrow(botOwner))
            {
                return RejectFollowerGrenade("cooldown", goalEnemy: goalEnemy);
            }

            if (IsDogFightActive() ||
                botOwner.Memory.IsUnderFire ||
                WasHitRecently(botOwner, 2f) ||
                Time.time - goalEnemy.FirstTimeSeen < FollowerRegularGrenadeFreshContactDelaySeconds)
            {
                return RejectFollowerGrenade("dogfightOrPressure", goalEnemy, cancelPending: true);
            }

            if (FollowerImmediateFirePolicy.HasReliableImmediateFireLane(botOwner, goalEnemy))
            {
                return RejectFollowerGrenade("cleanShot", goalEnemy, cancelPending: true);
            }

            FollowerGrenadeRuntimeGate.EnableExplicitThrow(botOwner);
            if (botOwner.WeaponManager.Grenades == null ||
                botOwner.SuppressGrenade == null)
            {
                return RejectFollowerGrenade("grenadeControllerMissing", goalEnemy, cancelPending: true, disableGate: true);
            }

            EnemyInfo suppressEnemy = GetSuppressGrenadeTarget(goalEnemy, out ThrowWeapType? preferredThrowType);
            if (!TryGetFollowerGrenadeTarget(suppressEnemy, out Vector3 targetPosition, out targetRejectReason))
            {
                return RejectFollowerGrenade(targetRejectReason ?? "targetPositionUnknown", suppressEnemy, cancelPending: true, disableGate: true);
            }

            if (!IsFollowerRegularGrenadeEnvelopeDistance(targetPosition, out distanceRejectReason))
            {
                return RejectFollowerGrenade(distanceRejectReason ?? "distance", suppressEnemy, cancelPending: true, disableGate: true);
            }

            if (FollowerShotSafety.IsFriendlyNearGrenadeImpact(
                    botOwner,
                    targetPosition,
                    FollowerRegularGrenadeUnsafeRadius,
                    includeMovementPrediction: true,
                    out string impactRejectReason))
            {
                return RejectFollowerGrenade($"friendlyImpact:{impactRejectReason}", suppressEnemy, cancelPending: true, disableGate: true);
            }

            string? candidateRejectReason = null;

            if (preferredThrowType != null &&
                TryStartFollowerGrenadeCandidate(
                    suppressEnemy,
                    preferredThrowType.Value,
                    targetPosition,
                    "SupGrenade",
                    holdAfterThrow: true,
                    out decision,
                    out candidateRejectReason))
            {
                return true;
            }

            if (preferredThrowType != ThrowWeapType.frag_grenade)
            {
                if (TryStartFollowerGrenadeCandidate(
                    suppressEnemy,
                    ThrowWeapType.frag_grenade,
                    targetPosition,
                    "SupGrenade2",
                    holdAfterThrow: false,
                    out decision,
                    out string? fragRejectReason))
                {
                    return true;
                }

                candidateRejectReason = fragRejectReason ?? candidateRejectReason;
            }

            if (preferredThrowType != ThrowWeapType.stun_grenade)
            {
                if (TryStartFollowerGrenadeCandidate(
                    suppressEnemy,
                    ThrowWeapType.stun_grenade,
                    targetPosition,
                    "SupGrenade3",
                    holdAfterThrow: false,
                    out decision,
                    out string? stunRejectReason))
                {
                    return true;
                }

                candidateRejectReason = stunRejectReason ?? candidateRejectReason;
            }

            return RejectFollowerGrenade(candidateRejectReason ?? "initFailedOrNoGrenade", suppressEnemy, cancelPending: true, disableGate: true);
        }

        private bool TryStartFollowerGrenadeCandidate(
            EnemyInfo suppressEnemy,
            ThrowWeapType throwType,
            Vector3 targetPosition,
            string decisionReason,
            bool holdAfterThrow,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            out string? rejectReason)
        {
            decision = default;
            rejectReason = null;

            if (!TryGetFollowerGrenadeByType(throwType, out ThrowWeapItemClass? grenade) || grenade == null)
            {
                rejectReason = $"noGrenade:{throwType}";
                return false;
            }

            if (!IsFollowerRegularGrenadeDistance(targetPosition, grenade, out rejectReason))
            {
                RecordFollowerGrenadeReject(rejectReason ?? "distance", suppressEnemy);
                return false;
            }

            if (!TryInitFollowerSuppressGrenade(
                    suppressEnemy,
                    grenade,
                    throwType,
                    targetPosition,
                    out AIGreanageThrowData? throwData,
                    out string? trajectoryRejectReason))
            {
                rejectReason = trajectoryRejectReason ?? $"initFailed:{throwType}";
                return false;
            }

            if (holdAfterThrow)
            {
                HoldFor(botOwner.Settings.FileSettings.Boss.KILLA_AFTER_GRENADE_SUPPRESS_DELAY);
            }

            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.suppressGrenade, decisionReason);
            BattleRecorder.RecordGrenadeEvent(
                botOwner,
                "init",
                CreateFollowerGrenadeRecorderReason(decisionReason, grenade, targetPosition, throwData),
                goalEnemy: suppressEnemy,
                target: targetPosition);
            CommitGrenadeDecision(decision);
            return true;
        }

        private bool TryGetFollowerGrenadeTarget(EnemyInfo? enemyInfo, out Vector3 targetPosition, out string? rejectReason)
        {
            targetPosition = Vector3.zero;
            rejectReason = null;

            if (!IsTrackedEnemyAlive(enemyInfo))
            {
                rejectReason = "targetNotAlive";
                return false;
            }

            if (enemyInfo!.Person == null)
            {
                rejectReason = "targetPersonMissing";
                return false;
            }

            if (IsFriendlyGrenadeLauncherTarget(enemyInfo))
            {
                rejectReason = "targetFriendly";
                return false;
            }

            if (!TryGetGrenadeTargetPosition(enemyInfo, out targetPosition))
            {
                rejectReason = "targetPositionUnknown";
                return false;
            }

            return true;
        }

        private bool TryGetFollowerGrenadeByType(ThrowWeapType throwType, out ThrowWeapItemClass? grenade)
        {
            grenade = null;

            Inventory inventory = botOwner.GetPlayer?.InventoryController?.Inventory;
            if (inventory == null)
            {
                return false;
            }

            foreach (Item item in inventory.GetPlayerItems(EPlayerItems.Equipment))
            {
                if (item is ThrowWeapItemClass throwWeapItem &&
                    throwWeapItem.ThrowType == throwType)
                {
                    grenade = throwWeapItem;
                    return true;
                }
            }

            return false;
        }

        private bool IsFollowerRegularGrenadeEnvelopeDistance(Vector3 targetPosition, out string? rejectReason)
        {
            rejectReason = null;
            if (!IsFinite(targetPosition) || targetPosition.sqrMagnitude <= 0.01f)
            {
                rejectReason = "distanceInvalid";
                return false;
            }

            float targetDistanceSqr = (targetPosition - botOwner.Position).sqrMagnitude;
            if (targetDistanceSqr < FollowerRegularGrenadeMinDistance * FollowerRegularGrenadeMinDistance)
            {
                rejectReason = "distanceTooClose:min15";
                return false;
            }

            if (targetDistanceSqr > FollowerRegularGrenadeMaxDistance * FollowerRegularGrenadeMaxDistance)
            {
                rejectReason = "distanceTooFar:max40";
                return false;
            }

            return true;
        }

        private bool IsFollowerRegularGrenadeDistance(Vector3 targetPosition, ThrowWeapItemClass grenade, out string? rejectReason)
        {
            rejectReason = null;
            if (!IsFollowerRegularGrenadeEnvelopeDistance(targetPosition, out rejectReason))
            {
                return false;
            }

            float maxDistance = GetFollowerRegularGrenadeMaxDistance(grenade);
            float targetDistanceSqr = (targetPosition - botOwner.Position).sqrMagnitude;
            if (targetDistanceSqr > maxDistance * maxDistance)
            {
                rejectReason = $"distanceTooFar:{GetFollowerGrenadeTimerLabel(grenade)}:max{Mathf.RoundToInt(maxDistance)}";
                return false;
            }

            return true;
        }

        private float GetFollowerRegularGrenadeMaxDistance(ThrowWeapItemClass grenade)
        {
            if (IsFollowerImpactGrenade(grenade))
            {
                return FollowerRegularGrenadeMaxDistance;
            }

            float fuseRange = Mathf.InverseLerp(
                FollowerRegularTimedGrenadeMinFuseSeconds,
                FollowerRegularTimedGrenadeMaxFuseSeconds,
                GetFollowerGrenadeExplDelay(grenade));
            return Mathf.Lerp(
                FollowerRegularTimedGrenadeMinFuseMaxDistance,
                FollowerRegularGrenadeMaxDistance,
                fuseRange);
        }

        private bool IsFollowerImpactGrenade(ThrowWeapItemClass grenade)
        {
            return GetFollowerGrenadeExplDelay(grenade) <= FollowerRegularGrenadeImpactDelayThreshold;
        }

        private static float GetFollowerGrenadeExplDelay(ThrowWeapItemClass grenade)
        {
            return Mathf.Max(0f, grenade.GetExplDelay);
        }

        private string CreateFollowerGrenadeRecorderReason(
            string decisionReason,
            ThrowWeapItemClass grenade,
            Vector3 targetPosition,
            AIGreanageThrowData? throwData)
        {
            float distance = Vector3.Distance(botOwner.Position, targetPosition);
            float maxDistance = GetFollowerRegularGrenadeMaxDistance(grenade);
            string trajectory = throwData != null
                ? $":ang{Mathf.RoundToInt(throwData.Ang)}:flight{EstimateFollowerGrenadeFlightSeconds(throwData):0.0}"
                : string.Empty;
            return $"{decisionReason}:{grenade.ThrowType}:{GetFollowerGrenadeTimerLabel(grenade)}:d{Mathf.RoundToInt(distance)}:max{Mathf.RoundToInt(maxDistance)}{trajectory}";
        }

        private string GetFollowerGrenadeTimerLabel(ThrowWeapItemClass grenade)
        {
            if (IsFollowerImpactGrenade(grenade))
            {
                return "impact";
            }

            return $"fuse{Mathf.RoundToInt(GetFollowerGrenadeExplDelay(grenade) * 10f)}";
        }

        private bool TryInitFollowerSuppressGrenade(
            EnemyInfo suppressEnemy,
            ThrowWeapItemClass grenade,
            ThrowWeapType throwType,
            Vector3 targetPosition,
            out AIGreanageThrowData? throwData,
            out string? rejectReason)
        {
            throwData = null;
            rejectReason = null;
            if (botOwner.SuppressGrenade == null ||
                botOwner.WeaponManager?.Grenades == null ||
                !IsFinite(targetPosition))
            {
                rejectReason = $"initFailed:{throwType}";
                return false;
            }

            Vector3 throwTarget = targetPosition + Vector3.up * FollowerRegularGrenadeTargetHeight;
            if (!TryGetFollowerGrenadeThrowData(
                    grenade,
                    throwType,
                    throwTarget,
                    out throwData,
                    out rejectReason))
            {
                return false;
            }

            botOwner.SuppressGrenade.method_0(suppressEnemy, null);
            throwData!.GrenadeType = throwType;
            botOwner.WeaponManager.Grenades.SetThrowData(throwData);
            return true;
        }

        private bool TryGetFollowerGrenadeThrowData(
            ThrowWeapItemClass grenade,
            ThrowWeapType throwType,
            Vector3 throwTarget,
            out AIGreanageThrowData? throwData,
            out string? rejectReason)
        {
            throwData = null;
            rejectReason = null;
            BotGrenadeController? grenades = botOwner.WeaponManager?.Grenades;
            if (grenades == null)
            {
                rejectReason = $"grenadeControllerMissing:{throwType}";
                return false;
            }

            AIGreandeAng[] candidateAngles = GetFollowerGrenadeCandidateAngles(grenade);
            string? lastRejectReason = null;
            for (int i = 0; i < candidateAngles.Length; i++)
            {
                AIGreandeAng angle = candidateAngles[i];
                Vector3 throwOrigin = FollowerShotSafety.GetGrenadeThrowOrigin(botOwner, grenades);
                AIGreanageThrowData candidate = GClass577.CanThrowGrenade2(
                    throwOrigin,
                    throwTarget,
                    grenades,
                    angle,
                    botOwner.Settings.FileSettings.Grenade.MIN_THROW_GRENADE_DIST_SQRT);
                if (candidate == null || !candidate.CanThrow)
                {
                    lastRejectReason = $"trajectoryBlocked:{angle}";
                    continue;
                }

                if (FollowerShotSafety.IsRegularGrenadeTrajectoryUnsafeForThrower(
                        botOwner,
                        grenades,
                        candidate,
                        out string trajectoryRejectReason))
                {
                    lastRejectReason = trajectoryRejectReason;
                    continue;
                }

                float flightSeconds = EstimateFollowerGrenadeFlightSeconds(candidate);
                if (!IsFollowerGrenadeFlightSafe(grenade, flightSeconds))
                {
                    lastRejectReason =
                        $"airburstRisk:{angle}:flight{flightSeconds:0.0}:fuse{GetFollowerGrenadeExplDelay(grenade):0.0}";
                    continue;
                }

                throwData = candidate;
                return true;
            }

            rejectReason = lastRejectReason ?? $"trajectoryUnavailable:{throwType}";
            return false;
        }

        private static AIGreandeAng[] GetFollowerGrenadeCandidateAngles(ThrowWeapItemClass grenade)
        {
            if (GetFollowerGrenadeExplDelay(grenade) <= FollowerRegularGrenadeImpactDelayThreshold)
            {
                return new[]
                {
                    AIGreandeAng.ang15,
                    AIGreandeAng.ang25,
                    AIGreandeAng.ang35,
                    AIGreandeAng.ang45,
                    AIGreandeAng.ang55,
                    AIGreandeAng.ang65
                };
            }

            float fuseSeconds = GetFollowerGrenadeExplDelay(grenade);
            if (fuseSeconds < 2f)
            {
                return new[]
                {
                    AIGreandeAng.ang15,
                    AIGreandeAng.ang25,
                    AIGreandeAng.ang35
                };
            }

            if (fuseSeconds < 3f)
            {
                return new[]
                {
                    AIGreandeAng.ang15,
                    AIGreandeAng.ang25,
                    AIGreandeAng.ang35,
                    AIGreandeAng.ang45
                };
            }

            return new[]
            {
                AIGreandeAng.ang15,
                AIGreandeAng.ang25,
                AIGreandeAng.ang35,
                AIGreandeAng.ang45,
                AIGreandeAng.ang55
            };
        }

        private static bool IsFollowerGrenadeFlightSafe(ThrowWeapItemClass grenade, float flightSeconds)
        {
            if (GetFollowerGrenadeExplDelay(grenade) <= FollowerRegularGrenadeImpactDelayThreshold)
            {
                return true;
            }

            return flightSeconds > 0f &&
                   flightSeconds + FollowerRegularGrenadeAirburstFuseMarginSeconds <= GetFollowerGrenadeExplDelay(grenade);
        }

        private static float EstimateFollowerGrenadeFlightSeconds(AIGreanageThrowData throwData)
        {
            if (throwData == null ||
                !IsFinite(throwData.Position) ||
                !IsFinite(throwData.Target) ||
                throwData.Force <= 0.01f)
            {
                return 0f;
            }

            Vector3 offset = throwData.Target - throwData.Position;
            offset.y = 0f;
            float horizontalDistance = offset.magnitude;
            float angleRadians = throwData.Ang * Mathf.Deg2Rad;
            float solverSpeed = throwData.Force / 1.3f;
            float horizontalSpeed = solverSpeed * Mathf.Cos(angleRadians);
            return horizontalSpeed > 0.01f ? horizontalDistance / horizontalSpeed : 0f;
        }

        private bool TryValidateSafeGrenadeThrowPosition(EnemyInfo goalEnemy, Vector3 enemyAnchor, out string reason)
        {
            reason = "unsafePosition";
            if (goalEnemy == null || botOwner.Memory.IsUnderFire || WasHitRecently(botOwner, 2f))
            {
                reason = "unsafePosition:pressure";
                return false;
            }

            if (botOwner.Mover?.HasPathAndNoComplete == true &&
                botOwner.GoToSomePointData?.IsCome() != true)
            {
                reason = "unsafePosition:moving";
                return false;
            }

            if (!IsFinite(enemyAnchor))
            {
                reason = "unsafePosition:noTarget";
                return false;
            }

            if (!botOwner.Memory.IsInCover || botOwner.Memory.CurCustomCoverPoint == null)
            {
                reason = "unsafePosition:noCover";
                return false;
            }

            if (!botOwner.Memory.CurCustomCoverPoint.CanIHideFromPos(0f, true, false, enemyAnchor))
            {
                reason = "unsafePosition:coverExposed";
                return false;
            }

            return true;
        }

        private bool RejectFollowerGrenade(
            string reason,
            EnemyInfo? goalEnemy = null,
            bool cancelPending = false,
            bool disableGate = false)
        {
            if (cancelPending)
            {
                FollowerGrenadeCooldowns.CancelPending(botOwner);
            }

            if (disableGate)
            {
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
            }

            RecordFollowerGrenadeReject(reason, goalEnemy);
            return false;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordFollowerGrenadeReject(string reason, EnemyInfo? goalEnemy)
        {
            if (!BattleRecorder.IsRecordingFor(botOwner, requireRecordedCombat: true))
            {
                return;
            }

            if (string.Equals(lastFollowerGrenadeRejectReason, reason, StringComparison.Ordinal) &&
                Time.time < nextFollowerGrenadeRejectRecordAt)
            {
                return;
            }

            lastFollowerGrenadeRejectReason = reason;
            nextFollowerGrenadeRejectRecordAt = Time.time + FollowerRegularGrenadeRejectRecordSeconds;
            BattleRecorder.RecordGrenadeEvent(botOwner, "reject", reason, goalEnemy: goalEnemy);
        }

        private bool IsGrenadeThrowUnsafe(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null)
            {
                return true;
            }

            if (botOwner.Memory.IsUnderFire || WasHitRecently(botOwner, 2f))
            {
                return true;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            return false;
        }

        private bool IsPendingGrenadeImpactUnsafe(
            BotGrenadeController grenades,
            bool includeMovementPrediction,
            out string reason)
        {
            reason = string.Empty;
            Vector3? target = grenades?.AIGreanageThrowData?.Target;
            return target.HasValue &&
                   FollowerShotSafety.IsFriendlyNearGrenadeImpact(
                       botOwner,
                       target.Value,
                       FollowerRegularGrenadeUnsafeRadius,
                       includeMovementPrediction,
                       out reason);
        }

        private EnemyInfo GetSuppressGrenadeTarget(EnemyInfo goalEnemy, out ThrowWeapType? preferredThrowType)
        {
            preferredThrowType = null;
            if (botOwner.EnemiesController?.EnemyInfos == null)
            {
                return goalEnemy;
            }

            foreach (EnemyInfo enemyInfo in botOwner.EnemiesController.EnemyInfos.Values)
            {
                if (enemyInfo != goalEnemy && enemyInfo.IsSuppressed())
                {
                    preferredThrowType = ThrowWeapType.smoke_grenade;
                    return enemyInfo;
                }
            }

            return goalEnemy;
        }

        private void CancelActiveHealIfNeeded()
        {
            ClearCommittedHealCover();
            FollowerMedical.CancelActiveMedical(botOwner);
        }

        private void CompleteActiveHeal()
        {
            ClearCommittedHealCover();
            FollowerMedical.CompleteHealing(botOwner);
            healBlockUntil = Time.time + 5f;
            healStartedAt = 0f;
        }

        private void AbortActiveHeal()
        {
            ClearCommittedHealCover();
            FollowerMedical.AbortHealing(botOwner, recoverDestroyedSurgeryParts: true);
            healBlockUntil = Time.time + 5f;
            healStartedAt = 0f;
        }

        public AICoreActionEndStruct EndShootFromCover()
        {
            // Selection accepts either the normal EFT cover cast or a verified standing lane from
            // a shooting-cover commitment. End against that same contract so raising to fire cannot
            // become a select/end loop while EFT's crouched CanShoot flag is still false.
            if (CanShootFromCurrentCoverOrStandingIntent(out string cause))
            {
                shootFromCoverGraceUntil = Time.time + ShootFromCoverLosFlickerGraceSeconds;
                return Continue();
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (HasActiveCombatEnemy(goalEnemy) &&
                goalEnemy != null &&
                goalEnemy.IsVisible &&
                Time.time < shootFromCoverGraceUntil)
            {
                return Continue();
            }

            shootFromCoverGraceUntil = 0f;
            return new AICoreActionEndStruct(cause, true);
        }

        public AICoreActionEndStruct EndThrowGrenadeFromPlace()
        {
            BotRequest? currentRequest = botOwner.BotRequestController?.CurRequest;
            BotGrenadeController? grenades = botOwner.WeaponManager?.Grenades;
            if (grenades != null &&
                !HasAnyActiveCombatEnemy() &&
                (grenades.ThrowindNow || grenades.ReadyToThrow) &&
                !FollowerGrenadeRuntimeGate.HasReleasedThrow(botOwner))
            {
                AbortPendingGrenadeThrow(grenades);
                FollowerGrenadeCooldowns.CancelPending(botOwner);
                FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
                ClearCommittedGrenade();
                return new AICoreActionEndStruct("grenadeCanceledNoEnemies", true);
            }

            bool grenadeSequenceActive =
                grenades != null &&
                (grenades.ThrowindNow || grenades.ReadyToThrow);
            bool grenadeRequestActive =
                currentRequest?.BotRequestType == BotRequestType.throwGrenade ||
                currentRequest?.BotRequestType == BotRequestType.throwGrenadeFromPlace;
            if (grenadeSequenceActive || grenadeRequestActive)
            {

                return Continue();
            }

            ClearCommittedGrenade();
            return new AICoreActionEndStruct("grenadeRequestFinished", true);
        }

        private static void AbortPendingGrenadeThrow(BotGrenadeController grenades)
        {
            if (grenades?.AIGreanageThrowData != null)
            {
                grenades.method_6(null);
            }
        }

        public AICoreActionEndStruct EndBaseGoToPoint()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (goalEnemy.CanShoot)
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            if (botOwner.GoToSomePointData.IsCome())
            {
                ArmCommittedArrivalHold("goToPoint", preferCover: false);
                return new AICoreActionEndStruct("arrivedAtPoint", true);
            }

            return Continue();
        }

        public AICoreActionEndStruct EndBaseGoToEnemy()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemyMissingOrDead", true);
            }

            if (botOwner.Memory.IsUnderFire)
            {
                return new AICoreActionEndStruct("underFire", true);
            }

            if (ShouldBreakAdvanceForImmediateFire())
            {
                return new AICoreActionEndStruct("stableAdvanceFire", true);
            }

            // Raw visibility/can-shoot can flicker for a single frame. Only stop the committed
            // advance when the stable-fire gate above or an already-active dogfight owns the handoff.
            if (!IsDogFightActive())
            {
                return Continue();
            }

            return new AICoreActionEndStruct("dogFightConditionsMet", true);
        }

        public AICoreActionEndStruct EndBaseAttackMoving(string? reason = null)
        {
            bool isMoveToHeal = IsMedicalRetreatMovementReason(reason);

            if (!isMoveToHeal &&
                IsCommittedShootingCoverReason(reason) &&
                !HasActiveOrRetainedGoalEnemy(out _))
            {
                ClearCommittedCover();
                ClearCommittedMovement();
                return new AICoreActionEndStruct("shootCoverEnemyMissingOrDead", true);
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightActive", true);
            }

            if (botOwner.Memory.IsInCover)
            {
                if (!isMoveToHeal)
                {
                    HoldCoverForMaxDuration();
                    ArmCommittedArrivalHold(reason, preferCover: true);
                }
                return new AICoreActionEndStruct("inCover", true);
            }

            if (!isMoveToHeal && IsAtCommittedMovementDestination())
            {
                bool preferCover = committedMovementCoverId.HasValue || HasCommittedCover();
                if (preferCover)
                {
                    HoldCoverForMaxDuration();
                }

                ArmCommittedArrivalHold(reason ?? "attackMoving", preferCover);
                return new AICoreActionEndStruct("arrivedCommittedDestination", true);
            }

            if (botOwner.WeaponManager.Stationary.ShallEndShootFromCurrent())
            {
                return new AICoreActionEndStruct("stationary", true);
            }

            return Continue();
        }

        public void HoldFor(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            holdEndTime = Time.time + seconds;
            holdActive = true;
        }

        /// <summary>
        /// Hold in cover for a tactic/aggression-aware duration. Marksman holds longer; aggressive follows hold shorter.
        /// Use this instead of explicit seconds for follower combat hold decisions to respect tactic intent.
        /// </summary>
        public void HoldCoverForMaxDuration()
        {
            if (holdActive && holdEndTime > Time.time)
            {
                return;
            }

            HoldFor(GetMaxCoverHoldDuration());
        }

        public static bool IsStableNoCoverHoldReason(string reason)
        {
            return string.Equals(reason, "goalEnemy.P", StringComparison.Ordinal) ||
                   string.Equals(reason, "canShootLas", StringComparison.Ordinal) ||
                   string.Equals(reason, "deltaLastHi", StringComparison.Ordinal) ||
                   string.Equals(reason, "unsafePushBossHold", StringComparison.Ordinal) ||
                   string.Equals(reason, "escortNoSafeCover", StringComparison.Ordinal) ||
                   string.Equals(reason, "bossHoldOpen", StringComparison.Ordinal) ||
                   string.Equals(reason, "reloadNoCover", StringComparison.Ordinal) ||
                   string.Equals(reason, RecoveryNoCoverThreatHoldReason, StringComparison.Ordinal) ||
                   FollowerCombatPush.IsNoPushHoldReason(reason) ||
                   reason.StartsWith("committedPositionHold", StringComparison.Ordinal) ||
                   reason.StartsWith("committedCoverHold", StringComparison.Ordinal) ||
                   FollowerCombatPush.IsPushReason(reason);
        }

        public static bool IsTargetHandoffScanReason(string? reason)
        {
            return string.Equals(reason, TargetHandoffScanReason, StringComparison.Ordinal);
        }

        public AICoreActionEndStruct EndBaseHoldPosition(string reason)
        {
            if (HasActiveCombatGestureOrder())
            {
                if (string.Equals(reason, TargetHandoffScanReason, StringComparison.Ordinal))
                {
                    ClearTargetHandoffScan("combatGestureOrder");
                }

                return new AICoreActionEndStruct("combatGestureBreakHold", true);
            }

            if (string.Equals(reason, TargetHandoffScanReason, StringComparison.Ordinal))
            {
                return EndTargetHandoffScan();
            }

            if (IsReloadHoldReason(reason))
            {
                return EndReloadHold(reason);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            bool wasHitRecently =
                WasHitRecently(botOwner, 0.75f) ||
                FollowerAwareness.WasRecentlyDamaged(botOwner);
            if (wasHitRecently)
            {
                return new AICoreActionEndStruct("hitHold", true);
            }

            bool underFirePressure = HasRecoveryPressure(0.75f);
            if (underFirePressure && botOwner.Memory.IsInCover)
            {
                HoldCoverForMaxDuration();
            }

            if (holdActive && holdEndTime < Time.time)
            {
                holdActive = false;
                return new AICoreActionEndStruct("holdExpired", true);
            }

            if (!botOwner.Memory.IsInCover)
            {
                if (!IsStableNoCoverHoldReason(reason))
                {
                    return new AICoreActionEndStruct("notInCover", true);
                }

                // No-cover hold reasons are allowed to crouch-wait, but not under active pressure.
                if (underFirePressure)
                {
                    return new AICoreActionEndStruct("underFireNoCover", true);
                }
            }

            if (goalEnemy == null)
            {
                return new AICoreActionEndStruct("canSearchEnemy", true);
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return new AICoreActionEndStruct("enemyVisibleAndShootable", true);
            }

            if (goalEnemy.IsVisible &&
                goalEnemy.Distance < botOwner.Settings.FileSettings.Cover.END_HOLD_IF_ENEMY_CLOSE_AND_VISIBLE)
            {
                return new AICoreActionEndStruct("enemyCloseAndVisible", true);
            }

            return Continue();
        }

        public bool TryGetCommittedRecoveryDecision(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
            bool ignoreCommittedPosition = false)
        {
            decision = default;
            if (!ignoreCommittedPosition && HasCommittedPosition(out decision))
            {
                return true;
            }

            bool reachedCommittedCover = HasCommittedCover() && IsBotInCommittedCover();

            if (!TryCommitCombatCover(
                    goalEnemy,
                    requireShootLane: goalEnemy.IsVisible && goalEnemy.CanShoot,
                    CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius(),
                    out _,
                    recoveryManeuver: true))
            {
                return false;
            }

            // Proximity alone cannot promote an old firing position into recovery hold. The cover
            // must first survive the same recovery validation/requalification used for a fresh
            // candidate; a rejected old commitment is replaced above before this arrival check.
            if (reachedCommittedCover && IsBotInCommittedCover())
            {
                ArmCommittedRecoveryArrivalHold(CommittedCoverReason ?? "retreatSafeCover");
                return HasCommittedPosition(out decision);
            }

            decision = CreateCommittedCoverMoveDecision();
            return true;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> CreateNoCoverRecoveryDecision(
            EnemyInfo goalEnemy)
        {
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.shootFromPlace,
                    RecoveryNoCoverFightReason);
            }

            if (TryCreateSuppressDecision(
                    goalEnemy,
                    RecoveryNoCoverSuppressReason,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> suppressDecision,
                    allowObstructedSuppression: true))
            {
                return suppressDecision;
            }

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                RecoveryNoCoverThreatHoldReason);
        }

        public void UpdateRecoveryNoCoverCommitment(
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (IsRecoveryNoCoverReason(nextDecision.Reason))
            {
                if (string.Equals(activeRecoveryNoCoverReason, nextDecision.Reason, StringComparison.Ordinal) &&
                    recoveryNoCoverUntil > Time.time)
                {
                    return;
                }

                activeRecoveryNoCoverReason = nextDecision.Reason;
                recoveryNoCoverUntil = Time.time + RecoveryNoCoverCommitSeconds;
                recoveryNoCoverEnemyProfileId = botOwner.Memory?.GoalEnemy?.ProfileId ?? string.Empty;
                recoveryNoCoverDamageRevision = FollowerAwareness.GetDamageRevision(botOwner);
                BattleRecorder.RecordCommitmentEvent(
                    botOwner,
                    "recovery",
                    "beginNoCover",
                    nextDecision.Reason,
                    nextDecision,
                    untilTime: recoveryNoCoverUntil);
                return;
            }

            if (activeRecoveryNoCoverReason == null)
            {
                return;
            }

            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "recovery",
                "clearNoCover",
                activeRecoveryNoCoverReason);
            ClearRecoveryNoCoverCommitment();
        }

        public AICoreActionEndStruct EndRecoveryNoCoverThreatHold()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("recoveryEnemyMissingOrDead", true);
            }

            if (string.Equals(
                    activeRecoveryNoCoverReason,
                    RecoveryNoCoverThreatHoldReason,
                    StringComparison.Ordinal))
            {
                string currentEnemyProfileId = goalEnemy!.ProfileId ?? string.Empty;
                if (!string.Equals(
                        currentEnemyProfileId,
                        recoveryNoCoverEnemyProfileId,
                        StringComparison.Ordinal))
                {
                    return InterruptRecoveryNoCoverCommitment("recoveryThreatChanged");
                }

                if (FollowerAwareness.GetDamageRevision(botOwner) != recoveryNoCoverDamageRevision)
                {
                    return InterruptRecoveryNoCoverCommitment("recoveryFreshDamage");
                }
            }

            if (IsDogFightActive() ||
                (goalEnemy!.IsVisible &&
                 (goalEnemy.CanShoot || goalEnemy.Distance <= RecoveryNoCoverPointBlankBreakDistance)))
            {
                return new AICoreActionEndStruct("recoveryFightAvailable", true);
            }

            return Time.time >= recoveryNoCoverUntil
                ? new AICoreActionEndStruct("recoveryCoverRetry", true)
                : Continue();
        }

        public AICoreActionEndStruct EndRecoveryNoCoverSuppress(string reason)
        {
            AICoreActionEndStruct result = EndSuppressFire(reason);
            if (result.Value &&
                (string.Equals(result.Reason, "enemyMissingOrDead", StringComparison.Ordinal) ||
                 string.Equals(result.Reason, "shootImmediately", StringComparison.Ordinal) ||
                 string.Equals(result.Reason, "dogFightStarted", StringComparison.Ordinal) ||
                 string.Equals(result.Reason, "pointBlankNonFoliageContact", StringComparison.Ordinal)))
            {
                return result;
            }

            return Time.time >= recoveryNoCoverUntil
                ? new AICoreActionEndStruct("recoveryCoverRetry", true)
                : Continue();
        }

        private AICoreActionEndStruct InterruptRecoveryNoCoverCommitment(string reason)
        {
            BattleRecorder.RecordCommitmentEvent(
                botOwner,
                "recovery",
                "interruptNoCover",
                reason);
            ClearRecoveryNoCoverCommitment();
            return new AICoreActionEndStruct(reason, true);
        }

        private void ClearRecoveryNoCoverCommitment()
        {
            activeRecoveryNoCoverReason = null;
            recoveryNoCoverUntil = 0f;
            recoveryNoCoverEnemyProfileId = string.Empty;
            recoveryNoCoverDamageRevision = 0;
        }

        public void ResetRecoveryNoCoverCommitment()
        {
            ClearRecoveryNoCoverCommitment();
        }

        public bool HasRecoveryPressure(float recentHitSeconds = 1f)
        {
            return botOwner.Memory.IsUnderFire ||
                   WasHitRecently(botOwner, recentHitSeconds) ||
                   FollowerAwareness.WasRecentlyDamaged(botOwner) ||
                   FollowerAwareness.WasRecentlyThreatened(botOwner);
        }

        /// <summary>
        /// Convenience terminal result for decisions that always end in one update.
        /// </summary>
        public static AICoreActionEndStruct EndImmediately() => new AICoreActionEndStruct(string.Empty, true);

        public static AICoreActionEndStruct Continue() => default;

        /// <summary>
        /// Determines if heal cover should be cleared due to improved health, increased threat, or exceeded duration.
        /// </summary>
        private bool ShouldClearHealCover(EnemyInfo? goalEnemy, out string? clearReason)
        {
            clearReason = null;

            if (committedHealCover == null)
            {
                return false;
            }

            // Exit if health status now healthy (healed enough to rejoin)
            if (botOwner.GetPlayer?.HealthStatus == ETagStatus.Healthy)
            {
                clearReason = "healthy";
                return true;
            }

            // Exit if enemy pushed closer (cover ineffective against new threat)
            if (goalEnemy != null && goalEnemy.IsVisible)
            {
                float enemyDist = Vector3.Distance(botOwner.Position, goalEnemy.CurrPosition);
                if (enemyDist < CombatDistanceConfiguration.Instance.GetHealCoverRetreatDistance() * 0.6f)  // Enemy too close relative to retreat distance
                {
                    clearReason = "enemyClose";
                    return true;
                }
            }

            // Exit if heal cycle exceeded reasonable max duration (prevents indefinite heal holds)
            const float MaxHealDurationSeconds = 20f;
            if (Time.time - healStartedAt > MaxHealDurationSeconds)
            {
                clearReason = "timeout";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the maximum time a follower should hold in cover based on tactic and aggression.
        /// Marksman holds longer to provide sniper support; defensive followers hold at assigned positions.
        /// </summary>
        private float GetMaxCoverHoldDuration()
        {
            FollowerCombatTactic tactic = GetFollowerTactic();
            float aggression = GetAggression01();

            // Base hold duration by tactic
            float baseDuration = tactic switch
            {
                FollowerCombatTactic.Marksman => 12f,      // Snipers hold longer for optimal shots and teammate support
                FollowerCombatTactic.Protector => 8f,      // Defensive followers hold their assigned position
                FollowerCombatTactic.Balanced => 6f,       // Balanced, more active repositioning
                _ => 6f
            };

            // Aggression multiplier (lower aggression = longer hold, higher = shorter)
            // Range: 1.5x (very passive) to 1.0x (very aggressive)
            float aggressionMultiplier = 1f + (0.5f * (1f - Mathf.Clamp01(aggression)));

            return baseDuration * aggressionMultiplier;
        }
    }
}
