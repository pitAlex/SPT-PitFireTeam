using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace Comfort.Common {public static class Singleton<T> where T:new(){public static T Instance=new T();}}
namespace UnityEngine {public struct Rect {}}
namespace EFT {
    public partial class Player {public MarkerTransform Transform=>new MarkerTransform{position=Position};}
    public class MarkerTransform {public Vector3 position;}
    public partial class EnemyInfo {public string ProfileId="eftEnemy";public Vector3 CurrPosition;public Player Person;public bool IsVisible;}
    public class GameWorld {
        public Dictionary<string,Player> Players=new Dictionary<string,Player>();
        public Player GetAlivePlayerByProfileID(string id)=>Players.TryGetValue(id,out var p)&&p.HealthController.IsAlive?p:null;
    }
}

__MARKER_CONTACT__

internal class MarkerHarness {
    private class BotData {public BotOwner Data;}
    private readonly List<BotData> botMap=new List<BotData>();
    private readonly Dictionary<string,EnemyMarkerContact> enemyMarkersByProfileId=new Dictionary<string,EnemyMarkerContact>();
    private readonly List<EnemyMarkerContact> enemyMarkers=new List<EnemyMarkerContact>();
    private readonly HashSet<string> activeEnemyProfileIds=new HashSet<string>();
    private readonly Player myPlayer=new Player();
    private float _statusReportUntil=100000;
    private const float HiddenEnemyMarkerRefreshSeconds=5f, ReliableVisibleMaxAgeSeconds=.35f, MaxEnemyMarkerWorldCoordinate=100000f;
    public MarkerHarness(params BotOwner[] owners){foreach(var owner in owners)botMap.Add(new BotData{Data=owner});}
    public void Update(){SynchronizeEnemyMarkerContacts(true);RefreshEnemyMarkerContacts(false,false);}
    public EnemyMarkerContact Contact(string id)=>enemyMarkersByProfileId.TryGetValue(id,out var c)?c:null;
    public int Count=>enemyMarkers.Count;
    // The unchanged EFT raycast helper is outside this fixture's scope.
    private static bool IsEnemyReliablyVisibleForMarker(BotOwner owner,EnemyInfo enemy)=>enemy.IsVisible;
__MARKER_METHODS__
}

public static partial class CombatChecks {
    private static void TestSainEnemyMarkers(){
        var bot=Spawn("marker");
        new SAINFollowerSoloCombatLayer(bot,74);new SAINFollowerSquadCombatLayer(bot,75);Tick();
        bot.Memory.HaveEnemy=true;bot.Memory.GoalEnemy.ProfileId="staleEft";
        bot.Memory.GoalEnemy.CurrPosition=new Vector3(999,0,0);
        var target=new Enemy{EnemyPosition=new Vector3(90,0,0)};
        target.EnemyPlayer.ProfileId="tracked";
        target.KnownPlaces.LastKnownPosition=new Vector3(25,0,0);
        bot.Sain.GoalEnemy=target;
        var markers=new MarkerHarness(bot);markers.Update();
        Check(markers.Count==1&&markers.Contact("staleEft")==null,"ready SainMan reports native selection instead of a stale EFT goal");
        Check(markers.Contact("tracked").WorldPosition.x==25&&!markers.Contact("tracked").IsVisible,"hidden SainMan contact uses last known position");
        target.EnemyPosition=new Vector3(150,0,0);Time.time+=6;markers.Update();
        Check(markers.Contact("tracked").WorldPosition.x==25,"hidden enemy movement and five-second refresh do not reveal its live position");
        target.KnownPlaces.LastKnownPosition=new Vector3(40,0,0);markers.Update();
        Check(markers.Contact("tracked").WorldPosition.x==40,"new native knowledge updates the marker without waiting for a live-position refresh");
        target.IsVisible=target.CanShoot=true;target.TimeSinceSeen=0;markers.Update();
        Check(markers.Contact("tracked").WorldPosition.x==150&&markers.Contact("tracked").IsVisible,"fresh native sight tracks the visible target");
        target.TimeSinceSeen=1;markers.Update();
        Check(markers.Contact("tracked").WorldPosition.x==40&&!markers.Contact("tracked").IsVisible,"stale native sight flags fall back to known position immediately");
        target.IsVisible=false;target.EnemyKnown=false;markers.Update();
        Check(markers.Count==0&&bot.Memory.GoalEnemy.ProfileId=="staleEft","forgotten native contact removes marker without changing EFT memory");
        target.EnemyKnown=true;target.KnownPlaces.LastKnownPosition=null;markers.Update();
        Check(markers.Count==0,"cleared native known place does not fall back to a live enemy position");
        target.KnownPlaces.LastKnownPosition=new Vector3(40,0,0);bot.Sain.GoalEnemy=null;markers.Update();
        Check(markers.Count==0,"released native selection does not revive the EFT goal marker");
        bot.Sain.GoalEnemy=target;target.EnemyPlayer.HealthController.IsAlive=false;markers.Update();
        Check(markers.Count==0,"dead native contact cannot keep a live marker active");
        target.EnemyPlayer.HealthController.IsAlive=true;
        target.KnownPlaces.LastKnownPosition=new Vector3(float.NaN,0,0);markers.Update();
        Check(markers.Count==0,"invalid native coordinates are not displayed");
        target.KnownPlaces.LastKnownPosition=new Vector3(40,0,0);

        var other=Spawn("markerOther");new SAINFollowerSoloCombatLayer(other,74);new SAINFollowerSquadCombatLayer(other,75);Tick();
        var otherTarget=new Enemy{EnemyPosition=new Vector3(160,0,0),IsVisible=true,CanShoot=true,TimeSinceSeen=0};
        otherTarget.EnemyPlayer.ProfileId="tracked";other.Sain.GoalEnemy=otherTarget;
        var shared=new MarkerHarness(bot,other);shared.Update();
        Check(shared.Count==1&&shared.Contact("tracked").IsVisible&&shared.Contact("tracked").WorldPosition.x==160,"shared marker favors a follower with current native sight");
        target.EnemyKnown=false;shared.Update();
        Check(shared.Count==1,"one follower forgetting does not remove another follower's report");
        other.Sain.GoalEnemy=null;shared.Update();
        Check(shared.Count==0,"marker disappears once no follower reports the native contact");

        var core=Spawn("markerCore",pitTeam.Components.FollowerCombatTactic.Balanced);
        core.Memory.HaveEnemy=true;core.Memory.GoalEnemy.CurrPosition=new Vector3(12,0,0);
        var legacy=new MarkerHarness(core);legacy.Update();
        Check(legacy.Count==1&&legacy.Contact("eftEnemy").WorldPosition.x==12,"other tactics retain EFT marker positions");
        core.Memory.GoalEnemy.CurrPosition=new Vector3(18,0,0);Time.time+=1;legacy.Update();
        Check(legacy.Contact("eftEnemy").WorldPosition.x==12,"core hidden marker retains its existing five-second refresh");
        Time.time+=5;legacy.Update();Check(legacy.Contact("eftEnemy").WorldPosition.x==18,"core hidden marker refresh still works");
        bot.Follower.CombatTactic=pitTeam.Components.FollowerCombatTactic.Balanced;markers.Update();
        Check(markers.Contact("staleEft")!=null,"opting out restores core marker selection");
        var pending=Spawn("markerUnready");pending.Memory.HaveEnemy=true;
        var fallback=new MarkerHarness(pending);fallback.Update();
        Check(fallback.Contact("eftEnemy")!=null,"unready addon retains core marker fallback");
    }
}
