using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using pitTeam.Server.Models;

namespace pitTeam.Server.Services;

public static class FriendlyTeammateIdentityPolicy
{
    public static string NormalizeNickname(string? value) => !string.IsNullOrWhiteSpace(value)
        ? value.Trim() : throw new FriendlyTeammateException("Missing teammate nickname");

    public static bool NicknameInUse(IEnumerable<string?> names, string nickname) =>
        names.Any(name => string.Equals(name, nickname, StringComparison.OrdinalIgnoreCase));

    public static bool IsVoice([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] CustomizationItem? template) => template?.Parent == "5fc100cf95572123ae738483";

    public static string DisplayName(string id, string? name, IReadOnlyDictionary<string, string> selected,
        IReadOnlyDictionary<string, string> english)
    {
        // EFT BaseCustomizationItem.NameLocalizationKey uses this key for heads and voices alike.
        string key = id + " Name";
        foreach (var language in new[] { selected, english })
            if (language.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) && value != key)
                return value;
        return string.IsNullOrWhiteSpace(name) ? id : name;
    }
}
