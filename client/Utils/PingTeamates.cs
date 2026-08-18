
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.HealthSystem;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

using pitTeam.Components;
using pitTeam.Modules;

namespace pitTeam.Utils
{
    internal class BotData
    {
        public void SetData(BotOwner botData)
        {
            LastUpdate = Time.time;
            Data = botData;
        }

        public float LastUpdate;
        public BotOwner Data;
        public GUIContent GuiContent;
        public Rect GuiRect;
    }

    internal sealed class EnemyMarkerContact
    {
        public EnemyMarkerContact(string enemyProfileId, float untilTime)
        {
            EnemyProfileId = enemyProfileId;
            UntilTime = untilTime;
        }

        public string EnemyProfileId { get; }
        public Vector3 WorldPosition;
        public float UntilTime;
        public float NextHiddenPositionRefreshTime;
        public bool HasCapturedPosition;
        public bool IsVisible;
        public bool IsDead;
        public bool IsRetainedDeath;
        public BotOwner? ReportingFollower;
        public Rect MarkRect;

        public void SetDead(bool isDead)
        {
            IsDead = isDead;
        }
    }

    internal sealed class RetainedEnemyDownContact
    {
        public RetainedEnemyDownContact(Vector3 worldPosition, float recordedAt)
        {
            WorldPosition = worldPosition;
            RecordedAt = recordedAt;
        }

        public Vector3 WorldPosition { get; }
        public float RecordedAt { get; }
    }

    internal class PingTeamates : MonoBehaviour, IDisposable
    {

        public List<BotData> botMap = new List<BotData>();
        private readonly Dictionary<string, BotData> botDataCache = new Dictionary<string, BotData>(StringComparer.Ordinal);
        private readonly Dictionary<string, EnemyMarkerContact> enemyMarkersByProfileId =
            new Dictionary<string, EnemyMarkerContact>(StringComparer.Ordinal);
        private readonly List<EnemyMarkerContact> enemyMarkers = new List<EnemyMarkerContact>();
        private readonly HashSet<string> activeEnemyProfileIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, RetainedEnemyDownContact> retainedEnemyDownByProfileId =
            new Dictionary<string, RetainedEnemyDownContact>(StringComparer.Ordinal);

        private float _statusReportUntil;
        private float _enemyKilledMarkerUntil;

        private float nextUpdateTime;

        private GUIStyle guiStyle;
        private Texture2D? _enemyVisibleTexture;
        private Texture2D? _enemySeenTexture;
        private Texture2D? _enemyDownTexture;
        private bool _enemyMarkerTextureLoadAttempted;

        private float screenScale = 1.0f;
        private float fovFactor = 1f;
        private readonly StringBuilder _guiTextBuilder = new StringBuilder(256);
        private static readonly EBodyPart[] TrackedBodyParts =
        {
            EBodyPart.Head,
            EBodyPart.Chest,
            EBodyPart.Stomach,
            EBodyPart.RightArm,
            EBodyPart.LeftArm,
            EBodyPart.RightLeg,
            EBodyPart.LeftLeg
        };

        Player myPlayer;

        private bool _statusReportVisible;

        public static PingTeamates Instance = null;

        private static RadioSound radioSound;
        private const float MaxSpatialPingDistance = 40f;
        private const float MaxEnemyMarkerWorldCoordinate = 100000f;
        private const float EnemyMarkerHeightOffset = 1.6f;
        private const float EnemySeenMarkerSizePixels = 34f;
        private const float EnemyVisibleMarkerSizePixels = 30f;
        private const float EnemyActiveMarkerScale = 0.9f;
        private const float ReliableVisibleMaxAgeSeconds = 0.35f;
        private const float HiddenEnemyMarkerRefreshSeconds = 5f;
        private const string EnemyVisibleTextureFileName = "enemy-visible.png";
        private const string EnemySeenTextureFileName = "enemy-seen.png";
        private const string EnemyDownTextureFileName = "enemy-down.png";
        private const float StatusReportHeadGapPixels = 10f;
        private const float StatusReportCloseHeadGapPixels = 20f;
        private const float StatusReportCloseDistanceMeters = 6f;
        private const float StatusReportNormalDistanceMeters = 12f;
        private const float FallbackHeadTopOffsetMeters = 0.18f;
        private const float AlwaysHighlightRosterRefreshSeconds = 1f;
        private bool locationPing = false;
        private float _nextAlwaysHighlightRosterUpdateTime;
        private bool _alwaysHighlightWasActive;
        private bool _alwaysHighlightRosterReady;
        private int _highlightRebuildAfterFrame = -1;
        private readonly TeammatePingHighlight _teammateHighlight = new TeammatePingHighlight();

        public void Ping(pitAIBossPlayer player)
        {
            float now = Time.time;
            bool statusReportWasActive = _statusReportUntil > now;
            bool killedMarkerWasActive = IsEnemyKilledMarkerDisplayActive(now);
            _statusReportUntil = now + pitFireTeam.pingTime.Value;
            _enemyKilledMarkerUntil = IsEnemyKilledMarkerEnabled()
                ? now + (pitFireTeam.enemyKilledDisplayTime?.Value ?? 10)
                : 0f;

            locationPing = false;

            List<Components.BotFollowerPlayer> followers = BossPlayers.GetFollowersByBoss(player.realPlayer.ProfileId);

            myPlayer = player.realPlayer;

            botMap.Clear();
            if (!statusReportWasActive && !killedMarkerWasActive)
            {
                ClearEnemyMarkerContacts();
            }

            foreach (Components.BotFollowerPlayer fl in followers)
            {
                BotOwner bot = fl?.GetBot();
                if (bot == null || bot.IsDead)
                {
                    continue;
                }

                string profileId = bot.ProfileId;
                if (string.IsNullOrEmpty(profileId))
                {
                    continue;
                }

                if (!botDataCache.TryGetValue(profileId, out BotData botData))
                {
                    botData = new BotData();
                    botDataCache[profileId] = botData;
                }

                botData.SetData(bot);
                botMap.Add(botData);
            }

            if (killedMarkerWasActive)
            {
                ExtendDisplayedEnemyKilledMarkers();
            }

            SynchronizeEnemyMarkerContacts();
            if (IsEnemyKilledMarkerDisplayActive(now))
            {
                AddRetainedEnemyDownContacts();
            }
            RefreshEnemyMarkerContacts(removeUnresolved: true, captureHiddenPosition: true);
            EnemyMarkerContact? closestReportedContact = GetClosestReportedContact(player.Position);
            BotOwner? closestEnemySpeaker = closestReportedContact?.ReportingFollower;
            Vector3 closestReportedEnemyPosition = closestReportedContact?.WorldPosition ?? Vector3.zero;
            locationPing = closestReportedContact != null;

            if (pitFireTeam.statusReportHighlight?.Value != false)
            {
                if (pitFireTeam.statusReportAlwaysHighlight?.Value == true)
                {
                    if (statusReportWasActive)
                    {
                        // Repeated reports extend the existing outline without a blank frame.
                        _highlightRebuildAfterFrame = -1;
                        _teammateHighlight.Show(botMap, myPlayer);
                        _alwaysHighlightRosterReady = true;
                    }
                    else
                    {
                        // Match a manual Off/On cycle: clear the command buffer and cached
                        // renderers now, then let EFT settle LOD visibility for one frame
                        // before collecting the live teammate renderers again.
                        _teammateHighlight.Reset();
                        _highlightRebuildAfterFrame = Time.frameCount + 1;
                    }
                }
                else
                {
                    _teammateHighlight.Show(botMap, myPlayer);
                }
            }

            if (radioSound != null && locationPing)
            {
                Vector3 position = GetLimitedPosition(player.Position, closestReportedEnemyPosition, MaxSpatialPingDistance);
                radioSound.PlayLocationSound(position);
            }

            if (closestEnemySpeaker != null)
            {
                TrySpeakBossRelativeEnemyDirection(closestEnemySpeaker, player.realPlayer, closestReportedEnemyPosition);
            }

            if (radioSound != null && !locationPing)
            {
                if (HasAnyAliveFollower())
                {
                    Vector3 closestFollowerPos = GetClosestFollowerPosition(player.Position);
                    Vector3 clampedRadioPos = GetLimitedPosition(player.Position, closestFollowerPos, MaxSpatialPingDistance);
                    radioSound.PlayRadioSound(clampedRadioPos);
                }
                else
                {
                    radioSound.PlayRadioSound();
                }
            }

        }

        private void TrySpeakBossRelativeEnemyDirection(BotOwner speaker, Player bossPlayer, Vector3 enemyPosition)
        {
            if (speaker == null || bossPlayer == null)
            {
                return;
            }

            EPhraseTrigger trigger = GetBossRelativeDirectionTrigger(bossPlayer, enemyPosition);
            if (trigger == EPhraseTrigger.PhraseNone)
            {
                return;
            }

            speaker.BotTalk.TrySay(trigger, true);
        }

        private EPhraseTrigger GetBossRelativeDirectionTrigger(Player bossPlayer, Vector3 enemyPosition)
        {
            Vector3 toEnemy = enemyPosition - bossPlayer.Transform.position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude <= 0.0001f)
            {
                return EPhraseTrigger.OnRepeatedContact;
            }

            Vector3 bossLookDirection = bossPlayer.MovementContext?.PlayerRealForward ?? bossPlayer.LookDirection;
            bossLookDirection.y = 0f;
            if (bossLookDirection.sqrMagnitude <= 0.0001f)
            {
                bossLookDirection = bossPlayer.Transform.forward;
                bossLookDirection.y = 0f;
            }

            if (bossLookDirection.sqrMagnitude <= 0.0001f)
            {
                return EPhraseTrigger.OnRepeatedContact;
            }

            float signedAngle = Vector3.SignedAngle(bossLookDirection.normalized, toEnemy.normalized, Vector3.up);
            float absoluteAngle = Mathf.Abs(signedAngle);

            if (absoluteAngle <= 35f)
            {
                return EPhraseTrigger.InTheFront;
            }

            if (absoluteAngle >= 145f)
            {
                return EPhraseTrigger.OnSix;
            }

            return signedAngle < 0f ? EPhraseTrigger.LeftFlank : EPhraseTrigger.RightFlank;
        }

        public void Dispose()
        {
            _teammateHighlight.Dispose();
            botMap.Clear();
            botDataCache.Clear();
            ClearEnemyMarkerContacts();
            retainedEnemyDownByProfileId.Clear();
            DestroyEnemyMarkerTextures();
            Destroy(this);
            Destroy(radioSound);
            radioSound = null;
        }

        public void Update()
        {
            if (!IsEnemyKilledMarkerEnabled())
            {
                _enemyKilledMarkerUntil = 0f;
                retainedEnemyDownByProfileId.Clear();
            }

            bool highlightEnabled = pitFireTeam.statusReportHighlight?.Value != false;
            bool alwaysHighlightActive =
                highlightEnabled &&
                pitFireTeam.statusReportAlwaysHighlight?.Value == true;
            bool highlightRebuildPending = _highlightRebuildAfterFrame >= 0;

            if (highlightRebuildPending &&
                Time.frameCount >= _highlightRebuildAfterFrame)
            {
                if (highlightEnabled && myPlayer != null)
                {
                    _teammateHighlight.Show(botMap, myPlayer);
                    _alwaysHighlightRosterReady = alwaysHighlightActive;
                }

                _highlightRebuildAfterFrame = -1;
                highlightRebuildPending = false;
            }

            if (!highlightRebuildPending &&
                alwaysHighlightActive &&
                (!_alwaysHighlightWasActive || Time.time >= _nextAlwaysHighlightRosterUpdateTime))
            {
                _alwaysHighlightRosterReady = RefreshAlwaysHighlightRoster();
                _nextAlwaysHighlightRosterUpdateTime =
                    Time.time + AlwaysHighlightRosterRefreshSeconds;
            }
            else if (!alwaysHighlightActive)
            {
                _alwaysHighlightRosterReady = false;
            }

            _alwaysHighlightWasActive = alwaysHighlightActive;

            if (botMap.Count > 0)
            {
                _statusReportVisible = _statusReportUntil > Time.time;
                if (!_statusReportVisible && !alwaysHighlightActive)
                {
                    botMap.Clear();
                }
            }
            else
            {
                _statusReportVisible = false;
            }

            bool killedMarkerDisplayActive = IsEnemyKilledMarkerDisplayActive(Time.time);
            if (_statusReportVisible)
            {
                SynchronizeEnemyMarkerContacts();
                RefreshEnemyMarkerContacts(removeUnresolved: false, captureHiddenPosition: false);
            }

            if (!_statusReportVisible && !killedMarkerDisplayActive)
            {
                ClearEnemyMarkerContacts();
            }
            else if (!_statusReportVisible || !killedMarkerDisplayActive)
            {
                PruneEnemyMarkerContacts(
                    keepLiveContacts: _statusReportVisible,
                    keepRetainedDeaths: killedMarkerDisplayActive);
            }

            _teammateHighlight.Render(
                !highlightRebuildPending &&
                highlightEnabled &&
                (_statusReportVisible || (alwaysHighlightActive && _alwaysHighlightRosterReady)));


            if (Time.time < nextUpdateTime)
            {
                return;
            }
            nextUpdateTime = Time.time + 1.0f;

            if (CameraClass.Instance.SSAA != null && CameraClass.Instance.SSAA.isActiveAndEnabled)
            {
                int outputWidth = CameraClass.Instance.SSAA.GetOutputWidth();
                float inputWidth = CameraClass.Instance.SSAA.GetInputWidth();
                screenScale = outputWidth / inputWidth;
            }
        }

        private bool RefreshAlwaysHighlightRoster()
        {
            Player localPlayer = GamePlayerOwner.MyPlayer;
            pitAIBossPlayer boss =
                localPlayer != null
                    ? BossPlayers.Instance?.GetBossPlayer(localPlayer.ProfileId)
                    : null;

            botMap.Clear();
            if (localPlayer == null || boss == null)
            {
                return false;
            }

            myPlayer = localPlayer;
            List<Components.BotFollowerPlayer> followers =
                BossPlayers.GetFollowersByBoss(localPlayer.ProfileId);
            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner bot = followers[i]?.GetBot();
                if (bot == null || bot.IsDead || string.IsNullOrEmpty(bot.ProfileId))
                {
                    continue;
                }

                if (!botDataCache.TryGetValue(bot.ProfileId, out BotData botData))
                {
                    botData = new BotData();
                    botDataCache[bot.ProfileId] = botData;
                    botData.SetData(bot);
                }
                else if (!ReferenceEquals(botData.Data, bot))
                {
                    botData.SetData(bot);
                }

                botMap.Add(botData);
            }

            _teammateHighlight.Show(botMap, myPlayer);
            return true;
        }

        void OnGUI()
        {
            bool killedMarkerDisplayActive = IsEnemyKilledMarkerDisplayActive(Time.time);
            if (!_statusReportVisible && !killedMarkerDisplayActive) return;

            if (guiStyle == null)
            {
                CreateGuiStyle();
            }

            guiStyle.normal.textColor = StatusReportHighlightColor.GetConfiguredTextColor();

            if (_statusReportVisible && botMap != null)
            {
                for (int i = 0; i < botMap.Count; i++)
                {
                    DrawBotGUI(botMap[i]);
                }
            }

            if ((_statusReportVisible && pitFireTeam.enemyMarker.Value) ||
                killedMarkerDisplayActive)
            {
                for (int i = 0; i < enemyMarkers.Count; i++)
                {
                    DrawEnemyMarkerGUI(enemyMarkers[i]);
                }
            }
        }

        private void DrawBotGUI(BotData bt)
        {
            if (!_statusReportVisible) return;

            if (bt == null || bt.Data == null || !bt.Data.HealthController.IsAlive) return;

            Camera mainCamera = Camera.main;
            Player teammate = bt.Data.GetPlayer;
            if (mainCamera == null || teammate == null) return;

            Vector3 screenPos = mainCamera.WorldToScreenPoint(GetStatusReportHeadTop(teammate));

            if (screenPos.z > 0)
            {
                float teammateDistance = (bt.Data.Position - myPlayer.Transform.position).magnitude;
                int dist = Mathf.RoundToInt(teammateDistance);

                if (dist < 301)
                {
                    if (bt.GuiContent == null)
                    {
                        bt.GuiContent = new GUIContent();
                    }
                    if (bt.GuiRect == null)
                    {
                        bt.GuiRect = new Rect();
                    }

                    StringBuilder stringBuilder = _guiTextBuilder;
                    stringBuilder.Clear();

                    bool showName = pitFireTeam.statusReportShowName?.Value != false;
                    bool showDistance = pitFireTeam.statusReportShowDistance?.Value != false;
                    bool showHealth = pitFireTeam.statusReportShowHealth?.Value != false;
                    bool showTactic = pitFireTeam.statusReportShowTactic?.Value != false;
                    bool showCombatStatus = pitFireTeam.statusReportShowCombatStatus?.Value != false;

                    if (showName)
                    {
                        stringBuilder.Append(bt.Data.Profile.Nickname);
                    }

                    if (showDistance)
                    {
                        if (stringBuilder.Length > 0)
                        {
                            stringBuilder.Append(" - ");
                        }

                        stringBuilder.Append(dist);
                        stringBuilder.Append("m");
                    }

                    if (showCombatStatus)
                    {
                        string? combatStatus = null;
                        if (IsFollowerCurrentlyHealing(bt.Data))
                        {
                            combatStatus = pitFireTeam.GetBotStatusText("Heal");
                        }
                        else if (DoesFollowerWantToHeal(bt.Data))
                        {
                            combatStatus = pitFireTeam.GetBotStatusText("WantToHeal");
                        }
                        else if (bt.Data.Memory.HaveEnemy)
                        {
                            EnemyInfo goalEnemy = bt.Data.Memory.GoalEnemy;
                            if (goalEnemy != null)
                            {
                                float lastSeenAgo = Time.time - goalEnemy.PersonalLastSeenTime;
                                if (IsEnemyReliablyVisibleForMarker(bt.Data, goalEnemy) || lastSeenAgo < 5f)
                                {
                                    combatStatus = pitFireTeam.GetBotStatusText("Engaged");
                                }
                                else
                                {
                                    combatStatus = pitFireTeam.GetBotStatusText("Alerted");
                                }
                            }
                            else
                            {
                                combatStatus = pitFireTeam.GetBotStatusText("Alerted");
                            }
                        }

                        if (combatStatus != null)
                        {
                            if (stringBuilder.Length > 0)
                            {
                                stringBuilder.Append(": ");
                            }

                            stringBuilder.Append(combatStatus);
                        }
                    }

                    bool detailStarted = false;
                    if (showHealth)
                    {
                        float hp = 0;
                        float hpmax = 0;
                        string blackout = "";

                        for (int i = 0; i < TrackedBodyParts.Length; i++)
                        {
                            EBodyPart part = TrackedBodyParts[i];
                            bt.Data.Profile.Health.BodyParts.TryGetValue(part, out var bodyPart);
                            if (bodyPart != null)
                            {
                                ValueStruct value = bt.Data.HealthController.GetBodyPartHealth(part, true);
                                hp += value.Current;
                                hpmax += value.Maximum;
                                if (value.Current == 0)
                                {
                                    if (blackout.Length > 0) blackout += ", ";
                                    blackout += part.ToString().Localized();
                                }
                            }
                        }

                        if (hp > 0)
                        {
                            AppendStatusReportDetailSeparator(stringBuilder, ref detailStarted);
                            if (hp < hpmax)
                                stringBuilder.Append($"HP: {hp}/{hpmax}");
                            else stringBuilder.Append($"HP: {hpmax}");

                            if (blackout.Length > 0)
                            {
                                stringBuilder.Append(Environment.NewLine);
                                stringBuilder.Append("0%: " + blackout);
                            }
                        }
                    }

                    if (BossPlayers.IsFollower(bt.Data))
                    {
                        BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(bt.Data);
                        if (showTactic)
                        {
                            string tactic = pitFireTeam.GetTacticOptionText(0);
                            if (followerData != null)
                            {
                                tactic = followerData.CombatTactic switch
                                {
                                    FollowerCombatTactic.Marksman => pitFireTeam.GetSocialUiText("ProfileTacticMarksman"),
                                    FollowerCombatTactic.Protector => pitFireTeam.GetSocialUiText("ProfileTacticProtector"),
                                    _ => pitFireTeam.GetTacticOptionText(0),
                                };
                            }
                            if (tactic != null)
                            {
                                AppendStatusReportDetailSeparator(stringBuilder, ref detailStarted);
                                stringBuilder.Append($"MD: {tactic}");
                            }
                        }

                    }

                    bt.GuiContent.text = stringBuilder.ToString();
                    if (string.IsNullOrEmpty(bt.GuiContent.text))
                    {
                        return;
                    }

                    Vector2 guiSize = guiStyle.CalcSize(bt.GuiContent);

                    bt.GuiRect.x = (screenPos.x * screenScale) - (guiSize.x / 2);
                    float headGapPixels = Mathf.Lerp(
                        StatusReportCloseHeadGapPixels,
                        StatusReportHeadGapPixels,
                        Mathf.InverseLerp(StatusReportCloseDistanceMeters, StatusReportNormalDistanceMeters, teammateDistance));

                    bt.GuiRect.y = Screen.height - ((screenPos.y * screenScale) + guiSize.y + headGapPixels);
                    bt.GuiRect.size = guiSize;

                    GUI.Box(bt.GuiRect, bt.GuiContent.text, guiStyle);
                }
            }
        }

        private static Vector3 GetStatusReportHeadTop(Player teammate)
        {
            PlayerBones bones = teammate.PlayerBones;
            if (bones != null)
            {
                if (bones.BodyPartCollidersDictionary.TryGetValue(EBodyPartColliderType.HeadCommon, out BodyPartCollider headCollider) &&
                    headCollider?.Collider != null)
                {
                    Bounds headBounds = headCollider.Collider.bounds;
                    return new Vector3(headBounds.center.x, headBounds.max.y, headBounds.center.z);
                }

                if (bones.Head != null)
                {
                    return bones.Head.position + (Vector3.up * FallbackHeadTopOffsetMeters);
                }
            }

            return teammate.Position + (Vector3.up * 1.78f);
        }

        private static void AppendStatusReportDetailSeparator(StringBuilder stringBuilder, ref bool detailStarted)
        {
            if (detailStarted)
            {
                stringBuilder.Append(" | ");
                return;
            }

            if (stringBuilder.Length > 0)
            {
                stringBuilder.Append(Environment.NewLine);
            }

            detailStarted = true;
        }

        private void DrawEnemyMarkerGUI(EnemyMarkerContact contact)
        {
            if (contact == null || Time.time > contact.UntilTime)
            {
                return;
            }

            if (contact.IsDead)
            {
                if (!IsEnemyKilledMarkerDisplayActive(Time.time))
                {
                    return;
                }
            }
            else if (!_statusReportVisible || pitFireTeam.enemyMarker?.Value == false)
            {
                return;
            }

            // Keep the established behavior of hiding world markers while an optic camera is active.
            if (CameraClass.Instance?.OpticCameraManager?.CurrentOpticSight != null &&
                CameraClass.Instance.OpticCameraManager.Camera != null)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null || !IsPlausibleEnemyMarkerPosition(contact.WorldPosition))
            {
                return;
            }

            Vector3 screenPos = mainCamera.WorldToScreenPoint(
                contact.WorldPosition + (Vector3.up * EnemyMarkerHeightOffset));
            if (screenPos.z <= 0f)
            {
                return;
            }

            if (!EnsureEnemyMarkerTextures())
            {
                return;
            }

            Texture2D? markerTexture = contact.IsDead
                ? _enemyDownTexture
                : contact.IsVisible
                    ? _enemyVisibleTexture
                    : _enemySeenTexture;
            if (markerTexture == null)
            {
                return;
            }

            bool animateVertically = ReferenceEquals(markerTexture, _enemySeenTexture);
            DrawEnemyMarker(contact, screenPos, markerTexture, animateVertically);
        }

        private void ClearEnemyMarkerContacts()
        {
            enemyMarkers.Clear();
            enemyMarkersByProfileId.Clear();
            activeEnemyProfileIds.Clear();
        }

        private static bool IsEnemyKilledMarkerEnabled()
        {
            return (pitFireTeam.enemyKilledRetainTime?.Value ?? 15) > 0;
        }

        private bool IsEnemyKilledMarkerDisplayActive(float now)
        {
            return IsEnemyKilledMarkerEnabled() &&
                   _enemyKilledMarkerUntil > now;
        }

        private void ExtendDisplayedEnemyKilledMarkers()
        {
            for (int i = 0; i < enemyMarkers.Count; i++)
            {
                EnemyMarkerContact contact = enemyMarkers[i];
                if (contact.IsDead)
                {
                    contact.UntilTime = _enemyKilledMarkerUntil;
                }
            }
        }

        private void PruneEnemyMarkerContacts(
            bool keepLiveContacts,
            bool keepRetainedDeaths)
        {
            for (int i = enemyMarkers.Count - 1; i >= 0; i--)
            {
                EnemyMarkerContact contact = enemyMarkers[i];
                bool isKilledContact = contact.IsDead || contact.IsRetainedDeath;
                if ((isKilledContact && keepRetainedDeaths) ||
                    (!isKilledContact && keepLiveContacts))
                {
                    continue;
                }

                enemyMarkers.RemoveAt(i);
                enemyMarkersByProfileId.Remove(contact.EnemyProfileId);
            }
        }

        public static void TryRememberEnemyDown(Player victim, IPlayer aggressor)
        {
            PingTeamates instance = Instance;
            Player localPlayer = GamePlayerOwner.MyPlayer;
            if (instance == null || victim == null || aggressor == null || localPlayer == null ||
                string.IsNullOrEmpty(victim.ProfileId) || string.IsNullOrEmpty(aggressor.ProfileId))
            {
                return;
            }

            int retainTimeSeconds = pitFireTeam.enemyKilledRetainTime?.Value ?? 15;
            if (retainTimeSeconds <= 0)
            {
                return;
            }

            List<Components.BotFollowerPlayer> followers =
                BossPlayers.GetFollowersByBoss(localPlayer.ProfileId);
            bool wasTrackedEnemy =
                instance.enemyMarkersByProfileId.TryGetValue(
                    victim.ProfileId,
                    out EnemyMarkerContact trackedContact) &&
                trackedContact.UntilTime > Time.time;
            if (!WasKilledByPlayerSquad(aggressor.ProfileId, localPlayer.ProfileId, followers) ||
                (!wasTrackedEnemy && !IsCurrentEnemyOfAnyFollower(victim.ProfileId, followers)))
            {
                return;
            }

            Vector3 deathPosition;
            try
            {
                deathPosition = victim.Transform != null
                    ? victim.Transform.position
                    : victim.Position;
            }
            catch
            {
                return;
            }

            if (!IsPlausibleEnemyMarkerPosition(deathPosition))
            {
                return;
            }

            instance.retainedEnemyDownByProfileId[victim.ProfileId] =
                new RetainedEnemyDownContact(deathPosition, Time.time);

            // Promote the currently displayed contact immediately. Otherwise the follower can
            // clear GoalEnemy before Update(), causing synchronization to remove the marker until
            // the player requests another Status Report.
            if (instance.IsEnemyKilledMarkerDisplayActive(Time.time))
            {
                instance.AddRetainedEnemyDownContacts();
            }
        }

        private static bool WasKilledByPlayerSquad(
            string aggressorProfileId,
            string localPlayerProfileId,
            List<Components.BotFollowerPlayer> followers)
        {
            if (string.Equals(aggressorProfileId, localPlayerProfileId, StringComparison.Ordinal))
            {
                return true;
            }

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i]?.GetBot();
                if (follower != null &&
                    string.Equals(follower.ProfileId, aggressorProfileId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCurrentEnemyOfAnyFollower(
            string victimProfileId,
            List<Components.BotFollowerPlayer> followers)
        {
            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i]?.GetBot();
                if (follower == null || follower.IsDead)
                {
                    continue;
                }

                EnemyInfo? goalEnemy;
                try
                {
                    // EFT may clear HaveEnemy during death processing before this hook runs.
                    // The matching GoalEnemy can still identify the contact at that boundary.
                    goalEnemy = follower.Memory?.GoalEnemy;
                }
                catch
                {
                    continue;
                }

                if (string.Equals(goalEnemy?.ProfileId, victimProfileId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void AddRetainedEnemyDownContacts()
        {
            int retainTimeSeconds = pitFireTeam.enemyKilledRetainTime?.Value ?? 15;
            if (retainTimeSeconds <= 0)
            {
                retainedEnemyDownByProfileId.Clear();
                return;
            }

            List<string>? expiredProfileIds = null;
            foreach (KeyValuePair<string, RetainedEnemyDownContact> retained in retainedEnemyDownByProfileId)
            {
                float age = Time.time - retained.Value.RecordedAt;
                if (!IsFinite(age) || age < 0f || age > retainTimeSeconds)
                {
                    expiredProfileIds ??= new List<string>();
                    expiredProfileIds.Add(retained.Key);
                    continue;
                }

                if (!enemyMarkersByProfileId.TryGetValue(retained.Key, out EnemyMarkerContact contact))
                {
                    contact = new EnemyMarkerContact(retained.Key, _enemyKilledMarkerUntil);
                    enemyMarkersByProfileId.Add(retained.Key, contact);
                    enemyMarkers.Add(contact);
                }

                contact.WorldPosition = retained.Value.WorldPosition;
                contact.UntilTime = _enemyKilledMarkerUntil;
                contact.HasCapturedPosition = true;
                contact.IsVisible = false;
                contact.IsRetainedDeath = true;
                contact.ReportingFollower = null;
                contact.SetDead(true);
            }

            if (expiredProfileIds == null)
            {
                return;
            }

            for (int i = 0; i < expiredProfileIds.Count; i++)
            {
                retainedEnemyDownByProfileId.Remove(expiredProfileIds[i]);
            }
        }

        private void SynchronizeEnemyMarkerContacts()
        {
            activeEnemyProfileIds.Clear();
            for (int i = 0; i < botMap.Count; i++)
            {
                BotOwner? follower = botMap[i]?.Data;
                if (follower == null || follower.IsDead ||
                    !TryGetCurrentGoalEnemy(follower, out EnemyInfo? goalEnemy) ||
                    string.IsNullOrEmpty(goalEnemy.ProfileId))
                {
                    continue;
                }

                string enemyProfileId = goalEnemy.ProfileId;
                activeEnemyProfileIds.Add(enemyProfileId);
                if (enemyMarkersByProfileId.TryGetValue(
                        enemyProfileId,
                        out EnemyMarkerContact existingContact))
                {
                    if (!existingContact.IsRetainedDeath)
                    {
                        existingContact.UntilTime = _statusReportUntil;
                    }

                    continue;
                }

                EnemyMarkerContact contact =
                    new EnemyMarkerContact(enemyProfileId, _statusReportUntil);
                enemyMarkersByProfileId.Add(enemyProfileId, contact);
                enemyMarkers.Add(contact);
            }

            for (int i = enemyMarkers.Count - 1; i >= 0; i--)
            {
                EnemyMarkerContact contact = enemyMarkers[i];
                if (contact.IsRetainedDeath || activeEnemyProfileIds.Contains(contact.EnemyProfileId))
                {
                    continue;
                }

                enemyMarkers.RemoveAt(i);
                enemyMarkersByProfileId.Remove(contact.EnemyProfileId);
            }
        }

        private EnemyMarkerContact? GetClosestReportedContact(Vector3 playerPosition)
        {
            EnemyMarkerContact? closest = null;
            float closestSqr = float.MaxValue;
            for (int i = 0; i < enemyMarkers.Count; i++)
            {
                EnemyMarkerContact contact = enemyMarkers[i];
                BotOwner? reporter = contact.ReportingFollower;
                if (reporter == null || reporter.IsDead)
                {
                    continue;
                }

                float distanceSqr = (reporter.Position - playerPosition).sqrMagnitude;
                if (distanceSqr < closestSqr)
                {
                    closest = contact;
                    closestSqr = distanceSqr;
                }
            }

            return closest;
        }

        private void RefreshEnemyMarkerContacts(bool removeUnresolved, bool captureHiddenPosition)
        {
            for (int i = enemyMarkers.Count - 1; i >= 0; i--)
            {
                EnemyMarkerContact contact = enemyMarkers[i];
                if (contact.IsRetainedDeath)
                {
                    continue;
                }

                if (TryResolveEnemyMarker(contact.EnemyProfileId, out EnemyMarkerResolution resolution))
                {
                    bool shouldRefreshPosition =
                        resolution.IsVisible ||
                        captureHiddenPosition ||
                        !contact.HasCapturedPosition ||
                        Time.time >= contact.NextHiddenPositionRefreshTime;
                    if (shouldRefreshPosition)
                    {
                        contact.WorldPosition = resolution.WorldPosition;
                        contact.HasCapturedPosition = true;
                        contact.NextHiddenPositionRefreshTime =
                            Time.time + HiddenEnemyMarkerRefreshSeconds;
                    }

                    contact.IsVisible = resolution.IsVisible;
                    contact.SetDead(resolution.IsDead);
                    contact.ReportingFollower = resolution.ReportingFollower;
                    continue;
                }

                contact.IsVisible = false;
                contact.ReportingFollower = null;
                contact.SetDead(
                    Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(contact.EnemyProfileId) == null);
                if (!removeUnresolved)
                {
                    continue;
                }

                enemyMarkers.RemoveAt(i);
                enemyMarkersByProfileId.Remove(contact.EnemyProfileId);
            }
        }

        private bool TryResolveEnemyMarker(
            string enemyProfileId,
            out EnemyMarkerResolution resolution)
        {
            resolution = default;
            if (string.IsNullOrEmpty(enemyProfileId))
            {
                return false;
            }

            Player? liveEnemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(enemyProfileId);
            bool lifeStateKnown = liveEnemy?.HealthController != null;
            bool enemyAlive = liveEnemy?.HealthController?.IsAlive == true;

            BotOwner? visibleReporter = null;
            Vector3 visiblePosition = Vector3.zero;
            float visibleReporterDistanceSqr = float.MaxValue;

            BotOwner? fallbackReporter = null;
            Vector3 fallbackPosition = Vector3.zero;
            float fallbackReporterDistanceSqr = float.MaxValue;

            for (int i = 0; i < botMap.Count; i++)
            {
                BotOwner? follower = botMap[i]?.Data;
                if (follower == null || follower.IsDead ||
                    !TryGetCurrentGoalEnemy(follower, out EnemyInfo? goalEnemy) ||
                    !string.Equals(goalEnemy.ProfileId, enemyProfileId, StringComparison.Ordinal) ||
                    !TryGetEnemyCurrentPosition(goalEnemy, liveEnemy, out Vector3 currentEnemyPosition))
                {
                    continue;
                }

                if (goalEnemy.Person?.HealthController != null)
                {
                    lifeStateKnown = true;
                    enemyAlive |= goalEnemy.Person.HealthController.IsAlive;
                }

                float reporterDistanceSqr = myPlayer != null
                    ? (follower.Position - myPlayer.Position).sqrMagnitude
                    : 0f;

                if (IsEnemyReliablyVisibleForMarker(follower, goalEnemy))
                {
                    if (visibleReporter == null || reporterDistanceSqr < visibleReporterDistanceSqr)
                    {
                        visibleReporter = follower;
                        visiblePosition = currentEnemyPosition;
                        visibleReporterDistanceSqr = reporterDistanceSqr;
                    }

                    continue;
                }

                if (fallbackReporter == null || reporterDistanceSqr < fallbackReporterDistanceSqr)
                {
                    fallbackReporter = follower;
                    fallbackPosition = currentEnemyPosition;
                    fallbackReporterDistanceSqr = reporterDistanceSqr;
                }
            }

            bool isDead = lifeStateKnown && !enemyAlive;
            if (visibleReporter != null)
            {
                resolution = new EnemyMarkerResolution(
                    visiblePosition,
                    isVisible: true,
                    isDead,
                    visibleReporter);
                return true;
            }

            if (fallbackReporter != null)
            {
                resolution = new EnemyMarkerResolution(
                    fallbackPosition,
                    isVisible: false,
                    isDead,
                    fallbackReporter);
                return true;
            }

            return false;
        }

        private static bool TryGetCurrentGoalEnemy(BotOwner bot, out EnemyInfo? goalEnemy)
        {
            goalEnemy = null;
            if (bot?.Memory?.HaveEnemy != true)
            {
                return false;
            }

            try
            {
                goalEnemy = bot.Memory.GoalEnemy;
                return goalEnemy != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetEnemyCurrentPosition(
            EnemyInfo goalEnemy,
            Player? liveEnemy,
            out Vector3 currentEnemyPosition)
        {
            currentEnemyPosition = Vector3.zero;
            try
            {
                currentEnemyPosition = liveEnemy?.Transform != null
                    ? liveEnemy.Transform.position
                    : goalEnemy.CurrPosition;
            }
            catch
            {
                return false;
            }

            return IsPlausibleEnemyMarkerPosition(currentEnemyPosition);
        }

        private static bool IsPlausibleEnemyMarkerPosition(Vector3 position)
        {
            return IsFinite(position) &&
                   Mathf.Abs(position.x) <= MaxEnemyMarkerWorldCoordinate &&
                   Mathf.Abs(position.y) <= MaxEnemyMarkerWorldCoordinate &&
                   Mathf.Abs(position.z) <= MaxEnemyMarkerWorldCoordinate;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct EnemyMarkerResolution
        {
            public EnemyMarkerResolution(
                Vector3 worldPosition,
                bool isVisible,
                bool isDead,
                BotOwner reportingFollower)
            {
                WorldPosition = worldPosition;
                IsVisible = isVisible;
                IsDead = isDead;
                ReportingFollower = reportingFollower;
            }

            public Vector3 WorldPosition { get; }
            public bool IsVisible { get; }
            public bool IsDead { get; }
            public BotOwner ReportingFollower { get; }
        }

        private void CreateGuiStyle()
        {
            guiStyle = new GUIStyle(GUI.skin.box);
            guiStyle.alignment = TextAnchor.MiddleCenter;
            guiStyle.margin = new RectOffset(2, 2, 2, 2);
            guiStyle.fontStyle = FontStyle.Bold;
            guiStyle.fontSize = 20;
            guiStyle.richText = true;
            guiStyle.border = new RectOffset(0, 0, 0, 0);
            guiStyle.normal.background = MakeTexture(new Color(0, 0, 0, 0.3f));

            guiStyle.normal.textColor = StatusReportHighlightColor.GetConfiguredTextColor();


        }

        private bool EnsureEnemyMarkerTextures()
        {
            if (_enemyVisibleTexture != null &&
                _enemySeenTexture != null &&
                _enemyDownTexture != null)
            {
                return true;
            }

            if (_enemyMarkerTextureLoadAttempted)
            {
                return false;
            }

            _enemyMarkerTextureLoadAttempted = true;
            _enemyVisibleTexture = LoadEnemyMarkerTexture(
                EnemyVisibleTextureFileName,
                "pitFireTeam_EnemyVisibleMarker");
            _enemySeenTexture = LoadEnemyMarkerTexture(
                EnemySeenTextureFileName,
                "pitFireTeam_EnemySeenMarker");
            _enemyDownTexture = LoadEnemyMarkerTexture(
                EnemyDownTextureFileName,
                "pitFireTeam_EnemyDownMarker");

            if (_enemyVisibleTexture != null &&
                _enemySeenTexture != null &&
                _enemyDownTexture != null)
            {
                return true;
            }

            DestroyEnemyMarkerTextures();
            return false;
        }

        private static Texture2D? LoadEnemyMarkerTexture(
            string fileName,
            string textureName)
        {
            string? texturePath = FindEnemyMarkerTexturePath(fileName);
            if (string.IsNullOrEmpty(texturePath))
            {
                pitFireTeam.Log.LogError($"[StatusReport] Enemy-marker texture was not found: {fileName}");
                return null;
            }

            byte[] fileData;
            try
            {
                fileData = File.ReadAllBytes(texturePath);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log.LogError(
                    $"[StatusReport] Failed to read enemy-marker texture '{texturePath}': {ex.Message}");
                return null;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(fileData))
            {
                Destroy(texture);
                pitFireTeam.Log.LogError($"[StatusReport] Failed to decode enemy-marker texture: {texturePath}");
                return null;
            }

            texture.Apply(false, true);
            texture.name = textureName;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private static string? FindEnemyMarkerTexturePath(string fileName)
        {
            string pluginDirectory =
                Path.GetDirectoryName(typeof(PingTeamates).Assembly.Location) ?? string.Empty;
            string[] candidates =
            {
                Path.Combine(pluginDirectory, fileName),
                Path.Combine(pluginDirectory, "resources", fileName),
                Path.Combine(
                    Directory.GetParent(pluginDirectory)?.FullName ?? pluginDirectory,
                    "resources",
                    fileName),
                Path.Combine(
                    Environment.CurrentDirectory,
                    "BepInEx",
                    "plugins",
                    "pitFireTeam",
                    "resources",
                    fileName)
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            return null;
        }

        private void DestroyEnemyMarkerTextures()
        {
            if (_enemyVisibleTexture != null)
            {
                Destroy(_enemyVisibleTexture);
                _enemyVisibleTexture = null;
            }

            if (_enemySeenTexture != null)
            {
                Destroy(_enemySeenTexture);
                _enemySeenTexture = null;
            }

            if (_enemyDownTexture != null)
            {
                Destroy(_enemyDownTexture);
                _enemyDownTexture = null;
            }
        }

        private Texture2D MakeTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void DrawEnemyMarker(
            EnemyMarkerContact contact,
            Vector3 markerPos,
            Texture2D texture,
            bool animateVertically)
        {
            float markerHeight = contact.IsDead
                ? EnemyVisibleMarkerSizePixels * EnemyActiveMarkerScale
                : contact.IsVisible
                    ? EnemyVisibleMarkerSizePixels * EnemyActiveMarkerScale
                    : EnemySeenMarkerSizePixels * EnemyActiveMarkerScale;
            float markerWidth = markerHeight;
            float animationOffset = animateVertically
                ? Mathf.Sin(Time.time * 5f) * 5f
                : 0f;

            contact.MarkRect.x =
                (markerPos.x * screenScale / fovFactor) - (markerWidth / 2f);
            contact.MarkRect.y =
                Screen.height -
                ((markerPos.y * screenScale / fovFactor) + markerHeight) +
                animationOffset;
            contact.MarkRect.size = new Vector2(markerWidth, markerHeight);

            Color previousGuiColor = GUI.color;
            try
            {
                GUI.color = Color.white;
                GUI.DrawTexture(contact.MarkRect, texture, ScaleMode.ScaleToFit, true);
            }
            finally
            {
                GUI.color = previousGuiColor;
            }
        }

        private static bool IsEnemyReliablyVisibleForMarker(BotOwner bot, EnemyInfo goalEnemy)
        {
            if (bot == null || goalEnemy == null)
            {
                return false;
            }

            if (!goalEnemy.IsVisible || !goalEnemy.CanShoot)
            {
                return false;
            }

            // UI red should mean "actively visible now", not stale memory visibility.
            float lastSeenAge = Time.time - goalEnemy.PersonalLastSeenTime;
            if (!IsFinite(lastSeenAge) ||
                lastSeenAge < 0f ||
                lastSeenAge > ReliableVisibleMaxAgeSeconds)
            {
                return false;
            }

            if (bot.LookSensor == null || !bot.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            ShootPointClass? shootPoint = bot.CurrentEnemyTargetPosition(true);
            if (shootPoint == null)
            {
                return false;
            }

            return global::pitTeam.Utils.Utils.CanShootToTarget(
                shootPoint,
                bot.WeaponRoot.position,
                bot.LookSensor.Mask,
                false);
        }

        private static bool IsFollowerCurrentlyHealing(BotOwner bot)
        {
            if (bot?.Medecine == null)
            {
                return false;
            }

            if (bot.Medecine.Using ||
                bot.Medecine.FirstAid?.Using == true ||
                bot.Medecine.SurgicalKit?.Using == true ||
                bot.Medecine.Stimulators?.Using == true)
            {
                return true;
            }

            return false;
        }

        private static bool DoesFollowerWantToHeal(BotOwner bot)
        {
            if (bot?.Medecine == null || IsFollowerCurrentlyHealing(bot))
            {
                return false;
            }

            var decision = bot.Brain?.Agent?.LastResult();
            if (decision != null)
            {
                string reason = decision.Value.Reason ?? string.Empty;
                if (string.Equals(reason, "runToHeal", StringComparison.Ordinal) ||
                    string.Equals(reason, "moveToHeal", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return bot.Medecine.FirstAid?.Have2Do == true
                || bot.Medecine.SurgicalKit?.HaveWork == true;
        }

        public static void Enable()
        {
            if (Singleton<AbstractGame>.Instantiated)
            {
                var gameWorld = Singleton<GameWorld>.Instance;

                Instance = gameWorld.GetOrAddComponent<PingTeamates>();
                radioSound = gameWorld.GetOrAddComponent<RadioSound>();
                radioSound.Enable();
            }
        }
        private Vector3 GetLimitedPosition(Vector3 origin, Vector3 target, float maxDistance)
        {
            Vector3 delta = target - origin;
            float distance = delta.magnitude;
            if (distance > maxDistance && distance > 0.001f)
            {
                return origin + (delta / distance) * maxDistance;
            }

            return target;
        }

        private Vector3 GetClosestFollowerPosition(Vector3 playerPosition)
        {
            float best = float.MaxValue;
            Vector3 bestPos = playerPosition;

            foreach (BotData bt in botMap)
            {
                if (bt?.Data == null || bt.Data.IsDead) continue;

                float sqr = (bt.Data.Position - playerPosition).sqrMagnitude;
                if (sqr < best)
                {
                    best = sqr;
                    bestPos = bt.Data.Position;
                }
            }

            return bestPos;
        }

        private bool HasAnyAliveFollower()
        {
            foreach (BotData bt in botMap)
            {
                if (bt?.Data != null && !bt.Data.IsDead)
                {
                    return true;
                }
            }

            return false;
        }

        public static void Disable()
        {
            Instance.Dispose();
        }

    }

}
