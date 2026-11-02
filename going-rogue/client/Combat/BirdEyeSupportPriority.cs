using System;

namespace pitTeam.GoingRogue.Combat;

internal enum BirdEyeSupportSource { Player, Knight, Pipe }

internal static class BirdEyeSupportPriority
{
    // Inputs are already-qualified active engagements, not mere squad presence.
    internal static BirdEyeSupportSource Select(string knightTarget, string pipeTarget)
    {
        if (!string.IsNullOrEmpty(knightTarget))
            return string.Equals(knightTarget, pipeTarget, StringComparison.Ordinal)
                ? BirdEyeSupportSource.Pipe : BirdEyeSupportSource.Knight;
        return string.IsNullOrEmpty(pipeTarget) ? BirdEyeSupportSource.Player : BirdEyeSupportSource.Pipe;
    }
}
