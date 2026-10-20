using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Commerce;

int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
var storage = new FriendlyTeammateStorage();
var saves = new SaveServer();
var insurance = new InsuranceService();
var service = new GiveGearInsuranceService(storage, saves, insurance);
var profile = saves.Profile;
profile.CharacterData.PmcData.InsuredItems.AddRange([new("gift"), new("scope"), new("keep")]);
profile.InsuranceList.Add(new() { Items = [new("gift"), new("scope"), new("keep")] });
insurance.Items.AddRange([new("gift"), new("scope"), new("keep")]);
storage.Members.Add(new() { Aid = 1 });
storage.Settings.InsuredItems.AddRange([new() { ItemId = "gift" }, new() { ItemId = "scope" }, new() { ItemId = "keep" }]);
service.Cancel("session", ["GIFT", "scope", "GIFT"]);
Check(profile.CharacterData.PmcData.InsuredItems.Single().ItemId!.Value.ToString() == "keep", "exact active coverage cancelled");
Check(profile.InsuranceList.Single().Items!.Single().Id.ToString() == "keep", "queued stock duplicates cancelled");
Check(insurance.Items.Single().Id.ToString() == "keep", "staged stock duplicates cancelled");
Check(storage.Settings.InsuredItems.Single().ItemId == "keep", "original follower policy cancelled, unrelated retained");
Check(saves.Writes == 1 && storage.Writes == 1, "cancellation persisted in both stores");
service.Cancel("session", ["gift", "scope"]);
Check(saves.Writes == 2 && storage.Writes == 1, "retry persists deletion without creating policies");
profile.CharacterData.PmcData.InsuredItems.Add(new("gift"));
profile.InsuranceList.Add(new() { Items = [new("gift")] });
service.Cancel("session", ["gift"]);
Check(profile.CharacterData.PmcData.InsuredItems.All(p => p.ItemId!.Value.ToString() != "gift") &&
    profile.InsuranceList.All(p => p.Items!.All(i => i.Id.ToString() != "gift")), "raid-end import cancelled again");
service.Cancel("session", []);
Check(saves.Writes == 3, "empty donation no mutation");
saves.Fail = true;
profile.CharacterData.PmcData.InsuredItems.Add(new("gift"));
bool failed = false;
try { service.Cancel("session", ["gift"]); } catch (IOException) { failed = true; }
Check(failed, "durability failure cannot acknowledge successful cancellation");
saves.Fail = false;
service.Cancel("session", ["gift"]);
Check(saves.Writes == 4, "retry saves even when previous failed save already changed memory");
Console.WriteLine($"PASS: {checks} production Give Gear insurance cancellation checks.");

namespace SPTarkov.DI.Annotations { public class InjectableAttribute : Attribute { } }
namespace SPTarkov.Server.Core.Models.Common
{
    public readonly record struct MongoId(string Value)
    {
        public override string ToString() => Value;
        public static implicit operator MongoId(string value) => new(value);
    }
}
namespace pitTeam.Server.Models
{
    using SPTarkov.Server.Core.Models.Common;
    public class Item(string id) { public MongoId Id = id; }
    public class Policy(string id) { public MongoId? ItemId = (MongoId)id; }
    public class Package { public List<Item>? Items = []; }
    public class Pmc { public List<Policy> InsuredItems = []; }
    public class Characters { public Pmc PmcData = new(); }
    public class Profile { public Characters CharacterData = new(); public List<Package> InsuranceList = []; }
    public class Member { public int? Aid; }
    public class FriendlyTeammateSettings { public List<GiftPolicy> InsuredItems = []; }
    public class GiftPolicy { public string ItemId = ""; }
}
namespace pitTeam.Server.Services
{
    using SPTarkov.Server.Core.Models.Common;
    public class FriendlyTeammateStorage
    {
        public List<Member> Members = []; public FriendlyTeammateSettings Settings = new(); public int Writes;
        public List<Member> ReadProfiles(MongoId id) => Members;
        public T? Read<T>(MongoId id, string document) where T : class => Settings as T;
        public void Write<T>(MongoId id, string document, T value) => Writes++;
    }
}
namespace SPTarkov.Server.Core.Servers
{
    using SPTarkov.Server.Core.Models.Common;
    public class SaveServer
    {
        public Profile Profile = new(); public int Writes; public bool Fail;
        public Profile GetProfile(MongoId id) => Profile;
        public Task SaveProfileAsync(MongoId id) { if (Fail) throw new IOException("save"); Writes++; return Task.CompletedTask; }
    }
}
namespace SPTarkov.Server.Core.Services.Commerce
{
    using SPTarkov.Server.Core.Models.Common;
    public class InsuranceService
    {
        public List<Item> Items = [];
        public bool InsuranceDictionaryExists(MongoId id) => Items.Count > 0;
        public Dictionary<MongoId, List<Item>>? GetInsurance(MongoId id) => new() { ["trader"] = Items };
    }
}
