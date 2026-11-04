#pragma warning disable CS0649 // Controlled native stand-ins retain their default fields.
partial class RecruitHarness
{
    static string CreateRecruitProfileJson(Profile profile,object converters)=>profile.Id+":"+profile.Info.Settings.Role+":"+profile.Equipment;
}
static class Program
{
    static int checks;
    static void Check(bool pass,string label){if(!pass)throw new Exception(label);checks++;}
    static void Main()
    {
        foreach(bool extension in new[]{false,true})
        foreach(var role in Enum.GetValues<WildSpawnType>())
        foreach(var side in Enum.GetValues<EPlayerSide>())
        {
            GameplayModeAddons.Enabled=extension;var f=new Follower();f.Bot.Profile.Info.Settings.Role=role;f.Bot.Side=side;f.Bot.Profile.Info.Side=side;
            var captures=RecruitHarness.Capture(f);
            bool expected=!Utils.Props.BossFollowersType.Contains(role)&&(side!=EPlayerSide.Savage||role==WildSpawnType.exUsec&&extension);
            Check(captures.Count==(expected?1:0),"actual post-raid candidate side/role/addon admission: "+extension+"/"+role+"/"+side);
            if(expected)Check(captures[0].ProfileJson.Contains(":"+role+":kit"),"native role and captured kit forwarded");
        }
        GameplayModeAddons.Enabled=true;
        var a=new Follower();a.Bot.Profile.Id="Condor";a.Bot.Side=EPlayerSide.Savage;a.Bot.Profile.Info.Settings.Role=WildSpawnType.exUsec;
        var b=new Follower();b.Bot.Profile.Id="Sniperlife";b.Bot.Side=EPlayerSide.Savage;b.Bot.Profile.Info.Settings.Role=WildSpawnType.exUsec;
        var pair=RecruitHarness.Capture(a,b);
        Check(pair.Count==2&&pair.Select(c=>c.ProfileId).SequenceEqual(new[]{"Condor","Sniperlife"}),"both surviving Rogue recruits reach server candidate selection");
        Check(pair.All(c=>c.Side=="Savage"&&c.ProfileJson.Contains(":exUsec:")),"native Rogue identity reaches addon invitation policy");
        foreach(bool saved in new[]{false,true})
        {
            a.HasPersistentProgress=saved;
            Check(RecruitHarness.Capture(a).Count==(saved?0:1),"saved members cannot request another roster invitation");
        }
        a.HasPersistentProgress=false;a.Boss.realPlayer.Side=EPlayerSide.Savage;
        Check(RecruitHarness.Capture(a).Count==0,"player Scav does not queue a permanent recruit");
        a.Boss.realPlayer.Side=EPlayerSide.Usec;
        a.Bot.Profile.Customization.Clear();
        Check(RecruitHarness.Capture(a).Count==0,"missing native identity retains admission rejection");
        Console.WriteLine($"PASS: {checks} production post-raid Rogue/PMC/Scav/Goon candidate capture checks; no live extraction simulation.");
    }
}
enum EPlayerSide {Usec,Bear,Savage}
enum WildSpawnType {pmcUSEC,assault,exUsec,bossKnight,followerBigPipe,followerBirdEye}
enum EBodyModelPart {Voice,Head}
readonly record struct MongoID(string Value) {public override string ToString()=>Value;}
class Profile {public string Id="id",Nickname="recruit",AccountId="account",Equipment="kit";public Info Info=new();public Dictionary<EBodyModelPart,MongoID> Customization=new(){{EBodyModelPart.Voice,new("voice")},{EBodyModelPart.Head,new("head")}};}
class Info {public Settings Settings=new();public int Level=60;public EPlayerSide Side=EPlayerSide.Savage;}
class Settings {public WildSpawnType Role=WildSpawnType.exUsec;}
class Health {public bool IsAlive=true;}
class Player {public EPlayerSide Side=EPlayerSide.Usec;public Health HealthController=new();}
class Boss {public Player realPlayer=new();}
class Bot {public Profile Profile=new();public EPlayerSide Side=EPlayerSide.Usec;public bool IsDead;}
class Follower {public Bot Bot=new();public Boss Boss=new();public bool HasPersistentProgress,DebugForceRecruitInvite;public float CombatAggression=50;public string[] GivenEquipmentSlots=[];public Bot GetBot()=>Bot;public Boss GetBoss()=>Boss;}
static class GameplayModeAddons {public static bool Enabled;public static bool OwnsCompanionCombat(string role)=>Enabled&&role=="exUsec";}
static class GameplayModeRuntime {public static bool GetEffectiveValue(bool value)=>value;}
static class pitFireTeam {public static bool recruitPickup=true;}
static class Utils {public static class Props {public static WildSpawnType[] BossFollowersType=[WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye];}}
static class Modules {public static class Logger {public static void LogInfo(string message){}}}
