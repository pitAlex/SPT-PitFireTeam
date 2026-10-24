public static class RecruitmentPersonalityChecks
{
    private static int count;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
    private static readonly MethodInfo Create=typeof(BossPlayers).GetMethod("CreateRecruitCombatAggression",BindingFlags.NonPublic|BindingFlags.Static);
    private static float Capture(BotOwner owner) => (float)Create.Invoke(null,new object[]{owner});
    public static int Run()
    {
        var owner=new BotOwner();pitFireTeam.IsSAINInstalled=true;
        var personalities=new[]{SAIN.Plugin.EPersonality.Coward,SAIN.Plugin.EPersonality.Rat,SAIN.Plugin.EPersonality.Normal,SAIN.Plugin.EPersonality.Chad,SAIN.Plugin.EPersonality.GigaChad,SAIN.Plugin.EPersonality.Wreckless,SAIN.Plugin.EPersonality.SnappingTurtle,SAIN.Plugin.EPersonality.Timmy};
        var values=new[]{0f,30f,50f,70f,100f,100f,40f,20f};
        for(int i=0;i<personalities.Length;i++)
        {
            SAIN.Plugin.SAINEnableClass.Bot=new() {Info=new() {Value=personalities[i]}};
            float value=Capture(owner);
            Check(value==values[i],"Pre-conversion native personality determines aggression: "+personalities[i]);
            Check(SAIN.Plugin.SAINEnableClass.Bot.Info.Value==personalities[i],"Capture never mutates native personality");
            Check(Capture(owner)==value,"Native identity cannot reroll into an unrelated aggression");
        }
        float captured=Capture(owner);SAIN.Plugin.SAINEnableClass.Bot.Info.Value=SAIN.Plugin.EPersonality.GigaChad;
        Check(captured==20f,"Captured recruit value survives later personality conversion");
        Check(!RecruitCombatAggression.TryMap("FuturePersonality",out _),"Unknown native personality falls back safely");
        SAIN.Plugin.SAINEnableClass.Bot.Info.Value=SAIN.Plugin.EPersonality.FuturePersonality;
        float fallback=Capture(owner);Check(fallback>=20f&&fallback<=60f,"Unknown personality keeps legacy fallback");
        SAIN.Plugin.SAINEnableClass.Bot=null;fallback=Capture(owner);Check(fallback>=20f&&fallback<=60f,"Absent component keeps fallback");
        pitFireTeam.IsSAINInstalled=false;Check(!BotFollowerPlayer.TryGetNativeSainPersonality(owner,out _),"SAIN absence is a soft dependency");
        Check(!BotFollowerPlayer.TryGetNativeSainPersonality(null,out _),"Null native candidate is safe");
        pitFireTeam.IsSAINInstalled=true;SAIN.Plugin.SAINEnableClass.Bot=new(){Info=null};
        Check(!BotFollowerPlayer.TryGetNativeSainPersonality(owner,out _),"Uninitialized native info is safe");
        return count;
    }
}
