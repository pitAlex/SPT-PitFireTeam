// Controlled game stand-ins for the production recruitment patch and SAIN reflection bridge.
using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using UnityEngine;
using pitTeam;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Patches;

namespace UnityEngine
{
    public struct Vector3 {
        public float x, y, z; public static Vector3 zero => default; public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator -(Vector3 a,Vector3 b) => new Vector3{x=a.x-b.x,y=a.y-b.y,z=a.z-b.z};
        public static Vector3 operator +(Vector3 a,Vector3 b) => new Vector3{x=a.x+b.x,y=a.y+b.y,z=a.z+b.z};
        public static float Angle(Vector3 a,Vector3 b) => (float)(Math.Acos(Math.Max(-1,Math.Min(1,(a.x*b.x+a.y*b.y+a.z*b.z)/Math.Sqrt(a.sqrMagnitude*b.sqrMagnitude))))*180/Math.PI);
    }
    public struct Ray { public Vector3 origin, direction; }
    public static class Time { public static float time; }
    public static class Random { public static int Range(int min, int max) => min; public static float Fraction, ValueFraction; public static int Rolls; public static float value { get { Rolls++; return ValueFraction; } } public static float Range(float min, float max) => min + (max-min)*Fraction; }
    public class Transform { public Vector3 position; }
}
namespace EFT.Interactive { public enum EInteraction { NoGesture, GetOffGesture, OkGesture, FriendlyGesture } }
namespace EFT
{
    public class GameWorld { public List<Player> AllAlivePlayersList = new(); }
    public enum EPhraseTrigger { None, Negative, DontKnow, Roger, Toxic, MumblePhrase, OnMutter, OnFight, OnBeingHurt, OnEnemyGrenade, Cooperation, FollowMe, NeedHelp, OnRepeatedContact, HoldFire }
    public enum ETagStatus { Coop, Solo, Unaware }
    public enum EPlayerSide { Usec, Bear, Savage }
    public enum EBotState { Active, Inactive }
    public enum BotLogicDecision {holdPosition,heal,healStimulators}
    public class Medicine {public FirstAid FirstAid=new();public Surgery SurgicalKit=new();public Stimulators Stimulators=new();}
    public class FirstAid {public bool Have2Do,Using;}
    public class Surgery {public bool HaveWork,Using;}
    public class Stimulators {public bool Using;}
    public class Brain {public Agent Agent=new();}
    public class Agent {public DecisionResult Result=new();public DecisionResult LastResult()=>Result;}
    public class DecisionResult {public BotLogicDecision Action;}
    public interface IPlayer { string ProfileId { get; } EPlayerSide Side { get; } Profile Profile { get; } Vector3 Position { get; } }
    public class Profile { public string Nickname="candidate"; public Info Info = new(); public FenceInfo FenceInfo = new(); }
    public class Info { public int Level = 20; public EPlayerSide Side; }
    public class FenceInfo { public double Standing = 6; }
    public class Health { public bool IsAlive = true; }
    public class Speaker
    {
        public bool Speaking, Busy;
        public bool AcceptDirect=true; public readonly List<EPhraseTrigger> DirectPhrases=new();
        public object Play(EPhraseTrigger phrase, ETagStatus mask, bool force, object unused) {
            if(!AcceptDirect)return null;DirectPhrases.Add(phrase);Speaking=true;return new();
        }
    }
    public class Player : IPlayer
    {
        public bool IsAI = true;
        public AIData AIData = new();
        public EPhraseTrigger Spoken;
        public string ProfileId { get; set; } = "boss";
        public EPlayerSide Side { get; set; }
        public Profile Profile { get; } = new();
        public Health HealthController = new(); public Speaker Speaker = new(); public Vector3 Position {get;set;}
        public Player InteractablePlayer;
        public HandsController HandsController = new();
        public MovementContext MovementContext = new();
        public bool UsedSimplifiedSkeleton;
        public bool MarkSpeaking; public int VoiceCalls;
        public Ray InteractionRay;
        public Dictionary<BodyPartType, BodyPart> MainParts = new() {
            [BodyPartType.head] = new BodyPart { Position = new Vector3 { y = 2 } },
            [BodyPartType.body] = new BodyPart { Position = new Vector3 { y = 1 } }
        };
        public void Say(EPhraseTrigger phrase) { if (NativeSpeech.PlayerPrefix(this, phrase)) { Spoken = phrase==EPhraseTrigger.MumblePhrase ? EPhraseTrigger.OnMutter : phrase; VoiceCalls++; if(MarkSpeaking) Speaker.Speaking=true; } }
    }
    public class AIData { public BotOwner BotOwner; }
    public class EnemyInfo { public string ProfileId; public Player Person; }
    public class Memory { public EnemyInfo GoalEnemy; public bool IsUnderFire; public bool HaveEnemy => GoalEnemy != null; }
    public class HandsController { public bool Busy; public FirearmsAnimator FirearmsAnimator = new(); public bool IsInInteractionStrictCheck() => Busy; }
    public class FirearmsAnimator { public bool Idle=true; public bool IsIdling() => Idle; }
    public class MovementContext { public bool IsSprintEnabled, IsInMountedState, IsStationaryWeaponInHands; public PlayerAnimator PlayerAnimator=new(); }
    public class PlayerAnimator { public AnimatedInteractions AnimatedInteractions=new(); }
    public class AnimatedInteractions : EFT.AnimatedInteractionsSubsystem.AnimatedInteractions { }
    public class PatrolSettings { public bool CAN_GESTUS=true; }
    public class FileSettings { public PatrolSettings Patrol=new(); }
    public class BotSettings { public FileSettings FileSettings=new(); }
    public class WeaponManager { public Grenades Grenades=new(); public WeaponSelector Selector=new(); public WeaponInfo _currentWeaponInfo=new(); }
    public class Grenades { public bool ThrowindNow; }
    public class WeaponSelector { public bool IsChanging; }
    public class WeaponInfo { public Reload Reload=new(); }
    public class Reload { public bool Reloading; }
    public class ShootData { public bool Shooting; }
    public class GrenadeDangerPoint { public object Grenade=new(); public float EndTime=float.MaxValue; }
    public class BewareGrenade { public GrenadeDangerPoint GrenadeDangerPoint; }
    public class BotTalk
    {
        public BotOwner _owner;
        public int Queued;
        public bool QueueRequests;
        public bool ThrowOnSay;
        public Action AfterSay;
        public EPhraseTrigger Last;
        private bool silenced;
        private float silenceEnds;
        public bool IsSilenced { get { if (silenced && silenceEnds < Time.time) silenced=false; return silenced; } }
        public void TrySay(EPhraseTrigger phrase, bool withGroupDelay = true)
        {
            if (QueueRequests) Queued++;
            else Say(phrase, false);
        }
        public void Say(EPhraseTrigger type, bool sayImmediately, ETagStatus? additionalMask = null)
        {
            if (ThrowOnSay) throw new Exception("speaker failure");
            if (!NativeSpeech.BotPrefix(this, type)) return;
            Last = type;
            _owner.GetPlayer.Say(type);
            AfterSay?.Invoke();
        }
        public void SetSilence(float seconds) { silenced=true; silenceEnds=Time.time+seconds; }
        public void DropNextSayPeriod() { }
    }
    public class Gesture {
        public BotOwner Owner; public int Calls; public bool Accept=true; public EFT.Interactive.EInteraction Last;
        public void TryGestus(EFT.Interactive.EInteraction interaction, bool force) {
            Calls++; Last=interaction;
            if(Owner!=null && Accept) Owner.GetPlayer.MovementContext.PlayerAnimator.AnimatedInteractions.IsInteractionPlaying=true;
        }
    }
    public class Boss { public bool IsMe(Player player) => true; }
    public class BotFollower { public bool HaveBoss; public Boss BossToFollow = new(); }
    public class Group { public int MembersCount; public BotGroupRequestController RequestsController=new(); public bool Hostile,Allied,PolicyEnemy; public Dictionary<IPlayer,bool> Neutrals=new(); public bool IsEnemy(IPlayer player)=>Hostile; public bool IsAlly(IPlayer player)=>Allied; public bool IsPlayerEnemy(IPlayer player)=>PolicyEnemy; }
    public class BotReceiver { public BotOwner _owner; public void OnPhraseSay(GlobalEventDispatcher.PhraseDelegateInfo info) {} }
    public class BotOwner
    {
        public static Vector3 STAY_HEIGHT=new Vector3 {y=1.5f};
        public BotOwner() { BotTalk._owner = this; GetPlayer.AIData.BotOwner = this; Gesture.Owner=this;Mover.Owner=this; }
        public string ProfileId = "candidate"; public EPlayerSide Side; public Profile Profile = new();
        public bool IsDead, IsFollower; public EBotState BotState = EBotState.Active;
        public Player GetPlayer = new(); public Memory Memory = new(); public BotTalk BotTalk = new();
        public Gesture Gesture = new(); public BotFollower BotFollower = new(); public Group BotsGroup;
        public Vector3 Position => GetPlayer.Position;
        public Transform WeaponRoot = new();
        public LookSensor LookSensor = new();
        public BotSettings Settings=new(); public WeaponManager WeaponManager=new(); public ShootData ShootData=new();
        public BewareGrenade BewareGrenade=new();public Medicine Medecine=new();public Brain Brain=new();
        public bool UsingMedical {get=>Medecine.FirstAid.Using;set=>Medecine.FirstAid.Using=value;}
        public Mover Mover=new(); public Steering Steering=new();
        public bool IsEnemyLookingAtMe(EnemyInfo enemy) => false;
    }
    public class Mover { public BotOwner Owner;public bool Sprinting;public int Stops; public void Stop() {Stops++;}public void Sprint(bool value,bool debug){Sprinting=value;Owner.GetPlayer.MovementContext.IsSprintEnabled=value;} }
    public class Steering { public Vector3 LastLook; public int Looks;public bool Throw; public void LookToPoint(Vector3 point) {if(Throw)throw new Exception("steering failure");LastLook=point;Looks++;} }
    public class BotGroupRequestController { public int Calls;public Action AfterAsk; public bool TryAskFollowMeRequest(IPlayer player,BotOwner bot) {Calls++;AfterAsk?.Invoke();return true;} }
}
public class LookSensor {
    public float VisibleDist = 100; public bool InSector = true, Clear = true, Throw;
    public bool IsPointInVisibleSector(Vector3 point) => InSector;
    public bool CheckLookSimple(Player from, Player to) => Throw ? throw new Exception("sight failure") : Clear;
}
public class GlobalEventDispatcher { public class PhraseDelegateInfo { public EPhraseTrigger phrase;public IPlayer PlayerRequester; } }
namespace SPT.Reflection.Patching
{
    public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); }
    public class PatchPrefixAttribute : Attribute { }
}
namespace pitTeam.BigBrain { }
namespace pitTeam.Components
{
    public class pitAIBossPlayer {
        public Group bossGroup; public Player Value = new(); public Player Player() => Value;
        private Player realPlayer => Value;
        __GESTURE_VISIBILITY__
        __COOPERATION_RECEIVER__
    }
    public class BotFollowerPlayer {
        public bool IsSquadMate; public BotOwner Bot; public BotOwner GetBot() => Bot;
        private static Type _sainEnableType;
        private static MethodInfo _getSainByBotOwnerMethod, _getSainByProfileMethod;
        __NATIVE_PERSONALITY_CAPTURE__
    }
}
namespace pitTeam
{
    public class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    public static class pitFireTeam
    {
        public static bool IsSAINInstalled = true;
        public static Setting<bool> pickupEnabled = new(true), tieredPickup = new(true);
        public static Setting<int> maximumPickup = new(10);
        public static Logger Log = new();
        public static bool ShouldDisableSainForFollower(BotOwner bot) => false;
    }
}
namespace pitTeam.Modules
{
    internal static class PmcKarmaRuntime { internal static void NoteRecruitment(BotOwner bot, IPlayer leader, bool squadMate) { } }
    public static class GameplayModeRuntime {
        public static bool IsAllegiance;
        public static T GetEffectiveValue<T>(Setting<T> entry) {
            if (IsAllegiance && ReferenceEquals(entry,pitFireTeam.maximumPickup)) return (T)(object)2;
            if (IsAllegiance && (ReferenceEquals(entry,pitFireTeam.pickupEnabled) || ReferenceEquals(entry,pitFireTeam.tieredPickup))) return (T)(object)true;
            return entry.Value;
        }
    }
    public static class AllegiancePmcFriendship {
        public static bool Allowed=true, IsEnabled=true, Acceptance=true, ResolveHandled, ResolveAccepted=true;
        public static int AcceptanceCalls, ResolveCalls, Refusals; public static Player EncounterPlayer;
        internal static FriendlyEncounterInteraction Interaction; internal static AllegianceFriendlyGreeting Greeting; public static BotOwner InteractionBot;
        public static bool AcceptScavRecruitment(BotOwner bot,IPlayer player) { AcceptanceCalls++; return Allowed && Acceptance; }
        public static bool TryResolveRecruitmentAcceptance(BotOwner bot,IPlayer player,bool proactive,out bool accepted) {
            ResolveCalls++;accepted=ResolveAccepted;
            if(ResolveHandled)return true;
            if(!proactive && GameplayModeRuntime.IsAllegiance && bot.Side==EPlayerSide.Savage){accepted=AcceptScavRecruitment(bot,player);return true;}
            return false;
        }
        internal static bool TryGetInteraction(BotOwner bot,out FriendlyEncounterInteraction interaction,out AllegianceFriendlyGreeting greeting) {
            interaction=Interaction;greeting=Greeting;return bot==InteractionBot && IsEnabled;
        }
        public static void NoteRequestedRefusal(BotOwner bot) {Refusals++;if(bot.Side!=EPlayerSide.Savage)BossPlayers.RememberRecruitmentDenial(bot.ProfileId);Interaction?.BeginRefusal(bot);Greeting?.Cancel();}
        public static bool CanRecruit(BotOwner bot, IPlayer player) => Allowed;
        public static void RecordCooperationAttempt(BotOwner bot,IPlayer requester,bool canReact) { }
    }
    public class Logger
    {
        public static void LogInfo(string message) { }
        public static int Warnings;
        public static bool AllowErrors;public static int Errors;
        public void LogWarning(string message) => Warnings++;
        public static void LogError(object error) {if(!AllowErrors)throw new Exception("Unexpected recruitment failure",error as Exception);Errors++;}
    }
    public partial class BossPlayers
    {
        public static BossPlayers Instance = new(); public static pitAIBossPlayer Boss = new();
        public static HashSet<string> Denied = new(); public static int Added;
        public pitAIBossPlayer GetBossPlayer(string id) => Boss;
        public static bool IsFollower(BotOwner bot) => bot.IsFollower;
        public static bool IsPlayerBoss(string id) => Boss.Value.ProfileId==id;
        public static bool HasDeniedRecruitment(string id) => Denied.Contains(id);
        public static void RememberRecruitmentDenial(string id) => Denied.Add(id);
        public static List<BotFollowerPlayer> Active = new();
        public static List<BotFollowerPlayer> GetFollowersByBoss(string id) => Active;
        public static object AddFollower(BotOwner bot, pitAIBossPlayer boss) {
            Added++;bot.IsFollower=true;Active.Add(new BotFollowerPlayer {Bot=bot});return new();
        }
    }
}
namespace pitTeam.Patches
{
    public static class FollowerReloadPhraseRemap { public static EPhraseTrigger Remap(BotOwner owner, EPhraseTrigger phrase) => phrase; }
    public static class FollowerMutedCombatPhraseGate { public static bool ShouldBlock(BotOwner owner, EPhraseTrigger phrase) => false; }
    public static class FollowerContactPhraseGate {
        public static bool IsContactPhrase(EPhraseTrigger phrase) => false;
        public static bool ShouldAllowOrSchedule(BotOwner owner, EPhraseTrigger phrase, ETagStatus? mask) => true;
    }
    public static class BotOwnerManualUpdatePatch { public static Dictionary<string, Action<BotOwner>> BotOwnerUpdate = new(); }
}
namespace pitTeam.Utils
{
    public static class FollowerMedical { public static bool IsUsingMedical(BotOwner bot) => bot.UsingMedical; }
    public static class Utils
    {
        public static Action Pending;
        public static bool HeadVisible = true, BodyVisible = true;
        public static int SightChecks;
        public static bool CanShootToTarget(ShootToPoint target, Vector3 origin, int mask, bool doubleSide) {
            SightChecks++;
            return target.Point.y == 2 ? HeadVisible : BodyVisible;
        }
        public static void SetTimeout(Action callback, int delayMs) => Pending = callback;
    }
}
namespace SAIN.Plugin
{
    public enum EPersonality { Coward, Rat, Normal, Chad, GigaChad, Wreckless, SnappingTurtle, Timmy, FuturePersonality }
    public class NativeInfo {
        public bool Throw;
        public EPersonality Value=EPersonality.Normal;
        public EPersonality Personality => Throw ? throw new Exception("native personality probe failed") : Value;
    }
    public class NativeBot
    {
        public NativeInfo Info { get; set; } = new();
        public bool ActiveEnemy, Throw;
        public bool HasEnemy => Throw ? throw new InvalidOperationException("native probe failed") : ActiveEnemy;
    }
    public static class SAINEnableClass
    {
        public static NativeBot Bot;
        public static bool GetSAIN(string id, out NativeBot bot) { bot = Bot; return bot != null; }
    }
}
public static class RecruitmentCombatChecks
{
    private static int count;
    private static readonly MethodInfo Prefix = typeof(FollowRequestPatch).GetMethod("PatchPrefix", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo Complete = typeof(FollowRequestPatch).GetMethod("CompleteRecruitConversion", BindingFlags.NonPublic | BindingFlags.Static);
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
    private static BotOwner Fresh()
    {
        AllegiancePmcFriendship.Allowed = true;
        AllegiancePmcFriendship.Acceptance = true; AllegiancePmcFriendship.AcceptanceCalls = 0;
        GameplayModeRuntime.IsAllegiance = false;
        BossPlayers.Denied.Clear(); BossPlayers.Active.Clear(); BossPlayers.Added = 0; BossPlayers.Boss = new();
        BotOwnerManualUpdatePatch.BotOwnerUpdate.Clear(); pitTeam.Utils.Utils.Pending = null;
        pitFireTeam.IsSAINInstalled = true; pitFireTeam.pickupEnabled.Value = true;
        pitFireTeam.tieredPickup.Value = true; pitFireTeam.maximumPickup.Value = 10;
        SAIN.Plugin.SAINEnableClass.Bot = new();
        Logger.Warnings = 0;
        var bot=new BotOwner();FollowerForcedPhraseGate.Clear(bot);return bot;
    }
    private static void Ask(BotOwner bot)
    {
        object[] args = { new BotGroupRequestController(), true, BossPlayers.Boss.Value, bot };
        Check(!(bool)Prefix.Invoke(null, args) && !(bool)args[1], "Recruitment owns the request result");
    }
    private static void Deferred(BotOwner bot) => BotOwnerManualUpdatePatch.BotOwnerUpdate[bot.ProfileId](bot);
    public static int Run()
    {
        foreach(int level in new[]{0,20,100}) {
            var fieldScav=Fresh();GameplayModeRuntime.IsAllegiance=true;fieldScav.Side=EPlayerSide.Savage;
            fieldScav.Profile.Info.Level=level;BossPlayers.Denied.Add(fieldScav.ProfileId);
            Ask(fieldScav);Deferred(fieldScav);pitTeam.Utils.Utils.Pending();
            Check(BossPlayers.Added==1 && AllegiancePmcFriendship.AcceptanceCalls==1,"Scav acceptance bypasses generated levels and legacy level denial at all conversion stages");
        }
        var refusingScav=Fresh();GameplayModeRuntime.IsAllegiance=true;refusingScav.Side=EPlayerSide.Savage;AllegiancePmcFriendship.Acceptance=false;
        Ask(refusingScav);
        Check(refusingScav.BotTalk.Last==EPhraseTrigger.Negative && BotOwnerManualUpdatePatch.BotOwnerUpdate.Count==0 && BossPlayers.Denied.Count==0,"Scav chance refusal does not queue conversion or create a PMC level denial");
        var fightingScav=Fresh();GameplayModeRuntime.IsAllegiance=true;fightingScav.Side=EPlayerSide.Savage;fightingScav.Memory.GoalEnemy=new();Ask(fightingScav);
        Check(AllegiancePmcFriendship.AcceptanceCalls==0 && fightingScav.BotTalk.Last==EPhraseTrigger.DontKnow,"Combat precedes Scav acceptance roll");
        var cappedScav=Fresh();GameplayModeRuntime.IsAllegiance=true;cappedScav.Side=EPlayerSide.Savage;
        BossPlayers.Active.Add(new BotFollowerPlayer {Bot=new BotOwner()});BossPlayers.Active.Add(new BotFollowerPlayer {Bot=new BotOwner()});Ask(cappedScav);
        Check(AllegiancePmcFriendship.AcceptanceCalls==0 && BotOwnerManualUpdatePatch.BotOwnerUpdate.Count==0,"Capacity precedes Scav acceptance roll");
        foreach(bool allegiance in new[]{false,true}) {
            foreach(var side in new[]{EPlayerSide.Usec,EPlayerSide.Bear}) {
                var scav=Fresh();GameplayModeRuntime.IsAllegiance=allegiance;
                BossPlayers.Boss.Value.Side=EPlayerSide.Savage;scav.Side=side;Ask(scav);
                Check(BotOwnerManualUpdatePatch.BotOwnerUpdate.Count==0 && BossPlayers.Added==0,"Player Scav cannot recruit PMC in either mode");
            }
            var friendlyScav=Fresh();GameplayModeRuntime.IsAllegiance=allegiance;
            BossPlayers.Boss.Value.Side=EPlayerSide.Savage;friendlyScav.Side=EPlayerSide.Savage;
            Ask(friendlyScav);Deferred(friendlyScav);pitTeam.Utils.Utils.Pending();
            Check(BossPlayers.Added==1,"Same-side Scav recruitment preserves Fence path in either mode");
        }
        foreach(bool allegiance in new[]{false,true}) {
            Fresh();GameplayModeRuntime.IsAllegiance=allegiance;pitFireTeam.maximumPickup.Value=2;
            var queued=new List<Action>();var candidates=new List<BotOwner>();
            for(int i=0;i<3;i++) {
                var queuedCandidate=new BotOwner {ProfileId="queued-"+i,Side=allegiance && i%2==0 ? EPlayerSide.Bear : EPlayerSide.Usec};
                queuedCandidate.Profile.Info.Level=1;candidates.Add(queuedCandidate);
                Ask(queuedCandidate);Deferred(queuedCandidate);queued.Add(pitTeam.Utils.Utils.Pending);
            }
            foreach(var conversion in queued) conversion();
            Check(BossPlayers.Added==2 && candidates[2].BotTalk.Last==EPhraseTrigger.Negative && BossPlayers.Denied.Count==0,"First-group delay cannot exceed pickup limit or cache capacity refusal");
        }
        var duplicate=Fresh();duplicate.Profile.Info.Level=1;Ask(duplicate);Deferred(duplicate);
        var firstConversion=pitTeam.Utils.Utils.Pending;Ask(duplicate);Deferred(duplicate);
        var secondConversion=pitTeam.Utils.Utils.Pending;firstConversion();secondConversion();
        Check(BossPlayers.Added==1,"Duplicate delayed callbacks cannot convert the same bot twice");
        var freed=Fresh();freed.Profile.Info.Level=1;pitFireTeam.maximumPickup.Value=1;
        BossPlayers.Active.Add(new BotFollowerPlayer {Bot=new BotOwner(),IsSquadMate=true});
        BossPlayers.Active.Add(new BotFollowerPlayer {Bot=new BotOwner {IsDead=true}});
        BossPlayers.Active.Add(new BotFollowerPlayer {Bot=new BotOwner {BotState=EBotState.Inactive}});
        Ask(freed);Deferred(freed);pitTeam.Utils.Utils.Pending();
        Check(BossPlayers.Added==1,"Saved squadmates, dead and inactive pickups do not consume pickup capacity");
        var cross=Fresh();cross.Side=EPlayerSide.Bear;cross.Profile.Info.Level=1;Ask(cross);
        Check(cross.BotTalk.Last==EPhraseTrigger.Toxic && BotOwnerManualUpdatePatch.BotOwnerUpdate.Count==0,"Guns for Hire keeps its same-side recruitment rule");
        cross=Fresh();GameplayModeRuntime.IsAllegiance=true;cross.Side=EPlayerSide.Bear;cross.Profile.Info.Level=1;Ask(cross);Deferred(cross);pitTeam.Utils.Utils.Pending();
        Check(BossPlayers.Added==1 && cross.Side==EPlayerSide.Bear,"Selected BEAR passes USEC request and both deferred conversion gates");
        cross=Fresh();GameplayModeRuntime.IsAllegiance=true;cross.Side=EPlayerSide.Bear;cross.Profile.Info.Level=1;AllegiancePmcFriendship.Allowed=false;Ask(cross);
        Check(BotOwnerManualUpdatePatch.BotOwnerUpdate.Count==0,"Unselected opposite faction cannot queue conversion");
        cross=Fresh();GameplayModeRuntime.IsAllegiance=true;cross.Side=EPlayerSide.Bear;cross.Profile.Info.Level=40;Ask(cross);cross.Profile.Info.Level=1;Ask(cross);
        Check(BossPlayers.HasDeniedRecruitment(cross.ProfileId) && BotOwnerManualUpdatePatch.BotOwnerUpdate.Count==0,"Cross-faction tiered refusal stays sticky");
        cross=Fresh();GameplayModeRuntime.IsAllegiance=true;cross.Side=EPlayerSide.Bear;cross.Profile.Info.Level=1;SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy=true;Ask(cross);
        Check(cross.BotTalk.Last==EPhraseTrigger.DontKnow && BossPlayers.Denied.Count==0,"Opposite-faction SAIN combat refusal stays temporary");
        cross=Fresh();GameplayModeRuntime.IsAllegiance=true;cross.Side=EPlayerSide.Bear;cross.Profile.Info.Level=1;Ask(cross);Deferred(cross);AllegiancePmcFriendship.Allowed=false;pitTeam.Utils.Utils.Pending();
        Check(BossPlayers.Added==0,"Opposite-faction revocation during conversion delay prevents pickup");
        cross=Fresh();GameplayModeRuntime.IsAllegiance=true;cross.Side=EPlayerSide.Bear;cross.Profile.Info.Level=1;pitFireTeam.maximumPickup.Value=0;Ask(cross);
        Check(BotOwnerManualUpdatePatch.BotOwnerUpdate.Count==1 && BossPlayers.Denied.Count==0,"Raw cfg capacity cannot disable Allegiance pickup");
        GameplayModeRuntime.IsAllegiance=true;
        var saved=new Profile();saved.Info.Side=EPlayerSide.Bear;
        Check(BotsControllerPatch.ResolveFollowerSpawnSide(saved,EPlayerSide.Usec)==EPlayerSide.Bear,"Allegiance spawn preserves saved BEAR faction");
        saved.Info.Side=EPlayerSide.Usec;
        Check(BotsControllerPatch.ResolveFollowerSpawnSide(saved,EPlayerSide.Bear)==EPlayerSide.Usec,"Allegiance spawn preserves saved USEC faction");
        saved.Info.Side=EPlayerSide.Savage;
        Check(BotsControllerPatch.ResolveFollowerSpawnSide(saved,EPlayerSide.Usec)==EPlayerSide.Usec && BotsControllerPatch.ResolveFollowerSpawnSide(null,EPlayerSide.Bear)==EPlayerSide.Bear,"Invalid or missing saved PMC faction uses leader fallback");
        GameplayModeRuntime.IsAllegiance=false;saved.Info.Side=EPlayerSide.Bear;
        Check(BotsControllerPatch.ResolveFollowerSpawnSide(saved,EPlayerSide.Usec)==EPlayerSide.Usec,"Guns for Hire spawn retains existing leader-side behavior");
        var candidate=Fresh(); candidate.Profile.Info.Level=1; AllegiancePmcFriendship.Allowed=false; Ask(candidate);
        Check(BotOwnerManualUpdatePatch.BotOwnerUpdate.Count==0 && BossPlayers.Denied.Count==0, "Unselected Allegiance candidate refused without a level roll");
        candidate=Fresh(); candidate.Profile.Info.Level=1; Ask(candidate); AllegiancePmcFriendship.Allowed=false; Deferred(candidate);
        Check(BossPlayers.Added==0 && pitTeam.Utils.Utils.Pending==null, "Revocation before manual update prevents recruitment");
        candidate=Fresh(); candidate.Profile.Info.Level=1; Ask(candidate); Deferred(candidate); AllegiancePmcFriendship.Allowed=false; pitTeam.Utils.Utils.Pending();
        Check(BossPlayers.Added==0, "Revocation during delayed conversion prevents recruitment");
        var bot = Fresh(); bot.Memory.GoalEnemy = new(); bot.Profile.Info.Level = 40; Ask(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow && !BossPlayers.HasDeniedRecruitment(bot.ProfileId), "EFT combat does not cache a level refusal");
        bot = Fresh(); bot.Profile.Info.Level = 40; SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = true; Ask(bot); Ask(bot);
        Check(bot.Memory.GoalEnemy == null && bot.BotTalk.Last == EPhraseTrigger.DontKnow && BossPlayers.Denied.Count == 0 && BotOwnerManualUpdatePatch.BotOwnerUpdate.Count == 0, "SAIN-only combat retries stay temporary");
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = false; bot.Profile.Info.Level = 1; Ask(bot); Deferred(bot);
        pitTeam.Utils.Utils.Pending(); Check(BossPlayers.Added == 1, "Combat-only refusal permits recruitment after combat");
        bot = Fresh(); bot.Profile.Info.Level = 40; Ask(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.Negative && BossPlayers.HasDeniedRecruitment(bot.ProfileId), "Peaceful level denial is cached");
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = true; Ask(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow && BossPlayers.HasDeniedRecruitment(bot.ProfileId), "Combat takes priority while preserving earlier refusal");
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = false; bot.Profile.Info.Level = 1; Ask(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.Negative && BotOwnerManualUpdatePatch.BotOwnerUpdate.Count == 0, "Earlier refusal remains final after combat");
        bot = Fresh(); bot.Profile.Info.Level = 1; Ask(bot);
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = true; Deferred(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow && BossPlayers.Added == 0 && BossPlayers.Denied.Count == 0 && pitTeam.Utils.Utils.Pending == null, "Combat before manual update prevents conversion");
        bot = Fresh(); bot.Profile.Info.Level = 1; Ask(bot); bot.Memory.GoalEnemy = new(); Deferred(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow && BossPlayers.Added == 0 && BossPlayers.Denied.Count == 0, "Deferred EFT combat remains temporary");
        bot = Fresh(); bot.Profile.Info.Level = 1; Ask(bot); BossPlayers.Denied.Add(bot.ProfileId);
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = true; Deferred(bot);
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow && BossPlayers.Added == 0, "Deferred combat precedes cached denial");
        bot = Fresh(); bot.Profile.Info.Level = 1; Ask(bot); Deferred(bot);
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = true; pitTeam.Utils.Utils.Pending();
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow && BossPlayers.Added == 0 && BossPlayers.Denied.Count == 0, "Combat during first-conversion delay prevents conversion");
        BossPlayers.Denied.Add(bot.ProfileId); Complete.Invoke(null, new object[] { bot, BossPlayers.Boss });
        Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow, "Final conversion combat precedes cached denial");
        SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = false; Complete.Invoke(null, new object[] { bot, BossPlayers.Boss });
        Check(bot.BotTalk.Last == EPhraseTrigger.Negative && BossPlayers.Added == 0, "Final conversion still enforces peaceful cached denial");
        bot = Fresh(); pitFireTeam.IsSAINInstalled = false; SAIN.Plugin.SAINEnableClass.Bot.ActiveEnemy = true;
        Check(!SainGoalEnemyBridge.HasEnemy(bot), "SAIN absence ignores native fixture");
        bot.Memory.GoalEnemy = new(); Ask(bot); Check(bot.BotTalk.Last == EPhraseTrigger.DontKnow, "SAIN absence preserves EFT combat gate");
        bot = Fresh(); SAIN.Plugin.SAINEnableClass.Bot = null;
        Check(!SainGoalEnemyBridge.HasEnemy(bot), "Missing SAIN component is safe");
        Check(!SainGoalEnemyBridge.HasEnemy(null), "Null owner is safe");
        SAIN.Plugin.SAINEnableClass.Bot = new() { Throw = true };
        Check(!SainGoalEnemyBridge.HasEnemy(bot), "Reflection failure is contained");
        Check(Logger.Warnings == 1 && !SainGoalEnemyBridge.HasEnemy(bot) && Logger.Warnings == 1, "Reflection failure logs once");
        return count;
    }
}
