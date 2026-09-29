using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyRecruitPickupRequest : IRequestData
{
    [JsonPropertyName("Candidates")]
    public List<FriendlyRecruitPickupCandidate> Candidates { get; set; } = [];
}

public record FriendlyRecruitPickupCandidate
{
    [JsonPropertyName("ProfileId")]
    public string ProfileId { get; set; } = string.Empty;

    [JsonPropertyName("AccountId")]
    public string AccountId { get; set; } = string.Empty;

    [JsonPropertyName("Nickname")]
    public string Nickname { get; set; } = string.Empty;

    [JsonPropertyName("Level")]
    public int Level { get; set; }

    [JsonPropertyName("Side")]
    public string Side { get; set; } = string.Empty;

    [JsonPropertyName("Voice")]
    public string Voice { get; set; } = string.Empty;

    [JsonPropertyName("Head")]
    public string Head { get; set; } = string.Empty;

    [JsonPropertyName("ProfileJson")]
    public string ProfileJson { get; set; } = string.Empty;
}

public record FriendlyRecruitRequestEntry : FriendlyRecruitPickupCandidate
{
    // Server-owned snapshot; deliberately absent from the client pickup request model.
    public int? RecruitmentGearPrice { get; set; }

    [JsonPropertyName("createdAt")]
    public long CreatedAt { get; set; }
}

public record FriendlySocialFriendRequestEntry
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public double Date { get; set; }

    [JsonPropertyName("profile")]
    public FriendlySocialFriendProfile Profile { get; set; } = new();
}

public record FriendlySocialFriendProfile
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("aid")]
    public string Aid { get; set; } = string.Empty;

    [JsonPropertyName("Info")]
    public FriendlySocialFriendInfo Info { get; set; } = new();
}

public record FriendlySocialFriendInfo
{
    [JsonPropertyName("Nickname")]
    public string Nickname { get; set; } = string.Empty;

    [JsonPropertyName("Side")]
    public string Side { get; set; } = string.Empty;

    [JsonPropertyName("Level")]
    public int Level { get; set; }

    [JsonPropertyName("MemberCategory")]
    public MemberCategory MemberCategory { get; set; }

    [JsonPropertyName("SelectedMemberCategory")]
    public MemberCategory SelectedMemberCategory { get; set; }

    [JsonPropertyName("Ignored")]
    public bool Ignored { get; set; }

    [JsonPropertyName("Banned")]
    public bool Banned { get; set; }
}
