using EFT;
using pitTeam;
using pitTeam.Components;
using pitTeam.GoingRogue.Combat;
using pitTeam.Modules;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    private static (pitAIBossPlayer Boss, BotFollowerPlayer Commander, BotFollowerPlayer Member, BotFollowerPlayer PlayerSide) Setup(
        WildSpawnType commanderRole = WildSpawnType.bossKnight, WildSpawnType memberRole = WildSpawnType.exUsec)
    {
        GoonTeamSplit.Reset(); BossPlayers.Followers.Clear(); Time.time = 100;
        GameplayModeAddons.Current = GameplayMode.Allegiance;
        CompanionTeamCommands.Register(GoonTeamSplit.Available, GoonTeamSplit.SetSplit, GoonTeamSplit.Refresh);
        var boss = new pitAIBossPlayer();
        BotFollowerPlayer Add(WildSpawnType role)
        {
            var bot = new BotOwner(); bot.Profile.Info.Settings.Role = role;
            var follower = new BotFollowerPlayer(bot, boss); bot.Data = follower;
            BossPlayers.Followers.Add(follower); return follower;
        }
        return (boss, Add(commanderRole), Add(memberRole), Add(WildSpawnType.pmcUSEC));
    }
    public static void Main()
    {
        foreach (var commander in new[] { WildSpawnType.bossKnight, WildSpawnType.followerBigPipe })
        foreach (var member in new[] { WildSpawnType.followerBirdEye, WildSpawnType.exUsec })
        {
            var s = Setup(commander, member);
            Check(CompanionTeamCommands.Available(s.Boss), "both living sides eligible");
            CompanionTeamCommands.SetSplit(s.Boss, true);
            Check(CompanionTeamCommands.Available(s.Boss), "both phrases remain available after Split");
            Check(!s.Commander.TeamSplitCombatActive && !s.Member.TeamSplitCombatActive, "peaceful Split only arms");
            foreach (EPhraseTrigger phrase in Enum.GetValues<EPhraseTrigger>())
                Check(CompanionTeamCommands.AllowsPhrase(s.Commander.GetBot(), phrase), "peaceful commands unchanged");
            s.Commander.SetCombatDeterminationOverride(20);
            s.Commander.SetTemporaryCombatAggressionOverride(0, "oldHold");
            s.Boss.HasCombatCommandContext = true; GoonTeamSplit.Refresh();
            Check(s.Commander.TeamSplitCombatActive && s.Member.TeamSplitCombatActive && !s.PlayerSide.TeamSplitCombatActive, "only unit detached");
            Check(s.Commander.EffectiveSelfDetermination == 100 && s.Member.EffectiveSelfDetermination == 100, "whole unit independent");
            Check(s.Commander.EffectiveCombatAggression == 70, "commander On Your Own baseline");
            Check(s.Member.EffectiveCombatAggression == (member == WildSpawnType.exUsec ? 68 : 70), "Rogue role aggression retained");
            Check(s.Commander.Clears == 1 && s.Member.Clears == 1, "obsolete orders released once on combat entry");
            Check(ReferenceEquals(GoonTeamSplit.CombatCommander(s.Member.GetBot()), s.Commander.GetBot()), "split combat commander resolves Pipe and Knight");
            Check(GoonTeamSplit.CombatCommander(s.Commander.GetBot()) == null, "commander never follows himself as subordinate");
            int clears = s.Commander.Clears;
            for (int i = 0; i < 10; i++) { CompanionTeamCommands.SetSplit(s.Boss, true); GoonTeamSplit.Refresh(); }
            Check(s.Commander.Clears == clears, "repeat Split never resets active tasks");
            s.Boss.IssueRegroup();
            Check(s.Commander.Regroups == 0 && s.Member.Regroups == 0 && s.PlayerSide.Regroups == 1,
                "production Regroup changes only player-side member");
            s.Boss.IssueHold();
            Check(s.Commander.EffectiveCombatAggression == 70 && s.Member.EffectiveCombatAggression != 0 &&
                s.PlayerSide.EffectiveCombatAggression == 0, "production Hold cannot mutate detached percentages");
            s.Commander.GetBot().Position = new Vector3(41, 0, 0);
            s.Boss.IssueHelp();
            Check(!s.Commander.TryGetTeamSplitHelpEnemy(out _) && s.Member.TryGetTeamSplitHelpEnemy(out _),
                "production Need Help selects each recipient separately");
            Check(s.Commander.Cancels == 0 && s.Member.Cancels == 1 && s.PlayerSide.Cancels == 1,
                "excluded commander has no push-cancel side effect");
            s.Commander.GetBot().Position = default;
            // Attention is global even when both sides retain the same enemy group.
            var sharedGroup = new EnemyGroup();
            var contact = new Player();
            sharedGroup.Enemies[contact] = new object();
            foreach(var f in new[]{s.Commander,s.Member,s.PlayerSide})
            {
                f.GetBot().BotsGroup=sharedGroup;
                f.GetBot().Memory.GoalEnemy=new EnemyContact();
                f.GetBot().Memory.LastEnemy=contact;
                f.GetBot().EnemiesController.EnemyInfos[contact]=new object();
            }
            using(CompanionTeamCommands.Enter(s.Boss,s.Boss.realPlayer,EPhraseTrigger.Look))
            {
                Check(CompanionTeamCommands.AllowsPhrase(s.Commander.GetBot(),EPhraseTrigger.Look),"native Attention admits detached commander");
                Check(CompanionTeamCommands.AllowsPhrase(s.Member.GetBot(),EPhraseTrigger.Look),"native Attention admits detached subordinate");
                s.Boss.ClearAttentionContacts();
            }
            foreach(var f in new[]{s.Commander,s.Member,s.PlayerSide})
            {
                Check(f.GetBot().Memory.GoalEnemy==null && f.GetBot().Memory.LastEnemy==null,"Attention clears each side's selected contact");
                Check(f.GetBot().EnemiesController.EnemyInfos.Count==0,"Attention clears each side's personal memory");
            }
            Check(sharedGroup.Enemies.Count==0 && sharedGroup.Removals==1,"Attention clears shared enemy cache exactly once");
            GoonTeamSplit.Refresh();
            Check(s.Commander.TeamSplitCombatActive && s.Member.TeamSplitCombatActive,"Attention reset preserves the split latch");
            // Need Sniper is a Birdeye-specific exception, without the Help range limit.
            s.Member.GetBot().Position=new Vector3(90,0,0);
            s.Boss.IssueSniper();
            Check(s.Commander.SniperOrders==0 && s.PlayerSide.SniperOrders==0,"Need Sniper does not order non-marksman actors");
            Check(s.Member.SniperOrders==(member==WildSpawnType.followerBirdEye?1:0),"Birdeye receives the production sniper handler while split");
            Check(CompanionTeamCommands.AllowsPhrase(s.Member.GetBot(),EPhraseTrigger.NeedSniper)==(member==WildSpawnType.followerBirdEye),"native sniper admission matches the subordinate role");
            s.Member.GetBot().UnavailableForSniper=true;s.Boss.IssueSniper();
            Check(s.Member.SniperOrders==(member==WildSpawnType.followerBirdEye?1:0),"busy Birdeye retains normal sniper refusal");
            s.Member.GetBot().UnavailableForSniper=false;s.Member.GetBot().Position=default;
            foreach (var phrase in new[] { EPhraseTrigger.Regroup, EPhraseTrigger.GoForward, EPhraseTrigger.HoldPosition, EPhraseTrigger.Suppress, EPhraseTrigger.FollowMe })
            {
                using (CompanionTeamCommands.Enter(s.Boss, s.Boss.realPlayer, phrase))
                {
                    Check(!CompanionTeamCommands.AllowsCurrentInput(s.Commander.GetBot()), "voice excludes commander");
                    Check(!CompanionTeamCommands.AllowsCurrentInput(s.Member.GetBot()), "voice excludes subordinate");
                    Check(CompanionTeamCommands.AllowsCurrentInput(s.PlayerSide.GetBot()), "player retains own member");
                    using (CompanionTeamCommands.Enter(s.Boss, s.Boss.realPlayer, default, gesture: true))
                        Check(CompanionTeamCommands.AllowsCurrentInput(s.Commander.GetBot()), "combat gestures accepted");
                    Check(!CompanionTeamCommands.AllowsCurrentInput(s.Commander.GetBot()), "nested gesture restores voice scope");
                }
                Check(CompanionTeamCommands.AllowsCurrentInput(s.Commander.GetBot()), "scope always released");
                Check(!CompanionTeamCommands.AllowsPhrase(s.Commander.GetBot(), phrase), "native receiver cannot bypass");
            }
            foreach (float distance in new[] { 39.99f, 40f, 40.01f })
            {
                s.Commander.GetBot().Position = new Vector3(distance, 0, 0);
                Check(CompanionTeamCommands.AllowsPhrase(s.Commander.GetBot(), EPhraseTrigger.NeedHelp) == (distance <= 40), "individual 40m help limit");
            }
            s.Member.GetBot().Position = new Vector3(0, 41, 0);
            Check(!CompanionTeamCommands.AllowsPhrase(s.Member.GetBot(), EPhraseTrigger.NeedHelp), "help includes vertical distance");
            s.Member.GetBot().Position = default;
            var enemy = new BotOwner(); s.Member.RequestTeamSplitHelp(enemy);
            Check(s.Member.TryGetTeamSplitHelpEnemy(out var accepted) && accepted == enemy, "explicit help retained for recipient");
            Time.time += 8;
            Check(!s.Member.TryGetTeamSplitHelpEnemy(out _), "help expires without ending split");
            Check(s.Member.TeamSplitCombatActive, "help expiry resumes detached behavior");
            s.Member.RequestTeamSplitHelp(enemy); enemy.IsDead = true;
            Check(!s.Member.TryGetTeamSplitHelpEnemy(out _), "dead help target released");
            // An accepted gesture owns its temporary setting; maintenance never stamps over it.
            s.Commander.SetTemporaryCombatAggressionOverride(0, "gestureHold"); GoonTeamSplit.Refresh();
            Check(s.Commander.EffectiveCombatAggression == 0, "gesture hold retains priority");
            s.Boss.HasCombatCommandContext = false; GoonTeamSplit.Refresh();
            Check(!s.Commander.TeamSplitCombatActive && !s.Member.TeamSplitCombatActive, "peaceful effects released");
            s.Boss.HasCombatCommandContext = true; GoonTeamSplit.Refresh();
            Check(s.Commander.TeamSplitCombatActive && s.Commander.EffectiveCombatAggression == 70, "next battle reactivates armed split");
            int commanderRegroups=s.Commander.Regroups, memberRegroups=s.Member.Regroups, playerRegroups=s.PlayerSide.Regroups;
            s.Boss.IssueCombine();
            Check(s.Commander.Regroups==commanderRegroups+1 && s.Member.Regroups==memberRegroups+1 &&
                s.PlayerSide.Regroups==playerRegroups+1, "Combine releases filtering before issuing one player Regroup to everyone");
            Check(s.Commander.CombatRegroupUsesBossAnchor && s.Member.CombatRegroupUsesBossAnchor,
                "Combine regroup explicitly uses the human anchor instead of the Goon commander");
            Check(!s.Commander.TeamSplitCombatActive && s.Commander.EffectiveSelfDetermination == 50, "Combine restores runtime preferences");
            Check(s.Commander.EffectiveCombatAggression == 55, "Combine removes split-only aggression");
            Check(CompanionTeamCommands.Available(s.Boss), "Combine preserves both phrases");
            int afterCombine = s.Commander.Clears; int repeatRegroups=s.Commander.Regroups; s.Boss.IssueCombine();
            Check(s.Commander.Regroups==repeatRegroups,"repeat Combine cannot restart a productive regroup");
            Check(s.Commander.Clears == afterCombine, "repeat Combine harmless");
        }
        foreach(bool combat in new[]{false,true})
        {
            var s=Setup();s.Boss.HasCombatCommandContext=combat;
            CompanionTeamCommands.SetSplit(s.Boss,true);
            s.Boss.IssueCombine();
            Check(s.Commander.Regroups==1 && s.Member.Regroups==1 && s.PlayerSide.Regroups==1,
                "Combine requests normal player regroup both when armed peacefully and in combat");
            Check(s.Commander.CombatRegroupUsesBossAnchor==combat,
                "peaceful Combine retains normal peaceful Regroup; combat Combine has explicit human anchor");
            s.Boss.HasCombatCommandContext=true;GoonTeamSplit.Refresh();
            Check(!s.Commander.TeamSplitCombatActive,"Combine regroup does not re-arm split");
        }
        foreach (var loss in new[] { "commander", "subordinate", "playerSide", "removed", "mode", "owner" })
        {
            var s = Setup(); s.Boss.HasCombatCommandContext = true; CompanionTeamCommands.SetSplit(s.Boss, true);
            switch (loss)
            {
                case "commander": s.Commander.GetBot().IsDead = true; break;
                case "subordinate": s.Member.GetBot().IsDead = true; break;
                case "playerSide": s.PlayerSide.GetBot().IsDead = true; break;
                case "removed": BossPlayers.Followers.Remove(s.Member); break;
                case "mode": GameplayModeAddons.Current = GameplayMode.GunsForHire; break;
                case "owner": s.Member.Leader = new pitAIBossPlayer(); break;
            }
            GoonTeamSplit.Refresh();
            Check(!s.Commander.TeamSplitCombatActive && !s.Member.TeamSplitCombatActive, "invalid composition releases all split effects: " + loss);
            s.Commander.GetBot().IsDead = s.Member.GetBot().IsDead = s.PlayerSide.GetBot().IsDead = false;
            GameplayModeAddons.Current = GameplayMode.Allegiance; GoonTeamSplit.Refresh();
            Check(!s.Commander.TeamSplitCombatActive, "invalidated split never silently rearms: " + loss);
        }
        var noCommander = Setup(WildSpawnType.followerBirdEye);
        Check(!CompanionTeamCommands.Available(noCommander.Boss), "Birdeye is not a commander");
        var onlyRogues = Setup(WildSpawnType.exUsec);
        Check(!CompanionTeamCommands.Available(onlyRogues.Boss), "Rogue cannot command split");
        var remote = Setup(); remote.Boss.realPlayer.IsYourPlayer = false;
        Check(!CompanionTeamCommands.Available(remote.Boss), "foreign human excluded");
        GoonTeamSplit.Reset(); CompanionTeamCommands.Unregister();
        Check(!CompanionTeamCommands.Available(remote.Boss), "absent addon leaves Core commands ordinary");
        Console.WriteLine($"Passed {checks} production split-state, recipient, composition, lifetime and help checks.");
    }
}

namespace UnityEngine
{
    public static class Time { public static float time; }
    public struct Vector3(float x, float y, float z)
    {
        public float x = x, y = y, z = z;
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x, a.y-b.y, a.z-b.z);
    }
}
namespace EFT
{
    public enum WildSpawnType { bossKnight, followerBigPipe, followerBirdEye, exUsec, pmcUSEC }
    public enum EBotState { Active, NonActive, Disposed }
    public interface IPlayer { string ProfileId { get; } Vector3 Position { get; } }
    public class Player : IPlayer
    {
        public string ProfileId { get; } = Guid.NewGuid().ToString(); public Vector3 Position { get; set; }
        public bool IsYourPlayer = true; public Health HealthController = new();
    }
    public class Health { public bool IsAlive = true; }
    public class Settings { public WildSpawnType Role; }
    public class Info { public Settings Settings = new(); }
    public class Profile { public Info Info = new(); }
    public class Talk { public void TrySay(EPhraseTrigger phrase,bool force) { } }
    public class Gestures { public void TryGestus(EInteraction gesture,bool force) { } }
    public class BotOwner
    {
        public Talk BotTalk = new(); public Gestures Gesture = new();
        public Vector3 Position; public bool IsDead; public EBotState BotState;
        public Profile Profile = new(); public Player GetPlayer = new(); public BotFollowerPlayer Data;
        public EnemyGroup BotsGroup=new(); public EnemyMemory Memory; public EnemyController EnemiesController=new();
        public bool UnavailableForSniper;
        public BotOwner(){Memory=new EnemyMemory(this);}
    }
    public class EnemyGroup { public int Id=1,Removals; public Dictionary<IPlayer,object> Enemies=new(); public void RemoveEnemy(IPlayer enemy){Enemies.Remove(enemy);Removals++;} }
    public class EnemyController { public Dictionary<IPlayer,object> EnemyInfos=new(); }
    public class EnemyContact { public EnemyGroupInfo GroupInfo=new(); }
    public class EnemyGroupInfo { public float EnemyLastSeenTimeSense=100; public bool IsHaveSeen=true; }
    public class EnemyMemory(BotOwner owner) { public EnemyContact GoalEnemy; public IPlayer LastEnemy; public void DeleteInfoAboutEnemy(IPlayer enemy)=>owner.EnemiesController.EnemyInfos.Remove(enemy); }
}
public enum EPhraseTrigger { None, Regroup, GoForward, HoldPosition, Suppress, FollowMe, NeedHelp, Negative, Look, NeedSniper, Roger }
public enum EInteraction { OkGesture, NoGesture }
namespace pitTeam
{
    public enum CustomPhrases { TeamStatus = 10001, TeamSplit = 10005, TeamCombine = 10006 }
    public enum GameplayMode { Allegiance, GunsForHire }
    public static class pitFireTeam { public static bool ShouldUseSainRegroupRoute(EFT.BotOwner owner,bool combat)=>false; }
}
namespace pitTeam.Modules
{
    public static class GameplayModeAddons { public static pitTeam.GameplayMode Current = pitTeam.GameplayMode.Allegiance; }
    public static class Logger { public static void LogInfo(string message) { } public static void LogError(string message) { } }
    public static class FollowerAwareness { public static void ClearTransientState(BotOwner bot) { } }
    public static class FollowerEnemyTracking { public static void Clear(BotOwner bot) { } }
    public static class FollowerContactEnemyRetention { public static void ClearAndAllowNextGoalClear(BotOwner bot) { } }
    public static class FollowerGoalEnemyTracker { public static IDisposable Begin(string a,string b)=>new Scope(); private sealed class Scope:IDisposable { public void Dispose(){} } }
    public static class FollowerCombatTactics { public static bool IsMarksman(string tactic)=>tactic=="Marksman"; }
    public class BossPlayers
    {
        public static BossPlayers Instance = new();
        public static List<BotFollowerPlayer> Followers = new();
        public static IEnumerable<BotFollowerPlayer> GetFollowers() => Followers;
        public BotFollowerPlayer GetFollower(BotOwner bot) => Followers.FirstOrDefault(f => f.GetBot() == bot);
    }
}
namespace pitTeam.Components
{
    public enum FollowerCommandType { None, PushEnemy }
    public class pitAIBossPlayer
    {
        public Player realPlayer = new(); public bool HasCombatCommandContext;
        public IEnumerable<BotOwner> Followers => BossPlayers.GetFollowers().Where(f => f.GetBoss() == this).Select(f => f.GetBot());
        /* COMMAND_RECIPIENTS */
        private Logic aBossLogic = new();
        private BotOwner helpTarget = new();
        private const float PhraseCommandDistance = 35;
        public void IssueCombine() { using var scope=CompanionTeamCommands.Enter(this,realPlayer,(EPhraseTrigger)CustomPhrases.TeamCombine); ApplyTeamSplitCommand(realPlayer,false); }
        public void IssueRegroup() { using var scope=CompanionTeamCommands.Enter(this,realPlayer,EPhraseTrigger.Regroup); ApplyRegroupCommand(realPlayer,false); }
        public void IssueHold() { using var scope=CompanionTeamCommands.Enter(this,realPlayer,EPhraseTrigger.HoldPosition); ApplyTemporaryCombatAggressionCommand(realPlayer,0,EPhraseTrigger.None); }
        public void IssueHelp() { using var scope=CompanionTeamCommands.Enter(this,realPlayer,EPhraseTrigger.NeedHelp); ApplyNeedHelpPhrase(realPlayer); }
        public void IssueSniper() { using var scope=CompanionTeamCommands.Enter(this,realPlayer,EPhraseTrigger.NeedSniper); ApplyNeedSniperPhrase(realPlayer); }
        public void ClearAttentionContacts(){var groups=new HashSet<string>();foreach(var bot in CommandFollowers)ClearEnemyStateForAttention(bot,groups);}
        private void ProcessContactCommand(IPlayer human) { }
        private bool IsMarksmanUnavailableForNeedSniper(BotOwner bot)=>bot.UnavailableForSniper;
        private bool TryGetClosestNeedHelpEnemy(out BotOwner target) { target=helpTarget; return true; }
        private void PrioritizeEnemy(BotOwner follower,BotOwner enemy) { }
        private bool IsCombatRegroupContext() => HasCombatCommandContext;
        private bool IsFollowerLockedByLootCommand(BotOwner bot) => false;
        private bool ShouldIgnoreCombatRegroup(BotOwner bot,Vector3 point,bool tight) => false;
        private bool ShouldIgnoreRegroup(BotOwner bot,Vector3 point,bool tight) => false;
        private void TryResetSainDecisionState(BotOwner bot) { }
        private BotOwner FindLookedAtFollower(Player human,float distance,float radius,float angle) => null;
        private bool HasHoldCombatContext(BotOwner bot,BotFollowerPlayer follower) => HasCombatCommandContext;
        private bool IsPickedUpFollower(BotFollowerPlayer follower) => false;
        private bool RollPickupFollowerHoldAcceptance(BotOwner bot,BotFollowerPlayer follower,IPlayer human,string reason) => true;
        /* COMMAND_HANDLERS */
    }
    public class Logic { public void MarkManualUnderAttack(BotOwner enemy) { } }
    public partial class BotFollowerPlayer(BotOwner bot, pitAIBossPlayer leader)
    {
        private BotOwner _bot = bot;
        public pitAIBossPlayer Leader = leader;
        private float? _combatDeterminationOverride, _companionCombatDetermination = null;
        private float? _companionCombatAggression = bot.Profile.Info.Settings.Role == WildSpawnType.exUsec ? 68f : null;
        private bool _temporaryCombatAggressionOverrideActive;
        private float _temporaryCombatAggressionOverride, _combatAggression = 55;
        public float SelfDetermination = 50;
        public int Clears;
        public int Regroups, Cancels, SniperOrders;
        public string CombatTactic=>_bot.Profile.Info.Settings.Role==WildSpawnType.followerBirdEye?"Marksman":"Rifleman";
        public void SetNeedSniper(float timeout)=>SniperOrders++;
        public void ClearTemporaryCombatAggressionOverride(string reason)=>_temporaryCombatAggressionOverrideActive=false;
        public bool CombatRegroupUsesBossAnchor;
        public BotOwner GetBot() => _bot;
        public pitAIBossPlayer GetBoss() => Leader;
        public void SetCombatDeterminationOverride(float value) => _combatDeterminationOverride = value;
        public void SetTemporaryCombatAggressionOverride(float value, string source) { _temporaryCombatAggressionOverrideActive = true; _temporaryCombatAggressionOverride = value; }
        public void ResetCombatPercentageOverrides(string source) { _combatDeterminationOverride = null; _temporaryCombatAggressionOverrideActive = false; }
        public void ClearCommand(string source) => Clears++;
        public void SetCombatRegroupBossAnchor(bool value) { CombatRegroupUsesBossAnchor=value; }
        public void SetCanPatrol(bool value) { }
        public void SetRegroup(float timeout,bool tight=false) => Regroups++;
        public void RequestOrderedPushCancel(string reason) => Cancels++;
        public bool TryPeekActiveCommand(out FollowerCommandType command,out Vector3 point,out float until) { command=default; point=default; until=0; return false; }
        private void UpdateTemporaryCombatAggressionClearDelay() { }
        // The verifier injects the exact production effective-value properties.
        /* EFFECTIVE_VALUES */
    }
}
public static class Mathf { public static bool Approximately(float a,float b) => Math.Abs(a-b)<.001f; }
