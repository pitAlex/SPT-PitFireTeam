using EFT;
using pitTeam.BigBrain.Actions;
using DrakiaXYZ.BigBrain.Brains;

int checks=0;
void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
foreach(string prefix in new[]{"knight.autonomousAdvance","knight.onYourOwn.pursuit","bigPipe.autonomousAdvance"})
foreach(float distance in new[]{12f,30f,30.01f,60f,100f,float.NaN})
foreach(bool canSprint in new[]{false,true})
foreach(bool noSprint in new[]{false,true})
{
    var bot=new BotOwner{CanSprintPlayer=canSprint};bot.Mover.NoSprint=noSprint;bot.Memory.GoalEnemy.Distance=distance;
    var action=new CombatGoToPointAction(bot);var data=new CustomLayer.ActionData{Reason=prefix+".runToPoint"};
    action.Update(data);
    Check(bot.Mover.Sprinting==(distance>30 && canSprint && !noSprint),"production approach sprint/close/mobility gate");
    Check(bot.NativeUpdates==1,"native movement provider executes exactly once");
    bot.Memory.GoalEnemy=null;action.Update(data);
    Check(!bot.Mover.Sprinting,"lost contact cannot keep assault sprint active");
}
foreach(string reason in new[]{"knight.autonomousAdvance.walkToPoint","bigPipe.autonomousAdvance.walkToPoint","ordinaryPoint","ordinary.runToPoint"})
{
    var bot=new BotOwner();bot.Memory.GoalEnemy.Distance=100;
    new CombatGoToPointAction(bot).Update(new(){Reason=reason});
    Check(!bot.Mover.Sprinting,"controlled walk and ordinary point movement retain native non-sprint result");
}
Console.WriteLine($"PASS: {checks} production point-action native-update, distant-run, close-walk and mobility checks.");

namespace DrakiaXYZ.BigBrain.Brains
{
    public class CustomLayer {public class ActionData {public string Reason;}}
}
namespace EFT
{
    public class CoreActionResultGoToPoint { }
    public class EnemyInfo {public float Distance;}
    public class Memory {public EnemyInfo GoalEnemy=new();}
    public class Movement {public bool IsInPronePose;}
    public class Player {public Movement MovementContext=new();}
    public class Mover {public bool NoSprint,Sprinting;}
    public class BotOwner
    {
        public Player GetPlayer=new();public Memory Memory=new();public Mover Mover=new();
        public bool CanSprintPlayer=true;public int NativeUpdates;
        public void SetPose(float value){} public void SetTargetMoveSpeed(float value){}
    }
    public class GoToSomePoint(BotOwner owner)
    {
        public void UpdateNodeByBrain(CoreActionResultGoToPoint data){owner.NativeUpdates++;owner.Mover.Sprinting=false;}
    }
}
namespace pitTeam.Modules {public static class FollowerEnemyTracking {public static float Distance(EnemyInfo e)=>e.Distance;}}
namespace pitTeam.BigBrain.Actions
{
    public abstract class FollowerCombatActionBase(BotOwner owner)
    {
        protected BotOwner BotOwner=owner;
        protected static string GetReason(CustomLayer.ActionData data)=>data.Reason;
        protected T GetData<T>(CustomLayer.ActionData data) where T:new()=>new();
        protected void SetCombatSprint(bool sprint)=>BotOwner.Mover.Sprinting=sprint;
        public abstract void Update(CustomLayer.ActionData data);
    }
}
