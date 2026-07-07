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
        private static bool initialized;
        private static bool updateHubSubscribed;
        private static bool writeErrorLogged;

        public static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
        }

        public static void Shutdown()
        {
            if (!initialized)
            {
                return;
            }

            EndRaid("pluginShutdown");
            UnregisterUpdateHub();
            initialized = false;
        }

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
                    AutoFlush = true
                };

                eventSequence = 0;
                writeErrorLogged = false;
                FollowerStates.Clear();

                WriteEventInternal("raidStart", null, new
                {
                    raidId = currentRaidId,
                    locationId = currentLocationId,
                    file = currentFilePath,
                    snapshotIntervalMs = GetSnapshotIntervalMs()
                });
                RegisterUpdateHub();
            }
            catch (Exception ex)
            {
                SafeLogRecorderError("Failed to start battle recorder.", ex);
                DisposeWriter();
            }
        }

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
                currentRaidId = null;
                currentLocationId = null;
                currentFilePath = null;
                eventSequence = 0;
                writeErrorLogged = false;
                UnregisterUpdateHub();
            }
        }

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

        public static void RecordCombatLayerState(BotOwner bot, bool active, string reason)
        {
            if (!CanRecordBot(bot))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(bot);
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
            state.InCombat = true;
            state.LastCombatSeenTime = Time.time;
            state.LastDecisionAction = nextDecision.Action.ToString();
            state.LastDecisionReason = nextDecision.Reason;
            if (!string.IsNullOrEmpty(objectiveName))
            {
                state.CurrentObjective = objectiveName;
            }

            WriteEventInternal("decisionSelected", bot, new
            {
                objective = objectiveName,
                previous = previousDecision.HasValue ? CreateDecisionPayload(previousDecision.Value) : null,
                next = CreateDecisionPayload(nextDecision),
                state = CreateRecorderStatePayload(state)
            });
        }

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

            WriteEventInternal("decisionEnd", bot, new
            {
                objective = objectiveName,
                decision = CreateDecisionPayload(currentDecision),
                end = new
                {
                    shouldEnd = endResult.Value,
                    reason = endResult.Reason
                },
                state = CreateRecorderStatePayload(state)
            });
        }

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

        private static void OnBotManualUpdate(BotOwner owner)
        {
            if (!IsEnabled())
            {
                EndRaid("disabled");
                return;
            }

            if (!CanRecordBot(owner))
            {
                return;
            }

            RecorderFollowerState state = GetOrCreateState(owner);
            bool layerActive = FollowerCombatLayer.IsFollowerCombatLayerActive(owner);
            bool shouldSnapshot = state.InCombat || layerActive;
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
            Vector3 lookDirection = NormalizePlanar(bot.LookDirection);
            bool hasMoveTarget = TryGetCurrentMoveTarget(bot, out Vector3 moveTarget);
            Vector3 moveTargetDirection = hasMoveTarget
                ? NormalizePlanar(moveTarget - currentPosition)
                : Vector3.zero;

            Vector3 movementDirection = Vector3.zero;
            bool hasMovementDirection = false;
            if (state.HasPreviousSnapshot)
            {
                Vector3 delta = currentPosition - state.LastSnapshotPosition;
                if (delta.sqrMagnitude > 0.0025f)
                {
                    movementDirection = NormalizePlanar(delta);
                    hasMovementDirection = movementDirection.sqrMagnitude > 0.0001f;
                }
            }

            EnemyInfo? goalEnemy = bot.Memory?.GoalEnemy;
            object? enemySnapshot = CreateEnemySnapshot(bot, goalEnemy, currentPosition, lookDirection, moveTargetDirection);
            object? bossSnapshot = CreateBossSnapshot(bot, currentPosition, lookDirection);

            var snapshot = new
            {
                state = CreateRecorderStatePayload(state),
                botState = bot.BotState.ToString(),
                position = CreateVector(currentPosition),
                lookDirection = CreateVector(lookDirection),
                currentMoveTarget = hasMoveTarget ? CreateVector(moveTarget) : null,
                movement = new
                {
                    sprinting = bot.Mover?.Sprinting == true,
                    hasPathTarget = bot.GoToSomePointData?.HaveTarget() == true,
                    reachedTarget = bot.GoToSomePointData?.IsCome() == true,
                    targetPose = SanitizeFloat(bot.Mover?.TargetPose ?? 0f),
                    poseLevel = SanitizeFloat(bot.GetPlayer?.MovementContext?.PoseLevel ?? 0f),
                    prone = bot.GetPlayer?.MovementContext?.IsInPronePose == true,
                    direction = hasMovementDirection ? CreateVector(movementDirection) : null,
                    lookVsMoveAngle = hasMovementDirection ? SanitizeFloat(Vector3.Angle(lookDirection, movementDirection)) : null,
                    lookVsMoveTargetAngle = hasMoveTarget ? SanitizeFloat(Vector3.Angle(lookDirection, moveTargetDirection)) : null
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
                    healthStatus = bot.GetPlayer?.HealthStatus.ToString()
                },
                dogFight = bot.DogFight?.DogFightState.ToString(),
                weapon = CreateLightWeaponSnapshot(bot),
                enemy = enemySnapshot,
                boss = bossSnapshot,
                tactic = followerData?.CombatTactic.ToString()
            };

            state.LastSnapshotPosition = currentPosition;
            state.HasPreviousSnapshot = true;
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
                objective = state.CurrentObjective,
                lastDecisionAction = state.LastDecisionAction,
                lastDecisionReason = state.LastDecisionReason
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
                        magazineCount = activeWeapon.GetCurrentMagazine()?.Cartridges?.Count
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
            return new
            {
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
                magazineCount = weapon.GetCurrentMagazine()?.Cartridges?.Count
            };
        }

        private static object? CreateEnemySnapshot(
            BotOwner bot,
            EnemyInfo? goalEnemy,
            Vector3 botPosition,
            Vector3 lookDirection,
            Vector3 moveTargetDirection)
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
                distance = SanitizeFloat(goalEnemy.Distance),
                visibleType = goalEnemy.VisibleType.ToString(),
                isVisible = goalEnemy.IsVisible,
                canShoot = goalEnemy.CanShoot,
                reliableShootLane = FollowerImmediateFirePolicy.HasReliableImmediateFireLane(bot, goalEnemy),
                haveSeen = goalEnemy.HaveSeen,
                personalSeenTime = SanitizeFloat(goalEnemy.PersonalSeenTime),
                personalLastSeenTime = SanitizeFloat(goalEnemy.PersonalLastSeenTime),
                lastKnownPosition = CreateVector(goalEnemy.EnemyLastPositionReal),
                position = CreateVector(position),
                provenance = CreateEnemyProvenanceContext(groupInfo),
                contact = CreateEnemyContactContext(goalEnemy, groupInfo),
                geometry = CreateEnemyGeometryContext(bot, position),
                direction = hasEnemyDirection ? CreateVector(toEnemyDirection) : null,
                lookVsEnemyAngle = hasEnemyDirection ? SanitizeFloat(Vector3.Angle(lookDirection, toEnemyDirection)) : null,
                moveTargetVsEnemyAngle = moveTargetDirection.sqrMagnitude > 0.0001f && hasEnemyDirection
                    ? SanitizeFloat(Vector3.Angle(moveTargetDirection, toEnemyDirection))
                    : null
            };
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
            return pitFireTeam.battleRecorderEnabled?.Value == true;
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

                    var envelope = new
                    {
                        seq = ++eventSequence,
                        time = SanitizeFloat(Time.time),
                        utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
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
                }
            }
            catch (Exception ex)
            {
                if (!writeErrorLogged)
                {
                    writeErrorLogged = true;
                    SafeLogRecorderError("Battle recorder write failure.", ex);
                }
            }
        }

        private static void DisposeWriter()
        {
            lock (SyncRoot)
            {
                writer?.Flush();
                writer?.Dispose();
                writer = null;
            }
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
            public float LastCombatSeenTime;
            public float NextSnapshotTime;
            public bool HasPreviousSnapshot;
            public Vector3 LastSnapshotPosition;
            public string? CurrentObjective;
            public string? LastDecisionAction;
            public string? LastDecisionReason;
        }
    }
}
