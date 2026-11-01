using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Patches;

public enum PatrolMode { simple, follower, groupMoving, bossRoundProtect, bossCoverScouts, bossRoundProtectWithStay }
public interface IGetProfileData { bool ShallChooseByData(); }
public interface IBossToFollow { bool IsAI { get; } Player Player(); }
public class PatrolPointChooserBasic
{
    protected BotOwner owner;
    public PatrolPointChooserBasic(BotOwner bot) { owner=bot; }
    public virtual void ChooseStartWay() { }
}
public class PatrolPointChooserGroup : PatrolPointChooserBasic
{
    public PatrolPointChooserGroup(BotOwner bot) : base(bot) { }
    public override void ChooseStartWay()
    {
        // Exact failing expression in native PatrolPointChooserGroup.TryToFindWay.
        var way=owner.BotFollower.BossToFollow.Player().AIData.BotOwner.PatrollingData.Way;
    }
}
public class PatrolPointChooserFollower : PatrolPointChooserBasic { public PatrolPointChooserFollower(BotOwner b):base(b){} }
public class PatrolPointChooserBoss : PatrolPointChooserBasic { public PatrolPointChooserBoss(BotOwner b):base(b){} }
public class PatrolPointChooserByData : PatrolPointChooserBasic { public PatrolPointChooserByData(BotOwner b,IGetProfileData d):base(b){} }
public class PatrollingData
{
    public object Way=new object(); public PatrolMode Mode; public PatrolPointChooserBasic Chooser;
    public void SetMode(PatrolMode mode,PatrolPointChooserBasic chooser) { Mode=mode;Chooser=chooser;chooser.ChooseStartWay(); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    /*NATIVE_CHOOSER*/
}
public class PatrolDataFollower { public int Index; public bool Active; public void Activate()=>Active=true; public void SetIndex(int i)=>Index=i; }
public class BotFollower
{
    private readonly BotOwner _owner;
    public BotFollower(BotOwner owner) { _owner=owner; }
    public int Index; public IBossToFollow BossToFollow; public PatrolDataFollower PatrolDataFollower=new PatrolDataFollower();
    public int BossFound; private void BossFindAction()=>BossFound++;
    private void PeacefulActions(float period) { } private void OnStartLookToBody(float period) { }
    /*NATIVE_FOLLOW*/
}
public class StandartBotBrain
{
    public BotOwner _owner; public WildSpawnType ActivatedRole;
    [MethodImpl(MethodImplOptions.NoInlining)] public void Activate()=>ActivatedRole=_owner.Profile.Info.Settings.Role;
}
public class BotsGroup { public enum BotCurrentTactic { Protect } }
namespace EFT
{
    public enum WildSpawnType { assault, bossKnight, followerBigPipe, followerBirdEye, pmcUSEC, pmcBEAR, exUsec }
    public enum EPlayerSide { Savage, Usec, Bear }
    public class RoleSettings { public WildSpawnType Role; }
    public class Info { public RoleSettings Settings=new RoleSettings(); public string Nickname="fixture"; }
    public class Profile { public Info Info=new Info();public EPlayerSide Side;public Inventory Inventory=new Inventory(); }
    public class Inventory { public object Equipment=new object(); }
    public class AIData { public BotOwner BotOwner; }
    public class Player { public bool IsYourPlayer; public AIData AIData=new AIData(); }
    public class Tactic { public void SetTactic(BotsGroup.BotCurrentTactic tactic,bool flag,float x){} }
    public class Actions { public Action<float> OnStartPeacefulMove,OnStartLookToBody; }
    public class Boss { public object BossLogic; public void SetBoss(int n){} }
    public class BotOwner
    {
        public Profile Profile=new Profile(); public BotFollower BotFollower;
        public IGetProfileData SpawnProfileData; public PatrollingData PatrollingData=new PatrollingData();
        public Tactic Tactic=new Tactic(); public Actions PeacefulActions=new Actions(),DeadBodyWork=new Actions();
        public Boss Boss=new Boss(); public bool Managed;
        public BotOwner(){BotFollower=new BotFollower(this);}
        public bool IsPMC()=>Profile.Side!=EPlayerSide.Savage;
        public bool IsSimulatedPlayerScav()=>false;
        public bool HasMainProfileNickname()=>false;
    }
}
namespace pitTeam.Components
{
    public class pitAIBossPlayer : IBossToFollow
    { public Player realPlayer=new Player{IsYourPlayer=true}; public bool IsAI=>false; public Player Player()=>realPlayer; }
}
namespace pitTeam.Modules
{
    public static class BossPlayers { public static bool IsFollower(BotOwner b)=>b.Managed; }
}
namespace pitTeam.Utils
{
    public static class Props { public static List<WildSpawnType> BossFollowersType=new List<WildSpawnType>{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye}; }
}
namespace SPT.Reflection.Patching
{
    [AttributeUsage(AttributeTargets.Method)]public class PatchPrefixAttribute:Attribute{}
    public abstract class ModulePatch
    {
        protected abstract MethodBase GetTargetMethod();
        public void Enable()
        {
            var prefix=GetType().GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static);
            new Harmony("fixture."+GetType().Name).Patch(GetTargetMethod(),new HarmonyMethod(prefix));
        }
    }
}
namespace SPT.Custom.Patches
{
    public class Weights
    {
        public static int Lookups;
        public WildSpawnType GetPmcWildSpawnType(BotOwner owner,WildSpawnType role,string map)
        {Lookups++;if(role!=WildSpawnType.pmcUSEC && role!=WildSpawnType.pmcBEAR)throw new Exception("Missing PMC weight for "+role);return WildSpawnType.exUsec;}
        public WildSpawnType GetRandomisedPlayerScavType(BotOwner b,string s)=>WildSpawnType.assault;
        public WildSpawnType GetAssaultScavWildSpawnType(BotOwner b,string s)=>WildSpawnType.assault;
    }
    public class Fir {public void ConfigurePMCFindInRaidStatus(BotOwner b){} }
    public static class Logger {public static int Errors;public static void LogError(string s)=>Errors++;}
    public static class CustomAiPatch
    {
        private static string CurrentMapId=>"bigmap";
        private static readonly Weights _aIBrainSpawnWeightAdjustment=new Weights();
        private static readonly Fir _pmcFoundInRaidEquipment=new Fir();
        private static readonly List<string> _bossTypes=new List<string>();
        [MethodImpl(MethodImplOptions.NoInlining)]
        /*SPT_PREFIX*/
        /*SPT_POSTFIX*/
    }
}
internal static class Program
{
    private static int checks;
    private static void Check(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
    private sealed class NativeBoss:IBossToFollow
    { public Player Leader=new Player{AIData=new AIData{BotOwner=new BotOwner()}};public bool IsAI=>true;public Player Player()=>Leader; }
    private sealed class AuthoredPatrol:IGetProfileData {public bool ShallChooseByData()=>true;}
    private static BotOwner Bot(WildSpawnType role,bool managed=true,EPlayerSide side=EPlayerSide.Usec)
    { var bot=new BotOwner{Managed=managed};bot.Profile.Info.Settings.Role=role;bot.Profile.Side=side;return bot; }
    private static void Main()
    {
        var spt=typeof(SPT.Custom.Patches.CustomAiPatch);
        new Harmony("fixture.native.spt").Patch(AccessTools.Method(typeof(StandartBotBrain),"Activate"),
            new HarmonyMethod(AccessTools.Method(spt,"PatchPrefix")),new HarmonyMethod(AccessTools.Method(spt,"PatchPostFix")));
        foreach(var role in new[]{WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye})
        {
            var bot=Bot(role);bool failed=false;
            try{bot.BotFollower.SetToFollow(new pitAIBossPlayer(),0);}catch(NullReferenceException){failed=true;}
            Check(failed,"unpatched native human follow reproduces missing AI boss route: "+role);
        }
        var before=Bot(WildSpawnType.followerBigPipe);var originalBrain=new StandartBotBrain{_owner=before};originalBrain.Activate();
        Check(SPT.Custom.Patches.Weights.Lookups==1 && SPT.Custom.Patches.Logger.Errors>0,"unpatched SPT reproduces PMC-side Goon weight failure");
        new FollowerHumanPatrolChooserPatch().Enable();new FollowerNativeBrainActivationPatch().Enable();
        foreach(var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye,WildSpawnType.pmcUSEC})
        {
            var bot=Bot(role);var leader=new pitAIBossPlayer();bot.BotFollower.SetToFollow(leader,2);
            Check(ReferenceEquals(bot.BotFollower.BossToFollow,leader) && bot.BotFollower.Index==2,"same native human follower link/index: "+role);
            Check(bot.PatrollingData.Chooser.GetType()==typeof(PatrolPointChooserBasic),"human followers share Knight's basic chooser: "+role);
            Check(bot.PatrollingData.Mode==PatrolMode.follower && bot.BotFollower.PatrolDataFollower.Active && bot.BotFollower.BossFound==1,"native SetToFollow completes mode, activation and boss notification: "+role);
            Check(bot.Profile.Info.Settings.Role==role,"patrol fix never changes native role: "+role);
        }
        foreach(var role in new[]{WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye})
        {
            var bot=Bot(role);bot.BotFollower.SetToFollow(new NativeBoss(),0);
            Check(bot.PatrollingData.Chooser is PatrolPointChooserGroup,"native AI-led Goons retain group chooser: "+role);
        }
        var noBoss=Bot(WildSpawnType.followerBigPipe);
        Check(PatrollingData.GetPointChooser(noBoss,PatrolMode.groupMoving) is PatrolPointChooserGroup,"unattached actors retain requested group chooser");
        var otherHuman=Bot(WildSpawnType.followerBigPipe);var remote=new pitAIBossPlayer();remote.realPlayer.IsYourPlayer=false;otherHuman.BotFollower.BossToFollow=remote;
        Check(PatrollingData.GetPointChooser(otherHuman,PatrolMode.groupMoving) is PatrolPointChooserGroup,"unrelated human leaders are untouched");
        noBoss.BotFollower.BossToFollow=new pitAIBossPlayer();
        Check(PatrollingData.GetPointChooser(noBoss,PatrolMode.follower) is PatrolPointChooserFollower,"other requested patrol modes remain native");
        Check(PatrollingData.GetPointChooser(noBoss,PatrolMode.groupMoving,new AuthoredPatrol()) is PatrolPointChooserByData,"authored patrol-data precedence remains native");
        Check(PatrollingData.GetPointChooser(null,PatrolMode.groupMoving) is PatrolPointChooserGroup,"absent bot does not invent human ownership");
        var missingPlayer=Bot(WildSpawnType.followerBigPipe);missingPlayer.BotFollower.BossToFollow=new pitAIBossPlayer{realPlayer=null};
        Check(PatrollingData.GetPointChooser(missingPlayer,PatrolMode.groupMoving) is PatrolPointChooserGroup,"missing human player cannot activate compatibility override");
        foreach(var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye})
        foreach(var side in new[]{EPlayerSide.Usec,EPlayerSide.Bear,EPlayerSide.Savage})
        {
            var bot=Bot(role,true,side);var brain=new StandartBotBrain{_owner=bot};int lookups=SPT.Custom.Patches.Weights.Lookups;
            brain.Activate();
            Check(brain.ActivatedRole==role && bot.Profile.Info.Settings.Role==role,"native Goon role survives actual Harmony prefix/postfix state handoff: "+role+"/"+side);
            Check(SPT.Custom.Patches.Weights.Lookups==lookups,"managed Goon never consults PMC-only weights: "+role+"/"+side);
        }
        foreach(bool managed in new[]{false,true})
        {
            var bot=Bot(WildSpawnType.pmcUSEC,managed);var brain=new StandartBotBrain{_owner=bot};int lookups=SPT.Custom.Patches.Weights.Lookups;brain.Activate();
            Check(SPT.Custom.Patches.Weights.Lookups==lookups+1 && brain.ActivatedRole==WildSpawnType.exUsec && bot.Profile.Info.Settings.Role==WildSpawnType.pmcUSEC,"ordinary PMC randomization and paired state remain native: "+managed);
        }
        var hostile=Bot(WildSpawnType.followerBigPipe,false);int previous=SPT.Custom.Patches.Weights.Lookups;new StandartBotBrain{_owner=hostile}.Activate();
        Check(SPT.Custom.Patches.Weights.Lookups==previous+1,"unmarked actors retain SPT behavior even with a PMC side");
        Console.WriteLine("PASS: "+checks+" native human-follow / managed-Goon brain activation checks, including actual Harmony prefix/postfix execution. Controlled patrol inputs; not a Unity raid test.");
    }
}
