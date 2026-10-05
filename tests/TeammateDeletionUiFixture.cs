using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EFT.UI;
using pitTeam.Patches;
using pitTeam.Modules;
using SPT.Common.Http;

namespace pitTeam
{
    static class pitFireTeam
    {
        public static readonly LogStub Log = new LogStub();
        public static string GetSocialUiText(string key) => key;
    }
    class LogStub { public void LogError(object value) { } }
}
namespace EFT.UI
{
    class ItemUiContext
    {
        public static readonly ItemUiContext Instance = new ItemUiContext();
        public readonly SessionStub Session = new SessionStub();
        public int Messages;
        public WindowStub ShowMessageWindow(string body, object acceptAction, object cancelAction, string caption, bool forceShow)
        { Messages++; return new WindowStub(); }
    }
    class SessionStub { public object Profile = new object(); public object RagFair = new object(); }
    class WindowStub { public Task WindowResult => Task.FromResult(true); }
}
static class LocalizationStub { public static string Localized(this string key) => key; }
namespace SPT.Common.Http
{
    static class RequestHandler
    {
        public static string Response;
        public static TaskCompletionSource<bool> Pending;
        public static int Requests;
        public static string PostJson(string route, string body)
        { Requests++; Pending?.Task.GetAwaiter().GetResult(); return Response; }
    }
}
namespace pitTeam.Modules
{
    static class AddTeammateCreationFlow
    {
        public static readonly List<string> Toasts = new List<string>();
        public static void ShowToast(string value) { Toasts.Add(value); }
    }
    static class TeammateDeletion
    {
        public static object InventoryController = new object();
        private static bool busy;
        private class DeleteResponse { public bool deleted; public JsonType.FlatItem[] playerStashItems; }
__DELETE_METHOD__
    }
}
namespace pitTeam.Patches
{
    class FriendlyTeammateBodyResponse<T> { public int err; public string errmsg; public T data; }
    static class SocialNetworkClassPatch
    {
        public static int Refreshes;
        public static bool Fail;
        public static void RefreshFriendsList(bool force) { Refreshes++; if (Fail) throw new Exception("friends failure"); }
    }
    static class OtherPlayerProfileScreenPatch
    {
        public static bool Fail;
        public static int Refreshes;
        public static JsonType.FlatItem[] LastItems;
        // The native converter in EFT's default set is required to preserve raw upd/location JSON.
        public static JsonConverter[] GetDefaultJsonConverters() => new JsonConverter[] { new EFT.UnparsedDataConverter() };
        public static void ApplyServerSavedPlayerStash(object profile, object controller, object ragfair, JsonType.FlatItem[] items)
        { LastItems = items; Refreshes++; if (Fail) throw new Exception("stash failure"); }
        private static Exception CreateLiveStashRefreshException(string text) => new InvalidOperationException(text);
__STASH_METHODS__
        public static JsonType.FlatItem[] Normalize(JsonType.FlatItem[] items, string root) => NormalizePlayerStashSnapshot(items, root);
        public static EFT.StashChangesResponse Delta(JsonType.FlatItem[] current, JsonType.FlatItem[] saved) =>
            BuildPlayerStashRefreshDelta(current, NormalizePlayerStashSnapshot(saved, current[0]._id.ToString()));
    }
}
namespace pitTeam.Components
{
    class SquadRosterEntry { public string AccountId = "123"; }
    class SquadControlMenuUi
    {
        private object removeConfirmOverlay = new object();
        private readonly TaskCompletionSource<bool> rebuilt = new TaskCompletionSource<bool>();
        public int Destroyed;
        public int Rebuilds;
        private void Destroy(object value) { Destroyed++; }
        private void RebuildRosterTiles() { Rebuilds++; rebuilt.SetResult(removeConfirmOverlay == null); }
__OVERLAY_METHODS__
        public Task<bool> Remove() { RemoveTeammate(new SquadRosterEntry()); return rebuilt.Task; }
        public void Close() => CloseRemoveConfirmOverlay();
    }
}
class Program
{
    private static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
    static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) => {
            string filename = new System.Reflection.AssemblyName(eventArgs.Name).Name + ".dll";
            foreach (string folder in new[] { "client/libs4.1", "client/libs", "client/libs4.1/spt4.1.0" }) {
                string path = Path.Combine(args[0], folder, filename);
                if (File.Exists(path)) return System.Reflection.Assembly.LoadFrom(path);
            }
            return null;
        };
        Run().GetAwaiter().GetResult();
        Console.WriteLine("PASS: " + checks + " deletion UI/stash checks. Unity click verification remains required.");
        return 0;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task Run()
    {
        string root = "6613e1cf291a2e76b0026acc", bag = "6aac007aa1bee481ec48698f", cash = "6ac0792572a5de933052b087";
        Func<int, string> itemsJson = amount => "[{\"_id\":\"" + cash + "\",\"_tpl\":\"5449016a4bdc2d6f028b456f\",\"parentId\":\"" + bag + "\",\"slotId\":\"main\",\"upd\":{\"StackObjectsCount\":" + amount + "},\"location\":{\"x\":2,\"y\":3,\"r\":\"Horizontal\"}},{\"_id\":\"" + root + "\",\"_tpl\":\"5811ce772459770e9e5f9532\"},{\"_id\":\"" + bag + "\",\"_tpl\":\"59fb042886f7746c5005a7b2\",\"parentId\":\"" + root + "\",\"slotId\":\"hideout\"}]";
        Func<int, JsonType.FlatItem[]> parse = amount => JsonConvert.DeserializeObject<JsonType.FlatItem[]>(itemsJson(amount), OtherPlayerProfileScreenPatch.GetDefaultJsonConverters());
        var saved = parse(70000);
        var originalOrder = saved.Select(item => item._id.ToString()).ToArray();
        var normalized = OtherPlayerProfileScreenPatch.Normalize(saved, root);
        Check(normalized[0]._id.ToString() == root, "nested-first snapshot locates the actual stash root");
        Check(saved.Select(item => item._id.ToString()).SequenceEqual(originalOrder), "normalization preserves input order");
        Check(normalized.Select(item => item._id.ToString()).Distinct().Count() == 3, "normalization preserves all items exactly once");
        Check(ReferenceEquals(normalized, OtherPlayerProfileScreenPatch.Normalize(normalized, root)), "root-first normalization is unchanged");
        bool rejected = false;
        try { OtherPlayerProfileScreenPatch.Normalize(saved.Where(item => item._id.ToString() != root).ToArray(), root); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "missing stash root rejected before refreshing inventory");
        var current = OtherPlayerProfileScreenPatch.Normalize(parse(100000), root);
        var delta = OtherPlayerProfileScreenPatch.Delta(current, saved);
        Check(delta.change.Length == 1 && delta.change[0]._id.ToString() == cash, "nested-first money stack remains eligible for payment update");
        Check(delta.change[0].upd.JToken["StackObjectsCount"].Value<int>() == 70000, "native converter retains paid stack count");
        Check(delta.@new.Length == 0 && delta.del.Length == 0, "payment does not remove or re-add unchanged gear");
        Check(saved[0].location.JToken["x"].Value<int>() == 2, "native converter retains item location");
        RequestHandler.Response = "{\"err\":0,\"data\":{\"deleted\":true,\"playerStashItems\":" + itemsJson(70000) + "}}";
        Check(await pitTeam.Modules.TeammateDeletion.DeleteAsync("123"), "server-confirmed deletion succeeds");
        Check(OtherPlayerProfileScreenPatch.LastItems[0].upd.JToken["StackObjectsCount"].Value<int>() == 70000, "production deletion parser preserves stack count");
        OtherPlayerProfileScreenPatch.Fail = true;
        int before = SocialNetworkClassPatch.Refreshes;
        Check(await pitTeam.Modules.TeammateDeletion.DeleteAsync("123"), "stash refresh error still allows roster/overlay cleanup");
        Check(SocialNetworkClassPatch.Refreshes == before + 1, "stash failure still refreshes friends");
        Check(pitTeam.Modules.AddTeammateCreationFlow.Toasts.Last() == "LiveStashRefreshFailed", "stash error reports refresh failure rather than deletion failure");
        var overlay = new pitTeam.Components.SquadControlMenuUi();
        Check(await overlay.Remove(), "production roster callback closes confirmation after accepted deletion despite stash failure");
        Check(overlay.Destroyed == 1 && overlay.Rebuilds == 1, "production callback destroys overlay and rebuilds roster once");
        var cancelled = new pitTeam.Components.SquadControlMenuUi();
        cancelled.Close(); cancelled.Close();
        Check(cancelled.Destroyed == 1 && cancelled.Rebuilds == 0, "close handler remains safe and idempotent independently of deletion");
        OtherPlayerProfileScreenPatch.Fail = false;
        SocialNetworkClassPatch.Fail = true;
        Check(await pitTeam.Modules.TeammateDeletion.DeleteAsync("123"), "friends refresh error still allows roster/overlay cleanup");
        SocialNetworkClassPatch.Fail = false;
        int stashRefreshes = OtherPlayerProfileScreenPatch.Refreshes;
        RequestHandler.Response = "{\"err\":1,\"errmsg\":\"TeammateDeleteInsufficientFunds\"}";
        Check(!await pitTeam.Modules.TeammateDeletion.DeleteAsync("123"), "insufficient funds keeps member");
        Check(OtherPlayerProfileScreenPatch.Refreshes == stashRefreshes && ItemUiContext.Instance.Messages == 1, "rejection leaves inventory unchanged and shows message");
        RequestHandler.Response = "{\"err\":0,\"data\":{\"deleted\":false}}";
        Check(!await pitTeam.Modules.TeammateDeletion.DeleteAsync("123"), "unconfirmed removal keeps member");
        RequestHandler.Response = "{\"err\":0,\"data\":{\"deleted\":true,\"playerStashItems\":" + itemsJson(70000) + "}}";
        RequestHandler.Pending = new TaskCompletionSource<bool>();
        var first = pitTeam.Modules.TeammateDeletion.DeleteAsync("123");
        Check(!await pitTeam.Modules.TeammateDeletion.DeleteAsync("123"), "concurrent removal is blocked");
        RequestHandler.Pending.SetResult(true);
        Check(await first, "first removal completes and releases busy guard");
        RequestHandler.Pending = null;
        Check(await pitTeam.Modules.TeammateDeletion.DeleteAsync("124"), "subsequent removal is allowed");
    }
}
