param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$sources=foreach($name in @('FriendlyEncounterPopulationPolicy','FriendlyEncounterPopulation')){
    $source=Get-Content -Raw (Join-Path $RepositoryRoot "client/Modules/$name.cs")
    [regex]::Replace($source,'(?m)^using [^\r\n]+\r?\n','')
}
$fixture=@'
using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
namespace Comfort.Common {public static class Singleton<T> {public static T Instance;}}
public class GameWorld {public List<Player> AllAlivePlayersList=new List<Player>();}
namespace EFT {
    public enum EPlayerSide {Usec,Bear,Savage}
    public enum WildSpawnType {assault,marksman,cursedAssault,assaultGroup,crazyAssaultEvent,pmcUSEC,pmcBEAR,bossKnight,followerBigPipe,followerBirdEye,bossSanitar}
    public class Health {public bool IsAlive=true;}
    public class Settings {public WildSpawnType Role;}
    public class Info {public EPlayerSide Side;public Settings Settings=new Settings();}
    public class Profile {public Info Info=new Info();}
    public class AIData {public BotOwner BotOwner;}
    public class Player {public string ProfileId=Guid.NewGuid().ToString();public AIData AIData=new AIData();public Profile Profile=new Profile();public Health HealthController=new Health();}
    public class BotOwner {
        public bool IsDead,Grouped;public Player GetPlayer=new Player();public BotsGroup BotsGroup=new BotsGroup();
        public BotOwner(){GetPlayer.AIData.BotOwner=this;BotsGroup.Members.Add(this);}
        public string ProfileId=>GetPlayer.ProfileId;public Profile Profile=>GetPlayer.Profile;public EPlayerSide Side=>Profile.Info.Side;
    }
}
public class BotsGroup {public List<BotOwner> Members=new List<BotOwner>();public int MembersCount=>Members.Count;public BotOwner Member(int i)=>Members[i];}
namespace pitTeam.Modules {
    public static class BossPlayers {public static HashSet<BotOwner> Followers=new HashSet<BotOwner>();public static bool IsFollower(BotOwner bot)=>Followers.Contains(bot);}
    public static class AllegiancePmcFriendship {
        public static int Setting=55;public static HashSet<BotOwner> Selected=new HashSet<BotOwner>();
        public static int GetSettingPercent()=>Setting;public static bool IsGrouped(BotOwner bot)=>bot.Grouped;
        public static bool HasPopulationFriendship(BotOwner bot)=>Selected.Contains(bot)&&!bot.Grouped;
    }
    public static class AllegianceScavRelations {public static HashSet<BotsGroup> Calmed=new HashSet<BotsGroup>();public static bool IsCalmedGroup(BotsGroup group)=>Calmed.Contains(group);}
}
public static class RuntimePopulationChecks {
    static int checks;static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static void Reset(int setting=55){Singleton<GameWorld>.Instance=new GameWorld();pitTeam.Modules.AllegiancePmcFriendship.Setting=setting;pitTeam.Modules.AllegiancePmcFriendship.Selected.Clear();pitTeam.Modules.AllegianceScavRelations.Calmed.Clear();pitTeam.Modules.BossPlayers.Followers.Clear();}
    static BotOwner Spawn(WildSpawnType role=WildSpawnType.assault){var bot=new BotOwner();bot.Profile.Info.Settings.Role=role;bot.Profile.Info.Side=role==WildSpawnType.pmcUSEC?EPlayerSide.Usec:role==WildSpawnType.pmcBEAR?EPlayerSide.Bear:EPlayerSide.Savage;Singleton<GameWorld>.Instance.AllAlivePlayersList.Add(bot.GetPlayer);return bot;}
    static List<BotOwner> Population(int count,WildSpawnType role=WildSpawnType.assault){var bots=new List<BotOwner>();for(int i=0;i<count;i++)bots.Add(Spawn(role));return bots;}
    static void Select(BotOwner bot)=>pitTeam.Modules.AllegiancePmcFriendship.Selected.Add(bot);
    static bool Admit(BotOwner bot)=>pitTeam.Modules.FriendlyEncounterPopulation.CanAdmit(bot,out _,out _,out _);
    public static int Run(){
        Reset();var bots=Population(10);
        for(int i=0;i<5;i++){Check(Admit(bots[i]),"55% allows each of five successful Scav rolls");Select(bots[i]);}
        Check(!pitTeam.Modules.FriendlyEncounterPopulation.CanAdmit(bots[5],out int alive,out int friendly,out int limit)&&alive==10&&friendly==5&&limit==5,"sixth friendly exceeds five-of-ten cap");
        Check(pitTeam.Modules.AllegiancePmcFriendship.Selected.Count==5,"quota query never mutates established relationships");
        Reset(40);bots=Population(8);for(int i=0;i<3;i++)Select(bots[i]);
        bots[6].IsDead=true;bots[7].GetPlayer.HealthController.IsAlive=false;
        var newcomer=Spawn();
        Check(!pitTeam.Modules.FriendlyEncounterPopulation.CanAdmit(newcomer,out alive,out friendly,out limit)&&alive==7&&friendly==3&&limit==2,"hostile deaths grandfather three friendships and block new spawn above reduced cap");
        Check(pitTeam.Modules.AllegiancePmcFriendship.Selected.Count==3,"hostile deaths do not undo existing friendly encounters");
        Check(!Admit(Spawn())&&!Admit(Spawn()),"spawns at populations eight and nine remain blocked with three friendly Scavs");
        Check(Admit(Spawn()),"population ten restores one free slot under 40% cap");
        Check(Admit(bots[0]),"already-friendly actor adds no slot even after population shrink");
        Reset(30);bots=Population(10);for(int i=0;i<3;i++)Select(bots[i]);
        bots[0].GetPlayer.HealthController.IsAlive=false;
        Check(!Admit(bots[3]),"friendly death also shrinks denominator: nine alive permits only two existing friendlies");
        Check(Admit(Spawn()),"new spawn restores denominator and the freed friendly slot");
        foreach(int population in new[]{1,2,3,10}){
            Reset(15);bots=Population(population,WildSpawnType.pmcUSEC);
            int successes=0;foreach(var bot in bots)if(Admit(bot)){Select(bot);successes++;}
            Check(successes==Math.Min(2,population),"solo PMC minimum reserves two opportunities, bounded by living count");
        }
        Reset(20);bots=Population(10);var pmcs=Population(10,WildSpawnType.pmcBEAR);
        Select(bots[0]);Select(bots[1]);Select(pmcs[0]);Select(pmcs[1]);
        Check(!Admit(bots[2])&&!Admit(pmcs[2]),"Scav and solo-PMC pools enforce independent caps");
        var usec=Spawn(WildSpawnType.pmcUSEC);
        Check(!Admit(usec),"USEC and BEAR share one solo-PMC cap");
        Reset(30);bots=Population(10);Select(bots[0]);Select(bots[1]);
        var group=new BotsGroup();group.Members.Add(bots[2]);group.Members.Add(bots[3]);
        Check(!pitTeam.Modules.FriendlyEncounterPopulation.CanAdmitGroup(group,out _,out _,out _),"calming a two-Scav group cannot overshoot one available slot");
        Select(bots[2]);group.Members.Clear();group.Members.Add(bots[2]);group.Members.Add(bots[1]);
        Check(pitTeam.Modules.FriendlyEncounterPopulation.CanAdmitGroup(group,out _,out _,out _),"calming already-selected members adds no duplicate slots");
        pitTeam.Modules.AllegianceScavRelations.Calmed.Add(bots[0].BotsGroup);
        Check(!pitTeam.Modules.FriendlyEncounterPopulation.CanAdmit(bots[4],out alive,out friendly,out limit)&&friendly==3,"selected plus calmed actor counts once");
        Reset(50);bots=Population(4);var sniper=Spawn(WildSpawnType.marksman);pitTeam.Modules.AllegianceScavRelations.Calmed.Add(sniper.BotsGroup);Select(bots[0]);
        Check(!pitTeam.Modules.FriendlyEncounterPopulation.CanAdmit(bots[1],out alive,out friendly,out limit)&&alive==5&&friendly==2&&limit==2,"calmed sniper Scav consumes ordinary Scav budget");
        Reset(50);bots=Population(4);Select(bots[0]);
        foreach(var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye,WildSpawnType.bossSanitar})Spawn(role);
        var recruit=Spawn();Select(recruit);pitTeam.Modules.BossPlayers.Followers.Add(recruit);
        Check(pitTeam.Modules.FriendlyEncounterPopulation.CanAdmit(bots[1],out alive,out friendly,out limit)&&alive==4&&friendly==1&&limit==2,"bosses and recruited followers affect neither ambient numerator nor denominator");
        Check(!Admit(Spawn(WildSpawnType.bossKnight)),"boss role cannot directly enter encounter budget");
        Singleton<GameWorld>.Instance.AllAlivePlayersList.Add(bots[0].GetPlayer);
        pitTeam.Modules.FriendlyEncounterPopulation.CanAdmit(bots[1],out alive,out _,out _);
        Check(alive==4,"duplicate alive-list aliases cannot inflate population");
        Reset(50);pmcs=Population(8,WildSpawnType.pmcUSEC);pmcs[6].Grouped=pmcs[7].Grouped=true;
        pitTeam.Modules.FriendlyEncounterPopulation.CanAdmit(pmcs[0],out alive,out _,out limit);
        Check(alive==6&&limit==3,"grouped PMCs are excluded from solo-PMC denominator");
        Reset(50);bots=Population(4);newcomer=new BotOwner();newcomer.Profile.Info.Side=EPlayerSide.Savage;
        Check(pitTeam.Modules.FriendlyEncounterPopulation.CanAdmit(newcomer,out alive,out _,out _)&&alive==5,"activation before world publication includes candidate once");
        Singleton<GameWorld>.Instance=null;
        Check(!Admit(newcomer),"missing world fails closed");
        return checks;
    }
}
'@
Add-Type -TypeDefinition ($fixture+($sources -join "`n")) -Language CSharp
$count=[RuntimePopulationChecks]::Run()
"PASS: $count production live population/admission/death/spawn/calming checks; controlled EFT world and relationships."
