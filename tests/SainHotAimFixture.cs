using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Modules;

namespace pitTeam.Modules {
    public class FollowerSainProficiencyOverrides {
        public GlobalSettings Global=new GlobalSettings(); public AimSettings Aiming=new AimSettings();
        public class GlobalSettings {public bool FasterCQBReactionsGlobal=true; public float AimDownSightsAimTimeMultiplier=.5f,MinAimTime=.2f;}
        public class AimSettings {public bool FasterCQBReactions=true;public float FasterCQBReactionsDistance=10,FasterCQBReactionsMinimum=.1f,MAX_AIM_TIME=4;}
    }
}
public static partial class HotAimHooks {
    private static Stack<FollowerSainProficiencyOverrides> _activeAimValues;
    public class FollowerState {public Values Values=new Values();}
    public class Values {public FollowerSainProficiencyOverrides Sain=new FollowerSainProficiencyOverrides();}
    private static readonly FollowerState state=new FollowerState();
    private static bool TryGetActiveState(BotOwner bot,out FollowerState result){result=bot?.Follower!=null?state:null;return result!=null;}
}
public class HotAimBot { public BotOwner BotOwner{get;set;} }
public class FieldAimBot { public BotOwner BotOwner; }
public static class NativeAimFixture {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static float CalculateAim(HotAimBot bot,float distance,float angle,bool moving,bool panic,float delay){
        if(panic)throw new InvalidOperationException("fixture");
        return ClampAimTime(CalcFasterCQB(distance,CalcADSModifier(true,10,null),null,null),null,null);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float CalcADSModifier(bool aiming,float time,object diagnostic)=>time*2;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float CalcFasterCQB(float distance,float time,object settings,object diagnostic)=>time+10;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float ClampAimTime(float time,object settings,object diagnostic)=>time;
}
public static partial class ProficiencyChecks {
    private static void TestHotAimBindings(){
        var h=new Harmony("pitTeam.hotaim.audit");
        h.Patch(AccessTools.Method(typeof(NativeAimFixture),"CalculateAim"),
            prefix:new HarmonyMethod(typeof(HotAimHooks),"BeginDefaultFollowerAim"),
            finalizer:new HarmonyMethod(typeof(HotAimHooks),"EndDefaultFollowerAim"));
        foreach(var pair in new[]{("CalcADSModifier","UseDefaultFollowerAdsAimTime"),("CalcFasterCQB","UseDefaultFollowerFasterCqb"),("ClampAimTime","UseDefaultFollowerAimClamp")})
            h.Patch(AccessTools.Method(typeof(NativeAimFixture),pair.Item1),prefix:new HarmonyMethod(typeof(HotAimHooks),pair.Item2));
        var owner=new BotOwner{ProfileId="hot",Follower=new pitTeam.Components.BotFollowerPlayer()};
        var follower=new HotAimBot{BotOwner=owner};var ordinary=new HotAimBot{BotOwner=new BotOwner{ProfileId="other"}};
        Check(Near(NativeAimFixture.CalculateAim(follower,2,0,false,false,0),1),"direct argument bindings preserve follower ADS and CQB calculations");
        Check(Near(NativeAimFixture.CalculateAim(follower,30,0,false,false,0),4),"direct clamp binding preserves configured aim limits");
        Check(Near(NativeAimFixture.CalculateAim(ordinary,2,0,false,false,0),30),"ordinary bots retain native aim calculations after follower scope");
        try{NativeAimFixture.CalculateAim(follower,2,0,false,true,0);}catch(InvalidOperationException){}
        Check(Near(NativeAimFixture.CalculateAim(ordinary,2,0,false,false,0),30),"aim finalizer releases scope after native exception");
        Check(ReferenceEquals(SainBotOwnerAccessor.Get(follower),owner)&&ReferenceEquals(SainBotOwnerAccessor.Get(new FieldAimBot{BotOwner=owner}),owner),"cached owner access supports property and field layouts");
        Check(SainBotOwnerAccessor.Get(new object())==null&&SainBotOwnerAccessor.Get(null)==null,"unsupported owner layout fails open");
        h.UnpatchSelf();
    }
}
