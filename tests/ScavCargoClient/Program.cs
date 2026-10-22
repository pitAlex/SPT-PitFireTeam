using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using pitTeam.Modules;
using UnityEngine;

namespace UnityEngine
{
    public struct Vector3(float x,float y,float z) { public float x=x,y=y,z=z; }
    public class Transform { public Vector3 position; }
    public class GameObject { public bool activeInHierarchy=true; }
    public class Collider { public bool enabled=true; public GameObject gameObject=new(); public Transform transform=new(); public Player Player; }
    public class BoxCollider : Collider { }
}

namespace EFT.InventoryLogic
{
    public class Item
    {
        public string Id;
        public int StackObjectsCount=1;
        public Item Parent;
        public List<Item> Children=new();
        public IEnumerable<Item> GetAllItems() => new[]{this}.Concat(Children.SelectMany(i=>i.GetAllItems()));
    }
    public class Inventory {public Item Equipment=new(){Id="equipment"};}
    public class Controller {public Inventory Inventory=new();}
}
namespace EFT
{
    public enum EPlayerSide {Usec,Bear,Savage}
    public enum ExitStatus {Survived,Runner,Killed,Transit,MissingInAction,Left}
    public class Health {public bool IsAlive=true;}
    public interface IPlayer {bool IsYourPlayer {get;} string ProfileId {get;}}
    public class Player : IPlayer {public bool IsYourPlayer {get;set;} public string ProfileId {get;set;} public EPlayerSide Side;public Health HealthController=new();public Controller InventoryController=new();public Collider Body=new();}
    public class BotOwner {public Player GetPlayer=new(){ProfileId="scav",Side=EPlayerSide.Savage};public bool IsDead;public string ProfileId=>GetPlayer.ProfileId;}
    public class GameWorld {public Player MainPlayer=new(){ProfileId="human",IsYourPlayer=true};public Player GetPlayerByCollider(Collider collider)=>collider.Player;}
    public class AbstractGame { }
    public static class Converters {public static JsonConverter[] ConvertersArray=new JsonConverter[0];}
    public static class Serialization {public static JsonConverter[] Converters=new JsonConverter[0];}
    public class ItemFactory
    {
        public object[] TreeToFlatItems(IEnumerable<Item> roots)=>roots.SelectMany(r=>r.GetAllItems()).Distinct().Select(i=>(object)new {_id=i.Id,_tpl="template",parentId=i.Parent?.Id,slotId="main"}).ToArray();
    }
}
namespace Comfort.Common {public static class Singleton<T> where T:new() {public static T Instance=new();}}
namespace EFT.Interactive
{
    public class ExfiltrationPoint
    {
        public class Options {public string Name;}
        public Options Settings=new(){Name="co-op"};public List<Player> Entered=new();public BoxCollider Trigger=new();
        public T GetComponent<T>() where T:class=>Trigger as T;
    }
}
namespace CommonAssets.Scripts.Game
{
    public class Point : EFT.Interactive.ExfiltrationPoint { }
    public class ExfiltrationController {public static ExfiltrationController Instance=new();public Point[] ExfiltrationPoints=[new()];}
}
namespace pitTeam.Modules
{
    public static class GameplayModeRuntime {public static bool IsAllegiance=true;}
    public static class AllegianceScavRelations {public static bool TrustBroken;}
    public static class AllegiancePmcFriendship {public static bool Selected=true;public static bool IsRegularScav(BotOwner bot)=>bot?.GetPlayer?.Side==EPlayerSide.Savage;public static bool IsSelectedFriendly(BotOwner bot,IPlayer leader)=>Selected;}
    public class Boss {public Player realPlayer;}
    public class Follower {public bool IsSquadMate;public Boss Boss;public Boss GetBoss()=>Boss;}
    public static class BossPlayers {public static Dictionary<string,Follower> Followers=new();public static Follower GetFollowerByProfileId(string id)=>Followers.GetValueOrDefault(id);}
    public static class TeammateBackpackInspection {public static Action Flush;public static void TrackBackpackItemChanges()=>Flush?.Invoke();}
    public static class InteractableObjects
    {
        public static HashSet<string> ProtectedIds = new();
        public static bool IsProtectedFollowerEquipment(Item item) => ProtectedIds.Contains(item.Id);
    }
    public static class Logger {public static void LogInfo(string message){}}
    public static class FollowerInsuranceRaidReports
    {
        public static string CurrentServerId="raid";
        public static List<JObject> Sent=new();
        public sealed class Report {public string Json;public void Send(string route){if(route!="/singleplayer/pitfireteam/scav-cargo")throw new Exception("Wrong route");Sent.Add(JObject.Parse(Json));}}
        public static Report Prepare(string json)=>new(){Json=json};
    }
}
public static class Program
{
    static int checks;
    static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
    static Player Human=>Comfort.Common.Singleton<GameWorld>.Instance.MainPlayer;
    static CommonAssets.Scripts.Game.Point Point=>CommonAssets.Scripts.Game.ExfiltrationController.Instance.ExfiltrationPoints[0];
    static BotOwner Setup(bool playerScav=false,bool allegiance=true,bool selected=true)
    {
        AllegianceScavCargo.Reset();InteractableObjects.ProtectedIds.Clear();GameplayModeRuntime.IsAllegiance=allegiance;AllegiancePmcFriendship.Selected=selected;AllegianceScavRelations.TrustBroken=false;
        FollowerInsuranceRaidReports.Sent.Clear();BossPlayers.Followers.Clear();TeammateBackpackInspection.Flush=null;
        Comfort.Common.Singleton<GameWorld>.Instance=new();CommonAssets.Scripts.Game.ExfiltrationController.Instance=new();
        Human.Side=playerScav ? EPlayerSide.Savage : EPlayerSide.Usec;
        var bot=new BotOwner();var bag=new Item {Id="native-backpack",Parent=bot.GetPlayer.InventoryController.Inventory.Equipment};
        var gun=new Item {Id="native-gun",Parent=bot.GetPlayer.InventoryController.Inventory.Equipment};
        bot.GetPlayer.InventoryController.Inventory.Equipment.Children.AddRange([bag,gun]);
        BossPlayers.Followers[bot.ProfileId]=new(){Boss=new(){realPlayer=Human}};Point.Entered.AddRange([Human,bot.GetPlayer]);
        Human.Body.Player=Human;bot.GetPlayer.Body.Player=bot.GetPlayer;
        AllegianceScavCargo.OnRecruited(bot,Human);return bot;
    }
    static Item Give(BotOwner bot,string id="given")
    {
        var bag=bot.GetPlayer.InventoryController.Inventory.Equipment.Children[0];var item=new Item {Id=id,Parent=bag};bag.Children.Add(item);AllegianceScavCargo.TrackGiven(bot,item);return item;
    }
    static void End(ExitStatus status=ExitStatus.Survived,string exit="co-op")=>AllegianceScavCargo.EndRaid(Human.ProfileId,status,exit);
    static string[] Returned()=>FollowerInsuranceRaidReports.Sent.Single()["items"].Select(i=>i["_id"].Value<string>()).ToArray();
    public static void Main()
    {
        var bot=Setup();var gift=Give(bot);var child=new Item {Id="given-child",Parent=gift};gift.Children.Add(child);AllegianceScavCargo.TrackGiven(bot,gift);End();
        Check(Returned().Order().SequenceEqual(new[]{"given","given-child"}.Order()),"Given container and children returned, Scav kit excluded");
        Check(gift.Parent.Id=="native-backpack" && gift.Children.Contains(child),"Capture never detaches live inventory");
        Check(FollowerInsuranceRaidReports.Sent.Single()["scavProfileIds"].Count()==1,"Actual partner evidence included");
        End();Check(FollowerInsuranceRaidReports.Sent.Count==1,"Repeat Stop cannot duplicate capture");
        bot=Setup();gift=Give(bot);gift.Parent.Children.Remove(gift);End();Check(Returned().Length==0,"Taken-back cargo is excluded");
        bot=Setup();gift=Give(bot);AllegianceScavCargo.RemoveGiven(bot,[gift.Id]);End();Check(Returned().Length==0,"Taking back ends provenance even if bot later loots same item");
        bot=Setup();var native=bot.GetPlayer.InventoryController.Inventory.Equipment.Children[1];AllegianceScavCargo.TrackGiven(bot,native);Give(bot,"loot");
        AllegianceScavCargo.RemoveGiven(bot,["loot"]);End();Check(Returned().Length==0,"Re-given starting kit and untracked loot excluded");
        bot=Setup();End();Check(FollowerInsuranceRaidReports.Sent.Count==1 && Returned().Length==0,"Partner with no cargo still reports an empty return without controlling Fence rewards");
        foreach(var exit in new[]{"ordinary","paid-co-op","co-op"}) {
            bot=Setup();Give(bot);Point.Settings.Name=exit;Point.Entered.Clear();bot.GetPlayer.Body.transform.position=new(100,0,0);End(exit:exit);
            Check(Returned().Single()=="given","Every exit captures a living current companion regardless of proximity or native Entered");
            Check(Point.Entered.Count==0,"Capture leaves native extraction admission untouched");
        }
        foreach(int unavailable in new[]{0,1,2,3}) {
            bot=Setup();Give(bot);
            if(unavailable==0)Point.Trigger=null;
            else if(unavailable==1)Point.Trigger.enabled=false;
            else if(unavailable==2)Point.Trigger.gameObject.activeInHierarchy=false;
            else CommonAssets.Scripts.Game.ExfiltrationController.Instance=null;
            End();Check(Returned().Single()=="given","Successful extraction does not depend on exit collider/controller availability");
        }
        foreach(var status in Enum.GetValues<ExitStatus>()) {
            bot=Setup();Give(bot);End(status);Check(FollowerInsuranceRaidReports.Sent.Count==(status is ExitStatus.Survived or ExitStatus.Runner or ExitStatus.Transit ? 1:0),"Living extraction or departing transit captures cargo");
            if(FollowerInsuranceRaidReports.Sent.Count>0)
                Check(FollowerInsuranceRaidReports.Sent.Single()["transit"].Value<bool>()==(status==ExitStatus.Transit),"Transit settlement is explicit and distinct from final extraction");
        }
        bot=Setup();Give(bot);End(ExitStatus.Transit,"transit");
        Check(Returned().Single()=="given","Departing Scav returns commanded cargo at transit, not his starting kit");
        End(ExitStatus.Transit,"transit");End();
        Check(FollowerInsuranceRaidReports.Sent.Count==1,"Transit and later duplicate Stop callbacks cannot settle the same Scav twice");
        AllegianceScavCargo.Reset();BossPlayers.Followers.Clear();End();
        Check(FollowerInsuranceRaidReports.Sent.Count==1,"Next map has no departing Scav or cargo to settle again");
        bot=Setup();End(ExitStatus.Transit,"transit");
        Check(FollowerInsuranceRaidReports.Sent.Count==1 && Returned().Length==0,"Transit with an empty-handed Scav still captures partner evidence for a Fence note");
        foreach(int unavailable in new[]{0,1,2,3,4,5}) {
            bot=Setup();Give(bot);
            if(unavailable==0)bot.IsDead=true;
            if(unavailable==1)Human.HealthController.IsAlive=false;
            if(unavailable==2)BossPlayers.Followers.Clear();
            if(unavailable==3)AllegianceScavRelations.TrustBroken=true;
            if(unavailable==4)BossPlayers.Followers[bot.ProfileId].IsSquadMate=true;
            End(ExitStatus.Transit,unavailable==5 ? "" : "transit");
            Check(FollowerInsuranceRaidReports.Sent.Count==0,"Transit preserves death, dismissal, betrayal, squad and exit-evidence exclusions");
        }
        bot=Setup();AllegianceScavCargo.Reset();bot.GetPlayer.Side=EPlayerSide.Bear;
        AllegianceScavCargo.OnRecruited(bot,Human);Give(bot);End(ExitStatus.Transit,"transit");
        Check(FollowerInsuranceRaidReports.Sent.Count==0,"PMC field recruits do not enter Scav transit settlement");
        bot=Setup();Give(bot);End(exit:"other");Check(Returned().Single()=="given","Native end result supplies the exit name without a second scene lookup");
        bot=Setup();Give(bot);End(exit:"");Check(FollowerInsuranceRaidReports.Sent.Count==0,"Missing exit evidence cannot return cargo");
        bot=Setup();Give(bot);AllegianceScavCargo.EndRaid("other-player",ExitStatus.Survived,"co-op");Check(FollowerInsuranceRaidReports.Sent.Count==0,"Another player cannot claim a companion");
        bot=Setup();Give(bot);BossPlayers.Followers[bot.ProfileId].Boss.realPlayer=new(){ProfileId="other-player"};End();Check(FollowerInsuranceRaidReports.Sent.Count==0,"Companion following another leader is excluded");
        bot=Setup();Give(bot);bot.GetPlayer.HealthController.IsAlive=false;End();Check(FollowerInsuranceRaidReports.Sent.Count==0,"Health-dead Scav is excluded even before IsDead is published");
        bot=Setup();AllegianceScavCargo.Reset();Give(bot);End();Check(FollowerInsuranceRaidReports.Sent.Count==0,"Unrecruited friendly does not count as a companion");
        bot=Setup();Give(bot);bot.IsDead=true;End();Check(FollowerInsuranceRaidReports.Sent.Count==0,"Dead Scav not an extraction partner");
        bot=Setup();Give(bot);Human.HealthController.IsAlive=false;End();Check(FollowerInsuranceRaidReports.Sent.Count==0,"Dead player never captures cargo");
        bot=Setup();Give(bot);BossPlayers.Followers.Clear();End();Check(FollowerInsuranceRaidReports.Sent.Count==0,"Dismissed Scav excluded");
        bot=Setup();Give(bot);AllegianceScavRelations.TrustBroken=true;End();Check(FollowerInsuranceRaidReports.Sent.Count==0,"Betrayal immediately blocks cargo and standing capture before deferred follower removal");
        bot=Setup();Give(bot);BossPlayers.Followers[bot.ProfileId].IsSquadMate=true;End();Check(FollowerInsuranceRaidReports.Sent.Count==0,"Saved squadmates cannot enter Scav cargo path");
        bot=Setup();Give(bot);GameplayModeRuntime.IsAllegiance=false;End();Check(FollowerInsuranceRaidReports.Sent.Count==0,"Guns for Hire excluded");
        bot=Setup();Give(bot);TeammateBackpackInspection.Flush=()=>Give(bot,"open-screen-cargo");End();Check(Returned().Contains("open-screen-cargo"),"Open inspection flushes before capture");
        bot=Setup();var stack=bot.GetPlayer.InventoryController.Inventory.Equipment.Children[1];stack.StackObjectsCount=60;
        AllegianceScavCargo.TrackStackChange(bot,stack,40);End();
        Check(Returned().Single()=="native-gun" && FollowerInsuranceRaidReports.Sent.Single()["items"][0]["upd"]["StackObjectsCount"].Value<int>()==20,"Merged gift returns only added units, excluding native share");
        bot=Setup();stack=bot.GetPlayer.InventoryController.Inventory.Equipment.Children[1];stack.StackObjectsCount=60;AllegianceScavCargo.TrackStackChange(bot,stack,40);stack.StackObjectsCount=45;End();
        Check(FollowerInsuranceRaidReports.Sent.Single()["items"][0]["upd"]["StackObjectsCount"].Value<int>()==5,"Consumed units do not return as phantom cargo");
        bot=Setup();gift=Give(bot);gift.StackObjectsCount=20;AllegianceScavCargo.TrackStackChange(bot,gift,1);gift.StackObjectsCount=15;AllegianceScavCargo.TrackStackChange(bot,gift,20);End();
        Check(FollowerInsuranceRaidReports.Sent.Single()["items"][0]["upd"]["StackObjectsCount"].Value<int>()==15,"Partial withdrawals lower given stack quantity");
        bot=Setup();var before=AllegianceScavCargo.CaptureCommandedLoot(bot);
        var loot=new Item{Id="commanded",Parent=bot.GetPlayer.InventoryController.Inventory.Equipment.Children[0]};loot.Parent.Children.Add(loot);
        child=new Item{Id="commanded-child",Parent=loot};loot.Children.Add(child);
        AllegianceScavCargo.TrackCommandedLoot(bot,loot,before);End();
        Check(Returned().Order().SequenceEqual(new[]{"commanded","commanded-child"}.Order()),"Command-collected item tree returns without a backpack inspection");
        bot=Setup();before=AllegianceScavCargo.CaptureCommandedLoot(bot);loot=new Item{Id="ambient",Parent=bot.GetPlayer.InventoryController.Inventory.Equipment.Children[0]};loot.Parent.Children.Add(loot);End();
        Check(Returned().Length==0,"Ambient loot not collected by player order remains excluded");
        bot=Setup();stack=bot.GetPlayer.InventoryController.Inventory.Equipment.Children[1];stack.StackObjectsCount=40;before=AllegianceScavCargo.CaptureCommandedLoot(bot);stack.StackObjectsCount=60;AllegianceScavCargo.TrackCommandedLoot(bot,stack,before);End();
        Check(FollowerInsuranceRaidReports.Sent.Single()["items"][0]["upd"]["StackObjectsCount"].Value<int>()==20,"Commanded stack merge excludes the native share");
        bot=Setup();
        var refillGift=Give(bot);refillGift.StackObjectsCount=25;AllegianceScavCargo.TrackStackChange(bot,refillGift,1);
        var refillTarget=bot.GetPlayer.InventoryController.Inventory.Equipment.Children[1];
        var refillBefore=AllegianceScavCargo.CaptureCommandedLoot(bot);
        refillGift.Parent.Children.Remove(refillGift);refillTarget.StackObjectsCount+=25;
        AllegianceScavCargo.TrackMagazineRefill(bot,refillTarget,refillBefore,true);End();
        Check(Returned().Length==1 && FollowerInsuranceRaidReports.Sent.Single()["items"][0]["upd"]["StackObjectsCount"].Value<int>()==25,
            "Donated rounds loaded later into a native magazine retain only their donated return quantity");
        bot=Setup();var donatedTarget=Give(bot);donatedTarget.StackObjectsCount=10;AllegianceScavCargo.TrackStackChange(bot,donatedTarget,1);
        refillBefore=AllegianceScavCargo.CaptureCommandedLoot(bot);donatedTarget.StackObjectsCount=20;
        AllegianceScavCargo.TrackMagazineRefill(bot,donatedTarget,refillBefore,false);End();
        Check(FollowerInsuranceRaidReports.Sent.Single()["items"][0]["upd"]["StackObjectsCount"].Value<int>()==10,
            "Native rounds loaded into a donated magazine never become extra return cargo");
        bot=Setup();refillBefore=AllegianceScavCargo.CaptureCommandedLoot(bot);
        var nativeChild=new Item{Id="native-refill-split",StackObjectsCount=15,Parent=bot.GetPlayer.InventoryController.Inventory.Equipment};nativeChild.Parent.Children.Add(nativeChild);
        AllegianceScavCargo.TrackMagazineRefill(bot,nativeChild,refillBefore,false);End();
        Check(Returned().Length==0,"Native refill split identities remain excluded from Fence returns");
        bot=Setup();before=AllegianceScavCargo.CaptureCommandedLoot(bot);native=bot.GetPlayer.InventoryController.Inventory.Equipment.Children[1];AllegianceScavCargo.TrackCommandedLoot(bot,native,before);End();
        Check(Returned().Length==0,"Repositioning the Scav's own kit is not new cargo");
        bot=Setup();before=AllegianceScavCargo.CaptureCommandedLoot(bot);loot=new Item{Id="failed-move"};AllegianceScavCargo.TrackCommandedLoot(bot,loot,before);End();
        Check(Returned().Length==0,"Failed transfer outside live inventory cannot become return cargo");
        bot=Setup();before=AllegianceScavCargo.CaptureCommandedLoot(bot);loot=new Item{Id="equipped-loot",Parent=bot.GetPlayer.InventoryController.Inventory.Equipment};loot.Parent.Children.Add(loot);AllegianceScavCargo.TrackCommandedLoot(bot,loot,before);loot.Parent.Children.Remove(loot);End();
        Check(Returned().Length==0,"Dropped commanded equipment cannot be duplicated in returns");
        bot=Setup();Give(bot,"first-cargo");var second=new BotOwner{GetPlayer=new(){ProfileId="second-scav",Side=EPlayerSide.Savage}};
        var secondBag=new Item{Id="second-bag",Parent=second.GetPlayer.InventoryController.Inventory.Equipment};secondBag.Parent.Children.Add(secondBag);
        BossPlayers.Followers[second.ProfileId]=new(){Boss=new(){realPlayer=Human}};AllegianceScavCargo.OnRecruited(second,Human);Give(second,"second-cargo");End();
        Check(FollowerInsuranceRaidReports.Sent.Count==1&&FollowerInsuranceRaidReports.Sent[0]["scavProfileIds"].Count()==2&&Returned().Length==2,"Multiple companions contribute one package");
        bot=Setup();gift=Give(bot,"donated-magazine");
        Check(AllegianceScavCargo.GetPureGivenIds(bot).Contains(gift.Id),"Pure donations are eligible split sources with preserved return ownership");
        before=AllegianceScavCargo.CaptureCommandedLoot(bot);
        var nativeSplit=new Item {Id="native-split",Parent=gift,StackObjectsCount=20};gift.Children.Add(nativeSplit);
        var givenSplit=new Item {Id="given-split",Parent=gift,StackObjectsCount=30};gift.Children.Add(givenSplit);
        AllegianceScavCargo.RegisterNonReturnableIds(bot,[nativeSplit.Id]);
        AllegianceScavCargo.TrackCommandedLoot(bot,bot.GetPlayer.InventoryController.Inventory.Equipment,before);End();
        Check(Returned().Contains(gift.Id) && Returned().Contains(givenSplit.Id) && !Returned().Contains(nativeSplit.Id),
            "A donated magazine returns player ammo but never newly split native Scav ammunition");
        bot=Setup();stack=bot.GetPlayer.InventoryController.Inventory.Equipment.Children[1];stack.StackObjectsCount=60;
        AllegianceScavCargo.TrackStackChange(bot,stack,40);
        Check(AllegianceScavCargo.HasMixedStack(bot,stack) && !AllegianceScavCargo.GetPureGivenIds(bot).Contains(stack.Id),
            "Mixed-origin stacks cannot be repartitioned as pure donations");
        Check(!AllegianceScavCargo.HasMixedStack(null,stack) && !AllegianceScavCargo.GetPureGivenIds(null).Any(),
            "Non-Scav recipients have no Scav split provenance");
        bot=Setup();gift=Give(bot,"borrowed-gun");
        AllegianceScavCargo.RegisterNonReturnableIds(bot,[gift.Id]);End();
        Check(Returned().Length==0 && bot.GetPlayer.InventoryController.Inventory.Equipment.GetAllItems().Contains(gift),
            "Borrowed weapon remains usable in raid but is not Fence cargo, including stale prior gift tracking");
        bot=Setup();gift=Give(bot,"borrowed-helmet");var validGift=Give(bot,"real-gift");
        InteractableObjects.ProtectedIds.Add(gift.Id);End();
        Check(Returned().SequenceEqual([validGift.Id]), "Final exact-ID protection excludes loaned gear without suppressing real gifts");
        foreach(bool allegiance in new[]{false,true})
        foreach(var status in Enum.GetValues<ExitStatus>()) {
            bot=Setup(playerScav:true,allegiance:allegiance,selected:false);
            gift=Give(bot,"scav-run-gift");
            child=new Item{Id="scav-run-child",Parent=gift};gift.Children.Add(child);AllegianceScavCargo.TrackGiven(bot,gift);
            End(status,"ordinary");
            bool success=status is ExitStatus.Survived or ExitStatus.Runner or ExitStatus.Transit;
            Check(FollowerInsuranceRaidReports.Sent.Count==(success ? 1:0),"Native Scav recruitment supports Scav raids in both modes without PMC encounter selection");
            if(success) {
                Check(Returned().Order().SequenceEqual(new[]{gift.Id,child.Id}.Order()),"Player Scav returns exact donated tree, never companion starting equipment");
                Check(gift.Parent.Id=="native-backpack","Scav extraction does not detach the companion's live equipment");
                End(status,"ordinary");Check(FollowerInsuranceRaidReports.Sent.Count==1,"Repeated player-Scav completion cannot duplicate capture");
            }
        }
        foreach(int unavailable in Enumerable.Range(0,7)) {
            bot=Setup(playerScav:true,allegiance:false,selected:false);Give(bot);
            if(unavailable==0)bot.IsDead=true;
            if(unavailable==1)Human.HealthController.IsAlive=false;
            if(unavailable==2)BossPlayers.Followers.Clear();
            if(unavailable==3)BossPlayers.Followers[bot.ProfileId].Boss.realPlayer=new(){ProfileId="other-player"};
            if(unavailable==4)BossPlayers.Followers[bot.ProfileId].IsSquadMate=true;
            if(unavailable==5)AllegianceScavRelations.TrustBroken=true;
            End(exit:unavailable==6 ? "" : "ordinary");
            Check(FollowerInsuranceRaidReports.Sent.Count==0,"Player-Scav mode retains death, dismissal, ownership, squad, betrayal and extraction evidence gates");
        }
        bot=Setup(playerScav:true,allegiance:false,selected:false);AllegianceScavCargo.Reset();BossPlayers.Followers.Clear();
        AllegianceScavCargo.OnRecruited(bot,Human);Give(bot);End();
        Check(FollowerInsuranceRaidReports.Sent.Count==0,"Nearby friendly Scav without actual follower registration cannot acquire return provenance");
        bot=Setup(selected:false);Give(bot);End();
        Check(FollowerInsuranceRaidReports.Sent.Count==0,"PMC still requires selected Allegiance recruitment");
        Console.WriteLine($"PASS: {checks} production Scav cargo client checks.");
    }
}
