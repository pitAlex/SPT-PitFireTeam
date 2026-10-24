using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.SAINAddon;
using NativeEnemy = SAIN.SAINComponent.Classes.EnemyClasses.Enemy;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.Modules {
    // The native adapter consumes Core's finite retention service; its implementation
    // is not part of this native selector fixture.
    public static class FollowerContactEnemyRetention {
        public static BotOwner Owner;public static Player Target;public static float Until;public static bool Prioritized;
        public static bool TryGetActiveRetainedEnemy(BotOwner owner,out Player enemy,out bool prioritized) {
            enemy=Target;prioritized=Prioritized;
            return owner==Owner&&Target?.HealthController.IsAlive==true&&Time.time<=Until;
        }
    }
}

namespace EFT {
    public enum EBotEnemyCause { checkAddTODO, addPlayer }
    public partial class Player { public bool FriendlyScav=true, ProtectedContactTarget; }
    public partial class BotOwner { public BotsGroup BotsGroup=new BotsGroup(); }
}
public class BotsGroup {
    public readonly List<Player> Allies=new List<Player>();
    public readonly Dictionary<Player,BotGroupEnemyInfo> Neutrals=new Dictionary<Player,BotGroupEnemyInfo>();
    public readonly Dictionary<Player,BotGroupEnemyInfo> Enemies=new Dictionary<Player,BotGroupEnemyInfo>();
    public bool RejectEnemy;public int Adds;
    public bool IsEnemy(Player p)=>Enemies.ContainsKey(p);
    public bool IsPlayerEnemy(Player p)=>Enemies.ContainsKey(p);
    public bool AddEnemy(Player p,EBotEnemyCause cause) {
        Adds++;
        if(RejectEnemy || p.FriendlyScav && cause==EBotEnemyCause.checkAddTODO)return false;
        // EFT removes ally/neutral state only when inserting a new enemy entry.
        if(!Enemies.ContainsKey(p)){Enemies.Add(p,new BotGroupEnemyInfo());Allies.Remove(p);Neutrals.Remove(p);}
        return true;
    }
}
namespace pitTeam.Utils {
    public static class Enemy {
        public static EBotEnemyCause LastCause;public static bool SharedSeen;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static EnemyInfo MakeEnemy(BotOwner b,Player p,EBotEnemyCause cause=EBotEnemyCause.checkAddTODO,bool countSharedSeenAsPersonal=true) {
            LastCause=cause;SharedSeen=countSharedSeenAsPersonal;
            if(p==null || !p.HealthController.IsAlive || p.ProtectedContactTarget || b==null)return null;
            if(p.FriendlyScav && cause==EBotEnemyCause.checkAddTODO)return null;
            b.BotsGroup.AddEnemy(p,cause);
            // Core can still create a personal record if group validation refuses.
            return new EnemyInfo{ProfileId=p.ProfileId,Person=p};
        }
    }
}
namespace pitTeam.Components {
    public partial class pitAIBossPlayer {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RegisterContactEnemyForFollower(BotOwner follower,Player enemy,bool prioritizeAsGoal,bool allowGoalPromotion,bool playerVisualContact) {
            var info=pitTeam.Utils.Enemy.MakeEnemy(follower,enemy,EBotEnemyCause.checkAddTODO,false);
            if(info!=null && allowGoalPromotion){follower.Memory.GoalEnemy=info;follower.Memory.HaveEnemy=true;}
            // Model the existing Contact sync and SAIN's subsequent ally removal.
            if(info!=null){follower.Sain.GoalEnemy=new NativeEnemy{EnemyPlayer=enemy};follower.Sain.EnemyController.KnownEnemies.Add(follower.Sain.GoalEnemy);}
        }
        public void ContactFixture(BotOwner b,Player p,bool allow=true,bool prioritize=true)=>RegisterContactEnemyForFollower(b,p,prioritize,allow,true);
    }
}
public static partial class CombatChecks {
    private static void NativeContactUpdate(BotOwner b) {
        var enemy=b.Sain.GoalEnemy;
        if(enemy!=null && b.BotsGroup.Allies.Contains(enemy.EnemyPlayer)) {
            b.Sain.EnemyController.KnownEnemies.Remove(enemy);b.Sain.GoalEnemy=null;
        }
    }
    private static void TestContactOverride() {
        TestContactSelection();
        var harmony=new Harmony("xyz.pit.fireteam.sainaddon");SainContactEnemyBridge.Apply(harmony);
        var command=new pitAIBossPlayer();var b=SupportBot("contactOverride");
        var scav=new Player{ProfileId="friendlyScav"};var untouched=new Player{ProfileId="otherFriendly"};
        b.BotsGroup.Allies.Add(scav);b.BotsGroup.Neutrals.Add(scav,new BotGroupEnemyInfo());b.BotsGroup.Allies.Add(untouched);
        var prior=b.Memory.GoalEnemy;
        Check(pitTeam.Utils.Enemy.MakeEnemy(b,scav)==null,"ambient friendly Scav acquisition stays blocked");
        Check(b.Memory.GoalEnemy==prior&&b.BotsGroup.Allies.Contains(scav),"ambient acquisition does not alter relationship or goal");
        command.ContactFixture(b,scav);
        Check(pitTeam.Utils.Enemy.LastCause==EBotEnemyCause.addPlayer&&!pitTeam.Utils.Enemy.SharedSeen,"accepted addon Contact uses explicit cause without granting personal sight");
        Check(b.BotsGroup.Enemies.ContainsKey(scav)&&!b.BotsGroup.Allies.Contains(scav)&&!b.BotsGroup.Neutrals.ContainsKey(scav),"Contact establishes consistent native group hostility");
        Check(b.Memory.GoalEnemy.Person==scav&&b.Sain.GoalEnemy.EnemyPlayer==scav,"Contact synchronizes exact commanded target in both brains");
        NativeContactUpdate(b);
        Check(b.Sain.GoalEnemy!=null&&SAINFollowerCombatHandoff.HasLiveEnemy(b.Sain),"native ally cleanup no longer drops accepted Contact");
        Check(b.BotsGroup.Allies.Contains(untouched),"Contact preserves other friendly relationships");
        b.BotsGroup.Allies.Add(scav);b.BotsGroup.Neutrals.Add(scav,new BotGroupEnemyInfo());command.ContactFixture(b,scav);NativeContactUpdate(b);
        Check(b.Sain.GoalEnemy!=null&&!b.BotsGroup.Allies.Contains(scav)&&!b.BotsGroup.Neutrals.ContainsKey(scav),"existing enemy plus stale ally membership is reconciled");
        Check(b.BotsGroup.Enemies.Count==1,"repeated Contact does not duplicate group enemies");
        var infoOnly=new Player{ProfileId="infoOnly"};b.BotsGroup.Allies.Add(infoOnly);command.ContactFixture(b,infoOnly,false);
        Check(!b.BotsGroup.Enemies.ContainsKey(infoOnly)&&b.BotsGroup.Allies.Contains(infoOnly),"non-promoting reports do not turn friendlies hostile");
        command.ContactFixture(b,infoOnly,true,false);
        Check(pitTeam.Utils.Enemy.LastCause==EBotEnemyCause.checkAddTODO&&!b.BotsGroup.Enemies.ContainsKey(infoOnly)&&b.BotsGroup.Allies.Contains(infoOnly),"automatic squad report cannot override friendly Scav protection");
        var protectedTarget=new Player{ProfileId="protected",ProtectedContactTarget=true};b.BotsGroup.Allies.Add(protectedTarget);command.ContactFixture(b,protectedTarget);
        Check(!b.BotsGroup.Enemies.ContainsKey(protectedTarget)&&b.BotsGroup.Allies.Contains(protectedTarget),"Core protected-target exclusions remain authoritative");
        var rejected=new Player{ProfileId="rejected"};b.BotsGroup.Allies.Add(rejected);b.BotsGroup.RejectEnemy=true;command.ContactFixture(b,rejected);
        Check(!b.BotsGroup.Enemies.ContainsKey(rejected)&&b.BotsGroup.Allies.Contains(rejected),"native group rejection does not remove ally protections");b.BotsGroup.RejectEnemy=false;
        var core=Spawn("contactCore",FollowerCombatTactic.Balanced);command.ContactFixture(core,scav);
        Check(pitTeam.Utils.Enemy.LastCause==EBotEnemyCause.checkAddTODO&&core.BotsGroup.Enemies.Count==0,"Core tactic keeps original Contact path");
        var unready=Spawn("contactUnready");command.ContactFixture(unready,scav);
        Check(pitTeam.Utils.Enemy.LastCause==EBotEnemyCause.checkAddTODO&&unready.BotsGroup.Enemies.Count==0,"unready addon keeps Core fallback");
        b.IsFollower=false;command.ContactFixture(b,scav);Check(pitTeam.Utils.Enemy.LastCause==EBotEnemyCause.checkAddTODO,"ordinary SAIN bots are not overridden");b.IsFollower=true;
        b=ShooterBot("contactShooter");command.ContactFixture(b,scav);
        Check(pitTeam.Utils.Enemy.LastCause==EBotEnemyCause.addPlayer,"SAINShooter honours the same Contact override");
        harmony.Unpatch(AccessTools.Method(typeof(pitAIBossPlayer),"RegisterContactEnemyForFollower"),HarmonyPatchType.All,harmony.Id);
        command.ContactFixture(b,scav);Check(pitTeam.Utils.Enemy.LastCause==EBotEnemyCause.checkAddTODO,"addon removal restores original Contact call");
        SainContactEnemyBridge.Apply(harmony);
    }

    private static void TestContactSelection() {
        var b=SupportBot("retainedContact");var contact=b.Sain.GoalEnemy;
        b.Memory.GoalEnemy=contact.EnemyInfo;b.Memory.HaveEnemy=true;
        contact.IsVisible=contact.CanShoot=false;contact.TimeSinceSeen=999;
        contact.KnownPlaces.LastKnownPosition=new Vector3(31,1,36);
        contact.KnownPlaces.TimeLastKnownUpdated=Time.time;
        var heard=new NativeEnemy();heard.EnemyPlayer.ProfileId="olderNearbyContact";
        heard.IsVisible=heard.CanShoot=false;heard.KnownPlaces.LastKnownPosition=new Vector3(9,0,41);
        b.Sain.EnemyController.KnownEnemies.Add(heard);
        FollowerContactEnemyRetention.Owner=b;FollowerContactEnemyRetention.Target=contact.EnemyPlayer;
        FollowerContactEnemyRetention.Prioritized=true;FollowerContactEnemyRetention.Until=Time.time+10;
        var selector=new SAIN.SAINComponent.Classes.EnemyClasses.SAINEnemyController(b.Sain);
        float observed=contact.KnownPlaces.TimeLastKnownUpdated;
        Check(selector.ChooseForTest(heard)==contact,"prioritized Contact wins over a nearer hidden native candidate before publication");
        Check(selector.NativeCalls==1&&selector.Publications==1,"Contact preference preserves one native selection and publication");
        b.Sain.GoalEnemy=selector.ChooseForTest(heard);
        var markers=new MarkerHarness(b);markers.Update();
        Check(markers.Contact(contact.EnemyProfileId)?.WorldPosition.x==31&&markers.Contact(heard.EnemyProfileId)==null,"Contact marker stays at the reported target instead of jumping to nearby hidden enemy");
        Check(contact.KnownPlaces.TimeLastKnownUpdated==observed&&contact.TimeSinceSeen==999&&!contact.IsVisible&&!contact.CanShoot,"selection does not refresh knowledge or grant sight and firing permission");
        heard.IsVisible=true;Check(selector.ChooseForTest(heard)==heard,"visible threat interrupts Contact preference");heard.IsVisible=false;
        heard.CanShoot=true;Check(selector.ChooseForTest(heard)==heard,"shootable threat interrupts Contact preference");heard.CanShoot=false;
        b.Sain.Decision.DogFightDecision.DogFightActive=true;Check(selector.ChooseForTest(heard)==heard,"dogfight interrupts Contact preference");b.Sain.Decision.DogFightDecision.DogFightActive=false;
        b.Sain.Medical.TimeSinceShot=.5f;Check(selector.ChooseForTest(heard)==heard,"recent damage interrupts Contact preference");b.Sain.Medical.TimeSinceShot=999;
        b.Memory.IsUnderFire=true;Check(selector.ChooseForTest(heard)==heard,"incoming fire interrupts Contact preference");b.Memory.IsUnderFire=false;
        b.UsingMedical=true;Check(selector.ChooseForTest(heard)==heard,"active medicine interrupts Contact preference");b.UsingMedical=false;
        FollowerContactEnemyRetention.Prioritized=false;Check(selector.ChooseForTest(heard)==heard,"ordinary reports cannot pin native selection");FollowerContactEnemyRetention.Prioritized=true;
        contact.EnemyKnown=false;Check(selector.ChooseForTest(heard)==heard,"Contact preference cannot revive forgotten native knowledge");contact.EnemyKnown=true;
        contact.Valid=false;Check(selector.ChooseForTest(heard)==heard,"invalid native contact cannot be selected");contact.Valid=true;
        var known=contact.KnownPlaces.LastKnownPosition;contact.KnownPlaces.LastKnownPosition=null;Check(selector.ChooseForTest(heard)==heard,"Contact preference cannot manufacture a known position");contact.KnownPlaces.LastKnownPosition=known;
        FollowerEnemyTracking.Eligible=false;Check(selector.ChooseForTest(heard)==heard,"expired Core knowledge cannot authorize preference");FollowerEnemyTracking.Eligible=true;
        b.Memory.GoalEnemy=new EnemyInfo{ProfileId=contact.EnemyProfileId,Person=contact.EnemyPlayer};Check(selector.ChooseForTest(heard)==heard,"stale native record with matching id cannot replace the accepted record");b.Memory.GoalEnemy=contact.EnemyInfo;
        b.Memory.GoalEnemy=null;Check(selector.ChooseForTest(heard)==heard,"Contact retention cannot bypass accepted-goal admission");b.Memory.GoalEnemy=contact.EnemyInfo;
        contact.EnemyPlayer.HealthController.IsAlive=false;Check(selector.ChooseForTest(heard)==heard,"dead Contact is not retained");contact.EnemyPlayer.HealthController.IsAlive=true;
        FollowerContactEnemyRetention.Until=Time.time-.01f;Check(selector.ChooseForTest(heard)==heard,"expired priority restores ordinary native selection");
        FollowerContactEnemyRetention.Until=Time.time+10;b.IsFollower=false;Check(selector.ChooseForTest(heard)==heard,"ordinary bots retain native selection");b.IsFollower=true;
        b.Follower.CombatTactic=FollowerCombatTactic.SAINShooter;Tick();Check(selector.ChooseForTest(heard)==contact,"SAINShooter uses the same prioritized Contact selection");
        FollowerContactEnemyRetention.Owner=null;FollowerContactEnemyRetention.Target=null;
    }
}
