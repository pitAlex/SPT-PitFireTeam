using System.Runtime.CompilerServices;

// Shipped together: reuse the internal command/cover/action contracts without making the
// implementation public API. Core never constructs or references an addon type.
[assembly: InternalsVisibleTo("pitFireTeam.GoingRogue")]
