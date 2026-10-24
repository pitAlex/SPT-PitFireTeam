using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Models;

public record FriendlyTeammateCreateRequest : IRequestData
{
    [JsonPropertyName("quoteToken")]
    public string? QuoteToken { get; set; }

    [JsonPropertyName("withoutKit")]
    public bool WithoutKit { get; set; }

    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    [JsonPropertyName("voice")]
    public string? Voice { get; set; }

    [JsonPropertyName("head")]
    public string? Head { get; set; }
}
