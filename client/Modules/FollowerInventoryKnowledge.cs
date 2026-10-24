using System;
using EFT.InventoryLogic;

namespace pitTeam.Modules
{
    internal static class FollowerInventoryKnowledge
    {
        internal static bool ShouldTreatItemExamined(InventoryController controller, Item item)
        {
            if (controller == null || item == null) return false;
            try
            {
                var follower = BossPlayers.GetFollowerByProfileId(controller.Profile?.ProfileId);
                var player = follower?.GetBot()?.GetPlayer;
                if (player == null || !player.IsAI || !ReferenceEquals(player.InventoryController, controller)) return false;

                var equipment = controller.Inventory?.Equipment;
                if (equipment == null) return false;
                for (Item current = item; current != null;)
                {
                    if (ReferenceEquals(current, equipment)) return true;
                    Item parent = current.Parent?.Container?.ParentItem;
                    if (ReferenceEquals(parent, current)) break;
                    current = parent;
                }
            }
            catch
            {
                // A transient/detached inventory view keeps the native examination result.
            }
            return false;
        }
    }
}
