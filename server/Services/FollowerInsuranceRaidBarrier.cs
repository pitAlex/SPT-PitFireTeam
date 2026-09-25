using pitTeam.Server.Models;

namespace pitTeam.Server.Services;

// The manifest confirms delivery observation, not authority to mint items or settle a claim.
public static class FollowerInsuranceRaidBarrier
{
    public static bool Matches(FollowerInsuranceRaidDiagnostic raid, string? serverId) =>
        !string.IsNullOrWhiteSpace(serverId) && string.Equals(raid.ServerId, serverId, StringComparison.Ordinal);

    public static bool Observe(FollowerInsuranceRaidDiagnostic raid, string? serverId, string? reportId)
    {
        if (!Matches(raid, serverId)) return false;
        if (!Guid.TryParseExact(reportId, "N", out _)) { raid.ReportsFailed = true; return false; }
        if (raid.ReportsComplete && !raid.ExpectedReportIds.Contains(reportId)) raid.ReportsFailed = true;
        if (!raid.ReceivedReportIds.Contains(reportId!)) raid.ReceivedReportIds.Add(reportId!);
        return true;
    }

    public static bool Complete(FollowerInsuranceRaidDiagnostic raid, string? serverId, IEnumerable<string>? reportIds, bool failed)
    {
        if (!Matches(raid, serverId)) return false;
        var ids = (reportIds ?? []).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
        failed |= reportIds == null || ids.Any(id => !Guid.TryParseExact(id, "N", out _));
        if (raid.ReportsComplete && !raid.ExpectedReportIds.SequenceEqual(ids)) failed = true;
        raid.ReportsFailed |= failed;
        raid.ReportsComplete = true;
        raid.ExpectedReportIds = ids;
        return true;
    }

    public static bool IsComplete(FollowerInsuranceRaidDiagnostic raid) =>
        raid.ReportsComplete && !raid.ReportsFailed
        && raid.ExpectedReportIds.ToHashSet(StringComparer.Ordinal).SetEquals(raid.ReceivedReportIds);
}
