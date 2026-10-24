using pitTeam.Server.Models;

namespace pitTeam.Server.Services;

/// <summary>Partitions existing policies by validated final ownership. Never creates coverage.</summary>
public static class FollowerInsuranceReconciler
{
    public static (List<FriendlyTeammateInsuredItem> Player, List<FriendlyTeammateInsuredItem> Follower) Reconcile(
        IEnumerable<FriendlyTeammateInsuredItem> playerPolicies,
        IEnumerable<FriendlyTeammateInsuredItem> followerPolicies,
        HashSet<string> originalPlayerIds, HashSet<string> originalFollowerIds,
        HashSet<string> finalPlayerIds, HashSet<string> finalFollowerIds,
        IReadOnlyDictionary<string, string> followerIdRemaps, bool followerInsuranceEnabled)
    {
        var policies = new Dictionary<string, FriendlyTeammateInsuredItem>(StringComparer.OrdinalIgnoreCase);
        void Add(FriendlyTeammateInsuredItem policy, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(policy.TraderId)) return;
            if (policies.TryGetValue(id, out var existing)
                && !string.Equals(existing.TraderId, policy.TraderId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Conflicting insurance traders for item '{id}'.");
            policies[id] = policy with { ItemId = id };
        }

        foreach (var policy in playerPolicies)
            if (originalPlayerIds.Contains(policy.ItemId)) Add(policy, policy.ItemId);

        // Remaps belong only to the follower: a legacy collision must not clone a player's policy.
        foreach (var policy in followerPolicies)
            if (followerInsuranceEnabled && originalFollowerIds.Contains(policy.ItemId))
                Add(policy, followerIdRemaps.TryGetValue(policy.ItemId, out var id) ? id : policy.ItemId);

        var player = new List<FriendlyTeammateInsuredItem>();
        var follower = new List<FriendlyTeammateInsuredItem>();
        foreach (var policy in policies.Values)
        {
            bool onPlayer = finalPlayerIds.Contains(policy.ItemId);
            bool onFollower = finalFollowerIds.Contains(policy.ItemId);
            if (onPlayer && onFollower)
                throw new InvalidOperationException($"Insured item '{policy.ItemId}' has two owners.");
            if (onPlayer) player.Add(policy);
            else if (onFollower && followerInsuranceEnabled) follower.Add(policy);
        }
        return (player, follower);
    }
}
