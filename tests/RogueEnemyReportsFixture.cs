using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using pitTeam.GoingRogue;
using pitTeam.Modules;

namespace Comfort.Common {public static class Singleton<T> {public static T Instance;}}
namespace EFT {
    public struct Position {public float X;public float sqrMagnitude=>X*X;public static Position operator -(Position a,Position b)=>new(){X=a.X-b.X};}
    public enum WildSpawnType {bossKnight,followerBigPipe,followerBirdEye,exUsec,assault}
    public enum EBotState {Active,Inactive}
    public enum EBotEnemyCause {addCauseGroup}
    public class Health {public bool IsAlive=true;}
    public class Settings {public WildSpawnType Role;}
    public class Info {public Settings Settings=new();}
    public class Profile {public Info Info=new();}
    public class AIData {public BotOwner BotOwner;}
    public class Player {public string ProfileId;public Profile Profile=new();public AIData AIData=new();public Health HealthController=new();public Position Position;}
    public class EnemyInfo {public Player Person;}
    public class Memory {public EnemyInfo GoalEnemy;}
    public class BotOwner {public bool IsDead;public EBotState BotState=EBotState.Active;public Player GetPlayer;public Profile Profile=>GetPlayer.Profile;public Position Position=>GetPlayer.Position;public Memory Memory=new();public BotsGroup BotsGroup=new();}
    public class BotsGroup {
        public HashSet<Player> Enemies=new();public int Adds;public bool Reject;public EBotEnemyCause Cause;
        public bool IsEnemy(Player player)=>Enemies.Contains(player);
        public bool AddEnemy(Player player,EBotEnemyCause cause){Adds++;Cause=cause;if(Reject)return false;Enemies.Add(player);return true;}
    }
    public class GameWorld {public Player MainPlayer;public List<Player> AllAlivePlayersList=new();}
}
namespace pitTeam.Modules {
    public enum GameplayMode {Allegiance,GunsForHire}
    public static class GameplayModeAddons {public static GameplayMode Current=GameplayMode.Allegiance;}
    public class Follower {public BotOwner Bot;public BotOwner GetBot()=>Bot;}
    public static class BossPlayers {
        public static List<Follower> Followers=new();public static List<Follower> GetFollowersByBoss(string id)=>Followers;
        public static bool IsFollower(BotOwner bot)=>Followers.Any(f=>f.Bot==bot);
    }
}
namespace pitTeam.GoingRogue {internal static class RogueAlliance {internal static bool Friendly=true;internal static bool? Friendship(string role)=>Friendly;}}
internal static class Checks {
    private static int count;private static GameWorld world;private static Player enemy;private static BotOwner goon;
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
    private static BotOwner Actor(string id,WildSpawnType role,float distance,bool owned=false){
        var player=new Player{ProfileId=id,Position=new(){X=distance}};player.Profile.Info.Settings.Role=role;
        var bot=new BotOwner{GetPlayer=player};player.AIData.BotOwner=bot;world.AllAlivePlayersList.Add(player);
        if(owned)BossPlayers.Followers.Add(new(){Bot=bot});return bot;
    }
    private static void Setup(WildSpawnType role=WildSpawnType.bossKnight){
        BossPlayers.Followers.Clear();GameplayModeAddons.Current=GameplayMode.Allegiance;RogueAlliance.Friendly=true;
        world=new(){MainPlayer=new(){ProfileId="human"}};Comfort.Common.Singleton<GameWorld>.Instance=world;
        goon=Actor("goon",role,0,true);enemy=Actor("enemy",WildSpawnType.assault,90).GetPlayer;
        goon.BotsGroup.Enemies.Add(enemy);goon.Memory.GoalEnemy=new(){Person=enemy};
    }
    public static void Main(){
        foreach(var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye}){
            Setup(role);var nearby=Actor("nearby",WildSpawnType.exUsec,30);var member=Actor("member",WildSpawnType.exUsec,40);member.BotsGroup=nearby.BotsGroup;
            var edge=Actor("edge",WildSpawnType.exUsec,150);var distant=Actor("distant",WildSpawnType.exUsec,150.1f);
            var hostile=Actor("hostile",WildSpawnType.exUsec,10);hostile.BotsGroup.Enemies.Add(world.MainPlayer);
            var owned=Actor("owned",WildSpawnType.exUsec,10,true);var scav=Actor("scav",WildSpawnType.assault,10);
            var dead=Actor("dead",WildSpawnType.exUsec,10);dead.IsDead=true;var inactive=Actor("inactive",WildSpawnType.exUsec,10);inactive.BotState=EBotState.Inactive;
            RogueEnemyReports.Update();
            Check(nearby.BotsGroup.Adds==1&&edge.BotsGroup.Adds==1,"nearby Rogue groups added once, including 150m boundary: "+role);
            Check(nearby.BotsGroup.IsEnemy(enemy)&&nearby.BotsGroup.Cause==EBotEnemyCause.addCauseGroup,"native group enemy addition uses addCauseGroup");
            Check(new[]{distant,hostile,owned,scav,dead,inactive}.All(bot=>bot.BotsGroup.Adds==0),"range, user friendship, native role and owned-follower filters");
            RogueEnemyReports.Update();Check(nearby.BotsGroup.Adds==1,"known enemy is not repeatedly re-added");
        }
        foreach(string gate in new[]{"mode","alliance","dead-goon","inactive-goon","no-enemy","dead-enemy","not-hostile"}){
            Setup();var recipient=Actor("recipient",WildSpawnType.exUsec,30);
            switch(gate){case "mode":GameplayModeAddons.Current=GameplayMode.GunsForHire;break;case "alliance":RogueAlliance.Friendly=false;break;
                case "dead-goon":goon.IsDead=true;break;case "inactive-goon":goon.BotState=EBotState.Inactive;break;case "no-enemy":goon.Memory.GoalEnemy=null;break;
                case "dead-enemy":enemy.HealthController.IsAlive=false;break;case "not-hostile":goon.BotsGroup.Enemies.Clear();break;}
            RogueEnemyReports.Update();Check(recipient.BotsGroup.Adds==0,"report eligibility: "+gate);
        }
        Setup();var rejected=Actor("rejected",WildSpawnType.exUsec,30);rejected.BotsGroup.Reject=true;RogueEnemyReports.Update();
        Check(rejected.BotsGroup.Adds==1&&!rejected.BotsGroup.IsEnemy(enemy),"native rejection is respected without fallback");
        Console.WriteLine("PASS: "+count+" native Rogue group-add/range/user-friendship checks. Combat response remains native.");
    }
}
