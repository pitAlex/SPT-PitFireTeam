using System;
using System.Collections.Generic;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;

namespace pitTeam.Modules
{
    /// <summary>A replayable edit contains identities and addresses, never clone item references.</summary>
    internal sealed class GearSwapEdit
    {
        private enum Kind { Move, Swap, Split, Merge, Transfer, Fold }
        private Kind _kind;
        private string _item, _other, _created;
        private Address _to, _toOther;
        private int _count;
        private bool _fold;

        internal void ValidateProvenance(HashSet<string> playerOwned)
        {
            // A single merged item ID cannot express two different post-raid ownership rules.
            if ((_kind == Kind.Merge || _kind == Kind.Transfer) &&
                playerOwned.Contains(_item) != playerOwned.Contains(_other))
                throw new InvalidOperationException("Keep player-owned and follower-owned stacks separate.");
        }

        internal void UpdateProvenance(HashSet<string> playerOwned)
        {
            if (_kind == Kind.Split && playerOwned.Contains(_item)) playerOwned.Add(_created);
            if ((_kind == Kind.Merge || _kind == Kind.Transfer) && playerOwned.Contains(_item)) playerOwned.Add(_other);
        }

        internal static GearSwapEdit Capture(IOperationResult result)
        {
            if (result is IPossibleDestroyResult destroy && destroy.ItemsDestroyRequired)
                throw new InvalidOperationException("Swap Gear never destroys items.");
            switch (result)
            {
                case MoveResult move:
                    return new GearSwapEdit { _kind = Kind.Move, _item = move.Item.Id, _to = new Address(move.To) };
                case SwapResult swap:
                    return new GearSwapEdit { _kind = Kind.Swap, _item = swap.Item.Id, _other = swap.Item2.Id,
                        _to = new Address(swap.To), _toOther = new Address(swap.To2) };
                case SplitResult split:
                    return new GearSwapEdit { _kind = Kind.Split, _item = split.Item.Id, _created = split.ResultItem.Id,
                        _count = split.Count, _to = new Address(split.To) };
                case MergeResult merge:
                    return new GearSwapEdit { _kind = Kind.Merge, _item = merge.Item.Id, _other = merge.TargetItem.Id };
                case TransferResult transfer:
                    return new GearSwapEdit { _kind = Kind.Transfer, _item = transfer.Item.Id,
                        _other = transfer.TargetItem.Id, _count = transfer.Count };
                case FoldResult fold:
                    return new GearSwapEdit { _kind = Kind.Fold, _item = fold.Foldable.Item.Id, _fold = fold.NewValue };
                default:
                    throw new InvalidOperationException("Unsupported staged operation: " + result.GetType().Name);
            }
        }

        internal IOperationResult Execute(Dictionary<string, Item> items, InventoryController controller,
            Func<Item, bool> canEdit, Func<ItemAddress, bool> canPlace)
        {
            Item item = items[_item];
            Item other = _other == null ? null : items[_other];
            ItemAddress to = _to?.Resolve(items);
            ItemAddress toOther = _toOther?.Resolve(items);
            if (!canEdit(item) || (other != null && !canEdit(other)) ||
                (to != null && !canPlace(to)) || (toOther != null && !canPlace(toOther)))
                throw new InvalidOperationException("Edit touches hidden or foreign inventory.");

            OperationResult result;
            switch (_kind)
            {
                case Kind.Move: result = ItemManipulator.Move(item, to, controller, false); break;
                case Kind.Swap: result = ItemManipulator.Swap(item, to, other, toOther, controller, false); break;
                case Kind.Split:
                    result = ItemManipulator.SplitExact(item, _count, to, controller, new FixedId(_created), false);
                    break;
                case Kind.Merge: result = ItemManipulator.Merge(item, other, controller, false); break;
                case Kind.Transfer: result = ItemManipulator.TransferExact(item, _count, other, controller, false); break;
                case Kind.Fold: result = ItemManipulator.Fold(item.GetItemComponent<FoldableComponent>(), _fold, false); break;
                default: throw new InvalidOperationException("Unknown gear edit.");
            }
            if (result.Failed) throw new InvalidOperationException(result.Error.ToString());
            if (result.Value is IPossibleDestroyResult destroy && destroy.ItemsDestroyRequired)
            {
                result.Value.RollBack();
                throw new InvalidOperationException("Operation would destroy items.");
            }
            if (result.Value is SplitResult split) items[split.ResultItem.Id] = split.ResultItem;
            return result.Value;
        }

        private sealed class Address
        {
            private readonly string _parent, _container;
            private readonly ItemAddressDescriptor _descriptor;
            internal Address(ItemAddress address)
            {
                _parent = address.Container.ParentItem.Id;
                _container = address.Container.ID;
                _descriptor = address.ToDescriptor();
            }
            internal ItemAddress Resolve(Dictionary<string, Item> items) =>
                ((CompoundItem)items[_parent]).GetContainer(_container).CreateItemAddress(_descriptor);
        }

        private sealed class FixedId : IDatabaseIdGenerator
        {
            private readonly MongoID _id;
            internal FixedId(string id) { _id = id; }
            public MongoID NextId => _id;
            public void RollBack() { }
        }
    }
}
