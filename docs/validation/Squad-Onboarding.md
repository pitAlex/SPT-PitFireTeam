# Squad onboarding validation

## 2026-10-21 - Player-level welcome teammate

The free welcome teammate previously forced generation, invitation, preview and acceptance to level 1 with zero XP. Generation now uses the player's current PMC level, minimum 1, as both its player-level input and exact kit-generation level range. The invitation records that prepared level. Preview and acceptance retain it and use native `ProfileHelper.GetExperience` to set consistent baseline XP. Starting skills/mastery and fabricated raid history remain zero. One-time account-wide eligibility, separate mode inboxes, delivery delay and crash/duplicate protection are unchanged; already prepared invitations keep their saved level.

Validation: `dotnet run --project tests/FollowerDatabase/FollowerDatabase.csproj -c Debug --no-restore -- --mode-smoke E:\SPTushanka\SPT_Runtime` passes **231** gameplay/storage checks. Added cases exercise production generation details for missing, negative, zero, 1, 2, 15 and 45 player levels, exact level range/one-bot generation, native baseline XP and repeated normalization of a persisted invitation. Existing checks retain delivery/account/mode isolation, duplicate recovery and skill/history cleanup. The fixture reports NU1900 because the NuGet vulnerability feed is unavailable; it completes successfully. The standalone server Debug build passes with zero warnings/errors, and scoped whitespace checks pass. Native bot generation and first-visit UI still require a live qualification pass.

With Tarkov and the SPT server closed, deployed the server DLL and symbols only and verified both SHA-256 hashes:

- DLL: `EF3519B6CA0FB1DB8DC8513AF65F98685A5BD77131F45355E1D353961A5D9D7F`
- PDB: `7E963AB4CA030ED5A5992B0831DC288A592084A65D1AA5E7D2EB9FB7E1A1DD9A`

No player profile, existing invitation or client binary was changed, and no commit was made.
