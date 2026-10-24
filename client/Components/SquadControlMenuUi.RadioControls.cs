using System;
using System.Linq;
using EFT.UI;
using EFT.UI.Matchmaker;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace pitTeam.Components
{
    internal partial class SquadControlMenuUi
    {
        private static Sprite fallbackRadioCircle;

        private static void SetLocationRadioSelected(Toggle radio, bool selected)
        {
            if (radio is AnimatedToggle animated) animated.ToggleSilent(selected);
            else radio.SetIsOnWithoutNotify(selected);
        }

        private Toggle CreateLocationRadioControl(RectTransform parent, string label, bool interactable,
            ToggleGroup group, bool selected, Action onSelect)
        {
            var host = new GameObject("pitFireTeam_LocationRadio", typeof(RectTransform), typeof(Image));
            host.SetActive(false);
            host.transform.SetParent(parent, false);
            Stretch(host.GetComponent<RectTransform>());
            var hitArea = host.GetComponent<Image>();
            hitArea.color = Color.clear;
            hitArea.raycastTarget = true;

            Toggle template = Resources.FindObjectsOfTypeAll<LocationConditionsPanel>()
                .Select(panel => panel._amTimeToggle ?? panel._pmTimeToggle).FirstOrDefault(toggle => toggle != null)
                ?? Resources.FindObjectsOfTypeAll<LocationConditionsPanelFactory>()
                    .Select(panel => panel._amTimeToggle ?? panel._pmTimeToggle).FirstOrDefault(toggle => toggle != null);
            Toggle radio = template != null ? Instantiate(template, host.transform, false)
                : CreateBasicToggle(host.GetComponent<RectTransform>());
            radio.name = "Radio";
            // Clone beneath an inactive host, then detach the native raid-time group and
            // replace its events before activation. Selecting a mod option cannot change raid time.
            radio.group = null;
            radio.onValueChanged = new Toggle.ToggleEvent();
            if (radio is AnimatedToggle animated) radio.onValueChanged.AddListener(animated.ToggleSilent);
            foreach (var text in radio.GetComponentsInChildren<TextMeshProUGUI>(true))
                text.gameObject.SetActive(false);
            var rect = (RectTransform)radio.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(28f, 28f);
            rect.localScale = Vector3.one;
            if (template == null) ConfigureFallbackRadio(radio);
            radio.gameObject.SetActive(true);
            radio.interactable = interactable;
            radio.group = group;
            SetLocationRadioSelected(radio, selected);
            radio.onValueChanged.AddListener(isOn => { if (isOn && radio.IsInteractable()) onSelect(); });

            if (!string.IsNullOrEmpty(label))
            {
                var text = CreateText("Label", label.ToUpperInvariant(), 18f, TextAlignmentOptions.MidlineLeft)
                    .GetComponent<TextMeshProUGUI>();
                text.transform.SetParent(host.transform, false);
                var textRect = (RectTransform)text.transform;
                Stretch(textRect);
                textRect.offsetMin = new Vector2(40f, 0f);
                text.raycastTarget = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
            }
            host.AddComponent<LocationRadioClickController>().Radio = radio;
            var canvas = host.AddComponent<CanvasGroup>();
            canvas.alpha = interactable ? 1f : 0.42f;
            canvas.interactable = canvas.blocksRaycasts = interactable;
            host.SetActive(true);
            return radio;
        }

        private static void ConfigureFallbackRadio(Toggle radio)
        {
            // Keep a circular radio if the location-screen prefab has not loaded yet.
            // This fallback also uses the same native Toggle/ToggleGroup selection contract.
            if (fallbackRadioCircle == null)
            {
                var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                var pixels = new Color[32 * 32];
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                        pixels[y * 32 + x] = new Color(1f, 1f, 1f,
                            Mathf.Clamp01(15.5f - new Vector2(x - 15.5f, y - 15.5f).magnitude));
                texture.SetPixels(pixels);
                texture.Apply(false, true);
                fallbackRadioCircle = Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f));
            }
            var background = (Image)radio.targetGraphic;
            background.sprite = fallbackRadioCircle;
            background.color = new Color(0.72f, 0.71f, 0.66f, 1f);
            var well = new GameObject("RadioWell", typeof(RectTransform), typeof(Image));
            well.transform.SetParent(background.transform, false);
            well.transform.SetAsFirstSibling();
            var wellRect = (RectTransform)well.transform;
            wellRect.anchorMin = wellRect.anchorMax = wellRect.pivot = new Vector2(0.5f, 0.5f);
            wellRect.sizeDelta = new Vector2(24f, 24f);
            var wellImage = well.GetComponent<Image>();
            wellImage.sprite = fallbackRadioCircle;
            wellImage.color = new Color(0.07f, 0.07f, 0.07f, 1f);
            wellImage.raycastTarget = false;
            var dot = (Image)radio.graphic;
            dot.sprite = fallbackRadioCircle;
            dot.color = background.color;
            dot.rectTransform.sizeDelta = new Vector2(14f, 14f);
            dot.raycastTarget = false;
        }

        private sealed class LocationRadioClickController : MonoBehaviour, IPointerClickHandler
        {
            public Toggle Radio;
            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left && Radio != null && Radio.IsInteractable())
                    Radio.isOn = true;
            }
        }
    }
}
