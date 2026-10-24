using System;
using EFT;
using pitTeam.Modules;
using pitTeam.Patches;
using UnityEngine;

public static class AllegianceCombatGreetingChecks
{
    private static int count;
    private static void Check(bool condition,string message) {if(!condition)throw new Exception(message);count++;}
    private static (BotOwner Bot,Player Human,AllegianceCombatGreeting Combat,AllegianceFriendlyGreeting Peaceful) Fresh(bool sain,bool scav)
    {
        Time.time=3000;UnityEngine.Random.Fraction=0;UnityEngine.Random.ValueFraction=0;
        GameplayModeRuntime.IsAllegiance=true;AllegiancePmcFriendship.IsEnabled=true;
        pitTeam.pitFireTeam.IsSAINInstalled=sain;SAIN.Plugin.SAINEnableClass.Bot=new();
        var bot=new BotOwner{ProfileId="combat-social-"+count,Side=scav?EPlayerSide.Savage:EPlayerSide.Usec};
        bot.GetPlayer.Position=new Vector3{x=10};bot.GetPlayer.MarkSpeaking=true;
        bot.Memory.GoalEnemy=new EnemyInfo{ProfileId="real-enemy",Person=new Player()};
        return (bot,new Player(),new AllegianceCombatGreeting(),new AllegianceFriendlyGreeting());
    }
    public static int Run()
    {
        foreach(bool sain in new[]{false,true}) foreach(bool scav in new[]{false,true}) foreach(float random in new[]{0f,1f})
        {
            var x=Fresh(sain,scav);UnityEngine.Random.Fraction=random;
            x.Peaceful.Update(x.Bot,x.Human);x.Combat.Update(x.Bot,x.Human);
            Check(x.Bot.GetPlayer.Spoken==(scav?EPhraseTrigger.NeedHelp:EPhraseTrigger.HoldFire),"Combat social PMC starts Hold Fire; Scav starts Need Help, with/without SAIN");
            Check(x.Bot.Mover.Stops==0&&x.Bot.Steering.Looks==0&&x.Bot.Gesture.Calls==0,"Combat social speech never writes movement, facing or gestures");
            Check(!FollowerForcedPhraseGate.TryGetArmedPhrase(x.Bot,out _),"Combat social playback clears its exact speech scope");
            x.Bot.GetPlayer.Speaker.Speaking=false;
            if(!scav) {
                Time.time=3000+1f+random-0.001f;x.Combat.Update(x.Bot,x.Human);
                Check(x.Bot.GetPlayer.VoiceCalls==1,"PMC Need Help waits for the sampled 1–2 second delay");
                Time.time+=0.0011f;x.Combat.Update(x.Bot,x.Human);
                Check(x.Bot.GetPlayer.VoiceCalls==2&&x.Bot.GetPlayer.Spoken==EPhraseTrigger.NeedHelp,"PMC Need Help follows Hold Fire at its eligible deadline");
                x.Bot.GetPlayer.Speaker.Speaking=false;
            }
            float firstHelp=Time.time;
            Time.time=firstHelp+3f+3f*random-0.001f;x.Combat.Update(x.Bot,x.Human);
            int initial=scav?1:2;Check(x.Bot.GetPlayer.VoiceCalls==initial,"Optional repeat waits for its sampled 3–6 second deadline");
            Time.time+=0.0011f;x.Combat.Update(x.Bot,x.Human);
            Check(x.Bot.GetPlayer.VoiceCalls==initial+1&&x.Bot.GetPlayer.Spoken==EPhraseTrigger.NeedHelp,"Need Help can repeat once");
            x.Bot.GetPlayer.Speaker.Speaking=false;Time.time+=20;x.Combat.Update(x.Bot,x.Human);
            Check(x.Bot.GetPlayer.VoiceCalls==initial+1,"Later approaches cannot replay the completed combat-help sequence");
            x.Bot.Memory.GoalEnemy=null;SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy=false;
            int calls=x.Bot.GetPlayer.VoiceCalls;x.Peaceful.Update(x.Bot,x.Human);Time.time+=3;x.Peaceful.Update(x.Bot,x.Human);
            Check(x.Bot.GetPlayer.VoiceCalls==calls+1&&x.Bot.GetPlayer.Spoken==(scav?EPhraseTrigger.OnMutter:EPhraseTrigger.HoldFire),"The original peaceful greeting remains available after the added combat speech");
        }
        foreach(bool sain in new[]{false,true}) foreach(bool scav in new[]{false,true}) {
            var x=Fresh(sain,scav);UnityEngine.Random.ValueFraction=1;
            x.Combat.Update(x.Bot,x.Human);x.Bot.GetPlayer.Speaker.Speaking=false;Time.time+=2;x.Combat.Update(x.Bot,x.Human);
            x.Bot.GetPlayer.Speaker.Speaking=false;Time.time+=20;x.Combat.Update(x.Bot,x.Human);
            Check(x.Bot.GetPlayer.VoiceCalls==(scav?1:2),"The optional repeat can be skipped without replaying another sequence");
        }
        foreach(bool sainOnly in new[]{false,true}) {
            var x=Fresh(true,false);
            if(sainOnly){x.Bot.Memory.GoalEnemy=null;SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy=true;}
            else x.Bot.Memory.GoalEnemy.IsVisible=true;
            if(sainOnly)SAIN.Plugin.SAINEnableClass.Bot.EnemyController.VisibleEnemies=new object[]{new object()};
            x.Combat.Update(x.Bot,x.Human);Check(x.Bot.GetPlayer.VoiceCalls==0,"EFT and SAIN visible enemies prevent combat social speech");
            if(sainOnly)SAIN.Plugin.SAINEnableClass.Bot.EnemyController.VisibleEnemies=new object[0];else x.Bot.Memory.GoalEnemy.IsVisible=false;
            Time.time+=1;x.Combat.Update(x.Bot,x.Human);
            Check(x.Bot.GetPlayer.Spoken==EPhraseTrigger.HoldFire,"A retained nonvisible EFT or SAIN enemy permits the additional help sequence");
        }
        foreach(var busy in new Action<BotOwner>[] {b=>b.ShootData.Shooting=true,b=>b.UsingMedical=true,
            b=>b.Medecine.FirstAid.Have2Do=true,b=>b.WeaponManager._currentWeaponInfo.Reload.Reloading=true,
            b=>b.WeaponManager.Selector.IsChanging=true,b=>b.WeaponManager.Grenades.ThrowindNow=true,
            b=>b.GetPlayer.HandsController.Busy=true,b=>b.GetPlayer.Speaker.Busy=true,b=>b.GetPlayer.Speaker.Speaking=true,
            b=>b.GetPlayer.MovementContext.IsInMountedState=true,b=>b.GetPlayer.MovementContext.IsStationaryWeaponInHands=true,
            b=>b.BewareGrenade.GrenadeDangerPoint=new GrenadeDangerPoint{Grenade=new object(),EndTime=3100}}) {
            var x=Fresh(true,false);busy(x.Bot);x.Combat.Update(x.Bot,x.Human);
            Check(x.Bot.GetPlayer.VoiceCalls==0,"Combat help yields to existing shooting, treatment, weapon, hands and warning owners");
        }
        foreach(int interruption in new[]{0,1,2,3,4,5}) {
            var x=Fresh(true,false);x.Combat.Update(x.Bot,x.Human);x.Bot.GetPlayer.Speaker.Speaking=false;
            var secondary=new Player();
            if(interruption==0)x.Bot.Memory.GoalEnemy.IsVisible=true;
            if(interruption==1)x.Bot.EnemiesController.EnemyInfos[secondary]=new EnemyInfo{IsVisible=true};
            if(interruption==2)SAIN.Plugin.SAINEnableClass.Bot.EnemyController.VisibleEnemies=new object[]{new object()};
            if(interruption==3)x.Bot.LookSensor.Clear=false;
            if(interruption==4)x.Bot.ShootData.Shooting=true;
            if(interruption==5)SAIN.Plugin.SAINEnableClass.Bot.EnemyController=null;
            Time.time+=2;x.Combat.Update(x.Bot,x.Human);
            Check(x.Bot.GetPlayer.VoiceCalls==1,"Pending Need Help rechecks all enemy visibility, sight, shooting and unavailable native sight state");
            x.Bot.Memory.GoalEnemy.IsVisible=false;x.Bot.EnemiesController.EnemyInfos.Clear();
            SAIN.Plugin.SAINEnableClass.Bot.EnemyController=new();
            x.Bot.LookSensor.Clear=true;x.Bot.ShootData.Shooting=false;
            Time.time+=0.6f;x.Combat.Update(x.Bot,x.Human);
            Check(x.Bot.GetPlayer.VoiceCalls==2&&x.Bot.GetPlayer.Spoken==EPhraseTrigger.NeedHelp,"Interrupted pending help resumes only when admission is safe again");
        }
        {
            var x=Fresh(true,true);x.Bot.Memory.IsUnderFire=true;x.Combat.Update(x.Bot,x.Human);
            Check(x.Bot.GetPlayer.Spoken==EPhraseTrigger.NeedHelp,"Incoming-fire state alone must not forbid the combat-specific request");
        }
        {
            var x=Fresh(true,false);x.Bot.LookSensor.Clear=false;x.Combat.Update(x.Bot,x.Human);
            x.Bot.LookSensor.Clear=true;x.Bot.GetPlayer.Position=new Vector3{x=50.01f};Time.time+=1;x.Combat.Update(x.Bot,x.Human);
            Check(x.Bot.GetPlayer.VoiceCalls==0,"Combat help requires clear sight of the player within 50m");
            x.Bot.GetPlayer.Position=new Vector3{x=10};Time.time+=1;x.Combat.Update(x.Bot,x.Human);
            x.Bot.GetPlayer.Speaker.Speaking=false;x.Bot.Memory.GoalEnemy=null;Time.time+=0.5f;x.Combat.Update(x.Bot,x.Human);
            x.Bot.Memory.GoalEnemy=new EnemyInfo();Time.time+=4;x.Combat.Update(x.Bot,x.Human);
            Check(x.Bot.GetPlayer.VoiceCalls==1,"Combat ending cancels a pending Need Help without stale later playback");
        }
        foreach(var end in new Action<BotOwner,AllegianceCombatGreeting>[] {(b,s)=>s.Cancel(),(b,s)=>b.IsFollower=true,(b,s)=>b.IsDead=true,(b,s)=>b.GetPlayer.HealthController.IsAlive=false}) {
            var x=Fresh(true,false);x.Combat.Update(x.Bot,x.Human);x.Bot.GetPlayer.Speaker.Speaking=false;end(x.Bot,x.Combat);
            Time.time+=4;x.Combat.Update(x.Bot,x.Human);Check(x.Bot.GetPlayer.VoiceCalls==1,"Cancellation, recruitment and death stop pending combat speech");
        }
        {
            var x=Fresh(true,true);x.Bot.BotTalk.ThrowOnSay=true;
            try{x.Combat.Update(x.Bot,x.Human);throw new Exception("Expected speaker failure");}catch(Exception ex)when(ex.Message=="speaker failure"){}
            Check(!FollowerForcedPhraseGate.TryGetArmedPhrase(x.Bot,out _),"Combat playback exceptions release the exact speech scope");
            x.Bot.BotTalk.ThrowOnSay=false;Time.time+=1;x.Combat.Update(x.Bot,x.Human);
            Check(x.Bot.GetPlayer.Spoken==EPhraseTrigger.NeedHelp,"Failed combat audio remains retryable");
        }
        return count;
    }
}
