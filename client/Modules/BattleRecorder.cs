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
using System.Reflection;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static class BattleRecorder
    {
        private const string UpdateHubSubscriptionId = "pitTeam.BattleRecorder";
        private const float SainOpponentRetentionSeconds = 5f;
        private const float SainOpponentDecisionProbeSeconds = 0.1f;
        private const float SainOpponentDiscoveryProbeSeconds = 1f;
        private const float FollowerWeaponActivityProbeSeconds = 0.1f;
        private const int FlushEventBatchSize = 64;
        private static readonly long FlushIntervalTicks = TimeSpan.FromSeconds(1).Ticks;
        private const BindingFlags SainMemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly object SyncRoot = new object();
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.None
        };

        private static readonly Dictionary<string, RecorderFollowerState> FollowerStates =
            new Dictionary<string, RecorderFollowerState>(StringComparer.Ordinal);
        private static readonly Dictionary<string, RecorderSainOpponentState> SainOpponentStates =
            new Dictionary<string, RecorderSainOpponentState>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, Dictionary<string, MemberInfo?>> SainMemberCache =
            new Dictionary<Type, Dictionary<string, MemberInfo?>>();

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
        private static bool sainAccessorResolved;
        private static bool sainAccessorFailureRecorded;
        private static Type? sainEnableType;
        private static MethodInfo? getSainByBotOwnerMethod;
        private static MethodInfo? getSainByProfileMethod;

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
                SainOpponentStates.Clear();
                sainAccessorFailureRecorded = false;

                WriteEventInternal("raidStart", null, new
                {
                    raidId = currentRaidId,
                    locationId = currentLocationId,
                    file = currentFilePath,
                    schemaVersion = 5,
                    snapshotIntervalMs = GetSnapshotIntervalMs(),
                    followerWeaponActivityProbeMs = Mathf.RoundToInt(FollowerWeaponActivityProbeSeconds * 1000f),
                    sainOpponentDecisionProbeMs = Mathf.RoundToInt(SainOpponentDecisionProbeSeconds * 1000f),
                    sainOpponentDiscoveryProbeMs = Mathf.RoundToInt(SainOpponentDiscoveryProbeSeconds * 1000f),
                    sainOpponentRetentionMs = Mathf.RoundToInt(SainOpponentRetentionSeconds * 1000f)
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
                SainOpponentStates.Clear();
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
            AICoreActionResultStruct<BotLogicDecision, GClass26>? previousDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision,
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
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision,
            AICoreActionEndStruct endResult,
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
            WriteEventInternal("goalEnemyTransition", bot, new
            {
                source,
                reason,
                allowed,
                previous = CreateTransitionEnemyContext(bot, previous),
                next = CreateTransitionEnemyContext(bot, next),
                context = CreateTransitionContext(bot, state)
            });
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
            AICoreActionResultStruct<BotLogicDecision, GClass26>? decision = null,
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
                    TryRecordSainOpponent(owner);
                    return;
                }

                RecorderFollowerState state = GetOrCreateState(owner);
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

        private static void TryRecordSainOpponent(BotOwner owner)
        {
            if (!pitFireTeam.IsSAINInstalled || owner.GetPlayer?.HealthController?.IsAlive != true)
            {
                return;
            }

            RecorderSainOpponentState state = GetOrCreateSainOpponentState(owner.ProfileId);
            if (Time.time < state.NextProbeTime)
            {
                return;
            }

            state.NextProbeTime = Time.time + Mathf.Min(
                SainOpponentDecisionProbeSeconds,
                GetSnapshotIntervalSeconds());

            string? eftTargetProfileId = owner.Memory?.GoalEnemy?.ProfileId;
            bool eftTargetsTeam = IsTeamProfileId(eftTargetProfileId);
            bool followerTargetsOpponent = TryFindFollowerTargetingOpponent(owner.ProfileId, out string? followerProfileId);
            bool discoveryProbeDue = Time.time >= state.NextDiscoveryProbeTime;
            if (discoveryProbeDue)
            {
                state.NextDiscoveryProbeTime = Time.time + SainOpponentDiscoveryProbeSeconds;
            }

            object? sainBot = null;
            if (eftTargetsTeam ||
                followerTargetsOpponent ||
                state.RelevantUntil > Time.time ||
                discoveryProbeDue)
            {
                sainBot = TryGetSainBot(owner);
            }

            if (sainBot == null)
            {
                return;
            }

            object? sainEnemy = ReadSainMember(sainBot, "GoalEnemy");
            string? sainTargetProfileId = ReadSainString(sainEnemy, "EnemyProfileId");
            bool sainTargetsTeam = IsTeamProfileId(sainTargetProfileId);
            bool directlyRelevant = eftTargetsTeam || sainTargetsTeam || followerTargetsOpponent;
            if (directlyRelevant)
            {
                state.RelevantUntil = Time.time + SainOpponentRetentionSeconds;
            }
            else if (state.RelevantUntil <= Time.time)
            {
                state.HasDecisionSignature = false;
                state.HasPreviousSnapshot = false;
                return;
            }

            string relationReason = CreateSainOpponentRelationReason(
                eftTargetsTeam,
                sainTargetsTeam,
                followerTargetsOpponent,
                directlyRelevant);
            SainOpponentDecisionSample decision = CreateSainOpponentDecisionSample(sainBot, sainTargetProfileId);

            if (!state.HasDecisionSignature || !string.Equals(state.LastDecisionSignature, decision.Signature, StringComparison.Ordinal))
            {
                WriteEventInternal("sainOpponentDecision", owner, new
                {
                    relation = CreateSainOpponentRelationPayload(
                        relationReason,
                        eftTargetProfileId,
                        sainTargetProfileId,
                        followerProfileId,
                        state),
                    previous = state.HasDecisionSignature
                        ? new
                        {
                            layer = state.LastLayer,
                            action = state.LastAction,
                            combatDecision = state.LastCombatDecision,
                            squadDecision = state.LastSquadDecision,
                            selfDecision = state.LastSelfDecision,
                            targetProfileId = state.LastTargetProfileId
                        }
                        : null,
                    current = CreateSainDecisionPayload(sainBot, decision)
                });
            }

            state.HasDecisionSignature = true;
            state.LastDecisionSignature = decision.Signature;
            state.LastLayer = decision.Layer;
            state.LastAction = decision.Action;
            state.LastCombatDecision = decision.CombatDecision;
            state.LastSquadDecision = decision.SquadDecision;
            state.LastSelfDecision = decision.SelfDecision;
            state.LastTargetProfileId = sainTargetProfileId;

            if (Time.time >= state.NextSnapshotTime)
            {
                state.NextSnapshotTime = Time.time + GetSnapshotIntervalSeconds();
                WriteEventInternal(
                    "sainOpponentSnapshot",
                    owner,
                    CreateSainOpponentSnapshot(
                        owner,
                        sainBot,
                        sainEnemy,
                        decision,
                        state,
                        relationReason,
                        eftTargetProfileId,
                        sainTargetProfileId,
                        followerProfileId));
            }
        }

        private static object CreateSainOpponentSnapshot(
            BotOwner owner,
            object sainBot,
            object? sainEnemy,
            SainOpponentDecisionSample decision,
            RecorderSainOpponentState state,
            string relationReason,
            string? eftTargetProfileId,
            string? sainTargetProfileId,
            string? followerProfileId)
        {
            Vector3 position = owner.Position;
            float snapshotElapsed = 0f;
            float distanceMoved = 0f;
            if (state.HasPreviousSnapshot)
            {
                snapshotElapsed = Mathf.Max(0f, Time.time - state.LastSnapshotTime);
                distanceMoved = Vector3.Distance(position, state.LastSnapshotPosition);
            }

            object? mover = ReadSainMember(sainBot, "Mover");
            object? activePath = ReadSainMember(mover, "ActivePath");
            object? cover = ReadSainMember(sainBot, "Cover");
            object? coverInUse = ReadSainMember(cover, "CoverInUse");
            object? coverMovingTo = ReadSainMember(cover, "CoverPoint_MovingTo");
            object? manualShoot = ReadSainMember(sainBot, "ManualShoot");
            object? shoot = ReadSainMember(sainBot, "Shoot");
            object? aim = ReadSainMember(sainBot, "Aim");
            object? suppression = ReadSainMember(sainBot, "Suppression");
            object? info = ReadSainMember(sainBot, "Info");
            object? lastShotEnemy = ReadSainMember(shoot, "LastShotEnemy");
            object? lastSuppressByEnemy = ReadSainMember(suppression, "LastSuppressByEnemy");
            Player? player = owner.GetPlayer ?? owner.AIData?.Player;
            Vector3 lookDirection = NormalizePlanar(owner.LookDirection);
            Vector3 bodyDirection = player?.Transform != null
                ? NormalizePlanar(player.Transform.forward)
                : lookDirection;

            object payload = new
            {
                relation = CreateSainOpponentRelationPayload(
                    relationReason,
                    eftTargetProfileId,
                    sainTargetProfileId,
                    followerProfileId,
                    state),
                sain = new
                {
                    active = ReadSainBool(sainBot, "BotActive"),
                    inCombat = ReadSainBool(sainBot, "IsInCombat"),
                    inStandBy = ReadSainBool(sainBot, "BotInStandBy"),
                    layersActive = ReadSainBool(sainBot, "SAINLayersActive"),
                    layer = decision.Layer,
                    action = new
                    {
                        name = decision.Action,
                        type = ReadSainMember(sainBot, "CurrentAction")?.GetType().Name
                    },
                    decision = CreateSainDecisionPayload(sainBot, decision),
                    personality = new
                    {
                        name = ReadSainString(info, "Personality"),
                        aggressionMultiplier = ReadSainFloat(info, "AggressionMultiplier"),
                        timeBeforeSearch = ReadSainFloat(info, "TimeBeforeSearch"),
                        holdGroundDelay = ReadSainFloat(info, "HoldGroundDelay"),
                        forgetEnemyTime = ReadSainFloat(info, "ForgetEnemyTime")
                    }
                },
                position = CreateVector(position),
                lookDirection = CreateVector(lookDirection),
                movement = new
                {
                    moving = ReadSainBool(mover, "Moving"),
                    running = ReadSainBool(mover, "Running"),
                    crawling = ReadSainBool(mover, "Crawling"),
                    eftSprinting = owner.Mover?.Sprinting == true,
                    poseLevel = player?.MovementContext != null
                        ? SanitizeFloat(player.MovementContext.PoseLevel)
                        : null,
                    bodyDirection = bodyDirection.sqrMagnitude > 0.0001f
                        ? CreateVector(bodyDirection)
                        : null,
                    snapshotElapsed = state.HasPreviousSnapshot ? SanitizeFloat(snapshotElapsed) : null,
                    distanceMovedSinceSnapshot = state.HasPreviousSnapshot ? SanitizeFloat(distanceMoved) : null,
                    speedMetersPerSecond = state.HasPreviousSnapshot && snapshotElapsed > 0.001f
                        ? SanitizeFloat(distanceMoved / snapshotElapsed)
                        : null,
                    path = activePath != null
                        ? new
                        {
                            status = ReadSainString(activePath, "Status"),
                            destination = ReadSainVectorPayload(activePath, "Destination"),
                            pathLength = ReadSainFloat(activePath, "PathLength"),
                            pathStatus = ReadSainString(activePath, "PathStatus"),
                            currentIndex = ReadSainInt(activePath, "CurrentIndex"),
                            onLastCorner = ReadSainBool(activePath, "OnLastCorner"),
                            destinationReachDistance = ReadSainFloat(activePath, "DestinationReachDistance"),
                            wantToSprint = ReadSainBool(activePath, "WantToSprint"),
                            sprintStatus = ReadSainString(activePath, "CurrentSprintStatus"),
                            sprintReason = ReadSainString(activePath, "SprintReason")
                        }
                        : null
                },
                contact = CreateSainEnemyPayload(sainEnemy),
                cover = new
                {
                    seekingState = ReadSainString(cover, "CoverSeekingState"),
                    finderState = ReadSainString(cover, "CurrentCoverFinderState"),
                    sprintingToCover = ReadSainBool(cover, "SprintingToCover"),
                    spottedInCover = ReadSainBool(cover, "SpottedInCover"),
                    inUse = CreateSainCoverPointPayload(owner, coverInUse),
                    movingTo = CreateSainCoverPointPayload(owner, coverMovingTo)
                },
                fire = new
                {
                    eft = CreateCombatActivitySnapshot(owner),
                    manual = new
                    {
                        shooting = ReadSainBool(manualShoot, "Shooting"),
                        reason = ReadSainString(manualShoot, "Reason"),
                        shootPosition = ReadSainVectorPayload(manualShoot, "ShootPosition")
                    },
                    aim = new
                    {
                        canAim = ReadSainBool(aim, "CanAim"),
                        status = ReadSainString(aim, "AimStatus"),
                        lastAimAge = CreateReflectedAge(aim, "LastAimTime")
                    },
                    lastShotEnemyProfileId = ReadSainString(lastShotEnemy, "EnemyProfileId"),
                    friendlyFireClear = ReadSainBool(ReadSainMember(sainBot, "FriendlyFire"), "ClearShot")
                },
                suppression = new
                {
                    state = ReadSainString(suppression, "CurrentState"),
                    amount = ReadSainFloat(suppression, "SuppressionNumber"),
                    suppressed = ReadSainBool(suppression, "IsSuppressed"),
                    heavySuppressed = ReadSainBool(suppression, "IsHeavySuppressed"),
                    suppressingTarget = ReadSainBool(suppression, "SuppressingTarget"),
                    lastSuppressByProfileId = ReadSainString(lastSuppressByEnemy, "EnemyProfileId")
                },
                memory = new
                {
                    haveEnemy = owner.Memory?.HaveEnemy == true,
                    underFire = owner.Memory?.IsUnderFire == true,
                    inCover = owner.Memory?.IsInCover == true
                },
                medical = new
                {
                    firstAidPending = owner.Medecine?.FirstAid?.Have2Do == true,
                    firstAidUsing = owner.Medecine?.FirstAid?.Using == true,
                    surgeryPending = owner.Medecine?.SurgicalKit?.HaveWork == true,
                    surgeryUsing = owner.Medecine?.SurgicalKit?.Using == true,
                    healthStatus = owner.GetPlayer?.HealthStatus.ToString()
                },
                health = CreateLimbStatusSnapshot(owner),
                weapon = CreateLightWeaponSnapshot(owner)
            };

            state.LastSnapshotPosition = position;
            state.LastSnapshotTime = Time.time;
            state.HasPreviousSnapshot = true;
            return payload;
        }

        private static object CreateSainDecisionPayload(object sainBot, SainOpponentDecisionSample decision)
        {
            object? decisionObject = ReadSainMember(sainBot, "Decision");
            return new
            {
                hasDecision = ReadSainBool(decisionObject, "HasDecision"),
                combat = decision.CombatDecision,
                previousCombat = ReadSainString(decisionObject, "PreviousCombatDecision"),
                squad = decision.SquadDecision,
                previousSquad = ReadSainString(decisionObject, "PreviousSquadDecision"),
                self = decision.SelfDecision,
                previousSelf = ReadSainString(decisionObject, "PreviousSelfDecision"),
                changeTime = ReadSainFloat(decisionObject, "ChangeDecisionTime"),
                age = ReadSainFloat(decisionObject, "TimeSinceChangeDecision")
            };
        }

        private static SainOpponentDecisionSample CreateSainOpponentDecisionSample(
            object sainBot,
            string? targetProfileId)
        {
            object? action = ReadSainMember(sainBot, "CurrentAction");
            object? decision = ReadSainMember(sainBot, "Decision");
            string? layer = ReadSainString(sainBot, "ActiveLayer");
            string? actionName = ReadSainString(action, "Name") ?? action?.GetType().Name;
            string? combatDecision = ReadSainString(decision, "CurrentCombatDecision");
            string? squadDecision = ReadSainString(decision, "CurrentSquadDecision");
            string? selfDecision = ReadSainString(decision, "CurrentSelfDecision");
            string signature = string.Join(
                "|",
                layer ?? string.Empty,
                actionName ?? string.Empty,
                combatDecision ?? string.Empty,
                squadDecision ?? string.Empty,
                selfDecision ?? string.Empty,
                targetProfileId ?? string.Empty);

            return new SainOpponentDecisionSample(
                signature,
                layer,
                actionName,
                combatDecision,
                squadDecision,
                selfDecision);
        }

        private static object? CreateSainEnemyPayload(object? enemy)
        {
            if (enemy == null)
            {
                return null;
            }

            return new
            {
                profileId = ReadSainString(enemy, "EnemyProfileId"),
                name = ReadSainString(enemy, "EnemyName"),
                current = ReadSainBool(enemy, "IsCurrentEnemy"),
                known = ReadSainBool(enemy, "EnemyKnown"),
                distance = ReadSainFloat(enemy, "RealDistance"),
                seen = ReadSainBool(enemy, "Seen"),
                visible = ReadSainBool(enemy, "IsVisible"),
                canShoot = ReadSainBool(enemy, "CanShoot"),
                lineOfSight = ReadSainBool(enemy, "InLineOfSight"),
                heard = ReadSainBool(enemy, "Heard"),
                enemyLookingAtMe = ReadSainBool(enemy, "EnemyLookingAtMe"),
                timeSinceSeen = ReadSainFloat(enemy, "TimeSinceSeen"),
                timeSinceHeard = ReadSainFloat(enemy, "TimeSinceHeard"),
                position = ReadSainVectorPayload(enemy, "EnemyPosition"),
                lastKnownPosition = ReadSainVectorPayload(enemy, "LastKnownPosition")
            };
        }

        private static object? CreateSainCoverPointPayload(BotOwner owner, object? coverPoint)
        {
            if (coverPoint == null)
            {
                return null;
            }

            object? pointData = ReadSainMember(coverPoint, "CoverPoint");
            object? coverData = ReadSainMember(coverPoint, "CoverData");
            object? hitCounts = ReadSainMember(coverPoint, "_hitsInCover");
            Vector3? position = ReadSainVector(coverPoint, "Position");
            return new
            {
                id = ReadSainInt(coverPoint, "Id") ?? ReadSainInt(pointData, "Id"),
                position = position.HasValue ? CreateVector(position.Value) : null,
                distance = position.HasValue
                    ? SanitizeFloat(Vector3.Distance(owner.Position, position.Value))
                    : ReadSainFloat(coverPoint, "DistanceToBot") ?? ReadSainFloat(coverData, "BotDistance"),
                spotted = ReadSainBool(hitCounts, "Spotted"),
                bad = ReadSainBool(coverData, "IsBad"),
                straightDistanceStatus = ReadSainString(coverData, "StraightLengthStatus"),
                pathDistanceStatus = ReadSainString(coverData, "PathLengthStatus")
            };
        }

        private static object CreateSainOpponentRelationPayload(
            string reason,
            string? eftTargetProfileId,
            string? sainTargetProfileId,
            string? followerProfileId,
            RecorderSainOpponentState state)
        {
            return new
            {
                reason,
                eftTargetProfileId,
                sainTargetProfileId,
                followerTargetingProfileId = followerProfileId,
                retainedFor = SanitizeFloat(Mathf.Max(0f, state.RelevantUntil - Time.time))
            };
        }

        private static string CreateSainOpponentRelationReason(
            bool eftTargetsTeam,
            bool sainTargetsTeam,
            bool followerTargetsOpponent,
            bool directlyRelevant)
        {
            var reasons = new List<string>(3);
            if (eftTargetsTeam)
            {
                reasons.Add("eftTargetsTeam");
            }

            if (sainTargetsTeam)
            {
                reasons.Add("sainTargetsTeam");
            }

            if (followerTargetsOpponent)
            {
                reasons.Add("followerTargetsOpponent");
            }

            return directlyRelevant ? string.Join("+", reasons) : "retained";
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

        private static object CreateDecisionPayload(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
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
                    ? SanitizeFloat(Mathf.Max(0f, shootData.NextFingerDownCan - Time.time))
                    : null,
                isAiming = shootController?.IsAiming == true,
                aimingReady = currentAiming?.IsReady == true,
                hardAim = currentAiming?.HardAim == true,
                aimingDistance = currentAiming != null
                    ? SanitizeFloat(currentAiming.LastDist2Target)
                    : null,
                reloading = weaponManager?.Reload?.Reloading == true,
                weaponReady = weaponManager?.IsWeaponReady == true,
                haveBullets = weaponManager?.HaveBullets == true
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
            BotSettingsClass? groupInfo = TryGetGroupInfo(bot, goalEnemy, player);

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
                isLookingAtFollower = bot.IsEnemyLookingAtMe(goalEnemy),
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
            ThrowWeapItemClass? selectedGrenade = grenades?.Grenade;
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
            BotSettingsClass? groupInfo = TryGetGroupInfo(bot, goalEnemy, goalEnemy.Person);

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
                isLookingAtFollower = bot.IsEnemyLookingAtMe(goalEnemy),
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

        private static BotSettingsClass? TryGetGroupInfo(BotOwner bot, EnemyInfo? enemyInfo, IPlayer? player)
        {
            if (enemyInfo?.GroupInfo != null)
            {
                return enemyInfo.GroupInfo;
            }

            try
            {
                if (player != null &&
                    bot.BotsGroup?.Enemies != null &&
                    bot.BotsGroup.Enemies.TryGetValue(player, out BotSettingsClass groupInfo))
                {
                    return groupInfo;
                }
            }
            catch
            {
            }

            return null;
        }

        private static object? CreateEnemyProvenanceContext(BotSettingsClass? groupInfo)
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

        private static object? CreateEnemyContactContext(EnemyInfo? enemyInfo, BotSettingsClass? groupInfo)
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

        private static RecorderSainOpponentState GetOrCreateSainOpponentState(string profileId)
        {
            if (!SainOpponentStates.TryGetValue(profileId, out RecorderSainOpponentState? state))
            {
                state = new RecorderSainOpponentState();
                SainOpponentStates[profileId] = state;
            }

            return state;
        }

        private static bool TryFindFollowerTargetingOpponent(
            string opponentProfileId,
            out string? followerProfileId)
        {
            followerProfileId = null;
            foreach (BotFollowerPlayer follower in BossPlayers.GetFollowers())
            {
                BotOwner? followerBot = follower?.GetBot();
                if (followerBot == null || string.IsNullOrEmpty(followerBot.ProfileId))
                {
                    continue;
                }

                if (!FollowerStates.TryGetValue(followerBot.ProfileId, out RecorderFollowerState? followerState) ||
                    !IsBotInRecordedCombat(followerBot, followerState))
                {
                    continue;
                }

                if (string.Equals(
                        followerBot.Memory?.GoalEnemy?.ProfileId,
                        opponentProfileId,
                        StringComparison.Ordinal))
                {
                    followerProfileId = followerBot.ProfileId;
                    return true;
                }
            }

            return false;
        }

        private static bool IsTeamProfileId(string? profileId)
        {
            return !string.IsNullOrEmpty(profileId) &&
                   (BossPlayers.IsPlayerBoss(profileId) || BossPlayers.IsFollowerProfileId(profileId));
        }

        private static object? TryGetSainBot(BotOwner owner)
        {
            try
            {
                ResolveSainAccessor();
                if (getSainByBotOwnerMethod != null)
                {
                    return getSainByBotOwnerMethod.Invoke(null, new object[] { owner });
                }

                if (getSainByProfileMethod != null && !string.IsNullOrEmpty(owner.ProfileId))
                {
                    object?[] arguments = { owner.ProfileId, null };
                    bool found = getSainByProfileMethod.Invoke(null, arguments) is bool result && result;
                    return found ? arguments[1] : null;
                }
            }
            catch (Exception ex)
            {
                RecordSainAccessorFailure("invokeFailed", ex);
            }

            return null;
        }

        private static void ResolveSainAccessor()
        {
            if (sainAccessorResolved)
            {
                return;
            }

            sainAccessorResolved = true;
            sainEnableType = FindLoadedType("SAIN.SAINEnableClass") ??
                             FindLoadedType("SAIN.Plugin.SAINEnableClass");
            if (sainEnableType == null)
            {
                RecordSainAccessorFailure("typeNotFound", null);
                return;
            }

            foreach (MethodInfo method in sainEnableType.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "GetSAIN", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(BotOwner))
                {
                    getSainByBotOwnerMethod = method;
                }
                else if (parameters.Length == 2 &&
                         parameters[0].ParameterType == typeof(string) &&
                         parameters[1].IsOut)
                {
                    getSainByProfileMethod = method;
                }
            }

            if (getSainByBotOwnerMethod == null && getSainByProfileMethod == null)
            {
                RecordSainAccessorFailure("methodNotFound", null);
            }
        }

        private static Type? FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type? type = assembly.GetType(fullName, false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static void RecordSainAccessorFailure(string reason, Exception? exception)
        {
            if (sainAccessorFailureRecorded)
            {
                return;
            }

            sainAccessorFailureRecorded = true;
            WriteEventInternal("sainOpponentRecorderDiagnostic", null, new
            {
                reason,
                exception = exception?.GetBaseException().Message
            });
        }

        private static object? ReadSainMember(object? instance, string memberName)
        {
            if (instance == null)
            {
                return null;
            }

            try
            {
                Type type = instance.GetType();
                if (!SainMemberCache.TryGetValue(type, out Dictionary<string, MemberInfo?>? members))
                {
                    members = new Dictionary<string, MemberInfo?>(StringComparer.Ordinal);
                    SainMemberCache[type] = members;
                }

                if (!members.TryGetValue(memberName, out MemberInfo? member))
                {
                    member = type.GetProperty(memberName, SainMemberFlags) ??
                             (MemberInfo?)type.GetField(memberName, SainMemberFlags);
                    members[memberName] = member;
                }

                return member switch
                {
                    PropertyInfo property => property.GetValue(instance),
                    FieldInfo field => field.GetValue(instance),
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        private static string? ReadSainString(object? instance, string memberName)
        {
            return ReadSainMember(instance, memberName)?.ToString();
        }

        private static bool? ReadSainBool(object? instance, string memberName)
        {
            object? value = ReadSainMember(instance, memberName);
            return value is bool boolValue ? boolValue : null;
        }

        private static float? ReadSainFloat(object? instance, string memberName)
        {
            object? value = ReadSainMember(instance, memberName);
            if (value == null)
            {
                return null;
            }

            try
            {
                return SanitizeFloat(Convert.ToSingle(value, CultureInfo.InvariantCulture));
            }
            catch
            {
                return null;
            }
        }

        private static int? ReadSainInt(object? instance, string memberName)
        {
            object? value = ReadSainMember(instance, memberName);
            if (value == null)
            {
                return null;
            }

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static Vector3? ReadSainVector(object? instance, string memberName)
        {
            object? value = ReadSainMember(instance, memberName);
            return value is Vector3 vector && IsFinite(vector) ? vector : null;
        }

        private static object? ReadSainVectorPayload(object? instance, string memberName)
        {
            Vector3? value = ReadSainVector(instance, memberName);
            return value.HasValue ? CreateVector(value.Value) : null;
        }

        private static float? CreateReflectedAge(object? instance, string memberName)
        {
            float? timestamp = ReadSainFloat(instance, memberName);
            return timestamp.HasValue && timestamp.Value > 0f
                ? SanitizeFloat(Mathf.Max(0f, Time.time - timestamp.Value))
                : null;
        }

        private static bool CanRecordBot(BotOwner? bot)
        {
            return bot != null &&
                   IsRecording() &&
                   !string.IsNullOrEmpty(bot.ProfileId) &&
                   BossPlayers.IsFollower(bot);
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

            BotOwnerUpdateHub.Register(UpdateHubSubscriptionId, OnBotManualUpdate);
            updateHubSubscribed = true;
        }

        private static void UnregisterUpdateHub()
        {
            if (!updateHubSubscribed)
            {
                return;
            }

            BotOwnerUpdateHub.Unregister(UpdateHubSubscriptionId);
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
        }

        private sealed class RecorderSainOpponentState
        {
            public float NextProbeTime;
            public float NextSnapshotTime;
            public float NextDiscoveryProbeTime;
            public float RelevantUntil;
            public bool HasPreviousSnapshot;
            public Vector3 LastSnapshotPosition;
            public float LastSnapshotTime;
            public bool HasDecisionSignature;
            public string? LastDecisionSignature;
            public string? LastLayer;
            public string? LastAction;
            public string? LastCombatDecision;
            public string? LastSquadDecision;
            public string? LastSelfDecision;
            public string? LastTargetProfileId;
        }

        private readonly struct SainOpponentDecisionSample
        {
            public SainOpponentDecisionSample(
                string signature,
                string? layer,
                string? action,
                string? combatDecision,
                string? squadDecision,
                string? selfDecision)
            {
                Signature = signature;
                Layer = layer;
                Action = action;
                CombatDecision = combatDecision;
                SquadDecision = squadDecision;
                SelfDecision = selfDecision;
            }

            public string Signature { get; }
            public string? Layer { get; }
            public string? Action { get; }
            public string? CombatDecision { get; }
            public string? SquadDecision { get; }
            public string? SelfDecision { get; }
        }
    }
}
