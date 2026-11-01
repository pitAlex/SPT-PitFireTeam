using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.GoingRogue.Combat;
using pitTeam.Modules;

internal static class LifecycleChecks
{
    private sealed class Layer(BotOwner bot) : GoonCombatLayer(bot,76,"followerBigPipe","BigPipe","BigPipe")
    {
        public bool Ready=true, FailRefresh; public int Releases;
        internal override FollowerCombatLogicBase CreateLogic(BotOwner owner)=>new BigPipeCombatLogic(owner);
        internal override bool InitializeRole()=>Ready;
        internal override void RefreshRole() { if(FailRefresh) throw new InvalidOperationException("fixture role failure"); }
        internal override void ReleaseRole()=>Releases++;
    }
    internal static void Run(Action<bool,string> check)
    {
        (BotOwner bot,Layer layer,BigPipeFightLayer fight) Setup(bool registered=true)
        {
            GoonCombatLayer.ReleaseAll();BossPlayers.Instance.Followers.Clear();
            GameplayModeAddons.Current=GameplayMode.GoingRogue;
            pitTeam.GoingRogue.GoingRoguePlugin.CombatEnabled=true;
            var bot=new BotOwner();var fight=new BigPipeFightLayer();
            foreach(var item in new AICoreLayer<BotLogicDecision>[]{new("Danger",80),new("Maintenance",78),fight,new PeacefulRequestLayer()})
            {bot.Brain.BaseBrain._layers.Add(item.Priority,item);bot.Brain.BaseBrain._activeLayers.Add(item);}
            if(registered)BossPlayers.Instance.Followers.Add(bot,new BotFollowerPlayer{Owner=bot});
            return(bot,new Layer(bot),fight);
        }
        {
            var(bot,layer,fight)=Setup();
            check(!GoonCombatLayer.IsSpawnReady(bot.ProfileId,"followerBigPipe"),"unclaimed Big Pipe is not spawn-ready");
            layer.Ready=false;GoonCombatLayer.RefreshAll();
            check(!layer.IsActive() && bot.Brain.BaseBrain._activeLayers.Contains(fight),"incomplete native role keeps native layers and cannot activate");
            layer.Ready=true;GoonCombatLayer.RefreshAll();var driver=FollowerCombatLayer.Last;
            check(GoonCombatLayer.IsOwned(bot,"followerBigPipe") && GoonCombatLayer.IsSpawnReady(bot.ProfileId,"followerBigPipe"),"successful native role claim gates readiness");
            check(!GoonCombatLayer.IsSpawnReady(bot.ProfileId,"bossKnight") && !GoonCombatLayer.IsSpawnReady("other","followerBigPipe"),"readiness matches both actor and role");
            check(!bot.Brain.BaseBrain._activeLayers.Contains(fight) && bot.Brain.BaseBrain._activeLayers.Count==2,"only combat/request conflicts are leased; native danger and maintenance remain");
            layer.Start();layer.Stop();
            check(driver.Starts==1 && driver.Stops==0 && FollowerRecovery.Stops>0 && FollowerGrenadeRuntimeGate.Stops>0,"danger interruption suspends shared objective while stopping shots and grenades");
            layer.Start();check(driver.Starts==1,"danger resume preserves existing objective rather than restarting it");
            GoonCombatLayer.RequestPositionRefresh(bot);check(GoonCombatLayer.PositionRevision(bot)==1,"native position refresh is deferred through revision state");
            GoonCombatLayer.AbortSpawn(bot.ProfileId);
            check(!layer.IsActive() && !GoonCombatLayer.IsSpawnReady(bot.ProfileId,"followerBigPipe"),"spawn abort clears ownership and readiness");
            check(driver.Stops==1 && layer.Releases==1 && bot.Brain.BaseBrain._activeLayers.Contains(fight),"spawn abort stops driver, releases coordination and restores native fight layer");
            check(fight.Activations==0,"restoration cannot duplicate native Activate subscriptions");
        }
        foreach(string scenario in new[]{"hostile","wrongRole","wrongBrain","otherMode","otherLeader","inactive","dead","disabled"})
        {
            var(bot,layer,fight)=Setup(scenario!="hostile");
            switch(scenario)
            {
                case "wrongRole":bot.Profile.Info.Settings.Role=WildSpawnType.assault;break;
                case "wrongBrain":bot.Brain.BaseBrain.BrainName="Other";break;
                case "otherMode":GameplayModeAddons.Current=GameplayMode.Allegiance;break;
                case "otherLeader":BossPlayers.Instance.Followers[bot].Boss.realPlayer.IsYourPlayer=false;break;
                case "inactive":bot.BotState=EBotState.NonActive;break;
                case "dead":bot.IsDead=true;break;
                case "disabled":pitTeam.GoingRogue.GoingRoguePlugin.CombatEnabled=false;break;
            }
            GoonCombatLayer.RefreshAll();
            check(!layer.IsActive() && !GoonCombatLayer.IsOwned(bot,"followerBigPipe") && !GoonCombatLayer.IsSpawnReady(bot.ProfileId,"followerBigPipe"),"unrelated/ineligible bot cannot claim readiness: "+scenario);
            check(bot.Brain.BaseBrain._activeLayers.Contains(fight),"ineligible bot retains native combat: "+scenario);
        }
        foreach(string scenario in new[]{"replacement","fault","modeExit","dead"})
        {
            var(bot,layer,fight)=Setup();GoonCombatLayer.RefreshAll();var driver=FollowerCombatLayer.Last;var brain=bot.Brain.BaseBrain;
            if(scenario=="replacement")bot.Brain.BaseBrain=new();
            if(scenario=="fault")layer.FailRefresh=true;
            if(scenario=="modeExit")GameplayModeAddons.Current=GameplayMode.Allegiance;
            if(scenario=="dead")bot.IsDead=true;
            GoonCombatLayer.RefreshAll();
            check(!GoonCombatLayer.IsOwned(bot,"followerBigPipe") && !GoonCombatLayer.IsSpawnReady(bot.ProfileId,"followerBigPipe") && driver.Stops==1,"ownership loss/fault stops driver and revokes readiness: "+scenario);
            check(layer.Releases==1,"ownership loss/fault releases role coordination: "+scenario);
            if(scenario=="fault" || scenario=="modeExit")check(brain._activeLayers.Contains(fight),"recoverable ownership loss restores existing native layers: "+scenario);
            else check(!brain._activeLayers.Contains(fight),"dead or detached brain is not reactivated: "+scenario);
            if(scenario=="fault") {GoonCombatLayer.RefreshAll();check(!layer.IsActive(),"faulted layer cannot silently reclaim combat");}
        }
        {
            var(bot,_,_)=Setup();bot.Brain.BaseBrain=new FollowerBigPipeLayersStrategy();
            GoonCombatLayer.ReleaseAll();var concrete=new BigPipeCombatLayer(bot,76);GoonCombatLayer.RefreshAll();
            var prefix=typeof(BigPipePlannedWeaponSelectionPatch).GetMethod("Prefix",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            bool AllowsNative(BotOwner owner)=>(bool)prefix.Invoke(null,new object[]{new BigPipeBotWeaponSelector{Owner=owner}});
            check(concrete.IsActive() && !AllowsNative(bot),"claimed native Big Pipe delegates autonomous weapon selection to the planned launcher owner");
            check(AllowsNative(new BotOwner()) && AllowsNative(null),"hostile/unregistered/missing-owner selectors retain native updates");
            GoonCombatLayer.AbortSpawn(bot.ProfileId);
            check(AllowsNative(bot),"releasing managed Big Pipe restores native weapon selection");
        }
        GoonCombatLayer.ReleaseAll();GameplayModeAddons.Current=GameplayMode.GoingRogue;
    }
}
