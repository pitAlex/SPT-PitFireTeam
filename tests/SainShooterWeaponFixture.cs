using System;
using EFT;
using pitTeam.Components;
using pitTeam.SAINAddon;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.Decision;
using UnityEngine;
namespace EFT {
    public partial class EnemyInfo {public float Distance=60;}
    public partial class BotOwner {
        public bool AutoAvailable,AutoReady,SupportSelected,AutoRequestAccepted=true,AutoAdvanceAllowed=true;
        public int AutoRequests,PrimaryReturns;
    }
}
namespace pitTeam.BigBrain {
    public sealed partial class FollowerCombatCommon {
        public bool HasAutomaticCloseCombatWeaponAvailable()=>riskOwner.AutoAvailable;
        public bool IsAutomaticCloseCombatWeaponReady()=>riskOwner.AutoReady;
        public bool TryRequestAutomaticSupportForCloseCombat()=>RequestAuto();
        public bool IsEligibleAutomaticMarksmanSupportSelectedAndReady()=>riskOwner.AutoReady&&riskOwner.SupportSelected;
        public bool IsWeaponSelectionSettledForAutomaticMarksmanSupportRequest()=>!riskOwner.WeaponManager.Selector.IsChanging&&riskOwner.WeaponManager.IsWeaponReady;
        public bool TryRequestEligibleAutomaticMarksmanSupport()=>RequestAuto();
        private bool RequestAuto(){if(!riskOwner.AutoAvailable||!riskOwner.AutoRequestAccepted||riskOwner.WeaponManager.Selector.IsChanging)return false;riskOwner.AutoRequests++;riskOwner.WeaponManager.Selector.IsChanging=true;return true;}
        public bool HasLoadedAutomaticMarksmanSupportWeapon()=>riskOwner.AutoAvailable;
        public bool TrySwitchBackToPrimaryFromAutomaticMarksmanSupport(){riskOwner.PrimaryReturns++;riskOwner.SupportSelected=false;riskOwner.AutoReady=false;return true;}
        public bool IsUsingAutomaticMarksmanSupportOverNonAutomaticPrimary()=>riskOwner.SupportSelected;
        public bool IsTemporaryHoldPositionAggressionActive()=>riskOwner.Follower.IsTemporaryCombatAggressionOverrideActive;
        public float GetAggression01()=>riskOwner.Follower.CombatAggression/100f;
        public bool ShouldBlockProactiveAutoPushForWeaponThreat(EnemyInfo e)=>riskOwner.RiskWeaponPolicy==2;
        public bool ShouldUseCautiousWeaponThreatStyle(EnemyInfo e)=>riskOwner.RiskWeaponPolicy==1;
        private static int GetAllowedLowThreatEnemyCount(float a)=>a>=.7f?3:a>=.4f?2:1;
    }
    public sealed class FollowerCombatSniper(BotOwner owner,FollowerCombatCommon common) {
        private bool IsWithinMarksmanAutoSearchDistance(EnemyInfo e,float aggression)=>owner.AutoAdvanceAllowed;
        internal static bool CanUseAutomaticSupportForCloseThreat(BotOwner owner,EnemyInfo e)=>e!=null&&e.Distance<=20;
    }
}
public static partial class CombatChecks {
    private static void AutoReady(BotOwner b){b.AutoReady=true;b.SupportSelected=true;b.WeaponManager.Selector.IsChanging=false;}
    private static void TestShooterWeaponTransitions(){
        var b=ShooterBot("weaponLeavesClose");b.AutoAvailable=true;b.Follower.CombatAggression=0;
        b.Sain.GoalEnemy.EnemyInfo.Distance=10;FiringPositionFinder.Candidate=null;
        var m=b.Sain.Decision.Manager;var p=SAINFollowerRuntime.GetMarksman(b);
        m.Publish(ECombatDecision.SeekCover);
        b.Sain.GoalEnemy.EnemyInfo.Distance=60;Time.time+=1;m.Publish(ECombatDecision.SeekCover);
        Check(!p.Weapons.Preparing&&b.PrimaryReturns==0,"leaving close threat cancels preparation while the draw callback is still pending");
        AutoReady(b);Time.time+=10;m.Publish(ECombatDecision.SeekCover);
        Check(!p.Weapons.Preparing&&b.PrimaryReturns==1,"late defensive draw callback returns to primary after threat leaves");

        b=ShooterBot("weaponMedicalReturn");b.AutoAvailable=true;
        m=b.Sain.Decision.Manager;p=SAINFollowerRuntime.GetMarksman(b);m.Publish(ECombatDecision.SeekCover);
        AutoReady(b);p.Weapons.Prepare();b.RiskMedical=true;
        m.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.FirstAid);
        Check(b.PrimaryReturns==0,"primary restoration cannot preempt incoming First Aid");
        p.Clear("combatEnded");Check(b.PrimaryReturns==0,"combat cleanup retains deferred restoration during medical work");
        b.RiskMedical=false;m.Publish(ECombatDecision.SeekCover);m.Publish(ECombatDecision.SeekCover);
        Check(b.PrimaryReturns==1,"primary restoration resumes after medical protection clears");

        b=ShooterBot("weaponGrenadeReturn");b.AutoAvailable=true;
        m=b.Sain.Decision.Manager;p=SAINFollowerRuntime.GetMarksman(b);m.Publish(ECombatDecision.SeekCover);
        AutoReady(b);p.Weapons.Prepare();m.Publish(ECombatDecision.ThrowGrenade);
        Check(b.PrimaryReturns==0,"incoming grenade action prevents cleanup weapon swap");
        p.Clear("combatEnded");Check(b.PrimaryReturns==0,"live native grenade action protects lifecycle cleanup");
        m.Publish(ECombatDecision.SeekCover);m.Publish(ECombatDecision.SeekCover);
        Check(b.PrimaryReturns==1,"primary restoration resumes after grenade action");

        b=ShooterBot("weaponSuppressionSettle");b.AutoAvailable=true;b.WeaponManager.Selector.IsChanging=true;
        b.Follower.SuppressEnemyUseAutomaticSecondary=true;b.Follower.Command=FollowerCommandType.SuppressEnemy;
        b.Follower.SuppressEnemyTargetProfileId=b.Sain.GoalEnemy.EnemyProfileId;
        m=b.Sain.Decision.Manager;m.Publish(ECombatDecision.SeekCover);
        var support=SAINFollowerRuntime.GetSquadSupport(b);
        Check(support.Active&&b.AutoRequests==0,"suppression retains its command while an unrelated weapon transition settles");
        for(int i=0;i<20;i++)m.Publish(ECombatDecision.SeekCover);
        support.Tick();support.Steer();Check(b.AutoRequests==0&&b.Sain.Suppression.Calls==0,"selector settle wait neither redraws nor fires");
        Time.time+=2.8f;b.WeaponManager.Selector.IsChanging=false;m.Publish(ECombatDecision.SeekCover);
        Check(support.Active&&b.AutoRequests==1,"settled selector issues one accepted support draw");
        Time.time+=2.8f;AutoReady(b);m.Publish(ECombatDecision.SeekCover);
        Check(support.Active&&support.OwnsAction,"settle time does not consume the separate accepted draw window");
        Time.time+=5.5f;m.Publish(ECombatDecision.SeekCover);Check(support.Active,"six-second suppression execution budget starts after readiness");
        Time.time+=.6f;m.Publish(ECombatDecision.SeekCover);Check(!support.Active,"suppression execution still has a bounded deadline");

        b=ShooterBot("weaponSettleTimeout");b.AutoAvailable=true;b.WeaponManager.Selector.IsChanging=true;
        b.Follower.SuppressEnemyUseAutomaticSecondary=true;b.Follower.Command=FollowerCommandType.SuppressEnemy;
        b.Follower.SuppressEnemyTargetProfileId=b.Sain.GoalEnemy.EnemyProfileId;
        m=b.Sain.Decision.Manager;m.Publish(ECombatDecision.SeekCover);Time.time+=3.1f;m.Publish(ECombatDecision.SeekCover);
        Check(!SAINFollowerRuntime.GetSquadSupport(b).Active&&b.AutoRequests==0,"selector settle timeout ends suppression without a switch request");

        b=ShooterBot("weaponPreparedStandoff");b.AutoAvailable=true;b.Follower.CombatAggression=70;
        b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(46,0,0);FiringPositionFinder.Candidate=new Vector3(28,0,0);
        m=b.Sain.Decision.Manager;p=SAINFollowerRuntime.GetMarksman(b);m.Publish(ECombatDecision.Search);
        Check(p.Preparing&&p.Destination.HasValue,"automatic search prepares a valid eighteen-metre separation");
        b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(40,0,0);AutoReady(b);Time.time+=1;m.Publish(ECombatDecision.Search);
        Check(!p.OwnsMovement&&!p.Destination.HasValue,"six-metre knowledge movement invalidates twelve-metre standoff at readiness");
        Check(FiringPositionFinder.Calls==1,"invalid readiness handoff does not trigger another native scan");
        b=ShooterBot("weaponPreparedRoute");b.AutoAvailable=true;b.Follower.CombatAggression=70;
        m=b.Sain.Decision.Manager;p=SAINFollowerRuntime.GetMarksman(b);FiringPositionFinder.Candidate=new Vector3(25,0,0);
        m.Publish(ECombatDecision.Search);AutoReady(b);pitTeam.Utils.Utils.PathComplete=false;
        m.Publish(ECombatDecision.Search);pitTeam.Utils.Utils.PathComplete=true;
        Check(!p.OwnsMovement&&!p.Destination.HasValue,"route invalidated during draw cannot enter movement at readiness");
    }
    private static void TestShooterWeapons(){
        var b=ShooterBot("autoSearch");b.AutoAvailable=true;b.Follower.CombatAggression=70;
        b.Sain.GoalEnemy.EnemyInfo.Distance=60;
        var m=b.Sain.Decision.Manager;var p=SAINFollowerRuntime.GetMarksman(b);
        FiringPositionFinder.Candidate=new Vector3(25,0,0);m.Publish(ECombatDecision.Search);
        Check(p.Preparing&&!p.OwnsMovement&&p.Destination.HasValue&&b.AutoRequests==1,"Shooter commits native destination before one automatic draw");
        for(int i=0;i<30;i++)m.Publish(ECombatDecision.Search);
        Check(b.AutoRequests==1&&FiringPositionFinder.Calls==1,"weapon preparation never repeats request or finder");
        AutoReady(b);m.Publish(ECombatDecision.Search);
        Check(p.OwnsMovement&&!p.Preparing,"ready support weapon releases retained close-search movement");
        Check(b.PrimaryReturns==0,"automatic movement retains support weapon outside defensive radius");
        int handoffPathChecks=pitTeam.Utils.Utils.PathCalls;
        for(int i=0;i<30;i++)m.Publish(ECombatDecision.Search);
        Check(pitTeam.Utils.Utils.PathCalls==handoffPathChecks,"readiness destination revalidation does not repeat during committed movement");
        p.Clear("combatEnded");Check(b.PrimaryReturns==1,"combat cleanup restores primary");

        b=ShooterBot("autoShotDuringDraw");b.AutoAvailable=true;b.Follower.CombatAggression=70;
        m=b.Sain.Decision.Manager;p=SAINFollowerRuntime.GetMarksman(b);FiringPositionFinder.Candidate=new Vector3(25,0,0);
        m.Publish(ECombatDecision.Search);AutoReady(b);b.Sain.GoalEnemy.IsVisible=true;b.Sain.GoalEnemy.CanShoot=true;
        m.Publish(ECombatDecision.Search);Check(!p.Weapons.Preparing&&!p.Destination.HasValue,"real shot releases pending weapon preparation and forward destination");
        m.Publish(ECombatDecision.StandAndShoot);Check(b.PrimaryReturns==1,"shot interruption cannot leave a permanent preparation weapon lease");

        b=ShooterBot("autoTimeout");b.AutoAvailable=true;b.Follower.CombatAggression=70;b.Sain.GoalEnemy.EnemyInfo.Distance=60;
        m=b.Sain.Decision.Manager;p=SAINFollowerRuntime.GetMarksman(b);FiringPositionFinder.Candidate=new Vector3(25,0,0);
        m.Publish(ECombatDecision.Search);Time.time+=3.1f;m.Publish(ECombatDecision.Search);
        Check(!p.Destination.HasValue&&!p.OwnsMovement&&b.AutoRequests==1,"failed asynchronous draw retires destination at three seconds");
        AutoReady(b);m.Publish(ECombatDecision.SeekCover);
        Check(b.PrimaryReturns==1,"late weapon callback is restored after timed-out intent");

        b=ShooterBot("autoNoCandidate");b.AutoAvailable=true;b.Follower.CombatAggression=70;b.Sain.GoalEnemy.EnemyInfo.Distance=60;
        FiringPositionFinder.Candidate=null;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(b.AutoRequests==0,"proactive weapon draw requires a usable native destination");

        b=ShooterBot("autoZeroDefense");b.AutoAvailable=true;b.Follower.CombatAggression=0;b.Sain.GoalEnemy.EnemyInfo.Distance=10;
        m=b.Sain.Decision.Manager;p=SAINFollowerRuntime.GetMarksman(b);m.Publish(ECombatDecision.SeekCover);
        Check(p.Preparing&&b.AutoRequests==1,"zero aggression still permits defensive support weapon preparation");
        AutoReady(b);m.Publish(ECombatDecision.SeekCover);Check(b.PrimaryReturns==0,"close threat retains secondary through ordinary holds");
        b.Sain.GoalEnemy.EnemyInfo.Distance=60;m.Publish(ECombatDecision.SeekCover);
        Check(b.PrimaryReturns==1,"leaving close threat restores primary without an automatic intent");

        b=ShooterBot("autoVisible");b.AutoAvailable=true;b.Sain.GoalEnemy.EnemyInfo.Distance=10;
        b.Sain.GoalEnemy.IsVisible=true;b.Sain.GoalEnemy.CanShoot=true;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(b.AutoRequests==0,"immediate visible shot retains current weapon");

        foreach(int policy in new[]{1,2}) {
            b=ShooterBot("autoRisk"+policy);b.AutoAvailable=true;b.Follower.CombatAggression=70;b.RiskWeaponPolicy=policy;
            FiringPositionFinder.Candidate=new Vector3(25,0,0);b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
            Check(b.AutoRequests==0&&!SAINFollowerRuntime.GetMarksman(b).OwnsMovement,"Core cautious/blocked ammo policy rejects proactive closing "+policy);
        }
        b=ShooterBot("autoZeroOffense");b.AutoAvailable=true;b.Follower.CombatAggression=0;
        FiringPositionFinder.Candidate=new Vector3(25,0,0);b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(b.AutoRequests==0,"zero aggression blocks proactive automatic advance");
        b=ShooterBot("autoMedical");b.AutoAvailable=true;b.RiskMedical=true;b.Sain.GoalEnemy.EnemyInfo.Distance=10;
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(b.AutoRequests==0,"pending treatment blocks defensive weapon draw");
        b=ShooterBot("autoRequestRejected");b.AutoAvailable=true;b.AutoRequestAccepted=false;b.Sain.GoalEnemy.EnemyInfo.Distance=10;
        m=b.Sain.Decision.Manager;m.Publish(ECombatDecision.SeekCover);b.AutoRequestAccepted=true;
        for(int i=0;i<20;i++)m.Publish(ECombatDecision.SeekCover);
        Check(b.AutoRequests==0,"rejected draw cannot churn into repeated requests before retry");
        Time.time+=4.1f;m.Publish(ECombatDecision.SeekCover);Check(b.AutoRequests==1,"rejected defensive draw retries after bounded cooldown");
        b=ShooterBot("autoSuppressionHook");b.AutoAvailable=true;
        var boss=(pitAIBossPlayer)b.BotFollower.BossToFollow;
        Check(!boss.AddonExcluded(b)&&boss.Issue(b)&&boss.Secondary&&boss.Force&&!boss.Require,"Shooter suppression hook retains Core secondary selection without launcher");
        b.AutoAvailable=false;Check(!boss.Issue(b),"Shooter suppression rejects absent eligible automatic support");

        b=ShooterBot("autoOrderedSuppress");b.AutoAvailable=true;b.Follower.SuppressEnemyUseAutomaticSecondary=true;
        b.Follower.Command=FollowerCommandType.SuppressEnemy;b.Follower.SuppressEnemyTargetProfileId=b.Sain.GoalEnemy.EnemyProfileId;
        m=b.Sain.Decision.Manager;m.Publish(ECombatDecision.SeekCover);var support=SAINFollowerRuntime.GetSquadSupport(b);
        Check(support.Active&&b.AutoRequests==1,"ordered Shooter suppression prepares eligible secondary");
        support.Tick();support.Steer();Check(b.Sain.Suppression.Calls==0,"ordered suppression cannot fire while weapon preparation is pending");
        AutoReady(b);m.Publish(ECombatDecision.SeekCover);
        Check(support.Active&&support.OwnsAction,"ready automatic weapon retains suppression objective");
        support.Clear("combatEnded");Check(b.PrimaryReturns==1,"suppression cleanup restores primary");
    }
}
