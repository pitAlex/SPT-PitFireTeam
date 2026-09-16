using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using EFT;
using pitTeam.Modules;
using pitTeam.Components;
using pitTeam.Patches;
using UnityEngine;
namespace UnityEngine {
    public static class Mathf {
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static float Clamp(float v,float lo,float hi)=>Math.Max(lo,Math.Min(hi,v));
        public static bool Approximately(float a,float b)=>Math.Abs(a-b)<.000001f;
    }
}
namespace Newtonsoft.Json {public class JsonIgnoreAttribute:Attribute{}}
namespace EFT {
    public class BotCoreSettings {
        public float VisibleDistance=127,ScatteringPerMeter=.1275f,ScatteringClosePerMeter=.12f;
        public float AccuratySpeed=.15f,VisibleAngle=200,DamageCoeff=1;
        public bool CanGrenade=false;
        public BotCoreSettings(){}
        public BotCoreSettings(BotCoreSettings s){VisibleDistance=s.VisibleDistance;ScatteringPerMeter=s.ScatteringPerMeter;ScatteringClosePerMeter=s.ScatteringClosePerMeter;AccuratySpeed=s.AccuratySpeed;VisibleAngle=s.VisibleAngle;DamageCoeff=s.DamageCoeff;CanGrenade=s.CanGrenade;}
    }
    public class BotSettingsComponents {public BotCoreSettings Core=new();}
    public class BotCurrentSettings {
        public BotSettingsComponents FileSettings;public float VisibleCoef=.9f,ScatterCoef=.75f;
        public BotCurrentSettings(BotSettingsComponents settings){FileSettings=settings;}
        public float CurrentVisibleDistance=>FileSettings.Core.VisibleDistance*VisibleCoef;
        public float CurrentScattering=>FileSettings.Core.ScatteringPerMeter*ScatterCoef;
        public float CurrentScatteringClose=>FileSettings.Core.ScatteringClosePerMeter*ScatterCoef;
    }
    public class BotOwner {public string ProfileId;public BotFollowerPlayer Follower;}
}
public class BotAimingData {
    public BotOwner _owner;public float Baseline=.33f;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public float CalcTimeShoot(float distance,float angle)=>999;
}
namespace SPT.Reflection.Patching {
    public abstract class ModulePatch {protected abstract MethodBase GetTargetMethod();}
    public class PatchPostfixAttribute:Attribute{}
}
namespace pitTeam.Components {
    public class BotFollowerPlayer {
        public FollowerProficiencyValues Proficiency=new();public float LastBase,LastFinal;
        public void RecordProficiencyAimTime(float before,float after){LastBase=before;LastFinal=after;}
    }
}
namespace pitTeam.Modules {
    public class FollowerProficiencyValues {public FollowerProficiencyModifierValues Modifiers=new();}
    public static class BossPlayers {
        public static System.Collections.Generic.Dictionary<string,BotFollowerPlayer> Followers=new();
        public static BotFollowerPlayer GetFollowerByProfileId(string id)=>Followers.TryGetValue(id,out var f)?f:null;
    }
    public static class FollowerProficiency {
        public static bool TryGetValues(BotOwner bot,out FollowerProficiencyValues values){values=bot.Follower?.Proficiency;return values!=null;}
    }
    public static class Logger {public static void LogError(string s){throw new Exception(s);}public static void LogError(Exception e){throw e;}}
}
public class RecoilFixture {
    public BotOwner BotOwner {get;set;}
    private float _currentRecoilHorizAngle,_currentRecoilVertAngle;
    public float H=>_currentRecoilHorizAngle;public float V=>_currentRecoilVertAngle;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Calculate(){_currentRecoilHorizAngle=2;_currentRecoilVertAngle=-4;}
}
public static partial class ProficiencyChecks {
    private static int count;
    private static void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    private static bool Near(float a,float b)=>Math.Abs(a-b)<.0001;
    public static bool NativeSainAim(BotAimingData __instance,ref float __result){__result=__instance.Baseline;return false;}
    public static void Main(){
        var harmony=new Harmony("pitTeam.proficiency.audit");
        harmony.Patch(AccessTools.Method(typeof(BotAimingData),"CalcTimeShoot"),prefix:new HarmonyMethod(typeof(ProficiencyChecks),nameof(NativeSainAim)),postfix:new HarmonyMethod(AccessTools.Method(typeof(FollowerAimTimeProficiencyPatch),"PatchPostfix")));
        harmony.Patch(AccessTools.Method(typeof(RecoilFixture),"Calculate"),postfix:new HarmonyMethod(AccessTools.Method(typeof(RecoilHooks),"ApplyFollowerAccuracyToCalculatedRecoil")));
        var follower=new BotFollowerPlayer();var owner=new BotOwner{ProfileId="follower",Follower=follower};BossPlayers.Followers[owner.ProfileId]=follower;
        var aim=new BotAimingData{_owner=owner};var recoil=new RecoilFixture{BotOwner=owner};
        var shared=new BotCoreSettings();var first=new BotSettingsComponents{Core=shared};var ordinary=new BotSettingsComponents{Core=shared};
        var current=new BotCurrentSettings(first);var projection=new FollowerSainEftCoreProjection();var values=new FollowerSainCoreValues{VisibleDistance=250,ScatteringPerMeter=.08f,ScatteringClosePerMeter=.12f};
        Check(Near(current.CurrentVisibleDistance,114.3f)&&Near(current.CurrentScattering,.095625f),"reproduces recorded stale baseline");
        Check(projection.Apply(current,values),"projection invalidates changed vision cache");
        Check(Near(current.CurrentVisibleDistance,225)&&Near(current.CurrentScattering,.06f),"effective getters use SAIN baseline and existing modifiers");
        Check(Near(current.CurrentScatteringClose,.09f),"close scatter preserves its matching baseline");
        Check(!ReferenceEquals(first.Core,shared)&&ReferenceEquals(ordinary.Core,shared)&&Near(shared.VisibleDistance,127),"projection is follower-local even when original core is shared");
        Check(first.Core.AccuratySpeed==shared.AccuratySpeed&&first.Core.VisibleAngle==shared.VisibleAngle&&!first.Core.CanGrenade&&first.Core.DamageCoeff==shared.DamageCoeff,"projection preserves aim timing, FOV, damage and capabilities");
        var projected=first.Core;Check(!projection.Apply(current,values)&&ReferenceEquals(first.Core,projected)&&Near(current.CurrentVisibleDistance,225),"Chad or preset refresh is idempotent and reuses the local projection");
        foreach(float percent in new[]{50f,100f,150f,200f}){
            follower.Proficiency.Modifiers.SetPrecisionPercent(percent);follower.Proficiency.Modifiers.SetReactionPercent(percent);follower.Proficiency.Modifiers.SetVisionPercent(percent);
            var mods=follower.Proficiency.Modifiers;current.VisibleCoef=.9f*mods.SafeVisionDistanceFactor;current.ScatterCoef=.75f/mods.SafeAccuracyFactor;
            Check(Near(current.CurrentVisibleDistance,225*percent/100)&&Near(current.CurrentScattering,.06f*100/percent),"vision and precision remain proportional at "+percent);
            Check(Near(aim.CalcTimeShoot(30,10),.33f*100/percent),"combined aim speed survives SAIN prefix at "+percent);
            recoil.Calculate();Check(Near(recoil.H,2*100/percent)&&Near(recoil.V,-4*100/percent),"final SAIN recoil respects precision at "+percent);
            Check(Near(mods.ScaleReactionDelay(.3f),.3f*100/percent),"recognition factor stays proportional at "+percent);
        }
        follower.Proficiency.Modifiers.SetPrecisionPercent(100);follower.Proficiency.Modifiers.SetReactionPercent(150);
        aim.Baseline=.3338954f;Check(Near(aim.CalcTimeShoot(30,10),.2671163f),"reproduces Medved recorded aim-time reduction");
        Check(Near(follower.LastBase,.3338954f)&&Near(follower.LastFinal,.2671163f),"diagnostics retain native and final aim times");
        follower.Proficiency.Modifiers.SetPrecisionPercent(0);follower.Proficiency.Modifiers.SetReactionPercent(0);recoil.Calculate();
        Check(Near(recoil.H,40)&&Near(aim.CalcTimeShoot(30,10),.3338954f/.05f),"zero sliders use finite five-percent runtime floor");
        follower.Proficiency.Modifiers.SetPrecisionPercent(200);follower.Proficiency.Modifiers.SetReactionPercent(200);aim.Baseline=.01f;
        Check(Near(aim.CalcTimeShoot(1,0),.02f),"aim-time safety floor remains enforced");
        var nonFollower=new BotOwner{ProfileId="ordinary"};aim._owner=nonFollower;aim.Baseline=.33f;recoil.BotOwner=nonFollower;recoil.Calculate();
        Check(Near(aim.CalcTimeShoot(30,10),.33f)&&recoil.H==2&&recoil.V==-4,"ordinary SAIN bots retain native aim and recoil");
        projection.Restore();Check(ReferenceEquals(first.Core,shared),"dismissal restores original core reference");projection.Restore();
        Check(ReferenceEquals(first.Core,shared),"restoration is idempotent");
        projection.Apply(current,values);var replacement=new BotCoreSettings{VisibleDistance=333};first.Core=replacement;projection.Restore();
        Check(ReferenceEquals(first.Core,replacement),"cleanup preserves a newer core installed by another owner");
        projection.Apply(current,values);var otherSettings=new BotSettingsComponents();var otherCurrent=new BotCurrentSettings(otherSettings);
        projection.Apply(otherCurrent,values);Check(ReferenceEquals(first.Core,replacement)&&Near(otherCurrent.CurrentVisibleDistance,225),"settings replacement releases the old projection and applies to the new runtime");
        TestHotAimBindings();
        projection.Restore();Console.WriteLine("Passed "+count+" production proficiency checks with controlled EFT/SAIN fixtures.");
    }
}
