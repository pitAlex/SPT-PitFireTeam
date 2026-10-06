# Gameplay mode verification

Run from the repository root:

```powershell
dotnet run --project tests/GameplayModes
dotnet run --project tests/FollowerDatabase -- --mode-smoke "<SPT runtime root>"
dotnet run --project tests/TeammateHiring
```

The client fixture links the production `GameplayModeRuntime` and real BepInEx configuration library. It checks all Allegiance values, config change enforcement, persisted snapshots, restarting and restoring all settings, repeated switches, rejected switches, lost responses, damaged snapshots, raid rejection, and waiting for delayed post-raid requests. Game/UI and HTTP endpoints are fixture hosts.

The server fixture exercises production storage, settings, manual hiring guards, request serialization, delayed invitation invalidation, and SPT storage dependency injection. Both mode databases are temporary; installed runtime assemblies are only read as dependencies. It verifies isolation of roster, equipment and pending recruitment data, account-id reservation across both modes, restart behavior, deletion isolation and rejection of corrupt mode settings. The existing hiring fixture covers purchase, recruitment, rollback and recovery regressions.

These checks do not replace an in-game pass: switch modes in My Squad, verify all disabled controls and tooltips, recruit/extract/accept a teammate in Allegiance, restart, and switch back to confirm the original roster and settings.
