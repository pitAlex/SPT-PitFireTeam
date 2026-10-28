#nullable enable annotations
#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using pitTeam.Modules;
using pitTeam.Components;
using pitTeam.BigBrain;
using pitTeam.GoingRogue;
using Modules = pitTeam.Modules;
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public float sqrMagnitude=>x*x+y*y+z*z;
        public static Vector3 zero=>default;
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public static class Time {public static float time=100;}
    public static class Random {public static float value=0.5f;}
    public static class Mathf
    {
        public static float Max(float a,float b)=>Math.Max(a,b);public static int Max(int a,int b)=>Math.Max(a,b);
        public static float Clamp(float v,float min,float max)=>Math.Max(min,Math.Min(max,v));
        public static float Clamp01(float v)=>Clamp(v,0,1);
        public static float InverseLerp(float a,float b,float v)=>Clamp01((v-a)/(b-a));
    }
}
public enum EBotState { Active, NonActive }
public enum WildSpawnType { assault, bossKnight, followerBigPipe, followerBirdEye, pmcUSEC, pmcBEAR, bossSanitar }
public enum EPhraseTrigger { Going, Negative }
public enum EInteraction { OkGesture, NoGesture }
public enum FollowerCommandType { None, MoveToPoint, CombatMoveToPointTactical, PushEnemy }
public class ProfileSettings {public WildSpawnType Role;}
public class Info {public ProfileSettings Settings=new ProfileSettings();public int Level=79;}
public class Profile {public Info Info=new Info();public string Nickname="test";}
public class Health {public bool IsAlive=true;}
public class AIData {public float PowerOfEquipment=100;public BotOwner BotOwner;}
public interface IPlayer {Vector3 Position{get;}Profile Profile{get;}AIData AIData{get;}}
public class Player:IPlayer
{
    public Vector3 Position{get;set;}public Profile Profile{get;}=new Profile();public AIData AIData{get;}=new AIData();
    public Health HealthController=new Health();public string ProfileId="enemy";
}
public class EnemyInfo
{
    public Player Person=new Player();public string ProfileId=>Person.ProfileId;public bool Eligible=true,Known=true;
    public Vector3 Point;public bool IsVisible;public float PersonalLastSeenTime;
}
public class Memory {public bool HaveEnemy=true;public EnemyInfo GoalEnemy=new EnemyInfo();}
public class Enemies {public Dictionary<string,EnemyInfo> EnemyInfos=new Dictionary<string,EnemyInfo>();}
public class Talk {public List<EPhraseTrigger> Requests=new List<EPhraseTrigger>();public void TrySay(EPhraseTrigger phrase,bool group){Requests.Add(phrase);}}
public class Gesture {public List<EInteraction> Requests=new List<EInteraction>();public void TryGestus(EInteraction gesture,bool force){Requests.Add(gesture);}}
public class BotOwner
{
    public bool IsDead,Locked,React=true;public EBotState BotState=EBotState.Active;public Vector3 Position;
    public Player GetPlayer=new Player();public Profile Profile=>GetPlayer.Profile;public AIData AIData=>GetPlayer.AIData;
    public Memory Memory=new Memory();public Enemies EnemiesController=new Enemies();
    public Talk BotTalk=new Talk();public Gesture Gesture=new Gesture();public string ProfileId="follower";
    public BotFollowerPlayer Follower=new BotFollowerPlayer();
}
namespace pitTeam.Components
{
    public enum FollowerCombatTactic {Balanced,Marksman,SainMan,SAINShooter}
    public static class FollowerCombatTactics
    {public static bool IsMarksman(FollowerCombatTactic t)=>t==FollowerCombatTactic.Marksman||t==FollowerCombatTactic.SAINShooter;}
    public class Boss {public Player realPlayer=new Player();}
    public class BotFollowerPlayer
    {
        public bool IsSquadMate;public float PickupIndependence01=0.5f;public FollowerCombatTactic CombatTactic;
        public int Pushes,Points,Moves,AggressionChanges;public Vector3 Target;public FollowerCommandType Command;
        public Boss GetBoss()=>new Boss();
        public void SetPushEnemy(float seconds){Pushes++;Command=FollowerCommandType.PushEnemy;}
        public bool TrySetSainPushEnemy(SainPushOrder order,EnemyInfo info){SetPushEnemy(12);return true;}
        public void SetTemporaryCombatAggressionOverride(float value,string source){AggressionChanges++;}
        public void SetCombatMoveToPointTactical(Vector3 point,float seconds){Target=point;Points++;Command=FollowerCommandType.CombatMoveToPointTactical;}
        public void SetMoveToPoint(Vector3 point,float seconds){Target=point;Moves++;Command=FollowerCommandType.MoveToPoint;}
    }
}
public class BossPlayers {public static BossPlayers Instance=new BossPlayers();public BotFollowerPlayer GetFollower(BotOwner bot)=>bot.Follower;}
public class SainPushOrder {public SainPushOrder(string id,Vector3 point){}}
public class SainPushTargetCandidate {public Player Enemy=new Player();public Vector3 LastKnownPosition;}
public static class SainAddonBridge {public static bool IsSainManSelected(BotOwner bot)=>false;public static bool IsFollowerCombatEnabled(BotOwner bot)=>false;}
namespace pitTeam.Modules
{
    public static class GameplayModeAddons
    {public static bool Registered=true,KnightPresent=true;public static Func<CompanionAdvanceOrderContext,bool?> Leadership;public static bool? EvaluateAdvanceOrder(CompanionAdvanceOrderContext context)=>Leadership?.Invoke(context);public static CompanionCommandPolicy GetCompanionCommandPolicy(string role)=>Registered?GoonCommandPolicy.ForRole(role,KnightPresent):null;}
    public static class FollowerEnemyTracking
    {
        public static bool IsEligible(EnemyInfo enemy)=>enemy.Eligible;
        public static bool TryGetKnownPosition(EnemyInfo enemy,out Vector3 position,out float at){position=enemy.Point;at=100;return enemy.Known;}
    }
    public static class BattleRecorder
    {public static void RecordCommandDiagnostic(BotOwner bot,FollowerCommandType command,string action,string reason,Func<object> details){}}
    public static class Logger {public static void LogInfo(string message){}}
}
namespace pitTeam.BigBrain
{
    public static class FollowerCombatRiflemanEngagement
    {public static float GetCombatRoleThreatMultiplier(WildSpawnType role)=>role==WildSpawnType.bossSanitar?1.5f:1f;}
}
public class AdvanceOrders
{
    public List<BotOwner> Followers=new List<BotOwner>();public BotOwner Focused;
    public Vector3 WorldPoint=new Vector3(10,0,0);public bool PointValid=true;public int LookOverrides;
    private float _nextThereGestureAt;private int _advanceOrderSequence;private const float PhraseCommandDistance=30,GestureCommandDistance=20,CombatThereMaxDistance=30;
    public void There(Player player)=>ApplyThereGesture(player);public void Forward(Player player)=>ApplyGoForwardPhrase(player);
    public static bool Accept(BotOwner bot,Player player,bool point)=>TryAcceptCombatAdvanceOrder(bot,bot.Follower,player,bot.Memory.GoalEnemy,point);
    private BotOwner FindLookedAtFollower(Player player,float distance)=>Focused;
    private SainPushTargetCandidate SelectSainPushTarget(IPlayer player,BotOwner focused)=>null;
    private EnemyInfo GetSainPushTargetInfo(BotOwner bot,Player enemy)=>null;
    private static bool IsFollowerLockedByLootCommand(BotOwner bot)=>bot.Locked;
    private static bool CanReactToBossGesture(BotOwner bot,IPlayer player)=>bot.React;
    private bool TryGetGoToCommandTarget(IPlayer player,out Vector3 point){point=WorldPoint;return PointValid;}
    private bool TryGetGoToCommandTarget(IPlayer player,float distance,out Vector3 point)=>TryGetGoToCommandTarget(player,out point);
    private static bool CanAcceptThereCommand(BotOwner bot)=>!bot.Memory.HaveEnemy;
    private static Vector3 GetMoveToPointPresentationLookTarget(BotOwner bot,IPlayer player,Vector3 point)=>point;
    private void ApplyFollowerLookOverride(BotOwner bot,Vector3 point){LookOverrides++;}
    private static void RecordCommandSourceDiagnostic(BotOwner bot,FollowerCommandType command,string source,string reason,IPlayer player,Vector3? target,bool combatCommand){}
    /* PRODUCTION METHODS */
}
public static class CombatAdvanceOrdersChecks
{
    public static int Run()
    {
        int checks=0;Action<bool,string> check=(ok,why)=>{checks++;if(!ok)throw new Exception(why);};
        BotOwner Bot(WildSpawnType role){var bot=new BotOwner();bot.Profile.Info.Settings.Role=role;return bot;}
        foreach(var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye})
        {
            foreach(float roll in new[]{0f,0.5f,1f})
            {
                Random.value=roll;var bot=Bot(role);var orders=new AdvanceOrders();orders.Followers.Add(bot);
                bot.Memory.GoalEnemy.Person.AIData.PowerOfEquipment=5000;
                orders.There(new Player());
                check(bot.Follower.Points==1 && bot.Follower.Pushes==0,"All Goons accept valid There as point movement: "+role);
                check(bot.Follower.Target.x==10 && bot.Follower.AggressionChanges==0,"There retains pointed destination without a push/aggression override");
            }
            var unavailable=Bot(role);unavailable.Locked=true;var locked=new AdvanceOrders();locked.Followers.Add(unavailable);
            locked.There(new Player());check(unavailable.Follower.Points==0,"Always willing does not bypass loot reservation");
            var invalid=Bot(role);var badPoint=new AdvanceOrders{PointValid=false};badPoint.Followers.Add(invalid);
            badPoint.There(new Player());check(invalid.Follower.Points==0,"Always willing does not invent a navigable point");
            var distant=Bot(role);var far=new AdvanceOrders{WorldPoint=new Vector3(31,0,0)};far.Followers.Add(distant);
            far.There(new Player());check(distant.Follower.Points==0,"Boss There retains 30m target limit");
            foreach(string unavailableState in new[]{"dead","inactive","outOfRange","cannotSeeGesture"})
            {
                var unavailableBot=Bot(role);
                if(unavailableState=="dead")unavailableBot.IsDead=true;
                if(unavailableState=="inactive")unavailableBot.BotState=EBotState.NonActive;
                if(unavailableState=="outOfRange")unavailableBot.Position=new Vector3(21,0,0);
                if(unavailableState=="cannotSeeGesture")unavailableBot.React=false;
                var gated=new AdvanceOrders();gated.Followers.Add(unavailableBot);gated.There(new Player());
                check(unavailableBot.Follower.Points==0,"High willingness preserves actor/gesture admission: "+unavailableState);
            }
        }
        foreach(bool accepted in new[]{false,true})
        {
            Random.value=accepted?0f:1f;var recruit=Bot(WildSpawnType.pmcUSEC);var orders=new AdvanceOrders();orders.Followers.Add(recruit);
            recruit.Follower.Command=FollowerCommandType.PushEnemy;
            orders.There(new Player());check(recruit.Follower.Points==(accepted?1:0),"Ordinary recruit There uses push acceptance roll");
            check(accepted || orders.LookOverrides==0,"Refused point cannot overwrite combat look state");
            check(accepted || recruit.Follower.Command==FollowerCommandType.PushEnemy,"Refused point preserves the existing order");
        }
        foreach(var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe})
        {
            var bot=Bot(role);bot.AIData.PowerOfEquipment=200;bot.Memory.GoalEnemy.Person.AIData.PowerOfEquipment=100;
            Random.value=0.99f;var orders=new AdvanceOrders();orders.Followers.Add(bot);orders.Forward(new Player());
            check(bot.Follower.Pushes==1 && bot.Follower.AggressionChanges==1,"Accepted assault-boss Go Forward queues actual push");
            bot.Follower.CombatTactic=FollowerCombatTactic.Marksman;
            check(AdvanceOrders.Accept(bot,new Player(),false),"Assault-boss policy owns its role despite a stale tactic tag");
            foreach(int level in new[]{1,79})
            foreach(float independence in new[]{0f,1f})
            {bot.Profile.Info.Level=level;bot.Follower.PickupIndependence01=independence;check(AdvanceOrders.Accept(bot,new Player(),false),"Boss confidence ignores level and pickup personality");}
            bot.Memory.GoalEnemy.Person.HealthController.IsAlive=false;
            check(!AdvanceOrders.Accept(bot,new Player(),false),"Dead enemy cannot grant boss push acceptance");
            bot.Memory.GoalEnemy.Person.HealthController.IsAlive=true;bot.Memory.GoalEnemy.Eligible=false;
            check(!AdvanceOrders.Accept(bot,new Player(),false),"Unadmitted target cannot grant boss push acceptance");
        }
        foreach(var tactic in new[]{FollowerCombatTactic.Marksman,FollowerCombatTactic.SAINShooter})
        foreach(bool saved in new[]{false,true})
        {
            var sniper=Bot(WildSpawnType.pmcUSEC);sniper.Follower.CombatTactic=tactic;sniper.Follower.IsSquadMate=saved;
            var orders=new AdvanceOrders();orders.Followers.Add(sniper);orders.Forward(new Player());
            check(sniper.Follower.Pushes==0 && sniper.Follower.AggressionChanges==0 && sniper.BotTalk.Requests.Contains(EPhraseTrigger.Negative),"Ordinary snipers explicitly refuse Go Forward");
        }
        var bird=Bot(WildSpawnType.followerBirdEye);var birdOrders=new AdvanceOrders();birdOrders.Followers.Add(bird);birdOrders.Forward(new Player());
        check(bird.Follower.Pushes==0 && bird.BotTalk.Requests.Contains(EPhraseTrigger.Negative),"Birdeye refuses push even with a fallback Balanced tactic");
        var risky=Bot(WildSpawnType.bossKnight);risky.AIData.PowerOfEquipment=20;risky.Memory.GoalEnemy.Person.AIData.PowerOfEquipment=500;
        risky.Memory.GoalEnemy.Person.Profile.Info.Settings.Role=WildSpawnType.bossSanitar;
        risky.Follower.Command=FollowerCommandType.MoveToPoint;UnityEngine.Random.value=0.99f;
        var riskOrders=new AdvanceOrders();riskOrders.Followers.Add(risky);riskOrders.Forward(new Player());
        check(risky.Follower.Pushes==0 && risky.Follower.AggressionChanges==0 && risky.Follower.Command==FollowerCommandType.MoveToPoint,
            "Boss can refuse severe equipment/threat mismatch without mutating its current order or aggression");
        var knownBot=Bot(WildSpawnType.bossKnight);knownBot.AIData.PowerOfEquipment=100;Random.value=0.9f;
        check(AdvanceOrders.Accept(knownBot,new Player(),false),"Single equal-equipped enemy is within high boss confidence");
        for(int i=0;i<4;i++)knownBot.EnemiesController.EnemyInfos[i.ToString()]=new EnemyInfo{Person=new Player{ProfileId=i.ToString()}};
        check(!AdvanceOrders.Accept(knownBot,new Player(),false),"Several known nearby enemies lower willingness");
        foreach(var enemy in knownBot.EnemiesController.EnemyInfos.Values)enemy.Known=false;
        check(AdvanceOrders.Accept(knownBot,new Player(),false),"Unknown enemy positions cannot inflate known threat count");
        foreach(var enemy in knownBot.EnemiesController.EnemyInfos.Values){enemy.Known=true;enemy.Point=new Vector3(100,0,0);}
        check(AdvanceOrders.Accept(knownBot,new Player(),false),"Distant known enemies do not become a local cluster");
        GameplayModeAddons.Registered=false;Random.value=1f;
        check(!AdvanceOrders.Accept(Bot(WildSpawnType.bossKnight),new Player(),true),"Absent addon does not grant boss-specific point acceptance");
        GameplayModeAddons.Registered=true;
        foreach(var actor in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe})
        {
            var matchup=Bot(actor);var target=matchup.Memory.GoalEnemy;Random.value=0.9f;
            target.Person.Profile.Info.Settings.Role=WildSpawnType.pmcUSEC;
            check(AdvanceOrders.Accept(matchup,new Player(),false),"Boss identity raises equal-gear PMC push confidence");
            target.Person.Profile.Info.Settings.Role=WildSpawnType.bossSanitar;
            check(!AdvanceOrders.Accept(matchup,new Player(),false),"Enemy bosses receive no ordinary-target discount");
            target.Person.AIData.PowerOfEquipment=250;Random.value=0.73f;
            target.Person.Profile.Info.Settings.Role=WildSpawnType.pmcUSEC;
            check(!AdvanceOrders.Accept(matchup,new Player(),false),"Strong PMC equipment still reduces confidence");
            target.Person.Profile.Info.Settings.Role=WildSpawnType.assault;
            check(AdvanceOrders.Accept(matchup,new Player(),false),"Same gear on an ordinary Scav receives a larger boss-relative advantage");
            check(matchup.AIData.PowerOfEquipment==100 && target.Person.AIData.PowerOfEquipment==250,"Confidence multiplier cannot mutate real equipment power");
            target.Person.Profile.Info.Settings=null;target.Person.AIData.PowerOfEquipment=100;Random.value=0.9f;
            check(!AdvanceOrders.Accept(matchup,new Player(),false),"Missing enemy role metadata cannot be discounted as an ordinary Scav");
        }
        var legacyPolicy=new pitTeam.Modules.CompanionCommandPolicy(false,(own,enemy,threat)=>0.5f);
        GameplayModeAddons.KnightPresent=false;
        var soloPipe=Bot(WildSpawnType.followerBigPipe);soloPipe.AIData.PowerOfEquipment=1;soloPipe.Memory.GoalEnemy.Person.AIData.PowerOfEquipment=999;Random.value=1f;
        check(AdvanceOrders.Accept(soloPipe,new Player(),false),"Independent Big Pipe accepts even an unfavorable admitted push at the maximum random roll");
        soloPipe.Memory.GoalEnemy.Eligible=false;
        check(!AdvanceOrders.Accept(soloPipe,new Player(),false),"Independent Big Pipe still requires admitted target knowledge");
        soloPipe.Memory.GoalEnemy.Eligible=true;soloPipe.Memory.GoalEnemy.Person.HealthController.IsAlive=false;
        check(!AdvanceOrders.Accept(soloPipe,new Player(),false),"Independent Big Pipe cannot push a dead target");
        soloPipe.Memory.GoalEnemy.Person.HealthController.IsAlive=true;GameplayModeAddons.KnightPresent=true;
        check(!AdvanceOrders.Accept(soloPipe,new Player(),false),"Knight presence restores confidence-based refusal for Big Pipe");
        check(legacyPolicy.RelativeCombatPower("assault")==1,"original policy constructor remains neutral and compatible");
        foreach(float invalid in new[]{0f,float.NaN,float.PositiveInfinity})
        {
            var invalidPolicy=new pitTeam.Modules.CompanionCommandPolicy(false,(own,enemy,threat)=>0.5f,role=>invalid);
            check(invalidPolicy.RelativeCombatPower("assault")==1,"invalid optional role modifier remains neutral");
        }
        var coordinated=new AdvanceOrders();
        var knight=Bot(WildSpawnType.bossKnight);var pipe=Bot(WildSpawnType.followerBigPipe);
        coordinated.Followers.Add(knight);coordinated.Followers.Add(pipe);
        int callbacks=0;var sequences=new List<int>();
        GameplayModeAddons.Leadership=context=>{callbacks++;sequences.Add(context.Sequence);return false;};
        Time.time+=1;coordinated.Forward(new Player());
        check(callbacks==2 && sequences[0]==sequences[1],"One broadcast publishes the same leadership sequence to both Goons");
        check(knight.Follower.Pushes==0 && pipe.Follower.Pushes==0,"Leadership refusal prevents both push publication and aggression changes");
        Time.time+=1;coordinated.Forward(new Player());
        check(sequences[2]!=sequences[0] && sequences[2]==sequences[3],"A later accepted broadcast has a fresh shared sequence");
        callbacks=0;
        GameplayModeAddons.Leadership=context=>{callbacks++;return context.EvaluateNormal(knight,knight.Memory.GoalEnemy);};
        knight.AIData.PowerOfEquipment=200;Random.value=0;
        check(AdvanceOrders.Accept(pipe,new Player(),false) && callbacks==1,"Normal evaluator uses Knight's confidence without recursively consulting leadership");
        GameplayModeAddons.Leadership=context=>{callbacks++;return true;};callbacks=0;
        pipe.Memory.GoalEnemy.Eligible=false;
        check(!AdvanceOrders.Accept(pipe,new Player(),false) && callbacks==0,"Leadership cannot bypass target admission");
        pipe.Memory.GoalEnemy.Eligible=true;pipe.Memory.GoalEnemy.Person.HealthController.IsAlive=false;
        check(!AdvanceOrders.Accept(pipe,new Player(),false) && callbacks==0,"Leadership cannot admit a dead target");
        check(AdvanceOrders.Accept(pipe,new Player(),true) && callbacks==0,"There remains independent of push leadership");
        GameplayModeAddons.Leadership=null;
        return checks;
    }
}
/* PRODUCTION POLICIES */
