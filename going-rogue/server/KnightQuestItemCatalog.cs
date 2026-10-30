using pitTeam.GoingRogue.Quests;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.GoingRogue.Server;

internal static class KnightQuestItemCatalog
{
    internal const string NativeLauncher = "6275303a9f372d6ea97f9ec7";

    internal static void Register(CustomItemService items, TemplateTable templates, GlobalTable globals,
        LocaleTable locales, JsonUtil json)
    {
        var preset = KnightQuestModule.Read<Preset>(json, "KnightLauncherPreset.json");
        if (templates.Items.ContainsKey(new(KnightQuestPolicy.StolenLauncher)) || globals.ItemPresets.ContainsKey(preset.Id) ||
            preset.Encyclopedia != new MongoId(KnightQuestPolicy.StolenLauncher) ||
            preset.Items.Count != 5 || preset.Items.Select(item => item.Id).Distinct().Count() != preset.Items.Count ||
            preset.Items.Count(item => item.Template == new MongoId(KnightQuestPolicy.StolenLauncher)) != 1 ||
            preset.Items.Any(item => item.Template != new MongoId(KnightQuestPolicy.StolenLauncher) && !templates.Items.ContainsKey(item.Template)))
            throw new InvalidDataException("Invalid or duplicate stolen-launcher item/preset.");

        var translations = KnightQuestModule.ReadLocales(json);
        LocaleDetails Text(string locale)
        {
            var text = translations.GetValueOrDefault(locale) ?? translations["en"];
            return new()
            {
                Name = text[KnightQuestPolicy.StolenLauncher + " Name"],
                ShortName = text[KnightQuestPolicy.StolenLauncher + " ShortName"],
                Description = text[KnightQuestPolicy.StolenLauncher + " Description"]
            };
        }
        // Clone the current game's weapon definition, retaining modern fields/filters.
        // Only the legacy quest-item differences are imposed on this independent item.
        var result = items.CreateItemFromClone(new()
        {
            NewId = new(KnightQuestPolicy.StolenLauncher), ItemTplToClone = new(NativeLauncher),
            ParentId = templates.Items[new(NativeLauncher)].Parent,
            NewItemName = "weapon_milkor_m32a1_bigpipe_quest",
            OverrideProperties = new TemplateItemProperties
            {
                QuestItem = true, IsUngivable = true, IsUnbuyable = true,
                Width = 4, Height = 1, ExaminedByDefault = true
            },
            AddToFleaPriceDb = false, AddToWeaponShelf = false,
            HandbookParentId = "5b5f79d186f774093f2ed3c2", HandbookPriceRoubles = 185000,
            Locales = new[] { "en" }.Concat(locales.Global.Keys).Distinct()
                .ToDictionary(locale => locale, Text)
        });
        if (!result.Success) throw new InvalidDataException(string.Join("; ", result.Errors));
        globals.ItemPresets.Add(preset.Id, preset);
    }
}
