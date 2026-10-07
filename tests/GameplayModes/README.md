# Gameplay mode verification

Run from the repository root:

```powershell
dotnet run --project tests/GameplayModes
dotnet run --project tests/FollowerDatabase -- --mode-smoke "<SPT runtime root>"
dotnet run --project tests/TeammateHiring
.\tests\Verify-FriendlyEncounterPenalties.ps1 -RepositoryRoot "<repository root>" -GameRoot "<game root>"
```

The client fixture links the production `GameplayModeRuntime` and real BepInEx configuration library. It checks all Allegiance values, effective runtime values despite raw preference changes, editing/reloading the cfg without rewriting preferences, persisted snapshots, restarting and restoring Guns for Hire settings, Allegiance-only friendly chance preference persistence and mode-based control availability, repeated switches, rejected switches, lost responses, damaged snapshots, raid rejection, and waiting for delayed post-raid requests. Game/UI and HTTP endpoints are fixture hosts.

The server fixture exercises production storage, settings, manual hiring guards, request serialization, delayed invitation invalidation, and SPT storage dependency injection. Both mode databases are temporary; installed runtime assemblies are only read as dependencies. It verifies isolation of roster, equipment and pending recruitment data, account-id reservation across both modes, restart behavior, deletion isolation and rejection of corrupt mode settings. The existing hiring fixture covers purchase, recruitment, rollback and recovery regressions.

These checks do not replace an in-game pass: switch modes in My Squad, verify all disabled controls and tooltips, recruit/extract/accept a teammate in Allegiance, restart, and switch back to confirm the original roster and settings.

Encounter penalty checks compile the production client cache, shared arithmetic and extracted `traitor` kill-report methods against game-facing fixture hosts. They cover own raid recruits versus spawned/foreign/ordinary victims, direct player attribution, opposite factions, Raid End Messages off, duplicate callbacks, lost-response retries, concurrent pending reports, exact expiry and countdown precision. The mode-storage fixture also exercises the real encrypted Allegiance database for per-user isolation, restart persistence, independent stacked expiry and mode exclusion. The hostility fixture checks that penalties subtract percentage points after the multiplier without rerolling existing candidates.
