# Squad onboarding validation

## 2026-10-30 — Going Rogue artwork and addon installation choice — deployed

Added the supplied cover as `going-rogue/client/Resources/going-rogue-card.png`, resized from 1122 × 1402 to the existing 568 × 710 asset size and visually inspected. The client addon registers its installed image path and copies the image beside its DLL; Core uses the same 284 × 355 portrait area and card controls as the built-in modes. New profiles receive three cards when both addon components are available, or two built-in cards without them. Existing users installing the addon receive exactly their current mode and Going Rogue. The shared per-player acknowledgement survives mode switches/restarts/reinstallation, remains pending on Back, and does not change the original welcome candidate/deadline/state or roster contents. Users already in Going Rogue avoid duplicate identical choices.

Validation: **574** gameplay/storage checks, including 34 new addon-onboarding cases; **198** gameplay configuration/registry checks; **649** addon/installed API references and **17** patch targets; **26** quest-preview regressions and **60** native preview API uses. Cases cover fresh/legacy users, both current built-in modes, keeping the current mode, selecting Going Rogue, repeated completion, Back/restart, unavailable client/server components, rejected raid/unoffered/unsynchronized choices, welcome/roster preservation and remove/reinstall behavior. Client Debug builds pass with zero warnings/errors; server builds have zero compiler errors and two cached NU1900 advisory-feed warnings. Source and addon output images have matching hashes and dimensions. Native Unity card layout/click behavior was not exercised.

The user explicitly confirmed that the earlier restart-required mode change was intentionally reverted. This build preserves the current immediate coordinated mode transition for both the initial choice and later addon-installation choice.

With Tarkov/SPT stopped, deployed all eight matching Core/Going Rogue client/server DLL/PDB files and the new addon PNG with backups and per-copy process/hash guards. All nine installed hashes and installed image dimensions were independently verified. Manifest: `tests/artifacts/rogue-onboarding-deployed-3f87634cc9aa4eadbcdccbb30c6fd00c/manifest.json`. No live player database/profile was edited and no process was launched.

## 2026-10-21 - Player-level welcome teammate

The free welcome teammate previously forced generation, invitation, preview and acceptance to level 1 with zero XP. Generation now uses the player's current PMC level, minimum 1, as both its player-level input and exact kit-generation level range. The invitation records that prepared level. Preview and acceptance retain it and use native `ProfileHelper.GetExperience` to set consistent baseline XP. Starting skills/mastery and fabricated raid history remain zero. One-time account-wide eligibility, separate mode inboxes, delivery delay and crash/duplicate protection are unchanged; already prepared invitations keep their saved level.

Validation: `dotnet run --project tests/FollowerDatabase/FollowerDatabase.csproj -c Debug --no-restore -- --mode-smoke E:\SPTushanka\SPT_Runtime` passes **231** gameplay/storage checks. Added cases exercise production generation details for missing, negative, zero, 1, 2, 15 and 45 player levels, exact level range/one-bot generation, native baseline XP and repeated normalization of a persisted invitation. Existing checks retain delivery/account/mode isolation, duplicate recovery and skill/history cleanup. The fixture reports NU1900 because the NuGet vulnerability feed is unavailable; it completes successfully. The standalone server Debug build passes with zero warnings/errors, and scoped whitespace checks pass. Native bot generation and first-visit UI still require a live qualification pass.

With Tarkov and the SPT server closed, deployed the server DLL and symbols only and verified both SHA-256 hashes:

- DLL: `EF3519B6CA0FB1DB8DC8513AF65F98685A5BD77131F45355E1D353961A5D9D7F`
- PDB: `7E963AB4CA030ED5A5992B0831DC288A592084A65D1AA5E7D2EB9FB7E1A1DD9A`

No player profile, existing invitation or client binary was changed, and no commit was made.
