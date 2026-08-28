using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using pitTeam.BigBrain.Actions;
using pitTeam.Components;
using pitTeam.Modules;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace pitTeam.BigBrain
{
    internal static class FollowerLayerRegistry
    {
        private static bool initialized;
        private const int FollowerRequestLayerPriority = 73;
        private const int FollowerLayerPriority = 71;
        private const int FollowerCombatLayerPriority = 72;

        public static void Init()
        {
            if (initialized) return;
            initialized = true;

            List<string> brains = new List<string>
            {
                "PmcBear",
                "PmcUsec",
                "ExUsec",
                "PMC",
                "Assault",
                "Obdolbs",
                "CursAssault",
                "Knight",
                "BigPipe",
                "BirdEye"
            };

            List<string> pmcCombatBrains = new List<string>
            {
                "PmcBear",
                "PmcUsec",
                "ExUsec",
                "PMC",
                "Assault",
                "Obdolbs",
                "CursAssault"
            };

            List<string> vanillaLayersToDisable = new List<string>
            {
                "FightReqNull",
                "PeacecReqNull"
            };

            try
            {
                BrainManager.RemoveLayers(vanillaLayersToDisable, brains);
                BrainManager.AddCustomLayer(typeof(FollowerCombatLayer), pmcCombatBrains, FollowerCombatLayerPriority);
                BrainManager.AddCustomLayer(typeof(FollowerRequestLayer), brains, FollowerRequestLayerPriority);
                BrainManager.AddCustomLayer(typeof(FollowerPatrolLayer), brains, FollowerLayerPriority);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"Failed to register follower patrol layer for brains: {string.Join(", ", brains)}");
                Modules.Logger.LogError(ex);
            }
        }

    }

    internal sealed class FollowerPatrolLayer : CustomLayer
    {
        private const float OutOfCombatReloadInitialCooldown = 1f;
        private const float OutOfCombatReloadCheckInterval = 3f;
        private const float OutOfCombatReloadActionCooldown = 5f;
        private const float OutOfCombatReloadSlotCooldown = 2f;
        private const float OutOfCombatReloadFullCycleCooldown = 30f;
        private const float OutOfCombatReloadGiveUpCooldown = 300f;
        private const int OutOfCombatReloadMaxSwitchAttemptsPerWeapon = 2;
        private const int OutOfCombatReloadMaxFailedReloadsPerWeapon = 2;
        private const float OutOfCombatTacticalDeviceInitialCooldown = 0.75f;
        private const float OutOfCombatTacticalDeviceCheckInterval = 3f;
        private const float HealNodeStartTimeout = 4f;
        private const float HealStartCheckInterval = 0.25f;
        private const float PatrolHealCoverSearchRadius = 60f;
        private const float PatrolHealCoverArriveDistance = 2f;
        private const string PatrolHealCoverActionReason = "runToHeal";
        private const string PatrolHealWaitActionReason = "healCooldownWait";

        private static readonly EquipmentSlot[] ReloadSlotOrder =
        {
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.Holster
        };

        private static readonly EquipmentSlot[] StowedTacticalDeviceSlotOrder =
        {
            EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.Holster
        };

        private float _nextErrorLogAt;

        private float healSoftTimeoutAt = 0f;
        private float healNodeEnteredAt = 0f;
        private bool isHealing = false;
        private bool triedFillMagazines = false;
        private bool reloadingInProgress = false;
        private float nextReloadCheckAt = 0f;
        private float nextMagazineFillCheckAt = 0f;
        private readonly HashSet<EquipmentSlot> reloadSlotsTried = new HashSet<EquipmentSlot>();
        private readonly HashSet<string> reloadWeaponsProcessed = new HashSet<string>();
        private readonly Dictionary<EquipmentSlot, float> reloadSlotRetryAfter = new Dictionary<EquipmentSlot, float>();
        private readonly Dictionary<string, int> reloadSwitchAttemptsByWeapon = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> reloadFailuresByWeapon = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> reloadGiveUpUntilByWeapon = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly HashSet<string> stowedTacticalDeviceWeaponsProcessed = new HashSet<string>(StringComparer.Ordinal);
        private EquipmentSlot? forcedTopOffSlot = null;
        private EquipmentSlot? returnAfterTopOffSlot = null;
        private string? reloadingWeaponId = null;
        private float nextPatrolLauncherFallbackRecordAt = 0f;
        private float nextStowedTacticalDeviceCheckAt = 0f;
        private float nextHealWorkRefreshAt = 0f;
        private float nextHealStartCheckAt = 0f;
        private bool stoppedForHealDecision = false;
        private bool healUseObserved = false;
        private CustomNavigationPoint? patrolHealCover;
        private bool patrolHealCoverSearchDone;
        private bool patrolHealCoverFallback;
        private bool patrolHealStartAnnounced;
        private BotFollowerPlayer? followerData;

        private Action? selectedAction = null;
        public FollowerPatrolLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
        }

        public override string GetName()
        {
            return "pitTeam.FollowerPatrol";
        }

        public override bool IsActive()
        {
            if (BotOwner == null || BotOwner.BotState != EBotState.Active || BotOwner.GetPlayer == null || !BotOwner.GetPlayer.HealthController.IsAlive)
            {
                return false;
            }

            if (!BossPlayers.IsFollower(BotOwner))
            {
                return false;
            }

            bool isHealAction = selectedAction?.Type == typeof(HealAction);
            bool isHealWaitAction = selectedAction?.Type == typeof(PatrolHealWaitAction);
            bool isHealDecision = BotOwner.Brain.Agent?.LastResult().Action == BotLogicDecision.heal;
            bool isUsingMedical = Utils.FollowerMedical.IsUsingMedical(BotOwner);

            // Preserve an actual medical use, but do not let a stale Heal action/LastResult own
            // patrol forever after EFT has stopped using the item. Once no medical use is active,
            // normal enemy, command, and post-combat readiness checks must be allowed to run.
            if (isUsingMedical && (isHealAction || isHealDecision && !isHealWaitAction))
            {
                return true;
            }

            if (!BotOwner.BotFollower.HaveBoss) return false;
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer) return false;

            if (BotOwner.Memory.HaveEnemy)
            {
                return false;
            }

            if (HasVisibleKnownEnemy())
            {
                return false;
            }

            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null)
            {
                return false;
            }

            if (followerData.IsBackpackInspectionActive)
            {
                return true;
            }

            if (HasRequestLayerCommand(followerData))
            {
                return false;
            }

            if (!followerData.IsReadyForPatrolAfterCombat())
            {
                return false;
            }

            return true;
        }

        private static bool HasRequestLayerCommand(BotFollowerPlayer followerData)
        {
            if (followerData == null ||
                !followerData.TryPeekActiveCommand(out FollowerCommandType command, out _, out _))
            {
                return false;
            }

            return command != FollowerCommandType.PushEnemy &&
                   command != FollowerCommandType.SuppressEnemy &&
                   command != FollowerCommandType.NeedSniper;
        }

        private bool HasVisibleKnownEnemy()
        {
            try
            {
                var infos = BotOwner?.EnemiesController?.EnemyInfos;
                if (infos == null || infos.Count == 0) return false;

                foreach (var kv in infos)
                {
                    var info = kv.Value;
                    if (info == null) continue;
                    if (info.IsVisible) return true;
                }
            }
            catch
            {
                // Ignore transient enemy-info enumeration issues and keep vanilla behavior.
            }
            return false;
        }

        public override void Stop()
        {
            isHealing = false;
            stoppedForHealDecision = false;
            patrolHealStartAnnounced = false;
            healUseObserved = false;
            nextHealStartCheckAt = 0f;
            selectedAction = null;
            ResetPatrolHealCoverState();
            ResetReloadState();
            ResetStowedTacticalDeviceState();
            base.Stop();
        }

        public override void Start()
        {
            base.Start();
            isHealing = false;
            stoppedForHealDecision = false;
            patrolHealStartAnnounced = false;
            healUseObserved = false;
            nextHealStartCheckAt = 0f;
            ResetPatrolHealCoverState();
            ResetReloadState();
            ResetStowedTacticalDeviceState();
            BossPlayers.Instance?.GetFollower(BotOwner)?.ClearCombatIndependent();
            if (BossPlayers.Instance?.GetFollower(BotOwner)?.IsBackpackInspectionActive != true)
            {
                BotOwner.Mover.Pause = false;
            }
            ResetTiltForPatrol();
            if (BossPlayers.Instance?.GetFollower(BotOwner)?.IsBackpackInspectionActive != true &&
                BotOwner.Mover.TargetPose < 0.85f)
            {
                BotOwner.SetPose(1f);
            }

            BotOwner.PatrollingData?.Pause();

            if (BotOwner.BotRequestController?.CurRequest != null)
            {
                BotOwner.BotRequestController.CurRequest.Complete();
                BotOwner.BotRequestController.CurRequest = null;
            }

            Utils.FollowerRecovery.SoftReset(BotOwner);

            BotLogicDecision logicDecision = BotOwner.Brain.Agent.LastResult().Action;
            if (BotOwner.Brain.Agent._nodesDictionary.TryGetValue(logicDecision, out var logicInstance))
            {
                logicInstance.Dispose();
            }
        }

        private void ResetTiltForPatrol()
        {
            try
            {
                BotOwner.Tilt?.Stop();
                BotOwner.GetPlayer?.MovementContext?.SetTilt(0f, true);
            }
            catch (Exception ex)
            {
                LogLayerException("ResetTiltForPatrol", ex);
            }
        }

        public override Action GetNextAction()
        {
            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            if (BotOwner.Mover.Pause && followerData?.IsBackpackInspectionActive != true)
            {
                BotOwner.Mover.Pause = false;
            }

            try
            {
                if (TryCompletePostCombatFullHealRestore())
                {
                    selectedAction = new Action(typeof(FollowAction), "FollowerPatrol");
                    return selectedAction;
                }

                RefreshHealWorkIfNeeded();

                GetPatrolHealState(
                    out bool isUsingHeal,
                    out bool hasPendingHealWork,
                    out bool hasRecoverableTopOffWork);

                if (ShouldOwnPatrolHealAction(isUsingHeal, hasPendingHealWork, hasRecoverableTopOffWork))
                {
                    if (!isUsingHeal && TryCreatePatrolHealCoverAction(out Action? coverAction))
                    {
                        selectedAction = coverAction;
                        return selectedAction;
                    }

                    if (!CanStartPatrolHealAction(isUsingHeal, hasPendingHealWork, hasRecoverableTopOffWork))
                    {
                        BeginPatrolHealSequence(announce: false);
                        nextHealStartCheckAt = Time.time + HealStartCheckInterval;
                        selectedAction = new Action(typeof(PatrolHealWaitAction), PatrolHealWaitActionReason);
                        return selectedAction;
                    }

                    if (!isHealing)
                    {
                        BeginPatrolHealSequence(announce: true);
                    }

                    isHealing = true;
                    if (healSoftTimeoutAt <= 0f)
                    {
                        healSoftTimeoutAt = Time.time + 20f;
                    }
                    selectedAction = new Action(typeof(HealAction), "Heal");
                    return selectedAction;
                }

                isHealing = false;
                stoppedForHealDecision = false;
                healUseObserved = false;
                ResetPatrolHealStartAnnouncementIfSequenceComplete();

                if (TryReturnSelectedLauncherToPrimaryAfterCombat())
                {
                    selectedAction = new Action(typeof(FollowAction), "FollowerPatrol");
                    return selectedAction;
                }

                // put the weapon reload here
                TryTurnOffStowedTacticalDevicesOutOfCombat();
                TryHandleOutOfCombatReload();

                selectedAction = new Action(typeof(FollowAction), "FollowerPatrol");

                return selectedAction;
            }
            catch (Exception ex)
            {
                LogLayerException("GetNextAction", ex);
                selectedAction = new Action(typeof(FollowAction), "FollowerPatrol");
                return selectedAction;
            }
        }

        public override bool IsCurrentActionEnding()
        {
            try
            {
                bool isHealAction = selectedAction?.Type == typeof(HealAction);
                bool isHealWaitAction = selectedAction?.Type == typeof(PatrolHealWaitAction);
                bool isHealCoverAction = selectedAction?.Type == typeof(CombatRunToCoverAction);
                bool isHealDecision = BotOwner.Brain.Agent?.LastResult().Action == BotLogicDecision.heal;

                if (isHealWaitAction)
                {
                    if (!IsActive())
                    {
                        return true;
                    }

                    if (TryCompletePostCombatFullHealRestore())
                    {
                        return true;
                    }

                    RefreshHealWorkIfNeeded();
                    GetPatrolHealState(
                        out bool isUsingHeal,
                        out bool hasPendingHealWork,
                        out bool hasRecoverableTopOffWork);
                    return IsPatrolHealWaitActionEnding(
                        isUsingHeal,
                        hasPendingHealWork,
                        hasRecoverableTopOffWork);
                }

                if (!isHealAction && !isHealDecision)
                {
                    if (!IsActive())
                    {
                        return true;
                    }

                    if (TryCompletePostCombatFullHealRestore())
                    {
                        return true;
                    }

                    RefreshHealWorkIfNeeded();
                    GetPatrolHealState(
                        out bool isUsingHeal,
                        out bool hasPendingHealWork,
                        out bool hasRecoverableTopOffWork);
                    if (isHealCoverAction)
                    {
                        return IsPatrolHealCoverActionEnding(isUsingHeal || hasPendingHealWork || hasRecoverableTopOffWork);
                    }

                    if (ShouldOwnPatrolHealAction(isUsingHeal, hasPendingHealWork, hasRecoverableTopOffWork))
                    {
                        return true;
                    }

                    if (!TryReturnSelectedLauncherToPrimaryAfterCombat())
                    {
                        TryTurnOffStowedTacticalDevicesOutOfCombat();
                        TryHandleOutOfCombatReload();
                    }

                    return false;
                }

                return EndHealing();
            }
            catch (Exception ex)
            {
                LogLayerException("IsCurrentActionEnding", ex);
                return true;
            }
        }

        private void RefreshHealWorkIfNeeded()
        {
            if (BotOwner?.Medecine == null ||
                BotOwner.GetPlayer?.ActiveHealthController == null ||
                !BotOwner.HealthController.IsAlive)
            {
                return;
            }

            if (BotOwner.Medecine.Using ||
                BotOwner.Medecine.FirstAid.Have2Do ||
                BotOwner.Medecine.SurgicalKit.HaveWork ||
                Time.time < nextHealWorkRefreshAt)
            {
                return;
            }

            bool hasHealRelevantDamage = BotOwner.GetPlayer.HealthStatus != ETagStatus.Healthy;
            foreach (EBodyPart part in EFT.HealthSystem.HealthHelper.RealBodyParts)
            {
                if (!BotOwner.GetPlayer.ActiveHealthController.IsBodyPartDestroyed(part))
                {
                    continue;
                }

                hasHealRelevantDamage = true;
                break;
            }

            if (!hasHealRelevantDamage)
            {
                return;
            }

            nextHealWorkRefreshAt = Time.time + 1f;

            try
            {
                Utils.FollowerMedical.RefreshMedicalWork(BotOwner);
            }
            catch (Exception ex)
            {
                LogLayerException("RefreshHealWorkIfNeeded", ex);
            }
        }

        private bool HasPendingHealWork()
        {
            return BotOwner?.Medecine != null &&
                   (BotOwner.Medecine.FirstAid?.Using == true ||
                    BotOwner.Medecine.SurgicalKit?.Using == true ||
                    BotOwner.Medecine.FirstAid?.Have2Do == true ||
                    BotOwner.Medecine.SurgicalKit?.HaveWork == true);
        }

        private void LogLayerException(string where, Exception ex)
        {
            if (Time.time < _nextErrorLogAt) return;
            _nextErrorLogAt = Time.time + 1f;
            Modules.Logger.LogError($"FollowerPatrolLayer.{where} failed for bot={BotOwner?.Profile?.Nickname ?? BotOwner?.name ?? "<null>"}");
            Modules.Logger.LogError(ex);
        }

        private bool EndHealing()
        {
            bool isHealAction = selectedAction?.Type == typeof(HealAction);

            if (TryCompletePostCombatFullHealRestore())
            {
                CompleteHealing();
                return true;
            }

            GetPatrolHealState(
                out bool isUsingHeal,
                out bool hasPendingHealWork,
                out bool hasRecoverableTopOffWork);
            if (isUsingHeal)
            {
                healNodeEnteredAt = Time.time;
                if (!healUseObserved)
                {
                    healUseObserved = true;
                }

                // FollowerMedical owns stuck controller recovery. Do not cancel a legitimate
                // multi-part heal here merely because its total animation exceeds 15 seconds.
                return false;
            }

            bool completedMedicalUse = healUseObserved;
            healUseObserved = false;

            // Old EndHeal equivalent: no real medical work -> end heal action. The post-combat
            // restore timer can keep running after movement resumes.
            if (!hasPendingHealWork && !hasRecoverableTopOffWork)
            {
                CompleteHealing();
                return true;
            }

            if (completedMedicalUse)
            {
                // Let the completed vanilla node end. The patrol layer will select a stationary
                // medical-wait action until the next treatment clears EFT's heal cooldown.
                CompleteHealing();
                return true;
            }

            if (healNodeEnteredAt > 0f && healNodeEnteredAt + HealNodeStartTimeout < Time.time)
            {
                RefreshHealWorkForRetry();
                GetPatrolHealState(
                    out isUsingHeal,
                    out hasPendingHealWork,
                    out hasRecoverableTopOffWork);
                if (isUsingHeal)
                {
                    return false;
                }

                if (!hasPendingHealWork && !hasRecoverableTopOffWork)
                {
                    CompleteHealing();
                    return true;
                }

                if (CanStartPatrolHealAction(isUsingHeal, hasPendingHealWork, hasRecoverableTopOffWork))
                {
                    healNodeEnteredAt = Time.time;
                    return false;
                }

                CompleteHealing();
                return true;
            }

            if (!IsActive() && isHealAction)
            {
                CompleteHealing();
                return true;
            }
            return false;
        }

        private void GetPatrolHealState(
            out bool isUsingHeal,
            out bool hasPendingHealWork,
            out bool hasRecoverableTopOffWork)
        {
            bool isUsingFirstAid = BotOwner?.Medecine?.FirstAid?.Using == true;
            bool isUsingSurgery = BotOwner?.Medecine?.SurgicalKit?.Using == true;
            bool hasFirstAidWork = BotOwner?.Medecine?.FirstAid?.Have2Do == true;
            bool hasSurgeryWork = BotOwner?.Medecine?.SurgicalKit?.HaveWork == true;
            bool hasDeferredFirstAidWork =
                BotOwner?.Medecine?.FirstAid?.Damaged == true &&
                BotOwner.Medecine.FirstAid.HaveSmth2Use;

            isUsingHeal = isUsingFirstAid || isUsingSurgery;
            hasPendingHealWork = hasSurgeryWork || hasFirstAidWork || hasDeferredFirstAidWork;
            hasRecoverableTopOffWork = Utils.FollowerMedical.HasRecoverableFirstAidDamage(BotOwner);
            bool hasKnownWork = isUsingHeal || hasPendingHealWork || hasRecoverableTopOffWork;
            bool shouldKeepPostCombatFullHeal = Utils.FollowerMedical.ShouldKeepPostCombatFullHeal(
                BotOwner,
                hasKnownWork,
                scanForWork: false);

            if (!isUsingHeal &&
                !hasPendingHealWork &&
                !hasRecoverableTopOffWork &&
                !shouldKeepPostCombatFullHeal)
            {
                Utils.FollowerMedical.CompletePostCombatFullHeal(BotOwner);
            }
        }

        private void CompleteHealing()
        {
            isHealing = false;
            stoppedForHealDecision = false;
            healUseObserved = false;
            ResetPatrolHealStartAnnouncementIfSequenceComplete();
            healSoftTimeoutAt = 0f;
            healNodeEnteredAt = 0f;
            nextHealStartCheckAt = 0f;
            ResetPatrolHealCoverStateIfSequenceComplete();
            // Normal patrol healing should finish/cancel medical state without restoring all raid HP.
            Utils.FollowerMedical.CompleteHealing(BotOwner);
        }

        private void AbortHealing()
        {
            isHealing = false;
            stoppedForHealDecision = false;
            healUseObserved = false;
            ResetPatrolHealStartAnnouncementIfSequenceComplete();
            healSoftTimeoutAt = 0f;
            healNodeEnteredAt = 0f;
            nextHealStartCheckAt = 0f;
            ResetPatrolHealCoverStateIfSequenceComplete();
            Utils.FollowerMedical.AbortHealing(BotOwner, recoverDestroyedSurgeryParts: true);
        }

        private bool TryCompletePostCombatFullHealRestore()
        {
            if (!Utils.FollowerMedical.TryCompletePostCombatFullHealRestore(BotOwner))
            {
                return false;
            }

            isHealing = false;
            stoppedForHealDecision = false;
            patrolHealStartAnnounced = false;
            healUseObserved = false;
            healSoftTimeoutAt = 0f;
            healNodeEnteredAt = 0f;
            nextHealStartCheckAt = 0f;
            ResetPatrolHealCoverState();
            return true;
        }

        private bool TryCreatePatrolHealCoverAction(out Action? coverAction)
        {
            coverAction = null;

            if (patrolHealCoverFallback || BotOwner == null)
            {
                return false;
            }

            if (!patrolHealCoverSearchDone)
            {
                patrolHealCoverSearchDone = true;
                patrolHealCover = FindPatrolHealCover();
                if (patrolHealCover == null)
                {
                    patrolHealCoverFallback = true;
                    return false;
                }
            }

            if (patrolHealCover == null || !IsPatrolHealCoverValid(patrolHealCover))
            {
                patrolHealCover = null;
                patrolHealCoverFallback = true;
                return false;
            }

            if (IsAtPatrolHealCover())
            {
                return false;
            }

            BotOwner.Memory.SetCoverPoints(patrolHealCover, "PatrolHealCover");
            AnnouncePatrolHealStartOnce();
            coverAction = new Action(
                typeof(CombatRunToCoverAction),
                PatrolHealCoverActionReason,
                new FollowerCombatActionData(BotLogicDecision.runToCover, PatrolHealCoverActionReason, null));
            return true;
        }

        private bool IsPatrolHealCoverActionEnding(bool hasHealWork)
        {
            if (!hasHealWork)
            {
                ResetPatrolHealCoverStateIfSequenceComplete();
                return true;
            }

            if (patrolHealCover == null || !IsPatrolHealCoverValid(patrolHealCover))
            {
                patrolHealCoverFallback = true;
                return true;
            }

            BotOwner.Memory.SetCoverPoints(patrolHealCover, "PatrolHealCover");
            return IsAtPatrolHealCover();
        }

        private CustomNavigationPoint? FindPatrolHealCover()
        {
            try
            {
                return Utils.Covers.GetClosestCoverPoint(
                    BotOwner,
                    BotOwner.Position,
                    PatrolHealCoverSearchRadius,
                    IsPatrolHealCoverValid,
                    CoverSearchType.distToToCenter);
            }
            catch (Exception ex)
            {
                LogLayerException("FindPatrolHealCover", ex);
                return null;
            }
        }

        private bool IsPatrolHealCoverValid(CustomNavigationPoint? cover)
        {
            if (cover == null || BotOwner == null)
            {
                return false;
            }

            if (!cover.IsFreeById(BotOwner.Id) || cover.IsSpotted)
            {
                return false;
            }

            return Utils.Covers.IsNavigablePoint(BotOwner.Position, cover.Position, PatrolHealCoverSearchRadius);
        }

        private bool IsAtPatrolHealCover()
        {
            if (patrolHealCover == null || BotOwner == null)
            {
                return false;
            }

            if ((BotOwner.Position - patrolHealCover.Position).sqrMagnitude <= PatrolHealCoverArriveDistance * PatrolHealCoverArriveDistance)
            {
                return true;
            }

            try
            {
                bool goToPointArrived =
                    BotOwner.GoToSomePointData?.HaveTarget() == true &&
                    (BotOwner.GoToSomePointData.Point - patrolHealCover.Position).sqrMagnitude <= 1f &&
                    BotOwner.GoToSomePointData.IsCome();

                return BotOwner.Mover?.IsComeTo(PatrolHealCoverArriveDistance, true, patrolHealCover) == true ||
                       goToPointArrived;
            }
            catch
            {
                return false;
            }
        }

        private void ResetPatrolHealCoverState()
        {
            patrolHealCover = null;
            patrolHealCoverSearchDone = false;
            patrolHealCoverFallback = false;
        }

        private void ResetPatrolHealCoverStateIfSequenceComplete()
        {
            if (Utils.FollowerMedical.IsPostCombatFullHealActive(BotOwner))
            {
                return;
            }

            ResetPatrolHealCoverState();
        }

        private void ResetPatrolHealStartAnnouncementIfSequenceComplete()
        {
            if (Utils.FollowerMedical.IsPostCombatFullHealActive(BotOwner))
            {
                return;
            }

            patrolHealStartAnnounced = false;
        }

        private void AnnouncePatrolHealStartOnce()
        {
            if (patrolHealStartAnnounced)
            {
                return;
            }

            patrolHealStartAnnounced = true;
            try
            {
                BotOwner?.BotTalk?.TrySay(EPhraseTrigger.StartHeal, false);
            }
            catch (Exception ex)
            {
                LogLayerException("AnnouncePatrolHealStartOnce", ex);
            }
        }

        private static bool ShouldOwnPatrolHealAction(
            bool isUsingHeal,
            bool hasPendingHealWork,
            bool hasRecoverableTopOffWork)
        {
            return isUsingHeal || hasPendingHealWork || hasRecoverableTopOffWork;
        }

        private bool CanStartPatrolHealAction(
            bool isUsingHeal,
            bool hasPendingHealWork,
            bool hasRecoverableTopOffWork)
        {
            if (isUsingHeal)
            {
                return true;
            }

            return (hasPendingHealWork && CanStartVanillaHealNode()) ||
                   (hasRecoverableTopOffWork && Utils.FollowerMedical.CanStartFirstAidTopOff(BotOwner));
        }

        private bool CanStartVanillaHealNode()
        {
            try
            {
                if (BotOwner?.Medecine == null ||
                    BotOwner.WeaponManager?.Grenades?.ThrowindNow == true ||
                    BotOwner.Medecine.Using)
                {
                    return false;
                }

                return BotOwner.Medecine.FirstAid?.ShallStartUse() == true ||
                       BotOwner.Medecine.SurgicalKit?.ShallStartUse() == true;
            }
            catch (Exception ex)
            {
                LogLayerException("CanStartVanillaHealNode", ex);
                return false;
            }
        }

        private bool IsPatrolHealWaitActionEnding(
            bool isUsingHeal,
            bool hasPendingHealWork,
            bool hasRecoverableTopOffWork)
        {
            if (healSoftTimeoutAt > 0f && Time.time >= healSoftTimeoutAt)
            {
                if (Utils.FollowerMedical.IsPostCombatFullHealActive(BotOwner))
                {
                    Utils.FollowerMedical.ForceHeal(BotOwner);
                    Utils.FollowerMedical.CompletePostCombatFullHeal(BotOwner);
                    CompleteHealing();
                }
                else
                {
                    AbortHealing();
                }

                return true;
            }

            if (!ShouldOwnPatrolHealAction(isUsingHeal, hasPendingHealWork, hasRecoverableTopOffWork))
            {
                CompleteHealing();
                return true;
            }

            StopMovementForHealDecision();
            if (Time.time < nextHealStartCheckAt)
            {
                return false;
            }

            nextHealStartCheckAt = Time.time + HealStartCheckInterval;
            RefreshHealWorkIfNeeded();
            GetPatrolHealState(
                out isUsingHeal,
                out hasPendingHealWork,
                out hasRecoverableTopOffWork);

            if (!ShouldOwnPatrolHealAction(isUsingHeal, hasPendingHealWork, hasRecoverableTopOffWork))
            {
                CompleteHealing();
                return true;
            }

            return CanStartPatrolHealAction(isUsingHeal, hasPendingHealWork, hasRecoverableTopOffWork);
        }

        private void BeginPatrolHealSequence(bool announce)
        {
            if (!isHealing)
            {
                healUseObserved = false;
                healNodeEnteredAt = Time.time;
            }

            isHealing = true;
            if (healSoftTimeoutAt <= 0f)
            {
                healSoftTimeoutAt = Time.time + 20f;
            }

            if (announce)
            {
                AnnouncePatrolHealStartOnce();
            }

            StopMovementForHealDecision();
        }

        private void RefreshHealWorkForRetry()
        {
            try
            {
                Utils.FollowerMedical.RefreshMedicalWork(BotOwner);
                BotOwner.Medecine?.FirstAid?.Refresh();
                BotOwner.Medecine?.SurgicalKit?.Refresh();
                Utils.FollowerMedical.RefreshMedicalWork(BotOwner);
            }
            catch (Exception ex)
            {
                LogLayerException("RefreshHealWorkForRetry", ex);
            }
        }

        private void StopMovementForHealDecision()
        {
            if (stoppedForHealDecision || BotOwner == null) return;

            BotOwner.Mover?.Stop();
            if (BotOwner.Mover?.Sprinting == true)
            {
                BotOwner.Mover.Sprint(false, false);
            }
            BotOwner.StopMove();
            stoppedForHealDecision = true;
        }

        private void ResetReloadState()
        {
            // A patrol entry is one out-of-combat reload window. Exiting/re-entering patrol
            // is the only thing that allows the same carried weapon to be considered again.
            triedFillMagazines = false;
            reloadingInProgress = false;
            reloadSlotsTried.Clear();
            reloadWeaponsProcessed.Clear();
            forcedTopOffSlot = null;
            returnAfterTopOffSlot = null;
            reloadingWeaponId = null;
            nextPatrolLauncherFallbackRecordAt = 0f;
            nextReloadCheckAt = Time.time + OutOfCombatReloadInitialCooldown;
            nextMagazineFillCheckAt = Time.time + OutOfCombatReloadInitialCooldown;
        }

        private void ResetStowedTacticalDeviceState()
        {
            stowedTacticalDeviceWeaponsProcessed.Clear();
            nextStowedTacticalDeviceCheckAt = Time.time + OutOfCombatTacticalDeviceInitialCooldown;
        }

        private void TryTurnOffStowedTacticalDevicesOutOfCombat()
        {
            try
            {
                TryTurnOffStowedTacticalDevicesOutOfCombatInternal();
            }
            catch (Exception ex)
            {
                LogLayerException("TryTurnOffStowedTacticalDevicesOutOfCombat", ex);
                nextStowedTacticalDeviceCheckAt = Time.time + OutOfCombatTacticalDeviceCheckInterval;
            }
        }

        private void TryTurnOffStowedTacticalDevicesOutOfCombatInternal()
        {
            if (BotOwner?.WeaponManager == null || Time.time < nextStowedTacticalDeviceCheckAt)
            {
                return;
            }

            nextStowedTacticalDeviceCheckAt = Time.time + OutOfCombatTacticalDeviceCheckInterval;

            BotWeaponSelector? selector = BotOwner.WeaponManager.Selector;
            EquipmentSlot currentSlot = selector?.LastEquipmentSlot ?? EquipmentSlot.FirstPrimaryWeapon;
            Weapon? activeWeapon = BotOwner.WeaponManager.ShootController?.Item ?? BotOwner.WeaponManager.CurrentWeapon;

            foreach (EquipmentSlot slot in StowedTacticalDeviceSlotOrder)
            {
                Weapon? weapon = GetWeaponInSlot(slot);
                if (weapon == null || IsSelectedOrActiveWeapon(slot, weapon, currentSlot, activeWeapon))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(weapon.Id) && stowedTacticalDeviceWeaponsProcessed.Contains(weapon.Id))
                {
                    continue;
                }

                if (TryTurnOffTacticalDevices(weapon, out HashSet<string> changedDeviceIds))
                {
                    RefreshTacticalDeviceVisuals(changedDeviceIds);
                    Modules.Logger.LogInfo(
                        $"[PatrolLight] Turned off stowed tactical device(s) for {BotOwner?.Profile?.Nickname ?? BotOwner?.name ?? "<unknown>"} " +
                        $"slot={slot} weaponId={weapon.Id} template={weapon.TemplateId} devices={changedDeviceIds.Count}");
                }

                if (!string.IsNullOrEmpty(weapon.Id))
                {
                    stowedTacticalDeviceWeaponsProcessed.Add(weapon.Id);
                }
            }
        }

        private static bool IsSelectedOrActiveWeapon(EquipmentSlot slot, Weapon weapon, EquipmentSlot currentSlot, Weapon? activeWeapon)
        {
            if (slot == currentSlot)
            {
                return true;
            }

            if (activeWeapon == null)
            {
                return false;
            }

            if (ReferenceEquals(weapon, activeWeapon))
            {
                return true;
            }

            return !string.IsNullOrEmpty(weapon.Id) &&
                   !string.IsNullOrEmpty(activeWeapon.Id) &&
                   string.Equals(weapon.Id, activeWeapon.Id, StringComparison.Ordinal);
        }

        private static bool TryTurnOffTacticalDevices(Weapon weapon, out HashSet<string> changedDeviceIds)
        {
            changedDeviceIds = new HashSet<string>(StringComparer.Ordinal);
            if (weapon?.Mods == null)
            {
                return false;
            }

            foreach (LightComponent light in weapon.Mods.GetComponents<LightComponent>())
            {
                if (light?.Item == null || !light.IsActive)
                {
                    continue;
                }

                light.IsActive = false;
                if (!string.IsNullOrEmpty(light.Item.Id))
                {
                    changedDeviceIds.Add(light.Item.Id);
                }
            }

            return changedDeviceIds.Count > 0;
        }

        private void RefreshTacticalDeviceVisuals(HashSet<string> changedDeviceIds)
        {
            if (changedDeviceIds == null || changedDeviceIds.Count == 0 || BotOwner?.GetPlayer == null)
            {
                return;
            }

            TacticalComboVisualController[] controllers = BotOwner.GetPlayer.GetComponentsInChildren<TacticalComboVisualController>(true);
            foreach (TacticalComboVisualController controller in controllers)
            {
                LightComponent light = controller?.LightMod;
                string? lightId = light?.Item?.Id;
                if (!string.IsNullOrEmpty(lightId) && changedDeviceIds.Contains(lightId))
                {
                    controller.UpdateBeams(false);
                }
            }
        }

        private bool TryReturnSelectedLauncherToPrimaryAfterCombat()
        {
            BotWeaponManager? weaponManager = BotOwner?.WeaponManager;
            BotWeaponSelector? selector = weaponManager?.Selector;
            if (selector == null)
            {
                return false;
            }

            // Only a support-slot launcher is temporary. A launcher in FirstPrimaryWeapon is the
            // follower's real primary and must remain selected after combat.
            if (!FollowerCombatCommon.IsSupportGrenadeLauncherSelectedOrActive(BotOwner))
            {
                return false;
            }

            bool switchRequested = FollowerCombatCommon.TrySwitchSelectedGrenadeLauncherToPrimaryForOpportunity(
                BotOwner,
                goalEnemy: null,
                reason: "patrolReturn",
                tacticalIntent: false,
                out string waitReason);

            forcedTopOffSlot = null;
            returnAfterTopOffSlot = null;
            nextReloadCheckAt = Time.time + 0.5f;
            nextMagazineFillCheckAt = Time.time + OutOfCombatReloadActionCooldown;
            RecordPatrolLauncherFallback(switchRequested, waitReason);
            return true;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void RecordPatrolLauncherFallback(bool switchRequested, string waitReason)
        {
            if (!BattleRecorder.IsRecordingFor(BotOwner))
            {
                return;
            }

            if (Time.time < nextPatrolLauncherFallbackRecordAt)
            {
                return;
            }

            nextPatrolLauncherFallbackRecordAt = Time.time + 2f;
            BattleRecorder.RecordGrenadeEvent(
                BotOwner,
                "launcherFallbackSwitch",
                switchRequested
                    ? "patrolReturn:switched=True"
                    : $"patrolReturn:wait={waitReason}",
                goalEnemy: null);
        }

        private void TryHandleOutOfCombatReload()
        {
            try
            {
                TryHandleOutOfCombatReloadInternal();
            }
            catch (InvalidOperationException ex) when (IsInventoryEnumerationMutation(ex))
            {
                // EFT inventory scans can race with item moves/auto-fill transactions; skip this patrol cycle.
                ResetReloadState();
                nextReloadCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
            }
            catch (Exception ex)
            {
                LogLayerException("TryHandleOutOfCombatReload", ex);
                ResetReloadState();
                nextReloadCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
            }
        }

        private void TryHandleOutOfCombatReloadInternal()
        {
            if (BotOwner?.WeaponManager == null) return;
            Utils.FollowerRecovery.CheckReloadTimeout(BotOwner);
            if (Time.time < nextReloadCheckAt) return;

            var selector = BotOwner.WeaponManager.Selector;
            if (reloadingInProgress)
            {
                // EFT reload completion is not just BotReload.Reloading. Wait for hands/weapon readiness
                // before returning to the previous slot or advancing to the next maintenance slot.
                if (BotOwner.WeaponManager.Reload.Reloading || !BotOwner.WeaponManager.IsWeaponReady)
                {
                    return;
                }

                reloadingInProgress = false;
                MarkReloadWeaponProcessed(reloadingWeaponId);
                ClearOutOfCombatReloadAttemptBudget(reloadingWeaponId);
                reloadingWeaponId = null;
                TryReturnAfterTopOffSwitch(selector);
                nextReloadCheckAt = Time.time + OutOfCombatReloadSlotCooldown;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
                return;
            }

            if (!BotOwner.WeaponManager.IsWeaponReady || BotOwner.WeaponManager.Reload.Reloading) return;
            if (AreAllReloadWeaponsProcessed())
            {
                // Every carried weapon has reached a terminal reload decision for this patrol window.
                nextReloadCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
                return;
            }

            // First consume compatible loose ammo into inserted/spare magazines for all carried weapons.
            // Reload-switch decisions below then compare already-prepared magazines.
            if (!triedFillMagazines)
            {
                FollowerOutOfCombatReloadPolicy.TryFillCarriedWeaponMagazines(BotOwner);
                if (!InteractableObjects.IsLootedWeapon(BotOwner, BotOwner.WeaponManager.CurrentWeapon))
                {
                    BotOwner.WeaponManager.Reload.TryFillMagazines();
                }

                triedFillMagazines = true;
                nextReloadCheckAt = Time.time + OutOfCombatReloadCheckInterval;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
                return;
            }

            if (ShouldReloadCurrentWeaponOutOfCombat())
            {
                // A real reload/swap is starting for the currently selected weapon.
                // The weapon is only marked done after the reload animation has fully settled.
                EquipmentSlot currentSlot = selector.LastEquipmentSlot;
                string? currentWeaponId = GetReloadWeaponId(currentSlot);
                Weapon? currentWeapon = GetWeaponInSlot(currentSlot) ?? BotOwner.WeaponManager.CurrentWeapon;
                reloadSlotsTried.Add(currentSlot);
                reloadingInProgress = TryForceReloadCurrentWeaponOutOfCombat();
                reloadingWeaponId = reloadingInProgress ? currentWeaponId : null;
                if (!reloadingInProgress)
                {
                    RecordOutOfCombatReloadFailure(currentSlot, currentWeapon, "reloadRejected");
                }

                if (forcedTopOffSlot == currentSlot)
                {
                    if (!reloadingInProgress)
                    {
                        MarkReloadSlotFailed(currentSlot);
                        MarkReloadWeaponProcessed(currentWeaponId);
                        TryReturnAfterTopOffSwitch(selector);
                    }

                    forcedTopOffSlot = null;
                }
                else if (!reloadingInProgress)
                {
                    MarkReloadWeaponProcessed(currentWeaponId);
                }

                nextReloadCheckAt = Time.time + OutOfCombatReloadSlotCooldown;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadActionCooldown;
                return;
            }

            if (TrySelectNextWeaponToTopOff(selector, out bool processedSlot))
            {
                // Switched to a weapon that still needs normal reload work; let EFT finish the switch,
                // then the next patrol tick will start reload from that weapon's slot.
                nextReloadCheckAt = Time.time + OutOfCombatReloadSlotCooldown;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadActionCooldown;
                return;
            }

            if (processedSlot)
            {
                // This slot had no useful reload work after ammo/mag preparation. Wait before the
                // next slot so patrol does not churn through weapon checks in one frame.
                nextReloadCheckAt = Time.time + OutOfCombatReloadSlotCooldown;
                nextMagazineFillCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
                return;
            }

            reloadSlotsTried.Clear();
            returnAfterTopOffSlot = null;
            nextReloadCheckAt = Time.time + OutOfCombatReloadFullCycleCooldown;
        }

        private bool ShouldReloadCurrentWeaponOutOfCombat()
        {
            if (BotOwner?.WeaponManager?.Reload == null)
            {
                return false;
            }

            Weapon currentWeapon = BotOwner.WeaponManager.CurrentWeapon;
            if (currentWeapon == null)
            {
                return false;
            }

            if (IsReloadWeaponProcessed(currentWeapon))
            {
                return false;
            }

            EquipmentSlot currentSlot = BotOwner.WeaponManager.Selector?.LastEquipmentSlot ?? EquipmentSlot.FirstPrimaryWeapon;
            bool reloadLootedPrimaryLauncher = CanReloadLootedFirstPrimaryLauncher(currentSlot, currentWeapon);
            bool reloadLootedPrimaryPackage = CanReloadLootedFirstPrimaryPackage(currentSlot, currentWeapon);
            if (InteractableObjects.IsLootedWeapon(BotOwner, currentWeapon) &&
                !reloadLootedPrimaryLauncher &&
                !reloadLootedPrimaryPackage)
            {
                MarkReloadWeaponProcessed(currentWeapon.Id);
                return false;
            }

            if (IsOutOfCombatReloadGiveUpActive(currentSlot, currentWeapon))
            {
                return false;
            }

            if (reloadLootedPrimaryLauncher)
            {
                return true;
            }

            bool forceTopOffCurrentSlot =
                forcedTopOffSlot.HasValue &&
                currentSlot == forcedTopOffSlot.Value;

            // Chamber/OnlyBarrel support weapons do not always report magazine-style reload counts.
            if (currentWeapon.ReloadMode != Weapon.EReloadMode.ExternalMagazine)
            {
                // Chamber, revolver, shotgun, and internal-mag weapons rely on EFT's normal
                // reload path once compatible loose ammo exists.
                return FollowerOutOfCombatReloadPolicy.CanTopOffWeapon(BotOwner, currentWeapon);
            }

            EFT.InventoryLogic.Magazine? currentMagazine = currentWeapon.GetCurrentMagazine();
            int maxBulletCount = currentMagazine?.MaxCount ?? currentWeapon.GetMaxMagazineCount();
            if (maxBulletCount <= 0)
            {
                return false;
            }

            float reloadThreshold = BotOwner.Settings?.FileSettings?.Boss?.PERCENT_BULLET_TO_RELOAD ?? 0.6f;
            float currentRatio = (float)currentWeapon.GetCurrentMagazineCount() / maxBulletCount;
            if (!forceTopOffCurrentSlot && currentRatio >= reloadThreshold)
            {
                return false;
            }

            // For external-magazine weapons, only reload-swap if there is actually a better magazine available.
            // Loose-ammo top-off is handled by TryFillMagazines before this branch runs.
            return FollowerOutOfCombatReloadPolicy.HasBetterMagazine(BotOwner, currentWeapon);
        }

        private bool TryForceReloadCurrentWeaponOutOfCombat()
        {
            if (BotOwner?.WeaponManager?.Reload == null ||
                BotOwner.WeaponManager.Reload.Reloading ||
                BotOwner.WeaponManager.ShootController == null)
            {
                return false;
            }

            Weapon currentWeapon = BotOwner.WeaponManager.CurrentWeapon;
            if (currentWeapon == null)
            {
                return false;
            }

            EquipmentSlot currentSlot = BotOwner.WeaponManager.Selector?.LastEquipmentSlot ?? EquipmentSlot.FirstPrimaryWeapon;
            if (InteractableObjects.IsLootedWeapon(BotOwner, currentWeapon))
            {
                if (CanReloadLootedFirstPrimaryLauncher(currentSlot, currentWeapon))
                {
                    return FollowerCombatCommon.TryStartActiveGrenadeLauncherLooseAmmoReload(
                        BotOwner,
                        currentWeapon,
                        out _);
                }

                if (!CanReloadLootedFirstPrimaryPackage(currentSlot, currentWeapon))
                {
                    return false;
                }
            }

            if (currentWeapon.ReloadMode != Weapon.EReloadMode.ExternalMagazine)
            {
                return BotOwner.WeaponManager.Reload.TryReload();
            }

            EFT.InventoryLogic.Magazine? bestMagazine = BotOwner.WeaponManager.Reload.GetMagazineForReload(currentWeapon);
            if (bestMagazine == null || bestMagazine.Count <= currentWeapon.GetCurrentMagazineCount())
            {
                return false;
            }

            if (InteractableObjects.IsLootedWeapon(BotOwner, currentWeapon) &&
                !InteractableObjects.IsApprovedLootedWeaponMagazine(BotOwner, currentWeapon, bestMagazine))
            {
                Modules.Logger.LogInfo(
                    $"[PatrolReload] Rejected non-package magazine for looted primary " +
                    $"'{BotOwner?.Profile?.Nickname ?? BotOwner?.name ?? "<unknown>"}' " +
                    $"weapon={currentWeapon.TemplateId} magazine={bestMagazine.TemplateId}");
                return false;
            }

            if (!BotOwner.WeaponManager.ShootController.CanStartReload())
            {
                return false;
            }

            return BotOwner.WeaponManager.Reload.TryReload();
        }

        private bool TrySelectNextWeaponToTopOff(BotWeaponSelector selector, out bool processedSlot)
        {
            processedSlot = false;
            EquipmentSlot currentSlot = selector.LastEquipmentSlot;

            if (TrySelectWeaponToTopOff(selector, currentSlot, out processedSlot) || processedSlot)
            {
                return !processedSlot;
            }

            foreach (EquipmentSlot slot in ReloadSlotOrder)
            {
                if (slot == currentSlot)
                {
                    continue;
                }

                if (TrySelectWeaponToTopOff(selector, slot, out processedSlot) || processedSlot)
                {
                    return !processedSlot;
                }
            }

            return false;
        }

        private bool TrySelectWeaponToTopOff(BotWeaponSelector selector, EquipmentSlot slot, out bool processedSlot)
        {
            processedSlot = false;
            EquipmentSlot previousSlot = selector.LastEquipmentSlot;
            Weapon? weapon = GetWeaponInSlot(slot);
            if (weapon == null || IsReloadWeaponProcessed(weapon))
            {
                return false;
            }

            if (reloadSlotsTried.Contains(slot))
            {
                return false;
            }

            bool reloadLootedPrimaryLauncher = CanReloadLootedFirstPrimaryLauncher(slot, weapon);
            bool reloadLootedPrimaryPackage = CanReloadLootedFirstPrimaryPackage(slot, weapon);
            if (InteractableObjects.IsLootedWeapon(BotOwner, weapon) &&
                !reloadLootedPrimaryLauncher &&
                !reloadLootedPrimaryPackage)
            {
                MarkReloadWeaponProcessed(weapon.Id);
                processedSlot = true;
                return false;
            }

            if (IsOutOfCombatReloadGiveUpActive(slot, weapon))
            {
                MarkReloadWeaponProcessed(weapon.Id);
                processedSlot = true;
                return false;
            }

            if (!ShouldReloadWeaponInSlot(slot, weapon))
            {
                // No ammo, no better magazine, or already cooling down: this weapon is done for
                // the current patrol window and should not be reconsidered until patrol restarts.
                MarkReloadWeaponProcessed(weapon.Id);
                processedSlot = true;
                return false;
            }

            if (previousSlot == slot)
            {
                if (ShouldReloadCurrentWeaponOutOfCombat())
                {
                    return true;
                }

                reloadSlotsTried.Add(slot);
                MarkReloadWeaponProcessed(weapon.Id);
                processedSlot = true;
                return false;
            }

            if (IsOutOfCombatReloadSwitchBudgetSpent(slot, weapon))
            {
                MarkReloadWeaponProcessed(weapon.Id);
                processedSlot = true;
                return false;
            }

            bool switched = slot switch
            {
                EquipmentSlot.FirstPrimaryWeapon => selector.ChangeToMain(),
                EquipmentSlot.SecondPrimaryWeapon => selector.TryChangeToSlot(EquipmentSlot.SecondPrimaryWeapon, false),
                EquipmentSlot.Holster => selector.TryChangeToSlot(EquipmentSlot.Holster, false),
                _ => false,
            };

            if (switched)
            {
                RecordOutOfCombatReloadSwitchAttempt(slot, weapon);
                forcedTopOffSlot = slot;
                if (previousSlot != slot && !reloadLootedPrimaryLauncher)
                {
                    returnAfterTopOffSlot = previousSlot;
                }
            }

            return switched;
        }

        private bool ShouldReloadWeaponInSlot(EquipmentSlot slot)
        {
            Weapon? weapon = GetWeaponInSlot(slot);
            if (weapon == null)
            {
                return false;
            }

            return ShouldReloadWeaponInSlot(slot, weapon);
        }

        private bool ShouldReloadWeaponInSlot(EquipmentSlot slot, Weapon weapon)
        {
            if (IsReloadWeaponProcessed(weapon))
            {
                return false;
            }

            bool reloadLootedPrimaryLauncher = CanReloadLootedFirstPrimaryLauncher(slot, weapon);
            bool reloadLootedPrimaryPackage = CanReloadLootedFirstPrimaryPackage(slot, weapon);
            if (InteractableObjects.IsLootedWeapon(BotOwner, weapon) &&
                !reloadLootedPrimaryLauncher &&
                !reloadLootedPrimaryPackage)
            {
                return false;
            }

            if (IsOutOfCombatReloadGiveUpActive(slot, weapon))
            {
                return false;
            }

            if (IsReloadSlotRetryCoolingDown(slot))
            {
                return false;
            }

            if (reloadLootedPrimaryLauncher)
            {
                return true;
            }

            if (weapon.ReloadMode == Weapon.EReloadMode.ExternalMagazine)
            {
                return FollowerOutOfCombatReloadPolicy.HasBetterMagazine(BotOwner, weapon);
            }

            return FollowerOutOfCombatReloadPolicy.CanTopOffWeapon(BotOwner, weapon);
        }

        private bool CanReloadLootedFirstPrimaryLauncher(EquipmentSlot slot, Weapon weapon)
        {
            return slot == EquipmentSlot.FirstPrimaryWeapon &&
                   InteractableObjects.IsLootedWeapon(BotOwner, weapon) &&
                   FollowerCombatCommon.CanReloadGrenadeLauncherFromLooseAmmo(BotOwner, weapon);
        }

        private bool CanReloadLootedFirstPrimaryPackage(EquipmentSlot slot, Weapon weapon)
        {
            // Looted firearms normally stay outside patrol reload maintenance so they cannot
            // consume compatible magazines from the bot's spawned kit. Gear planning now records
            // magazines successfully acquired for each weapon package; only those magazines may
            // unlock the first-primary reload path.
            return slot == EquipmentSlot.FirstPrimaryWeapon &&
                   weapon?.ReloadMode == Weapon.EReloadMode.ExternalMagazine &&
                   InteractableObjects.IsLootedWeapon(BotOwner, weapon) &&
                   FollowerOutOfCombatReloadPolicy.HasBetterMagazine(BotOwner, weapon);
        }

        private void MarkReloadSlotFailed(EquipmentSlot slot)
        {
            reloadSlotRetryAfter[slot] = Time.time + OutOfCombatReloadFullCycleCooldown;
            reloadSlotsTried.Add(slot);
        }

        private bool IsReloadSlotRetryCoolingDown(EquipmentSlot slot)
        {
            return reloadSlotRetryAfter.TryGetValue(slot, out float retryAt) && Time.time < retryAt;
        }

        private bool IsOutOfCombatReloadGiveUpActive(EquipmentSlot slot, Weapon weapon)
        {
            string key = GetOutOfCombatReloadAttemptKey(slot, weapon);
            if (!reloadGiveUpUntilByWeapon.TryGetValue(key, out float retryAt))
            {
                return false;
            }

            if (Time.time < retryAt)
            {
                return true;
            }

            reloadGiveUpUntilByWeapon.Remove(key);
            reloadSwitchAttemptsByWeapon.Remove(key);
            reloadFailuresByWeapon.Remove(key);
            return false;
        }

        private bool IsOutOfCombatReloadSwitchBudgetSpent(EquipmentSlot slot, Weapon weapon)
        {
            if (IsOutOfCombatReloadGiveUpActive(slot, weapon))
            {
                return true;
            }

            string key = GetOutOfCombatReloadAttemptKey(slot, weapon);
            if (!reloadSwitchAttemptsByWeapon.TryGetValue(key, out int attempts) ||
                attempts < OutOfCombatReloadMaxSwitchAttemptsPerWeapon)
            {
                return false;
            }

            GiveUpOutOfCombatReload(slot, weapon, $"switchAttempts={attempts}");
            return true;
        }

        private void RecordOutOfCombatReloadSwitchAttempt(EquipmentSlot slot, Weapon weapon)
        {
            string key = GetOutOfCombatReloadAttemptKey(slot, weapon);
            int attempts = reloadSwitchAttemptsByWeapon.TryGetValue(key, out int currentAttempts)
                ? currentAttempts + 1
                : 1;
            reloadSwitchAttemptsByWeapon[key] = attempts;
        }

        private void RecordOutOfCombatReloadFailure(EquipmentSlot slot, Weapon? weapon, string reason)
        {
            if (weapon == null || IsOutOfCombatReloadGiveUpActive(slot, weapon))
            {
                return;
            }

            string key = GetOutOfCombatReloadAttemptKey(slot, weapon);
            int failures = reloadFailuresByWeapon.TryGetValue(key, out int currentFailures)
                ? currentFailures + 1
                : 1;
            reloadFailuresByWeapon[key] = failures;

            if (failures >= OutOfCombatReloadMaxFailedReloadsPerWeapon)
            {
                GiveUpOutOfCombatReload(slot, weapon, $"{reason}:failures={failures}");
            }
        }

        private void GiveUpOutOfCombatReload(EquipmentSlot slot, Weapon weapon, string reason)
        {
            string key = GetOutOfCombatReloadAttemptKey(slot, weapon);
            float retryAt = Time.time + OutOfCombatReloadGiveUpCooldown;
            reloadGiveUpUntilByWeapon[key] = retryAt;
            reloadSlotRetryAfter[slot] = retryAt;
            reloadSlotsTried.Add(slot);
            MarkReloadWeaponProcessed(weapon.Id);

            Modules.Logger.LogInfo(
                $"[PatrolReload] Giving up out-of-combat reload top-off for {BotOwner?.Profile?.Nickname ?? BotOwner?.name ?? "<unknown>"} " +
                $"slot={slot} weaponId={weapon.Id} template={weapon.TemplateId} reason={reason}");
        }

        private void ClearOutOfCombatReloadAttemptBudget(string? weaponId)
        {
            if (string.IsNullOrEmpty(weaponId))
            {
                return;
            }

            reloadSwitchAttemptsByWeapon.Remove(weaponId);
            reloadFailuresByWeapon.Remove(weaponId);
            reloadGiveUpUntilByWeapon.Remove(weaponId);
        }

        private static string GetOutOfCombatReloadAttemptKey(EquipmentSlot slot, Weapon? weapon)
        {
            return !string.IsNullOrEmpty(weapon?.Id) ? weapon.Id : slot.ToString();
        }

        private bool AreAllReloadWeaponsProcessed()
        {
            // Missing slots count as processed; real weapons must be marked by id.
            foreach (EquipmentSlot slot in ReloadSlotOrder)
            {
                if (!IsReloadSlotProcessed(slot))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsReloadSlotProcessed(EquipmentSlot slot)
        {
            Weapon? weapon = GetWeaponInSlot(slot);
            return weapon == null || IsReloadWeaponProcessed(weapon);
        }

        private bool IsReloadWeaponProcessed(Weapon weapon)
        {
            return !string.IsNullOrEmpty(weapon.Id) && reloadWeaponsProcessed.Contains(weapon.Id);
        }

        private string? GetReloadWeaponId(EquipmentSlot slot)
        {
            return GetWeaponInSlot(slot)?.Id;
        }

        private void MarkReloadWeaponProcessed(EquipmentSlot slot)
        {
            MarkReloadWeaponProcessed(GetReloadWeaponId(slot));
        }

        private void MarkReloadWeaponProcessed(string? weaponId)
        {
            if (!string.IsNullOrEmpty(weaponId))
            {
                reloadWeaponsProcessed.Add(weaponId);
            }
        }

        private static bool IsInventoryEnumerationMutation(InvalidOperationException ex)
        {
            return ex.Message?.IndexOf("Collection was modified", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TryReturnAfterTopOffSwitch(BotWeaponSelector selector)
        {
            if (!returnAfterTopOffSlot.HasValue ||
                selector == null ||
                selector.LastEquipmentSlot == returnAfterTopOffSlot.Value)
            {
                returnAfterTopOffSlot = null;
                return;
            }

            switch (returnAfterTopOffSlot.Value)
            {
                case EquipmentSlot.FirstPrimaryWeapon:
                    selector.ChangeToMain();
                    break;
                case EquipmentSlot.SecondPrimaryWeapon:
                    selector.TryChangeToSlot(EquipmentSlot.SecondPrimaryWeapon, false);
                    break;
                case EquipmentSlot.Holster:
                    selector.TryChangeToSlot(EquipmentSlot.Holster, false);
                    break;
            }

            returnAfterTopOffSlot = null;
        }

        private Weapon? GetWeaponInSlot(EquipmentSlot slot)
        {
            if (BotOwner?.GetPlayer?.InventoryController?.Inventory?.Equipment == null)
            {
                return null;
            }

            return BotOwner.GetPlayer.InventoryController.Inventory.Equipment.GetSlot(slot)?.ContainedItem as Weapon;
        }
    }

    internal static class FollowerOutOfCombatReloadPolicy
    {
        private const int MinimumBetterMagazineGain = 2;

        private static readonly EquipmentSlot[] WeaponSlotsToTopOff =
        {
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.Holster
        };

        public static bool CanTopOffWeapon(BotOwner botOwner, Weapon weapon)
        {
            if (botOwner?.GetPlayer?.InventoryController == null || weapon == null)
            {
                return false;
            }

            if (InteractableObjects.IsLootedWeapon(botOwner, weapon))
            {
                return false;
            }

            if (IsLauncherWeapon(weapon))
            {
                // EFT's launcher reload checks can trigger automatic weapon switching when a
                // one-shot launcher cannot reload. Launcher use stays owned by combat objectives.
                return false;
            }

            // External-mag weapons are allowed to reload only through a prepared better magazine.
            // Other reload modes only need compatible loose ammo and then use EFT's normal reload.
            if (HasBetterMagazine(botOwner, weapon))
            {
                return true;
            }

            EFT.InventoryLogic.Magazine? currentMagazine = weapon.GetCurrentMagazine();
            int currentCount = GetCurrentLoadedCount(weapon);
            int maxCount = GetCurrentCapacity(weapon, currentMagazine);
            if (maxCount <= 0 || currentCount >= maxCount)
            {
                return false;
            }

            return HasCompatibleLooseAmmo(botOwner, weapon, currentMagazine);
        }

        public static bool HasBetterMagazine(BotOwner botOwner, Weapon weapon)
        {
            if (botOwner?.GetPlayer?.InventoryController == null || weapon == null)
            {
                return false;
            }

            if (IsLauncherWeapon(weapon))
            {
                return false;
            }

            Slot magazineSlot = weapon.GetMagazineSlot();
            if (magazineSlot == null)
            {
                return false;
            }

            bool lootedWeapon = InteractableObjects.IsLootedWeapon(botOwner, weapon);
            int currentCount = weapon.GetCurrentMagazineCount();
            List<EFT.InventoryLogic.Magazine> magazines = new List<EFT.InventoryLogic.Magazine>();
            botOwner.GetPlayer.InventoryController.GetReachableItemsOfTypeNonAlloc<EFT.InventoryLogic.Magazine>(magazines, null);
            foreach (EFT.InventoryLogic.Magazine magazine in magazines)
            {
                if (magazine == null ||
                    IsInstalledInWeaponTree(magazine) ||
                    (lootedWeapon &&
                     !InteractableObjects.IsApprovedLootedWeaponMagazine(botOwner, weapon, magazine)) ||
                    magazine.Count - currentCount < MinimumBetterMagazineGain)
                {
                    continue;
                }

                if (EFT.InventoryLogic.ItemManipulator.CheckMoveIgnoringTargetItem(
                        magazine,
                        magazineSlot,
                        botOwner.GetPlayer.InventoryController).Succeeded)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryFillCarriedWeaponMagazines(BotOwner botOwner)
        {
            if (botOwner?.GetPlayer?.InventoryController?.Inventory?.Equipment == null)
            {
                return false;
            }

            bool filledAny = false;
            foreach (EquipmentSlot slot in WeaponSlotsToTopOff)
            {
                Weapon? weapon = botOwner.GetPlayer.InventoryController.Inventory.Equipment.GetSlot(slot)?.ContainedItem as Weapon;
                if (weapon == null ||
                    InteractableObjects.IsLootedWeapon(botOwner, weapon) ||
                    IsLauncherWeapon(weapon) ||
                    weapon.ReloadMode != Weapon.EReloadMode.ExternalMagazine)
                {
                    // Chamber/internal non-launcher weapons have no magazine top-off stage; their normal reload consumes loose ammo.
                    continue;
                }

                EFT.InventoryLogic.Magazine? currentMagazine = weapon.GetCurrentMagazine();
                if (currentMagazine != null && currentMagazine.Count < currentMagazine.MaxCount)
                {
                    filledAny |= TryFillMagazineWithLooseAmmo(botOwner, currentMagazine);
                }

                filledAny |= TryFillReachableSpareMagazines(botOwner, weapon);
            }

            return filledAny;
        }

        private static bool HasCompatibleLooseAmmo(BotOwner botOwner, Weapon weapon, EFT.InventoryLogic.Magazine? currentMagazine)
        {
            if (botOwner?.GetPlayer?.InventoryController == null || weapon == null)
            {
                return false;
            }

            Slot? chamber = weapon.HasChambers && weapon.Chambers?.Length > 0
                ? weapon.Chambers[0]
                : null;
            if (chamber == null && currentMagazine == null)
            {
                return false;
            }

            List<EFT.InventoryLogic.Ammo> ammos = new List<EFT.InventoryLogic.Ammo>();
            botOwner.GetPlayer.InventoryController.GetAcceptableItemsNonAlloc<EFT.InventoryLogic.Ammo>(
                BotReload._availableEquipmentSlots,
                ammos,
                ammo =>
                    ammo != null &&
                    ammo.StackObjectsCount > 0 &&
                    ((chamber != null && chamber.CanAccept(ammo)) ||
                     currentMagazine?.Cartridges?.Filters?.CheckItemFilter(ammo) == true) &&
                    ammo.CheckAction(null).Succeeded,
                null);

            return ammos.Count > 0;
        }

        private static bool TryFillReachableSpareMagazines(BotOwner botOwner, Weapon weapon)
        {
            if (botOwner?.GetPlayer?.InventoryController == null || weapon == null)
            {
                return false;
            }

            Slot magazineSlot = weapon.GetMagazineSlot();
            if (magazineSlot == null)
            {
                return false;
            }

            bool filledAny = false;
            List<EFT.InventoryLogic.Magazine> magazines = new List<EFT.InventoryLogic.Magazine>();
            botOwner.GetPlayer.InventoryController.GetReachableItemsOfTypeNonAlloc<EFT.InventoryLogic.Magazine>(
                magazines,
                magazine =>
                    magazine != null &&
                    !IsInstalledInWeaponTree(magazine) &&
                    magazine.Count < magazine.MaxCount &&
                    EFT.InventoryLogic.ItemManipulator.CheckMoveIgnoringTargetItem(
                        magazine,
                        magazineSlot,
                        botOwner.GetPlayer.InventoryController).Succeeded);

            foreach (EFT.InventoryLogic.Magazine magazine in magazines)
            {
                // Partial fills are intentional: a 10-round stack should turn a 0/11 pistol mag into 10/11.
                filledAny |= TryFillMagazineWithLooseAmmo(botOwner, magazine);
            }

            return filledAny;
        }

        private static bool TryFillMagazineWithLooseAmmo(BotOwner botOwner, EFT.InventoryLogic.Magazine magazine)
        {
            if (botOwner?.GetPlayer?.InventoryController == null ||
                magazine?.Cartridges?.Filters == null ||
                magazine.MaxCount <= 0 ||
                magazine.Count >= magazine.MaxCount)
            {
                return false;
            }

            List<EFT.InventoryLogic.Ammo> ammos = new List<EFT.InventoryLogic.Ammo>();
            botOwner.GetPlayer.InventoryController.GetAcceptableItemsNonAlloc<EFT.InventoryLogic.Ammo>(
                BotReload._availableEquipmentSlots,
                ammos,
                ammo =>
                    ammo != null &&
                    ammo.StackObjectsCount > 0 &&
                    magazine.Cartridges.Filters.CheckItemFilter(ammo) &&
                    ammo.CheckAction(null).Succeeded,
                null);

            EFT.InventoryLogic.Ammo? bestAmmo = null;
            foreach (EFT.InventoryLogic.Ammo ammo in ammos)
            {
                if (bestAmmo == null || ammo.StackObjectsCount > bestAmmo.StackObjectsCount)
                {
                    bestAmmo = ammo;
                }
            }

            if (bestAmmo == null)
            {
                return false;
            }

            int roundsToMove = Math.Min(magazine.MaxCount - magazine.Count, bestAmmo.StackObjectsCount);
            if (roundsToMove <= 0)
            {
                return false;
            }

            var result = magazine.Apply(botOwner.GetPlayer.InventoryController, bestAmmo, roundsToMove, true);
            if (result.Failed)
            {
                return false;
            }

            botOwner.GetPlayer.InventoryController.TryRunNetworkTransaction(result, null);
            return true;
        }

        private static bool IsInstalledInWeaponTree(Item item)
        {
            if (item == null)
            {
                return false;
            }

            foreach (Item parent in item.GetAllParentItems())
            {
                if (parent is Weapon)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLauncherWeapon(Weapon weapon)
        {
            return FollowerCombatCommon.IsGrenadeLauncherWeapon(weapon);
        }

        private static int GetCurrentLoadedCount(Weapon weapon)
        {
            if (weapon.ReloadMode == Weapon.EReloadMode.OnlyBarrel)
            {
                return weapon.ChamberAmmoCount;
            }

            return weapon.GetCurrentMagazineCount();
        }

        private static int GetCurrentCapacity(Weapon weapon, EFT.InventoryLogic.Magazine? currentMagazine)
        {
            if (weapon.ReloadMode == Weapon.EReloadMode.OnlyBarrel && weapon.Chambers != null)
            {
                return weapon.Chambers.Length;
            }

            return currentMagazine?.MaxCount ?? weapon.GetMaxMagazineCount();
        }
    }

}
