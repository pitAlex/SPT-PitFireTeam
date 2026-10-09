using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Diz.Binding;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace pitTeam.Modules
{
    /// <summary>Bounded, read-only snapshots. Never invokes a weapon/decision provider or repairs state.</summary>
    internal sealed class GearSwapDiagnostics
    {
        private static readonly FieldInfo ItemInHands = AccessTools.Field(typeof(Player), "_itemInHands");
        private readonly string _trace = Guid.NewGuid().ToString("N").Substring(0, 8);
        private readonly Player _player;
        private readonly BotOwner _bot;

        internal GearSwapDiagnostics(Player player, BotOwner bot) { _player = player; _bot = bot; }

        internal void Write(string phase, string detail)
        {
            Logger.LogInfo($"[SwapGear][Trace] exchange={_trace} follower='{_bot.ProfileId}' phase={phase} {detail}");
        }

        internal void State(string phase)
        {
            try
            {
                Actor(phase, _player);
                Actor(phase, _bot.GetPlayer);
            }
            catch (Exception ex) { Write(phase, "actorsUnavailable=" + ex.Message); }
        }

        internal void Draft(string phase, InventoryEquipment player, InventoryEquipment follower)
        {
            try
            {
                Write(phase, "draftPlayer=[" + Slots(player) + "] draftFollower=[" + Slots(follower) + "]");
            }
            catch (Exception ex) { Write(phase, "draftUnavailable=" + ex.Message); }
        }

        internal void SnapshotMismatch(string actor, string actual, string expected)
        {
            if (actual == expected) return;
            try
            {
                Write("replay-mismatch", $"actor={actor} actualChars={actual.Length} expectedChars={expected.Length}");
                foreach (string difference in GearSwapSnapshotDiff.Describe(actual, expected))
                    Write("replay-mismatch", $"actor={actor} {difference}");
            }
            catch (Exception ex) { Write("replay-mismatch", $"actor={actor} comparisonUnavailable={ex.Message}"); }
        }

        private static string Slots(InventoryEquipment equipment) => string.Join(" | ",
            TeammateGearSwap.FirearmSlots.Select(slot => $"{slot}={Item(equipment.GetSlot(slot).ContainedItem)}"));

        internal void Edit(string phase, int index, GearSwapEdit edit, Dictionary<string, Item> items)
        {
            try { Write(phase, $"edit={index} {edit.ToDiagnosticString(items)}"); }
            catch (Exception ex) { Write(phase, $"edit={index} detailUnavailable={ex.Message}"); }
        }

        internal void Hands(string phase, Player actor, Item requested, IHandsController returned, bool success, string error)
        {
            try
            {
                Write(phase, $"actor='{actor.ProfileId}' requested={Item(requested)} " +
                    $"returned={returned?.GetType().Name ?? "none"} returnedItem={Item(returned?.Item)} " +
                    $"success={success} error='{error}'");
                Actor(phase, actor);
            }
            catch (Exception ex) { Write(phase, "callbackSnapshotUnavailable=" + ex.Message); }
        }

        internal void Actor(string phase, Player actor)
        {
            // Diagnostics must not interrupt Apply, callbacks, or cleanup even during teardown.
            try
            {
                if (actor == null) { Write(phase, "actor=destroyed"); return; }
                var controller = actor.HandsController;
                var inventory = actor.InventoryController;
                Item boundHands = (ItemInHands?.GetValue(actor) as BindableState<Item>)?.Value;
                string slots = string.Join(" | ", TeammateGearSwap.FirearmSlots.Select(slot =>
                {
                    var views = actor.PlayerBody?.SlotViews;
                    var view = views != null && views.ContainsKey(slot) ? views.GetByKey(slot) : null;
                    return $"{slot}=[{Item(inventory.Inventory.Equipment.GetSlot(slot).ContainedItem)} " +
                        $"bodyItem={Item(view?._item)} model={Model(view?.Model)} " +
                        $"load={view?.LoadingJob?.Status.ToString() ?? "none"}]";
                }));
                string events = string.Join(",", inventory.ActiveEvents.Select(e =>
                    $"{e.GetType().Name}:{e.Status}:{Item(e.Item)}"));
                string manager = "";
                if (ReferenceEquals(actor, _bot.GetPlayer))
                {
                    var weapons = _bot.WeaponManager;
                    var selector = weapons?.Selector;
                    manager = $" main={selector?._mainWeapon} last={selector?._lastEquipmentSlot} " +
                        $"changing={selector?.IsChanging} ready={selector?.IsWeaponReady} " +
                        $"currentInfo={Item(weapons?._currentWeaponInfo?.weapon)} shoot={Item(weapons?.ShootController?.Item)}";
                }
                Write(phase, $"actor='{actor.ProfileId}' hands={controller?.GetType().Name ?? "none"} " +
                    $"handsItem={Item(controller?.Item)} boundHands={Item(boundHands)} destroyed={controller?.Destroyed} " +
                    $"handsModel={Model((controller as Player.ItemHandsController)?._controllerObject)} " +
                    $"process={actor.ProcessStatus} inventoryChanging={inventory.IsChangingWeapon} " +
                    $"events=[{events}]{manager} slots={slots}");
            }
            catch (Exception ex) { Write(phase, "snapshotUnavailable=" + ex.Message); }
        }

        internal void Settled()
        {
            try
            {
                // Existing main-thread scheduler; two observations only, no retries or state changes.
                _bot.AITaskManager?.RegisterDelayedTask(_bot, 1f, () => State("closed+1s"));
                _bot.AITaskManager?.RegisterDelayedTask(_bot, 3f, () => State("closed+3s"));
            }
            catch (Exception ex) { Write("settled", "scheduleUnavailable=" + ex.Message); }
        }

        internal static string Item(Item item) => item == null ? "none" :
            $"{item.Id}/{item.TemplateId}@{Address(item.CurrentAddress)}";

        internal static string Address(ItemAddress address) => address == null ? "detached" :
            $"{address.Container.ParentItem?.Id}:{address.Container.ID}:{address}";

        private static string Model(GameObject model)
        {
            if (model == null) return "none";
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            return $"{model.name}#{model.GetInstanceID()}:self={model.activeSelf}:hierarchy={model.activeInHierarchy}" +
                $":renderers={renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy)}/{renderers.Length}" +
                $":parent={model.transform.parent?.name ?? "none"}";
        }
    }
}
