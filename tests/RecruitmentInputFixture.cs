using System;
using System.Reflection;
using EFT;
using pitTeam.Modules;
using pitTeam.Patches;
using UnityEngine;
using EFT.UI.Gestures;
using HarmonyLib;

public enum BodyPartType { head, body }
public class BodyPart { public Vector3 Position; }
public class ShootToPoint { public Vector3 Point; public ShootToPoint(Vector3 point, int coefficient) { Point = point; } }
public static class LayersMaskController { public const int HighPolyWithTerrainMask = 1; }

namespace pitTeam.Patches {
    public enum CustomPhrases { TeamStatus=1000 }
    internal class QuickPanelPatch { __COOPERATION_AVAILABILITY__ }
    internal class CreatePhraseGroupPatch { __HELP_GROUP_METHOD__ }
    __MENU_VISIBILITY_PATCH__
    __MENU_PHRASE_PATCH__
}
namespace Comfort.Common { public static class Singleton<T> where T : new() { public static T Instance = new(); } }
namespace EFT.UI.Gestures {
    public class GesturesQuickPanel {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static bool IsSituationalPhrase(EPhraseTrigger phrase) => phrase==EPhraseTrigger.Cooperation || phrase==EPhraseTrigger.OnRepeatedContact;
        public bool IsPhraseAvailable(EPhraseTrigger phrase) => !IsSituationalPhrase(phrase);
    }
    public class GesturesMenu {
        public bool Active;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void SetPhraseActive(EPhraseTrigger phrase,bool active) { Active=active; }
    }
}

public static class RecruitmentInputChecks {
    private static int count;
    private static void Check(bool value,string description) { count++;if(!value)throw new Exception(description); }
    private static readonly MethodInfo Prefix=typeof(BotReceiverRecruitPatch).GetMethod("PatchPrefix",BindingFlags.NonPublic|BindingFlags.Static);
    private static readonly MethodInfo Recruit=typeof(pitTeam.Components.pitAIBossPlayer).GetMethod("TryRecruitFromCooperation",BindingFlags.NonPublic|BindingFlags.Instance);
    private static readonly MethodInfo Dispatch=typeof(pitTeam.Components.pitAIBossPlayer).GetMethod("ApplyCooperationCommand",BindingFlags.NonPublic|BindingFlags.Instance);
    private static bool Receive(BotOwner bot,Player player,EPhraseTrigger phrase) {
        if(phrase==EPhraseTrigger.Cooperation) Recruit.Invoke(BossPlayers.Boss,new object[]{bot,player});
        return (bool)Prefix.Invoke(null,new object[]{new BotReceiver{_owner=bot},new GlobalEventDispatcher.PhraseDelegateInfo{PlayerRequester=player,phrase=phrase}});
    }
    private static void Place(BotOwner bot,float x) {
        bot.GetPlayer.Position=new Vector3{x=x};
        bot.GetPlayer.MainParts[BodyPartType.body].Position=new Vector3{x=x,y=1};
    }

    public static int Run() {
        foreach(bool sain in new[]{false,true}) foreach(bool allegiance in new[]{false,true}) {
            pitTeam.pitFireTeam.IsSAINInstalled=sain;GameplayModeRuntime.IsAllegiance=allegiance;
            pitTeam.pitFireTeam.pickupEnabled.Value=true;AllegiancePmcFriendship.Allowed=true;
            pitTeam.Utils.Utils.HeadVisible=true;pitTeam.Utils.Utils.BodyVisible=true;
            var player=BossPlayers.Boss.Value;player.Side=EPlayerSide.Usec;player.Position=default;player.InteractablePlayer=null;
            player.InteractionRay=new Ray{origin=new Vector3{y=1},direction=new Vector3{x=1}};
            var bear=new BotOwner{Side=EPlayerSide.Bear,BotsGroup=new Group()};
            Place(bear,2);
            bear.GetPlayer.Side=EPlayerSide.Bear;player.InteractablePlayer=bear.GetPlayer;
            Check(QuickPanelPatch.CanShowCooperation(player)==allegiance,"Cooperation quick command follows selected cross-faction eligibility in both AI paths");
            AllegiancePmcFriendship.Allowed=false;
            Check(!QuickPanelPatch.CanShowCooperation(player),"Unselected Allegiance candidate cannot expose Cooperation");
            AllegiancePmcFriendship.Allowed=true;
            bear.Side=EPlayerSide.Usec;bear.GetPlayer.Side=EPlayerSide.Usec;
            Check(QuickPanelPatch.CanShowCooperation(player),"Eligible same-side target remains available");
            bear.GetPlayer.HealthController.IsAlive=false;
            Check(!QuickPanelPatch.CanShowCooperation(player),"Dead target has no Cooperation interaction");bear.GetPlayer.HealthController.IsAlive=true;
            bear.IsFollower=true;Check(!QuickPanelPatch.CanShowCooperation(player),"Existing follower has no recruitment interaction");bear.IsFollower=false;
            pitTeam.pitFireTeam.pickupEnabled.Value=false;Check(QuickPanelPatch.CanShowCooperation(player)==allegiance,"Pickup preference cannot hide Allegiance Cooperation");pitTeam.pitFireTeam.pickupEnabled.Value=true;
            player.Side=EPlayerSide.Savage;bear.Side=EPlayerSide.Bear;
            Check(!QuickPanelPatch.CanShowCooperation(player),"Player Scav cannot expose cross-faction PMC recruitment");player.Side=EPlayerSide.Usec;
            Check(!Receive(bear,player,EPhraseTrigger.FollowMe) && bear.BotsGroup.RequestsController.Calls==0,"Follow Me blocks native and mod recruitment for non-followers");
            bear.IsFollower=true;
            Check(!Receive(bear,player,EPhraseTrigger.FollowMe) && bear.BotsGroup.RequestsController.Calls==0,"Follower Follow Me remains core owned");bear.IsFollower=false;
            Check(!Receive(bear,player,EPhraseTrigger.Cooperation) && bear.BotsGroup.RequestsController.Calls==1,"Targeted Cooperation dispatches once through existing request flow");
            var other=new BotOwner{BotsGroup=new Group()};
            Place(other,3);
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==0,"Targeted Cooperation excludes other listeners");
            player.InteractablePlayer=null;
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==1,"Help-menu Cooperation without a selected target reaches nearby candidates");
            player.InteractionRay=new Ray{origin=new Vector3{y=1},direction=new Vector3{x=-1}};
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==1,"Visible nearby bot behind the player's view cannot be recruited from HELP");
            player.InteractablePlayer=other.GetPlayer;
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==1,"Stale contextual target cannot bypass the player's look direction");
            player.InteractablePlayer=null;
            player.InteractionRay=new Ray{origin=new Vector3{y=1},direction=new Vector3{x=1,y=1}};
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==1,"Nearby bot outside the interaction view cannot react");
            player.InteractionRay=new Ray{origin=new Vector3{y=1},direction=new Vector3{x=1}};
            pitTeam.Utils.Utils.HeadVisible=false;pitTeam.Utils.Utils.BodyVisible=false;
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==1,"Help-menu Cooperation cannot recruit through an obstruction");
            player.InteractablePlayer=other.GetPlayer;
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==1,"Contextual target cannot bypass the shared sight gate");
            pitTeam.Utils.Utils.BodyVisible=true;
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==2,"Visible body is enough when the head is obstructed");
            pitTeam.Utils.Utils.BodyVisible=false;pitTeam.Utils.Utils.HeadVisible=true;
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==3,"Visible head is enough when the body is obstructed");
            other.IsDead=true;
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==3,"Dead listener cannot receive Cooperation");other.IsDead=false;
            other.BotState=EBotState.Inactive;
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==3,"Inactive listener cannot receive Cooperation");other.BotState=EBotState.Active;
            var parts=player.MainParts;player.MainParts=null;
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==3,"Unavailable player sight targets fail closed");player.MainParts=parts;
            player.InteractablePlayer=null;
            Place(other,5);
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==4,"Visible candidate at exactly five metres remains eligible");
            int sightChecks=pitTeam.Utils.Utils.SightChecks;
            Place(other,5.01f);
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==4,"Recruitment phrase rejects candidates just beyond five metres without native fallback");
            Check(pitTeam.Utils.Utils.SightChecks==sightChecks,"Out-of-range listeners do not perform sight checks");
            Check(Receive(bear,player,EPhraseTrigger.NeedHelp) && bear.BotsGroup.RequestsController.Calls==1,"Need Help keeps its existing support behavior");
        }
        var help=new[]{EPhraseTrigger.NeedHelp};CreatePhraseGroupPatch.AddCooperationToHelpGroup("HELP",ref help);
        Check(help.Length==2 && help[0]==EPhraseTrigger.NeedHelp && help[1]==EPhraseTrigger.Cooperation,"Help menu keeps support and adds Cooperation");
        CreatePhraseGroupPatch.AddCooperationToHelpGroup("HELP",ref help);Check(help.Length==2,"Help menu does not duplicate Cooperation");
        var commands=new[]{EPhraseTrigger.FollowMe};CreatePhraseGroupPatch.AddCooperationToHelpGroup("COMMAND",ref commands);
        Check(commands.Length==1 && commands[0]==EPhraseTrigger.FollowMe,"Other menu groups keep their commands");
        new Harmony("pitFireTeam.tests.recruitment-menu").Patch(
            AccessTools.Method(typeof(GesturesMenu),nameof(GesturesMenu.SetPhraseActive)),
            prefix:new HarmonyMethod(typeof(GestureMenuCooperationVisibilityPatch).GetMethod("PatchPrefix",BindingFlags.Static|BindingFlags.NonPublic)));
        var menu=new GesturesMenu();
        menu.SetPhraseActive(EPhraseTrigger.Cooperation,false);
        Check(menu.Active,"Full menu retains Cooperative when contextual target is missing or too far away");
        menu.SetPhraseActive(EPhraseTrigger.Cooperation,true);
        Check(menu.Active,"Full menu retains Cooperative when contextual target becomes available");
        menu.SetPhraseActive(EPhraseTrigger.NeedHelp,false);
        Check(!menu.Active,"Other full-menu phrases retain native availability");
        new Harmony("pitFireTeam.tests.recruitment-phrase").Patch(
            AccessTools.Method(typeof(GesturesQuickPanel),nameof(GesturesQuickPanel.IsSituationalPhrase)),
            prefix:new HarmonyMethod(typeof(GestureMenuCooperationPhrasePatch).GetMethod("PatchPrefix",BindingFlags.Static|BindingFlags.NonPublic)));
        Check(!GesturesQuickPanel.IsSituationalPhrase(EPhraseTrigger.Cooperation),"Cooperation supports native right-click assignment and has no situational marker");
        Check(new GesturesQuickPanel().IsPhraseAvailable(EPhraseTrigger.Cooperation),"Cooperation can play from a binding or menu without a contextual target");
        Check(GesturesQuickPanel.IsSituationalPhrase(EPhraseTrigger.OnRepeatedContact),"Other situational phrases retain native classification");

        var boss=BossPlayers.Boss;var leader=boss.Value;
        var target=new BotOwner{BotsGroup=new Group()};Place(target,2);
        var listener=new BotOwner{BotsGroup=new Group()};Place(listener,3);
        leader.InteractionRay=new Ray{origin=new Vector3{y=1},direction=new Vector3{x=1}};
        leader.InteractablePlayer=target.GetPlayer;
        var world=Comfort.Common.Singleton<GameWorld>.Instance;
        world.AllAlivePlayersList.AddRange(new[]{leader,target.GetPlayer,listener.GetPlayer});
        pitTeam.Utils.Utils.HeadVisible=true;pitTeam.Utils.Utils.BodyVisible=true;
        Dispatch.Invoke(boss,new object[]{leader});
        Check(target.BotsGroup.RequestsController.Calls==1 && listener.BotsGroup.RequestsController.Calls==0,"AIBoss receiver dispatches only to the selected bot");
        Prefix.Invoke(null,new object[]{new BotReceiver{_owner=target},new GlobalEventDispatcher.PhraseDelegateInfo{PlayerRequester=leader,phrase=EPhraseTrigger.Cooperation}});
        Check(target.BotsGroup.RequestsController.Calls==1,"Native bot broadcast cannot duplicate AIBoss recruitment");
        leader.InteractablePlayer=null;
        Dispatch.Invoke(boss,new object[]{leader});
        Check(target.BotsGroup.RequestsController.Calls==2 && listener.BotsGroup.RequestsController.Calls==1,"AIBoss receiver finds visible candidates without contextual interaction");
        leader.InteractionRay=new Ray{origin=new Vector3{y=1},direction=new Vector3{x=-1}};
        Dispatch.Invoke(boss,new object[]{leader});
        Check(target.BotsGroup.RequestsController.Calls==2 && listener.BotsGroup.RequestsController.Calls==1,"Cooperation with nobody ahead has no recruitment effect");
        var stranger=new Player{ProfileId="stranger"};
        Dispatch.Invoke(boss,new object[]{stranger});
        Check(target.BotsGroup.RequestsController.Calls==2,"AIBoss receiver ignores another player's recruitment command");
        world.AllAlivePlayersList.Clear();
        Dispatch.Invoke(boss,new object[]{leader});
        Check(new GesturesQuickPanel().IsPhraseAvailable(EPhraseTrigger.Cooperation),"No candidate does not disable the phrase");
        return count;
    }
}
