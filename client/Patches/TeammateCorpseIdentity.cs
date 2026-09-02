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
            if (owner is not EFT.InventoryLogic.CorpseItemController corpseOwner)
            {
                return false;
            }

            return BossPlayers.GetFollowerByProfileId(corpseOwner.KilledProfileID)?.IsSquadMate == true;
        }
    }
}
