using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.SubComponents.CoverFinder;
using UnityEngine;

namespace SAIN.Components {
    public class PreparationAim {public int Resets;public void LoseAimTarget(){Resets++;}}
    public partial class BotComponent {public PreparationAim Aim=new PreparationAim();}
}
public static partial class CombatChecks {
    private static BotOwner PreparationBot(string id, Vector3 heard, ECombatDecision decision) {
        var b=CoverBot(id);b.Memory.GoalEnemy=null;
        b.BotFollower.BossToFollow=new pitAIBossPlayer();
        b.Sain.GoalEnemy.Seen=false;b.Sain.GoalEnemy.Heard=true;
        b.Sain.GoalEnemy.Hearing.EnemyHeardFromPeace=true;
        b.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=heard;
        b.Sain.GoalEnemy.KnownPlaces.LastHeardPlace=b.Sain.GoalEnemy.KnownPlaces.LastKnownPlace=
            new SAIN.SAINComponent.Classes.EnemyClasses.EnemyPlace{SoundType=SAINSoundType.FootStep,Position=heard};
        b.BotsGroup.Enemies[b.Sain.GoalEnemy.EnemyPlayer]=new BotGroupEnemyInfo();
        b.Sain.Decision.Manager.Publish(decision);
        return b;
    }
    private static void TestHeardSoundAdmission() {
        foreach(var sound in new[]{SAINSoundType.Conversation,SAINSoundType.Pain,SAINSoundType.Breathing,
            SAINSoundType.FootStep,SAINSoundType.Sprint,SAINSoundType.Prone,SAINSoundType.Jump,
            SAINSoundType.Land,SAINSoundType.GearSound,SAINSoundType.Bush,
            SAINSoundType.Shot,SAINSoundType.SuppressedShot,SAINSoundType.BulletImpact,
            SAINSoundType.GrenadeExplosion,SAINSoundType.Generic}) {
            var b=PreparationBot("sound"+sound,new Vector3(10,0,0),ECombatDecision.Freeze);
            b.Sain.GoalEnemy.KnownPlaces.LastHeardPlace.SoundType=sound;
            bool voice=sound==SAINSoundType.Conversation||sound==SAINSoundType.Pain||sound==SAINSoundType.Breathing;
            bool movement=sound==SAINSoundType.FootStep||sound==SAINSoundType.Sprint||sound==SAINSoundType.Prone||
                sound==SAINSoundType.Jump||sound==SAINSoundType.Land||sound==SAINSoundType.GearSound||sound==SAINSoundType.Bush;
            float boundary=25f;
            b.Sain.GoalEnemy.KnownPlaces.LastHeardPlace.Position=new Vector3(boundary,0,0);
            Check(SAINFollowerCombatHandoff.AllowsAmbushPreparation(b.Sain,b.Sain.GoalEnemy,ECombatDecision.Freeze,
                ESquadDecision.None,ESelfActionType.None)==(voice||movement),
                "only local voice/movement reports can prepare contact at their boundary: "+sound);
            b.Sain.GoalEnemy.KnownPlaces.LastHeardPlace.Position=new Vector3(boundary+.1f,0,0);
            Check(!SAINFollowerCombatHandoff.AllowsAmbushPreparation(b.Sain,b.Sain.GoalEnemy,ECombatDecision.Freeze,
                ESquadDecision.None,ESelfActionType.None),"heard preparation rejects farther report: "+sound);
            if(sound==SAINSoundType.Shot) {
                b.Memory.GoalEnemy=new EnemyInfo();
                Check(SAINFollowerCombatHandoff.AllowsDecision(b.Sain,ECombatDecision.StandAndShoot,
                    ESelfActionType.None,b.Sain.GoalEnemy),"gunshot filter does not block accepted combat");
            }
        }
        var stale=PreparationBot("soundStale",new Vector3(10,0,0),ECombatDecision.SeekCover);
        stale.Sain.GoalEnemy.KnownPlaces.LastKnownPlace=new SAIN.SAINComponent.Classes.EnemyClasses.EnemyPlace{
            SoundType=SAINSoundType.Shot,Position=new Vector3(10,0,0)};
        Check(!SAINFollowerCombatHandoff.AllowsAmbushPreparation(stale.Sain,stale.Sain.GoalEnemy,
            ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.None),
            "an old step report cannot justify preparation after a newer gunshot");
    }
    private static void TestPreparation() {
        TestAttentionIgnore();
        TestHeardSoundAdmission();
        Time.time+=.02f;
        foreach(var decision in new[]{ECombatDecision.Freeze,ECombatDecision.SeekCover,ECombatDecision.ShiftCover}) {
            var b=PreparationBot("prepare"+decision,new Vector3(20,0,0),decision);
            var solo=new SAINFollowerSoloCombatLayer(b,74);Tick();b.Sain.Decision.Manager.Publish(decision);
            Check(solo.IsActive()&&solo.GetNextAction().Type==typeof(SAINFollowerPreparationAction),"heard defensive decision owns preparation: "+decision);
            Check(!SAINFollowerRuntime.HasEnteredCombat(b),"heard defensive preparation is not admitted combat: "+decision);
            var action=new SAINFollowerPreparationAction(b);action.Start();
            b.Sain.GoalEnemy.EnemyPosition=new Vector3(-100,50,0);
            int shooting=b.Sain.Shoot.Calls, suppression=b.Sain.Suppression.Calls;
            action.OnSteeringTicked();action.Update(null);action.OnSteeringTicked();
            Check((b.Sain.Steering.LookPoint-(new Vector3(20,0,0)+b.Sain.Transform.WeaponRoot-b.Position)).sqrMagnitude<.001f,
                "preparation holds orientation to knowledge, not hidden live target: "+decision);
            Check(b.Sain.Shoot.Calls==shooting&&b.Sain.Suppression.Calls==suppression,
                "preparation never invokes native shooting/suppression: "+decision);
            Check(!b.Sain.Mover.Moving,"no-cover preparation stays stationary: "+decision);
            int queries=SainBotCoverData.Queries, paths=pitTeam.Utils.Utils.PathCalls;
            for(int i=0;i<20;i++){Time.time+=.1f;Time.time+=.02f;action.Update(null);}
            Check(SainBotCoverData.Queries==queries&&pitTeam.Utils.Utils.PathCalls==paths,"empty preparation does not rescan or probe routes per frame: "+decision);
            b.Follower.Command=FollowerCommandType.RegroupNearBoss;
            int looks=b.Sain.Steering.Looks;action.OnSteeringTicked();action.Update(null);
            Check(b.Sain.Steering.Looks==looks&&solo.IsCurrentActionEnding()&&!solo.IsActive(),"command interrupts preparation immediately: "+decision);
            action.Stop();
        }
        var local=PreparationBot("prepareCover",new Vector3(20,0,0),ECombatDecision.SeekCover);
        var cover=CoverAt(6,6);local.Sain.Cover.CoverPoints.Add(cover);
        var layer=new SAINFollowerSoloCombatLayer(local,74);Tick();local.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);layer.IsActive();layer.GetNextAction();
        var move=new SAINFollowerPreparationAction(local);move.Start();move.Update(null);
        Check(local.Sain.Mover.Moving&&local.Sain.Mover.Destination.x==6,"preparation moves to native-validated nearby cover");
        Check(((pitAIBossPlayer)local.BotFollower.BossToFollow).CombatEvents.Claims.ContainsKey(local.ProfileId),"preparation reserves its cover for squad spacing");
        int issued=local.Sain.Mover.Paths;
        foreach(var d in new[]{ECombatDecision.ShiftCover,ECombatDecision.Freeze,ECombatDecision.SeekCover}) {
            local.Sain.Decision.Manager.Publish(d);
            Check(!layer.IsCurrentActionEnding(),"defensive decision changes retain preparation cover: "+d);
            move.Update(null);
        }
        Check(local.Sain.Mover.Paths==issued,"cover decision changes never restart navigation");
        local.GetPlayer.Position=cover.Position;move.Update(null);
        Check(!local.Sain.Mover.Moving,"exact committed-cover arrival stops movement");
        for(int i=0;i<30;i++){Time.time+=.2f;Time.time+=.02f;move.Update(null);}
        Check(local.Sain.Mover.Paths==issued,"arrived preparation does not shift cover repeatedly");
        local.Memory.GoalEnemy=new EnemyInfo();local.Sain.Decision.Manager.Publish(ECombatDecision.Freeze);
        Check(layer.IsCurrentActionEnding()&&layer.GetNextAction().Type.Name=="FreezeAction","accepted Core goal switches preparation to ordinary native combat");
        move.Stop();
        Check(!((pitAIBossPlayer)local.BotFollower.BossToFollow).CombatEvents.Claims.ContainsKey(local.ProfileId),"preparation releases its reservation on handoff");
        foreach(bool complete in new[]{false,true}) {
            var b=PreparationBot("prepareRemoteFloor"+complete,new Vector3(3,6,0),ECombatDecision.SeekCover);
            b.Sain.Cover.CoverPoints.Add(CoverAt(5,5));
            pitTeam.Utils.Utils.PathComplete=complete;pitTeam.Utils.Utils.PathScale=20;
            var action=new SAINFollowerPreparationAction(b);action.Start();
            int paths=pitTeam.Utils.Utils.PathCalls, queries=SainBotCoverData.Queries;
            for(int i=0;i<20;i++){Time.time+=.02f;action.Update(null);}
            Check(!b.Sain.Mover.Moving&&pitTeam.Utils.Utils.PathCalls==paths+1&&SainBotCoverData.Queries==queries,
                "other-floor distant/incomplete connection waits without cover urgency: "+complete);
            action.Stop();
        }
        pitTeam.Utils.Utils.PathComplete=true;pitTeam.Utils.Utils.PathScale=1;
        var stairs=PreparationBot("prepareNearbyStairs",new Vector3(4,5,0),ECombatDecision.ShiftCover);
        stairs.Sain.Cover.CoverPoints.Add(CoverAt(5,5));
        var nearbyStairs=new SAINFollowerPreparationAction(stairs);nearbyStairs.Start();nearbyStairs.Update(null);
        Check(stairs.Sain.Mover.Moving,"short complete cross-floor threat route permits local defensive cover");
        nearbyStairs.Stop();Check(!stairs.Sain.Mover.Moving,"preparation stop cancels its own unfinished path");
        foreach(var point in new[]{CoverAt(4,70),CoverAt(30,30),CoverAt(5,5)}) {
            var b=PreparationBot("prepareReject"+point.PathData.PathLength,new Vector3(20,0,0),ECombatDecision.SeekCover);
            if(point.PathData.PathLength==5)point.Position=new Vector3(5,5,0);
            b.Sain.Cover.CoverPoints.Add(point);
            var action=new SAINFollowerPreparationAction(b);action.Start();action.Update(null);
            Check(!b.Sain.Mover.Moving,"preparation rejects distant route, far cover or different-floor cover");
            action.Stop();
        }
        var stall=PreparationBot("prepareStall",new Vector3(20,0,0),ECombatDecision.SeekCover);
        stall.Sain.Cover.CoverPoints.Add(CoverAt(6,6));
        var stalled=new SAINFollowerPreparationAction(stall);stalled.Start();stalled.Update(null);
        Time.time+=6.1f;stalled.Update(null);
        issued=stall.Sain.Mover.Paths;stalled.Update(null);
        Check(!stall.Sain.Mover.Moving&&stall.Sain.Mover.Paths==issued,"stalled cover gives up once and waits without rearming");
        stalled.Stop();

        var med=PreparationBot("prepareCoreHealing",new Vector3(20,0,0),ECombatDecision.SeekCover);
        med.UsingMedical=true;med.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(SAINFollowerRuntime.GetCombatPhase(med)==SAINFollowerCombatPhase.Released,
            "heard preparation cannot steal an active Core medical procedure");

        var pending=PreparationBot("prepareBudget",new Vector3(20,0,0),ECombatDecision.SeekCover);
        for(int i=0;i<12;i++)SainBotCoverData.Scene.Add(new SainBotColliderData{Collider=new Collider{Point=CoverAt(5+i,5+i)}});
        var planning=new SAINFollowerPreparationAction(pending);planning.Start();
        bool selected=false;
        for(int i=0;i<12;i++){
            Time.time+=.1f;int probes=CoverAnalyzer.Creates+CoverAnalyzer.Rechecks;
            planning.Update(null);
            Check(CoverAnalyzer.Creates+CoverAnalyzer.Rechecks-probes<=4,"preparation cover discovery obeys shared frame probe budget");
            if(pending.Sain.Mover.Moving){selected=true;break;}
            Check(pending.Sain.Mover.Paths==0,"pending preparation cannot advance or accept an early inferior cover");
        }
        Check(selected&&pending.Sain.Mover.Destination.x==5,"pending cover pass completes and picks shortest local route");
        pending.Sain.Mover.WalkToPoint(new Vector3(80,0,0));planning.Stop();
        Check(pending.Sain.Mover.Moving&&pending.Sain.Mover.Destination.x==80,"preparation cleanup cannot stop another action's path");
        SainBotCoverData.Scene.Clear();

        var floors=PreparationBot("prepareChangedFloor",new Vector3(3,6,0),ECombatDecision.SeekCover);
        floors.Sain.Cover.CoverPoints.Add(CoverAt(5,5));pitTeam.Utils.Utils.PathScale=20;
        var floorAction=new SAINFollowerPreparationAction(floors);floorAction.Start();floorAction.Update(null);
        floors.Sain.GoalEnemy.KnownPlaces.LastKnownPosition=new Vector3(3,0,0);
        floors.Sain.GoalEnemy.KnownPlaces.LastHeardPlace.Position=new Vector3(3,0,0);
        floorAction.Update(null);
        Check(floors.Sain.Mover.Moving,"new knowledge on current floor reconsiders the former remote-floor wait");
        floorAction.Stop();pitTeam.Utils.Utils.PathScale=1;
    }
}
