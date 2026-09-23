using System;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.Classes.Decision;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;
namespace UnityEngine {
    public partial struct Vector3 {
        public static Vector3 operator /(Vector3 a,float b)=>new Vector3(a.x/b,a.y/b,a.z/b);
        public static Vector3 Cross(Vector3 a,Vector3 b)=>new Vector3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
    }
    public enum QueryTriggerInteraction {Ignore}
    public struct RaycastHit {public float distance;}
    public static partial class Physics {
        public static bool HardWall=true,HeadExposed;public static float WallDistance=1;public static int Probes;
        public static bool Raycast(Vector3 origin,Vector3 direction,out RaycastHit hit,float distance,int mask,QueryTriggerInteraction query){
            if(mask!=LayersMaskController.HighPolyWithTerrainMask||query!=QueryTriggerInteraction.Ignore)throw new Exception("Medical cover must exclude foliage and triggers");
            Probes++;hit=new RaycastHit{distance=WallDistance};return HardWall&&!(HeadExposed&&origin.y>1.4f);
        }
    }
}
namespace pitTeam.Utils {public static class Covers {__CORE_COVER__}}
namespace SAIN.SAINComponent.Classes.Mover {
    public class DogFight : SAIN.SAINComponent.BotBase {
        public int Moves;public DogFight(BotComponent bot):base(bot){}
        [MethodImpl(MethodImplOptions.NoInlining)] public void DogFightMove(bool aggressive,Enemy enemy){Moves++;}
    }
}
namespace SAIN.SAINComponent.Classes.Decision {
    public partial class SelfActionDecisionClass : SAIN.SAINComponent.BotBase {
        public bool ItemEligible=true,NativeSafety;public int ItemChecks,SafetyChecks;
        public SelfActionDecisionClass(BotComponent bot):base(bot){}
        public bool FirstAidForTest(){ItemChecks++;if(!ItemEligible)return false;foreach(var enemy in Bot.EnemyController.KnownEnemies)if(!ShallFirstAidCheckEnemy(enemy))return false;return true;}
        [MethodImpl(MethodImplOptions.NoInlining)] private bool ShallFirstAidCheckEnemy(Enemy enemy){SafetyChecks++;return NativeSafety;}
    }
}
namespace SAIN.SAINComponent.Classes {
    public class BotSurgery : SAIN.SAINComponent.BotBase {
        public bool ItemEligible=true,Bleeding,NativeSafety;public int ItemChecks;
        public bool AreaClearForSurgery{get;private set;}
        public BotSurgery(BotComponent bot):base(bot){}
        [MethodImpl(MethodImplOptions.NoInlining)] private bool CheckEnemies()=>NativeSafety;
        [MethodImpl(MethodImplOptions.NoInlining)] public bool CheckAreaClearForSurgery(){ItemChecks++;return ItemEligible&&!Bleeding&&CheckEnemies();}
    }
}
public static partial class CombatChecks {
    private static void TestMedicalRecovery(){
        var b=PushBot("brickRecovery");var push=SAINFollowerRuntime.GetPush(b);var cover=CoverAt(0);
        b.Sain.Cover.CoverInUse=cover;b.Sain.Mover.Moving=false;
        var enemy=b.Sain.GoalEnemy;enemy.Seen=false;enemy.Heard=true;enemy.InLineOfSight=true;enemy.TimeSinceSeen=-1;
        var firstAid=new SelfActionDecisionClass(b.Sain);var surgery=new BotSurgery(b.Sain);b.Sain.Medical.Surgery=surgery;
        b.RiskMedical=true;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(push.Ordered&&push.Phase==SAINPushPhase.Recovery&&!push.OwnsMovement,"pending treatment pauses command before recovery checks");
        int probes=Physics.Probes;Check(firstAid.FirstAidForTest()&&firstAid.ItemChecks==1,"heard-only contact with native LOS flag permits first aid at verified solid cover");
        Check(Physics.Probes-probes==3,"safe recovery uses all three production core chest and head protection rays");
        probes=Physics.Probes;Check(surgery.CheckAreaClearForSurgery()&&surgery.AreaClearForSurgery&&surgery.ItemChecks==1,"surgery reuses safe cover and publishes actual clearance to its native action");
        Check(Physics.Probes==probes,"medical policies share bounded physical-cover cache");
        firstAid.ItemEligible=false;Check(!firstAid.FirstAidForTest(),"safe cover cannot bypass native first-aid item eligibility");firstAid.ItemEligible=true;
        surgery.ItemEligible=false;Check(!surgery.CheckAreaClearForSurgery()&&!surgery.AreaClearForSurgery,"unavailable surgery clears previously true native clearance");surgery.ItemEligible=true;
        surgery.Bleeding=true;Check(!surgery.CheckAreaClearForSurgery()&&!surgery.AreaClearForSurgery,"bleeding retains native first-aid-before-surgery requirement");surgery.Bleeding=false;
        enemy.IsVisible=true;Check(!firstAid.FirstAidForTest()&&!surgery.CheckAreaClearForSurgery(),"visible threat immediately blocks safe-cover exception despite cache");enemy.IsVisible=false;
        enemy.CanShoot=true;Check(!firstAid.FirstAidForTest(),"shootable threat blocks recovery");enemy.CanShoot=false;
        enemy.Seen=true;enemy.TimeSinceSeen=1;Check(!firstAid.FirstAidForTest(),"recently seen contact retains recovery delay");enemy.TimeSinceSeen=10;
        b.Memory.IsUnderFire=true;Check(!firstAid.FirstAidForTest(),"incoming fire prevents treatment override");b.Memory.IsUnderFire=false;
        b.Sain.Medical.TimeSinceShot=2;Check(!firstAid.FirstAidForTest(),"recent hit prevents treatment override");b.Sain.Medical.TimeSinceShot=999;
        b.Sain.Suppression.IsHeavySuppressed=true;Check(!firstAid.FirstAidForTest(),"heavy suppression prevents treatment override");b.Sain.Suppression.IsHeavySuppressed=false;
        b.Sain.Mover.Moving=true;Check(!firstAid.FirstAidForTest(),"travel to cover cannot count as reached medical cover");b.Sain.Mover.Moving=false;
        b.GetPlayer.Position=new Vector3(5,0,0);Check(!firstAid.FirstAidForTest(),"stale distant CoverInUse cannot authorize healing");b.GetPlayer.Position=new Vector3();
        cover.Spotted=true;Check(!firstAid.FirstAidForTest(),"spotted cover cannot authorize healing");cover.Spotted=false;
        cover.CoverData.IsBad=true;Check(!firstAid.FirstAidForTest(),"invalid cover cannot authorize healing");cover.CoverData.IsBad=false;
        enemy.KnownPlaces.LastKnownPosition=new Vector3(10,0,0);Check(!firstAid.FirstAidForTest(),"very close remembered threat blocks treatment");enemy.KnownPlaces.LastKnownPosition=new Vector3(50,0,0);
        enemy.KnownPlaces.LastKnownPosition=null;Check(!firstAid.FirstAidForTest(),"missing native knowledge fails closed");enemy.KnownPlaces.LastKnownPosition=new Vector3(50,0,0);
        var other=new Enemy();other.EnemyPlayer.ProfileId="rearThreat";other.IsVisible=true;b.Sain.EnemyController.KnownEnemies.Add(other);
        Check(!firstAid.FirstAidForTest(),"non-goal visible enemy blocks medical exception");other.IsVisible=false;other.KnownPlaces.LastKnownPosition=new Vector3(0,0,10);
        Check(!surgery.CheckAreaClearForSurgery(),"non-goal close enemy blocks surgery too");b.Sain.EnemyController.KnownEnemies.Remove(other);
        Time.time+=1;Physics.HardWall=false;Check(!firstAid.FirstAidForTest(),"no hard cover never converts obscured visibility into medical safety");Physics.HardWall=true;
        Time.time+=1;Physics.WallDistance=20;Check(!firstAid.FirstAidForTest(),"distant obstruction is not protective cover beside follower");Physics.WallDistance=1;
        Time.time+=1;Physics.HeadExposed=true;Check(!firstAid.FirstAidForTest(),"chest protection alone is insufficient when head is exposed");Physics.HeadExposed=false;
        Time.time+=1;Check(firstAid.FirstAidForTest(),"safe cover revalidates after obstruction changes");
        probes=Physics.Probes;var diagnostic=Newtonsoft.Json.JsonConvert.SerializeObject(SAINFollowerRuntime.GetCover(b).Snapshot);
        Check(diagnostic.Contains("protectedCover")&&Physics.Probes==probes,"medical recorder snapshot reports cached reason without re-running safety geometry");
        b.Memory.GoalEnemy.Alive=false;Check(!firstAid.FirstAidForTest(),"rejected goal leaves medicine to core handoff");b.Memory.GoalEnemy.Alive=true;
        b.Follower.CombatTactic=FollowerCombatTactic.Balanced;firstAid.NativeSafety=false;surgery.NativeSafety=true;
        Check(!firstAid.FirstAidForTest()&&surgery.CheckAreaClearForSurgery()&&!surgery.AreaClearForSurgery,"core tactic keeps native medical policy and native flag behavior");
        b.Follower.CombatTactic=FollowerCombatTactic.SainMan;surgery.NativeSafety=false;
        pitFireTeam.IsSAINAddonInstalled=false;Check(!firstAid.FirstAidForTest(),"absent addon leaves medical policy native");pitFireTeam.IsSAINAddonInstalled=true;
        b.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover,ESquadDecision.None,ESelfActionType.FirstAid);
        Check(push.Ordered&&!push.OwnsMovement,"native medicine execution retains the ordered push target");
        b.RiskMedical=false;Time.time+=4;b.Sain.Decision.Manager.Publish(ECombatDecision.Search);
        Check(push.Ordered&&push.OwnsMovement,"same ordered push resumes after treatment and recovery deadline");
        surgery.CheckAreaClearForSurgery();
        b.Sain.Mover.Moving=false;b.Sain.Cover.CoverInUse=cover;surgery.CheckAreaClearForSurgery();
        Check(surgery.AreaClearForSurgery,"safe surgery clearance is available before opt-out");
        b.Follower.CombatTactic=FollowerCombatTactic.Balanced;Tick();
        Check(!surgery.AreaClearForSurgery,"tactic opt-out restores original native surgery flag");
        b.Follower.CombatTactic=FollowerCombatTactic.SainMan;Tick();
        b.Sain.Mover.Moving=false;b.Sain.Cover.CoverInUse=cover;surgery.CheckAreaClearForSurgery();
        Check(surgery.AreaClearForSurgery,"re-entry can publish a new surgery clearance");
        SainAddonBridge.TryForceReleaseFollowerCombatState(b);
        Check(!surgery.AreaClearForSurgery,"explicit combat release restores native surgery flag");
        var hook=AccessTools.Method(typeof(SelfActionDecisionClass),"ShallFirstAidCheckEnemy");
        Check(Harmony.GetPatchInfo(hook).Owners.Contains("xyz.pit.fireteam.sainaddon"),"medical extension is owned only by addon Harmony id");
        SainMedicalDecisionBridge.Apply(new Harmony("xyz.pit.fireteam.sainaddon"));
        Check(Harmony.GetPatchInfo(hook).Postfixes.Count==1,"repeated medical installation does not duplicate its postfix");
    }
}
