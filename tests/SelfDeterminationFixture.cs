#nullable enable annotations
using System;
using System.Collections.Generic;
using pitTeam.Shared;
public static class Mathf {public static bool Approximately(float a,float b)=>Math.Abs(a-b)<.001f;public static float Clamp(float x,float a,float b)=>Math.Max(a,Math.Min(b,x));}
public static class Time {public static float time;}
public static class BattleRecorder {public static void RecordCombatAggressionOverride(params object[] values){} }
public sealed class BotOwner {public string ProfileId;public Follower State;}
public sealed class BossPlayers {public static BossPlayers Instance=new BossPlayers();public Follower GetFollower(BotOwner owner)=>owner.State;}
public sealed class Follower {
    public bool _IsSquadMate=true;private float _selfDetermination=50;private float? _combatDeterminationOverride;
    public bool SavedBossPreferences;private bool UsesSavedCombatPreferences=>_IsSquadMate||SavedBossPreferences;
    private bool _combatRegroupUsesBossAnchor;private float _combatAggression=50,_temporaryCombatAggressionOverride,_temporaryCombatAggressionClearAfter;
    private bool _temporaryCombatAggressionOverrideActive;public Boss _player;
    private float? _companionCombatAggression, _companionCombatDetermination;
    private void ClearVanillaRequestState(object request,string source){} private void UpdateTemporaryCombatAggressionClearDelay(){}
__PROPERTIES__
__METHODS__
}
public sealed class Boss {
    private readonly HashSet<string> _activeFollowerCombatProfiles=new HashSet<string>();
    public List<BotOwner> Followers=new List<BotOwner>();public bool MemoryCombat;
    private bool IsCombatRegroupContext()=>MemoryCombat;
    public void CoreEnter(BotOwner owner){_activeFollowerCombatProfiles.Add(owner.ProfileId);_combatPercentageResetAt=0;}
    public void CoreExit(BotOwner owner)=>_activeFollowerCombatProfiles.Remove(owner.ProfileId);
    public void Tick()=>UpdateCombatPercentageOverrides();
__BOSS_STATE__
}
public static class Program {
    private static int checks;
    private static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    public static void Main(){
        var service=new Persistence();service.Settings.SelfDetermination=73;
        service.SetTeammateProficiency(new MongoId(),new FriendlyTeammateProficiencyRequest());
        Check(service.Settings.SelfDetermination==73,"legacy proficiency save preserves determination");
        foreach(float value in new[]{-4f,0f,20f,50f,100f,144f,float.NaN,float.PositiveInfinity}){
            service.SetTeammateProficiency(new MongoId(),new FriendlyTeammateProficiencyRequest{SelfDetermination=value});
            Check(service.Settings.SelfDetermination==SelfDeterminationPolicy.Normalize(value),"production save normalizes determination "+value);
        }
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})Check(SelfDeterminationPolicy.Normalize(bad)==50,"nonfinite default");
        Check(SelfDeterminationPolicy.Normalize(-4)==0&&SelfDeterminationPolicy.Normalize(144)==100,"clamped persistence");
        Check(!SelfDeterminationPolicy.AllowsAutomaticPosition(0,4,144),"zero rejects new outward firing point beyond escort");
        Check(SelfDeterminationPolicy.AllowsAutomaticPosition(0,400,144),"zero permits bossward recovery from existing separation");
        float[] values={0,25,50,75,100},rifle={10,14,18,27,36},sniper={10,18.5f,27,40.5f,54};
        for(int i=0;i<values.Length;i++){
            Check(SelfDeterminationPolicy.Trigger(values[i],18)==rifle[i],"rifle calibration "+values[i]);
            Check(SelfDeterminationPolicy.Trigger(values[i],27)==sniper[i],"marksman calibration "+values[i]);
            foreach(float radius in new[]{10,18,38})foreach(bool marksman in new[]{false,true}){
                float trigger=SelfDeterminationPolicy.Trigger(values[i],radius),arrival=SelfDeterminationPolicy.Completion(values[i],trigger,marksman?24:18,marksman);
                Check(arrival<=trigger-2&&arrival>=2,"arrival inside activation "+values[i]+"/"+radius+"/"+marksman);
            }
        }
        var squad=new Boss();var a=new BotOwner{ProfileId="core",State=new Follower{_player=squad}};var b=new BotOwner{ProfileId="addon",State=new Follower{_player=squad}};
        squad.Followers.Add(a);squad.Followers.Add(b);a.State.SelfDetermination=65;a.State.CombatAggression=37;
        squad.CoreEnter(a);squad.NotifyAddonCombatEpisode(a,true);squad.CoreExit(a);
        Check(squad.HasFollowerCombat,"same follower Core-to-addon handoff retains combat registration");squad.NotifyAddonCombatEpisode(a,false);
        a.State.SetCombatIndependent(true);Check(a.State.CombatIndependent&&a.State.EffectiveSelfDetermination==100&&a.State.EffectiveCombatAggression==37,"On Your Own changes determination only");
        a.State.ResetCombatPercentageOverrides("unitSetup");
        a.State.SetCompanionCombatPreferences(70,100);
        Check(a.State.CombatIndependent&&a.State.EffectiveCombatAggression==70&&a.State.DeterminationSource=="companion","companion unit stance supplies runtime baseline");
        Check(a.State.SelfDetermination==65&&a.State.CombatAggression==37,"companion stance preserves saved proficiency");
        a.State.SetCombatIndependent(false);a.State.SetTemporaryCombatAggressionOverride(0,"HoldPosition");
        Check(a.State.EffectiveSelfDetermination==20&&a.State.EffectiveCombatAggression==0,"explicit cover and hold override companion unit stance");
        a.State.ClearCompanionCombatPreferences();
        Check(a.State.EffectiveSelfDetermination==20&&a.State.EffectiveCombatAggression==0,"composition changes cannot erase player command overrides");
        a.State.SetCompanionCombatPreferences(70,100);a.State.ResetCombatPercentageOverrides("commandCompleted");
        Check(a.State.EffectiveSelfDetermination==100&&a.State.EffectiveCombatAggression==70,"command reset reveals current unit baseline");
        a.State.SetCompanionCombatLeader(b);
        a.State.ClearCompanionCombatPreferences();
        Check(a.State.CompanionCombatLeader==null,"unit release also clears the runtime movement leader");
        Check(a.State.EffectiveSelfDetermination==65&&a.State.EffectiveCombatAggression==37,"unit release restores exact saved settings");
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity}){
            a.State.SetCompanionCombatPreferences(bad,100);Check(a.State.EffectiveCombatAggression==37,"invalid companion aggression falls back to saved");
            a.State.SetCompanionCombatPreferences(70,bad);Check(a.State.EffectiveSelfDetermination==65,"invalid companion determination falls back to saved");
        }
        a.State.SetCombatIndependent(true);
        a.State.SetTemporaryCombatAggressionOverride(100,"GoForward");Check(a.State.EffectiveSelfDetermination==100&&a.State.EffectiveCombatAggression==100,"Go Forward changes aggression only");
        a.State.SetCombatIndependent(false);Check(a.State.EffectiveSelfDetermination==20&&a.State.EffectiveCombatAggression==100&&!a.State.CombatIndependent,"Cover Me exact twenty independently of aggression");
        a.State.SetTemporaryCombatAggressionOverride(0,"HoldPosition");Check(a.State.EffectiveSelfDetermination==20&&a.State.EffectiveCombatAggression==0,"Hold Position preserves determination");
        a.State.ResetCombatPercentageOverrides("GoGoGo");Check(a.State.EffectiveSelfDetermination==65&&a.State.EffectiveCombatAggression==37,"Go Go Go restores both saved values");
        a.State.SetCombatIndependent(true);a.State.SetTemporaryCombatAggressionOverride(0,"HoldPosition");
        a.State.ResetCombatPercentageOverrides("Attention");Check(a.State.EffectiveSelfDetermination==65&&a.State.EffectiveCombatAggression==37&&!a.State.HasCombatPercentageOverrides,"Attention restores both saved values immediately");
        a.State.SelfDetermination=0;a.State.SetCombatIndependent(false);Check(a.State.EffectiveSelfDetermination==20,"Cover Me raises zero to exactly twenty");
        a.State._IsSquadMate=false;a.State.SelfDetermination=90;a.State.ClearCombatDeterminationOverride();Check(a.State.SelfDetermination==50,"pickup fixed fifty baseline");
        a.State.SavedBossPreferences=true;Check(a.State.SelfDetermination==90&&a.State.DeterminationSource=="saved"&&!a.State._IsSquadMate,"saved boss proficiency works without squadmate classification");
        a.State.SavedBossPreferences=false;
        a.State.SetCombatIndependent(true);Check(a.State.EffectiveSelfDetermination==100,"accepted pickup command can override baseline");
        squad.CoreEnter(a);squad.NotifyAddonCombatEpisode(b,true);b.State.SetCombatIndependent(false);b.State.SetTemporaryCombatAggressionOverride(100,"GoForward");
        squad.CoreExit(a);a.State.ClearActiveCombatIndependent();a.State.ClearCombatIndependent();Time.time=10;squad.Tick();
        Check(a.State.EffectiveSelfDetermination==100&&b.State.EffectiveSelfDetermination==20&&b.State.EffectiveCombatAggression==100,"individual release retains mixed-squad overrides");
        squad.NotifyAddonCombatEpisode(b,false);squad.MemoryCombat=true;Time.time=20;squad.Tick();Check(b.State.EffectiveSelfDetermination==20,"admitted pending combat retains overrides");
        squad.MemoryCombat=false;Time.time=21;squad.Tick();b.State.ClearCombatIndependent();
        Check(b.State.EffectiveSelfDetermination==20,"last follower patrol handoff preserves quiet confirmation");
        Time.time=22;squad.Tick();Check(b.State.EffectiveCombatAggression==100,"quiet confirmation does not reset prematurely");
        Time.time=23.1f;squad.Tick();Check(a.State.EffectiveSelfDetermination==50&&b.State.EffectiveSelfDetermination==50&&b.State.EffectiveCombatAggression==50,"whole squad end resets both settings");
        squad.CoreEnter(a);a.State.SetCombatIndependent(false);Time.time=24;squad.CoreExit(a);squad.Tick();Time.time=26.1f;squad.Tick();Check(a.State.EffectiveSelfDetermination==50,"new episode rearms quiet reset");
        var goon = new Follower { _IsSquadMate = false, SavedBossPreferences = true };
        goon.CombatAggression = 41; goon.SelfDetermination = 62;
        goon.SetCombatIndependent(true); goon.SetTemporaryCombatAggressionOverride(70, "OnYourOwn");
        Check(goon.EffectiveCombatAggression == 70 && goon.EffectiveSelfDetermination == 100,
            "Goon On Your Own applies both temporary percentages");
        Check(goon.CombatAggression == 41 && goon.SelfDetermination == 62,
            "Goon command preserves saved proficiency percentages");
        goon.ResetCombatPercentageOverrides("squadCombatEnded");
        Check(goon.EffectiveCombatAggression == 41 && goon.EffectiveSelfDetermination == 62 && !goon.HasCombatPercentageOverrides,
            "battle reset restores both saved Goon percentages");
        Console.WriteLine("Passed "+checks+" production state and calibration checks.");
    }
}
public sealed class MongoId {}
public sealed class FriendlyTeammateProficiencyRequest {public string Aid;public object Proficiency;public float? SelfDetermination;}
public sealed class SavedSettings {public float SelfDetermination=50;public object Proficiency;}
public sealed class Persistence {
    public SavedSettings Settings=new SavedSettings();
    private object FindByAccountId(MongoId session,string aid)=>new object();
    private SavedSettings GetTeammateSettings(MongoId session,object teammate)=>Settings;
    private object NormalizeProficiency(object value)=>value;
    private void SaveTeammateSettings(MongoId session,object teammate,SavedSettings settings){}
__SAVE_METHOD__
}
