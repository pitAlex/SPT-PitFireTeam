using System;
using System.Globalization;
using System.Linq;
using pitTeam.Modules;
using pitTeam.Shared;
using TMPro;
using UnityEngine;

namespace pitTeam.Components
{
    internal partial class SquadControlMenuUi
    {
        private TextMeshProUGUI encounterPenaltyLabel;
        private long encounterPenaltyNextUpdate;
        private int encounterPenaltyVersion = -1;

        private void CreateRosterPenaltyLabel(RectTransform parent)
        {
            GameObject labelObject = CreateText("pitFireTeam_RosterEncounterPenalty", string.Empty, 17f, TextAlignmentOptions.MidlineRight);
            labelObject.transform.SetParent(parent, false);
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.sizeDelta = new Vector2(580f, 24f);
            rect.anchoredPosition = new Vector2(-SettingsViewportSideInset - 28f, -12f);
            encounterPenaltyLabel = labelObject.GetComponent<TextMeshProUGUI>();
            encounterPenaltyLabel.color = new Color(0.92f, 0.25f, 0.22f, 0.96f);
            encounterPenaltyLabel.fontWeight = FontWeight.Regular;
            encounterPenaltyLabel.raycastTarget = false;
            labelObject.SetActive(false);
            encounterPenaltyVersion = -1;
        }

        private void UpdateRosterPenaltyLabel(bool force = false)
        {
            if (encounterPenaltyLabel == null || rosterPanel == null || !rosterPanel.activeInHierarchy) return;
            if (!GameplayModeRuntime.IsAllegiance)
            {
                encounterPenaltyLabel.gameObject.SetActive(false);
                encounterPenaltyVersion = -1;
                return;
            }
            long now = FriendlyEncounterPenaltyRuntime.NowUnixMs;
            int version = FriendlyEncounterPenaltyRuntime.Version;
            if (!force && version == encounterPenaltyVersion && now < encounterPenaltyNextUpdate) return;
            encounterPenaltyVersion = version;
            var active = FriendlyEncounterPenaltyRuntime.GetActiveEntries();
            encounterPenaltyLabel.gameObject.SetActive(active.Count > 0);
            if (active.Count == 0) { encounterPenaltyNextUpdate = long.MaxValue; return; }
            string value = FriendlyEncounterPenaltyPolicy.GetCountdown(active.Min(entry => entry.ExpiresAtUnixMs), now,
                out bool hours, out encounterPenaltyNextUpdate);
            string time = string.Format(CultureInfo.InvariantCulture,
                GetSocialUiText(hours ? "FriendlyEncounterPenaltyHours" : "FriendlyEncounterPenaltyMinutes"), value);
            encounterPenaltyLabel.text = string.Format(CultureInfo.InvariantCulture, GetSocialUiText("FriendlyEncounterPenaltyLabel"),
                active.Count * FriendlyEncounterPenaltyPolicy.PointsPerKill, time);
        }
    }
}
