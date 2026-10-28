using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;

namespace pitTeam.Modules
{
    internal sealed partial class TeammateGearSwap
    {
        internal readonly bool CommandLoot;
        private static EftGamePlayerOwner _commandOwner;
        private Item _commandSource, _commandSourceDraft;
        private IItemOwner _commandSourceOwner;
        private string _commandSourceBefore;
        private Func<bool> _commandAvailable;
        private bool _commandBody;

        // Called at the source, after the ordinary command's approach/search delay. The
        // source and recipient are cloned together; nothing is first stuffed into cargo.
        internal static bool TryCommandLoot(BotOwner bot, Item source, IEnumerable<Item> roots,
            bool body, Func<bool> available, Action<bool> completed)
        {
            var owner = _commandOwner;
            var follower = BossPlayers.Instance?.GetFollower(bot);
            if (Current != null || owner == null || follower == null || follower.IsSquadMate ||
                (body && !follower.CanHandleBodyContainerLootCommands) || source == null ||
                UnsafeReason(owner, bot, follower, false, giving: true, commandLoot: true) != null) return false;
            var offering = roots.Where(i => i != null).Distinct().ToArray();
            if (offering.Length == 0) return false;
            _ = RunCommandLoot(owner, bot, follower, source, offering, body, available, completed);
            return true;
        }

        private static async Task RunCommandLoot(EftGamePlayerOwner owner, BotOwner bot,
            Components.BotFollowerPlayer follower, Item source, Item[] offering, bool body,
            Func<bool> available, Action<bool> completed)
        {
            TeammateGearSwap session = null;
            bool committed = false;
            try
            {
                session = new TeammateGearSwap(owner, bot, follower, giving: true, commandLoot: true);
                Current = session;
                session._commandBody = body;
                session._commandAvailable = available;
                session.InitializeCommandSource(source, offering);
                await session.Apply();
                committed = session._committed;
            }
            catch (Exception ex) { Logger.LogError("[CommandGear] Staged recruit loot failed: " + ex); }
            finally
            {
                try { session?.Close(); }
                catch (Exception ex) { Logger.LogError("[CommandGear] Cleanup failed: " + ex); }
            }
            try { completed?.Invoke(committed); }
            catch (Exception ex) { Logger.LogError("[CommandGear] Completion observer failed: " + ex); }
        }

        private void InitializeCommandSource(Item source, Item[] offering)
        {
            _commandSource = source;
            _commandSourceOwner = source.Owner;
            _commandSourceBefore = Snapshot(source);
            _commandSourceDraft = Clone(source);
            _commandSourceDraft.CurrentAddress = null;
            new ItemController(_commandSourceDraft, source.Owner?.ID ?? source.Id, "command gear draft", false);
            var originals = Tree(source).ToDictionary(i => (string)i.Id);
            var copies = Tree(_commandSourceDraft).ToDictionary(i => (string)i.Id);
            if (!originals.Keys.OrderBy(i => i).SequenceEqual(copies.Keys.OrderBy(i => i)) ||
                copies.Any(pair => ReferenceEquals(pair.Value, originals[pair.Key])))
                throw new InvalidOperationException("Command source clone did not preserve independent exact identities.");
            if (Snapshot(_commandSourceDraft) != _commandSourceBefore)
                throw new InvalidOperationException("Command source clone did not preserve item state.");
            foreach (var pair in copies) _draftItems.Add(pair.Key, pair.Value);
            // The source draft is not the player's inventory or the visible offer grid.
            // Use the planner's clone-only admission while moving its offered roots.
            _planningDonation = true;
            try
            {
                foreach (var root in offering)
                {
                    if (!originals.TryGetValue(root.Id, out var original) || !ReferenceEquals(original, root))
                        throw new InvalidOperationException("Offering escaped the commanded source.");
                    var clone = copies[root.Id];
                    var destination = Donation.Grids[0].FindLocationForItem(clone);
                    if (destination == null) throw new InvalidOperationException("Command offer staging is full.");
                    var move = ItemManipulator.Move(clone, destination, BotDraft, false);
                    if (move.Failed) throw new InvalidOperationException(move.Error.ToString());
                    _edits.Add(GearSwapEdit.Capture(move.Value));
                    _playerOwnedIds.UnionWith(Tree(clone).Select(i => (string)i.Id));
                }
            }
            finally { _planningDonation = false; }
            PreserveBorrowedGearOwnership();
        }

        private string CommandUnsafeReason()
        {
            string reason = UnsafeReason(Owner, Bot, Follower, Applying, giving: true, commandLoot: true);
            if (reason != null) return reason;
            if (_commandBody && !Follower.CanHandleBodyContainerLootCommands)
                return Follower.AllowsBodyContainerLootCommands ? "spawnedTeammateAvailable" : "companionPolicy";
            return _commandAvailable?.Invoke() == false ? "commandSourceUnavailable" : null;
        }

        private void ValidateCommandSource()
        {
            if (!CommandLoot) return;
            if (_commandSource == null || !ReferenceEquals(_commandSource.Owner, _commandSourceOwner) ||
                !ReferenceEquals(_commandSourceOwner?.RootItem, _commandSource) ||
                Snapshot(_commandSource) != _commandSourceBefore)
                throw new GearSwapValidationException("SwapGearStale");
        }

        private void AddCommandSource(Dictionary<string, Item> items)
        {
            if (CommandLoot)
                foreach (var item in Tree(_commandSource)) items.Add(item.Id, item);
        }

        private void ValidateCommandReplay()
        {
            if (CommandLoot && Snapshot(_commandSource) != Snapshot(_commandSourceDraft))
                throw new InvalidOperationException("Command source replay did not match its draft.");
        }
    }
}
