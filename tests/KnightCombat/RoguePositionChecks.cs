using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.GoingRogue.Combat;
using pitTeam.Modules;
using UnityEngine;

internal static class RoguePositionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(var commanderRole in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe})
        foreach(int count in new[]{1,2,3,6})
        {
            ExUsecCombatExtension.Reset();BossPlayers.Instance.Followers.Clear();KnightSquadCoordinator.Assigned=false;
            Time.time=200;FollowerCombatCommon.SupportedGround=true;
            var human=new pitAIBossPlayer();human.realPlayer.Position=new(-1000,0,0);
            var leader=new BotOwner();leader.Profile.Info.Settings.Role=commanderRole;
            BossPlayers.Instance.Followers[leader]=new(){Owner=leader,Boss=human};
            var enemy=new EnemyInfo{ProfileId="formation-target",Anchor=new(100,0,0)};leader.Memory.GoalEnemy=enemy;
            var bots=Enumerable.Range(0,count).Select(i=>{
                var bot=new BotOwner{Position=new(-60,0,-60-i*8)};bot.Profile.Info.Settings.Role=WildSpawnType.exUsec;
                bot.Memory.GoalEnemy=enemy;BossPlayers.Instance.Followers[bot]=new(){Owner=bot,Boss=human};return bot;
            }).ToArray();
            int probes=0;var blocked=new FollowerCombatCommon{AssignedPlanner=seed=>{probes++;return(false,seed);}};
            foreach(var bot in bots)check(!RoguePositionCoordinator.TryPlan(bot,enemy,blocked,out _,out _),"no admitted point cannot manufacture an assignment");
            check(probes<=12,"shared firing-point budget stays bounded across simultaneous Rogue requests");
            Time.time+=0.3f;
            var common=new FollowerCombatCommon{FiringPoint=true,AssignedPlanner=seed=>(true,new(-12,0,0))};
            int assigned=0;
            foreach(var bot in bots)
            {
                Time.time+=0.3f;
                if(RoguePositionCoordinator.TryPlan(bot,enemy,common,out _,out _))assigned++;
            }
            check(assigned==1,"one actual valid destination assigns only one Rogue despite distinct seeds: "+commanderRole+"/"+count);
            foreach(var bad in new[]{new Vector3(-3,0,0),new Vector3(float.NaN,0,0)})
                check(!RoguePositionCoordinator.TryReserve(bots[0],enemy,bad),"commander overlap and nonfinite destinations cannot be reserved");
            var first=bots.First(bot=>RoguePositionCoordinator.TryGetOrderSeed(bot,enemy,out var seed)&&seed.x==-12&&seed.z==0);
            first.IsDead=true;Time.time+=0.3f;
            check(!RoguePositionCoordinator.TryPlan(first,enemy,common,out _,out _),"dead members cannot acquire a new assignment");
            var replacement=new BotOwner{Position=new(-80,0,-80)};replacement.Profile.Info.Settings.Role=WildSpawnType.exUsec;
            replacement.Memory.GoalEnemy=enemy;BossPlayers.Instance.Followers[replacement]=new(){Owner=replacement,Boss=human};
            check(RoguePositionCoordinator.TryPlan(replacement,enemy,common,out _,out _),"dead Rogue releases its destination for the remaining unit");
            RoguePositionCoordinator.Reset();
            foreach(var bot in bots)bot.IsDead=false;
            common.AssignedPlanner=seed=>(true,seed);
            var destinations=new List<Vector3>();
            foreach(var bot in bots)
            {
                Time.time+=0.3f;
                if(RoguePositionCoordinator.TryPlan(bot,enemy,common,out var point,out _))destinations.Add(point);
            }
            check(destinations.Count>0,"valid distinct sectors can allocate a formation");
            for(int i=0;i<destinations.Count;i++)for(int j=i+1;j<destinations.Count;j++)
                check(Vector3.Distance(destinations[i],destinations[j])>=RoguePositionCoordinator.Spacing,"accepted final points retain shared spacing");
            RoguePositionCoordinator.Reset();Time.time+=0.3f;
            common.AssignedPlanner=seed=>(true,new(-12,0,0));
            FollowerCombatCommon.SupportedGround=false;
            check(!RoguePositionCoordinator.TryPlan(bots[0],enemy,common,out _,out _),"NavMesh-style admission without supporting ground cannot assign");
            FollowerCombatCommon.SupportedGround=true;
            var ally=new BotOwner{Position=new(-12,0,0)};BossPlayers.Instance.Followers[ally]=new(){Owner=ally,Boss=human};Time.time+=0.3f;
            check(!RoguePositionCoordinator.TryPlan(bots[0],enemy,common,out _,out _),"other ally current position blocks crowding");
            ally.Position=new(80,0,-80);ally.Mover.HasPathAndNoComplete=true;ally.GoToSomePointData.SetPoint(new(-12,0,0));Time.time+=0.3f;
            check(!RoguePositionCoordinator.TryPlan(bots[0],enemy,common,out _,out _),"another ally planned destination is reserved too");
            ally.Mover.HasPathAndNoComplete=false;leader.IsDead=true;Time.time+=0.3f;
            check(!RoguePositionCoordinator.TryPlan(bots[0],enemy,common,out _,out _),"dead commander cannot keep allocating points");
        }
        ExUsecCombatExtension.Reset();BossPlayers.Instance.Followers.Clear();FollowerCombatCommon.SupportedGround=true;
    }
}
