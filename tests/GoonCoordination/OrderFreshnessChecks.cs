using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.GoingRogue.Combat;
using UnityEngine;
using static Program;

internal static class OrderFreshnessChecks
{
    internal static void Run()
    {
        foreach(string invalidation in new[]{"age","leaderMoved","enemyMoved","unknown","playerHit","independentPlayerHit"})
        {
            BossPlayers.Followers.Clear();KnightCombatLayer.Owned.Clear();GoonPushLeadership.Reset();Time.time=100;
            var human=new pitAIBossPlayer();
            var knight=new BotOwner();knight.Profile.Info.Settings.Role=WildSpawnType.bossKnight;
            var pipe=new BotOwner();pipe.Profile.Info.Settings.Role=WildSpawnType.followerBigPipe;
            var enemy=new EnemyInfo{ProfileId="enemy",Person=new Player()};knight.Memory.GoalEnemy=pipe.Memory.GoalEnemy=enemy;
            var k=new BotFollowerPlayer{Bot=knight,Leader=human};var p=new BotFollowerPlayer{Bot=pipe,Leader=human};
            BossPlayers.Followers.Add(k);BossPlayers.Followers.Add(p);KnightCombatLayer.Owned.Add(knight);
            knight.Boss.BossLogic=new BossKnight{_owner=knight,Publish=true};
            using var coordinator=new KnightSquadCoordinator(knight);coordinator.Refresh();
            Check(KnightSquadCoordinator.TryGetMemberPosition(pipe,out _),"fresh native assignment is actionable");
            if(invalidation=="age")Time.time=108.01f;
            if(invalidation=="leaderMoved")knight.Position=new(8.1f,0,0);
            if(invalidation=="enemyMoved")enemy.Person.Position=new(10.1f,0,0);
            if(invalidation=="unknown")enemy.Known=false;
            if(invalidation is "playerHit" or "independentPlayerHit")human.Logic.IsHitted=true;
            if(invalidation=="independentPlayerHit")p.Independent=true;
            Check(KnightSquadCoordinator.TryGetMemberPosition(pipe,out _)==(invalidation=="independentPlayerHit"),
                "assignment validity follows tactical context, not an indefinite cache: "+invalidation);
            if(invalidation is "age" or "leaderMoved" or "enemyMoved")
            {
                Time.time+=1;coordinator.Refresh();
                Check(KnightSquadCoordinator.TryGetMemberPosition(pipe,out _),"expired geometry is refreshed through the native commander: "+invalidation);
            }
        }
        BossPlayers.Followers.Clear();KnightCombatLayer.Owned.Clear();GoonPushLeadership.Reset();
    }
}
