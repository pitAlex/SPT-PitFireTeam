using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;

namespace pitTeam.Server.Services;

[Injectable]
public class FriendlyLanguageService(ISptLogger<FriendlyLanguageService> logger)
{
    private const string ModFolderName = "pitFireTeam-ServerMod";
    private const string LanguageFolderName = "lang";
    private readonly ConcurrentDictionary<string, string> sessionLocales = new();
    private JsonObject? embeddedEnglishFallback;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public string GetLanguageJson(MongoId sessionId, string? requestedLocale, string? embeddedEnglishJson)
    {
        string locale = NormalizeLocale(requestedLocale);
        sessionLocales[sessionId.ToString()] = locale;
        string languageDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "user",
            "mods",
            ModFolderName,
            "Resources",
            LanguageFolderName);

        JsonObject? embeddedEnglish = ParseLanguageJson(embeddedEnglishJson, "embedded English language");
        if (embeddedEnglish != null)
        {
            embeddedEnglishFallback = CloneObject(embeddedEnglish);
        }

        EnsureEnglishLanguageFile(languageDirectory, embeddedEnglish);

        JsonObject fallback = LoadLanguageFile(languageDirectory, "en")
            ?? CloneObject(embeddedEnglish)
            ?? CloneObject(embeddedEnglishFallback)
            ?? [];
        JsonObject selected = string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase)
            ? fallback
            : LoadLanguageFile(languageDirectory, locale) ?? [];

        if (!ReferenceEquals(fallback, selected))
        {
            MergeMissingValues(selected, fallback);
        }

        return selected.ToJsonString(SerializerOptions);
    }

    public string[] GetStringArray(MongoId sessionId, string key)
    {
        JsonObject language = GetSessionLanguage(sessionId);
        if (language.TryGetPropertyValue(key, out JsonNode? node) && node is JsonArray array)
        {
            string[] values = array
                .Select(value => value?.GetValue<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray()!;
            if (values.Length > 0)
            {
                return values;
            }
        }

        return [];
    }

    public Dictionary<string, string> GetStringMap(MongoId sessionId, string key)
    {
        JsonObject language = GetSessionLanguage(sessionId);
        if (!language.TryGetPropertyValue(key, out JsonNode? node) || node is not JsonObject obj)
        {
            return [];
        }

        Dictionary<string, string> values = [];
        foreach (KeyValuePair<string, JsonNode?> entry in obj)
        {
            string? value = entry.Value?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[entry.Key] = value;
            }
        }

        return values;
    }

    private JsonObject GetSessionLanguage(MongoId sessionId)
    {
        string locale = sessionLocales.TryGetValue(sessionId.ToString(), out string? savedLocale)
            ? savedLocale
            : "en";
        string languageDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "user",
            "mods",
            ModFolderName,
            "Resources",
            LanguageFolderName);

        JsonObject fallback = LoadLanguageFile(languageDirectory, "en")
            ?? CloneObject(embeddedEnglishFallback)
            ?? [];
        JsonObject selected = string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase)
            ? fallback
            : LoadLanguageFile(languageDirectory, locale) ?? [];

        if (!ReferenceEquals(fallback, selected))
        {
            MergeMissingValues(selected, fallback);
        }

        return selected;
    }

    private void EnsureEnglishLanguageFile(string languageDirectory, JsonObject? embeddedEnglish)
    {
        if (embeddedEnglish == null || embeddedEnglish.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(languageDirectory);

        string path = Path.Combine(languageDirectory, "en.json");
        JsonObject? current = LoadLanguageFile(languageDirectory, "en");
        if (current == null)
        {
            WriteLanguageFile(path, embeddedEnglish);
            return;
        }

        if (!MergeMissingValues(current, embeddedEnglish))
        {
            return;
        }

        WriteLanguageFile(path, current);
    }

    private JsonObject? LoadLanguageFile(string languageDirectory, string locale)
    {
        string path = Path.Combine(languageDirectory, $"{locale}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return ParseLanguageJson(File.ReadAllText(path), path);
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to load pitFireTeam language file '{path}': {ex.Message}");
            return null;
        }
    }

    private JsonObject? ParseLanguageJson(string? json, string source)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            JsonNode? node = JsonNode.Parse(json);
            if (node is JsonObject language)
            {
                return language;
            }

            logger.Warning($"pitFireTeam language source '{source}' is not a JSON object.");
            return null;
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to parse pitFireTeam language source '{source}': {ex.Message}");
            return null;
        }
    }

    private void WriteLanguageFile(string path, JsonObject language)
    {
        try
        {
            File.WriteAllText(path, language.ToJsonString(SerializerOptions));
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to write pitFireTeam language file '{path}': {ex.Message}");
        }
    }

    private static string NormalizeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return "en";
        }

        string normalized = locale.Trim().ToLowerInvariant();
        int separatorIndex = normalized.IndexOfAny(['-', '_']);
        if (separatorIndex > 0)
        {
            normalized = normalized[..separatorIndex];
        }

        if (normalized.StartsWith("zh") || normalized.StartsWith("ch"))
        {
            return "chs";
        }

        return normalized switch
        {
            "de" => "ge",
            _ => normalized
        };
    }

    private static bool MergeMissingValues(JsonObject target, JsonObject fallback)
    {
        bool changed = false;
        foreach (KeyValuePair<string, JsonNode?> fallbackEntry in fallback)
        {
            if (!target.TryGetPropertyValue(fallbackEntry.Key, out JsonNode? targetValue) || targetValue == null)
            {
                target[fallbackEntry.Key] = fallbackEntry.Value?.DeepClone();
                changed = true;
                continue;
            }

            if (!IsCompatibleLanguageNode(targetValue, fallbackEntry.Value))
            {
                target[fallbackEntry.Key] = fallbackEntry.Value?.DeepClone();
                changed = true;
                continue;
            }

            if (targetValue is JsonObject targetObject && fallbackEntry.Value is JsonObject fallbackObject)
            {
                changed |= MergeMissingValues(targetObject, fallbackObject);
            }
        }

        return changed;
    }

    private static bool IsCompatibleLanguageNode(JsonNode target, JsonNode? fallback)
    {
        if (fallback == null)
        {
            return true;
        }

        return fallback switch
        {
            JsonObject => target is JsonObject,
            JsonArray => target is JsonArray,
            JsonValue => target is JsonValue,
            _ => true
        };
    }

    private static JsonObject? CloneObject(JsonObject? value)
    {
        return value?.DeepClone() as JsonObject;
    }

}
