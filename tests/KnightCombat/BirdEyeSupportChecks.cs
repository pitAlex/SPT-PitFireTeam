using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.GoingRogue.Combat;
using pitTeam.GoingRogue;
using pitTeam.Modules;
using UnityEngine;

internal static class BirdEyeSupportChecks
{
    private sealed class Layer(BotOwner bot,string role,string brain) : GoonCombatLayer(bot,76,role,brain,brain)
    {
        internal override FollowerCombatLogicBase CreateLogic(BotOwner bot)=>new BigPipeCombatLogic(bot);
        internal override bool InitializeRole()=>true;
    }
    internal static void Run(Action<bool,string> check)
    {
        foreach(var (knight,pipe,expected) in new[]{
            ((string)null,(string)null,BirdEyeSupportSource.Player),
            ("a",null,BirdEyeSupportSource.Knight),(null,"b",BirdEyeSupportSource.Pipe),
            ("a","b",BirdEyeSupportSource.Knight),("a","a",BirdEyeSupportSource.Pipe)})
            check(BirdEyeSupportPriority.Select(knight,pipe)==expected,"support hierarchy and same-target exception");
        foreach(string scenario in new[]{"solo","knight","pipe","different","same","advancingKnight","sameAdvance","deadKnight","idleKnight","foreignKnight","deadPipe","unknownKnight"})
        {
            Time.time=200;GoonCombatLayer.ReleaseAll();BossPlayers.Instance.Followers.Clear();
            GameplayModeAddons.Current=GameplayMode.Allegiance;GoingRoguePlugin.CombatEnabled=true;
            FollowerCombatAnchor.Independent=false;KnightSquadCoordinator.Assigned=false;
            GoonPushLeadership.Advancing.Clear();
            var human=new pitAIBossPlayer{EngagedEnemy="playerEnemy"};
            var bird=new BotOwner();bird.Profile.Info.Settings.Role=WildSpawnType.followerBirdEye;
            var current=bird.Memory.GoalEnemy;current.ProfileId="playerEnemy";current.Distance=100;current.Anchor=new(100,0,0);
            BossPlayers.Instance.Followers[bird]=new BotFollowerPlayer{Owner=bird,Boss=human};
            var c=new FollowerCombatCommon{FiringPoint=true};c.Tracked[current.ProfileId]=current;
            BotOwner Add(WildSpawnType role,string id)
            {
                var bot=new BotOwner();bot.Profile.Info.Settings.Role=role;
                string brain=role==WildSpawnType.bossKnight?"Knight":"BigPipe";bot.Brain.BaseBrain.BrainName=brain;
                bot.Memory.GoalEnemy=new EnemyInfo{ProfileId=id,Distance=100,Anchor=new(100,0,0),IsVisible=true,CanShoot=true};
                var known=new EnemyInfo{ProfileId=id,Distance=100,Anchor=new(100,0,0)};c.Tracked[id]=known;
                BossPlayers.Instance.Followers[bot]=new BotFollowerPlayer{Owner=bot,Boss=human};
                _=new Layer(bot,role.ToString(),brain);return bot;
            }
            BotOwner k=null,p=null;
            if(scenario!="solo"&&scenario!="pipe"&&scenario!="deadPipe") k=Add(WildSpawnType.bossKnight,"knightEnemy");
            if(scenario!="solo"&&scenario!="knight") p=Add(WildSpawnType.followerBigPipe,scenario is "same" or "sameAdvance"?"knightEnemy":"pipeEnemy");
            GoonCombatLayer.RefreshAll();
            switch(scenario)
            {
                case "deadKnight":k.IsDead=true;break;
                case "idleKnight":k.Memory.GoalEnemy.IsVisible=k.Memory.GoalEnemy.CanShoot=false;break;
                case "foreignKnight":BossPlayers.Instance.Followers[k].Boss=new();break;
                case "deadPipe":p.IsDead=true;break;
                case "unknownKnight":c.Tracked["knightEnemy"].Known=false;break;
                case "advancingKnight":k.Memory.GoalEnemy.IsVisible=k.Memory.GoalEnemy.CanShoot=false;GoonPushLeadership.Advancing.Add(k);break;
                case "sameAdvance":p.Memory.GoalEnemy.IsVisible=p.Memory.GoalEnemy.CanShoot=false;GoonPushLeadership.Advancing.Add(p);break;
            }
            var policy=new BirdEyeSniperPositioning(bird,c);var result=policy.GetDecision(current);
            string expected=scenario switch {"knight" or "different" or "advancingKnight" or "same" or "sameAdvance"=>"knightEnemy","solo" or "deadPipe"=>"playerEnemy",_=>"pipeEnemy"};
            check(result.HasValue&&c.SupportTarget==expected,"production selection across squad composition: "+scenario);
            if(scenario is "same" or "sameAdvance")check(result.Value.Reason.Contains("Pipe"),"same enemy selects Pipe's support sector");
            check(policy.HasGoonSupport()==(scenario!="solo"&&scenario!="deadPipe"),"only active Goon support can relax automatic player-distance regroup: "+scenario);
            check(c.AssignedBackline&&c.AssignedSniper,"hierarchy retains exact sniper admission: "+scenario);
            policy.Reset();c.FiringPoint=false;c.ContactAdvance=false;c.SupportTarget=null;
            check(!policy.GetDecision(current).HasValue&&c.SupportTarget==null,"failed position cannot publish target/movement: "+scenario);
        }
        {
            GoonCombatLayer.ReleaseAll();BossPlayers.Instance.Followers.Clear();KnightSquadCoordinator.Assigned=false;
            var bird=new BotOwner();bird.Profile.Info.Settings.Role=WildSpawnType.followerBirdEye;
            bird.Memory.GoalEnemy.Distance=58;bird.Memory.GoalEnemy.Anchor=new(58,0,0);
            BossPlayers.Instance.Followers[bird]=new(){Owner=bird};
            var c=new FollowerCombatCommon{FiringPoint=false,ContactAdvance=true};var policy=new BirdEyeSniperPositioning(bird,c);
            check(!policy.GetDecision(bird.Memory.GoalEnemy).HasValue,"first failed firing position retains retry grace");
            Time.time+=1.1f;
            check(policy.GetDecision(bird.Memory.GoalEnemy).HasValue&&c.AdvanceStop==40,"repeat failure permits bounded sniper reacquisition at native minimum");
        }
        GoonCombatLayer.ReleaseAll();BossPlayers.Instance.Followers.Clear();GoonPushLeadership.Advancing.Clear();
    }
}
