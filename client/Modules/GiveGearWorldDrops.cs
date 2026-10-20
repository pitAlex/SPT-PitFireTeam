using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;

namespace pitTeam.Modules
{
    /// <summary>
    /// Prepare world owners synchronously while Apply is protected. Do not expose physics,
    /// publish throw callbacks or yield until every root has a real world owner.
    /// </summary>
    internal sealed class GiveGearWorldDrops
    {
        private readonly GameWorld _world;
        private readonly Player _bot;
        private readonly List<LootItem> _loot = new List<LootItem>();
        private readonly List<IOperationResult> _removed = new List<IOperationResult>();
        private readonly List<Item> _detached = new List<Item>();
        private bool _committed, _rolledBack;

        private GiveGearWorldDrops(Player bot)
        {
            _world = Singleton<GameWorld>.Instance;
            _bot = bot;
        }

        internal static GiveGearWorldDrops Prepare(Player bot, Item[] roots)
        {
            var batch = new GiveGearWorldDrops(bot);
            try
            {
                foreach (Item item in roots) batch.Prepare(item);
                return batch;
            }
            catch
            {
                batch.Rollback();
                throw;
            }
        }

        private void Prepare(Item item)
        {
            if (_world == null || _bot == null || _bot.Destroyed) throw new InvalidOperationException("Raid ended before donation drop.");
            // Resolve the prefab before detaching the real item. Null/failed assets cannot lose it.
            GameObject prefab = Singleton<ObjectsFactory>.Instance.CreateLootPrefab(item, Player.GetVisibleToCamera(_bot), null);
            if (prefab == null) throw new InvalidOperationException("Donation loot prefab unavailable.");
            prefab.SetActive(false);
            LootItem loot = null;
            try
            {
                var discard = ItemManipulator.Discard(item, _bot.InventoryController, false);
                if (discard.Failed) throw new InvalidOperationException(discard.Error.ToString());
                if (discard.Value is IPossibleDestroyResult possible && possible.ItemsDestroyRequired)
                {
                    discard.Value.RollBack();
                    throw new InvalidOperationException("Donation drop would destroy an item.");
                }
                _removed.Add(discard.Value);
                _detached.Add(item);
                new ItemController(item, item.Id, item.ShortName, true);
                loot = _world.CreateLootWithRigidbody(prefab, item, item.ShortName, false, null,
                    out BoxCollider collider, true, true, 0f);
                if (loot == null) throw new InvalidOperationException("Donation world loot creation failed.");
                _loot.Add(loot);
                loot.gameObject.SetActive(false);
                loot.transform.SetPositionAndRotation(_bot.Position + Vector3.up * 0.65f, Quaternion.identity);
                loot.LastOwner = _bot;
                loot.CacheParameters();
            }
            catch
            {
                // Creation may register a LootItem before a later native initialization throws.
                var partial = loot ?? prefab.GetComponent<LootItem>();
                if (partial != null && !_loot.Contains(partial)) _loot.Add(partial);
                if (partial == null) UnityEngine.Object.Destroy(prefab);
                throw;
            }
        }

        internal void Rollback()
        {
            if (_committed) throw new InvalidOperationException("Committed world drops cannot be rolled back.");
            if (_rolledBack) return;
            var errors = new List<Exception>();
            for (int index = _loot.Count - 1; index >= 0; index--)
                try { _world.DestroyLoot(_loot[index]); }
                catch (Exception ex)
                {
                    // Native observers may throw before DestroyLoot finishes. Complete its
                    // registry cleanup for this still-hidden, unpublished object only.
                    var loot = _loot[index];
                    _world.LootList.Remove(loot);
                    _world.ItemOwners.Remove(loot.ItemOwner);
                    _world.LootItems.Remove(loot.GetNetId());
                    loot.gameObject.SetActive(false);
                    loot.Kill();
                    Logger.LogError("[GiveGear] Unpublished loot observer failed during rollback: " + ex);
                }
            for (int index = _removed.Count - 1; index >= 0; index--)
                try
                {
                    // New ItemController assigns a root address. Remove that dead world-owner
                    // address before native ContainerRemoveResult.AddWithoutRestrictions.
                    _detached[index].CurrentAddress = null;
                    _removed[index].RollBack();
                }
                catch (Exception ex) { errors.Add(ex); }
            if (errors.Count > 0) throw new AggregateException("Donation world-owner rollback failed.", errors);
            _rolledBack = true;
        }

        internal void Commit()
        {
            if (_committed) return;
            if (_rolledBack) throw new InvalidOperationException("Rolled-back world drops cannot be committed.");
            _committed = true;
            var errors = new List<Exception>();
            foreach (var loot in _loot)
            {
                // Ownership is already final: an observer failure must not restore a second copy.
                try
                {
                    loot.gameObject.SetActive(true);
                    if (loot.RigidBody != null) loot.RigidBody.velocity = Vector3.up;
                    _world.OnThrowItem?.Invoke(loot);
                }
                catch (Exception ex) { errors.Add(ex); }
            }
            foreach (var removed in _removed)
            {
                try { removed.RaiseEvents(_bot.InventoryController, CommandStatus.Begin); }
                catch (Exception ex) { errors.Add(ex); }
                try { removed.RaiseEvents(_bot.InventoryController, CommandStatus.Succeed); }
                catch (Exception ex) { errors.Add(ex); }
            }
            if (errors.Count > 0) Logger.LogError(new AggregateException("Donation drop publication failed after commitment.", errors));
        }
    }
}
