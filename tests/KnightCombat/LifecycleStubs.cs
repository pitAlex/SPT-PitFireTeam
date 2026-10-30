using System.Text;
using EFT;
using pitTeam.Components;
using DrakiaXYZ.BigBrain.Brains;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch(Type type,string method) : Attribute
    { public Type TargetType=type; public string Method=method; }
}
namespace pitTeam.Patches
{
    public static class FollowerWeaponSwitchPolicyRuntime
    { public static BotOwner GetSelectorBotOwner(BotWeaponSelector selector)=>selector?.Owner; }
}

namespace DrakiaXYZ.BigBrain.Brains
{
    public abstract class CustomLayer(BotOwner owner,int priority)
    {
        public class Action { }
        protected BotOwner BotOwner=owner;
        protected int Priority=priority;
        public abstract string GetName(); public abstract bool IsActive();
        public abstract void Start(); public abstract void Stop();
        public abstract bool IsCurrentActionEnding(); public abstract Action GetNextAction();
        public abstract void BuildDebugText(StringBuilder builder);
    }
}
namespace pitTeam.Modules
{
    public enum GameplayMode { GoingRogue, Allegiance }
    public static class GameplayModeAddons { public static GameplayMode Current=GameplayMode.GoingRogue; }
    public class BossPlayers
    {
        public static BossPlayers Instance=new();
        public Dictionary<BotOwner,BotFollowerPlayer> Followers=new();
        public static IEnumerable<BotFollowerPlayer> GetFollowers()=>Instance.Followers.Values;
        public BotFollowerPlayer GetFollower(BotOwner owner)=>Followers.GetValueOrDefault(owner);
    }
    public static class FollowerRecovery { public static int Stops; public static void StopShooting(BotOwner owner)=>Stops++; }
    public static class FollowerGrenadeRuntimeGate { public static int Stops; public static void EnforceDisabled(BotOwner owner)=>Stops++; }
}
namespace pitTeam.Utils
{
    public static class Logger
    {
        public static List<string> Errors=new();
        public static void LogError(string s)=>Errors.Add(s); public static void LogInfo(string s) { }
    }
}
namespace pitTeam.GoingRogue
{
    public static class GoingRoguePlugin { public static bool CombatEnabled=true; }
}
namespace pitTeam.BigBrain
{
    public class FollowerCombatLayer
    {
        public static FollowerCombatLayer Last;
        public int Starts,Stops; public bool Active=true;
        public FollowerCombatLayer(BotOwner owner,int priority,Func<BotOwner,FollowerCombatLogicBase> logic,
            Func<BotOwner,AICoreActionResult<BotLogicDecision,CoreActionResultParams>,CustomLayer.Action> actions,string name)
        { Last=this; }
        public static CustomLayer.Action CreateBigBrainAction(BotOwner owner,AICoreActionResult<BotLogicDecision,CoreActionResultParams> decision)=>new();
        public void Start()=>Starts++; public void Stop()=>Stops++;
        public bool IsActive()=>Active; public bool IsCurrentActionEnding()=>false;
        public CustomLayer.Action GetNextAction()=>new(); public void BuildDebugText(StringBuilder builder) { }
    }
}
