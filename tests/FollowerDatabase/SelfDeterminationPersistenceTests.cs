using System.Text.Json;
using pitTeam.Server.Models;
using pitTeam.Server.Persistence;
using pitTeam.Server.Services;
using SPTarkov.Server.Core.Models.Common;

internal static class SelfDeterminationPersistenceTests
{
    public static void Run(FriendlyTeammateStorage storage, MongoId session, Action<bool,string> check)
    {
        check(JsonSerializer.Deserialize<FriendlyTeammateSettings>("{}")!.SelfDetermination==50,"legacy settings default determination to fifty");
        check(JsonSerializer.Deserialize<FriendlyTeammateProficiencyRequest>("{\"aid\":\"legacy\"}")!.SelfDetermination==null,
            "old proficiency requests omit determination without resetting it");
        foreach(float value in new[]{0f,20f,50f,75f,100f})
        {
            storage.Write(session,"determination-settings.json",new FriendlyTeammateSettings{SelfDetermination=value,Aggression=37});
            var saved=storage.Read<FriendlyTeammateSettings>(session,"determination-settings.json")!;
            check(saved.SelfDetermination==value&&saved.Aggression==37,"database retains independent percentages "+value);
            var request=JsonSerializer.Deserialize<FriendlyTeammateProficiencyRequest>("{\"aid\":\"member\",\"selfDetermination\":"+value.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}")!;
            check(request.SelfDetermination==value,"new proficiency request accepts determination "+value);
        }
    }
}
