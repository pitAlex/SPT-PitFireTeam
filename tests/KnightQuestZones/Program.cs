using EFT;
using EFT.Interactive;
using EFT.Quests;
using pitTeam.GoingRogue.Quests;
using pitTeam.Modules;
using UnityEngine;

int checks=0;
void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
GameWorld World(string map="shoreline") => new(){MainPlayer=new(){IsYourPlayer=true,Location=map,
    Profile=new(){QuestsData=[new(){Id=KnightQuestPolicy.OffLimits,Status=EQuestStatus.Started}]}}};
GameObject Owned() => GameObject.All.SingleOrDefault(o=>o.name=="GoingRogue."+KnightQuestPolicy.PierZone);
var world=World();
KnightQuestZones.Ensure(world);
var zone=Owned();var trigger=zone.Components.OfType<QuestTrigger>().Single();var box=zone.Components.OfType<BoxCollider>().Single();
Check(trigger.Id=="pier_zone01" && zone.active && box.isTrigger,"original zone ID uses an active native quest trigger and trigger collider");
Check(zone.layer==LayerMask.Layer && zone.transform.parent==world.transform,"zone belongs to the raid world and native Triggers layer");
Check(zone.transform.position==new Vector3(-324.9464f,-62.07625f,520.8833f),"exact legacy world position retained");
Check(zone.transform.localScale==new Vector3(55f,17.75f,112f) && box.size==Vector3.one && box.center==Vector3.zero,"original full box dimensions are preserved without a half-size or center offset");
Check(zone.transform.rotation==new Quaternion(3.694647E-08f,0.4226183f,-7.923195E-08f,-0.9063078f),"legacy quaternion is used directly, without Euler reinterpretation");
KnightQuestZones.Ensure(world);Check(ReferenceEquals(zone,Owned()),"repeated setup cannot create duplicate colliders");
KnightQuestZones.Reset(World());Check(ReferenceEquals(zone,Owned()) && zone.active,"disposal of another world cannot remove the current zone");
KnightQuestZones.Reset(world);Check(Owned()==null && !zone.active,"owning-world cleanup disables and destroys the volume");
KnightQuestZones.Reset();Check(Owned()==null,"repeated cleanup is harmless");
foreach(var map in new[]{"woods","bigmap","TarkovStreets",""}){KnightQuestZones.Ensure(World(map));Check(Owned()==null,"other map cannot create a pier zone: "+map);}
world=World();GameplayModeRuntime.IsAllegiance=false;KnightQuestZones.Ensure(world);Check(Owned()==null,"other mode cannot create the zone");GameplayModeRuntime.IsAllegiance=true;
world.MainPlayer.Side=EPlayerSide.Savage;KnightQuestZones.Ensure(world);Check(Owned()==null,"player Scav raid cannot create the quest zone");world.MainPlayer.Side=EPlayerSide.Usec;
world.MainPlayer.IsYourPlayer=false;KnightQuestZones.Ensure(world);Check(Owned()==null,"non-local world player cannot create a quest zone");world.MainPlayer.IsYourPlayer=true;
world.MainPlayer.Profile.QuestsData.Clear();KnightQuestZones.Ensure(world);Check(Owned()==null,"no quest cannot create a zone");
foreach(var state in new[]{EQuestStatus.AvailableForStart,EQuestStatus.Success,EQuestStatus.Fail})
{world.MainPlayer.Profile.QuestsData=[new(){Id=KnightQuestPolicy.OffLimits,Status=state}];KnightQuestZones.Ensure(world);Check(Owned()==null,"inactive quest cannot create zone: "+state);}
world=World("SHORELINE");world.MainPlayer.Profile.QuestsData[0].Status=EQuestStatus.AvailableForFinish;
KnightQuestZones.Ensure(world);Check(Owned()!=null,"ready-for-hand-in quest and case-insensitive map remain supported");KnightQuestZones.Reset();
var external=new GameObject("another-mod-zone");external.AddComponent<QuestTrigger>().SetId(KnightQuestPolicy.PierZone);
KnightQuestZones.Ensure(World());Check(Owned()==null,"existing native zone is reused rather than duplicated");KnightQuestZones.Reset();Check(GameObject.All.Contains(external),"addon cleanup preserves another owner's zone");UnityEngine.Object.Destroy(external);
external=new GameObject("disabled-external-zone");external.AddComponent<QuestTrigger>().SetId(KnightQuestPolicy.PierZone);external.Components.OfType<QuestTrigger>().Single().isActiveAndEnabled=false;
KnightQuestZones.Ensure(World());Check(Owned()!=null,"disabled foreign trigger cannot suppress a working pier zone");KnightQuestZones.Reset();Check(GameObject.All.Contains(external),"disabled foreign trigger remains owned by its creator");UnityEngine.Object.Destroy(external);
world=World();KnightQuestZones.Ensure(world);zone=Owned();var next=World();KnightQuestZones.Ensure(next);
Check(!ReferenceEquals(zone,Owned()) && !zone.active && Owned().transform.parent==next.transform,"a new raid replaces only the old owned zone");KnightQuestZones.Reset();
LayerMask.Layer=-1;bool rejected=false;try{KnightQuestZones.Ensure(World());}catch(InvalidOperationException){rejected=true;}Check(rejected && Owned()==null,"missing native layer cannot produce an active incorrect collider");LayerMask.Layer=23;
Console.WriteLine($"PASS: {checks} production pier-zone geometry/lifetime/admission checks. Unity objects are host doubles; native physics contacts require EFT qualification.");

namespace UnityEngine
{
    public class Object
    {
        public static T[] FindObjectsOfType<T>() => GameObject.All.SelectMany(o=>o.Components).OfType<T>().ToArray();
        public static void Destroy(GameObject value){value.active=false;GameObject.All.Remove(value);}
    }
    public class GameObject:Object
    {
        public static List<GameObject> All=[];public List<object> Components=[];public string name;public bool active=true;public int layer;public Transform transform=new();
        public GameObject(string name){this.name=name;All.Add(this);}public void SetActive(bool value)=>active=value;
        public T AddComponent<T>() where T:new(){var value=new T();Components.Add(value);return value;}
    }
    public class Transform {public Transform parent;public Vector3 position,localScale;public Quaternion rotation;public void SetParent(Transform value,bool stays)=>parent=value;}
    public record struct Vector3(float x,float y,float z){public static Vector3 zero=>new(0,0,0);public static Vector3 one=>new(1,1,1);}
    public record struct Quaternion(float x,float y,float z,float w);
    public class BoxCollider {public Vector3 center,size;public bool isTrigger;}
    public static class LayerMask {public static int Layer=23;public static int NameToLayer(string name)=>Layer;}
}
namespace EFT
{
    public enum EPlayerSide{Usec,Savage}
    public class GameWorld {public Player MainPlayer;public UnityEngine.Transform transform=new();public void OnGameStarted(){}public void Dispose(){}}
    public class Player {public bool IsYourPlayer;public EPlayerSide Side;public string Location;public Profile Profile;}
    public class Profile {public List<QuestData> QuestsData=[];}
    public class QuestData {public string Id;public Quests.EQuestStatus Status;}
}
namespace EFT.Quests {public enum EQuestStatus {AvailableForStart,Started,AvailableForFinish,Success,Fail}}
namespace EFT.Interactive {public class TriggerWithId {public string Id;public bool isActiveAndEnabled=true;public void SetId(string id)=>Id=id;}public class QuestTrigger:TriggerWithId{}}
namespace pitTeam.Modules {public static class GameplayModeRuntime{public static bool IsAllegiance=true;}public static class Logger {public static void LogInfo(string value){}public static void LogError(string value){}}}
namespace HarmonyLib {public class HarmonyPatch:Attribute {public HarmonyPatch(Type type,string name){}public HarmonyPatch(Type type,string name,Type[] args){}}}
