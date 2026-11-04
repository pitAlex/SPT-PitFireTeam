using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using pitTeam.Components;
using pitTeam.Modules;
using UnityEngine;

namespace UnityEngine {public struct Vector3 {public float X;public static float Distance(Vector3 a,Vector3 b)=>Math.Abs(a.X-b.X);}}
namespace EFT.HealthSystem {public class HealthEffects {}}
namespace EFT.Ballistics {public class DamageInfo {public object Weapon;}}
namespace EFT {
    public enum EPlayerSide {Usec,Bear,Savage}
    public enum EBodyPart {Head,Chest}
    public enum WildSpawnType {pmcUSEC,pmcBEAR,exUsec,bossKnight,followerBigPipe,followerBirdEye,assault}
    public static class EnumText {public static string ToStringNoBox<T>(this T value)=>value.ToString();}
    public class Settings {public WildSpawnType Role;}
    public class Info {public Settings Settings=new();}
    public class Profile {public string Id;public Info Info=new();}
    public class Inventory {public List<string> EquippedInSlotsTemplateIds=new();}
    public class Health {public HealthEffects BodyPartEffects=new();public string[] Buffs=new string[0];public string[] ActiveBuffsNames()=>Buffs;}
    public class Player {public Profile Profile=new();public string ProfileId=>Profile.Id;public Vector3 Position;public EPlayerSide Side;public string Location="woods";public int CurrentHour=15;public Inventory Inventory=new();public Health HealthController=new();public List<string> TriggerZones=new();public Quests.QuestController QuestController=new();}
}
namespace EFT.Quests {
    public class Call {public string Target,Victim,Location,Role;public List<string> Equipment,Zones;public object Weapon;public EBodyPart Part;public float Distance;public int Hour;public HealthEffects VictimEffects,FollowerEffects;public string[] Buffs;public Profile Killer;public bool Forwarding;}
    public class QuestController {
        public Profile Profile;public List<Call> Calls=new();public int Credits;public bool Throw;
        public Func<Call,bool> Matches=call=>call.Target=="AnyPmc";
        public void CheckKillConditionCounter(string target,string victim,List<string> equipment,object weapon,EBodyPart part,string location,float distance,string role,int hour,HealthEffects victimEffects,HealthEffects followerEffects,List<string> zones,string[] buffs){
            var call=new Call{Target=target,Victim=victim,Equipment=equipment,Weapon=weapon,Part=part,Location=location,Distance=distance,Role=role,Hour=hour,VictimEffects=victimEffects,FollowerEffects=followerEffects,Zones=zones,Buffs=buffs,Killer=Profile,Forwarding=Fixture.IsForwarding};
            Calls.Add(call);if(Throw)throw new InvalidOperationException("native quest failure");if(Matches(call))Credits++;
        }
    }
}
namespace pitTeam.Components {
    public class pitAIBossPlayer {public Player realPlayer;public Vector3 Position=>realPlayer.Position;}
    public class BotFollowerPlayer {
        __ROSTER_PROPERTY__
        public bool IsSquadMate;public bool ReportsRaidKills;public pitAIBossPlayer Boss;
        public BotFollowerPlayer(bool roster,bool squad){IsRosterMember=roster;IsSquadMate=squad;ReportsRaidKills=roster;}
        public pitAIBossPlayer GetBoss()=>Boss;
    }
}
namespace pitTeam.Modules {
    public static class Logger {public static int Errors;public static void LogError(object message){Errors++;}}
    public static class SquadRaidKillReport {public static int Reports;public static void RecordTeammateKill(Player killer,Player victim){Reports++;}}
}
internal static class Fixture {
    [ThreadStatic] private static bool forwardingRosterKill;
    public static bool IsForwarding=>forwardingRosterKill;
    private static int checks,xp;
    private static FieldInfo GetQuestControllerProfileField()=>typeof(EFT.Quests.QuestController).GetField("Profile");
    __TryCreditFollowerKillQuestProgress__
    __BuildQuestKillTargets__
    private static void Dispatch(BotFollowerPlayer follower,Player followerPlayer,Player __instance,EFT.Ballistics.DamageInfo damageInfo,EBodyPart bodyPart){
        __DISPATCH__
        xp++;
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    public static void Main(){
        foreach(var role in new[]{WildSpawnType.pmcUSEC,WildSpawnType.exUsec,WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye})
        foreach(bool roster in new[]{false,true})
        foreach(float range in new[]{0f,79.99f,80f,80.01f}){
            var human=new Player{Profile=new Profile{Id="human"}};human.QuestController.Profile=human.Profile;
            var killer=new Player{Profile=new Profile{Id="killer"},Position=new Vector3{X=range}};killer.Profile.Info.Settings.Role=role;
            var victim=new Player{Profile=new Profile{Id="victim"},Side=EPlayerSide.Bear,Position=new Vector3{X=range+50}};victim.Profile.Info.Settings.Role=WildSpawnType.pmcBEAR;
            bool squad=roster&&(role==WildSpawnType.pmcUSEC||role==WildSpawnType.exUsec);
            var follower=new BotFollowerPlayer(roster,squad){Boss=new pitAIBossPlayer{realPlayer=human}};
            if(role==WildSpawnType.bossKnight||role==WildSpawnType.followerBigPipe||role==WildSpawnType.followerBirdEye)follower.ReportsRaidKills=true;
            var weapon=new object();var damage=new EFT.Ballistics.DamageInfo{Weapon=weapon};
            killer.HealthController.Buffs=new[]{"buff"};victim.TriggerZones.Add("zone");victim.Inventory.EquippedInSlotsTemplateIds.Add("armor");
            xp=0;Dispatch(follower,killer,victim,damage,EBodyPart.Head);
            var controller=human.QuestController;bool eligible=roster&&range<=80;
            Check(controller.Credits==(eligible?1:0),"native matching quest credited only for deployed roster member within 80m: "+role+"/"+roster+"/"+range);
            Check(xp==(squad?1:0),"boss XP/squadmate gate remains independent from new quest-credit eligibility");
            Check(controller.Profile==human.Profile&&!IsForwarding,"quest owner and forwarding scope restored");
            if(!eligible){Check(controller.Calls.Count==0,"nonqualifying origin/distance never invokes native quest checks");continue;}
            Check(controller.Calls.Count==3,"one native check per Any/Bear/AnyPmc target alias");
            foreach(var call in controller.Calls){
                Check(call.Killer==killer.Profile&&call.Forwarding,"native checks see actual killer profile inside scoped forwarding");
                Check(call.Victim==victim.ProfileId&&call.Weapon==weapon&&call.Part==EBodyPart.Head&&Math.Abs(call.Distance-50)<0.0001f&&call.Location==killer.Location&&call.Role=="pmcBEAR"&&call.Hour==victim.CurrentHour,"native kill identity, weapon, body part, map, distance, role and time preserved");
                Check(call.Equipment==victim.Inventory.EquippedInSlotsTemplateIds&&call.Zones==victim.TriggerZones&&call.VictimEffects==victim.HealthController.BodyPartEffects&&call.FollowerEffects==killer.HealthController.BodyPartEffects&&call.Buffs==killer.HealthController.Buffs,"native equipment, zone, effect and buff inputs preserved");
            }
            controller.Calls.Clear();controller.Credits=0;controller.Matches=call=>false;
            Dispatch(follower,killer,victim,damage,EBodyPart.Chest);
            Check(controller.Credits==0&&controller.Calls.Count==3,"failed native requirements are not bypassed");
            controller.Throw=true;Logger.Errors=0;Dispatch(follower,killer,victim,damage,EBodyPart.Head);
            Check(Logger.Errors==1&&controller.Profile==human.Profile&&!IsForwarding,"native counter exception restores owner/scope without aborting death processing");
            forwardingRosterKill=true;Dispatch(follower,killer,victim,damage,EBodyPart.Head);
            Check(IsForwarding&&controller.Profile==human.Profile,"nested forwarding preserves prior scope");forwardingRosterKill=false;
        }
        Check(BuildQuestKillTargets(EPlayerSide.Usec).Contains("AnyPmc")&&BuildQuestKillTargets(EPlayerSide.Savage).Contains("Bot"),"existing victim-side aliases retained");
        Console.WriteLine("PASS: "+checks+" production roster quest dispatch/80m/native-input/restoration checks. Controlled quest controller; no live EFT quest progress.");
    }
}
