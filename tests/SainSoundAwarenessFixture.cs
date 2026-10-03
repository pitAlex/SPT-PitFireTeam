using System.Runtime.CompilerServices;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Components;
using SAIN.Components.PlayerComponentSpace;
using SAIN.Classes.Bot.Sense.Hearing;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace DrakiaXYZ.BigBrain.Brains {
    public abstract class CustomLogic {
        public BotOwner BotOwner {get;}
        protected CustomLogic(BotOwner owner){BotOwner=owner;}
        public abstract void Update(CustomLayer.ActionData data);
    }
}
namespace EFT {
    public partial class Player {public Vector3 LookDirection=new Vector3(-1,0,0);}
    public class PeaceMover {public bool Sprinting;}
    public partial class BotOwner {public PeaceMover Mover=new PeaceMover();public SAIN.Components.Steering Steering=new SAIN.Components.Steering();public bool Suppressed;}
}
namespace pitTeam.Components {
    public partial class BotFollowerPlayer {
        public bool IsBackpackInspectionActive,CommandLook;public Vector3 CommandLookPoint;
        public bool TryGetCommandLookOverride(out Vector3 point){point=CommandLookPoint;return CommandLook;}
    }
}
namespace pitTeam.Modules {
    public static class FollowerEnemyEnforceSuppression {public static bool IsSuppressed(BotOwner bot)=>bot.Suppressed;}
}
namespace pitTeam.BigBrain.Actions {
    public class FollowAction : CustomLogic {
        public FollowAction(BotOwner owner):base(owner){}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void Update(CustomLayer.ActionData data){BotOwner.Steering.LookToPoint(new Vector3(0,0,100));}
    }
    public class GestureCommandAction : CustomLogic {
        public GestureCommandAction(BotOwner owner):base(owner){}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void Update(CustomLayer.ActionData data){BotOwner.Steering.LookToPoint(BotOwner.Follower.CommandLook?BotOwner.Follower.CommandLookPoint:new Vector3(0,0,100));}
    }
}
namespace SAIN.Components.PlayerComponentSpace {
    public struct AISoundData {
        public BotComponent Bot;public Enemy Enemy;public Vector3 Position;public float PlayerDistance;public SAINSoundType SoundType;
        public bool IsGunShot=>SoundType==SAINSoundType.Shot||SoundType==SAINSoundType.SuppressedShot;
    }
}
namespace SAIN.Classes.Bot.Sense.Hearing {
    public class HearingAnalysis {
        public bool Audible=true;public int Calls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool CheckIfSoundHeard(AISoundData sound){Calls++;return Audible;}
    }
}
public static partial class CombatChecks {
    private static BotOwner SoundBot(string id) {
        var b=PreparationBot(id,new Vector3(20,0,0),ECombatDecision.Search);
        b.Sain.ActiveLayer=SAIN.Models.Enums.ESAINLayer.None;
        return b;
    }
    private static AISoundData Sound(BotOwner b,SAINSoundType type,float x=20,float y=0) =>
        new AISoundData{Bot=b.Sain,Enemy=b.Sain.GoalEnemy,Position=new Vector3(x,y,0),PlayerDistance=new Vector3(x,y,0).magnitude,SoundType=type};
    private static void TestSoundAwareness() {
        var hearing=new HearingAnalysis();
        var b=SoundBot("heardOrientation");var awareness=SAINFollowerRuntime.GetSoundAwareness(b);
        var follow=new pitTeam.BigBrain.Actions.FollowAction(b);var hold=new pitTeam.BigBrain.Actions.GestureCommandAction(b);
        var sound=Sound(b,SAINSoundType.FootStep);int paths=UnityEngine.AI.NavMesh.Calculations;
        hearing.Audible=false;Check(!hearing.CheckIfSoundHeard(sound)&&hearing.Calls==1,"sound observer retains native failed audibility without rerunning it");
        follow.Update(null);Check(b.Steering.LookPoint.z==100,"inaudible steps cannot override follow facing");
        hearing.Audible=true;Check(hearing.CheckIfSoundHeard(sound)&&hearing.Calls==2,"sound observer preserves one native accepted hearing evaluation");
        follow.Update(null);Check(b.Steering.LookPoint.x==20,"heard steps retain direction after peaceful follow steering");
        b.Follower.Command=FollowerCommandType.HoldPosition;hold.Update(null);
        Check(b.Steering.LookPoint.x==20,"Hold scanning cannot overwrite a fresh sound bearing");
        b.Sain.GoalEnemy.EnemyPosition=new Vector3(-40,0,0);Time.time+=1;hold.Update(null);
        Check(b.Steering.LookPoint.x==20,"sound-facing holds the emitted position instead of tracking hidden movement");
        Check(b.Memory.GoalEnemy==null&&b.Sain.Decision.CurrentCombatDecision==ECombatDecision.None&&UnityEngine.AI.NavMesh.Calculations==paths,
            "heard orientation creates no goal, combat decision or route");
        Time.time+=2.1f;hold.Update(null);Check(b.Steering.LookPoint.z==100,"sound-facing expires after three seconds");

        foreach(var type in new[]{SAINSoundType.Bush,SAINSoundType.FootStep,SAINSoundType.Sprint,SAINSoundType.Conversation}) {
            awareness.Clear();sound=Sound(b,type,25);hearing.CheckIfSoundHeard(sound);hold.Update(null);
            Check(b.Steering.LookPoint.x==25,"local sound accepted at 25 metres: "+type);
            awareness.Clear();sound=Sound(b,type,25.1f);hearing.CheckIfSoundHeard(sound);hold.Update(null);
            Check(b.Steering.LookPoint.z==100,"local sound outside 25 metres rejected: "+type);
        }
        sound=Sound(b,SAINSoundType.Bush,10,-8);hearing.CheckIfSoundHeard(sound);hold.Update(null);
        Check(b.Steering.LookPoint.y==-7&&b.Memory.GoalEnemy==null&&UnityEngine.AI.NavMesh.Calculations==paths,
            "audible downstairs movement keeps its elevation without a route or acquisition");
        awareness.Clear();sound=Sound(b,SAINSoundType.Shot,120,1);sound.Enemy.EnemyPlayer.LookDirection=new Vector3(1,0,0);
        hearing.CheckIfSoundHeard(sound);hold.Update(null);Check(b.Steering.LookPoint.z==100,"audible distant gunfire facing away is ignored");
        sound.Enemy.EnemyPlayer.LookDirection=new Vector3(-1,0,0);hearing.CheckIfSoundHeard(sound);hold.Update(null);
        Check(b.Steering.LookPoint.x==120&&b.Memory.GoalEnemy==null,"audible distant shot toward follower turns without a goal");
        var incidental=Sound(b,SAINSoundType.FootStep,10);hearing.CheckIfSoundHeard(incidental);hold.Update(null);
        Check(b.Steering.LookPoint.x==120,"incidental movement cannot downgrade a directed-shot bearing, even from the same enemy");
        b.Follower.CommandLook=true;b.Follower.CommandLookPoint=new Vector3(0,0,-100);hold.Update(null);
        Check(b.Steering.LookPoint.z==-100,"explicit player look overrides acoustic facing");b.Follower.CommandLook=false;
        b.Mover.Sprinting=true;hold.Update(null);Check(b.Steering.LookPoint.z==100,"actual sprint preserves movement-facing");b.Mover.Sprinting=false;
        b.Memory.GoalEnemy=new EnemyInfo();hold.Update(null);Check(b.Steering.LookPoint.z==100&&awareness.Contact==null,"accepted combat clears peaceful sound ownership");b.Memory.GoalEnemy=null;

        b.Leader.Position=new Vector3(120,0,50);sound.Enemy.EnemyPlayer.LookDirection=new Vector3(0,0,1);
        hearing.CheckIfSoundHeard(sound);hold.Update(null);Check(b.Steering.LookPoint.x==120,"shot toward the player turns a follower outside the shooter's cone");
        awareness.Clear();b.Leader.Position=Vector3.zero;
        var sibling=SoundBot("soundSibling");sibling.GetPlayer.Position=new Vector3(120,0,50);
        ((pitAIBossPlayer)b.BotFollower.BossToFollow).Followers.Add(sibling);
        hearing.CheckIfSoundHeard(sound);hold.Update(null);Check(b.Steering.LookPoint.x==120,"shot toward another live squadmate also turns the listener");
        Check(!SainFollowerSoundAwareness.Faces(new Vector3(0,-12,0),new Vector3(1,0,0),new Vector3(1,1,0)),
            "horizontal underground fire does not count as aimed at the squad above");

        awareness.Clear();sound=Sound(b,SAINSoundType.FootStep);hearing.CheckIfSoundHeard(sound);
        SAINFollowerRuntime.RememberAttentionContacts(b);hold.Update(null);Check(b.Steering.LookPoint.z==100,"Attention clears an active sound hold");
        hearing.CheckIfSoundHeard(sound);hold.Update(null);Check(b.Steering.LookPoint.z==100,"Attention prevents renewed local sound facing in the same sector");
        sound=Sound(b,SAINSoundType.SuppressedShot,120,1);sound.Enemy.EnemyPlayer.LookDirection=new Vector3(-1,0,0);
        hearing.CheckIfSoundHeard(sound);hold.Update(null);Check(b.Steering.LookPoint.x==120,"new directed gunfire remains relevant after a dismissed sound contact");
        awareness.Clear();b.Suppressed=true;hearing.CheckIfSoundHeard(sound);hold.Update(null);Check(b.Steering.LookPoint.z==100,"Core immediate Attention suppression remains authoritative");b.Suppressed=false;
        b.Leader.Position=new Vector3(11,0,0);sound=Sound(b,SAINSoundType.Bush);hearing.CheckIfSoundHeard(sound);hold.Update(null);
        Check(b.Steering.LookPoint.x==20,"player sector change re-enables local sound facing");
        b.UsingMedical=true;hold.Update(null);Check(b.Steering.LookPoint.z==100&&awareness.Contact==null,"medical ownership clears sound hold");b.UsingMedical=false;
        b.BotsGroup.Enemies.Clear();hearing.CheckIfSoundHeard(sound);hold.Update(null);
        Check(b.Steering.LookPoint.z==100,"native sound alone cannot make a friendly or neutral actor a threat");b.BotsGroup.Enemies[sound.Enemy.EnemyPlayer]=new BotGroupEnemyInfo();
        hearing.CheckIfSoundHeard(sound);sound.Enemy.EnemyPlayer.HealthController.IsAlive=false;hold.Update(null);
        Check(b.Steering.LookPoint.z==100&&awareness.Contact==null,"source death clears sound facing");sound.Enemy.EnemyPlayer.HealthController.IsAlive=true;
        b.Follower.Command=FollowerCommandType.TakeBodyGear;hearing.CheckIfSoundHeard(sound);hold.Update(null);Check(b.Steering.LookPoint.z==100,"sound facing does not overwrite loot interaction aiming");b.Follower.Command=FollowerCommandType.HoldPosition;
        b.Follower.IsBackpackInspectionActive=true;hearing.CheckIfSoundHeard(sound);hold.Update(null);Check(b.Steering.LookPoint.z==100,"backpack inspection preserves its own facing");b.Follower.IsBackpackInspectionActive=false;
        b.Follower.CombatIndependent=true;hearing.CheckIfSoundHeard(sound);hold.Update(null);Check(b.Steering.LookPoint.z==100,"On Your Own retains native combat ownership");b.Follower.CombatIndependent=false;
        b.Sain.ActiveLayer=SAIN.Models.Enums.ESAINLayer.Combat;hearing.CheckIfSoundHeard(sound);hold.Update(null);Check(b.Steering.LookPoint.z==100,"active native action retains steering ownership");b.Sain.ActiveLayer=SAIN.Models.Enums.ESAINLayer.None;
        b.Follower.CombatTactic=FollowerCombatTactic.Balanced;hearing.CheckIfSoundHeard(sound);hold.Update(null);Check(b.Steering.LookPoint.z==100,"Core tactic is excluded from addon sound hooks");b.Follower.CombatTactic=FollowerCombatTactic.SAINShooter;Tick();
        hearing.CheckIfSoundHeard(sound);hold.Update(null);Check(b.Steering.LookPoint.x==20,"SAINShooter receives the same peaceful hearing response");
        int recorded=BattleRecorder.Events.FindAll(e=>e.Kind=="sainSoundReaction").Count;
        int checkedSounds=hearing.Calls;paths=UnityEngine.AI.NavMesh.Calculations;
        for(int i=0;i<100;i++){hearing.CheckIfSoundHeard(sound);hold.Update(null);}
        Check(hearing.Calls==checkedSounds+100&&UnityEngine.AI.NavMesh.Calculations==paths,
            "repeated accepted sounds add no hearing reevaluation or navigation work");
        Check(BattleRecorder.Events.FindAll(e=>e.Kind=="sainSoundReaction").Count==recorded,
            "a sound burst cannot generate per-frame diagnostic events");
        SainAddonBridge.RaiseFollowerLifecycleEvent(b,FollowerLifecycleEvent.OnDismiss);hold.Update(null);
        Check(b.Steering.LookPoint.z==100,"dismissal releases acoustic facing");
        var ordinary=Spawn("ordinarySound",FollowerCombatTactic.Balanced);var ordinarySound=Sound(ordinary,SAINSoundType.Shot);
        Check(hearing.CheckIfSoundHeard(ordinarySound),"ordinary bot audibility remains native");
    }
}
