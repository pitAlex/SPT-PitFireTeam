using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using Newtonsoft.Json.Linq;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN;
using SAIN.Components;
using SAIN.Preset;
using SAIN.Preset.Shared.Models.Preset.Personalities;
using SAIN.Preset.Shared.Personalities.BasePersonality;

// Use the real SAIN.Preset.Shared 4.5.1 settings/enum assembly. Only native game
// instances and preset delivery are fixtures; interpolation and lifecycle are production.
namespace SAIN.Preset {
    public class SAINPresetClass { public PersonalityManager PersonalityManager=new PersonalityManager(); }
    public class PersonalityManager {
        public Dictionary<EPersonality,PersonalitySettingsClass> PersonalityDictionary=new Dictionary<EPersonality,PersonalitySettingsClass>();
        public PersonalityManager(){
            var identities=new[]{EPersonality.Coward,EPersonality.Rat,EPersonality.Normal,EPersonality.Chad,EPersonality.GigaChad};
            float[] search={400,240,60,16,6}, sprint={0,0,10,60,75};
            for(int i=0;i<identities.Length;i++){
                var p=new PersonalitySettingsClass(identities[i]);
                p.Behavior.Search.SearchBaseTime=search[i];p.Behavior.Search.SprintWhileSearchChance=sprint[i];
                p.Behavior.Search.WillSearchForEnemy=i!=0;p.Behavior.Search.Sneaky=i==1;
                p.Behavior.Search.HeardFromPeaceBehavior=i==4?SAIN.Preset.Shared.Enums.EHeardFromPeaceBehavior.SearchNow:SAIN.Preset.Shared.Enums.EHeardFromPeaceBehavior.Freeze;
                p.Behavior.Rush.CanRushEnemyReloadHeal=i>=3;p.Behavior.Rush.CanJumpCorners=i==4;
                p.Behavior.Talk.CanTaunt=i>=3;p.Behavior.Talk.CanBegForLife=i==0;
                p.Behavior.Talk.CanFakeDeathRare=true;
                p.Assignment.RandomlyAssignedChance=10+i;
                p.Difficulty.GainSightCoef=10+i;
                p.Difficulty.AggressionCoef=1+i*0.1f;
                PersonalityDictionary.Add(identities[i],p);
            }
        }
    }
}
namespace SAIN.Components {
    public class Talk {
        public SAIN.SAINComponent.Classes.Talk.EnemyTalk EnemyTalk{get;}
        public Talk(BotComponent bot){EnemyTalk=new SAIN.SAINComponent.Classes.Talk.EnemyTalk(bot);}
    }
}
namespace SAIN.SAINComponent.Classes.Talk {
    public class EnemyTalk {
        private readonly BotComponent bot;
        public int Refreshes;public bool CanTaunt;
        public EnemyTalk(BotComponent bot){this.bot=bot;}
        protected void UpdatePresetSettings(SAINPresetClass preset){Refreshes++;CanTaunt=bot.Info.PersonalitySettings.Talk.CanTaunt;}
    }
}
namespace pitTeam.Modules {
    public static partial class BattleRecorder {
        public static void RecordCommandSet(BotFollowerPlayer f,FollowerCommandType c,UnityEngine.Vector3 target,float until,string source){}
    }
}
public static partial class CombatChecks {
    private static JObject CombatPolicy(PersonalitySettingsClass p)=>JObject.FromObject(new{
        p.Behavior.General,p.Behavior.Search,p.Behavior.Rush,p.Behavior.Cover,p.Difficulty.AggressionCoef});
    private static JObject NonCombatPolicy(PersonalitySettingsClass p){
        var difficulty=JObject.FromObject(p.Difficulty);difficulty.Remove("AggressionCoef");
        return JObject.FromObject(new{p.Behavior.Talk,p.Assignment,difficulty});
    }
    private static BotOwner PersonalityBot(string id,float aggression){
        var b=Spawn(id);b.Follower.CombatAggression=aggression;
        new SAINFollowerSoloCombatLayer(b,74);new SAINFollowerSquadCombatLayer(b,75);Tick();return b;
    }
    private static void TestPersonality(){
        var profiles=SAINPlugin.LoadedPreset.PersonalityManager.PersonalityDictionary;
        var identities=new[]{EPersonality.Coward,EPersonality.Rat,EPersonality.Normal,EPersonality.Chad,EPersonality.GigaChad};
        float[] anchors={0,30,50,70,100};
        var untouched=JObject.FromObject(SAINPlugin.LoadedPreset);
        var owners=new List<BotOwner>();
        for(int i=0;i<anchors.Length;i++){
            var b=PersonalityBot("personality"+i,anchors[i]);owners.Add(b);
            var actual=b.Sain.Info.PersonalitySettingsClass;var expected=profiles[identities[i]];
            Check(Math.Abs(b.Sain.Info.Difficulty.AggressionModifier-2*expected.Difficulty.AggressionCoef)<0.00001f,"native difficulty reads installed personality aggression "+anchors[i]);
            Check(b.Sain.Info.Personality==identities[i],"exact personality identity at "+anchors[i]);
            Check(JToken.DeepEquals(CombatPolicy(actual),CombatPolicy(expected)),"every combat setting equals anchor "+anchors[i]);
            Check(JToken.DeepEquals(NonCombatPolicy(actual),NonCombatPolicy(new PersonalitySettingsClass())),
                "speech assignment and mechanical difficulty stay neutral at "+anchors[i]);
            Check(!ReferenceEquals(actual,expected)&&!ReferenceEquals(actual.Behavior,expected.Behavior)&&
                !ReferenceEquals(actual.Behavior.Search,expected.Behavior.Search)&&!ReferenceEquals(actual.Difficulty,expected.Difficulty)&&
                !ReferenceEquals(actual.Assignment.AllowedTypes,expected.Assignment.AllowedTypes),"anchor copy owns nested settings and lists "+anchors[i]);
            Check(b.Settings.FileSettings.Mind.TIME_TO_FORGOR_ABOUT_ENEMY_SEC==60&&b.Sain.Info.ForgetEnemyTime==60,"anchor preserves both enemy memory durations "+anchors[i]);
        }
        var subject=owners[3];var originalSettings=subject.Sain.Info.PersonalitySettingsClass;
        int updates=subject.Sain.Info.Difficulty.Updates, searches=subject.Sain.Info.SearchRefreshes, talks=subject.Sain.Talk.EnemyTalk.Refreshes;
        Tick();Tick();
        Check(ReferenceEquals(subject.Sain.Info.PersonalitySettingsClass,originalSettings)&&subject.Sain.Info.Difficulty.Updates==updates&&
            subject.Sain.Info.SearchRefreshes==searches&&subject.Sain.Talk.EnemyTalk.Refreshes==talks,"unchanged preparation does not allocate settings or reroll caches");
        Check(JToken.DeepEquals(untouched,JObject.FromObject(SAINPlugin.LoadedPreset)),"all anchor applications leave shared preset unchanged");

        float[] samples={15,35,55,80};
        float[] expectedSearch={320,195,49,12.666667f};
        for(int i=0;i<samples.Length;i++){
            subject.Follower.CombatAggression=samples[i];Tick();
            Check(Math.Abs(subject.Sain.Info.PersonalitySettings.Search.SearchBaseTime-expectedSearch[i])<0.0001f,"numeric interpolation within segment "+samples[i]);
        }
        foreach(float midpoint in new[]{15f,40f,60f,85f}){
            subject.Follower.CombatAggression=midpoint-0.001f;Tick();var before=subject.Sain.Info.Personality;
            subject.Follower.CombatAggression=midpoint;Tick();var after=subject.Sain.Info.Personality;
            Check(before!=after,"discrete identity changes at midpoint "+midpoint);
        }
        subject.Follower.CombatAggression=84.999f;Tick();
        Check(!subject.Sain.Info.PersonalitySettings.Rush.CanJumpCorners&&subject.Sain.Info.PersonalitySettings.Search.HeardFromPeaceBehavior==SAIN.Preset.Shared.Enums.EHeardFromPeaceBehavior.Freeze,"boolean and enum remain lower below midpoint");
        subject.Follower.CombatAggression=85f;Tick();
        Check(subject.Sain.Info.PersonalitySettings.Rush.CanJumpCorners&&subject.Sain.Info.PersonalitySettings.Search.HeardFromPeaceBehavior==SAIN.Preset.Shared.Enums.EHeardFromPeaceBehavior.SearchNow,"boolean and enum switch together at midpoint");
        subject.Follower.CombatAggression=70;Tick();
        var activeSearch=new SAIN.Layers.Combat.Solo.SearchAction(subject);activeSearch.Start();activeSearch.SeedSprint();
        subject.Sain.Mover.WalkToPoint(new UnityEngine.Vector3(17,0,4));var path=subject.Sain.Mover.ActivePath;
        subject.Follower.OverrideAggression=0;Tick();
        Check(subject.Sain.Info.Personality==EPersonality.Coward&&subject.Follower.CombatAggression==70,"temporary hold changes settings without changing saved aggression");
        Check(!subject.Sain.Info.PersonalitySettings.Search.WillSearchForEnemy&&!activeSearch.Sprint&&activeSearch.SprintTimer==0,"zero aggression disables search permission and stale native sprint roll");
        Check(ReferenceEquals(subject.Sain.Mover.ActivePath,path),"personality refresh preserves active path");
        Check(!subject.Sain.Talk.EnemyTalk.CanTaunt&&!subject.Sain.Info.PersonalitySettings.Talk.CanBegForLife&&!subject.Sain.Info.PersonalitySettings.Talk.CanFakeDeathRare,"Coward combat never enables begging fake death or taunts");
        var snapshot=JObject.FromObject(SAINFollowerRuntime.GetPersonalitySnapshot(subject));
        Check((string)snapshot["source"]=="temporaryOverride"&&(string)snapshot["personality"]=="Coward","recorder distinguishes temporary aggression");
        int events=BattleRecorder.Events.FindAll(e=>e.Kind=="sainPersonality").Count;
        Tick();SAINFollowerRuntime.GetPersonalitySnapshot(subject);SainCombatRecorderBridge.Capture(subject);
        Check(BattleRecorder.Events.FindAll(e=>e.Kind=="sainPersonality").Count==events,"stable updates and snapshots do not emit transitions");
        subject.Follower.OverrideAggression=null;Tick();
        Check(subject.Sain.Info.Personality==EPersonality.Chad&&!subject.Sain.Talk.EnemyTalk.CanTaunt,"clearing temporary hold restores base combat behavior without importing talk traits");
        subject.Follower.CombatIndependent=true;Tick();
        Check(subject.Sain.Info.Personality==EPersonality.Chad,"independence does not force maximum aggression");

        var push=RegroupBot("personalityGoForward",80);
        push.Follower.CombatAggression=30;push.Follower.Command=FollowerCommandType.RegroupNearBoss;
        var regroup=SAINFollowerRuntime.GetRegroup(push);regroup.Observe();
        Check(regroup.Active,"Go Forward fixture starts an existing regroup");
        push.UsingMedical=true;var selfBefore=push.Sain.Decision.CurrentSelfDecision;
        push.Follower.SetTemporaryCombatAggressionOverride(0,"HoldPosition");
        push.Follower.SetPushEnemy(12);Tick();
        Check(push.Sain.Info.Personality==EPersonality.GigaChad&&push.Follower.CombatAggression==30&&
            push.Follower.EffectiveCombatAggression==100,"accepted Go Forward replaces hold with temporary GigaChad and preserves saved aggression");
        Check(push.Follower.Command==FollowerCommandType.None&&push.Follower._pushEnemyIssueSequence==0&&!regroup.Active,
            "addon replaces prior order and regroup without creating a durable core push");
        Check(push.UsingMedical&&push.Sain.Decision.CurrentSelfDecision==selfBefore,"aggression command preserves native medical work");
        push.Follower.ClearTemporaryCombatAggressionOverride("Gogogo");Tick();
        Check(push.Sain.Info.Personality==EPersonality.Rat&&push.Follower.EffectiveCombatAggression==30,"Gogogo after Go Forward restores saved Rat behavior");
        push.Follower.SetPushEnemy(12);push.Follower.ClearTemporaryCombatAggressionOverride("Gogogo");Tick();
        Check(push.Sain.Info.Personality==EPersonality.Rat,"Gogogo before the next preparation cannot be overwritten by a pending push");
        push.Follower.IgnoreCommands=true;push.Follower.SetPushEnemy(12);Tick();
        Check(!push.Follower.IsTemporaryCombatAggressionOverrideActive,"rejected core command never reaches addon aggression");
        push.Follower.IgnoreCommands=false;
        foreach(var tactic in new[]{FollowerCombatTactic.Balanced,FollowerCombatTactic.Marksman}){
            var core=Spawn("personalityCorePush"+tactic,tactic);core.Follower.SetPushEnemy(12);
            Check(core.Follower.Command==FollowerCommandType.PushEnemy&&float.IsPositiveInfinity(core.Follower._commandUntilTime)&&
                core.Follower._pushEnemyIssueSequence==1&&!core.Follower.IsTemporaryCombatAggressionOverrideActive,
                "core tactic retains durable ordered push "+tactic);
        }
        var unready=Spawn("personalityUnreadyPush");unready.Follower.SetPushEnemy(12);
        Check(unready.Follower.Command==FollowerCommandType.PushEnemy&&!unready.Follower.IsTemporaryCombatAggressionOverrideActive,
            "unready addon retains core push fallback");
        pitTeam.pitFireTeam.IsSAINAddonInstalled=false;push.Follower.SetPushEnemy(12);
        Check(push.Follower.Command==FollowerCommandType.PushEnemy&&!push.Follower.IsTemporaryCombatAggressionOverrideActive,
            "absent addon cannot handle the command");
        pitTeam.pitFireTeam.IsSAINAddonInstalled=true;push.Follower.ClearCommand("fixture");

        var other=PersonalityBot("personalityIndependentCopy",70);
        Check(!ReferenceEquals(other.Sain.Info.PersonalitySettingsClass,subject.Sain.Info.PersonalitySettingsClass),"same aggression still has independent follower copies");
        subject.Sain.Info.PersonalitySettings.Search.SearchBaseTime=999;
        Check(other.Sain.Info.PersonalitySettings.Search.SearchBaseTime==16&&profiles[EPersonality.Chad].Behavior.Search.SearchBaseTime==16,"changing one follower copy cannot affect another or a preset");
        subject.Sain.Info.SetPersonality(EPersonality.Normal);Tick();
        Check(subject.Sain.Info.PersonalitySettings.Search.SearchBaseTime==16&&subject.Sain.Info.Personality==EPersonality.Chad,"native preset reconfiguration reinstalls addon policy");
        profiles[EPersonality.Chad].Behavior.Search.SearchBaseTime=24;
        subject.Sain.Info.SetPersonality(EPersonality.Normal);Tick();
        Check(subject.Sain.Info.PersonalitySettings.Search.SearchBaseTime==24,"in-place preset edit is consumed after native preset refresh");
        profiles[EPersonality.Chad].Behavior.Search.SearchBaseTime=16;

        var replacement=new SAINPresetClass();replacement.PersonalityManager.PersonalityDictionary[EPersonality.Chad].Behavior.Search.SearchBaseTime=28;
        SAINPlugin.LoadedPreset=replacement;Tick();
        Check(subject.Sain.Info.PersonalitySettings.Search.SearchBaseTime==28,"replacement preset refreshes without relying on native reroll");
        var oldBot=subject.Sain;subject.Sain=new BotComponent(subject);Tick();
        Check(subject.Sain.Info.Personality==EPersonality.Chad&&oldBot.Info.Personality==EPersonality.Normal,"native component replacement restores previous instance and prepares new one");
        subject.Follower.CombatTactic=FollowerCombatTactic.Balanced;Tick();
        Check(subject.Sain.Info.Personality==EPersonality.Normal&&!pitTeam.pitFireTeam.UseSainFollowerCombat(subject),"tactic opt-out restores native personality");
        subject.Follower.CombatTactic=FollowerCombatTactic.SainMan;Tick();

        subject.Follower.CombatAggression=-20;Tick();Check(subject.Sain.Info.Personality==EPersonality.Coward,"negative input clamps to zero");
        subject.Follower.CombatAggression=200;Tick();Check(subject.Sain.Info.Personality==EPersonality.GigaChad,"large input clamps to 100");
        subject.Follower.CombatAggression=float.NaN;Tick();Check(subject.Sain.Info.Personality==EPersonality.Normal,"non-finite input uses neutral 50");
        subject.Follower.CombatAggression=0;Tick();
        var currentProfiles=SAINPlugin.LoadedPreset.PersonalityManager.PersonalityDictionary;
        var coward=currentProfiles[EPersonality.Coward];currentProfiles.Remove(EPersonality.Coward);Tick();
        Check(!pitTeam.pitFireTeam.UseSainFollowerCombat(subject)&&subject.Follower.CombatTactic==FollowerCombatTactic.SainMan,"missing required profile retains saved tactic and core fallback");
        currentProfiles.Add(EPersonality.Coward,coward);Tick();
        Check(pitTeam.pitFireTeam.UseSainFollowerCombat(subject)&&subject.Sain.Info.Personality==EPersonality.Coward,"profile recovery restores addon readiness");


        var rollback=PersonalityBot("personalityRollback",70);
        var prior=rollback.Sain.Info.PersonalitySettingsClass;float priorAggression=rollback.Sain.Info.Difficulty.AggressionModifier;
        rollback.Sain.Info.Difficulty.FailNextUpdate=true;bool failed=false;
        try { SainManPersonality.Apply(rollback,rollback.Sain.Info,SAINPlugin.LoadedPreset,EPersonality.Rat,
            SAINFollowerPersonality.Blend(currentProfiles[EPersonality.Rat],currentProfiles[EPersonality.Rat],0)); }
        catch(TargetInvocationException){failed=true;}
        Check(failed&&ReferenceEquals(prior,rollback.Sain.Info.PersonalitySettingsClass)&&rollback.Sain.Info.Personality==EPersonality.Chad,
            "failed native application restores previous identity and settings reference");
        Check(rollback.Sain.Info.Difficulty.AggressionModifier==priorAggression&&rollback.Sain.Info.ForgetEnemyTime==60&&rollback.Settings.FileSettings.Mind.TIME_TO_FORGOR_ABOUT_ENEMY_SEC==60,
            "failed application preserves aggression and both memory durations");
        rollback.Sain.Info.SetPersonality(EPersonality.GigaChad);rollback.Follower.CombatTactic=FollowerCombatTactic.Balanced;Tick();
        Check(rollback.Sain.Info.Personality==EPersonality.GigaChad,"cleanup preserves a newer native settings owner");
        var removed=Spawn("personalityRemovedOriginal");removed.Sain.Info.SetPersonality(EPersonality.Coward);
        var originalNative=removed.Sain.Info.PersonalitySettingsClass;
        new SAINFollowerSoloCombatLayer(removed,74);new SAINFollowerSquadCombatLayer(removed,75);Tick();
        currentProfiles.Remove(EPersonality.Coward);removed.Follower.CombatTactic=FollowerCombatTactic.Balanced;Tick();
        Check(removed.Sain.Info.Personality==EPersonality.Coward&&ReferenceEquals(removed.Sain.Info.PersonalitySettingsClass,originalNative),
            "removed original preset restores original reference instead of stranding addon settings");
        currentProfiles.Add(EPersonality.Coward,coward);

        var malformed=new PersonalitySettingsClass(EPersonality.Chad);malformed.Behavior.Search.SearchBaseTime=float.NaN;
        bool rejected=false;try{SAINFollowerPersonality.Blend(malformed,malformed,0);}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"invalid source setting is rejected before native publication");
        var held=EngageBot("personalityFailedAttempt");
        var attempt=SAINFollowerRuntime.GetEngageAttempt(held);
        var engage=new SAINFollowerMoveToEngageAction(held);held.Sain.Decision.EnemyDecisions.FiringPosition=null;engage.Start();engage.Update(null);
        Check(attempt.FailedFor(held.Sain.GoalEnemy),"engagement fixture owns a failed contact");
        held.Follower.SetPushEnemy(12);Tick();
        Check(ReferenceEquals(attempt,SAINFollowerRuntime.GetEngageAttempt(held))&&attempt.FailedFor(held.Sain.GoalEnemy),"personality transition cannot rearm a failed engagement");
        SAINPlugin.LoadedPreset=new SAINPresetClass();Tick();
    }
}
