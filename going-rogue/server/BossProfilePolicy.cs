using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Cloners;

namespace pitTeam.GoingRogue.Server;

// Character level and skill-panel selection are presentation policy. Skills come from generation.
public sealed class BossProfilePolicy(GlobalTable globals, BotTable bots)
{
    // Historical values are used only to remove the old imposed contribution once.
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<SkillTypes, double>> LegacySkillFloors =
        new Dictionary<string, IReadOnlyDictionary<SkillTypes, double>>(StringComparer.OrdinalIgnoreCase)
        {
            ["bossKnight"] = new Dictionary<SkillTypes, double>
            {
                [SkillTypes.Assault] = 4500, [SkillTypes.RecoilControl] = 4000,
                [SkillTypes.MagDrills] = 4000, [SkillTypes.TroubleShooting] = 3500,
                [SkillTypes.HeavyVests] = 4000, [SkillTypes.StressResistance] = 4000,
                [SkillTypes.Vitality] = 4000, [SkillTypes.Perception] = 3500,
                [SkillTypes.Throwing] = 3000, [SkillTypes.FirstAid] = 3000,
            },
            ["followerBigPipe"] = new Dictionary<SkillTypes, double>
            {
                [SkillTypes.LMG] = 4500, [SkillTypes.Launcher] = 4500,
                [SkillTypes.AttachedLauncher] = 4000, [SkillTypes.Throwing] = 4000,
                [SkillTypes.RecoilControl] = 4000, [SkillTypes.MagDrills] = 3500,
                [SkillTypes.HeavyVests] = 4000, [SkillTypes.StressResistance] = 4000,
            },
            ["followerBirdEye"] = new Dictionary<SkillTypes, double>
            {
                [SkillTypes.Sniper] = 4500, [SkillTypes.DMR] = 4500,
                [SkillTypes.Perception] = 4500, [SkillTypes.CovertMovement] = 4500,
                [SkillTypes.ProneMovement] = 4000, [SkillTypes.RecoilControl] = 4000,
                [SkillTypes.MagDrills] = 3500, [SkillTypes.Assault] = 3000,
            },
        };

    private readonly FastCloner cloner = new();

    public IReadOnlyList<string> VisibleSkills(string role)
    {
        if (!LegacySkillFloors.TryGetValue(role, out var relevant)) return [];
        if (!bots.Types.TryGetValue(role.ToLowerInvariant(), out var template) || template?.BotSkills?.Common == null)
            throw new InvalidDataException($"Native boss skill template is missing: {role}");
        // Selecting rows does not create skills or assign progress.
        return template.BotSkills.Common.Keys.Where(name => Enum.TryParse<SkillTypes>(name, true, out _))
            .Concat(relevant.Keys.Select(skill => skill.ToString())).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public bool Apply(BotBase profile)
    {
        string role = profile.Info?.Settings?.Role ?? string.Empty;
        if (!LegacySkillFloors.ContainsKey(role)) return false;
        var experience = globals.Configuration.Exp.Level.ExperienceTable;
        if (experience.Length == 0) throw new InvalidDataException("Character experience table is empty.");
        // EFT ProfileInfo.GetLevel returns table.Length after every XP threshold is met.
        int maximumLevel = experience.Length;
        int maximumExperience = experience.Sum(entry => entry.Experience);
        bool changed = profile.Info!.Level != maximumLevel || profile.Info.Experience != maximumExperience;
        profile.Info.Level = maximumLevel;
        profile.Info.Experience = maximumExperience;

        return changed;
    }

    // Called once for legacy profiles by Core, with a durable receipt in the same save batch.
    public void AdoptGeneratedSkills(BotBase saved, BotBase generated)
    {
        string role = saved.Info?.Settings?.Role ?? string.Empty;
        if (!LegacySkillFloors.TryGetValue(role, out var imposed) ||
            !string.Equals(role, generated.Info?.Settings?.Role, StringComparison.OrdinalIgnoreCase) ||
            generated.Skills?.Common == null)
            throw new InvalidDataException("Missing or mismatched native companion skills");
        var skills = cloner.Clone(generated.Skills) ?? throw new InvalidDataException("Could not clone native companion skills");
        var common = skills.Common.ToList();
        foreach (var old in saved.Skills?.Common ?? [])
        {
            var native = common.FirstOrDefault(skill => skill.Id == old.Id);
            double progress = old.Progress;
            if (imposed.TryGetValue(old.Id, out double floor) && progress >= floor)
                progress = Math.Min(CommonSkill.MaxSkillProgress, (native?.Progress ?? 0) + progress - floor);
            // Keep actual learned/native skills outside the old imposed contribution.
            if (native != null) native.Progress = progress;
            else if (progress > 0) common.Add(new CommonSkill { Id = old.Id, Progress = progress });
        }
        skills.Common = common;
        if (saved.Skills != null)
        {
            skills.Mastering = cloner.Clone(saved.Skills.Mastering);
            skills.Points = saved.Skills.Points;
        }
        saved.Skills = skills;
    }
}
