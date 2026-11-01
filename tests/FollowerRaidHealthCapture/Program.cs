using EFT;
using pitTeam.Modules;
using System.Text.Json;

int checks = 0;
void Check(bool passed, string message) { if (!passed) throw new Exception(message); checks++; }
var profile = new Profile { Id = "saved-teammate", AccountId = "123" };
var player = new Player { Profile = profile, ActiveHealthController = new() };
foreach (var part in EFT.HealthSystem.HealthHelper.RealBodyParts)
    player.ActiveHealthController.Values[part] = new() { Current = 80, Maximum = 120 };
var bot = new BotOwner { GetPlayer = player };
Check(!FollowerRaidHealthRetention.Begin(bot), "ordinary generated bots retain existing full-health spawn ownership");
FollowerRaidHealthRetention.RegisterSavedProfile(profile);
Check(FollowerRaidHealthRetention.Begin(bot), "explicitly saved teammate is eligible to retain spawn health");
var first = FollowerRaidHealthRetention.Capture(player);
Check(first.BodyParts.Count == 7 && first.BodyParts["Head"].Current == 80 && first.BodyParts["Head"].SpawnMaximum == 120,
    "captures all native body parts without healing them");
player.ActiveHealthController.Values[EBodyPart.LeftLeg] = new() { Current = 34, Maximum = 34 };
var surgery = FollowerRaidHealthRetention.Capture(player);
Check(surgery.BodyParts["LeftLeg"].Current == 34 && surgery.BodyParts["LeftLeg"].SpawnMaximum == 120,
    "surgery retains the original spawn maximum as normalization denominator");
Check(first.BodyParts["LeftLeg"].Current == 80 && first.BodyParts["LeftLeg"].SpawnMaximum == 120,
    "captured payloads do not follow later runtime changes");
Check(FollowerRaidHealthRetention.Begin(bot), "transit spawn remains eligible without resetting its carried health");
Check(FollowerRaidHealthRetention.Capture(player).BodyParts["LeftLeg"].SpawnMaximum == 120,
    "second activation in the same continuous raid cannot replace the original maximum");
FollowerRaidHealthRetention.RecordFallen(player);
Check(FollowerRaidHealthRetention.GetFallen("123", null).BodyParts["LeftLeg"].Current == 34,
    "death-time health is retained independently of gear serialization");
player.ActiveHealthController = null;
Check(FollowerRaidHealthRetention.GetFallen(null, "saved-teammate").BodyParts["Head"].Current == 80,
    "fallen HP is still available after controller disposal");
Check(FollowerRaidHealthRetention.Capture(player) == null, "unavailable live controller produces a missing snapshot instead of fake HP");
string json = JsonSerializer.Serialize(surgery);
Check(!json.Contains("Effects") && !json.Contains("Energy") && !json.Contains("Hydration"),
    "wire payload cannot carry bleeding, fractures, resources or other effects");
var roundTrip = JsonSerializer.Deserialize<FollowerRaidHealth>(json);
Check(roundTrip.BodyParts["LeftLeg"].SpawnMaximum == 120 && roundTrip.BodyParts["LeftLeg"].Current == 34,
    "HP-only wire snapshot retains its normalization values");
FollowerRaidHealthRetention.Clear();
Check(!FollowerRaidHealthRetention.Begin(bot) && FollowerRaidHealthRetention.GetFallen("123", "saved-teammate") == null,
    "final raid teardown removes eligibility and fallen snapshots");
player.ActiveHealthController = new();
foreach (var part in EFT.HealthSystem.HealthHelper.RealBodyParts)
    player.ActiveHealthController.Values[part] = new() { Current = 30, Maximum = 60 };
FollowerRaidHealthRetention.RegisterSavedProfile(profile);
FollowerRaidHealthRetention.Begin(bot);
Check(FollowerRaidHealthRetention.Capture(player).BodyParts["Head"].SpawnMaximum == 60,
    "new raid uses its new health multiplier rather than stale prior-raid maxima");
Console.WriteLine($"PASS: {checks} client raid-health capture checks.");

namespace EFT
{
    public enum EBodyPart { Head, Chest, Stomach, LeftArm, RightArm, LeftLeg, RightLeg, Common }
    public struct ValueStruct { public float Current; public float Maximum; }
    public class Profile { public string Id; public string AccountId; }
    public class Player { public Profile Profile; public string ProfileId => Profile.Id; public TestHealthController ActiveHealthController; }
    public class BotOwner { public Player GetPlayer; public string ProfileId => GetPlayer.ProfileId; public Profile Profile => GetPlayer.Profile; }
    public class TestHealthController
    {
        public Dictionary<EBodyPart, ValueStruct> Values = new();
        public ValueStruct GetBodyPartHealth(EBodyPart part, bool rounded) => part == EBodyPart.Common
            ? new() { Current = Values.Values.Sum(v => v.Current), Maximum = Values.Values.Sum(v => v.Maximum) } : Values[part];
    }
}
namespace EFT.HealthSystem
{
    public static class HealthHelper { public static EBodyPart[] RealBodyParts = Enum.GetValues<EBodyPart>().Where(p => p != EBodyPart.Common).ToArray(); }
}
namespace pitTeam.Modules
{
    public static class Logger
    {
        public static void LogError(string message) => throw new Exception(message);
        public static void LogInfo(string message) { }
    }
}
