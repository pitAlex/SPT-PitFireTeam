using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using pitTeam.Server.Models;

namespace pitTeam.Server.Services;

// Pure edit boundary: no submitted profile or inventory is ever deserialized back into storage.
public static class FriendlyTeammateEditorPolicy
{
    public static string Revision(string profile, string? equipment) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(profile.Length + ":" + profile + (equipment ?? ""))));

    public static TeammateEditorDraft Read(string profile, string? equipment)
    {
        var root = JsonNode.Parse(profile)!.AsObject();
        return new()
        {
            Revision = Revision(profile, equipment),
            HeadId = root["Customization"]?["Head"]?.GetValue<string>() ?? "",
            VoiceId = root["Customization"]?["Voice"]?.GetValue<string>() ?? "",
            Nickname = root["Info"]?["Nickname"]?.GetValue<string>() ?? "",
            Level = root["Info"]?["Level"]?.GetValue<int>() ?? 1,
            Experience = root["Info"]?["Experience"]?.GetValue<int>() ?? 0,
            PocketId = Pockets(root["Inventory"]?["items"] as JsonArray)?["_tpl"]?.GetValue<string>() ?? "",
            Skills = SkillNodes(root).Select(entry => new TeammateEditorSkill
            {
                Group = entry.Group, Id = entry.Node["Id"]!.GetValue<string>(),
                Progress = entry.Node["Progress"]?.GetValue<double>() ?? 0
            }).ToList(),
            BodyParts = (root["Health"]?["BodyParts"] as JsonObject ?? []).Where(p => p.Value?["Health"] != null)
                .Select(p => new TeammateEditorBodyPart { Name = p.Key,
                    Current = p.Value!["Health"]!["Current"]?.GetValue<double>() ?? 0,
                    Maximum = p.Value!["Health"]!["Maximum"]?.GetValue<double>() ?? 1 }).ToList()
        };
    }

    public static (string Profile, string? Equipment) Apply(string profile, string? equipment, TeammateEditorDraft edit,
        IReadOnlySet<string> heads, IReadOnlyDictionary<string, TeammateEditorPocket> pockets, IReadOnlySet<string>? voices = null)
    {
        if (edit == null || edit.Revision != Revision(profile, equipment)) Fail("EditorStale");
        var root = JsonNode.Parse(profile)!.AsObject();
        var original = Read(profile, equipment);
        if (edit!.Level < 1 || edit.Level > 100 || edit.Experience < 0) Fail("EditorInvalid");
        if (edit.HeadId != original.HeadId && !heads.Contains(edit.HeadId)) Fail("EditorInvalidHead");
        if (string.IsNullOrWhiteSpace(edit.Nickname)) Fail("EditorInvalidName");
        string nickname = FriendlyTeammateIdentityPolicy.NormalizeNickname(edit.Nickname);
        if (nickname != original.Nickname)
        {
            root["Info"]!["Nickname"] = nickname;
            root["Info"]!["LowerNickname"] = nickname.ToLowerInvariant();
        }
        if (edit.VoiceId != original.VoiceId)
        {
            if (voices == null || !voices.Contains(edit.VoiceId)) Fail("EditorInvalidVoice");
            root["Customization"]!["Voice"] = edit.VoiceId;
        }
        root["Info"]!["Level"] = edit.Level;
        root["Info"]!["Experience"] = edit.Experience;
        if (edit.HeadId != original.HeadId) root["Customization"]!["Head"] = edit.HeadId;

        var skills = SkillNodes(root).ToList();
        if (edit.Skills == null || edit.Skills.Count != skills.Count ||
            edit.Skills.Select(s => (s.Group, s.Id)).Distinct().Count() != skills.Count) Fail("EditorInvalid");
        foreach (var skill in edit.Skills!)
        {
            var node = skills.SingleOrDefault(s => s.Group == skill.Group && s.Node["Id"]!.GetValue<string>() == skill.Id).Node;
            if (node == null || !double.IsFinite(skill.Progress) || skill.Progress < 0 ||
                skill.Progress > (skill.Group == "Common" ? 5100 : 10000000)) Fail("EditorInvalid");
            node!["Progress"] = skill.Progress;
        }

        var body = root["Health"]?["BodyParts"] as JsonObject ?? [];
        var editableParts = body.Where(p => p.Value?["Health"] != null).Select(p => p.Key).ToHashSet();
        if (edit.BodyParts == null || edit.BodyParts.Count != editableParts.Count ||
            !edit.BodyParts.Select(p => p.Name).ToHashSet().SetEquals(editableParts)) Fail("EditorInvalid");
        foreach (var part in edit.BodyParts!)
        {
            var health = body[part.Name]!["Health"]!;
            double minimum = health["Minimum"]?.GetValue<double>() ?? 0;
            if (!double.IsFinite(part.Current) || !double.IsFinite(part.Maximum) || part.Maximum < Math.Max(1, minimum) ||
                part.Maximum > 10000 || part.Current < minimum || part.Current > part.Maximum) Fail("EditorInvalid");
            health["Current"] = part.Current;
            health["Maximum"] = part.Maximum;
        }
        if (edit.BodyParts.Any(p => original.BodyParts.Any(o => o.Name == p.Name && (o.Current != p.Current || o.Maximum != p.Maximum))))
            root["Health"]!["UpdateTime"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        string? newEquipment = equipment;
        if (edit.PocketId != original.PocketId)
        {
            if (!pockets.ContainsKey(edit.PocketId) || !pockets.ContainsKey(original.PocketId)) Fail("EditorInvalidPockets");
            ChangePockets(root["Inventory"]?["items"] as JsonArray, edit.PocketId, pockets);
            if (equipment != null)
            {
                // The saved Default snapshot can restore its pocket shell at spawn; update that shell too.
                var snapshot = JsonNode.Parse(equipment) as JsonArray ?? throw new InvalidDataException("EditorInvalid");
                ChangePockets(snapshot, edit.PocketId, pockets);
                newEquipment = snapshot.ToJsonString();
            }
        }
        return (root.ToJsonString(), newEquipment);
    }

    private static IEnumerable<(string Group, JsonObject Node)> SkillNodes(JsonObject root)
    {
        foreach (string group in new[] { "Common", "Mastering" })
            foreach (var node in root["Skills"]?[group] as JsonArray ?? [])
                if (node is JsonObject skill && skill["Id"] != null) yield return (group, skill);
    }

    private static JsonObject? Pockets(JsonArray? items) => items?.OfType<JsonObject>()
        .SingleOrDefault(item => item["slotId"]?.GetValue<string>() == "Pockets");

    private static void ChangePockets(JsonArray? items, string target, IReadOnlyDictionary<string, TeammateEditorPocket> layouts)
    {
        var shell = Pockets(items);
        if (shell == null) Fail("EditorInvalidPockets");
        if (!layouts.TryGetValue(shell["_tpl"]!.GetValue<string>(), out var before)) Fail("EditorInvalidPockets");
        var after = layouts[target];
        foreach (var child in items!.OfType<JsonObject>().Where(i => i["parentId"]?.GetValue<string>() == shell!["_id"]!.GetValue<string>()))
        {
            string slot = child["slotId"]?.GetValue<string>() ?? "";
            if (before!.Grids.TryGetValue(slot, out var oldGrid))
            {
                if (!after.Grids.TryGetValue(slot, out var grid) || grid.Width < oldGrid.Width || grid.Height < oldGrid.Height)
                    Fail("EditorEmptyPockets");
            }
            else if (!after.Slots.Contains(slot)) Fail("EditorEmptyPockets");
        }
        shell!["_tpl"] = target;
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string key) => throw new InvalidDataException(key);
}
