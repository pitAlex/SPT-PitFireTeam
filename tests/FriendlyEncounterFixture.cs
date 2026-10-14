using System;
using System.Reflection;
using EFT;
using pitTeam.BigBrain;
using pitTeam.BigBrain.Actions;
using pitTeam.Modules;
using pitTeam.Patches;
using UnityEngine;

public static class FriendlyEncounterChecks
{
    private static int count;
    private static void Check(bool condition,string name){if(!condition)throw new Exception(name);count++;}
    private static (BotOwner Bot,Player Human,FriendlyEncounterInteraction State,FriendlyEncounterLayer Layer,FriendlyEncounterAction Action) Fresh(bool accepted=true,bool handled=false)
    {
        Time.time=2000;UnityEngine.Random.Fraction=0;
        SAIN.Plugin.SAINEnableClass.Bot=new();pitTeam.pitFireTeam.IsSAINInstalled=true;
        GameplayModeRuntime.IsAllegiance=true;AllegiancePmcFriendship.IsEnabled=true;AllegiancePmcFriendship.Allowed=true;
        AllegiancePmcFriendship.ResolveHandled=handled;AllegiancePmcFriendship.ResolveAccepted=accepted;
        AllegiancePmcFriendship.ResolveCalls=0;AllegiancePmcFriendship.Refusals=0;
        BossPlayers.Active.Clear();BossPlayers.Denied.Clear();BossPlayers.Added=0;
        BotOwnerManualUpdatePatch.BotOwnerUpdate.Clear();FriendlyEncounterInteraction.ResetRaid();
        var bot=new BotOwner {ProfileId="social-"+count,BotsGroup=new Group()};
        var human=new Player {IsAI=false};bot.GetPlayer.Position=new Vector3{x=12};
        bot.GetPlayer.MainParts[BodyPartType.body].Position=new Vector3{x=12,y=1};
        human.InteractionRay=new Ray {origin=new Vector3{y=1},direction=new Vector3{x=1}};
        BossPlayers.Boss=new();BossPlayers.Boss.Value=human;
        AllegiancePmcFriendship.EncounterPlayer=human;AllegiancePmcFriendship.InteractionBot=bot;
        var state=new FriendlyEncounterInteraction();AllegiancePmcFriendship.Interaction=state;
        AllegiancePmcFriendship.Greeting=new AllegianceFriendlyGreeting();
        return (bot,human,state,new FriendlyEncounterLayer(bot,65),new FriendlyEncounterAction(bot));
    }
    public static int Run()
    {
        {
            var x=Fresh();x.Bot.GetPlayer.Position=new Vector3{x=12.01f};
            Check(!x.Layer.IsActive() && x.Bot.Mover.Stops==0,"Layer never activates or writes movement beyond 12m");
            x.Bot.GetPlayer.Position=new Vector3{x=12};x.Bot.LookSensor.Clear=false;
            Check(!x.Layer.IsActive(),"Acquisition requires actual sight");x.Bot.LookSensor.Clear=true;
            Check(x.Layer.IsActive() && !x.State.Waiting && x.Bot.Mover.Stops==0,"Layer probes do not start waits or movement");
            x.Layer.Start();x.Action.Update(null);
            Check(x.State.Waiting && x.Bot.Mover.Stops==1 && x.Bot.Steering.Looks==1,"Only the selected action stops and faces the player");
            Time.time=2000.49f;x.Action.Update(null);Check(x.Bot.Gesture.Calls==0,"Hello waits for its randomized deadline");
            Time.time=2000.5f;x.Action.Update(null);Check(x.Bot.Gesture.Calls==1,"Close visible contact gets one native Hello at its deadline");
            x.Bot.GetPlayer.HandsController.Busy=true;
            Check(x.Layer.IsActive(),"Own native Hello does not prematurely interrupt the waiting layer");
            x.Bot.GetPlayer.HandsController.Busy=false;x.Bot.GetPlayer.MovementContext.PlayerAnimator.AnimatedInteractions.IsInteractionPlaying=false;
            x.Human.Position=new Vector3{x=100};x.Bot.LookSensor.Clear=false;Time.time=2019.99f;x.Action.Update(null);
            Check(x.Layer.IsActive() && x.Bot.Steering.LastLook.x==100,"Walking away/lost sight retains the original wait and follows player facing");
            Time.time=2020;Check(!x.Layer.IsActive() && !x.State.Waiting,"Wait ends at exactly 20 seconds");
            int stops=x.Bot.Mover.Stops;x.Layer.Stop();x.Action.Update(null);
            Check(x.Bot.Mover.Stops==stops,"Ended layer never writes movement/facing again");
            x.Human.Position=default;x.Bot.LookSensor.Clear=true;Time.time=2029.99f;
            Check(!x.Layer.IsActive(),"The full 10-second cooldown is respected");Time.time=2030;
            Check(x.Layer.IsActive(),"Close player can activate another wait at cooldown expiry");x.Layer.Start();x.Action.Update(null);
            Time.time=2031;x.Action.Update(null);Check(x.Bot.Gesture.Calls==1,"Reactivation cannot replay the raid's Hello");
        }
        foreach(float random in new[]{0f,1f})
        {
            var x=Fresh();UnityEngine.Random.Fraction=random;x.Layer.IsActive();x.Layer.Start();
            Time.time=2000+0.5f+2.5f*random-0.001f;x.Action.Update(null);
            Check(x.Bot.Gesture.Calls==0,"Both randomized Hello deadline endpoints wait");
            Time.time+=0.0011f;x.Action.Update(null);
            // The social poll is bounded at 0.5s; a deadline can be observed on the next poll.
            Time.time+=0.5f;x.Action.Update(null);Check(x.Bot.Gesture.Calls==1,"Both Hello endpoint schedules eventually execute once");
        }
        {
            var x=Fresh();x.Layer.IsActive();x.Layer.Start();x.Bot.Settings.FileSettings.Patrol.CAN_GESTUS=false;
            Time.time=2000.5f;x.Action.Update(null);Check(x.Bot.Gesture.Calls==0,"Waiting layer still honors disabled native gestures");
            x.Bot.Settings.FileSettings.Patrol.CAN_GESTUS=true;x.Bot.Gesture.Accept=false;Time.time=2001;x.Action.Update(null);
            Check(x.Bot.Gesture.Calls==1 && !AllegiancePmcFriendship.Greeting.HelloSent,"Rejected native gesture never consumes the raid's Hello");
            x.Bot.Gesture.Accept=true;Time.time=2001.5f;x.Action.Update(null);
            Check(x.Bot.Gesture.Calls==2 && AllegiancePmcFriendship.Greeting.HelloSent,"Rejected Hello retries safely during the same waiting window");
            x.Bot.GetPlayer.MovementContext.PlayerAnimator.AnimatedInteractions.IsInteractionPlaying=false;Time.time=2002;x.Action.Update(null);
            Check(x.Bot.Gesture.Calls==2,"Accepted Hello cannot repeat after its animation finishes");
        }
        {
            var x=Fresh();x.Bot.Mover.Sprinting=true;x.Bot.GetPlayer.MovementContext.IsSprintEnabled=true;
            Check(x.Layer.IsActive() && x.Bot.Mover.Sprinting,"Inactive layer probes never change sprint input");
            x.Layer.Start();x.Action.Update(null);
            Check(!x.Bot.Mover.Sprinting && !x.Bot.GetPlayer.MovementContext.IsSprintEnabled,"Active waiting action stops sprint through the native mover");
            Time.time=2000.5f;x.Action.Update(null);Check(x.Bot.Gesture.Calls==1,"Stopped sprint permits the scheduled Hello without forcing animation");
        }
        foreach(var begin in new Action<BotOwner>[] {b=>b.Memory.GoalEnemy=new(),b=>b.Memory.IsUnderFire=true,
            b=>b.UsingMedical=true,b=>b.Medecine.FirstAid.Have2Do=true,b=>b.Medecine.SurgicalKit.HaveWork=true,
            b=>b.Brain.Agent.Result.Action=BotLogicDecision.heal,b=>b.Brain.Agent.Result.Action=BotLogicDecision.healStimulators,
            b=>b.WeaponManager._currentWeaponInfo.Reload.Reloading=true,
            b=>b.WeaponManager.Grenades.ThrowindNow=true,b=>b.WeaponManager.Selector.IsChanging=true,
            b=>b.GetPlayer.HandsController.Busy=true,b=>b.ShootData.Shooting=true,
            b=>b.GetPlayer.MovementContext.IsInMountedState=true,b=>b.GetPlayer.MovementContext.IsStationaryWeaponInHands=true})
        {
            var x=Fresh();x.Layer.IsActive();x.Layer.Start();x.Action.Update(null);int stops=x.Bot.Mover.Stops;
            begin(x.Bot);Time.time=2001;Check(!x.Layer.IsActive(),"Urgent native work interrupts active waiting");
            x.Action.Update(null);Check(x.Bot.Mover.Stops==stops,"Interrupted action does not keep stopping the urgent owner");
        }
        {
            var x=Fresh();x.Layer.IsActive();x.Layer.Start();SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy=true;
            Check(!x.Layer.IsActive(),"SAIN-only combat interrupts without an EFT goal");
        }
        foreach(bool scav in new[]{false,true})
        {
            var x=Fresh(handled:true);x.Bot.Side=scav ? EPlayerSide.Savage : EPlayerSide.Usec;
            x.Layer.IsActive();x.Layer.Start();x.Action.Update(null);
            Check(x.Bot.GetPlayer.Speaker.DirectPhrases.Count==1 && x.Bot.GetPlayer.Speaker.DirectPhrases[0]==
                (scav ? EPhraseTrigger.FollowMe : EPhraseTrigger.Cooperation),"Willing bot emits the faction's invitation without an implicit Hello");
            Check(FriendlyEncounterInteraction.InvitingBot==x.Bot && x.Bot.Gesture.Calls==0,"Single invitation owns its replies independently of the Hello timer");
            Time.time=2001;x.Action.Update(null);Check(x.Bot.GetPlayer.Speaker.DirectPhrases.Count==1,"Pending invitation cannot repeat within the wait");
            Check(x.State.Respond(x.Bot,x.Human,false) && FriendlyEncounterInteraction.InvitingBot==null,"Negative releases the invitation and waiting layer");
            Check(BossPlayers.Denied.Count==0 && AllegiancePmcFriendship.Refusals==0,"Player refusal never becomes a bot refusal");
            x.Bot.GetPlayer.Speaker.Speaking=false;Time.time=2011;x.Layer.IsActive();x.Layer.Start();x.Action.Update(null);
            Check(x.State.Waiting && x.Bot.GetPlayer.Speaker.DirectPhrases.Count==1,"Player can approach again but rejected offers never repeat");
            // A later direct player request still uses the bot's saved positive answer.
            var prefix=typeof(FollowRequestPatch).GetMethod("PatchPrefix",BindingFlags.NonPublic|BindingFlags.Static);
            object[] args={x.Bot.BotsGroup.RequestsController,true,x.Human,x.Bot};prefix.Invoke(null,args);
            Check(BotOwnerManualUpdatePatch.BotOwnerUpdate.ContainsKey(x.Bot.ProfileId),"Player can change their mind and request recruitment after declining");
        }
        {
            var x=Fresh(handled:true);x.Layer.IsActive();x.Layer.Start();x.Action.Update(null);
            x.Human.Position=new Vector3{x=50};
            Check(!x.State.Respond(x.Bot,x.Human,true) && x.Bot.BotsGroup.RequestsController.Calls==0,"Walking away cannot accidentally accept an invitation");
            x.Human.Position=default;x.Bot.LookSensor.Clear=false;
            Check(!x.State.Respond(x.Bot,x.Human,true),"Positive response requires current sight");x.Bot.LookSensor.Clear=true;
            Check(x.State.Respond(x.Bot,x.Human,true) && x.Bot.BotsGroup.RequestsController.Calls==1 && !x.State.Waiting,
                "Positive response asks only the inviting bot through the native recruitment controller");
        }
        {
            var x=Fresh(accepted:false,handled:true);x.Layer.IsActive();x.Layer.Start();x.Action.Update(null);
            Check(x.Bot.GetPlayer.Speaker.DirectPhrases.Count==0 && BossPlayers.Denied.Count==0,"A failed proactive answer is saved without asking or recording a spoken refusal");
            var prefix=typeof(FollowRequestPatch).GetMethod("PatchPrefix",BindingFlags.NonPublic|BindingFlags.Static);
            object[] args={x.Bot.BotsGroup.RequestsController,true,x.Human,x.Bot};prefix.Invoke(null,args);
            Check(BossPlayers.HasDeniedRecruitment(x.Bot.ProfileId) && x.State.Waiting && x.Layer.IsActive(),"Actual request stores NO immediately while retaining the refusal pause");
            Time.time=2001.49f;x.Action.Update(null);
            Check(x.State.Waiting && x.Bot.Mover.Stops==2,"Bot remains stopped for the minimum refusal pause even after a short line");
            Time.time=2001.5f;Check(!x.Layer.IsActive() && !x.State.Waiting,"Silent/finished refusal releases at the minimum pause deadline");
            Time.time=2100;Check(!x.Layer.IsActive(),"A bot that explicitly refused does not restart pointless interaction waits");
        }
        foreach(bool scav in new[]{false,true})
        {
            var x=Fresh(accepted:false,handled:true);x.Bot.Side=scav?EPlayerSide.Savage:EPlayerSide.Usec;
            x.Layer.IsActive();x.Layer.Start();Time.time=2019.9f;
            AllegiancePmcFriendship.NoteRequestedRefusal(x.Bot);x.Bot.GetPlayer.Speaker.Speaking=true;
            x.Bot.GetPlayer.HandsController.Busy=true;
            Time.time=2020;x.Action.Update(null);
            Check(x.Layer.IsActive() && x.Bot.Mover.Stops==1,"Late refusal keeps the timed wait despite response hands work and the original 20-second deadline");
            Check(x.Bot.GetPlayer.Speaker.DirectPhrases.Count==0 && x.Bot.Gesture.Calls==0,"Refusal pause cannot emit Hello or another invitation");
            x.Human.Position=new Vector3{x=100};x.Bot.LookSensor.Clear=false;Time.time=2021.39f;x.Action.Update(null);
            Check(x.Layer.IsActive() && x.Bot.Steering.LastLook.x==100,"Timed refusal survives player departure and continues facing without requiring fresh sight");
            AllegiancePmcFriendship.NoteRequestedRefusal(x.Bot);Time.time=2021.399f;
            Check(x.Layer.IsActive(),"Repeated requests keep the original refusal timestamp");
            Time.time=2021.4f;Check(!x.Layer.IsActive(),"Refusal releases at its fixed deadline even while speech and hands are busy");
            Time.time=2100;Check(!x.Layer.IsActive(),"Refusal remains terminal for PMC and Scav after grace expiry");
        }
        foreach(bool scav in new[]{false,true})
        foreach(var interrupt in new Action<BotOwner>[] {b=>b.Memory.GoalEnemy=new(),b=>b.Medecine.FirstAid.Have2Do=true,
            b=>b.WeaponManager._currentWeaponInfo.Reload.Reloading=true})
        {
            var x=Fresh(accepted:false,handled:true);x.Bot.Side=scav?EPlayerSide.Savage:EPlayerSide.Usec;
            x.Layer.IsActive();x.Layer.Start();AllegiancePmcFriendship.NoteRequestedRefusal(x.Bot);
            x.Bot.GetPlayer.Speaker.Speaking=true;interrupt(x.Bot);Time.time=2000.1f;
            Check(!x.Layer.IsActive() && !x.State.Waiting,"Combat and urgent treatment/reload interrupt refusal before its minimum pause");
            x.Bot.Memory.GoalEnemy=null;x.Bot.Medecine.FirstAid.Have2Do=false;x.Bot.WeaponManager._currentWeaponInfo.Reload.Reloading=false;
            Time.time=2100;Check(!x.Layer.IsActive(),"Interrupted refusal cannot restart a Scav or PMC interaction later");
        }
        {
            var x=Fresh(accepted:false,handled:true);x.Layer.IsActive();x.Layer.Start();AllegiancePmcFriendship.NoteRequestedRefusal(x.Bot);
            x.Bot.GetPlayer.Speaker.Speaking=true;Time.time=2000.5f;x.Layer.Stop();
            Check(!(bool)typeof(FriendlyEncounterInteraction).GetField("refusing",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(x.State),"Stop resets the refusal flag");
            Check((float)typeof(FriendlyEncounterInteraction).GetField("refusalStartedAt",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(x.State)==0f,"Stop resets the refusal timestamp");
        }
        {
            var x=Fresh(handled:true);x.Bot.GetPlayer.Speaker.AcceptDirect=false;x.Layer.IsActive();x.Layer.Start();x.Action.Update(null);
            Check(FriendlyEncounterInteraction.InvitingBot==null,"Failed/missing invitation playback never arms a response");
            x.Bot.GetPlayer.Speaker.AcceptDirect=true;Time.time=2001;x.Action.Update(null);
            Check(FriendlyEncounterInteraction.InvitingBot==x.Bot,"Failed invitation can retry while the interaction is eligible");
            x.State.Cancel(x.Bot);Check(FriendlyEncounterInteraction.InvitingBot==null && !x.State.Waiting,"Terminal cleanup releases all invitation ownership");
        }
        {
            var x=Fresh(handled:true);x.Layer.IsActive();x.Layer.Start();x.Action.Update(null);
            var other=new BotOwner {ProfileId="other-inviter"};other.GetPlayer.Position=new Vector3{x=5};
            var state=new FriendlyEncounterInteraction();state.Begin(other,x.Human);state.Update(other,x.Human,new AllegianceFriendlyGreeting());
            Check(other.GetPlayer.Speaker.DirectPhrases.Count==0 && FriendlyEncounterInteraction.InvitingBot==x.Bot,"Two close bots cannot compete for the same reply");
            Time.time=2020;x.Layer.IsActive();
            Check(FriendlyEncounterInteraction.InvitingBot==null,"Timeout releases a pending invitation");
        }
        {
            var x=Fresh(handled:true);x.Layer.IsActive();x.Layer.Start();x.Action.Update(null);x.Bot.IsDead=true;
            var other=new BotOwner {ProfileId="live-inviter"};other.GetPlayer.Position=new Vector3{x=5};
            var state=new FriendlyEncounterInteraction();state.Begin(other,x.Human);state.Update(other,x.Human,new AllegianceFriendlyGreeting());
            Check(FriendlyEncounterInteraction.InvitingBot==other && other.GetPlayer.Speaker.DirectPhrases.Count==1,
                "A dead inviter releases its global response slot even without another bot update");
        }
        foreach(bool full in new[]{false,true})
        {
            var x=Fresh(handled:true);x.Layer.IsActive();x.Layer.Start();x.Action.Update(null);
            if(full)for(int i=0;i<2;i++)BossPlayers.Active.Add(new pitTeam.Components.BotFollowerPlayer {Bot=new BotOwner {ProfileId="full-"+i}});
            var prefix=typeof(FollowRequestPatch).GetMethod("PatchPrefix",BindingFlags.NonPublic|BindingFlags.Static);
            x.Bot.BotsGroup.RequestsController.AfterAsk=()=> {
                object[] args={x.Bot.BotsGroup.RequestsController,true,x.Human,x.Bot};prefix.Invoke(null,args);
            };
            Check(x.State.Respond(x.Bot,x.Human,true),"Positive invitation response enters the production recruitment prefix");
            Check(BotOwnerManualUpdatePatch.BotOwnerUpdate.ContainsKey(x.Bot.ProfileId)==!full,
                "Capacity is checked again after invitation playback and before deferred conversion");
            if(full)Check(BossPlayers.Denied.Count==0 && BossPlayers.Added==0,"A full squad is a temporary refusal with no remembered bot NO");
            else {
                BotOwnerManualUpdatePatch.BotOwnerUpdate[x.Bot.ProfileId](x.Bot);pitTeam.Utils.Utils.Pending();
                Check(BossPlayers.Added==1 && x.Bot.IsFollower,"Accepted offer finishes through the existing deferred conversion");
            }
        }
        Logger.AllowErrors=true;
        try {
            var x=Fresh();Logger.Errors=0;x.Bot.LookSensor.Throw=true;
            Check(!x.Layer.IsActive() && Logger.Errors==2,"Layer API failure releases ownership and reports once");
            Check(!x.Layer.IsActive() && Logger.Errors==2,"Failed layer remains inert without per-frame error spam");
            x=Fresh();Logger.Errors=0;x.Layer.IsActive();x.Layer.Start();x.Bot.Steering.Throw=true;x.Action.Update(null);
            Check(!x.State.Waiting && Logger.Errors==2,"Action API failure cancels its pending wait/invitation");
            int stops=x.Bot.Mover.Stops;x.Action.Update(null);
            Check(x.Bot.Mover.Stops==stops && Logger.Errors==2,"Failed action never reclaims movement or repeats logging");
        } finally {Logger.AllowErrors=false;}
        AllegiancePmcFriendship.ResolveHandled=false;AllegiancePmcFriendship.Interaction=null;AllegiancePmcFriendship.Greeting=null;
        FriendlyEncounterInteraction.ResetRaid();
        return count;
    }
}
