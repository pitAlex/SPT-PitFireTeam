using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    private static void Main()
    {
        var human=new pitAIBossPlayer();human.realPlayer.Position=new(0,0,0);
        BotOwner Add(float x)
        {
            var bot=new BotOwner{Position=new(x,0,0)};bot.BotFollower.BossToFollow=human;
            BossPlayers.Instance.Followers[bot]=new(){Owner=bot,Leader=human,CombatIndependent=true};
            human.Followers.Add(bot);return bot;
        }
        var knight=Add(100);var pipe=Add(3);var bird=Add(5);var ordinary=Add(4);
        foreach(var member in new[]{pipe,bird})
        {
            var f=BossPlayers.Instance.GetFollower(member);f.CompanionCombatLeader=knight;
            Check(FollowerCombatAnchor.GetAnchorPosition(member).x==100,"unit anchor is Knight even with nearer player-side allies");
            Check(FollowerCombatAnchor.GetRealBossPosition(member).x==0,"real player anchor remains available for commands and safety");
            var follow=new FollowHarness(member);
            Check(follow.Resolve()&&ReferenceEquals(follow.Leader,knight.GetPlayer),"actual peaceful FollowAction binds to Knight without inventing contact");
            Check(ReferenceEquals(follow.Group,human)&&ReferenceEquals(member.BotFollower.BossToFollow,human),"following Knight does not reparent ownership or native boss link");
            knight.Position=new(150,2,0);
            Check(FollowerCombatAnchor.GetAnchorPosition(member).x==150&&follow.Resolve()&&follow.Leader.Position.y==2,
                "Knight movement is read live instead of retaining an old player or leader point");
            knight.Position=new(100,0,0);
            foreach(string command in new[]{"pending","consumedRegroup","coverMe"})
            {
                f.PendingCommand=command=="pending";f.CombatRegroupUsesBossAnchor=command=="consumedRegroup";
                f.CombatIndependent=command!="coverMe";
                Check(FollowerCombatAnchor.GetAnchorPosition(member).x==0,"explicit player authority retains the player combat anchor: "+command);
                Check(follow.Resolve()&&ReferenceEquals(follow.Leader,human.realPlayer),"pending/consumed commands retain ordinary player following: "+command);
                f.PendingCommand=false;f.CombatRegroupUsesBossAnchor=false;f.CombatIndependent=true;
            }
            foreach(string invalid in new[]{"dead","inactive","health","unknown","foreign","nan"})
            {
                var k=BossPlayers.Instance.GetFollower(knight);
                if(invalid=="dead")knight.IsDead=true;
                if(invalid=="inactive")knight.BotState=EBotState.NonActive;
                if(invalid=="health")knight.GetPlayer.HealthController.IsAlive=false;
                if(invalid=="unknown")BossPlayers.Instance.Followers.Remove(knight);
                if(invalid=="foreign")k.Leader=new pitAIBossPlayer();
                if(invalid=="nan")knight.Position=new(float.NaN,0,0);
                Check(!FollowerCombatAnchor.TryGetCompanionLeader(member,out _),"invalid unit leader cannot supply an anchor: "+invalid);
                Check(follow.Resolve()&&ReferenceEquals(follow.Leader,human.realPlayer),"invalid leader safely releases ordinary following: "+invalid);
                knight.IsDead=false;knight.BotState=EBotState.Active;knight.GetPlayer.HealthController.IsAlive=true;
                k.Leader=human;BossPlayers.Instance.Followers[knight]=k;knight.Position=new(100,0,0);
            }
            f.CompanionCombatLeader=null;
        }
        BossPlayers.Instance.GetFollower(knight).CompanionCombatLeader=knight;
        Check(FollowerCombatAnchor.GetAnchorPosition(knight).x==100,"unit commander anchors independent combat to his own position");
        var head=new FollowHarness(knight);
        Check(head.Resolve()&&ReferenceEquals(head.Leader,human.realPlayer),"Knight continues following the human during ordinary peaceful movement");
        var plain=BossPlayers.Instance.GetFollower(ordinary);plain.PendingCommand=true;
        Check(FollowerCombatAnchor.GetAnchorPosition(ordinary).x==3,"ordinary independent follower retains nearest-ally anchoring");
        var ordinaryFollow=new FollowHarness(ordinary);
        Check(ordinaryFollow.Resolve()&&ReferenceEquals(ordinaryFollow.Leader,human.realPlayer),"ordinary follower retains player following");
        var marksman=new MarksmanRegroupHarness(bird);
        var birdState=BossPlayers.Instance.GetFollower(bird);
        birdState.CompanionCombatLeader=knight;
        Check(!marksman.NeedsRegroup()&&marksman.DistanceReads==0,"independent sniper cannot start automatic player-distance regroup or collapse native sniper spacing");
        birdState.CompanionCombatLeader=null;
        Check(!marksman.NeedsRegroup()&&marksman.DistanceReads==0,"ordinary independent sniper shares the existing no-automatic-regroup contract");
        birdState.CombatIndependent=false;
        Check(marksman.NeedsRegroup()&&marksman.DistanceReads==1,"dependent Marksman keeps normal distance-based regroup");
        foreach(string mode in new[]{"obsolete","explicit","dependent","nonAutomatic","medical","medicineUsing","reload","weaponDraw","handsReload","grenade","airborne","unrelated"})
        {
            var f=BossPlayers.Instance.GetFollower(pipe);f.CombatIndependent=mode!="dependent";
            f.CombatRegroupUsesBossAnchor=mode=="explicit";
            var regroup=new AutomaticRegroupHarness(pipe,mode!="nonAutomatic");
            var current=new AICoreActionResult<BotLogicDecision,CoreActionResultParams>(
                mode=="medical"?BotLogicDecision.heal:BotLogicDecision.goToPoint,
                mode=="unrelated"?"other.move":"regroup.run");
            pipe.Medecine.Using=mode=="medicineUsing";pipe.WeaponManager.Reload.Reloading=mode=="reload";
            pipe.WeaponManager.Selector.IsChanging=mode=="weaponDraw";pipe.WeaponManager.ShootController.Reloading=mode=="handsReload";
            pipe.WeaponManager.Grenades.ThrowindNow=mode=="grenade";pipe.GetPlayer.MovementContext.IsGrounded=mode!="airborne";
            var end=regroup.ShallEndCurrentDecision(current);
            Check(end.Value==(mode=="obsolete")&&regroup.Complete==(mode=="obsolete"),"only obsolete safe automatic regroup completes: "+mode);
            Check(regroup.Clears==(mode=="obsolete"?2:0),"regroup transition clears only its own state after a valid release: "+mode);
            Check(regroup.MedicalEnds==(mode=="medical"?1:0),"medical action keeps its existing owner before independence release: "+mode);
            pipe.Medecine.Using=pipe.WeaponManager.Reload.Reloading=pipe.WeaponManager.Selector.IsChanging=
                pipe.WeaponManager.ShootController.Reloading=pipe.WeaponManager.Grenades.ThrowindNow=false;
            pipe.GetPlayer.MovementContext.IsGrounded=true;
        }
        Console.WriteLine($"PASS: {checks} production combat/peaceful companion anchor and FollowAction binding checks; controlled EFT inputs.");
    }
}

partial class MarksmanRegroupHarness(BotOwner owner)
{
    private BotOwner BotOwner=owner;
    private AnchorCommon CombatCommon=new(owner);
    private static bool IsFinite(Vector3 point)=>float.IsFinite(point.x)&&float.IsFinite(point.y)&&float.IsFinite(point.z);
    public int DistanceReads=>CombatCommon.DistanceReads;
    public bool NeedsRegroup()=>ShouldRegroupForBossDistance();
}
class AnchorCommon(BotOwner owner)
{
    public int DistanceReads;
    public Vector3 GetBossPosition()=>FollowerCombatAnchor.GetAnchorPosition(owner);
    public float GetBossNavDistance(Vector3 point){DistanceReads++;return 1000;}
    public bool ShouldDeferAutonomousRegroupAfterRecentFight(object enemy,float distance,float trigger)=>false;
}
partial class AutomaticRegroupHarness(BotOwner owner,bool automatic)
{
    private BotOwner BotOwner=owner;private bool automaticRegroup=automatic,complete;
    private RegroupEndCommon CombatCommon=new();
    public int Clears;public bool Complete=>complete;public int MedicalEnds=>CombatCommon.MedicalEnds;
    private static bool IsRegroupReason(string reason)=>reason?.StartsWith("regroup.")==true;
    private void ClearCurrentTarget()=>Clears++;
    private void ClearCommittedRegroupMove()=>Clears++;
}
class RegroupEndCommon
{
    public int MedicalEnds;
    public AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision,CoreActionResultParams> current)
    {MedicalEnds++;return new("medicalOwned",false);}
}

partial class FollowHarness(BotOwner owner)
{
    private BotOwner BotOwner=owner;
    private Player bossPlayer;
    private pitAIBossPlayer bossData;
    public Player Leader=>bossPlayer;
    public pitAIBossPlayer Group=>bossData;
    public bool Resolve()=>TryGetBossAndPlayer();
}
namespace UnityEngine
{
    public struct Vector3(float x,float y,float z)
    {
        public float x=x,y=y,z=z;public static Vector3 zero=>default;
        public float sqrMagnitude=>x*x+y*y+z*z;
        public static Vector3 operator-(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static float Distance(Vector3 a,Vector3 b)=>MathF.Sqrt((a-b).sqrMagnitude);
    }
    public class Transform {public Vector3 position;}
}
namespace EFT
{
    public enum EBotState {Active,NonActive}
    public class Health {public bool IsAlive=true;}
    public class MovementContext {public bool IsGrounded=true;}
    public class Player
    {
        public MovementContext MovementContext=new();public Transform Transform=new();public Health HealthController=new();
        public Vector3 Position{get=>Transform.position;set=>Transform.position=value;}
        public class FirearmController {public bool Reloading;public bool IsInReloadOperation()=>Reloading;}
    }
    public class Boss {public Vector3 Position;}
    public class Follower {public Boss BossToFollow;}
    public class EnemyInfo{}
    public class Memory {public EnemyInfo GoalEnemy;}
    public class Medicine {public bool Using;}
    public class Reload {public bool Reloading;}
    public class Selector {public bool IsChanging;}
    public class Grenades {public bool ThrowindNow,ReadyToThrow;}
    public class Weapons {public Reload Reload=new();public Selector Selector=new();public Grenades Grenades=new();public Player.FirearmController ShootController=new();}
    public class BotOwner
    {
        public bool IsDead;public Medicine Medecine=new();public Weapons WeaponManager=new();public Memory Memory=new();public EBotState BotState=EBotState.Active;public Player GetPlayer=new();public Follower BotFollower=new();
        public Vector3 Position{get=>GetPlayer.Position;set=>GetPlayer.Position=value;}
    }
}
namespace pitTeam.Components
{
    public class pitAIBossPlayer:Boss {public Player realPlayer=new();public List<BotOwner> Followers=new();}
    public class BotFollowerPlayer
    {
        public BotOwner Owner,CompanionCombatLeader;public pitAIBossPlayer Leader;
        public bool CombatIndependent,CombatRegroupUsesBossAnchor,PendingCommand;
        public pitAIBossPlayer GetBoss()=>Leader;
        public bool TryPeekActiveCommand(out int command,out Vector3 target,out float until){command=0;target=default;until=0;return PendingCommand;}
    }
}
namespace pitTeam.Modules
{
    class CombatDistanceConfiguration
    {
        public static CombatDistanceConfiguration Instance=new();
        public bool IsUrbanDetourRegroup(float direct,float nav)=>false;
        public float GetRegroupNeededDistanceMarksman(BotOwner bot)=>54;
    }
    public class BossPlayers
    {
        public static BossPlayers Instance=new();public Dictionary<BotOwner,BotFollowerPlayer> Followers=new();
        public BotFollowerPlayer GetFollower(BotOwner bot)=>bot!=null?Followers.GetValueOrDefault(bot):null;
    }
}
namespace pitTeam.BigBrain
{
    class FollowerCombatCommon
    {
        public static float GetSafeRegroupDistance(float nav,float direct)=>MathF.Max(nav,direct);
        public static bool IsMedicalDecision(AICoreActionResult<BotLogicDecision,CoreActionResultParams> current)=>current.Action==BotLogicDecision.heal;
        public static bool IsReloadHoldReason(string reason)=>reason=="reload";
        public static bool IsWeaponPreparationHoldReason(string reason)=>reason=="weaponSwitch";
    }
}
public enum BotLogicDecision {goToPoint,heal}
public class CoreActionResultParams{}
public struct AICoreActionResult<T,D>(T action,string reason) {public T Action=action;public string Reason=reason;}
public struct AICoreActionEnd(string reason,bool value) {public string Reason=reason;public bool Value=value;}
