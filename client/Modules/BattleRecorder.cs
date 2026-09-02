using BepInEx;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static class BattleRecorder
    {
        private const string UpdateHubSubscriptionId = "pitTeam.BattleRecorder";
        private const float FollowerWeaponActivityProbeSeconds = 0.1f;
        private const float GoalEnemyTransitionCoalesceSeconds = 1f;
        private const int FlushEventBatchSize = 64;
        private static readonly long FlushIntervalTicks = TimeSpan.FromSeconds(1).Ticks;

        private static readonly object SyncRoot = new object();
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.None
        };

        private static readonly Dictionary<string, RecorderFollowerState> FollowerStates =
            new Dictionary<string, RecorderFollowerState>(StringComparer.Ordinal);

        private static StreamWriter? writer;
        private static string? currentRaidId;
        private static string? currentLocationId;
        private static string? currentFilePath;
        private static int eventSequence;
        private static int eventsSinceFlush;
        private static long nextFlushUtcTicks;
        private static bool initialized;
        private static bool updateHubSubscribed;
        private static bool writeErrorLogged;

        [System.Diagnostics.Conditional("DEBUG")]
        public static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (pitFireTeam.battleRecorderEnabled != null)
            {
                pitFireTeam.battleRecorderEnabled.SettingChanged += OnEnabledSettingChanged;
            }

            initialized = true;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void Shutdown()
        {
            if (!initialized)
            {
                return;
            }

            if (pitFireTeam.battleRecorderEnabled != null)
            {
                pitFireTeam.battleRecorderEnabled.SettingChanged -= OnEnabledSettingChanged;
            }

            EndRaid("pluginShutdown");
            UnregisterUpdateHub();
            initialized = false;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void StartRaid(string? locationId)
        {
            if (!IsEnabled())
            {
                EndRaid("disabled");
                return;
            }

            EndRaid("newRaid");

            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                currentLocationId = string.IsNullOrWhiteSpace(locationId) ? "unknown" : locationId;
                currentRaidId = $"{timestamp}-{currentLocationId}";

                string rootDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BepInEx", "plugins", "pitFireTeam", "BattleRecords");
                Directory.CreateDirectory(rootDirectory);

                currentFilePath = Path.Combine(rootDirectory, $"{currentRaidId}.jsonl");
                writer = new StreamWriter(currentFilePath, false)
                {
                    AutoFlush = false
                };

                eventSequence = 0;
                eventsSinceFlush = 0;
                nextFlushUtcTicks = 0L;
                writeErrorLogged = false;
                FollowerStates.Clear();

                WriteEventInternal("raidStart", null, new
                {
                    raidId = currentRaidId,
                    locationId = currentLocationId,
                    file = currentFilePath,
                    schemaVersion = 10,
                    snapshotIntervalMs = GetSnapshotIntervalMs(),
                    followerWeaponActivityProbeMs = Mathf.RoundToInt(FollowerWeaponActivityProbeSeconds * 1000f),
                    goalEnemyTransitionCoalesceMs = Mathf.RoundToInt(GoalEnemyTransitionCoalesceSeconds * 1000f)
                });
                if (!IsRecording())
                {
                    return;
                }

                RegisterUpdateHub();
            }
            catch (Exception ex)
            {
                SafeLogRecorderError("Failed to start battle recorder.", ex);
                DisposeWriter();
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void EndRaid(string reason)
        {
            try
            {
                if (writer != null)
                {
                    FlushAllGoalEnemyTransitionRepeats();
                    WriteEventInternal("raidEnd", null, new
                    {
                        raidId = currentRaidId,
                        locationId = currentLocationId,
                        reason
                    });
                }
            }
            catch (Exception ex)
            {
                SafeLogRecorderError("Failed to finalize battle recorder.", ex);
            }
            finally
            {
                DisposeWriter();
                FollowerStates.Clear();
                currentRaidId = null;
                currentLocationId = null;
                currentFilePath = null;
                eventSequence = 0;
                eventsSinceFlush = 0;
                nextFlushUtcTicks = 0L;
                writeErrorLogged = false;
                UnregisterUpdateHub();
            }
        }

        private static void OnEnabledSettingChanged(object sender, EventArgs e)
        {
            if (!IsEnabled())
            {
                EndRaid("disabled");
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordCommandSet(
            BotFollowerPlayer follower,
            FollowerCommandType command,
            Vector3 target,
            float untilTime,
            string source)
        {
            BotOwner? bot = follower?.GetBot();
            if (!CanRecordBot(bot))
            {
                return;
            }

            if (ShouldSkipCommandRecord(bot!, command))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot!);

            WriteEventInternal("commandSet", bot, new
            {
                source,
                command = command.ToString(),
                untilTime = SanitizeFloat(untilTime),
                target = CreateVector(target),
                tactic = follower.CombatTactic.ToString(),
                state = CreateRecorderStatePayload(state)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordCommandCleared(
            BotFollowerPlayer follower,
            FollowerCommandType previousCommand,
            Vector3 previousTarget,
            float previousUntilTime,
            string reason)
        {
            BotOwner? bot = follower?.GetBot();
            if (!CanRecordBot(bot) || previousCommand == FollowerCommandType.None)
            {
                return;
            }

            if (ShouldSkipCommandRecord(bot!, previousCommand))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot!);

            WriteEventInternal("commandCleared", bot, new
            {
                reason,
                command = previousCommand.ToString(),
                untilTime = SanitizeFloat(previousUntilTime),
                target = CreateVector(previousTarget),
                tactic = follower.CombatTactic.ToString(),
                state = CreateRecorderStatePayload(state)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordCombatAggressionOverride(
            BotFollowerPlayer follower,
            string action,
            string source,
            bool previousActive,
            float previousAggression,
            bool currentActive,
            float currentAggression,
            float clearAfter)
        {
            BotOwner? bot = follower?.GetBot();
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot!);
            WriteEventInternal("combatAggressionOverride", bot, new
            {
                action,
                source,
                previous = new
                {
                    active = previousActive,
                    aggression = SanitizeFloat(previousAggression)
                },
                current = new
                {
                    active = currentActive,
                    aggression = SanitizeFloat(currentAggression),
                    clearAfter = clearAfter > 0f ? SanitizeFloat(clearAfter) : null,
                    clearInSeconds = clearAfter > 0f ? SanitizeFloat(Mathf.Max(0f, clearAfter - Time.time)) : null
                },
                state = CreateRecorderStatePayload(state)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordCommandDiagnostic(
            BotOwner bot,
            FollowerCommandType command,
            string action,
            string reason,
            Func<object?> detailsFactory)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            object? details = detailsFactory?.Invoke();

            WriteEventInternal("commandDiagnostic", bot, new
            {
                action,
                reason,
                command = command.ToString(),
                details,
                context = CreateTransitionContext(bot, state),
                snapshot = CreateBotSnapshot(bot, state)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordCombatLayerState(BotOwner bot, bool active, string reason)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            if (active && !state.InCombat)
            {
                state.CombatEpisodeId++;
                state.CombatStartedTime = Time.time;
                state.CurrentDecisionInstanceId = 0;
                state.CurrentDecisionSelectedTime = 0f;
                state.LastEndedDecisionInstanceId = 0;
                state.LastDecisionEndTime = 0f;
                state.LastDecisionEndReason = null;
                state.CurrentObjective = null;
                state.LastDecisionAction = null;
                state.LastDecisionReason = null;
                state.HasPreviousSnapshot = false;
                state.HasPreviousEffectiveMoveTarget = false;
            }

            state.InCombat = active;
            state.LastCombatSeenTime = Time.time;
            if (active)
            {
                state.NextSnapshotTime = Time.time;
            }

            WriteEventInternal(active ? "combatStart" : "combatStop", bot, new
            {
                reason,
                state = CreateRecorderStatePayload(state),
                snapshot = active ? CreateBotSnapshot(bot, state) : null
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordFollowerDeath(
            BotFollowerPlayer follower,
            Player player,
            IPlayer? aggressor,
            EBodyPart bodyPart,
            EDamageType lethalDamageType)
        {
            BotOwner? bot = follower?.GetBot();
            if (!IsRecording() || player == null || string.IsNullOrEmpty(player.ProfileId))
            {
                return;
            }

            RecorderFollowerState state = bot != null
                ? GetOrCreateState(bot)
                : GetOrCreateState(player.ProfileId);

            WriteEventInternal("followerDeath", bot, new
            {
                profileId = player.ProfileId,
                nickname = player.Profile?.Nickname,
                position = CreateVector(player.Transform.position),
                bodyPart = bodyPart.ToString(),
                lethalDamageType = lethalDamageType.ToString(),
                aggressor = aggressor != null
                    ? new
                    {
                        profileId = aggressor.ProfileId,
                        nickname = aggressor.Profile?.Nickname,
                        side = aggressor.Side.ToString()
                    }
                    : null,
                botState = bot?.BotState.ToString(),
                isDead = bot?.IsDead,
                medical = new
                {
                    healthStatus = player.HealthStatus.ToString()
                },
                state = CreateRecorderStatePayload(state),
                snapshot = bot != null ? CreateBotSnapshot(bot, state) : null
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordDecisionSelected(
            BotOwner bot,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? previousDecision,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> nextDecision,
            string? objectiveName)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            // BigBrain can finish a queued GetNextAction after the layer has already released.
            // That late callback must not resurrect the recorder's combat episode without a real
            // combatStart. The layer's next activation will establish a fresh episode explicitly.
            if (!state.InCombat)
            {
                return;
            }

            int previousDecisionInstanceId = state.CurrentDecisionInstanceId;
            float previousDecisionSelectedTime = state.CurrentDecisionSelectedTime;
            bool sameAsPrevious = previousDecision.HasValue &&
                                  previousDecision.Value.Action == nextDecision.Action &&
                                  string.Equals(previousDecision.Value.Reason, nextDecision.Reason, StringComparison.Ordinal);

            state.LastCombatSeenTime = Time.time;
            state.CurrentDecisionInstanceId = ++state.DecisionInstanceSequence;
            state.CurrentDecisionSelectedTime = Time.time;
            state.LastDecisionAction = nextDecision.Action.ToString();
            state.LastDecisionReason = nextDecision.Reason;
            if (!string.IsNullOrEmpty(objectiveName))
            {
                state.CurrentObjective = objectiveName;
            }

            WriteEventInternal("decisionSelected", bot, new
            {
                objective = objectiveName,
                combatEpisodeId = state.CombatEpisodeId,
                decisionInstanceId = state.CurrentDecisionInstanceId,
                selectedAt = SanitizeFloat(state.CurrentDecisionSelectedTime),
                previous = previousDecision.HasValue ? CreateDecisionPayload(previousDecision.Value) : null,
                next = CreateDecisionPayload(nextDecision),
                transition = new
                {
                    previousDecisionInstanceId = previousDecisionInstanceId > 0
                        ? (int?)previousDecisionInstanceId
                        : null,
                    previousDecisionAge = previousDecisionSelectedTime > 0f
                        ? SanitizeFloat(Time.time - previousDecisionSelectedTime)
                        : null,
                    sameAsPrevious,
                    previousEnded = previousDecisionInstanceId > 0 &&
                                    state.LastEndedDecisionInstanceId == previousDecisionInstanceId,
                    previousEndReason = previousDecisionInstanceId > 0 &&
                                        state.LastEndedDecisionInstanceId == previousDecisionInstanceId
                        ? state.LastDecisionEndReason
                        : null,
                    sincePreviousEnd = previousDecisionInstanceId > 0 &&
                                       state.LastEndedDecisionInstanceId == previousDecisionInstanceId &&
                                       state.LastDecisionEndTime > 0f
                        ? SanitizeFloat(Time.time - state.LastDecisionEndTime)
                        : null
                },
                state = CreateRecorderStatePayload(state)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordDecisionEnd(
            BotOwner bot,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams> currentDecision,
            AICoreActionEnd endResult,
            string? objectiveName)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            if (!string.IsNullOrEmpty(objectiveName))
            {
                state.CurrentObjective = objectiveName;
            }

            int decisionInstanceId = state.CurrentDecisionInstanceId;
            float endedAt = Time.time;
            float? duration = state.CurrentDecisionSelectedTime > 0f
                ? SanitizeFloat(endedAt - state.CurrentDecisionSelectedTime)
                : null;
            bool duplicateEnd = decisionInstanceId > 0 && state.LastEndedDecisionInstanceId == decisionInstanceId;

            WriteEventInternal("decisionEnd", bot, new
            {
                objective = objectiveName,
                combatEpisodeId = state.CombatEpisodeId,
                decisionInstanceId = decisionInstanceId > 0 ? (int?)decisionInstanceId : null,
                selectedAt = state.CurrentDecisionSelectedTime > 0f
                    ? SanitizeFloat(state.CurrentDecisionSelectedTime)
                    : null,
                endedAt = SanitizeFloat(endedAt),
                duration,
                duplicateEnd,
                decision = CreateDecisionPayload(currentDecision),
                end = new
                {
                    shouldEnd = endResult.Value,
                    reason = endResult.Reason
                },
                state = CreateRecorderStatePayload(state)
            });

            state.LastEndedDecisionInstanceId = decisionInstanceId;
            state.LastDecisionEndTime = endedAt;
            state.LastDecisionEndReason = endResult.Reason;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordObjectiveSwitch(BotOwner bot, string objectiveName, string reason)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            state.CurrentObjective = objectiveName;
            state.LastCombatSeenTime = Time.time;

            WriteEventInternal("objectiveSwitch", bot, new
            {
                objective = objectiveName,
                reason,
                state = CreateRecorderStatePayload(state)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordObjectiveDiagnostic(
            BotOwner bot,
            string objectiveName,
            string action,
            string reason,
            object? details = null)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            if (!IsBotInRecordedCombat(bot, state))
            {
                return;
            }

            WriteEventInternal("objectiveDiagnostic", bot, new
            {
                objective = objectiveName,
                action,
                reason,
                details,
                context = CreateTransitionContext(bot, state)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordObjectiveDiagnostic(
            BotOwner bot,
            string objectiveName,
            string action,
            string reason,
            Func<object?> detailsFactory)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            if (!IsBotInRecordedCombat(bot, state))
            {
                return;
            }

            object? details = detailsFactory?.Invoke();
            WriteEventInternal("objectiveDiagnostic", bot, new
            {
                objective = objectiveName,
                action,
                reason,
                details,
                context = CreateTransitionContext(bot, state)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordGoalEnemyTransition(
            BotOwner bot,
            EnemyInfo? previous,
            EnemyInfo? next,
            string source,
            string reason,
            bool allowed)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            if (previous == null && next == null)
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            float now = Time.time;
            if (state.GoalEnemyTransitionSignature != null &&
                now - state.GoalEnemyTransitionFirstTime <= GoalEnemyTransitionCoalesceSeconds &&
                state.GoalEnemyTransitionSignature.Matches(bot, previous, next, source, reason, allowed, state))
            {
                state.GoalEnemyTransitionRepeatCount++;
                state.GoalEnemyTransitionLastTime = now;
                return;
            }

            FlushGoalEnemyTransitionRepeats(bot, state);
            WriteEventInternal("goalEnemyTransition", bot, new
            {
                source,
                reason,
                allowed,
                previous = CreateTransitionEnemyContext(bot, previous),
                next = CreateTransitionEnemyContext(bot, next),
                context = CreateTransitionContext(bot, state)
            });

            state.GoalEnemyTransitionSignature = new GoalEnemyTransitionSignature(
                bot,
                previous,
                next,
                source,
                reason,
                allowed,
                state);
            state.GoalEnemyTransitionFirstTime = now;
            state.GoalEnemyTransitionLastTime = now;
            state.GoalEnemyTransitionRepeatCount = 0;
            state.GoalEnemyTransitionBot = bot;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordPushEmitted(
            BotOwner owner,
            string enemyProfileId,
            Vector3 enemyPosition,
            Vector3 destination,
            string reason,
            bool isSearchPush)
        {
            if (!CanRecordBot(owner))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(owner);
            if (!IsBotInRecordedCombat(owner, state))
            {
                return;
            }

            WriteEventInternal("pushEvent", owner, new
            {
                action = "emit",
                enemyProfileId,
                enemyPosition = CreateVector(enemyPosition),
                destination = CreateVector(destination),
                reason,
                isSearchPush
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordPushReleased(BotOwner owner, string reason)
        {
            if (!CanRecordBot(owner))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(owner);
            if (!IsBotInRecordedCombat(owner, state))
            {
                return;
            }

            WriteEventInternal("pushEvent", owner, new
            {
                action = "release",
                reason
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordGrenadeEvent(
            BotOwner bot,
            string action,
            string reason,
            bool? completed = null,
            EnemyInfo? goalEnemy = null,
            Vector3? target = null,
            Vector3? suppressFrom = null)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);

            WriteEventInternal("grenadeEvent", bot, new
            {
                action,
                reason,
                completed,
                target = target.HasValue && IsFinite(target.Value) ? CreateVector(target.Value) : null,
                suppressFrom = suppressFrom.HasValue && IsFinite(suppressFrom.Value) ? CreateVector(suppressFrom.Value) : null,
                state = CreateRecorderStatePayload(state),
                context = CreateGrenadeContext(bot, goalEnemy)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordPushCleared(string reason)
        {
            if (!IsRecording() || !AnyFollowerInRecordedCombat())
            {
                return;
            }

            WriteEventInternal("pushEvent", null, new
            {
                action = "clear",
                reason
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordCommitmentEvent(
            BotOwner bot,
            string commitment,
            string action,
            string? reason,
            AICoreActionResult<BotLogicDecision, CoreActionResultParams>? decision = null,
            Vector3? target = null,
            int? coverId = null,
            bool? preferCover = null,
            float? untilTime = null)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            if (!IsBotInRecordedCombat(bot, state))
            {
                return;
            }

            WriteEventInternal("commitmentEvent", bot, new
            {
                commitment,
                action,
                reason,
                decision = decision.HasValue ? CreateDecisionPayload(decision.Value) : null,
                target = target.HasValue && IsFinite(target.Value) ? CreateVector(target.Value) : null,
                coverId,
                preferCover,
                untilTime = untilTime.HasValue ? SanitizeFloat(untilTime.Value) : null,
                context = CreateTransitionContext(bot, state)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordCombatPosturePolicy(
            BotOwner bot,
            string action,
            string posture,
            bool allowed,
            string reason,
            float enemyDistance,
            Vector3? target = null)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            if (!IsBotInRecordedCombat(bot, state))
            {
                return;
            }

            WriteEventInternal("posturePolicy", bot, new
            {
                action,
                posture,
                allowed,
                reason,
                enemyDistance = SanitizeFloat(enemyDistance),
                target = target.HasValue && IsFinite(target.Value) ? CreateVector(target.Value) : null,
                currentPose = SanitizeFloat(bot.GetPlayer?.MovementContext?.PoseLevel ?? 0f),
                targetPose = SanitizeFloat(bot.Mover?.TargetPose ?? 0f),
                underFire = bot.Memory?.IsUnderFire == true,
                inCover = bot.Memory?.IsInCover == true,
                context = CreateTransitionContext(bot, state)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordCombatFireEvent(
            BotOwner bot,
            string action,
            string? reason,
            string gate,
            string? targetReason,
            bool suppression,
            bool shootRequested,
            bool shootStarted,
            float? aimAngle,
            Vector3? target)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            if (!IsBotInRecordedCombat(bot, state))
            {
                return;
            }

            WriteEventInternal("combatFire", bot, new
            {
                action,
                reason,
                gate,
                targetReason,
                suppression,
                shootRequested,
                shootStarted,
                aimAngle = aimAngle.HasValue ? SanitizeFloat(aimAngle.Value) : null,
                target = target.HasValue && IsFinite(target.Value) ? CreateVector(target.Value) : null,
                activity = CreateCombatActivitySnapshot(bot),
                context = CreateTransitionContext(bot, state)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordAimTargetSelection(
            BotOwner bot,
            EnemyInfo enemyInfo,
            EnemyPart? previousPart,
            bool previousPartEligible,
            bool previousRetargetTimerActive,
            float precisionPercent,
            float headPreference,
            bool hasCorrectedParts,
            bool correctedHead,
            bool correctedBody,
            bool eligibleHead,
            bool eligibleBody,
            int eligibleNonHeadCount,
            bool forcedHead,
            bool headRollAttempted,
            bool headRollSucceeded,
            EnemyPart? selectedPart,
            Vector3? selectedPoint)
        {
            if (!CanRecordBot(bot) || enemyInfo == null)
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            if (!IsBotInRecordedCombat(bot, state))
            {
                return;
            }

            enemyInfo._allParts.TryGetValue(BodyPartType.head, out EnemyPart? rawHead);
            enemyInfo._allParts.TryGetValue(BodyPartType.body, out EnemyPart? rawBody);
            enemyInfo._allPartsVision.TryGetValue(BodyPartType.head, out EnemyPartVision? rawHeadVision);
            enemyInfo._allPartsVision.TryGetValue(BodyPartType.body, out EnemyPartVision? rawBodyVision);

            Vector3? enemyRoot = enemyInfo.Person?.Transform != null
                ? enemyInfo.Person.Transform.position
                : null;
            string retargetReason = previousPart == null
                ? "noPreviousPart"
                : !previousPartEligible
                    ? "previousPartIneligible"
                    : "retargetTimerExpired";

            WriteEventInternal("aimTargetSelection", bot, new
            {
                enemy = new
                {
                    profileId = enemyInfo.ProfileId ?? enemyInfo.Person?.ProfileId,
                    nickname = enemyInfo.Person?.Profile?.Nickname,
                    distance = SanitizeFloat(enemyInfo.Distance),
                    visibleType = enemyInfo.VisibleType.ToString(),
                    isVisible = enemyInfo.IsVisible,
                    canShoot = enemyInfo.CanShoot,
                    root = enemyRoot.HasValue && IsFinite(enemyRoot.Value)
                        ? CreateVector(enemyRoot.Value)
                        : null
                },
                retarget = new
                {
                    reason = retargetReason,
                    previousPart = previousPart?.BodyPartType.ToString(),
                    previousPartEligible,
                    previousRetargetTimerActive
                },
                proficiency = new
                {
                    precisionPercent = SanitizeFloat(precisionPercent),
                    headPreference = SanitizeFloat(headPreference)
                },
                correction = new
                {
                    hasCorrectedParts,
                    headShootable = correctedHead,
                    bodyShootable = correctedBody
                },
                rawParts = new
                {
                    head = CreateAimPartDiagnostic(rawHead, rawHeadVision, enemyRoot),
                    body = CreateAimPartDiagnostic(rawBody, rawBodyVision, enemyRoot)
                },
                eligibility = new
                {
                    head = eligibleHead,
                    body = eligibleBody,
                    nonHeadCount = eligibleNonHeadCount
                },
                roll = new
                {
                    forcedHead,
                    attempted = headRollAttempted,
                    succeeded = headRollSucceeded
                },
                selected = selectedPart != null
                    ? new
                    {
                        part = selectedPart.BodyPartType.ToString(),
                        point = selectedPoint.HasValue && IsFinite(selectedPoint.Value)
                            ? CreateVector(selectedPoint.Value)
                            : null,
                        heightFromEnemyRoot = selectedPoint.HasValue &&
                                              enemyRoot.HasValue &&
                                              IsFinite(selectedPoint.Value) &&
                                              IsFinite(enemyRoot.Value)
                            ? SanitizeFloat(selectedPoint.Value.y - enemyRoot.Value.y)
                            : null
                    }
                    : null,
                context = CreateTransitionContext(bot, state)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordCombatMovementEvent(
            BotOwner bot,
            string action,
            string? reason,
            string mode,
            string gate,
            Vector3? target = null)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            if (!IsBotInRecordedCombat(bot, state))
            {
                return;
            }

            WriteEventInternal("combatMovement", bot, new
            {
                action,
                reason,
                mode,
                gate,
                target = target.HasValue && IsFinite(target.Value) ? CreateVector(target.Value) : null,
                movement = CreateCombatMovementGateContext(bot, target),
                context = CreateTransitionContext(bot, state)
            });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void RecordFollowerWeaponSafetyEvent(BotOwner bot, string reason)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
            if (Time.time < state.NextWeaponSafetyRecordTime)
            {
                return;
            }

            state.NextWeaponSafetyRecordTime = Time.time + 1f;
            WriteEventInternal("followerWeaponSafety", bot, new
            {
                reason,
                activity = CreateCombatActivitySnapshot(bot),
                state = CreateRecorderStatePayload(state)
            });
        }

        private static void OnBotManualUpdate(BotOwner owner)
        {
            try
            {
                if (!IsEnabled())
                {
                    EndRaid("disabled");
                    return;
                }

                if (owner == null || !IsRecording() || string.IsNullOrEmpty(owner.ProfileId))
                {
                    return;
                }

                if (!BossPlayers.IsFollower(owner))
                {
                    return;
                }

                RecorderFollowerState state = GetOrCreateState(owner);
                FlushExpiredGoalEnemyTransitionRepeats(owner, state);
                bool layerActive = FollowerCombatLayer.IsFollowerCombatLayerActive(owner);
                TryRecordFollowerWeaponActivity(owner, state, layerActive);
                BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(owner);
                bool hasActiveCommand = followerData?.TryPeekActiveCommand(out _, out _, out _) == true;
                bool postCombatFullHealActive = FollowerMedical.IsPostCombatFullHealActive(owner);
                bool shouldSnapshot = state.InCombat ||
                                      layerActive ||
                                      postCombatFullHealActive ||
                                      hasActiveCommand;
                if (!shouldSnapshot)
                {
                    return;
                }

                if (Time.time < state.NextSnapshotTime)
                {
                    return;
                }

                state.NextSnapshotTime = Time.time + GetSnapshotIntervalSeconds();
                WriteEventInternal("snapshot", owner, CreateBotSnapshot(owner, state));
            }
            catch (Exception ex)
            {
                StopAfterFatalRecorderFailure("Battle recorder update failure; recording stopped for this raid.", ex);
            }
        }

        private static void TryRecordFollowerWeaponActivity(
            BotOwner owner,
            RecorderFollowerState state,
            bool layerActive)
        {
            if (Time.time < state.NextWeaponActivityProbeTime)
            {
                return;
            }

            state.NextWeaponActivityProbeTime = Time.time + FollowerWeaponActivityProbeSeconds;

            BotWeaponManager? weaponManager = owner.WeaponManager;
            ShootData? shootData = owner.ShootData;
            Weapon? activeWeapon = weaponManager?.ShootController?.Item ?? weaponManager?.CurrentWeapon;
            string? activeWeaponId = activeWeapon?.Id;
            int loadedRounds = activeWeapon != null
                ? FollowerCombatCommon.CountLoadedRounds(activeWeapon)
                : -1;
            bool shooting = shootData?.Shooting == true;
            bool reloading = weaponManager?.Reload?.Reloading == true;
            float lastTriggerPressedTime = shootData?.LastTriggerPressd ?? 0f;

            if (!state.HasWeaponActivitySample)
            {
                state.HasWeaponActivitySample = true;
                state.LastWeaponActivityShooting = shooting;
                state.LastWeaponActivityReloading = reloading;
                state.LastWeaponActivityTriggerPressedTime = lastTriggerPressedTime;
                state.LastWeaponActivityWeaponId = activeWeaponId;
                state.LastWeaponActivityLoadedRounds = loadedRounds;
                return;
            }

            bool shootingChanged = shooting != state.LastWeaponActivityShooting;
            bool reloadingChanged = reloading != state.LastWeaponActivityReloading;
            bool triggerAdvanced = lastTriggerPressedTime > state.LastWeaponActivityTriggerPressedTime + 0.001f;
            bool weaponChanged = !string.Equals(
                activeWeaponId,
                state.LastWeaponActivityWeaponId,
                StringComparison.Ordinal);
            bool loadedRoundsChanged = !weaponChanged &&
                                       loadedRounds >= 0 &&
                                       state.LastWeaponActivityLoadedRounds >= 0 &&
                                       loadedRounds != state.LastWeaponActivityLoadedRounds;

            if (shootingChanged || reloadingChanged || triggerAdvanced || weaponChanged || loadedRoundsChanged)
            {
                WriteEventInternal("followerWeaponActivity", owner, new
                {
                    scope = state.InCombat || layerActive ? "combat" : "outOfCombat",
                    transitions = new
                    {
                        shootingChanged,
                        reloadingChanged,
                        triggerAdvanced,
                        weaponChanged,
                        loadedRoundsChanged
                    },
                    previous = new
                    {
                        shooting = state.LastWeaponActivityShooting,
                        reloading = state.LastWeaponActivityReloading,
                        lastTriggerPressedTime = state.LastWeaponActivityTriggerPressedTime > 0f
                            ? SanitizeFloat(state.LastWeaponActivityTriggerPressedTime)
                            : null,
                        weaponId = state.LastWeaponActivityWeaponId,
                        loadedRounds = state.LastWeaponActivityLoadedRounds >= 0
                            ? (int?)state.LastWeaponActivityLoadedRounds
                            : null
                    },
                    current = new
                    {
                        shooting,
                        reloading,
                        lastTriggerPressedTime = lastTriggerPressedTime > 0f
                            ? SanitizeFloat(lastTriggerPressedTime)
                            : null,
                        weaponId = activeWeaponId,
                        loadedRounds = loadedRounds >= 0 ? (int?)loadedRounds : null,
                        weaponReady = weaponManager?.IsWeaponReady == true,
                        haveBullets = weaponManager?.HaveBullets == true
                    },
                    state = CreateRecorderStatePayload(state)
                });
            }

            state.LastWeaponActivityShooting = shooting;
            state.LastWeaponActivityReloading = reloading;
            state.LastWeaponActivityTriggerPressedTime = lastTriggerPressedTime;
            state.LastWeaponActivityWeaponId = activeWeaponId;
            state.LastWeaponActivityLoadedRounds = loadedRounds;
        }

        private static object CreateBotSnapshot(BotOwner bot, RecorderFollowerState state)
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(bot);
            FollowerCommandType command = FollowerCommandType.None;
            Vector3 commandTarget = Vector3.zero;
            float commandUntilTime = 0f;
            if (followerData != null)
            {
                followerData.TryPeekActiveCommand(out command, out commandTarget, out commandUntilTime);
            }

            Vector3 currentPosition = bot.Position;
            Player? player = bot.GetPlayer ?? bot.AIData?.Player;
            var movementContext = player?.MovementContext;
            Vector3 lookDirection = NormalizePlanar(bot.LookDirection);
            Vector3 bodyDirection = player?.Transform != null
                ? NormalizePlanar(player.Transform.forward)
                : bot.Transform != null
                    ? NormalizePlanar(bot.Transform.forward)
                    : lookDirection;
            bool hasGoToPointTarget = TryGetCurrentMoveTarget(bot, out Vector3 goToPointTarget);
            bool hasMoverTarget = TryGetMoverTarget(bot, out Vector3 moverTarget);
            bool hasEffectiveMoveTarget = hasMoverTarget || hasGoToPointTarget;
            Vector3 effectiveMoveTarget = hasMoverTarget ? moverTarget : goToPointTarget;
            string? effectiveMoveTargetSource = hasMoverTarget
                ? "moverTargetPoint"
                : hasGoToPointTarget
                    ? "goToSomePointData"
                    : null;
            Vector3 moveTargetDirection = hasGoToPointTarget
                ? NormalizePlanar(goToPointTarget - currentPosition)
                : Vector3.zero;
            Vector3 effectiveMoveTargetDirection = hasEffectiveMoveTarget
                ? NormalizePlanar(effectiveMoveTarget - currentPosition)
                : Vector3.zero;

            Vector3 movementDirection = Vector3.zero;
            bool hasMovementDirection = false;
            float snapshotElapsed = 0f;
            float distanceMovedSinceSnapshot = 0f;
            if (state.HasPreviousSnapshot)
            {
                Vector3 delta = currentPosition - state.LastSnapshotPosition;
                snapshotElapsed = Mathf.Max(0f, Time.time - state.LastSnapshotTime);
                distanceMovedSinceSnapshot = delta.magnitude;
                if (delta.sqrMagnitude > 0.0025f)
                {
                    movementDirection = NormalizePlanar(delta);
                    hasMovementDirection = movementDirection.sqrMagnitude > 0.0001f;
                }
            }

            float effectiveTargetDistance = hasEffectiveMoveTarget
                ? Vector3.Distance(currentPosition, effectiveMoveTarget)
                : 0f;
            bool sameEffectiveTargetAsPrevious = hasEffectiveMoveTarget &&
                                                 state.HasPreviousEffectiveMoveTarget &&
                                                 (effectiveMoveTarget - state.LastEffectiveMoveTarget).sqrMagnitude <= 0.25f;
            float? effectiveTargetProgress = sameEffectiveTargetAsPrevious
                ? SanitizeFloat(state.LastEffectiveMoveTargetDistance - effectiveTargetDistance)
                : null;

            EnemyInfo? goalEnemy = bot.Memory?.GoalEnemy;
            object? enemySnapshot = CreateEnemySnapshot(
                bot,
                goalEnemy,
                currentPosition,
                lookDirection,
                bodyDirection,
                moveTargetDirection,
                effectiveMoveTargetDirection);
            object? bossSnapshot = CreateBossSnapshot(bot, currentPosition, lookDirection);

            var snapshot = new
            {
                state = CreateRecorderStatePayload(state),
                botState = bot.BotState.ToString(),
                position = CreateVector(currentPosition),
                lookDirection = CreateVector(lookDirection),
                currentMoveTarget = hasGoToPointTarget ? CreateVector(goToPointTarget) : null,
                moveTargets = new
                {
                    goToSomePoint = hasGoToPointTarget ? CreateVector(goToPointTarget) : null,
                    moverTargetPoint = hasMoverTarget ? CreateVector(moverTarget) : null,
                    effective = hasEffectiveMoveTarget ? CreateVector(effectiveMoveTarget) : null,
                    effectiveSource = effectiveMoveTargetSource
                },
                movement = new
                {
                    sprinting = bot.Mover?.Sprinting == true,
                    hasActiveMoverPath = bot.Mover?.HasPathAndNoComplete == true,
                    hasPathTarget = bot.GoToSomePointData?.HaveTarget() == true,
                    reachedTarget = bot.GoToSomePointData?.IsCome() == true,
                    targetPose = SanitizeFloat(bot.Mover?.TargetPose ?? 0f),
                    poseLevel = SanitizeFloat(movementContext?.PoseLevel ?? 0f),
                    prone = movementContext?.IsInPronePose == true,
                    canSprintPlayer = bot.CanSprintPlayer,
                    moverNoSprint = bot.Mover?.NoSprint == true,
                    movementCanSprint = movementContext?.CanSprint,
                    movementCanWalk = movementContext?.CanWalk,
                    sprintEnabled = movementContext?.IsSprintEnabled,
                    controlMovementDirection = movementContext != null
                        ? CreateVector(new Vector3(
                            movementContext.MovementDirection.x,
                            0f,
                            movementContext.MovementDirection.y))
                        : null,
                    clampedSpeed = movementContext != null
                        ? SanitizeFloat(movementContext.ClampedSpeed)
                        : null,
                    stamina = player?.Physical?.Stamina != null
                        ? SanitizeFloat(player.Physical.Stamina.NormalValue)
                        : null,
                    bodyDirection = bodyDirection.sqrMagnitude > 0.0001f
                        ? CreateVector(bodyDirection)
                        : null,
                    snapshotElapsed = state.HasPreviousSnapshot ? SanitizeFloat(snapshotElapsed) : null,
                    distanceMovedSinceSnapshot = state.HasPreviousSnapshot
                        ? SanitizeFloat(distanceMovedSinceSnapshot)
                        : null,
                    speedMetersPerSecond = state.HasPreviousSnapshot && snapshotElapsed > 0.001f
                        ? SanitizeFloat(distanceMovedSinceSnapshot / snapshotElapsed)
                        : null,
                    effectiveTargetDistance = hasEffectiveMoveTarget ? SanitizeFloat(effectiveTargetDistance) : null,
                    sameEffectiveTargetAsPrevious,
                    progressTowardEffectiveTarget = effectiveTargetProgress,
                    direction = hasMovementDirection ? CreateVector(movementDirection) : null,
                    lookVsMoveAngle = hasMovementDirection ? SanitizeFloat(Vector3.Angle(lookDirection, movementDirection)) : null,
                    lookVsMoveTargetAngle = hasGoToPointTarget
                        ? SanitizeFloat(Vector3.Angle(lookDirection, moveTargetDirection))
                        : null,
                    lookVsEffectiveMoveTargetAngle = hasEffectiveMoveTarget
                        ? SanitizeFloat(Vector3.Angle(lookDirection, effectiveMoveTargetDirection))
                        : null,
                    bodyVsMoveAngle = hasMovementDirection && bodyDirection.sqrMagnitude > 0.0001f
                        ? SanitizeFloat(Vector3.Angle(bodyDirection, movementDirection))
                        : null,
                    bodyVsEffectiveMoveTargetAngle = hasEffectiveMoveTarget && bodyDirection.sqrMagnitude > 0.0001f
                        ? SanitizeFloat(Vector3.Angle(bodyDirection, effectiveMoveTargetDirection))
                        : null
                },
                memory = new
                {
                    haveEnemy = bot.Memory?.HaveEnemy == true,
                    underFire = bot.Memory?.IsUnderFire == true,
                    inCover = bot.Memory?.IsInCover == true,
                    damagedRecently = FollowerCombatCommon.WasHitRecently(bot, 0.5f) ||
                                      FollowerAwareness.WasRecentlyDamaged(bot),
                    threatenedRecently = FollowerAwareness.WasRecentlyThreatened(bot),
                    hitRecently = FollowerCombatCommon.WasHitRecently(bot, 0.5f) ||
                                  FollowerAwareness.WasRecentlyHit(bot)
                },
                command = followerData != null && command != FollowerCommandType.None
                    ? new
                    {
                        type = command.ToString(),
                        target = CreateVector(commandTarget),
                        untilTime = SanitizeFloat(commandUntilTime)
                    }
                    : null,
                medical = new
                {
                    firstAidPending = bot.Medecine?.FirstAid?.Have2Do == true,
                    firstAidUsing = bot.Medecine?.FirstAid?.Using == true,
                    surgeryPending = bot.Medecine?.SurgicalKit?.HaveWork == true,
                    surgeryUsing = bot.Medecine?.SurgicalKit?.Using == true,
                    postCombatFullHealActive = FollowerMedical.IsPostCombatFullHealActive(bot),
                    healthStatus = bot.GetPlayer?.HealthStatus.ToString()
                },
                brain = new
                {
                    layer = bot.Brain?.BaseBrain?.CurLayerInfo?.Name(),
                    node = bot.Brain?.Agent?.GetActiveNodeName(),
                    lastAction = bot.Brain?.Agent?.LastResult().Action.ToString(),
                    lastReason = bot.Brain?.Agent?.LastResult().Reason
                },
                dogFight = bot.DogFight?.DogFightState.ToString(),
                weapon = CreateLightWeaponSnapshot(bot),
                combatActivity = CreateCombatActivitySnapshot(bot),
                health = CreateLimbStatusSnapshot(bot),
                cover = CreateCoverSnapshot(bot),
                enemy = enemySnapshot,
                boss = bossSnapshot,
                targetCommitment = CreateTargetCommitmentSnapshot(bot, followerData, goalEnemy),
                tactic = followerData?.CombatTactic.ToString(),
                proficiency = CreateProficiencySnapshot(bot, followerData),
                combatSettings = followerData != null
                    ? new
                    {
                        aggression = SanitizeFloat(followerData.CombatAggression),
                        effectiveAggression = SanitizeFloat(followerData.EffectiveCombatAggression),
                        weaponAdjustedAggression = SanitizeFloat(
                            FollowerWeaponAggressionOverrides.Apply(bot, followerData.EffectiveCombatAggression)),
                        temporaryAggressionOverride = followerData.IsTemporaryCombatAggressionOverrideActive,
                        combatIndependent = followerData.CombatIndependent
                    }
                    : null
            };

            state.LastSnapshotPosition = currentPosition;
            state.LastSnapshotTime = Time.time;
            state.HasPreviousSnapshot = true;
            state.HasPreviousEffectiveMoveTarget = hasEffectiveMoveTarget;
            if (hasEffectiveMoveTarget)
            {
                state.LastEffectiveMoveTarget = effectiveMoveTarget;
                state.LastEffectiveMoveTargetDistance = effectiveTargetDistance;
            }
            return snapshot;
        }

        private static object? CreateProficiencySnapshot(BotOwner bot, BotFollowerPlayer? follower)
        {
            if (bot == null || follower == null)
            {
                return null;
            }

            FollowerProficiencyModifierValues modifiers = follower.Proficiency.Modifiers;
            BotCurrentSettings current = bot.Settings?.Current;
            return new
            {
                configured = new
                {
                    visionDistance = SanitizeFloat(modifiers.VisionDistance),
                    visionSpeed = SanitizeFloat(modifiers.VisionSpeed),
                    aimSpeed = SanitizeFloat(modifiers.AimSpeed),
                    accuracy = SanitizeFloat(modifiers.Accuracy)
                },
                factors = new
                {
                    visionDistance = SanitizeFloat(modifiers.VisionDistanceFactor),
                    visionSpeed = SanitizeFloat(modifiers.VisionSpeedFactor),
                    aimSpeed = SanitizeFloat(modifiers.AimSpeedFactor),
                    accuracy = SanitizeFloat(modifiers.AccuracyFactor),
                    safeVisionDistance = SanitizeFloat(modifiers.SafeVisionDistanceFactor),
                    safeVisionSpeed = SanitizeFloat(modifiers.SafeVisionSpeedFactor),
                    safeAimSpeed = SanitizeFloat(modifiers.SafeAimSpeedFactor),
                    safeAccuracy = SanitizeFloat(modifiers.SafeAccuracyFactor)
                },
                effective = current != null
                    ? new
                    {
                        visibleDistance = SanitizeFloat(current.CurrentVisibleDistance),
                        runtimeVisionEffect = SanitizeFloat(current.RuntimeVisionEffectsK),
                        precisionSpeed = SanitizeFloat(current.CurrentPrecicingSpeed),
                        scattering = SanitizeFloat(current.CurrentScattering),
                        closeScattering = SanitizeFloat(current.CurrentScatteringClose)
                    }
                    : null,
                lastAimTime = new
                {
                    baseline = follower.LastBaseAimTime.HasValue
                        ? SanitizeFloat(follower.LastBaseAimTime.Value)
                        : null,
                    final = follower.LastFinalAimTime.HasValue
                        ? SanitizeFloat(follower.LastFinalAimTime.Value)
                        : null
                },
                ownership = pitFireTeam.UseSainFollowerCombat
                    ? "sainAddon"
                    : pitFireTeam.IsSAINInstalled
                        ? "sainCalculationsCoreCombat"
                        : "vanillaCore"
            };
        }

        private static object CreateDecisionPayload(AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
        {
            return new
            {
                action = decision.Action.ToString(),
                reason = decision.Reason,
                dataType = decision.Data?.GetType().Name
            };
        }

        private static object CreateRecorderStatePayload(RecorderFollowerState state)
        {
            return new
            {
                inCombat = state.InCombat,
                combatEpisodeId = state.CombatEpisodeId,
                combatAge = state.CombatStartedTime > 0f
                    ? SanitizeFloat(Time.time - state.CombatStartedTime)
                    : null,
                objective = state.CurrentObjective,
                decisionInstanceId = state.CurrentDecisionInstanceId > 0
                    ? (int?)state.CurrentDecisionInstanceId
                    : null,
                decisionAge = state.CurrentDecisionSelectedTime > 0f
                    ? SanitizeFloat(Time.time - state.CurrentDecisionSelectedTime)
                    : null,
                lastDecisionAction = state.LastDecisionAction,
                lastDecisionReason = state.LastDecisionReason
            };
        }

        private static object CreateCombatActivitySnapshot(BotOwner bot)
        {
            var weaponManager = bot.WeaponManager;
            var shootController = weaponManager?.ShootController;
            var currentAiming = bot.AimingManager?.CurrentAiming;
            ShootData? shootData = bot.ShootData;

            return new
            {
                shooting = shootData?.Shooting == true,
                canShootByState = shootData?.CanShootByState,
                lastTriggerPressedAge = shootData != null && shootData.LastTriggerPressd > 0f
                    ? SanitizeFloat(Time.time - shootData.LastTriggerPressd)
                    : null,
                nextTriggerAllowedIn = shootData != null
                    ? SanitizeFloat(Mathf.Max(0f, shootData.nextFingerDownCan - Time.time))
                    : null,
                isAiming = shootController?.IsAiming == true,
                aimingReady = currentAiming?.IsReady == true,
                hardAim = currentAiming?.HardAim == true,
                aimingDistance = currentAiming != null
                    ? SanitizeFloat(currentAiming.LastDist2Target)
                    : null,
                aimPlan = CreateAimPlanSnapshot(currentAiming),
                reloading = weaponManager?.Reload?.Reloading == true,
                weaponReady = weaponManager?.IsWeaponReady == true,
                haveBullets = weaponManager?.HaveBullets == true
            };
        }

        private static object? CreateAimPlanSnapshot(IBotAiming? currentAiming)
        {
            if (currentAiming is BotAimingData standardAim)
            {
                return CreateAimPlanPayload(
                    nameof(BotAimingData),
                    standardAim.Status,
                    standardAim.IsReady,
                    standardAim._curBetterAimTime,
                    standardAim._endAimTime,
                    standardAim.LastAimTime);
            }

            if (currentAiming is UnderbarrelLauncherBotAiming underbarrelAim)
            {
                return CreateAimPlanPayload(
                    nameof(UnderbarrelLauncherBotAiming),
                    underbarrelAim.Status,
                    underbarrelAim.IsReady,
                    underbarrelAim._curBetterAimTime,
                    underbarrelAim._endAimTime,
                    underbarrelAim.LastAimTime);
            }

            return currentAiming != null
                ? new
                {
                    controller = currentAiming.GetType().Name,
                    ready = currentAiming.IsReady
                }
                : null;
        }

        private static object CreateAimPlanPayload(
            string controller,
            AimStatus status,
            bool ready,
            float elapsed,
            float plannedDuration,
            float baseDuration)
        {
            float safeElapsed = Mathf.Max(0f, elapsed);
            float safePlannedDuration = Mathf.Max(0f, plannedDuration);
            return new
            {
                controller,
                status = status.ToString(),
                ready,
                elapsed = SanitizeFloat(safeElapsed),
                plannedDuration = SanitizeFloat(safePlannedDuration),
                baseDuration = SanitizeFloat(Mathf.Max(0f, baseDuration)),
                remaining = SanitizeFloat(Mathf.Max(0f, safePlannedDuration - safeElapsed)),
                progress = safePlannedDuration > 0.0001f
                    ? SanitizeFloat(Mathf.Clamp01(safeElapsed / safePlannedDuration))
                    : null
            };
        }

        private static object CreateCombatMovementGateContext(BotOwner bot, Vector3? target)
        {
            Player? player = bot.GetPlayer ?? bot.AIData?.Player;
            var movementContext = player?.MovementContext;
            Vector3 bodyDirection = player?.Transform != null
                ? NormalizePlanar(player.Transform.forward)
                : bot.Transform != null
                    ? NormalizePlanar(bot.Transform.forward)
                    : Vector3.zero;
            Vector3 targetDirection = target.HasValue
                ? NormalizePlanar(target.Value - bot.Position)
                : Vector3.zero;
            IHealthController? health = player?.HealthController;

            return new
            {
                sprinting = bot.Mover?.Sprinting == true,
                sprintEnabled = movementContext?.IsSprintEnabled,
                canSprintPlayer = bot.CanSprintPlayer,
                moverNoSprint = bot.Mover?.NoSprint == true,
                movementCanSprint = movementContext?.CanSprint,
                movementCanWalk = movementContext?.CanWalk,
                hasActiveMoverPath = bot.Mover?.HasPathAndNoComplete == true,
                stamina = player?.Physical?.Stamina != null
                    ? SanitizeFloat(player.Physical.Stamina.NormalValue)
                    : null,
                bodyDirection = bodyDirection.sqrMagnitude > 0.0001f
                    ? CreateVector(bodyDirection)
                    : null,
                bodyVsTargetAngle = bodyDirection.sqrMagnitude > 0.0001f && targetDirection.sqrMagnitude > 0.0001f
                    ? SanitizeFloat(Vector3.Angle(bodyDirection, targetDirection))
                    : null,
                leftLegBroken = health?.IsBodyPartBroken(EBodyPart.LeftLeg),
                leftLegDestroyed = health?.IsBodyPartDestroyed(EBodyPart.LeftLeg),
                rightLegBroken = health?.IsBodyPartBroken(EBodyPart.RightLeg),
                rightLegDestroyed = health?.IsBodyPartDestroyed(EBodyPart.RightLeg)
            };
        }

        private static object? CreateCoverSnapshot(BotOwner bot)
        {
            CustomNavigationPoint? cover = bot.Memory?.CurCustomCoverPoint;
            if (cover == null)
            {
                return new
                {
                    inCover = bot.Memory?.IsInCover == true,
                    id = (int?)null,
                    position = (object?)null,
                    distance = (float?)null,
                    spotted = (bool?)null,
                    coverType = (string?)null,
                    coverLevel = (string?)null,
                    defenceLevel = (float?)null,
                    dangerCoeff = (int?)null,
                    hideLevel = (int?)null,
                    wallDirection = (object?)null
                };
            }

            CoverPointDefenceInfo? defenceInfo = cover.DefenceInfo;

            return new
            {
                inCover = bot.Memory?.IsInCover == true,
                id = (int?)cover.Id,
                position = (object?)CreateVector(cover.Position),
                distance = SanitizeFloat(Vector3.Distance(bot.Position, cover.Position)),
                spotted = (bool?)cover.IsSpotted,
                coverType = cover.CoverType.ToString(),
                coverLevel = cover.CoverLevel.ToString(),
                defenceLevel = defenceInfo != null ? SanitizeFloat(defenceInfo.DefenceLevel) : null,
                dangerCoeff = defenceInfo != null ? (int?)defenceInfo.DangerCoeff : null,
                hideLevel = (int?)cover.HideLevel,
                wallDirection = cover.ToWallVector.sqrMagnitude > 0.0001f
                    ? (object?)CreateVector(cover.ToWallVector)
                    : null
            };
        }

        private static object CreateTargetCommitmentSnapshot(
            BotOwner bot,
            BotFollowerPlayer? followerData,
            EnemyInfo? goalEnemy)
        {
            string? orderedPushProfileId = null;
            Vector3 orderedPushPosition = Vector3.zero;
            bool hasOrderedPushLock = followerData != null &&
                                      followerData.TryGetOrderedPushTargetLock(
                                          out orderedPushProfileId,
                                          out orderedPushPosition);

            return new
            {
                hasMission = FollowerCombatTargetCommitments.HasMission(bot),
                currentGoalIsMission = goalEnemy != null &&
                                       FollowerCombatTargetCommitments.IsMissionTarget(bot, goalEnemy),
                currentGoalIsTemporary = goalEnemy != null &&
                                         FollowerCombatTargetCommitments.IsActiveTemporaryTarget(bot, goalEnemy),
                hasOrderedPushLock,
                orderedPushProfileId = hasOrderedPushLock ? orderedPushProfileId : null,
                orderedPushPosition = hasOrderedPushLock ? CreateVector(orderedPushPosition) : null
            };
        }

        private static object CreateTransitionContext(BotOwner bot, RecorderFollowerState state)
        {
            EnemyInfo? goalEnemy = bot.Memory?.GoalEnemy;
            return new
            {
                state = CreateRecorderStatePayload(state),
                memory = new
                {
                    haveEnemy = bot.Memory?.HaveEnemy == true,
                    underFire = bot.Memory?.IsUnderFire == true,
                    inCover = bot.Memory?.IsInCover == true,
                    damagedRecently = FollowerCombatCommon.WasHitRecently(bot, 0.5f) ||
                                      FollowerAwareness.WasRecentlyDamaged(bot),
                    threatenedRecently = FollowerAwareness.WasRecentlyThreatened(bot),
                    hitRecently = FollowerCombatCommon.WasHitRecently(bot, 0.5f) ||
                                  FollowerAwareness.WasRecentlyHit(bot)
                },
                enemy = CreateTransitionEnemyContext(bot, goalEnemy),
                boss = CreateTransitionBossContext(bot)
            };
        }

        private static object? CreateTransitionEnemyContext(BotOwner bot, EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null)
            {
                return null;
            }

            FollowerEnemyInfoCorrection.CorrectDistanceOnly(bot, goalEnemy);

            IPlayer? player = goalEnemy.Person;
            Vector3 position = player?.Transform != null
                ? player.Transform.position
                : goalEnemy.EnemyLastPositionReal;
            BotGroupEnemyInfo? groupInfo = TryGetGroupInfo(bot, goalEnemy, player);

            return new
            {
                profileId = goalEnemy.ProfileId,
                nickname = player?.Profile?.Nickname,
                role = player?.Profile?.Info?.Settings?.Role.ToString(),
                position = IsFinite(position) ? CreateVector(position) : null,
                distance = SanitizeFloat(goalEnemy.Distance),
                visibleType = goalEnemy.VisibleType.ToString(),
                isVisible = goalEnemy.IsVisible,
                canShoot = goalEnemy.CanShoot,
                isLookingAtFollower = SainGoalEnemyBridge.IsEnemyLookingAtFollower(bot, goalEnemy),
                reliableShootLane = FollowerImmediateFirePolicy.HasReliableImmediateFireLane(bot, goalEnemy),
                personalSeenTime = SanitizeFloat(goalEnemy.PersonalSeenTime),
                personalLastSeenTime = SanitizeFloat(goalEnemy.PersonalLastSeenTime),
                provenance = CreateEnemyProvenanceContext(groupInfo),
                contact = CreateEnemyContactContext(goalEnemy, groupInfo),
                geometry = CreateEnemyGeometryContext(bot, position)
            };
        }

        private static object CreateGrenadeContext(BotOwner bot, EnemyInfo? goalEnemy)
        {
            BotGrenadeController? grenades = bot.WeaponManager?.Grenades;
            EFT.InventoryLogic.ThrowWeap? selectedGrenade = grenades?.grenade;
            BotRequest? request = bot.BotRequestController?.CurRequest;
            float now = Time.time;

            return new
            {
                enemy = CreateTransitionEnemyContext(bot, goalEnemy ?? bot.Memory?.GoalEnemy),
                pressure = new
                {
                    underFire = bot.Memory?.IsUnderFire == true,
                    hitRecently05 = FollowerCombatCommon.WasHitRecently(bot, 0.5f) ||
                                    FollowerAwareness.WasRecentlyHit(bot),
                    hitRecently2 = FollowerCombatCommon.WasHitRecently(bot, 2f),
                    threatenedRecently = FollowerAwareness.WasRecentlyThreatened(bot)
                },
                position = new
                {
                    inCover = bot.Memory?.IsInCover == true,
                    hasCoverPoint = bot.Memory?.CurCustomCoverPoint != null,
                    hasPath = bot.Mover?.HasPathAndNoComplete == true,
                    sprinting = bot.Mover?.Sprinting == true,
                    bossDistance = CreateTransitionBossContext(bot)
                },
                actionState = new
                {
                    dogFight = bot.DogFight?.DogFightState.ToString(),
                    request = request?.BotRequestType.ToString(),
                    medicineUsing = bot.Medecine?.Using == true,
                    suppressGrenadeActive = bot.SuppressGrenade != null && !bot.SuppressGrenade.Complete
                },
                throwState = new
                {
                    runtimeGateAllowed = FollowerGrenadeRuntimeGate.IsThrowAllowed(bot),
                    controllerPresent = grenades != null,
                    selectedGrenadePresent = selectedGrenade != null,
                    selectedGrenadeType = selectedGrenade?.ThrowType.ToString(),
                    selectedGrenadeExplDelay = selectedGrenade != null ? SanitizeFloat(selectedGrenade.GetExplDelay) : null,
                    selectedGrenadeMinContactExplode = selectedGrenade != null ? SanitizeFloat(selectedGrenade.MinTimeToContactExplode) : null,
                    haveGrenade = grenades?.HaveGrenade,
                    haveFrag = grenades?.HaveGrenadeOfType(ThrowWeapType.frag_grenade),
                    haveStun = grenades?.HaveGrenadeOfType(ThrowWeapType.stun_grenade),
                    haveSmoke = grenades?.HaveGrenadeOfType(ThrowWeapType.smoke_grenade),
                    throwingNow = grenades?.ThrowindNow,
                    readyToThrow = grenades?.ReadyToThrow,
                    firstSeenAge = goalEnemy != null ? SanitizeFloat(now - goalEnemy.FirstTimeSeen) : null
                }
            };
        }

        private static object? CreateTransitionBossContext(BotOwner bot)
        {
            if (bot.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return null;
            }

            Vector3 bossPosition = boss.realPlayer != null
                ? boss.realPlayer.Transform.position
                : boss.Position;
            return new
            {
                distance = SanitizeFloat(Vector3.Distance(bot.Position, bossPosition))
            };
        }

        private static object? CreateWeaponSnapshot(BotOwner bot)
        {
            BotWeaponSelector? selector = bot.WeaponManager?.Selector;
            Weapon? activeWeapon = bot.WeaponManager?.ShootController?.Item;
            Weapon? firstPrimary = bot.GetPlayer?.InventoryController?.Inventory?.Equipment?.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem as Weapon;
            Weapon? secondPrimary = bot.GetPlayer?.InventoryController?.Inventory?.Equipment?.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem as Weapon;

            return new
            {
                currentSlot = selector?.LastEquipmentSlot.ToString(),
                active = CreateWeaponSlotSnapshot(activeWeapon),
                firstPrimary = CreateWeaponSlotSnapshot(firstPrimary),
                secondPrimary = CreateWeaponSlotSnapshot(secondPrimary)
            };
        }

        private static object? CreateLightWeaponSnapshot(BotOwner bot)
        {
            BotWeaponSelector? selector = bot.WeaponManager?.Selector;
            Weapon? activeWeapon = bot.WeaponManager?.ShootController?.Item;

            return new
            {
                currentSlot = selector?.LastEquipmentSlot.ToString(),
                active = activeWeapon != null
                    ? new
                    {
                        type = activeWeapon.GetType().Name,
                        magazineCount = activeWeapon.GetCurrentMagazine()?.Cartridges?.Count,
                        loadedRounds = FollowerCombatCommon.CountLoadedRounds(activeWeapon)
                    }
                    : null
            };
        }

        private static object? CreateLimbStatusSnapshot(BotOwner bot)
        {
            IHealthController? health = bot.GetPlayer?.ActiveHealthController;
            if (health == null)
            {
                return null;
            }

            return new
            {
                head = CreateBodyPartStatus(health, EBodyPart.Head),
                chest = CreateBodyPartStatus(health, EBodyPart.Chest),
                stomach = CreateBodyPartStatus(health, EBodyPart.Stomach),
                leftArm = CreateBodyPartStatus(health, EBodyPart.LeftArm),
                rightArm = CreateBodyPartStatus(health, EBodyPart.RightArm),
                leftLeg = CreateBodyPartStatus(health, EBodyPart.LeftLeg),
                rightLeg = CreateBodyPartStatus(health, EBodyPart.RightLeg)
            };
        }

        private static object CreateBodyPartStatus(IHealthController health, EBodyPart bodyPart)
        {
            ValueStruct value = health.GetBodyPartHealth(bodyPart, false);
            return new
            {
                current = SanitizeFloat(value.Current),
                maximum = SanitizeFloat(value.Maximum),
                normalized = value.Maximum > 0f
                    ? SanitizeFloat(value.Current / value.Maximum)
                    : null,
                broken = health.IsBodyPartBroken(bodyPart),
                destroyed = health.IsBodyPartDestroyed(bodyPart)
            };
        }

        private static object? CreateWeaponSlotSnapshot(Weapon? weapon)
        {
            if (weapon == null)
            {
                return null;
            }

            return new
            {
                id = weapon.Id,
                type = weapon.GetType().Name,
                magazineCount = weapon.GetCurrentMagazine()?.Cartridges?.Count,
                loadedRounds = FollowerCombatCommon.CountLoadedRounds(weapon)
            };
        }

        private static object? CreateEnemySnapshot(
            BotOwner bot,
            EnemyInfo? goalEnemy,
            Vector3 botPosition,
            Vector3 lookDirection,
            Vector3 bodyDirection,
            Vector3 moveTargetDirection,
            Vector3 effectiveMoveTargetDirection)
        {
            if (goalEnemy == null)
            {
                return null;
            }

            FollowerEnemyInfoCorrection.CorrectDistanceOnly(bot, goalEnemy);

            Vector3 position = goalEnemy.Person?.Transform != null
                ? goalEnemy.Person.Transform.position
                : goalEnemy.EnemyLastPositionReal;
            BotGroupEnemyInfo? groupInfo = TryGetGroupInfo(bot, goalEnemy, goalEnemy.Person);

            Vector3 toEnemyDirection = NormalizePlanar(position - botPosition);
            bool hasEnemyDirection = toEnemyDirection.sqrMagnitude > 0.0001f;

            return new
            {
                profileId = goalEnemy.ProfileId,
                role = goalEnemy.Person?.Profile?.Info?.Settings?.Role.ToString(),
                alive = goalEnemy.Person?.HealthController?.IsAlive == true,
                distance = SanitizeFloat(goalEnemy.Distance),
                visibleType = goalEnemy.VisibleType.ToString(),
                isVisible = goalEnemy.IsVisible,
                canShoot = goalEnemy.CanShoot,
                isLookingAtFollower = SainGoalEnemyBridge.IsEnemyLookingAtFollower(bot, goalEnemy),
                reliableShootLane = FollowerImmediateFirePolicy.HasReliableImmediateFireLane(bot, goalEnemy),
                haveSeen = goalEnemy.HaveSeen,
                personalSeenTime = SanitizeFloat(goalEnemy.PersonalSeenTime),
                personalLastSeenTime = SanitizeFloat(goalEnemy.PersonalLastSeenTime),
                lastKnownPosition = CreateVector(goalEnemy.EnemyLastPositionReal),
                position = CreateVector(position),
                provenance = CreateEnemyProvenanceContext(groupInfo),
                contact = CreateEnemyContactContext(goalEnemy, groupInfo),
                geometry = CreateEnemyGeometryContext(bot, position),
                clusterCount17m = TryGetEnemyClusterCount(bot, goalEnemy, position),
                direction = hasEnemyDirection ? CreateVector(toEnemyDirection) : null,
                lookVsEnemyAngle = hasEnemyDirection ? SanitizeFloat(Vector3.Angle(lookDirection, toEnemyDirection)) : null,
                bodyVsEnemyAngle = hasEnemyDirection && bodyDirection.sqrMagnitude > 0.0001f
                    ? SanitizeFloat(Vector3.Angle(bodyDirection, toEnemyDirection))
                    : null,
                moveTargetVsEnemyAngle = moveTargetDirection.sqrMagnitude > 0.0001f && hasEnemyDirection
                    ? SanitizeFloat(Vector3.Angle(moveTargetDirection, toEnemyDirection))
                    : null,
                effectiveMoveTargetVsEnemyAngle = effectiveMoveTargetDirection.sqrMagnitude > 0.0001f && hasEnemyDirection
                    ? SanitizeFloat(Vector3.Angle(effectiveMoveTargetDirection, toEnemyDirection))
                    : null
            };
        }

        private static float? TryGetEnemyClusterCount(BotOwner bot, EnemyInfo goalEnemy, Vector3 enemyPosition)
        {
            try
            {
                return SanitizeFloat(pitTeam.Utils.Enemy.GetEnemiesAtLocation(bot, goalEnemy, enemyPosition));
            }
            catch
            {
                return null;
            }
        }

        private static BotGroupEnemyInfo? TryGetGroupInfo(BotOwner bot, EnemyInfo? enemyInfo, IPlayer? player)
        {
            if (enemyInfo?.GroupInfo != null)
            {
                return enemyInfo.GroupInfo;
            }

            try
            {
                if (player != null &&
                    bot.BotsGroup?.Enemies != null &&
                    bot.BotsGroup.Enemies.TryGetValue(player, out BotGroupEnemyInfo groupInfo))
                {
                    return groupInfo;
                }
            }
            catch
            {
            }

            return null;
        }

        private static object? CreateEnemyProvenanceContext(BotGroupEnemyInfo? groupInfo)
        {
            if (groupInfo == null)
            {
                return null;
            }

            EBotEnemyCause cause = groupInfo.Cause;
            return new
            {
                cause = cause.ToString(),
                causeCategory = ClassifyEnemyCause(cause),
                requiresAwarenessGate = RequiresAcquisitionAwarenessGate(cause)
            };
        }

        private static object? CreateEnemyContactContext(EnemyInfo? enemyInfo, BotGroupEnemyInfo? groupInfo)
        {
            if (enemyInfo == null && groupInfo == null)
            {
                return null;
            }

            return new
            {
                memoryVisible = enemyInfo?.IsVisible == true || enemyInfo?.CanShoot == true,
                haveSeen = enemyInfo?.HaveSeen,
                personalSeenTime = enemyInfo != null ? SanitizeFloat(enemyInfo.PersonalSeenTime) : null,
                personalSeenAge = enemyInfo != null ? CreateAge(enemyInfo.PersonalSeenTime) : null,
                personalLastSeenTime = enemyInfo != null ? SanitizeFloat(enemyInfo.PersonalLastSeenTime) : null,
                personalLastSeenAge = enemyInfo != null ? CreateAge(enemyInfo.PersonalLastSeenTime) : null,
                firstTimeSeen = enemyInfo != null ? SanitizeFloat(enemyInfo.FirstTimeSeen) : null,
                firstSeenAge = enemyInfo != null ? CreateAge(enemyInfo.FirstTimeSeen) : null,
                groupHaveSeen = groupInfo?.IsHaveSeen,
                groupLastSeenTimeSense = groupInfo != null ? SanitizeFloat(groupInfo.EnemyLastSeenTimeSense) : null,
                groupLastSeenTimeSenseAge = groupInfo != null ? CreateAge(groupInfo.EnemyLastSeenTimeSense) : null,
                groupLastSeenTimeReal = groupInfo != null ? SanitizeFloat(groupInfo.EnemyLastSeenTimeReal) : null,
                groupLastSeenTimeRealAge = groupInfo != null ? CreateAge(groupInfo.EnemyLastSeenTimeReal) : null,
                groupLastShootTime = groupInfo != null ? SanitizeFloat(groupInfo.LastShootTime) : null,
                groupLastShootAge = groupInfo != null ? CreateAge(groupInfo.LastShootTime) : null
            };
        }

        private static object? CreateEnemyGeometryContext(BotOwner bot, Vector3 enemyPosition)
        {
            if (!IsFinite(enemyPosition))
            {
                return null;
            }

            Vector3 delta = enemyPosition - bot.Position;
            float planarDistance = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
            return new
            {
                straightDistance = SanitizeFloat(delta.magnitude),
                planarDistance = SanitizeFloat(planarDistance),
                verticalDelta = SanitizeFloat(delta.y)
            };
        }

        private static float? CreateAge(float timestamp)
        {
            if (timestamp <= 0f)
            {
                return null;
            }

            return SanitizeFloat(Time.time - timestamp);
        }

        private static string ClassifyEnemyCause(EBotEnemyCause cause)
        {
            switch (cause)
            {
                case EBotEnemyCause.byKill:
                case EBotEnemyCause.followGetHit:
                case EBotEnemyCause.addPlayer:
                case EBotEnemyCause.callBot:
                case EBotEnemyCause.gifterKill:
                case EBotEnemyCause.bossKillArena:
                case EBotEnemyCause.KillaSyncTagilla:
                case EBotEnemyCause.tagillaFindENemy:
                case EBotEnemyCause.fuckGestus:
                case EBotEnemyCause.pmcBossKill:
                case EBotEnemyCause.christmas:
                case EBotEnemyCause.synWithKilla:
                case EBotEnemyCause.ravangeZryachiy:
                case EBotEnemyCause.partisanBadKarma:
                case EBotEnemyCause.attackBTR:
                case EBotEnemyCause.tagillaAlarm:
                case EBotEnemyCause.MarkOfUnknowsDist:
                case EBotEnemyCause.zryachiyLogic:
                case EBotEnemyCause.pairLogic:
                    return "directAggressive";
                case EBotEnemyCause.initial:
                case EBotEnemyCause.AddNewMember:
                case EBotEnemyCause.addBotAtGroup:
                case EBotEnemyCause.addBotNoGroup:
                case EBotEnemyCause.addPlayerToBoss:
                    return "initialOrSetup";
                case EBotEnemyCause.addCauseGroup:
                case EBotEnemyCause.initCauseEnemy:
                case EBotEnemyCause.checkAddTODO:
                case EBotEnemyCause.AddEnemyToAllGroupsInBotZone:
                case EBotEnemyCause.AddEnemyToAllGroups:
                case EBotEnemyCause.warn:
                    return "softGroupOrMemory";
                case EBotEnemyCause.Unknown:
                    return "unknown";
                default:
                    return "other";
            }
        }

        private static bool RequiresAcquisitionAwarenessGate(EBotEnemyCause cause)
        {
            switch (cause)
            {
                case EBotEnemyCause.byKill:
                case EBotEnemyCause.followGetHit:
                case EBotEnemyCause.addPlayer:
                case EBotEnemyCause.callBot:
                case EBotEnemyCause.gifterKill:
                case EBotEnemyCause.bossKillArena:
                case EBotEnemyCause.KillaSyncTagilla:
                case EBotEnemyCause.tagillaFindENemy:
                case EBotEnemyCause.fuckGestus:
                case EBotEnemyCause.pmcBossKill:
                case EBotEnemyCause.christmas:
                case EBotEnemyCause.synWithKilla:
                case EBotEnemyCause.ravangeZryachiy:
                case EBotEnemyCause.partisanBadKarma:
                case EBotEnemyCause.attackBTR:
                case EBotEnemyCause.tagillaAlarm:
                case EBotEnemyCause.MarkOfUnknowsDist:
                case EBotEnemyCause.zryachiyLogic:
                case EBotEnemyCause.pairLogic:
                    return false;
                default:
                    return true;
            }
        }

        private static object? CreateBossSnapshot(BotOwner bot, Vector3 botPosition, Vector3 lookDirection)
        {
            if (bot.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return null;
            }

            Vector3 bossPosition = boss.realPlayer != null
                ? boss.realPlayer.Transform.position
                : boss.Position;
            Vector3 toBossDirection = NormalizePlanar(bossPosition - botPosition);
            bool hasBossDirection = toBossDirection.sqrMagnitude > 0.0001f;

            return new
            {
                profileId = boss.realPlayer?.ProfileId,
                position = CreateVector(bossPosition),
                distance = SanitizeFloat(Vector3.Distance(bot.Position, bossPosition)),
                lookDirection = boss.realPlayer != null ? CreateVector(NormalizePlanar(boss.realPlayer.LookDirection)) : null,
                direction = hasBossDirection ? CreateVector(toBossDirection) : null,
                lookVsBossAngle = hasBossDirection ? SanitizeFloat(Vector3.Angle(lookDirection, toBossDirection)) : null
            };
        }

        private static object CreateVector(Vector3 value)
        {
            return new
            {
                x = SanitizeFloat(value.x),
                y = SanitizeFloat(value.y),
                z = SanitizeFloat(value.z)
            };
        }

        private static bool TryGetCurrentMoveTarget(BotOwner bot, out Vector3 target)
        {
            target = Vector3.zero;
            if (bot?.GoToSomePointData == null || !bot.GoToSomePointData.HaveTarget())
            {
                return false;
            }

            target = bot.GoToSomePointData.Point;
            return true;
        }

        private static bool TryGetMoverTarget(BotOwner bot, out Vector3 target)
        {
            target = Vector3.zero;
            if (bot?.Mover?.TargetPoint is not Vector3 moverTarget || !IsFinite(moverTarget))
            {
                return false;
            }

            target = moverTarget;
            return true;
        }

        private static RecorderFollowerState GetOrCreateState(BotOwner bot)
        {
            string profileId = bot.ProfileId ?? string.Empty;
            return GetOrCreateState(profileId);
        }

        private static RecorderFollowerState GetOrCreateState(string profileId)
        {
            if (!FollowerStates.TryGetValue(profileId, out RecorderFollowerState? state))
            {
                state = new RecorderFollowerState();
                FollowerStates[profileId] = state;
            }

            return state;
        }

        private static bool IsBotInRecordedCombat(BotOwner bot, RecorderFollowerState state)
        {
            return state.InCombat || FollowerCombatLayer.IsFollowerCombatLayerActive(bot);
        }

        private static bool AnyFollowerInRecordedCombat()
        {
            foreach (RecorderFollowerState state in FollowerStates.Values)
            {
                if (state.InCombat)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanRecordBot(BotOwner? bot)
        {
            return bot != null &&
                   IsRecording() &&
                   !string.IsNullOrEmpty(bot.ProfileId) &&
                   BossPlayers.IsFollower(bot);
        }

        private static object? CreateAimPartDiagnostic(
            EnemyPart? part,
            EnemyPartVision? vision,
            Vector3? enemyRoot)
        {
            if (part == null)
            {
                return null;
            }

            Vector3 position = part.Position;
            return new
            {
                canShoot = part.CanShoot,
                visible = vision?.Visible == true,
                visibleType = vision?.VisibleType.ToString(),
                position = IsFinite(position) ? CreateVector(position) : null,
                heightFromEnemyRoot = enemyRoot.HasValue &&
                                      IsFinite(enemyRoot.Value) &&
                                      IsFinite(position)
                    ? SanitizeFloat(position.y - enemyRoot.Value.y)
                    : null
            };
        }

        private static bool ShouldSkipCommandRecord(BotOwner bot, FollowerCommandType command)
        {
            if (command != FollowerCommandType.MoveToPoint)
            {
                return false;
            }

            return bot.Memory?.HaveEnemy != true && !FollowerCombatLayer.IsFollowerCombatLayerActive(bot);
        }

        public static bool IsRecordingFor(BotOwner? bot, bool requireRecordedCombat = false)
        {
            if (!CanRecordBot(bot))
            {
                return false;
            }

            if (!requireRecordedCombat)
            {
                return true;
            }

            RecorderFollowerState state = GetOrCreateState(bot!);
            return IsBotInRecordedCombat(bot!, state);
        }

        private static bool IsEnabled()
        {
            return pitFireTeam.IsDebugBuild && pitFireTeam.battleRecorderEnabled?.Value == true;
        }

        private static bool IsRecording()
        {
            return IsEnabled() && writer != null && !string.IsNullOrEmpty(currentRaidId);
        }

        private static float GetSnapshotIntervalSeconds()
        {
            return Mathf.Max(0.05f, GetSnapshotIntervalMs() / 1000f);
        }

        private static int GetSnapshotIntervalMs()
        {
            return Math.Max(50, pitFireTeam.battleRecorderSnapshotIntervalMs?.Value ?? 200);
        }

        private static float? SanitizeFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return null;
            }

            return value;
        }

        private static Vector3 NormalizePlanar(Vector3 value)
        {
            value.y = 0f;
            if (value.sqrMagnitude <= 0.0001f)
            {
                return Vector3.zero;
            }

            return value.normalized;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsInfinity(value.z);
        }

        private static void FlushExpiredGoalEnemyTransitionRepeats(
            BotOwner bot,
            RecorderFollowerState state)
        {
            if (state.GoalEnemyTransitionSignature == null ||
                Time.time - state.GoalEnemyTransitionFirstTime < GoalEnemyTransitionCoalesceSeconds)
            {
                return;
            }

            FlushGoalEnemyTransitionRepeats(bot, state);
            ResetGoalEnemyTransitionCoalescing(state);
        }

        private static void FlushAllGoalEnemyTransitionRepeats()
        {
            foreach (RecorderFollowerState state in FollowerStates.Values)
            {
                if (state.GoalEnemyTransitionBot != null)
                {
                    FlushGoalEnemyTransitionRepeats(state.GoalEnemyTransitionBot, state);
                }

                ResetGoalEnemyTransitionCoalescing(state);
            }
        }

        private static void FlushGoalEnemyTransitionRepeats(
            BotOwner bot,
            RecorderFollowerState state)
        {
            GoalEnemyTransitionSignature? signature = state.GoalEnemyTransitionSignature;
            if (signature == null || state.GoalEnemyTransitionRepeatCount <= 0)
            {
                return;
            }

            WriteEventInternal("goalEnemyTransitionRepeat", bot, new
            {
                source = signature.Source,
                reason = signature.Reason,
                allowed = signature.Allowed,
                previous = signature.PreviousProfileId != null
                    ? new
                    {
                        profileId = signature.PreviousProfileId,
                        isVisible = signature.PreviousVisible,
                        canShoot = signature.PreviousCanShoot
                    }
                    : null,
                next = signature.NextProfileId != null
                    ? new
                    {
                        profileId = signature.NextProfileId,
                        isVisible = signature.NextVisible,
                        canShoot = signature.NextCanShoot
                    }
                    : null,
                repeatCount = state.GoalEnemyTransitionRepeatCount,
                totalOccurrences = state.GoalEnemyTransitionRepeatCount + 1,
                firstTime = SanitizeFloat(state.GoalEnemyTransitionFirstTime),
                lastTime = SanitizeFloat(state.GoalEnemyTransitionLastTime),
                duration = SanitizeFloat(Mathf.Max(
                    0f,
                    state.GoalEnemyTransitionLastTime - state.GoalEnemyTransitionFirstTime)),
                context = signature.CreateLightweightContext()
            });
        }

        private static void ResetGoalEnemyTransitionCoalescing(RecorderFollowerState state)
        {
            state.GoalEnemyTransitionSignature = null;
            state.GoalEnemyTransitionFirstTime = 0f;
            state.GoalEnemyTransitionLastTime = 0f;
            state.GoalEnemyTransitionRepeatCount = 0;
            state.GoalEnemyTransitionBot = null;
        }

        private static void WriteEventInternal(string eventType, BotOwner? bot, object payload)
        {
            if (!IsRecording() || writer == null)
            {
                return;
            }

            try
            {
                lock (SyncRoot)
                {
                    if (!IsRecording() || writer == null)
                    {
                        return;
                    }

                    DateTime utcNow = DateTime.UtcNow;
                    var envelope = new
                    {
                        seq = ++eventSequence,
                        time = SanitizeFloat(Time.time),
                        utc = utcNow.ToString("O", CultureInfo.InvariantCulture),
                        raidId = currentRaidId,
                        locationId = currentLocationId,
                        eventType,
                        bot = bot != null ? new
                        {
                            profileId = bot.ProfileId,
                            nickname = bot.Profile?.Nickname,
                            brain = bot.Brain?.BaseBrain?.ShortName()
                        } : null,
                        payload
                    };

                    writer.WriteLine(JsonConvert.SerializeObject(envelope, JsonSettings));
                    eventsSinceFlush++;
                    if (eventsSinceFlush >= FlushEventBatchSize || utcNow.Ticks >= nextFlushUtcTicks)
                    {
                        writer.Flush();
                        eventsSinceFlush = 0;
                        nextFlushUtcTicks = utcNow.Ticks + FlushIntervalTicks;
                    }
                }
            }
            catch (Exception ex)
            {
                StopAfterFatalRecorderFailure("Battle recorder write failure; recording stopped for this raid.", ex);
            }
        }

        private static void DisposeWriter()
        {
            StreamWriter? writerToDispose;
            lock (SyncRoot)
            {
                writerToDispose = writer;
                writer = null;
                eventsSinceFlush = 0;
                nextFlushUtcTicks = 0L;
            }

            if (writerToDispose == null)
            {
                return;
            }

            try
            {
                writerToDispose.Flush();
            }
            catch (Exception ex)
            {
                SafeLogRecorderError("Failed to flush battle recorder output.", ex);
            }

            try
            {
                writerToDispose.Dispose();
            }
            catch (Exception ex)
            {
                SafeLogRecorderError("Failed to dispose battle recorder output.", ex);
            }
        }

        private static void StopAfterFatalRecorderFailure(string message, Exception ex)
        {
            if (!writeErrorLogged)
            {
                writeErrorLogged = true;
                SafeLogRecorderError(message, ex);
            }

            DisposeWriter();
            UnregisterUpdateHub();
        }

        private static void RegisterUpdateHub()
        {
            if (updateHubSubscribed)
            {
                return;
            }

            BotOwnerUpdateHub.RegisterFollower(UpdateHubSubscriptionId, OnBotManualUpdate);
            updateHubSubscribed = true;
        }

        private static void UnregisterUpdateHub()
        {
            if (!updateHubSubscribed)
            {
                return;
            }

            BotOwnerUpdateHub.UnregisterFollower(UpdateHubSubscriptionId);
            updateHubSubscribed = false;
        }

        private static void SafeLogRecorderError(string message, Exception ex)
        {
            try
            {
                pitFireTeam.Log.LogError(message);
                pitFireTeam.Log.LogError(ex);
            }
            catch
            {
            }
        }

        private sealed class RecorderFollowerState
        {
            public bool InCombat;
            public int CombatEpisodeId;
            public float CombatStartedTime;
            public float LastCombatSeenTime;
            public float NextSnapshotTime;
            public bool HasPreviousSnapshot;
            public Vector3 LastSnapshotPosition;
            public float LastSnapshotTime;
            public bool HasPreviousEffectiveMoveTarget;
            public Vector3 LastEffectiveMoveTarget;
            public float LastEffectiveMoveTargetDistance;
            public string? CurrentObjective;
            public string? LastDecisionAction;
            public string? LastDecisionReason;
            public int DecisionInstanceSequence;
            public int CurrentDecisionInstanceId;
            public float CurrentDecisionSelectedTime;
            public int LastEndedDecisionInstanceId;
            public float LastDecisionEndTime;
            public string? LastDecisionEndReason;
            public float NextWeaponActivityProbeTime;
            public bool HasWeaponActivitySample;
            public bool LastWeaponActivityShooting;
            public bool LastWeaponActivityReloading;
            public float LastWeaponActivityTriggerPressedTime;
            public string? LastWeaponActivityWeaponId;
            public int LastWeaponActivityLoadedRounds = -1;
            public float NextWeaponSafetyRecordTime;
            public GoalEnemyTransitionSignature? GoalEnemyTransitionSignature;
            public float GoalEnemyTransitionFirstTime;
            public float GoalEnemyTransitionLastTime;
            public int GoalEnemyTransitionRepeatCount;
            public BotOwner? GoalEnemyTransitionBot;
        }

        private sealed class GoalEnemyTransitionSignature
        {
            public GoalEnemyTransitionSignature(
                BotOwner bot,
                EnemyInfo? previous,
                EnemyInfo? next,
                string source,
                string reason,
                bool allowed,
                RecorderFollowerState state)
            {
                Source = source;
                Reason = reason;
                Allowed = allowed;
                PreviousProfileId = previous?.ProfileId;
                PreviousVisible = previous?.IsVisible == true;
                PreviousCanShoot = previous?.CanShoot == true;
                NextProfileId = next?.ProfileId;
                NextVisible = next?.IsVisible == true;
                NextCanShoot = next?.CanShoot == true;
                CurrentGoalProfileId = bot.Memory?.GoalEnemy?.ProfileId;
                HaveEnemy = bot.Memory?.HaveEnemy == true;
                UnderFire = bot.Memory?.IsUnderFire == true;
                InCover = bot.Memory?.IsInCover == true;
                InCombat = state.InCombat;
                CombatEpisodeId = state.CombatEpisodeId;
                CurrentObjective = state.CurrentObjective;
                LastDecisionAction = state.LastDecisionAction;
                LastDecisionReason = state.LastDecisionReason;
            }

            public string Source { get; }
            public string Reason { get; }
            public bool Allowed { get; }
            public string? PreviousProfileId { get; }
            public bool PreviousVisible { get; }
            public bool PreviousCanShoot { get; }
            public string? NextProfileId { get; }
            public bool NextVisible { get; }
            public bool NextCanShoot { get; }
            private string? CurrentGoalProfileId { get; }
            private bool HaveEnemy { get; }
            private bool UnderFire { get; }
            private bool InCover { get; }
            private bool InCombat { get; }
            private int CombatEpisodeId { get; }
            private string? CurrentObjective { get; }
            private string? LastDecisionAction { get; }
            private string? LastDecisionReason { get; }

            public bool Matches(
                BotOwner bot,
                EnemyInfo? previous,
                EnemyInfo? next,
                string source,
                string reason,
                bool allowed,
                RecorderFollowerState state)
            {
                return Allowed == allowed &&
                       string.Equals(Source, source, StringComparison.Ordinal) &&
                       string.Equals(Reason, reason, StringComparison.Ordinal) &&
                       string.Equals(PreviousProfileId, previous?.ProfileId, StringComparison.Ordinal) &&
                       PreviousVisible == (previous?.IsVisible == true) &&
                       PreviousCanShoot == (previous?.CanShoot == true) &&
                       string.Equals(NextProfileId, next?.ProfileId, StringComparison.Ordinal) &&
                       NextVisible == (next?.IsVisible == true) &&
                       NextCanShoot == (next?.CanShoot == true) &&
                       string.Equals(CurrentGoalProfileId, bot.Memory?.GoalEnemy?.ProfileId, StringComparison.Ordinal) &&
                       HaveEnemy == (bot.Memory?.HaveEnemy == true) &&
                       UnderFire == (bot.Memory?.IsUnderFire == true) &&
                       InCover == (bot.Memory?.IsInCover == true) &&
                       InCombat == state.InCombat &&
                       CombatEpisodeId == state.CombatEpisodeId &&
                       string.Equals(CurrentObjective, state.CurrentObjective, StringComparison.Ordinal) &&
                       string.Equals(LastDecisionAction, state.LastDecisionAction, StringComparison.Ordinal) &&
                       string.Equals(LastDecisionReason, state.LastDecisionReason, StringComparison.Ordinal);
            }

            public object CreateLightweightContext()
            {
                return new
                {
                    state = new
                    {
                        inCombat = InCombat,
                        combatEpisodeId = CombatEpisodeId,
                        currentObjective = CurrentObjective,
                        lastDecisionAction = LastDecisionAction,
                        lastDecisionReason = LastDecisionReason
                    },
                    memory = new
                    {
                        haveEnemy = HaveEnemy,
                        underFire = UnderFire,
                        inCover = InCover,
                        currentGoalProfileId = CurrentGoalProfileId
                    }
                };
            }
        }

    }
}
