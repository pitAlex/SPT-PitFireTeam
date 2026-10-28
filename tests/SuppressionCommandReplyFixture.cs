using System;
using System.Collections.Generic;
public enum EPhraseTrigger { Covering, Negative }
public enum EInteraction { OkGesture, NoGesture }
public enum EBotState { Active, NonActive }
public enum FollowerCommandType { SuppressEnemy }
public enum ETagStatus { Combat }
public static class Time { public static float time=100f; }
public class TalkSettings { public TalkSettings FileSettings=>this; public TalkSettings Mind=>this; public bool TALK_WITH_QUERY=true; }
public class GroupTalk { public bool Allowed; public int Checks; public bool CanSay(BotOwner bot,EPhraseTrigger phrase){Checks++;return Allowed;} }
public class Group { public GroupTalk GroupTalk=new GroupTalk(); }
public class Gesture
{
    public readonly List<EInteraction> Requests=new List<EInteraction>();
    public void TryGestus(EInteraction gesture,bool force){Requests.Add(gesture);}
}
public class BotOwner
{
    public bool IsDead; public EBotState BotState=EBotState.Active;
    public TalkSettings Settings=new TalkSettings(); public Group BotsGroup=new Group();
    public Gesture Gesture=new Gesture(); public BotTalk BotTalk;
    public BotOwner(){BotTalk=new BotTalk(this);}
}
public class BotTalk
{
    public readonly BotOwner _owner;
    public bool _canSay=true; public float _nextCanSayTime=-1;
    public readonly List<EPhraseTrigger> Queued=new List<EPhraseTrigger>();
    public BotTalk(BotOwner owner){_owner=owner;}
    public void TrySay(EPhraseTrigger phrase,bool groupDelay)=>TrySay(phrase,null,groupDelay);
    private ETagStatus GetMask(ETagStatus? mask)=>mask??ETagStatus.Combat;
    private void AddToQuery(EPhraseTrigger phrase,bool demand,float delay,ETagStatus mask){Queued.Add(phrase);}
    private void SayAndDelay(EPhraseTrigger phrase){Queued.Add(phrase);}
    /* NATIVE TRY SAY */
}
public static class BattleRecorder
{
    public static int Responses;
    public static void RecordCommandDiagnostic(BotOwner bot,FollowerCommandType command,string action,string reason,Func<object> details)
    {if(action!="responseRequested")throw new Exception("Speech request must not claim playback");Responses++;}
}
public class SuppressionReplies
{
    public static void Reply(BotOwner bot,bool accepted)=>ReplyToSuppressionCommand(bot,accepted,"test");
    /* PRODUCTION REPLY */
}
public static class SuppressionCommandReplyChecks
{
    public static int Run()
    {
        int checks=0;
        Action<bool,string> check=(ok,why)=>{checks++;if(!ok)throw new Exception(why);};
        foreach(bool accepted in new[]{false,true})
        foreach(bool queued in new[]{false,true})
        {
            var bot=new BotOwner();bot.Settings.TALK_WITH_QUERY=queued;
            bot.BotTalk.TrySay(accepted?EPhraseTrigger.Covering:EPhraseTrigger.Negative,true);
            check(bot.BotTalk.Queued.Count==0,"Native group cooldown reproduces dropped previous command response");
            int groupChecks=bot.BotsGroup.GroupTalk.Checks;
            SuppressionReplies.Reply(bot,accepted);
            check(bot.BotTalk.Queued.Count==1 && bot.BotTalk.Queued[0]==(accepted?EPhraseTrigger.Covering:EPhraseTrigger.Negative),"Explicit acceptance/refusal reaches native speech scheduling");
            check(bot.BotsGroup.GroupTalk.Checks==groupChecks,"Explicit reply does not query ambient group speech gate");
            check(bot.Gesture.Requests.Count==1 && bot.Gesture.Requests[0]==(accepted?EInteraction.OkGesture:EInteraction.NoGesture),"Visual acknowledgment matches admission result");
        }
        foreach(string state in new[]{"dead","inactive","cannotSpeak","noTalk","noGesture"})
        {
            var bot=new BotOwner();
            if(state=="dead")bot.IsDead=true;
            if(state=="inactive")bot.BotState=EBotState.NonActive;
            if(state=="cannotSpeak")bot.BotTalk._canSay=false;
            if(state=="noTalk")bot.BotTalk=null;
            if(state=="noGesture")bot.Gesture=null;
            int before=BattleRecorder.Responses;
            SuppressionReplies.Reply(bot,false);
            if(state=="dead" || state=="inactive")
                check(BattleRecorder.Responses==before && bot.Gesture.Requests.Count==0,"Unavailable actor cannot answer");
            else
            {
                check(BattleRecorder.Responses==before+1,"Available actor records request without claiming audible playback");
                if(state!="noGesture")check(bot.Gesture.Requests.Count==1,"Gesture survives unavailable/suppressed speech");
                if(state=="cannotSpeak")check(bot.BotTalk.Queued.Count==0,"Native actor speech gate remains authoritative");
            }
        }
        SuppressionReplies.Reply(null,false);
        return checks;
    }
}
