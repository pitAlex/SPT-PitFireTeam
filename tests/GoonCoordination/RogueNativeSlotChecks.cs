using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.GoingRogue.Combat;
using static Program;

internal static class RogueNativeSlotChecks
{
    internal static void Run()
    {
        foreach(int rogues in new[]{0,1,2,3,8})
        {
            BossPlayers.Followers.Clear();KnightCombatLayer.Owned.Clear();TimeReset();
            var human=new pitAIBossPlayer();var enemy=new EnemyInfo{ProfileId="native-slot-target",Person=new Player()};
            BotOwner Add(WildSpawnType role)
            {var bot=new BotOwner();bot.Profile.Info.Settings.Role=role;bot.Memory.GoalEnemy=enemy;BossPlayers.Followers.Add(new(){Bot=bot,Leader=human});return bot;}
            var knight=Add(WildSpawnType.bossKnight);var pipe=Add(WildSpawnType.followerBigPipe);var bird=Add(WildSpawnType.followerBirdEye);
            for(int i=0;i<rogues;i++)Add(WildSpawnType.exUsec);
            knight.Boss.BossLogic=new BossKnight{_owner=knight,Publish=true};KnightCombatLayer.Owned.Add(knight);
            using(var coordinator=new KnightSquadCoordinator(knight))
            {
                coordinator.Refresh();
                Check(coordinator.Members.Count==2,"arbitrary Rogue count cannot overflow native Knight Goon slots: "+rogues);
                Check(KnightSquadCoordinator.TryGetMemberPosition(pipe,out _)&&KnightSquadCoordinator.TryGetMemberPosition(bird,out _),
                    "mixed squad retains both native Goon firing assignments: "+rogues);
            }
        }
        BossPlayers.Followers.Clear();KnightCombatLayer.Owned.Clear();
    }
    private static void TimeReset()=>UnityEngine.Time.time=500;
}
