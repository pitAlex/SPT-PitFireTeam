using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
BotOwner Bot(string id) => new() { ProfileId = id, Profile = new() { AccountId = id, Info = new() { Nickname = id } } };
string? Sender() => SPT.Common.Http.RequestHandler.Last?["member"]?["_id"]?.ToString();

NpcMessage.Instance = new();
NpcMessage.AddNpc(Bot("pmc"));
NpcMessage.NpcSendThankYou();
Check(Sender() == "pmc", "ordinary teammate sends the no-loot extraction message");
Check(NpcMessage.GetNpcType("ally") == "pmc", "ordinary teammate remains the fallback when no ally exists");
NpcMessage.AddNpc(Bot("recruit"), false);
Check(NpcMessage.GetNpcType("ally") == "recruit", "field recruit is classified separately from a teammate");
NpcMessage.AddNpc(Bot("knight"), false, true);
Check(NpcMessage.GetNpcType("boss") == "knight", "boss lookup never chooses an earlier ordinary teammate");
NpcMessage.NpcSendThankYou();
Check(Sender() == "recruit", "normal extraction prefers a recruited ally over a boss");
NpcMessage.NpcSendThankYou("knight");
Check(Sender() == "knight", "explicit boss extraction message still works");
NpcMessage.Instance._npcs.Remove("recruit");
NpcMessage.Instance._matesLost.Add("lost member");
NpcMessage.NpcSendThankYou();
Check(Sender() == "pmc" && SPT.Common.Http.RequestHandler.Last?["member"]?["SquadInfo"]?["Partial"]?.Value<bool>() == true,
    "surviving ordinary teammate sends the partial-squad message");
NpcMessage.Instance._npcs.Remove("pmc");
int calls = SPT.Common.Http.RequestHandler.Calls;
NpcMessage.NpcSendThankYou();
Check(SPT.Common.Http.RequestHandler.Calls == calls, "boss-only normal selection remains intentionally silent");
NpcMessage.Instance._playerDied = true;
NpcMessage.NpcSendThankYou("knight");
Check(SPT.Common.Http.RequestHandler.Calls == calls, "player death retains the message suppression gate");
NpcMessage.Instance._playerDied = false;
pitFireTeam.npcSendMessage.Value = false;
NpcMessage.NpcSendThankYou("knight");
Check(SPT.Common.Http.RequestHandler.Calls == calls, "disabled raid-end messages remain disabled");
Console.WriteLine($"PASS: {checks} production NPC registration/recipient/message checks; controlled bot and HTTP doubles.");

partial class NpcMessage
{
    public static NpcMessage? Instance;
    public readonly Dictionary<string, object> _npcs = new();
    public readonly List<string> _matesLost = new();
    public bool _playerDied;
}
class BotOwner { public string ProfileId = ""; public BotProfile Profile = new(); }
class BotProfile { public string AccountId = ""; public BotInfo Info = new(); }
class BotInfo { public int Level = 60, MemberCategory = 1; public string Nickname = "", Side = "Usec"; public BotSettings Settings = new(); }
class BotSettings { public string Role = "bossKnight"; }
class AbstractGame { public static JsonConverter[] Converters = Array.Empty<JsonConverter>(); }
class Traverse
{
    public static Traverse Create(object value) => new();
    public FieldValue<T> Field<T>(string name) => new();
}
class FieldValue<T> { public T Value => (T)(object)Array.Empty<JsonConverter>(); }
class Toggle { public bool Value = true; }
static class pitFireTeam { public static Toggle npcSendMessage = new(); }
static class GameplayModeRuntime { public static void RunRosterRequest(Action action) => action(); }
static class Extensions
{
    public static T PickRandom<T>(this List<T> entries) => entries[0];
    public static string ToJson(this object value, JsonConverter[] converters) => JsonConvert.SerializeObject(value);
}
namespace Modules { static class Logger { public static void LogError(object value) => throw new Exception(value.ToString()); } }
namespace SPT.Common.Http
{
    static class RequestHandler
    {
        public static JObject? Last;
        public static int Calls;
        public static void PostJson(string path, string json) { Calls++; Last = JObject.Parse(json); }
    }
}
