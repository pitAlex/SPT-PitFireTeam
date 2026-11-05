using EFT;
using pitTeam.Components;
using UnityEngine;
namespace pitTeam.GoingRogue.Combat;
// This fixture tests command admission. Real allocation/spacing runs in KnightCombat.
internal static class RoguePositionCoordinator
{
    internal static void Refresh(IEnumerable<BotFollowerPlayer> members){}
    internal static void Reset(){}
    internal static bool TryGetOrderSeed(BotOwner member,EnemyInfo enemy,out Vector3 point)
    {point=ExUsecCombatExtension.FindLeader(member)?.Position??default;return true;}
}
