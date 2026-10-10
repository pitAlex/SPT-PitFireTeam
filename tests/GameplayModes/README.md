# Gameplay mode verification

Run from the repository root:

```powershell
dotnet run --project tests/GameplayModes
dotnet run --project tests/FollowerDatabase -- --mode-smoke "<SPT runtime root>"
dotnet run --project tests/TeammateHiring
.\tests\Verify-FriendlyEncounterPenalties.ps1 -RepositoryRoot "<repository root>" -GameRoot "<game root>"
.\tests\Verify-RecruitmentCombat.ps1 -RepositoryRoot "<repository root>" -GameRoot "<game root>"
```

The client fixture links the production `GameplayModeRuntime` and real BepInEx configuration library. It checks all Allegiance values, effective runtime values despite raw preference changes, editing/reloading the cfg without rewriting preferences, persisted snapshots, restarting and restoring Guns for Hire settings, Allegiance-only friendly chance preference persistence and mode-based control availability, repeated switches, rejected switches, lost responses, damaged snapshots, raid rejection, and waiting for delayed post-raid requests. Game/UI and HTTP endpoints are fixture hosts.

The server fixture exercises production storage, settings, manual hiring guards, request serialization, delayed invitation invalidation, and SPT storage dependency injection. Both mode databases are temporary; installed runtime assemblies are only read as dependencies. It verifies isolation of roster, equipment and pending recruitment data, account-id reservation across both modes, restart behavior, deletion isolation and rejection of corrupt mode settings. The existing hiring fixture covers purchase, recruitment, rollback and recovery regressions.

These checks do not replace an in-game pass: switch modes in My Squad, verify all disabled controls and tooltips, recruit/extract/accept a teammate in Allegiance, restart, and switch back to confirm the original roster and settings.

The mode-storage run also exercises first-visit onboarding: account-wide completion, suppression by a squad in either database, refresh acknowledgment before a two-second deadline, early/wrong-mode/in-raid delivery rejection, the saved candidate across restart, duplicate delivery and receipt recovery after acceptance/decline. It checks the production starter level/XP/skill/mastery/session-stat normalization. In EFT, also check both first-visit cards and tooltips, Back/retry, choosing the current mode, delayed native friend-request display, preview/acceptance of the level-1 starter, and existing accounts with a roster in the inactive mode.

The same run exercises the production launcher wipe router with successful, rejected, missing and unconfirmed native responses. It checks username-based account selection despite a different HTTP session, removal of both mode stores' documents and initial-choice flag, stale welcome callbacks, repeated wipes, restart with preserved legacy JSONs, and byte-for-byte preservation of another account's databases. A launcher/EFT pass still needs to confirm Wipe Profile followed by an empty roster and a fresh mode-card choice.

Recruitment checks also exercise the production Allegiance greeting with real Harmony speech hooks and controlled sight/clock/speaker hosts, with and without SAIN. They cover distance-based first-line deadlines (0–50 m, maximum 2 seconds), randomized 1–3 second second-line deadlines, the second line's current-distance probability and exact thresholds, sticky skipped/accepted rolls across reentry and playback failure, the 50 m boundary, sector/obstruction/visible-distance gates, busy speech, recruitment reply priority, at most two lines, lost-sight reacquisition, lifecycle guards and fresh raid state. Actual voice playback/perception still needs an in-game pass.

Encounter penalty checks compile the production client cache, shared arithmetic and extracted `traitor` kill-report methods against game-facing fixture hosts. They cover own raid recruits versus spawned/foreign/ordinary victims, direct player attribution, opposite factions, Raid End Messages off, duplicate callbacks, lost-response retries, concurrent pending reports, exact expiry and countdown precision. Saved and pending penalties scale with the current Friendly Chance Multiplier without changing their stored expiry; switching the multiplier and reloading a legacy ledger cannot retain a smaller penalty. The mode-storage fixture also exercises the real encrypted Allegiance database for per-user isolation, restart persistence, independent stacked expiry and mode exclusion. The hostility fixture checks that scaled penalties subtract percentage points after the chance boost for both factions at every multiplier without rerolling existing candidates.
