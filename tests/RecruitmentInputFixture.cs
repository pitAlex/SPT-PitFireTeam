using System;
using System.Reflection;
using EFT;
using pitTeam.Modules;
using pitTeam.Patches;
using UnityEngine;

public enum BodyPartType { head, body }
public class BodyPart { public Vector3 Position; }
public class ShootToPoint { public Vector3 Point; public ShootToPoint(Vector3 point, int coefficient) { Point = point; } }
public static class LayersMaskController { public const int HighPolyWithTerrainMask = 1; }

namespace pitTeam.Patches {
    public enum CustomPhrases { TeamStatus=1000 }
    internal class QuickPanelPatch { __COOPERATION_AVAILABILITY__ }
    internal class CreatePhraseGroupPatch { __HELP_GROUP_METHOD__ }
}

public static class RecruitmentInputChecks {
    private static int count;
    private static void Check(bool value,string description) { count++;if(!value)throw new Exception(description); }
    private static readonly MethodInfo Prefix=typeof(BotReceiverRecruitPatch).GetMethod("PatchPrefix",BindingFlags.NonPublic|BindingFlags.Static);
    private static bool Receive(BotOwner bot,Player player,EPhraseTrigger phrase) =>
        (bool)Prefix.Invoke(null,new object[]{new BotReceiver{_owner=bot},new GlobalEventDispatcher.PhraseDelegateInfo{PlayerRequester=player,phrase=phrase}});

    public static int Run() {
        foreach(bool sain in new[]{false,true}) foreach(bool allegiance in new[]{false,true}) {
            pitTeam.pitFireTeam.IsSAINInstalled=sain;GameplayModeRuntime.IsAllegiance=allegiance;
            pitTeam.pitFireTeam.pickupEnabled.Value=true;AllegiancePmcFriendship.Allowed=true;
            pitTeam.Utils.Utils.HeadVisible=true;pitTeam.Utils.Utils.BodyVisible=true;
            var player=BossPlayers.Boss.Value;player.Side=EPlayerSide.Usec;player.Position=default;player.InteractablePlayer=null;
            var bear=new BotOwner{Side=EPlayerSide.Bear,BotsGroup=new Group()};
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
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==0,"Targeted Cooperation excludes other listeners");
            player.InteractablePlayer=null;
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==1,"Help-menu Cooperation without a selected target reaches nearby candidates");
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
            other.GetPlayer.Position=new Vector3{x=5};
            Check(!Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==4,"Visible candidate at exactly five metres remains eligible");
            int sightChecks=pitTeam.Utils.Utils.SightChecks;
            other.GetPlayer.Position=new Vector3{x=5.01f};
            Check(Receive(other,player,EPhraseTrigger.Cooperation) && other.BotsGroup.RequestsController.Calls==4,"Recruitment phrase rejects candidates just beyond five metres");
            Check(pitTeam.Utils.Utils.SightChecks==sightChecks,"Out-of-range listeners do not perform sight checks");
            Check(Receive(bear,player,EPhraseTrigger.NeedHelp) && bear.BotsGroup.RequestsController.Calls==1,"Need Help keeps its existing support behavior");
        }
        var help=new[]{EPhraseTrigger.NeedHelp};CreatePhraseGroupPatch.AddCooperationToHelpGroup("HELP",ref help);
        Check(help.Length==2 && help[0]==EPhraseTrigger.NeedHelp && help[1]==EPhraseTrigger.Cooperation,"Help menu keeps support and adds Cooperation");
        CreatePhraseGroupPatch.AddCooperationToHelpGroup("HELP",ref help);Check(help.Length==2,"Help menu does not duplicate Cooperation");
        var commands=new[]{EPhraseTrigger.FollowMe};CreatePhraseGroupPatch.AddCooperationToHelpGroup("COMMAND",ref commands);
        Check(commands.Length==1 && commands[0]==EPhraseTrigger.FollowMe,"Other menu groups keep their commands");
        return count;
    }
}
