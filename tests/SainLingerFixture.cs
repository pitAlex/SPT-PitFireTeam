using EFT;
using pitTeam;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace UnityEngine {
    public partial struct Vector3 {public static Vector3 forward=>new Vector3(0,0,1);}
    public static class Random {public static float value=.25f;}
    public struct Quaternion {
        private float yaw;
        public static Quaternion Euler(float x,float y,float z)=>new Quaternion{yaw=y*(float)System.Math.PI/180f};
        public static Vector3 operator *(Quaternion q,Vector3 v)=>new Vector3(
            v.x*(float)System.Math.Cos(q.yaw)+v.z*(float)System.Math.Sin(q.yaw),v.y,
            v.z*(float)System.Math.Cos(q.yaw)-v.x*(float)System.Math.Sin(q.yaw));
    }
}
public static partial class CombatChecks {
    private static void TestLinger(){
        var bot=Spawn("linger");
        var solo=new SAINFollowerSoloCombatLayer(bot,74);
        var squad=new SAINFollowerSquadCombatLayer(bot,75);Tick();
        Check(!solo.IsActive()&&!squad.IsActive(),"peaceful spawn does not arm linger");
        var enemy=new Enemy();bot.Sain.GoalEnemy=enemy;
        bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.RushEnemy;
        Check(solo.IsActive()&&solo.GetNextAction().Type.Name=="RushEnemyAction","linger fixture starts native rush");
        enemy.EnemyPlayer.HealthController.IsAlive=false;
        // Keep the dead object active and the old decision unchanged: death must win.
        Check(solo.IsCurrentActionEnding(),"dead enemy ends previous action before a native decision update");
        Check(solo.IsActive()&&solo.GetNextAction().Type==typeof(SAINFollowerLingerAction),"solo enters dedicated linger action");
        Check(!squad.IsActive(),"squad yields to solo-owned linger");
        Check(bot.Sain.Decision.CurrentCombatDecision==ECombatDecision.None,"dead combat decision reset through native publisher");
        SainAddonBridge.TryIsReadyForPatrolAfterCombat(bot,out bool ready);
        Check(!ready,"patrol waits for linger completion");
        var action=new SAINFollowerLingerAction(bot);int stops=bot.Sain.Mover.Stops;int ends=bot.Sain.Shoot.Ends;
        bot.LookDirection=new Vector3(0,-.8f,1);action.Start();action.Update(null);action.OnSteeringTicked();
        Check(bot.Sain.Mover.Stops==stops+1&&bot.Sain.Shoot.Ends>ends,"linger cancels previous movement and firing");
        Check(bot.Sain.Steering.LookPoint.y==bot.Sain.Transform.WeaponRoot.y,"entry linger look is horizontal");
        Time.time+=.6f;action.OnSteeringTicked();
        Check(bot.Sain.Steering.LookPoint.x<0,"linger makes one lateral scan");
        solo.NativeDecisionChanged=true;
        Check(!solo.IsCurrentActionEnding(),"native decision events cannot restart linger");
        Time.time+=2.4f;
        Check(!solo.IsActive()&&!squad.IsActive(),"linger expires after three seconds across both layers");
        Check(bot.RecoveryActive && bot.RecoveryStarts==1,"completed addon linger starts core full recovery once");
        solo.IsActive();squad.IsActive();Check(bot.RecoveryStarts==1,"repeated released-layer polls do not restart recovery");
        SainAddonBridge.TryIsReadyForPatrolAfterCombat(bot,out ready);Check(ready,"completed linger releases patrol");
        bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.Search;
        Check(!solo.IsActive()&&solo.GetNextAction().Type==typeof(SAINFollowerLingerAction),"stale post-expiry decision cannot resume native search");
        Check(!solo.IsCurrentActionEnding(),"retained inactive layer remains quiet instead of restarting linger every frame");
        int looks=bot.Sain.Steering.Looks;ends=bot.Sain.Shoot.Ends;action.Stop();action.OnSteeringTicked();action.Update(null);
        Check(bot.Sain.Steering.Looks==looks&&bot.Sain.Shoot.Ends==ends,"stopped linger cannot steer or cancel successor firing");

        enemy=new Enemy();bot.Sain.GoalEnemy=enemy;bot.Sain.Decision.CurrentSquadDecision=ESquadDecision.Suppress;
        Check(squad.IsActive()&&squad.GetNextAction().Type.Name=="SuppressAction","new enemy re-arms combat in squad layer");
        Check(!bot.RecoveryActive,"renewed real combat cancels previous full recovery");
        enemy.EnemyPlayer.HealthController.IsAlive=false;
        Check(squad.IsCurrentActionEnding()&&!squad.IsActive(),"squad suppression ends on enemy death");
        Check(solo.IsActive()&&solo.GetNextAction().Type==typeof(SAINFollowerLingerAction),"squad kill transfers to the same linger action");
        Time.time+=1f;squad.Stop();solo.Stop();
        Check(solo.IsActive(),"layer switches preserve the shared handoff timer");
        solo.GetNextAction();action.Start();
        var nextEnemy=new Enemy();bot.Sain.GoalEnemy=nextEnemy;bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.RushEnemy;
        looks=bot.Sain.Steering.Looks;ends=bot.Sain.Shoot.Ends;action.OnSteeringTicked();action.Update(null);
        Check(bot.Sain.Steering.Looks==looks&&bot.Sain.Shoot.Ends==ends,"reacquired live enemy immediately stops linger presentation");
        Check(solo.IsCurrentActionEnding()&&solo.IsActive()&&solo.GetNextAction().Type.Name=="RushEnemyAction","live enemy interrupts linger into native combat");action.Stop();

        var replacement=new Enemy();bot.Sain.EnemyController.KnownEnemies.Add(replacement);
        nextEnemy.EnemyPlayer.HealthController.IsAlive=false;
        Check(solo.IsActive()&&solo.GetNextAction().Type.Name=="RushEnemyAction","another known live enemy prevents false post-combat handoff");
        Check(replacement.EnemyPlayer.HealthController.IsAlive,"handoff preserves living enemy memory");
        bot.Sain.EnemyController.KnownEnemies.Clear();
        bot.UsingMedical=true;bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.SeekCover;bot.Sain.Decision.CurrentSelfDecision=ESelfActionType.Surgery;
        int resets=bot.Sain.Decision.Resets;
        Check(solo.IsActive()&&solo.GetNextAction().Type!=typeof(SAINFollowerLingerAction)&&bot.Sain.Decision.Resets==resets,"active medicine is not interrupted by linger");
        bot.UsingMedical=false;bot.Sain.Decision.CurrentSelfDecision=ESelfActionType.None;
        Check(solo.IsCurrentActionEnding()&&solo.GetNextAction().Type==typeof(SAINFollowerLingerAction),"medical completion enters pending linger");
        bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.AvoidGrenade;
        Check(SAINFollowerRuntime.GetCombatPhase(bot)==SAINFollowerCombatPhase.Combat&&bot.Sain.Decision.CurrentCombatDecision==ECombatDecision.AvoidGrenade,"native grenade avoidance is never reset by handoff");
        bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.None;
        solo.IsActive();solo.GetNextAction();
        bot.Follower.Command=FollowerCommandType.RegroupNearBoss;Time.time+=3;
        Check(!solo.IsActive()&&bot.Follower.Command==FollowerCommandType.RegroupNearBoss,"linger completion preserves pending player commands");
        SainAddonBridge.TryForceReleaseFollowerCombatState(bot);
        Check(!solo.IsActive(),"explicit release clears handoff without rearming");
        bot.Sain.GoalEnemy=new Enemy();bot.Sain.Decision.CurrentCombatDecision=ECombatDecision.Search;solo.IsActive();solo.GetNextAction();
        bot.Sain.GoalEnemy=null;solo.IsActive();action.Start();
        bot.Follower.CombatTactic=FollowerCombatTactic.Balanced;Tick();
        looks=bot.Sain.Steering.Looks;action.OnSteeringTicked();
        Check(!solo.IsActive()&&bot.Sain.Steering.Looks==looks,"tactic opt-out cancels linger ownership");
        bot.Follower.CombatTactic=FollowerCombatTactic.SainMan;Tick();
        Check(!solo.IsActive(),"reselecting SainMan does not resurrect old linger");
        TestInvestigationGate();
        TestMedicalLingerDeadline();
    }
    private static void TestInvestigationGate(){
        var bot=RegroupBot("investigation",100);
        bot.Memory.GoalEnemy=null;
        var enemy=bot.Sain.GoalEnemy;enemy.Seen=false;enemy.Heard=true;
        bot.Sain.EnemyController.KnownEnemies.Add(enemy);
        var solo=new SAINFollowerSoloCombatLayer(bot,74);
        var squad=new SAINFollowerSquadCombatLayer(bot,75);Tick();
        bot.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(bot.Sain.Decision.CurrentCombatDecision==ECombatDecision.None&&!solo.IsActive()&&!squad.IsActive(),"heard-only investigation cannot publish combat or take over either layer without accepted goal");
        Check(!SainCombatRecorderBridge.IsActive(bot),"rejected investigation cannot open a recorder combat episode");
        Check(bot.Sain.GoalEnemy==enemy&&bot.Sain.EnemyController.KnownEnemies.Contains(enemy),"investigation gate preserves native perception and living enemy memory");
        bot.Follower.Command=FollowerCommandType.RegroupNearBoss;
        bot.Sain.Decision.Manager.Frame();
        Check(bot.Follower.Command==FollowerCommandType.RegroupNearBoss&&!SAINFollowerRuntime.GetRegroup(bot).Active,"rejected investigation leaves peaceful command for core request layer");
        bot.Follower.Command=FollowerCommandType.None;
        bot.Follower.CombatIndependent=true;bot.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(solo.IsActive()&&solo.GetNextAction().Type.Name=="SearchAction","On Your Own allows native investigation without EFT goal");
        bot.Follower.CombatIndependent=false;
        Check(solo.IsCurrentActionEnding()&&solo.GetNextAction().Type==typeof(SAINFollowerLingerAction),"leaving On Your Own ends active unaccepted investigation");
        Time.time+=3.1f;Check(!solo.IsActive(),"unaccepted living memory cannot trap follower in linger");
        bot.Memory.GoalEnemy=new EnemyInfo{Alive=false};bot.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(!solo.IsActive(),"dead EFT goal does not authorize investigation");
        bot.Memory.GoalEnemy.Alive=true;bot.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(solo.IsActive(),"accepted living EFT goal enables combat even while enemy is invisible");
        SainAddonBridge.TryForceReleaseFollowerCombatState(bot);
        Check(bot.RecoveryActive,"explicit combat release starts core recovery");
        bot.Memory.GoalEnemy=null;
        bot.Sain.Decision.Manager.Publish(ECombatDecision.AvoidGrenade);
        Check(SAINFollowerRuntime.GetCombatPhase(bot)==SAINFollowerCombatPhase.Combat&&bot.Sain.Decision.CurrentCombatDecision==ECombatDecision.AvoidGrenade,"no-goal gate preserves urgent grenade avoidance");
        foreach(var self in new[]{ESelfActionType.FirstAid,ESelfActionType.Surgery,ESelfActionType.Stims}){
            Check(!SAINFollowerCombatHandoff.AllowsDecision(bot.Sain,ECombatDecision.SeekCover,self),"released medical selection cannot reenter addon combat "+self);
        }
        bot.UsingMedical=true;Check(!SAINFollowerCombatHandoff.AllowsDecision(bot.Sain,ECombatDecision.SeekCover,ESelfActionType.None),"core-owned recovery medicine cannot reopen addon combat after release");
        bot.UsingMedical=false;
        Check(!SAINFollowerCombatHandoff.AllowsDecision(bot.Sain,ECombatDecision.SeekCover,ESelfActionType.Reload),"reload alone cannot authorize investigative cover movement");
        SainAddonBridge.TryForceReleaseFollowerCombatState(bot);
        bot.Follower.CanPatrol=true;bot.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(solo.IsActive()&&bot.Follower.CombatIndependent,"peaceful On Your Own authorizes investigation and initializes combat independence");
        bot.Follower.CombatIndependent=false;bot.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(bot.Sain.Decision.CurrentCombatDecision==ECombatDecision.None,"combat independence revocation overrides saved patrol preference during this combat");
        solo.IsActive();Time.time+=3.1f;SAINFollowerRuntime.GetCombatPhase(bot);
        Check(!bot.Follower.CombatIndependent&&bot.Follower.CanPatrol,"combat release clears active independence while preserving patrol intent");
        bot.Follower.CanPatrol=false;bot.Follower.CombatIndependenceRequested=true;
        bot.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(solo.IsActive()&&bot.Follower.CombatIndependent,"saved combat On Your Own intent also authorizes the next independent investigation");
    }

    private static void TestMedicalLingerDeadline(){
        var b=RegroupBot("medicalLingerDeadline",20);var m=b.Sain.Decision.Manager;
        m.Publish(ECombatDecision.SeekCover);SAINFollowerRuntime.GetCombatPhase(b);
        b.Memory.GoalEnemy=null;
        float lostAt=Time.time;Check(SAINFollowerRuntime.GetCombatPhase(b)==SAINFollowerCombatPhase.Linger,"enemy loss starts one handoff deadline");
        Time.time=lostAt+1;m.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.Surgery);
        Check(SAINFollowerRuntime.GetCombatPhase(b)==SAINFollowerCombatPhase.Combat,"medical selection may start within original handoff window");
        m.Publish(ECombatDecision.None);Time.time=lostAt+2;SAINFollowerRuntime.GetCombatPhase(b);
        m.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.FirstAid);SAINFollowerRuntime.GetCombatPhase(b);
        Time.time=lostAt+3.1f;
        Check(SAINFollowerRuntime.GetCombatPhase(b)==SAINFollowerCombatPhase.Released&&b.RecoveryStarts==1,"cancelled/reselected medicine cannot extend enemy-loss deadline");
        m.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.Surgery);
        Check(b.Sain.Decision.CurrentCombatDecision==ECombatDecision.None&&b.Sain.Decision.CurrentSelfDecision==ESelfActionType.None&&!SainCombatRecorderBridge.IsActive(b),"rejected medical publication clears solo and self without reopening recorder");
        b.Sain.GoalEnemy=null;b.UsingMedical=true;m.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.FirstAid);
        Check(SAINFollowerRuntime.GetCombatPhase(b)==SAINFollowerCombatPhase.Released&&!SainCombatRecorderBridge.IsActive(b)&&b.RecoveryStarts==1,"core healing with no native goal cannot manufacture addon combat episodes");
        b.UsingMedical=false;b.Memory.GoalEnemy=new EnemyInfo();b.Sain.GoalEnemy=new Enemy();m.Publish(ECombatDecision.SeekCover);SAINFollowerRuntime.GetCombatPhase(b);
        Check(!b.RecoveryActive,"renewed accepted combat cancels core recovery normally");
        b.Memory.GoalEnemy=null;b.UsingMedical=true;m.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.FirstAid);
        SAINFollowerRuntime.GetCombatPhase(b);Time.time+=8;
        Check(SAINFollowerRuntime.GetCombatPhase(b)==SAINFollowerCombatPhase.Combat&&b.RecoveryStarts==1,"already-running native treatment survives past fixed deadline");
        b.UsingMedical=false;
        Check(SAINFollowerRuntime.GetCombatPhase(b)==SAINFollowerCombatPhase.Released&&b.RecoveryStarts==2,"treatment completion releases immediately when original deadline expired");
    }

}
