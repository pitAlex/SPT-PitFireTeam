using System.Collections.Generic;
using Comfort.Common;
using EFT;
using pitTeam.Modules;

namespace pitTeam.GoingRogue;

internal static class RogueEnemyReports
{
    internal const float Range = 150f;

    internal static void Update()
    {
        var world = Singleton<GameWorld>.Instance;
        if (GameplayModeAddons.Current != GameplayMode.Allegiance || RogueAlliance.Friendship("exUsec") != true ||
            world?.MainPlayer == null || world.AllAlivePlayersList == null) return;
        foreach (var follower in BossPlayers.GetFollowersByBoss(world.MainPlayer.ProfileId))
        {
            var goon = follower?.GetBot();
            var enemy = goon?.Memory?.GoalEnemy?.Person;
            if (goon == null || goon.IsDead || goon.BotState != EBotState.Active ||
                !RogueAlliancePolicy.IsGoon(goon.Profile?.Info?.Settings?.Role.ToString()) ||
                enemy?.HealthController?.IsAlive != true || goon.BotsGroup?.IsEnemy(enemy) != true) continue;
            var groups = new HashSet<BotsGroup>();
            foreach (var person in world.AllAlivePlayersList)
            {
                var rogue = person?.AIData?.BotOwner;
                var group = rogue?.BotsGroup;
                if (rogue == null || rogue.IsDead || rogue.BotState != EBotState.Active ||
                    rogue.Profile?.Info?.Settings?.Role != WildSpawnType.exUsec || BossPlayers.IsFollower(rogue) ||
                    group == null || group.IsEnemy(world.MainPlayer) ||
                    (rogue.Position - goon.Position).sqrMagnitude > Range * Range || !groups.Add(group) || group.IsEnemy(enemy)) continue;
                group.AddEnemy(enemy, EBotEnemyCause.addCauseGroup);
            }
        }
    }
}
