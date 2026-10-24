using System.Reflection;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services.InRaid;

namespace pitTeam.Server.Services;

// Scope receipt identity and ammo-box preservation to cargo mail only.
// Native co-op mail retains its wording/reward; admitted cargo joins before notification.
[Injectable(TypePriority = OnLoadOrder.Preload + 2)]
public sealed class FriendlyScavCargoHooks(ISptLogger<FriendlyScavCargoHooks> logger) : IOnLoad
{
    private sealed record MessageContext(SendMessageDetails Details, MongoId Id,
        bool PreserveCargoAmmo = true, FriendlyScavCargoService.CoopCargoDelivery? Cargo = null)
    {
        internal Message? CreatedMessage { get; set; }
    }
    private sealed record RaidContext(MongoId SessionId, EndLocalRaidRequestData Request);
    private static readonly AsyncLocal<MessageContext?> MessageId = new();
    private static readonly AsyncLocal<RaidContext?> NativeRaid = new();
    private static readonly AsyncLocal<bool> NativeGift = new();
    private static FriendlyScavCargoService? cargoService;
    private static MethodInfo? processItems;
    private static ISptLogger<FriendlyScavCargoHooks>? log;
    internal static bool Enabled { get; private set; }
    internal static void Register(FriendlyScavCargoService service) => cargoService = service;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log = logger;
        var harmony = new Harmony("xyz.pit.fireteam.server.scav-cargo");
        try
        {
            var message = Require(typeof(MailSendService), "CreateDialogMessage", [typeof(MongoId), typeof(SendMessageDetails)], typeof(Message));
            var ammo = Require(typeof(ItemHelper), "AddCartridgesToAmmoBox", [typeof(List<Item>), typeof(TemplateItem)], typeof(void));
            processItems = Require(typeof(MailSendService), "ProcessItemsBeforeAddingToMail", [typeof(MessageType?), typeof(SendMessageDetails)], typeof(MessageItems));
            var send = Require(typeof(MailSendService), "SendMessageToPlayer", [typeof(SendMessageDetails)], typeof(void));
            var end = Require(typeof(LocationLifecycleService), "EndLocalRaidAsync", [typeof(MongoId), typeof(EndLocalRaidRequestData), typeof(CancellationToken)], typeof(Task));
            var gift = Require(typeof(LocationLifecycleService), "SendCoopTakenFenceMessage", [typeof(MongoId)], typeof(void));
            harmony.Patch(message, postfix: Method(nameof(MessagePostfix)));
            harmony.Patch(ammo, prefix: Method(nameof(AmmoPrefix)));
            harmony.Patch(processItems, postfix: Method(nameof(ItemsPostfix)));
            harmony.Patch(send, prefix: Method(nameof(SendPrefix)), finalizer: Method(nameof(ScopeFinalizer)));
            harmony.Patch(end, prefix: Method(nameof(RaidPrefix)), finalizer: Method(nameof(ScopeFinalizer)));
            harmony.Patch(gift, prefix: Method(nameof(GiftPrefix)), finalizer: Method(nameof(ScopeFinalizer)));
            Enabled = true;
            logger.Info("[ScavCargo] Cargo mail receipt hooks enabled.");
        }
        catch (Exception ex)
        {
            harmony.UnpatchSelf();
            Enabled = false;
            logger.Error($"[ScavCargo] Cargo mail receipts unavailable; cargo reports will be rejected: {ex}");
        }
        return Task.CompletedTask;
    }

    private static MethodInfo Require(Type owner, string name, Type[] arguments, Type result)
    {
        var method = owner.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, arguments, null);
        if (method == null || method.ReturnType != result) throw new MissingMethodException(owner.FullName, name);
        return method;
    }
    private static HarmonyMethod Method(string name) => new(typeof(FriendlyScavCargoHooks).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!);

    private static void MessagePostfix(SendMessageDetails messageDetails, Message __result)
    {
        var current = MessageId.Value;
        if (current != null && ReferenceEquals(current.Details, messageDetails))
        {
            __result.Id = current.Id;
            current.CreatedMessage = __result;
        }
    }
    // Stock gift mail fills empty ammo boxes. A cargo return must preserve the empty box.
    private static bool AmmoPrefix() => MessageId.Value?.PreserveCargoAmmo != true;

    private static void RaidPrefix(MongoId sessionId, EndLocalRaidRequestData request, out IDisposable __state) =>
        __state = UseNativeRaid(sessionId, request);

    // An async method captures this ExecutionContext before returning its Task. Restoring
    // the caller's scope leaves its continuations isolated without retaining caller state.
    internal static IDisposable UseNativeRaid(MongoId sessionId, EndLocalRaidRequestData request)
    {
        var previous = NativeRaid.Value;
        NativeRaid.Value = new(sessionId, request);
        return new Restore(() => NativeRaid.Value = previous);
    }

    private static void GiftPrefix(out IDisposable __state)
    {
        bool previous = NativeGift.Value;
        NativeGift.Value = true;
        __state = new Restore(() => NativeGift.Value = previous);
    }

    private static void ScopeFinalizer(IDisposable? __state) => __state?.Dispose();

    private static bool SendPrefix(SendMessageDetails messageDetails, out IDisposable? __state)
    {
        __state = null;
        var raid = NativeRaid.Value;
        if (!NativeGift.Value || raid == null || messageDetails.RecipientId != raid.SessionId ||
            !string.Equals(messageDetails.Trader, Traders.FENCE, StringComparison.Ordinal)) return true;
        FriendlyScavCargoService.CoopCargoDelivery? cargo;
        try { cargo = cargoService?.PrepareCoopCargo(raid.SessionId, raid.Request); }
        catch (Exception ex)
        {
            log?.Warning($"[ScavCargo] Native gift retains its normal delivery; cargo preparation will retry separately: {ex}");
            return true;
        }
        if (cargo == null) return true;
        if (cargo.Replay) return false;
        var previous = MessageId.Value;
        MessageId.Value = new(messageDetails, cargo.MessageId, false, cargo);
        __state = new Restore(() => MessageId.Value = previous);
        return true;
    }

    private static void ItemsPostfix(MailSendService __instance, MessageType? dialogType,
        SendMessageDetails messageDetails, MessageItems __result)
    {
        var current = MessageId.Value;
        if (current?.Cargo == null || !ReferenceEquals(current.Details, messageDetails)) return;
        try
        {
            var details = new SendMessageDetails { Trader = Traders.FENCE, Items = current.Cargo.Items };
            MessageItems cargo;
            // Gift processing has already used all native ammo-box rules. Only the second,
            // cargo-only pass suppresses auto-filling; native cloning/remapping still applies.
            using (UseMessageId(details, current.Id))
                cargo = (MessageItems)processItems!.Invoke(__instance, [dialogType, details])!;
            if (cargo.Data?.Count != current.Cargo.Items.Count)
                throw new IOException("Native co-op mail did not preserve the complete Scav cargo.");
            var stash = __result.Data?.Count > 0 ? __result.Stash : cargo.Stash;
            foreach (var item in cargo.Data)
                if (item.ParentId == cargo.Stash.ToString()) item.ParentId = stash.ToString();
            var combined = new List<Item>(__result.Data ?? []);
            combined.AddRange(cargo.Data);
            // Commit the receipt before altering the native result. Failure leaves its
            // reward intact and the unconsumed cargo available to the end callback.
            current.Cargo.Prepared(combined.Count);
            __result.Stash = stash;
            __result.Data = combined;
        }
        catch (Exception ex)
        {
            if (current.CreatedMessage != null) current.CreatedMessage.Id = new MongoId();
            log?.Warning($"[ScavCargo] Native gift retains its normal delivery; cargo merge will retry separately: {ex}");
        }
    }
    internal static IDisposable UseMessageId(SendMessageDetails details, MongoId id)
    {
        var previous = MessageId.Value;
        MessageId.Value = new(details, id);
        return new Restore(() => MessageId.Value = previous);
    }
    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
