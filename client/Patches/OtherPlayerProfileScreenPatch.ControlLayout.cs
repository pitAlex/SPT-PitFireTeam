using EFT.UI;
using UnityEngine;

namespace pitTeam.Patches
{
    internal partial class OtherPlayerProfileScreenPatch
    {
        private static void ArrangeProfileControlRows()
        {
            Transform proficiencyRow = ProficiencyButtonRoot;
            Transform kitRow = EditLoadoutButtonRoot;
            RectTransform tacticsRow = LoadoutSelector as RectTransform;
            Transform editor = LoadoutSelector?.Find("Upper/pitFireTeam_LoadoutDropdownEditButton");
            if (tacticsRow == null || proficiencyRow == null || kitRow == null || editor == null || ProficiencyButton == null || EditLoadoutButton == null)
                return;

            // Keep the live controls and their callbacks; only change their hosts and geometry.
            PlaceProfileRowButton(ProficiencyButton.transform, tacticsRow, "Upper");
            PlaceProfileRowButton(editor, (RectTransform)proficiencyRow, "Upper");
            PlaceProfileRowButton(EditLoadoutButton.transform, (RectTransform)proficiencyRow, "Lower");
            proficiencyRow.name = "pitFireTeam_EquipmentButtons";
            ProficiencyButtonRoot = ProficiencyButton.transform;
            EditLoadoutButtonRoot = proficiencyRow;
            GameObject.Destroy(kitRow.gameObject);

            // Clothing, Proficiency/Tactic, equipment, then name/voice: four equally spaced rows.
            if (NicknameRenameButtonRoot is RectTransform nameRow)
                nameRow.anchoredPosition = tacticsRow.anchoredPosition + GetProfileControlRowOffset(tacticsRow, 2);
        }

        private static void PlaceProfileRowButton(Transform button, RectTransform row, string sectionName)
        {
            Transform section = row.Find(sectionName);
            InventoryClothingSelectionPanel panel = row.GetComponent<InventoryClothingSelectionPanel>();
            DropDownBox dropdown = (sectionName == "Upper" ? UpperDropdownField : LowerDropdownField)?.GetValue(panel) as DropDownBox;
            if (section == null || dropdown?.transform is not RectTransform source || button is not RectTransform target)
                return;
            foreach (Transform child in section) child.gameObject.SetActive(false);
            section.gameObject.SetActive(true);
            button.SetParent(section, false);
            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.sizeDelta = source.sizeDelta;
            target.anchoredPosition = source.anchoredPosition;
            target.localScale = Vector3.one;
            button.gameObject.SetActive(true);
        }
    }
}
