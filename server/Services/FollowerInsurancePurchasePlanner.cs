namespace pitTeam.Server.Services;

/// <summary>Validate every requested ID before payment; only new coverage contributes to the price.</summary>
public static class FollowerInsurancePurchasePlanner
{
    public static (List<T> Items, int Total) Plan<T>(IEnumerable<string> requestedIds,
        IReadOnlyDictionary<string, T> ownedItems, HashSet<string> coveredIds,
        Func<T, bool> eligible, Func<T, double> quote, int quotedTotal)
    {
        var ids = requestedIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0 || ids.Length > 1000 || quotedTotal < 0)
            throw new InvalidOperationException("FollowerInsuranceInvalidSelection");
        var items = new List<T>();
        foreach (string id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !ownedItems.TryGetValue(id, out var item) || !eligible(item))
                throw new InvalidOperationException("FollowerInsuranceInvalidSelection");
            if (!coveredIds.Contains(id)) items.Add(item);
        }
        double total = 0;
        foreach (var item in items)
        {
            double price = quote(item);
            if (!double.IsFinite(price) || price <= 0 || price != Math.Ceiling(price))
                throw new InvalidOperationException("FollowerInsurancePriceChanged");
            total += price;
        }
        if (total > int.MaxValue || total > quotedTotal)
            throw new InvalidOperationException("FollowerInsurancePriceChanged");
        return (items, (int)total);
    }
}
