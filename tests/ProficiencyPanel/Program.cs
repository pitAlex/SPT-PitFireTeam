using UnityEngine;
using UnityEngine.UI;
#pragma warning disable CS0649 // Native UI stand-ins are populated by the production builders.
partial class PanelHarness
{
    static bool ActiveProfileCombatSettingsLocked;
    static float ActiveProfileAggression=73,ActiveProfileSelfDetermination=42;
    static string ActiveProfileTactic="Marksman";
    static Proficiency ActiveProfileProficiency=new();
    static NumberSlider ProficiencyAggressionSlider,ProficiencyDeterminationSlider,ProficiencyVisionSlider,ProficiencyPrecisionSlider,ProficiencyReactionSlider;
    static GameObject ProficiencyOverlayRoot;
    static Field HideoutButtonField=new();
    public static int AggressionWrites,ProficiencyWrites;
    public static GameObject Build(Options options){Configure(options);ShowProficiencyOverlay(new(),new());return ProficiencyOverlayRoot;}
    public static void Reset()=>ResetProficiencyValues(new());
    public static bool Preserved=>ActiveProfileAggression==73&&ActiveProfileSelfDetermination==42;
    static void CloseProficiencyOverlay(){ProficiencyAggressionSlider=ProficiencyDeterminationSlider=null;ProficiencyOverlayRoot=null;}
    static string GetSocialUiText(string key)=>key;
    static string GetProficiencyEditorTitle(Profile profile)=>"Proficiency";
    static void AddLoadoutEditorHeaderDivider(Transform parent,string name,Vector2 a,Vector2 b,Vector2 c,Vector2 d){}
    static void CreateOverlayText(string name,Transform parent,Vector2 a,Vector2 b,TextAlignmentOptions alignment,string text,float size,Color color){}
    static Button CreateWindowCloseButton(Transform parent){var b=new GameObject("close",typeof(RectTransform),typeof(Button));b.transform.SetParent(parent,false);return b.GetComponent<Button>();}
    static DefaultUIButton CreateOverlayButton(DefaultUIButton template,Transform parent,Vector2 position,Vector2 size){var b=new GameObject("Reset",typeof(RectTransform),typeof(DefaultUIButton));b.transform.SetParent(parent,false);((RectTransform)b.transform).sizeDelta=size;return b.GetComponent<DefaultUIButton>();}
    static void HideProfileButtonIconContainer(DefaultUIButton button){}
    static void CreateAggressionRowContent(RectTransform row,Profile profile,float value,bool editable){ProficiencyAggressionSlider=new();}
    static NumberSlider CreateProficiencyModifierSlider(Transform parent,string rowName,string label,float y,float current,Action<float> update,string account,float min=0,float max=200){CreateProficiencySliderRow(parent,rowName,y);return new();}
    static void SetProficiencyAggressionForTactic(string tactic)=>ActiveProfileAggression=50;
    static void UpdateProficiencySlider(NumberSlider slider,float value){}
    static void ScheduleAggressionPersist(string id,int aggression)=>AggressionWrites++;
    static void ScheduleProficiencyPersist(string id,Proficiency proficiency)=>ProficiencyWrites++;
}
static class Program
{
    static int checks;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
    static void Main()
    {
        float? fullHeight=null,compactHeight=null;
        foreach(var flags in new[]{(false,false),(true,false),(false,true),(true,true)})
        {
            bool locked=flags.Item1||flags.Item2;
            var root=PanelHarness.Build(new(){ProficiencyOnly=flags.Item1,LockCombatSettings=flags.Item2});
            var panel=(RectTransform)root.transform.Find("pitFireTeam_ProficiencyPanel");
            var rows=panel.Children.OfType<RectTransform>().Where(t=>t.gameObject.name.EndsWith("Row")).OrderByDescending(t=>t.anchoredPosition.y).ToArray();
            Check(rows.Length==(locked?3:5),"visible row count follows both server capability flags");
            Check(rows[0].gameObject.name==(locked?"pitFireTeam_ProficiencyVisionRow":"pitFireTeam_ProficiencyAggressionRow"),"first visible row has no reserved hidden slots");
            Check(rows.All(t=>t.anchorMin.y==1&&t.anchorMax.y==1),"production rows anchor to panel top");
            float firstTop=-rows[0].anchoredPosition.y-rows[0].sizeDelta.y/2;
            Check(firstTop>=36&&firstTop-36<=16,"header-to-first-row gap is compact for either row set");
            for(int i=1;i<rows.Length;i++){
                float gap=rows[i-1].anchoredPosition.y-rows[i].anchoredPosition.y-(rows[i-1].sizeDelta.y+rows[i].sizeDelta.y)/2;
                Check(gap>=0&&gap<=10,"visible proficiency rows do not overlap or reserve excessive gaps");
            }
            var reset=(RectTransform)panel.Children.Single(t=>t.gameObject.name=="pitFireTeam_ProficiencyResetButton");
            float rowBottom=-rows.Last().anchoredPosition.y+rows.Last().sizeDelta.y/2;
            float resetTop=panel.sizeDelta.y-reset.anchoredPosition.y-reset.sizeDelta.y*reset.localScale.y;
            Check(resetTop>=rowBottom&&resetTop-rowBottom<=24,"last row clears Reset without a large footer gap");
            Check(reset.anchoredPosition.y<=16,"Reset retains a compact bottom inset");
            if(locked){compactHeight=panel.sizeDelta.y;PanelHarness.Reset();Check(PanelHarness.Preserved&&PanelHarness.AggressionWrites==0,"Reset preserves hidden Goon/Rogue combat values");}
            else fullHeight=panel.sizeDelta.y;
        }
        Check(compactHeight<fullHeight&&fullHeight<492,"both compact and complete overlays shrink from oversized legacy panel");
        Console.WriteLine($"PASS: {checks} production proficiency overlay capability, row geometry and locked Reset checks; no Unity text/render qualification.");
    }
}
class Options {public bool ProficiencyOnly,LockCombatSettings;}
class Profile {public string AccountId="id";}
class Proficiency {public float GetVisionPercent()=>140;public float GetPrecisionPercent()=>140;public float GetReactionPercent()=>140;public void SetVisionPercent(float v){}public void SetPrecisionPercent(float v){}public void SetReactionPercent(float v){}}
class FollowerProficiencyModifierValues {public const float DefaultPercent=100;}
class Field {public object GetValue(object screen)=>new GameObject("template",typeof(RectTransform),typeof(DefaultUIButton)).GetComponent<DefaultUIButton>();}
class OtherPlayerProfileScreen:Component {public OtherPlayerProfileScreen(){gameObject=new("screen",typeof(RectTransform));}}
class NumberSlider {public void UpdateValue(float a,bool b,float c,float d){}}
enum TextAlignmentOptions {MidlineLeft}
class ProfileOverlayHeaderDragHandle:Component {public RectTransform Target;}
class DefaultUIButton:Component {public Click OnClick=new();public void SetRawText(string text,int size){}}
class Click {public void AddListener(Action action){}public void RemoveAllListeners(){}}
namespace pitTeam.Shared {static class SelfDeterminationPolicy {public static float Normalize(float value)=>value;}}
namespace UnityEngine
{
    struct Vector2(float x,float y){public float x=x,y=y;public static Vector2 zero=>new(0,0);public static Vector2 one=>new(1,1);}
    struct Vector3(float x,float y,float z){public float x=x,y=y,z=z;public static Vector3 one=>new(1,1,1);public static Vector3 operator *(Vector3 v,float s)=>new(v.x*s,v.y*s,v.z*s);}
    struct Color {public Color(float r,float g,float b,float a){}}
    class Component {public GameObject gameObject;public string name {get=>gameObject.name;set=>gameObject.name=value;}public Transform transform=>gameObject.transform;}
    class Transform:Component {public readonly List<Transform> Children=new();public Vector3 localScale=Vector3.one;public void SetParent(Transform parent,bool world)=>parent.Children.Add(this);public void SetAsLastSibling(){}public Transform Find(string name)=>Children.FirstOrDefault(t=>t.gameObject.name==name);}
    class RectTransform:Transform {public Vector2 anchorMin,anchorMax,pivot,anchoredPosition,sizeDelta,offsetMin,offsetMax;}
    class GameObject {public string name;public Transform transform;readonly Dictionary<Type,Component> components=new();public GameObject(string name,params Type[] types){this.name=name;transform=types.Contains(typeof(RectTransform))?new RectTransform():new Transform();transform.gameObject=this;components[transform.GetType()]=transform;foreach(var type in types.Where(t=>t!=typeof(RectTransform)))AddComponent(type);}Component AddComponent(Type t){var c=(Component)Activator.CreateInstance(t);c.gameObject=this;components[t]=c;return c;}public T AddComponent<T>()where T:Component=>(T)AddComponent(typeof(T));public T GetComponent<T>()where T:Component=>components.TryGetValue(typeof(T),out var c)?(T)c:null;}
    static class Mathf {public static int RoundToInt(float v)=>(int)Math.Round(v);}
}
namespace UnityEngine.UI {class Image:Component {public Color color;public bool raycastTarget;}class Button:Component {public Click onClick=new();}}
