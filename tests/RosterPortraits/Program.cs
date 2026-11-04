using System.Collections;
using Comfort.Common;
using EFT.UI;
using PlayerIcons;
using UnityEngine;
using UnityEngine.UI;
#pragma warning disable CS0649 // Controlled native stand-ins retain their default fields.

partial class PortraitHarness
{
    readonly Queue<(string,PlayerIconImage,RectTransform,int)> _portraitQueue=new();
    object _portraitQueueCoroutine=new();
    int rosterBuildVersion=1;
    bool forceRosterRefreshOnNextInject;int squadVisitVersion=1;
    readonly HashSet<string> pendingTileRefreshAccountIds=new();
    readonly RectTransform shell=(RectTransform)new GameObject("shell",typeof(RectTransform)).transform;
    readonly RectTransform rosterGridRoot=(RectTransform)new GameObject("grid",typeof(RectTransform)).transform;
    readonly RectTransform rosterScrollRoot=(RectTransform)new GameObject("scroll",typeof(RectTransform)).transform;
    RectTransform pinnedRosterRoot;
    readonly Dictionary<string,SquadRosterEntry> entries=new();
    public PortraitHarness(){rosterScrollRoot.SetParent(shell,false);rosterGridRoot.SetParent(rosterScrollRoot,false);CreatePinnedRosterArea(shell);}
    readonly GameObject rosterPanel=new(),modePanel=new(),settingsPanel=new();
    public int Rebuilds;
    public readonly List<(string id,PlayerIconImage icon)> Tiles=new();
    void SetPortraitLoading(string id,bool loading) => Tiles.First(t=>t.id==id).icon._progress.SetActive(loading);
    public int RemoveButtons;
    public readonly Dictionary<string,bool> GroupBadges=new();
    public PlayerIconImage Add(string id,bool pinned=false)=>AddToRoot(pinned?pinnedRosterRoot:rosterGridRoot,new(){AccountId=id,PinnedRoster=pinned});
    PlayerIconImage AddToRoot(Transform root,SquadRosterEntry entry){entries[entry.AccountId]=entry;var icon=new PlayerIconImage();Tiles.Add((entry.AccountId,icon));var tile=new GameObject("pitFireTeam_RosterTile_"+entry.AccountId){Icon=icon};tile.transform.SetParent(root,false);_portraitQueue.Enqueue((entry.AccountId,icon,new(),rosterBuildVersion));UpdateRosterSections(entries.Values.Count(e=>e.PinnedRoster));return icon;}
    public IEnumerator Run(){var runner=DrainPortraitQueueCoroutine();_portraitQueueCoroutine=runner;return runner;}
    public bool Released=>_portraitQueueCoroutine==null;
    public void ReplaceScreen()=>rosterBuildVersion++;
    public void Hide()=>HideSquadPanels();
    public bool Dirty=>forceRosterRefreshOnNextInject;
    public bool Refresh(string id)=>RefreshRosterTile(entries[id]);
    public bool IsPinned(string id)=>ReferenceEquals(FindRosterTile(id)?.parent,pinnedRosterRoot);
    public bool PinnedVisible=>pinnedRosterRoot.gameObject.activeSelf;
    public float PinnedWidth=>pinnedRosterRoot.sizeDelta.x;
    public bool Centered=>pinnedRosterRoot.anchorMin.x==0.5f&&pinnedRosterRoot.anchorMax.x==0.5f&&pinnedRosterRoot.pivot.x==0.5f;
    public bool OutsideScroll=>ReferenceEquals(pinnedRosterRoot.parent,shell);
    public float ScrollTop=>-rosterScrollRoot.offsetMax.y;
    public void ConfigureSections(int count)=>UpdateRosterSections(count);
    public int AllTileCount=>RosterTiles().Count();
    public void CheckRemoval(string id)=>ProbeRemoveButton(null,entries[id]);
    public void UpdateGroups(params string[] ids)=>ApplyGroupBadgeVisibility(new(ids));
    void UpdateGroupBadge(string id,bool shown)=>GroupBadges[id]=shown;
    class SquadRosterEntry {public string AccountId;public bool PinnedRoster;}
    void RequestRosterRefreshOnNextInject(){forceRosterRefreshOnNextInject=true;pendingTileRefreshAccountIds.Clear();}
    void ClosePortraitContextMenu(){}void CloseRemoveConfirmOverlay(){}
    void SyncGroupBadgesFromKnownState(){}void NotifySquadScreenRefreshed(){}
    bool SettingsEntriesNeedRebuildForCurrentContext()=>false;
    void RefreshRosterTiles(List<string> ids){foreach(var id in ids)Refresh(id);}
    IEnumerator RebuildAfterTransitionCoroutine(bool roster,bool settings,int visit){if(roster)RebuildRosterTiles();yield break;}
    void RebuildRosterTiles(){CancelPortraitQueue();var old=entries.Values.ToArray();Tiles.Clear();rosterGridRoot.Children.Clear();pinnedRosterRoot.Children.Clear();rosterBuildVersion++;Rebuilds++;foreach(var entry in old)Add(entry.AccountId,entry.PinnedRoster);}
    GameObject CreateRosterTile(Transform root,SquadRosterEntry entry,int build){AddToRoot(root,entry);return root.Find("pitFireTeam_RosterTile_"+entry.AccountId).gameObject;}
    void Destroy(GameObject tile){tile.Icon._pmcPreview=null;}
}
static class Program
{
    static int checks;
    static void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
    static void Drive(IEnumerator runner,Action tick=null)
    {int count=0;while(runner.MoveNext()){Time.unscaledTime+=1;tick?.Invoke();if(++count>250)throw new Exception("Unbounded portrait queue");}}
    static PortraitHarness Setup()
    {Time.unscaledTime=0;pitFireTeam.Log.Messages.Clear();ItemUiContext.Instance=new();Singleton<EFT.PlayerIcons.PlayerIconCreator>.Instance=new();return new();}
    static void Main()
    {
        foreach(string failure in new[]{"sync","fault","canceled","invalid","missing","profileTimeout","renderThrow","renderTimeout"})
        {
            var h=Setup();var bad=h.Add("bad");var good=h.Add("good");
            ItemUiContext.Instance.Session.Fetch=id=>id=="good"?Task.FromResult(new Response()):failure switch {
                "sync"=>throw new InvalidOperationException("backend"),"fault"=>Task.FromException<Response>(new Exception("HTTP")),
                "canceled"=>Task.FromCanceled<Response>(new CancellationToken(true)),"invalid"=>Task.FromResult(new Response{Failed=true}),
                "missing"=>Task.FromResult(new Response{Value=null}),"profileTimeout"=>new TaskCompletionSource<Response>().Task,
                _=>Task.FromResult(new Response())};
            int renders=0;var creator=Singleton<EFT.PlayerIcons.PlayerIconCreator>.Instance;
            creator.Render=request=>++renders==1&&failure=="renderThrow"?throw new Exception("renderer"):
                new ItemIcon{Sprite=renders==1&&failure=="renderTimeout"?null:new Sprite()};
            Drive(h.Run());
            Check(good.Applied==1,"later portrait survives "+failure);
            Check(bad.Applied==0&&!bad._progress.Active,"failed tile stops loading without applying: "+failure);
            Check(h.Released,"queue ownership releases: "+failure);
            Check(pitFireTeam.Log.Messages.Count==1,"failure is diagnosed once: "+failure);
        }
        {
            var h=Setup();var icon=h.Add("slow");var creator=Singleton<EFT.PlayerIcons.PlayerIconCreator>.Instance;
            var pending=new ItemIcon();creator.Render=_=>pending;int frames=0;
            Drive(h.Run(),()=>{if(frames<4)Check(icon.Applied==0,"pending render has no tile callback");if(++frames==4)pending.Sprite=new Sprite();});
            Check(icon.Applied==1&&creator.Calls==2,"completed native cached icon is applied once");
        }
        foreach(string stage in new[]{"profile","render"})
        {
            var h=Setup();var icon=h.Add("stale");var completion=new TaskCompletionSource<Response>();
            if(stage=="profile")ItemUiContext.Instance.Session.Fetch=_=>completion.Task;
            var pending=new ItemIcon();Singleton<EFT.PlayerIcons.PlayerIconCreator>.Instance.Render=_=>pending;
            var runner=h.Run();Check(runner.MoveNext(),"stale test reaches pending "+stage);h.ReplaceScreen();
            completion.TrySetResult(new Response());pending.Sprite=new Sprite();Drive(runner);
            Check(icon.Applied==0&&h.Released,"replaced screen cannot receive late "+stage);
        }
        {
            var h=Setup();h.Add("close");ItemUiContext.Instance.Session.Fetch=_=>new TaskCompletionSource<Response>().Task;
            var runner=h.Run();Check(runner.MoveNext(),"queue enters pending fetch");((IDisposable)runner).Dispose();
            Check(h.Released,"coroutine cancellation releases queue ownership");
        }
        {
            var h=Setup();var initial=h.Add("Lee");h.Add("Knight");h.Add("Pipe");
            var completion=new TaskCompletionSource<Response>();ItemUiContext.Instance.Session.Fetch=_=>completion.Task;
            var runner=h.Run();Check(runner.MoveNext(),"opening visit starts portrait work");
            h.Hide();Check(h.Dirty&&h.Released,"onboarding cancellation requests replacement work and releases queue");
            h.Inject();Check(h.Rebuilds==1&&!h.Dirty,"real injection decision rebuilds existing tiles after onboarding cancellation");
            ItemUiContext.Instance.Session.Fetch=_=>Task.FromResult(new Response());Drive(h.Run());
            Check(h.Tiles.Count==3&&h.Tiles.All(t=>t.icon.Applied==1),"dequeued Lee and queued Goons all resume after temporary onboarding gate");
            Check(initial.Applied==0,"canceled tile cannot receive the rebuilt portrait");
        }
        {
            var h=Setup();var lee=h.Add("Lee");var knight=h.Add("Knight");h.Add("Pipe");
            var completion=new TaskCompletionSource<Response>();ItemUiContext.Instance.Session.Fetch=id=>id=="Lee"?completion.Task:Task.FromResult(new Response());
            var runner=h.Run();Check(runner.MoveNext(),"partial refresh test reaches pending unrelated portrait");
            Check(h.Refresh("Knight"),"production partial refresh replaces selected tile");
            completion.SetResult(new Response());Drive(runner);
            Check(lee.Applied==1,"partial Knight refresh cannot invalidate pending Lee");
            Check(knight.Applied==0,"destroyed Knight tile cannot be painted");
            Check(h.Tiles.Where(t=>t.id!="Knight"||!ReferenceEquals(t.icon,knight)).All(t=>t.icon.Applied==1),
                "unrelated pending tiles and replacement Knight all finish");
        }
        for(int goons=0;goons<=3;goons++)
        foreach(int ordinary in new[]{0,1,5,12})
        {
            var h=Setup();for(int i=0;i<goons;i++)h.Add("goon-"+i,true);for(int i=0;i<ordinary;i++)h.Add("member-"+i);
            // Configure the empty case too; production full rebuild always applies sections.
            h.ConfigureSections(goons);
            Check(h.PinnedVisible==(goons>0),"fixed row appears only when at least one Goon is owned");
            Check(h.Centered&&h.OutsideScroll,"production fixed row is centred outside scroll viewport");
            Check(h.PinnedWidth==(goons==0?0:goons*190+(goons-1)*18),"one/two/three fixed cards have centred row width");
            Check(h.ScrollTop==(goons==0?44:44+214+18),"scroll area begins beneath pinned row or reclaims full height");
            Check(h.AllTileCount==goons+ordinary,"shared tile enumeration includes both sections");
            if(goons>0){h.CheckRemoval("goon-0");Check(h.RemoveButtons==0,"pinned Goon has no remove button");Check(h.Refresh("goon-0")&&h.IsPinned("goon-0"),"partial refresh keeps Goon outside scrolling roster");}
            if(ordinary>0){h.CheckRemoval("member-0");Check(h.RemoveButtons==1,"ordinary member retains remove button");}
            h.UpdateGroups(goons>0?"goon-0":"member-0");
            Check(h.GroupBadges.Count==goons+ordinary,"live group badges address pinned and scrolling members");
            Drive(h.Run());Check(h.Tiles.Where(t=>t.icon._pmcPreview!=null).All(t=>t.icon.Applied==1),"both sections retain portrait loading");
            if(goons>0&&ordinary==0){h.Inject();Check(h.Rebuilds==0,"a Goon-only roster is populated and reopens without unnecessary rebuilding");}
        }
        Console.WriteLine($"PASS: {checks} production roster sections/removal/badge/portrait lifecycle checks; no Unity rendering simulation.");
    }
}
class Response {public bool Failed;public object Value=new();}
class Session {public Func<string,Task<Response>> Fetch=_=>Task.FromResult(new Response());public Task<Response> GetOtherPlayerProfile(string id)=>Fetch(id);}
class ItemIcon {public Sprite Sprite;}
class PlayerIconRequest {public int hash=123;public float progress;public PlayerIconRequest(object equipment,object customization){}}
static class pitFireTeam {public static Logger Log=new();public static CoroutineHost Instance=new();}
class CoroutineHost {public object StartCoroutine(IEnumerator work)=>work;public void StopCoroutine(object work){if(work is IDisposable disposable)disposable.Dispose();}}
class Logger {public List<string> Messages=new();public void LogError(object message)=>Messages.Add(message.ToString());public void LogWarning(object message)=>Messages.Add(message.ToString());}
namespace UnityEngine {enum TextAnchor{UpperCenter}struct Vector2(float x,float y){public float x=x,y=y;}static class Mathf{public static int Max(int a,int b)=>Math.Max(a,b);}class Transform {public string name=>gameObject.name;public Transform parent;public GameObject gameObject;public Dictionary<string,Transform> Children=new();public int childCount=>Children.Count;public Transform GetChild(int i)=>Children.Values.ElementAt(i);public Transform Find(string name)=>Children.GetValueOrDefault(name);public void SetParent(Transform root,bool world){parent=root;root.Children[name]=this;}public T GetComponent<T>()where T:class=>gameObject.GetComponent<T>();public int GetSiblingIndex()=>0;public void SetSiblingIndex(int index){}}class RectTransform:Transform {public Vector2 anchorMin,anchorMax,pivot,anchoredPosition,sizeDelta,offsetMax;} class Sprite {} class Image {public Sprite sprite;} class GameObject {public GameObject(string name="",params Type[] types){this.name=name;transform=types.Contains(typeof(RectTransform))?new RectTransform{gameObject=this}:new Transform{gameObject=this};foreach(var type in types.Where(t=>t!=typeof(RectTransform)))components[type]=Activator.CreateInstance(type);}public string name;readonly Dictionary<Type,object> components=new();public T GetComponent<T>()where T:class=>transform as T??components.GetValueOrDefault(typeof(T)) as T;public Transform transform;public PlayerIconImage Icon;public bool Active=true;public bool activeSelf=>Active;public void SetActive(bool active)=>Active=active;} static class Time {public static float unscaledTime;} class WaitForSeconds {public WaitForSeconds(float seconds){}}}
namespace UnityEngine.UI {class GridLayoutGroup{public Vector2 cellSize,spacing;public enum Constraint{FixedColumnCount}public Constraint constraint;public TextAnchor childAlignment;public int constraintCount;}}
namespace Comfort.Common {static class Singleton<T> {public static T Instance;}}
namespace EFT.UI {class ItemUiContext {public static ItemUiContext Instance=new();public Session Session=new();}}
namespace EFT {class OtherPlayerProfile {public OtherPlayerProfile(object source){}public object Equipment=new(),Customization=new();}}
namespace EFT.PlayerIcons {class PlayerIconCreator {public bool _containerIsBusy;public Queue<int> _renderQueue=new();public int Calls;public Func<PlayerIconRequest,ItemIcon> Render=_=>new(){Sprite=new()};readonly Dictionary<int,ItemIcon> cache=new();public ItemIcon GetIcon(PlayerIconRequest request){Calls++;if(cache.TryGetValue(request.hash,out var icon)&&icon.Sprite!=null)return icon;icon=Render(request);if(icon.Sprite!=null)cache[request.hash]=icon;return icon;}}}
namespace PlayerIcons {class PlayerIconImage {public UnityEngine.Image _pmcPreview=new();public GameObject _progress=new();public int Applied;public void SetPresetIcon(PlayerIconRequest request){var icon=Singleton<EFT.PlayerIcons.PlayerIconCreator>.Instance.GetIcon(request);if(icon.Sprite==null)throw new Exception("Subscribed to pending native icon");Applied++;_pmcPreview.sprite=icon.Sprite;_progress.SetActive(false);}}}
