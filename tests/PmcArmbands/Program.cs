using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Microsoft.Extensions.DependencyInjection;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Spt.Config;
using Path = System.IO.Path;

internal static class Program
{
    private static int checks;
    private static BotBaseInventory generated = null!;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
    }

    public static async Task Main(string[] args)
    {
        string[] runtimes = args.Select(Path.GetFullPath).ToArray();
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            foreach (string runtime in runtimes)
            {
                string path = Path.Combine(runtime, name.Name + ".dll");
                if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            return null;
        };
        await Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task Run()
    {
        var config = (PmcConfig)RuntimeHelpers.GetUninitializedObject(typeof(PmcConfig));
        config.ForceArmband = new ForceArmbandSettings { Enabled = false, Usec = ItemTpl.ARMBAND_RED, Bear = ItemTpl.ARMBAND_BLUE };
        var registrations = new ServiceCollection();
        registrations.AddSingleton(Logger<FriendlyServerSettingsService>());
        registrations.AddSingleton(Logger<FriendlyPmcArmbandHooks>());
        registrations.AddSingleton(config);
        registrations.AddSingleton((LostOnDeathConfig)RuntimeHelpers.GetUninitializedObject(typeof(LostOnDeathConfig)));
        // Use SPT's actual registration path, including each production attribute's lifetime.
        var handler = new DependencyInjectionHandler(registrations);
        var register = typeof(DependencyInjectionHandler).GetMethod("RegisterComponent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var type in new[] { typeof(FriendlyServerSettingsService), typeof(FriendlyPmcArmbandHooks) })
            register.Invoke(handler, [type.GetCustomAttribute<Injectable>()!.InjectionType, type, type]);
        using var provider = registrations.BuildServiceProvider();
        void Apply(bool enabled) => typeof(FriendlyServerSettingsService)
            .GetMethod("ApplySettings", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(provider.GetRequiredService<FriendlyServerSettingsService>(), [new FriendlyServerSettingsRequest { PmcArmbands = enabled }]);
        var details = new BotGenerationDetails { IsPmc = true, Side = "Usec", Role = "pmcUSEC", RoleLowercase = "pmcusec" };
        var root = new MongoId();
        Item NewItem(MongoId template, string? parent = null, string? slot = null) =>
            new() { Id = new MongoId(), Template = template, ParentId = parent, SlotId = slot };
        BotBaseInventory Inventory(params Item[] extras) => new()
        {
            Equipment = root,
            Items = [new Item { Id = root, Template = new MongoId("55d7217a4bdc2d86028b456d") }, ..extras],
        };
        Item Band(MongoId template) => NewItem(template, root.ToString(), "ArmBand");
        Item EquippedBand(BotBaseInventory inv) => inv.Items!.Single(item => item.ParentId == root.ToString() && item.SlotId == "ArmBand");
        var normalize = typeof(FriendlyPmcArmbandHooks).GetMethod("EnforceInventory", BindingFlags.Static | BindingFlags.NonPublic)!;
        void Normalize(BotBaseInventory? inv, bool enabled = true) => normalize.Invoke(null, [inv, details, enabled]);

        var wrong = Band(ItemTpl.ARMBAND_RED);
        var child = NewItem(ItemTpl.ARMBAND_RED, wrong.Id.ToString(), "child");
        var grandchild = NewItem(ItemTpl.ARMBAND_RED, child.Id.ToString(), "grandchild");
        var backpack = NewItem(ItemTpl.ARMBAND_RED, root.ToString(), "Backpack");
        var carriedBand = NewItem(ItemTpl.ARMBAND_RED, backpack.Id.ToString(), "main");
        var inv = Inventory(grandchild, child, wrong, backpack, carriedBand);
        Normalize(inv);
        Check(EquippedBand(inv).Template == ItemTpl.ARMBAND_BLUE, "USEC gets blue");
        Check(EquippedBand(inv).Id == wrong.Id, "Replacement preserves equipped item identity");
        Check(!inv.Items!.Contains(child) && !inv.Items.Contains(grandchild), "Replacement removes unsorted descendant tree");
        Check(inv.Items.Contains(backpack) && inv.Items.Contains(carriedBand), "Other equipment and carried bands remain intact");
        var correct = EquippedBand(inv);
        Normalize(inv);
        Check(ReferenceEquals(correct, EquippedBand(inv)), "Repeated normalization preserves a correct band");
        details.Side = "BEAR";
        inv = Inventory(); Normalize(inv);
        Check(EquippedBand(inv).Template == ItemTpl.ARMBAND_RED && inv.Items!.Count == 2, "Missing BEAR band is added under equipment root");
        inv = Inventory(Band(ItemTpl.ARMBAND_BLUE), Band(ItemTpl.ARMBAND_RED)); Normalize(inv);
        Check(inv.Items!.Count == 2 && EquippedBand(inv).Template == ItemTpl.ARMBAND_RED, "Duplicate equipped bands normalize to one");
        inv = Inventory(Band(ItemTpl.ARMBAND_BLUE));
        var before = inv.Items!.ToArray();
        Normalize(inv, false);
        Check(inv.Items.SequenceEqual(before), "Disabled leaves choices untouched");
        details.IsPmc = false; Normalize(inv);
        Check(inv.Items.SequenceEqual(before), "Non-PMC stays untouched");
        details.IsPmc = true; details.IsPlayerScav = true; Normalize(inv);
        Check(inv.Items.SequenceEqual(before), "Player Scav stays untouched even with PMC flag");
        details.IsPlayerScav = false; details.Side = "Savage"; Normalize(inv);
        Check(inv.Items.SequenceEqual(before), "Unknown faction stays untouched");
        details.Side = "Usec";
        Normalize(null);
        inv = new BotBaseInventory { Equipment = root, Items = [] }; Normalize(inv);
        Check(inv.Items!.Count == 0, "Missing equipment root cannot create orphan");

        // Exercise the real SPT Harmony boundary with a prefix that returns a replacement inventory.
        var native = typeof(BotInventoryGenerator).GetMethod(nameof(BotInventoryGenerator.GenerateInventory))!;
        var replacement = new Harmony("pitfireteam.tests.replacement-inventory");
        var hook = new Harmony("xyz.pit.fireteam.server.pmc-armbands");
        try
        {
            // Startup applies settings before SPT separately resolves the inventory hook.
            Apply(true);
            await provider.GetRequiredService<FriendlyPmcArmbandHooks>().OnLoadAsync(default);
            Check(Harmony.GetPatchInfo(native)?.Owners.Contains(hook.Id) == true, "Native hook installs on selected SPT runtime");
            replacement.Patch(native, prefix: new HarmonyMethod(typeof(Program).GetMethod(nameof(ReplacementPrefix), BindingFlags.Static | BindingFlags.NonPublic)!));
            var generator = (BotInventoryGenerator)RuntimeHelpers.GetUninitializedObject(typeof(BotInventoryGenerator));
            BotBaseInventory Generate() => generator.GenerateInventory(new MongoId(), new MongoId(), null!, details);
            Check(config.ForceArmband.Enabled && config.ForceArmband.Usec == ItemTpl.ARMBAND_BLUE && config.ForceArmband.Bear == ItemTpl.ARMBAND_RED,
                "Stock SPT configuration remains synchronized");
            config.ForceArmband.Enabled = false; config.ForceArmband.Usec = ItemTpl.ARMBAND_RED;
            generated = Inventory(Band(ItemTpl.ARMBAND_RED));
            var result = Generate();
            Check(ReferenceEquals(result, generated) && EquippedBand(result).Template == ItemTpl.ARMBAND_BLUE,
                "Separately resolved hook sees startup preference after skipped original despite shared config overwrite");
            Check(ReferenceEquals(provider.GetRequiredService<FriendlyServerSettingsService>(), provider.GetRequiredService<FriendlyServerSettingsService>()),
                "SPT resolutions share one applied settings service");
            Apply(false); generated = Inventory(Band(ItemTpl.ARMBAND_RED)); var unchanged = generated.Items!.ToArray();
            result = Generate();
            Check(result.Items!.SequenceEqual(unchanged), "Disabling applied setting takes effect on next generation");
            Apply(true); details.Side = "Bear"; generated = Inventory();
            Check(EquippedBand(Generate()).Template == ItemTpl.ARMBAND_RED, "Reenabling immediately adds missing BEAR band");
        }
        finally { replacement.UnpatchSelf(); hook.UnpatchSelf(); }
        Console.WriteLine($"PASS: {checks} PMC armband inventory/hook checks against {typeof(BotInventoryGenerator).Assembly.GetName()}.");
    }

    private static bool ReplacementPrefix(ref BotBaseInventory __result)
    {
        __result = generated;
        return false;
    }
    private static ISptLogger<T> Logger<T>() => DispatchProxy.Create<ISptLogger<T>, QuietLog>();
}

public class QuietLog : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.ReturnType == typeof(bool) ? false : null;
}
