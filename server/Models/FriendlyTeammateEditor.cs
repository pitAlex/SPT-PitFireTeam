namespace pitTeam.Server.Models;

public record TeammateEditorChoice(string Id, string Label);
public record TeammateEditorSummary(int Aid, string Name, bool Editable = true);
public record TeammateEditorSkill
{
    public string Group { get; set; } = "Common";
    public string Id { get; set; } = "";
    public double Progress { get; set; }
}
public record TeammateEditorBodyPart
{
    public string Name { get; set; } = "";
    public double Current { get; set; }
    public double Maximum { get; set; }
}

// Deliberately excludes inventory, identity, deletion and general configuration fields.
public record TeammateEditorDraft
{
    public string Revision { get; set; } = "";
    public string HeadId { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string VoiceId { get; set; } = "";
    public int Level { get; set; }
    public int Experience { get; set; }
    public string PocketId { get; set; } = "";
    public List<TeammateEditorSkill> Skills { get; set; } = [];
    public List<TeammateEditorBodyPart> BodyParts { get; set; } = [];
}

public record TeammateEditorPocket(string Id, string Label, Dictionary<string, (int Width, int Height)> Grids, HashSet<string> Slots);
