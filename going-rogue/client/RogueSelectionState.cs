using System.Collections.Generic;
using System.Linq;
using pitTeam.Modules;

namespace pitTeam.GoingRogue;

internal sealed class RogueSelectionState
{
    private string[] accounts = new string[0];
    private bool rogue;
    private bool goons;
    internal bool Unresolved { get; private set; }
    internal bool HasRogues => Unresolved || rogue;
    internal bool HasGoons => Unresolved || goons;

    internal void Begin(IEnumerable<string> selected)
    {
        accounts = selected.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        Unresolved = accounts.Length > 0;
        rogue = goons = false;
    }

    internal void Resolve(string json)
    {
        var rows = FollowerSpawnMetadata.Read(json, accounts, requireSettings: false);
        var roles = rows.Values.Select(row => FollowerSpawnMetadata.Text(row, "Role")).ToArray();
        // Publish only a complete validated snapshot; a failure leaves the closed gates intact.
        rogue = roles.Any(RogueRosterSelection.IsRole);
        goons = roles.Any(RogueRosterSelection.IsGoonRole);
        Unresolved = false;
    }
}
