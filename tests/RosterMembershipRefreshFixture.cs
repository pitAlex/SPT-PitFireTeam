using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

internal partial class RosterFixture
{
    private sealed class Panel { internal bool activeInHierarchy=true; }
    private sealed class Entry { internal bool IsWounded; }
    private sealed class Tile { internal Entry Entry=new Entry(); }
    private sealed class RosterHealthState { public string Aid; public bool IsWounded; }
    private sealed class RosterHealthResponse { public int err; public string errmsg; public List<RosterHealthState> data; }
    private Panel rosterPanel=new Panel();
    private Dictionary<string,Tile> rosterHealthTiles=new Dictionary<string,Tile>(StringComparer.Ordinal);
    private bool rosterHealthRefreshBusy;
    private float nextRosterHealthRefresh;
    private int rosterBuildVersion,tints;
    private void ApplyPortraitHealthTint(Tile tile){tints++;}
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    public static void Main(){Run().GetAwaiter().GetResult();}
    private static async Task Run()
    {
        var owner=new RosterFixture();owner.rosterHealthTiles.Add("member",new Tile());
        RequestHandler.Response="{\"err\":0,\"data\":[{\"Aid\":\"member\",\"IsWounded\":true}]}";
        await owner.RefreshRosterHealthAsync();
        Check(owner.tints==1 && owner.rosterHealthTiles["member"].Entry.IsWounded,"unchanged membership updates health");
        Check(SocialNetworkClassPatch.Refreshes==0,"health changes do not reload roster/portraits");
        RequestHandler.Response="{\"err\":0,\"data\":[{\"Aid\":\"member\"},{\"Aid\":\"knight\"}]}";
        await owner.RefreshRosterHealthAsync();
        Check(SocialNetworkClassPatch.Refreshes==1 && owner.tints==1,"newly granted Knight uses existing full roster/friends refresh");
        owner.rosterHealthTiles.Add("knight",new Tile());
        RequestHandler.Response="{\"err\":0,\"data\":[{\"Aid\":\"knight\"},{\"Aid\":\"member\",\"IsWounded\":true}]}";
        await owner.RefreshRosterHealthAsync();Check(SocialNetworkClassPatch.Refreshes==1,"same members in different order do not refresh again");
        RequestHandler.Response="{\"err\":0,\"data\":[{\"Aid\":\"member\"},{\"Aid\":\"replacement\"}]}";
        await owner.RefreshRosterHealthAsync();Check(SocialNetworkClassPatch.Refreshes==2,"same count with changed identity refreshes");
        RequestHandler.Response="{\"err\":0,\"data\":[]}";
        await owner.RefreshRosterHealthAsync();Check(SocialNetworkClassPatch.Refreshes==3,"removals refresh membership");
        owner.rosterHealthTiles.Clear();
        await owner.RefreshRosterHealthAsync();Check(SocialNetworkClassPatch.Refreshes==3,"unchanged empty roster remains stable");
        RequestHandler.Response="{\"err\":0,\"data\":[{\"Aid\":\"knight\"}]}";
        await owner.RefreshRosterHealthAsync();Check(SocialNetworkClassPatch.Refreshes==4,"first unlocked companion refreshes an empty roster");
        RequestHandler.BeforeReturn=()=>owner.rosterBuildVersion++;
        await owner.RefreshRosterHealthAsync();Check(SocialNetworkClassPatch.Refreshes==4,"late response from earlier roster build ignored");RequestHandler.BeforeReturn=null;
        owner.rosterPanel.activeInHierarchy=false;
        await owner.RefreshRosterHealthAsync();Check(SocialNetworkClassPatch.Refreshes==4,"hidden-panel response ignored");owner.rosterPanel.activeInHierarchy=true;
        GameplayModeRuntime.IsSwitching=true;
        await owner.RefreshRosterHealthAsync();Check(SocialNetworkClassPatch.Refreshes==4,"mode-transition response ignored");GameplayModeRuntime.IsSwitching=false;
        RequestHandler.Response="{\"err\":1,\"errmsg\":\"fixture failure\"}";
        await owner.RefreshRosterHealthAsync();Check(SocialNetworkClassPatch.Refreshes==4 && owner.nextRosterHealthRefresh==Time.unscaledTime+30f && !owner.rosterHealthRefreshBusy,"failed poll retains membership and backs off");
        RequestHandler.Response="{\"err\":0,\"data\":[]}";owner.nextRosterHealthRefresh=0;
        int calls=RequestHandler.Calls;owner.UpdateRosterHealth();
        Check(SpinWait.SpinUntil(()=>!owner.rosterHealthRefreshBusy && RequestHandler.Calls==calls+1,2000),"visible empty roster still polls for its first grant");
        owner.rosterPanel.activeInHierarchy=false;calls=RequestHandler.Calls;owner.nextRosterHealthRefresh=0;owner.UpdateRosterHealth();Check(RequestHandler.Calls==calls,"hidden roster does not poll");
        Console.WriteLine("PASS: "+checks+" production roster health/membership boundary checks; controlled UI/HTTP hosts, not Unity rendering.");
    }
}
internal static class RequestHandler { internal static string Response;internal static int Calls;internal static Action BeforeReturn;internal static string GetJson(string path){Interlocked.Increment(ref Calls);BeforeReturn?.Invoke();return Response;} }
internal static class SocialNetworkClassPatch { internal static int Refreshes;internal static void RefreshFriendsListAndSquadRoster(){Refreshes++;} }
internal static class GameplayModeRuntime { internal static bool IsSwitching; }
internal static class Time { internal static float unscaledTime=100; }
internal static class pitFireTeam { internal static Log Log=new Log(); }
internal sealed class Log { internal void LogWarning(string message){} }
