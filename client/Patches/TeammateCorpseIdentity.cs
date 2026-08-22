using EFT.InventoryLogic;
using pitTeam.Modules;

namespace pitTeam.Patches
{
    internal static class TeammateCorpseIdentity
    {
        public static bool IsTeammateCorpseEquipment(InventoryEquipment equipment)
        {
            return IsTeammateCorpseOwner(equipment?.Owner);
        }

        public static bool IsTeammateCorpseOwner(IItemOwner owner)
        {
            return owner is EFT.InventoryLogic.CorpseItemController corpseOwner &&
                   BossPlayers.IsFollowerProfileId(corpseOwner.KilledProfileID);
        }
    }
}
