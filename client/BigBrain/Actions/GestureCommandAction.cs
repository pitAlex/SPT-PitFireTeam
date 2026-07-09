using Comfort.Common;
using Diz.LanguageExtensions;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using EFT.UI;
using EFT.UI.DragAndDrop;
using JsonType;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Patches;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Executes boss-issued follower commands outside the combat objective system. It owns command
    /// movement, hold/come/there/regroup behavior, loot and door interaction, and command cleanup
    /// when combat or another command interrupts the active task.
    /// </summary>
    internal partial class GestureCommandAction : CustomLogic
    {
        private BotFollowerPlayer? followerData;
        private float nextPathCheckAt;
        private bool moveCommandInitialized;
        private float nextHoldLookChangeAt;
        private Vector3 holdLookPoint;
        private float moveArrivalLookUntil;
        private float comeArrivalHoldUntil;
        private Vector3 activeMoveTarget;
        private bool comeTargetInitialized;
        private Vector3 comeTarget;
        private bool comePoseInitialized;
        private float comeMovePose = 1f;
        private bool regroupTargetInitialized;
        private Vector3 regroupTarget;
        private float nextRegroupRefreshAt;
        private bool regroupRunMode;
        private bool regroupReportedOnPosition;
        private bool regroupBossAnchorInitialized;
        private Vector3 regroupBossAnchorPosition;
        private float nextRegroupBossAnchorCheckAt;
        private bool lootPickupInProgress;
        private float lootPickupReadyAt;
        private float lootPickupAttemptStartedAt;
        private LootItem? activeLootItem;
        private LootableContainer? activeLootContainer;
        private bool containerLootMoveInProgress;
        private float containerLootReadyAt;
        private float containerLootNextMoveAt;
        private float containerLootAttemptStartedAt;
        private BodyGearMove? pendingContainerLootMove;
        private float pendingContainerLootMoveReadyAt;
        private readonly Queue<BodyGearCandidate> pendingContainerGearSwapFollowUps = new Queue<BodyGearCandidate>();
        private int containerLootMovesSucceeded;
        private int containerLootReportedMovesSucceeded;
        private bool containerLootGenericSpoken;
        private bool containerLootWeaponListDirty;
        private bool containerLootOpened;
        private float containerLootOpenRequestedAt;
        private bool containerLootSearchStarted;
        private readonly HashSet<string> containerLootAttemptedItemIds = new HashSet<string>(StringComparer.Ordinal);
        private Corpse? activeBodyLootCorpse;
        private bool bodyLootMoveInProgress;
        private float bodyLootReadyAt;
        private float bodyLootNextMoveAt;
        private float bodyLootAttemptStartedAt;
        private BodyGearMove? pendingBodyLootMove;
        private float pendingBodyLootMoveReadyAt;
        private readonly Queue<BodyGearCandidate> pendingBodyGearSwapFollowUps = new Queue<BodyGearCandidate>();
        private int bodyLootMovesSucceeded;
        private int bodyLootReportedMovesSucceeded;
        private bool bodyLootGenericSpoken;
        private bool bodyLootWeaponListDirty;
        private bool bodyLootBackpackCapacityAttempted;
        private bool bodyLootSearchStarted;
        private BetterSource? activeLootSearchSource;
        private readonly HashSet<string> bodyLootAttemptedItemIds = new HashSet<string>(StringComparer.Ordinal);
        private Door? activeDoor;
        private bool doorMoveIssued;
        private bool doorInteractIssued;
        private float doorTimeoutAt;
        private FollowerCommandType lastCommand = FollowerCommandType.None;
        private int lastMoveToPointIssueSequence = -1;
        private float moveLastProgressDistance;
        private float moveLastProgressAt;
        private float nextMoveProgressDiagnosticAt;
        private string lastMoveDiagnosticKey = string.Empty;
        private float nextMoveDiagnosticAt;
        private const float RegroupArriveNavDistance = 4f;
        private const float RegroupRunDistance = 10f;
        private const float SameLevelTolerance = 1.75f;
        private const float RegroupCoverSearchRadius = 15f;
        private const float RegroupRandomRadius = 6f;
        private const float RegroupReservationSpacing = 1.5f;
        private const float RegroupReservationTtl = 2f;
        private const float MoveToPointArrivalDistance = 1.5f;
        private const float MoveToPointForcedArrivalDistance = 0.75f;
        private const float MoveToPointTargetChangeDistanceSqr = 0.25f;
        private const float MoveToPointProgressEpsilon = 0.25f;
        private const float MoveToPointNoProgressSeconds = 1.5f;
        private const float MoveToPointDiagnosticThrottleSeconds = 1f;
        private const float LootSearchDelayBaseSeconds = 1.50f;
        private const float LootSearchDelayPerSqrtCellSeconds = 1f;
        private const float LootSearchDelayMinSeconds = 1.75f;
        private const float LootSearchDelayMaxSeconds = 6.25f;
        private const float LootPickupSuccessLeadSeconds = 1.1f;
        private const float LootContainerOpenTimeoutSeconds = 3f;

        public GestureCommandAction(BotOwner botOwner) : base(botOwner) { }

        public override void Start()
        {
            followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            StopLootSearchSound();
            ReleaseRegroupReservation();
            nextPathCheckAt = 0f;
            moveCommandInitialized = false;
            nextHoldLookChangeAt = 0f;
            holdLookPoint = Vector3.zero;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            activeMoveTarget = Vector3.zero;
            comeTargetInitialized = false;
            comeTarget = Vector3.zero;
            comePoseInitialized = false;
            comeMovePose = 1f;
            regroupTargetInitialized = false;
            regroupTarget = Vector3.zero;
            nextRegroupRefreshAt = 0f;
            regroupRunMode = false;
            regroupReportedOnPosition = false;
            regroupBossAnchorInitialized = false;
            regroupBossAnchorPosition = Vector3.zero;
            nextRegroupBossAnchorCheckAt = 0f;
            lootPickupInProgress = false;
            lootPickupReadyAt = 0f;
            lootPickupAttemptStartedAt = 0f;
            activeLootItem = null;
            activeLootContainer = null;
            containerLootMoveInProgress = false;
            containerLootReadyAt = 0f;
            containerLootNextMoveAt = 0f;
            containerLootAttemptStartedAt = 0f;
            pendingContainerLootMove = null;
            pendingContainerLootMoveReadyAt = 0f;
            pendingContainerGearSwapFollowUps.Clear();
            containerLootMovesSucceeded = 0;
            containerLootReportedMovesSucceeded = 0;
            containerLootGenericSpoken = false;
            containerLootWeaponListDirty = false;
            containerLootOpened = false;
            containerLootOpenRequestedAt = 0f;
            containerLootSearchStarted = false;
            containerLootAttemptedItemIds.Clear();
            activeBodyLootCorpse = null;
            bodyLootMoveInProgress = false;
            bodyLootReadyAt = 0f;
            bodyLootNextMoveAt = 0f;
            bodyLootAttemptStartedAt = 0f;
            pendingBodyLootMove = null;
            pendingBodyLootMoveReadyAt = 0f;
            pendingBodyGearSwapFollowUps.Clear();
            bodyLootMovesSucceeded = 0;
            bodyLootReportedMovesSucceeded = 0;
            bodyLootGenericSpoken = false;
            bodyLootWeaponListDirty = false;
            bodyLootBackpackCapacityAttempted = false;
            bodyLootSearchStarted = false;
            bodyLootAttemptedItemIds.Clear();
            activeDoor = null;
            doorMoveIssued = false;
            doorInteractIssued = false;
            doorTimeoutAt = 0f;
            lastCommand = FollowerCommandType.None;
            lastMoveToPointIssueSequence = -1;
            ResetMoveToPointDiagnostics();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null || !followerData.TryGetActiveCommand(out FollowerCommandType command, out Vector3 target))
            {
                ReleaseRegroupReservation();
                lastCommand = FollowerCommandType.None;
                lastMoveToPointIssueSequence = -1;
                ResetMoveToPointDiagnostics();
                return;
            }

            EnsureCommandControl();
            bool moveToPointReissued = command == FollowerCommandType.MoveToPoint &&
                                        followerData.MoveToPointIssueSequence != lastMoveToPointIssueSequence;
            bool commandInstanceChanged = command != lastCommand || moveToPointReissued;

            // Request-layer commands are lower priority than real combat contact. If the command can
            // no longer safely continue, clear interaction state and let combat/patrol take over.
            if (ShouldInterruptCommandForCombat(command))
            {
                ReleaseRegroupReservation();
                CleanupLootInteraction($"CommandInterrupt:{command}");
                CleanupBodyLootInteraction($"CommandInterrupt:{command}");
                CleanupContainerLootInteraction($"CommandInterrupt:{command}");
                CleanupDoorInteraction();
                followerData?.ClearCommand($"CommandInterrupt:{command}");
                BotOwner.StopMove();
                BotOwner.SetPose(1f);
                lastCommand = FollowerCommandType.None;
                lastMoveToPointIssueSequence = -1;
                ResetMoveToPointDiagnostics();
                return;
            }

            if (commandInstanceChanged)
            {
                // Command changes must release resources owned by the previous command. This avoids
                // stale regroup reservations, loot pickup state, or door interaction state carrying
                // into a different command.
                if (lastCommand == FollowerCommandType.RegroupNearBoss)
                {
                    ReleaseRegroupReservation();
                }

                if (lastCommand == FollowerCommandType.TakeLootItem)
                {
                    CleanupLootInteraction($"CommandChanged:{command}");
                }

                if (lastCommand == FollowerCommandType.TakeBodyGear)
                {
                    CleanupBodyLootInteraction($"CommandChanged:{command}");
                }

                if (lastCommand == FollowerCommandType.TakeContainerLoot)
                {
                    CleanupContainerLootInteraction($"CommandChanged:{command}");
                }

                comeTargetInitialized = false;
                comeTarget = Vector3.zero;
                comePoseInitialized = false;
                comeMovePose = 1f;
                regroupTargetInitialized = false;
                regroupTarget = Vector3.zero;
                nextRegroupRefreshAt = 0f;
                regroupReportedOnPosition = false;
                regroupBossAnchorInitialized = false;
                regroupBossAnchorPosition = Vector3.zero;
                nextRegroupBossAnchorCheckAt = 0f;
                lootPickupInProgress = false;
                lootPickupReadyAt = 0f;
                lootPickupAttemptStartedAt = 0f;
                activeLootItem = null;
                activeLootContainer = null;
                containerLootMoveInProgress = false;
                containerLootReadyAt = 0f;
                containerLootNextMoveAt = 0f;
                containerLootAttemptStartedAt = 0f;
                pendingContainerLootMove = null;
                pendingContainerLootMoveReadyAt = 0f;
                pendingContainerGearSwapFollowUps.Clear();
                containerLootMovesSucceeded = 0;
                containerLootReportedMovesSucceeded = 0;
                containerLootGenericSpoken = false;
                containerLootWeaponListDirty = false;
                containerLootOpened = false;
                containerLootOpenRequestedAt = 0f;
                containerLootSearchStarted = false;
                containerLootAttemptedItemIds.Clear();
                activeBodyLootCorpse = null;
                bodyLootMoveInProgress = false;
                bodyLootReadyAt = 0f;
                bodyLootNextMoveAt = 0f;
                bodyLootAttemptStartedAt = 0f;
                pendingBodyLootMove = null;
                pendingBodyLootMoveReadyAt = 0f;
                pendingBodyGearSwapFollowUps.Clear();
                bodyLootMovesSucceeded = 0;
                bodyLootReportedMovesSucceeded = 0;
                bodyLootGenericSpoken = false;
                bodyLootWeaponListDirty = false;
                bodyLootBackpackCapacityAttempted = false;
                bodyLootSearchStarted = false;
                bodyLootAttemptedItemIds.Clear();
                CleanupDoorInteraction();

                if (command == FollowerCommandType.MoveToPoint)
                {
                    ResetMoveToPointState();
                    float commandDistance = (target - BotOwner.Position).magnitude;
                    RecordMoveToPointDiagnostic("commandStart", target, commandDistance, () => CreateMoveToPointDiagnostic(target, commandDistance));
                }
            }

            // Dispatcher only: each command scenario lives in a focused partial file so the
            // request layer stays readable while shared state cleanup remains centralized here.
            switch (command)
            {
                case FollowerCommandType.HoldPosition:
                    HandleHoldPosition();
                    break;

                case FollowerCommandType.ComeCloser:
                    HandleComeCloser();
                    break;

                case FollowerCommandType.MoveToPoint:
                    HandleMoveToPoint(target);
                    break;

                case FollowerCommandType.RegroupNearBoss:
                    HandleRegroupNearBoss();
                    break;

                case FollowerCommandType.TakeLootItem:
                    HandleTakeLootItem();
                    break;

                case FollowerCommandType.TakeBodyGear:
                    HandleTakeBodyGear();
                    break;

                case FollowerCommandType.TakeContainerLoot:
                    HandleTakeContainerLoot();
                    break;

                case FollowerCommandType.OpenDoor:
                    HandleOpenDoor();
                    break;
            }

            if (commandInstanceChanged)
            {
                lastCommand = command;
                lastMoveToPointIssueSequence = followerData.MoveToPointIssueSequence;
                if (
                    command == FollowerCommandType.MoveToPoint ||
                    command == FollowerCommandType.ComeCloser
                )
                {
                    BotOwner.Steering.LookToMovingDirection();
                }
            }
        }

        public override void Stop()
        {
            bool botInvalid = BotOwner == null ||
                              BotOwner.IsDead ||
                              BotOwner.BotState != EBotState.Active ||
                              BotOwner.GetPlayer?.HealthController?.IsAlive != true;

            if (followerData?.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) != true ||
                botInvalid ||
                command != FollowerCommandType.TakeLootItem)
            {
                CleanupLootInteraction("TakeLoot:actionStop");
            }

            if (followerData?.TryPeekActiveCommand(out FollowerCommandType bodyCommand, out _, out _) != true ||
                botInvalid ||
                bodyCommand != FollowerCommandType.TakeBodyGear)
            {
                CleanupBodyLootInteraction("TakeBodyGear:actionStop");
            }

            if (followerData?.TryPeekActiveCommand(out FollowerCommandType containerCommand, out _, out _) != true ||
                botInvalid ||
                containerCommand != FollowerCommandType.TakeContainerLoot)
            {
                CleanupContainerLootInteraction("TakeContainerLoot:actionStop");
            }

            CleanupDoorInteraction();
            base.Stop();
        }

    }
}
