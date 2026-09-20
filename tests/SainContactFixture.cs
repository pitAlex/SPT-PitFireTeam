using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.SAINAddon;
using NativeEnemy = SAIN.SAINComponent.Classes.EnemyClasses.Enemy;

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
}
