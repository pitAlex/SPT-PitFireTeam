using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.GoingRogue.Combat;
using UnityEngine;
using static Program;

internal static class ExUsecHierarchyChecks
{
    internal static void Run()
    {
        foreach(bool hasKnight in new[]{false,true})
        foreach(bool hasPipe in new[]{false,true})
        foreach(bool hasBird in new[]{false,true})
        {
            ExUsecCombatExtension.Reset();GoonPushLeadership.Reset();BossPlayers.Followers.Clear();GoonCombatLayer.Owned.Clear();
            Time.time=100;var human=new pitAIBossPlayer();
            BotOwner Add(WildSpawnType role,pitAIBossPlayer boss=null)
            {var b=new BotOwner();b.Profile.Info.Settings.Role=role;BossPlayers.Followers.Add(new(){Bot=b,Leader=boss??human});return b;}
            var rogue=Add(WildSpawnType.exUsec);rogue.Position=new(100,0,0);
            var knight=hasKnight?Add(WildSpawnType.bossKnight):null;
            var pipe=hasPipe?Add(WildSpawnType.followerBigPipe):null;
            if(hasBird)Add(WildSpawnType.followerBirdEye);
            Add(WildSpawnType.bossKnight,new pitAIBossPlayer());
            var expected=knight??pipe;
            Check(ExUsecCombatExtension.FindLeader(rogue)==expected,"Knight/Pipe/solo hierarchy excludes Birdeye and foreign ownership");
            GoonCombatLayer.Owned.Add(rogue);ExUsecCombatExtension.RefreshAnchors();
            var follower=BossPlayers.Instance.GetFollower(rogue);
            Check(follower.CompanionLeader==expected&&follower.UnitAggression==(hasKnight?68f:60f)&&follower.UnitDetermination==(hasKnight?100f:50f),
                "Rogue baseline is 60 aggression or 68 with Knight, independent with Knight and saved determination otherwise");
            var target=new EnemyInfo{ProfileId="target",Person=new Player()};rogue.Memory.GoalEnemy=target;
            if(expected==null)
            {
                Check(GoonPushLeadership.CanAutonomousAdvance(rogue,target),"solo Rogue defers advancement to ordinary Rifleman policy");
                Check(GoonPushLeadership.Evaluate(new(rogue,human.realPlayer,target,1,(_,_)=>false))==null,
                    "solo Rogue push uses ordinary confidence evaluation");
                continue;
            }
            expected.Memory.GoalEnemy=target;
            if(pipe!=null){pipe.Memory.GoalEnemy=target;GoonPushLeadership.PublishAutonomousIntent(pipe,target.ProfileId,true,true);}
            GoonPushLeadership.PublishAutonomousIntent(expected,target.ProfileId,false);
            Check(!GoonPushLeadership.CanAutonomousAdvance(rogue,target),"selected commander hold wins over lower-priority advance");
            Check(!GoonPushLeadership.TryGetSearchLeader(rogue,target,out _),"hold does not fabricate a follow mission");
            // A detached member cannot borrow the player's local delegation while his commander holds.
            rogue.Position=new(0,0,0);expected.Position=new(100,0,0);
            follower.TeamSplitCombatActive=true;
            Check(!GoonPushLeadership.CanAutonomousAdvance(rogue,target),"Split suppresses player-proximity delegation against commander's hold");
            follower.TeamSplitCombatActive=false;
            Check(GoonPushLeadership.CanAutonomousAdvance(rogue,target),"Combined member retains existing player-proximity delegation");
            rogue.Position=new(100,0,0);expected.Position=new(0,0,0);
            GoonPushLeadership.PublishAutonomousIntent(expected,target.ProfileId,true,true);
            Check(GoonPushLeadership.TryGetSearchLeader(rogue,target,out var lead)&&lead==expected,"admitted commander search drives unit follow");
            Check(GoonPushLeadership.Evaluate(new(rogue,human.realPlayer,target,2,(actor,enemy)=>actor==expected&&enemy==target))==true,
                "player push shares selected commander's confidence verdict");
            follower.PushEnemyIssueSequence++;
            Check(GoonPushLeadership.TryGetAdvanceOrder(expected,out _,out var revision,out var at),"published leader directive can be observed passively");
            expected.Memory.IsUnderFire=true;
            Check(!GoonPushLeadership.TryGetAdvanceLeader(rogue,target,out _),"ordinary published advances retain pressure cancellation");
            GoonPushLeadership.PublishAutonomousIntent(expected,target.ProfileId,true,true,allowHarassment:true);
            Check(GoonPushLeadership.TryGetAdvanceLeader(rogue,target,out _),"commander-admitted harmless Scav pressure preserves unit authority");
            Check(GoonPushLeadership.TryGetAdvanceOrder(expected,out _,out var pressureRevision,out _) && pressureRevision==revision,
                "harassment admission does not manufacture a new directive revision");
            expected.RecentHit=true;
            Check(!GoonPushLeadership.TryGetAdvanceLeader(rogue,target,out _),"actual commander hit cancels even an admitted harassment advance");
            expected.RecentHit=false;expected.Medecine.Using=true;
            Check(!GoonPushLeadership.TryGetAdvanceOrder(expected,out _,out _,out _),"treatment retains priority over admitted harassment");
            expected.Medecine.Using=false;expected.Memory.IsUnderFire=false;
            Time.time++;
            GoonPushLeadership.PublishAutonomousIntent(expected,target.ProfileId,true,true);
            Check(GoonPushLeadership.TryGetAdvanceOrder(expected,out _,out var sameRevision,out _)&&sameRevision==revision,
                "routine intent refresh does not create a new command");
            if(expected==pipe)
            {
                Check(!ExUsecCombatExtension.TryGetPushOverride(rogue,out _,out _),"old Pipe intent cannot steal a freshly accepted player push");
                GoonPushLeadership.PublishAutonomousIntent(pipe,target.ProfileId,false);
                GoonPushLeadership.PublishAutonomousIntent(pipe,target.ProfileId,true,true);
                Check(ExUsecCombatExtension.TryGetPushOverride(rogue,out var orderedTarget,out _)&&orderedTarget==target,
                    "new Pipe directive can supersede a push through the same admitted-target transaction");
                follower.Command=FollowerCommandType.Regroup;
                Check(!ExUsecCombatExtension.TryGetPushOverride(rogue,out _,out _),"Pipe cannot supersede player Regroup");
                follower.Command=FollowerCommandType.None;
            }
            if(knight!=null)
            {
                knight.Boss.BossLogic=new BossKnight{_owner=knight,Publish=true};KnightCombatLayer.Owned.Add(knight);
                using(var coordinator=new KnightSquadCoordinator(knight))
                {
                    coordinator.Refresh();
                    Check(!coordinator.Members.Contains(rogue),"Rogues cannot exceed Knight's two native Goon slots");
                    Check(!KnightSquadCoordinator.TryGetMemberPosition(rogue,out _),"Rogue firing assignments use the separate shared commander allocator");
                }
                KnightCombatLayer.Owned.Remove(knight);
            }
            target.Known=false;
            Check(!GoonPushLeadership.TryGetSearchLeader(rogue,target,out _),"unadmitted contact cannot become group search");target.Known=true;
            expected.Medecine.Using=true;
            Check(!GoonPushLeadership.CanAutonomousAdvance(rogue,target),"leader treatment invalidates stale advance");expected.Medecine.Using=false;
            expected.IsDead=true;ExUsecCombatExtension.RefreshAnchors();
            Check(ExUsecCombatExtension.FindLeader(rogue)==(expected==knight?pipe:null),"commander death advances hierarchy without electing Birdeye");
            Check(follower.UnitAggression==60f&&follower.UnitDetermination==50f,
                "Knight loss restores saved determination while preserving Rogue aggression");
            follower.SelfDetermination=37f;ExUsecCombatExtension.RefreshAnchors();
            Check(follower.UnitDetermination==37f,"fallback respects actual saved determination");
        }
        var retained=BossPlayers.Followers.FirstOrDefault(f=>f.Bot.Profile.Info.Settings.Role==WildSpawnType.exUsec);
        ExUsecCombatExtension.Reset();GoonPushLeadership.Reset();BossPlayers.Followers.Clear();GoonCombatLayer.Owned.Clear();
        Check(retained==null||retained.UnitAggression==null&&retained.UnitDetermination==null&&retained.CompanionLeader==null,
            "teardown releases only the Rogue runtime baseline and anchor");
    }
}
