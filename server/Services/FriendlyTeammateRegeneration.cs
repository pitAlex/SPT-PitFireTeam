using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Enums.Hideout;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace pitTeam.Server.Services;

// Mirrors OfflineHealthController.Regeneration and BonusController's percentage collection.
// Use each native body-part rate, not the player's observed HP delta (which caps at full HP).
public static class FriendlyTeammateRegeneration
{
    public static bool IsWounded(BotBaseHealth? health) => FriendlyTeammateTreatmentPricing.BodyParts.Any(part =>
        health?.BodyParts?.GetValueOrDefault(part)?.Health is not { Current: double current, Maximum: double maximum }
        || !double.IsFinite(current) || !double.IsFinite(maximum) || maximum <= 0 || current < maximum);

    public static Dictionary<string, double> Rates(PmcData player, GlobalTable globals)
    {
        var config = globals.Configuration;
        var body = config.Health.Effects.Regeneration.BodyHealth;
        var values = new Dictionary<string, double>
        {
            ["Head"] = body.Head.Value, ["Chest"] = body.Chest.Value, ["Stomach"] = body.Stomach.Value,
            ["LeftArm"] = body.LeftArm.Value, ["RightArm"] = body.RightArm.Value,
            ["LeftLeg"] = body.LeftLeg.Value, ["RightLeg"] = body.RightLeg.Value
        };
        bool powered = player.Hideout?.Areas?.Any(area => area.Type == HideoutAreas.Generator && area.Active == true) == true;
        double bonus = (player.Bonuses ?? []).Where(b => b.Type == BonusType.HealthRegeneration && (b.IsPassive == true || powered))
            .DistinctBy(b => b.Id).Sum(b => b.Value ?? 0);
        int skill = Math.Clamp((int)((player.Skills?.Common?.FirstOrDefault(s => s.Id == SkillTypes.HideoutManagement)?.Progress ?? 0) / 100), 0, 50);
        float boost = skill * (float)config.SkillsSettings.HideoutManagement.SkillBoostPercent;
        // PercentageBonus boosts a positive combined bonus; negative totals are not boosted.
        double effective = bonus + (bonus > 0 ? bonus * boost / 100d : 0);
        double loop = LoopSeconds(globals);
        return values.ToDictionary(v => v.Key, v =>
        {
            float perLoop = (float)v.Value * (float)loop / 60f;
            return Math.Max(0, (float)(perLoop * (1d + effective / 100d)) / loop);
        });
    }

    public static double LoopSeconds(GlobalTable globals) => Math.Max(0.1, globals.Configuration.Health.Effects.Regeneration.LoopTime);

    public static bool Advance(BotBaseHealth health, IReadOnlyDictionary<string, double> rates, double loop, double now)
    {
        double? beforeTime = health.UpdateTime;
        // Legacy/generated snapshots have no recovery checkpoint. Never interpret zero as
        // decades of healing; start their clock without granting elapsed HP.
        if (beforeTime is not double last || !double.IsFinite(last) || last <= 0 || last > now)
        {
            health.UpdateTime = now;
            return true;
        }
        double ticks = Math.Floor((now - last) / loop);
        if (ticks < 1) return false;
        // Effects need their native expiry/influence rules. This HP-only test never invents
        // effect recovery; retain their health and advance the checkpoint conservatively.
        if (health.BodyParts?.Values.Any(p => p.Effects?.Count > 0) != true)
        {
            FriendlyTeammateTreatmentPricing.MissingHealth(health);
            foreach (string part in FriendlyTeammateTreatmentPricing.BodyParts)
            {
                var hp = health.BodyParts![part].Health!;
                hp.Current = Math.Min(hp.Maximum!.Value, hp.Current!.Value + rates[part] * loop * ticks);
            }
        }
        health.UpdateTime = last + ticks * loop;
        return true;
    }
}
