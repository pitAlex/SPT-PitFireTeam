# Core 1.0.1 validation

## 2026-10-25 - Profile control layout and voice icon

Profile controls now use four paired rows: upper/lower clothing, Proficiency/tactic, Edit Loadout/Kit Loadouts, then Edit Name/voice. Live buttons retain their callbacks and use the native dropdown geometry; the equipment row and Proficiency button retain separate cleanup ownership. Loadout refresh does not recreate an editor button over Proficiency. The voice selector uses the supplied `voice.png`, preserving aspect ratio and matching the tactic icon tint.

Core Debug build (`BuildSAINAddon=false`) passed with zero warnings/errors, and `git diff --check` passed. After the user closed Tarkov, deployed only the client DLL, matching symbols and voice icon; the server stayed running and was not modified. Source/installed DLL and icon SHA-256 hashes matched (`5EB52C1516AE531E1B6C73DF5C3A515FA991424DD5F2837AF4EF4748E5D494D1` and `B5B186F1CD6D55609BCADCEA5E66470CEFDD210FD66D79D203DCC982939DC248`). In-game placement, hover, dropdown selection and reopen behavior remain unverified.

## 2026-10-24 - Restore Field Upkeep from 0.10.0

The `0.10.0` branch confirms the original `restrictedGearMaintenance` setting, default false, shown as a nested Field Upkeep checkbox only under Restricted. Core restores that config key, My Squad row, centralized English fallback, English/Russian/Chinese locales and client/server synchronization. It is shared across Guns for Hire and Allegiance, outside the nine-setting mode snapshot, and the Loadout Management section remains unavailable in raid-restricted contexts.

Restricted escaped/death equipment persistence now requires Field Upkeep. Disabled or missing/null values retain the saved kit; enabled values use the existing filtered live/death-time snapshots. Immersive/Realistic persistence is unaffected. Gear protection, tracked cargo/other-teammate exclusions, managed secure-container filtering and safe restoration of missing original magazines remain unchanged. Toggling the preference does not immediately rewrite saved equipment.

Core client/server project versions, client plugin and assembly/file versions, and server metadata move to 1.0.1. The optional SAIN addon keeps its own 1.0.0 version and is rebuilt against the new Core assembly.

Validation completed:

- **236** gameplay/storage checks, including Restricted extraction/death with upkeep true, false and null; baseline preservation, tracked/protected filtering and unchanged Immersive/Realistic admission.
- **73** production gameplay-config checks, including default-off behavior, enabled/disabled server synchronization and a shared preference across mode changes/restarts and legacy snapshots.
- **20** Restricted magazine-recovery checks.
- **426** follower return-policy assertions plus membership/snapshot guards.
- English/Russian/Chinese locale JSON parses and includes both restored setting strings.

The storage test build reports NU1900 because the NuGet vulnerability feed is unavailable; its checks pass. Standalone Core client, server and matching addon Debug builds pass with zero warnings/errors. Compiled metadata confirms Core assembly/plugin/server metadata 1.0.1, unchanged addon plugin version 1.0.0 and its reference to Core 1.0.1. Binary reference checks against installed SPT 4.1.6 pass (server: 199 types/772 members; client: 894 types/2,235 members; 456 server mod types loaded). Scoped whitespace checks pass.

With Tarkov and SPT closed, deployed the three Debug DLLs, matching symbols and English/Russian/Chinese locale files; all nine source/destination SHA-256 hashes matched. DLL hashes:

- Core: `70F8B952ECE93E1C2E9F8C0E1A4070CB73A3187F0E4B593C06CEAA5F2AA4B06B`
- Server: `3170AFD8BF2C45D6746C267EC14C27991C52FE292785F03FDE3E42D23D131D3C`
- Addon: `E03C0CFDFAD518D99EC2AE9A20E8E07EE001B9113F52C4063AC4818B35ADFBB8`

Live settings and profile databases were not modified. No release package or Git commit was created. Fresh My Squad rendering and on/off extraction/death qualification remain required; fixture/build/hash success does not establish those runtime outcomes.

## 2026-10-25 - Teammate Profile voice selector

Added `[EDIT NAME] [voice dropdown]` using the existing profile row and EFT `DropDownBox`. Current 4.1.3 source confirms faction-filtered `CustomizationSolver.GetAvailableVoices`, localized voice names, `BodyCustomization[Voice]` customization IDs and native `VoicePreviewSpeaker`. Reviewed the legacy friendlypmc editor and the referenced [client selector](https://github.com/sgtlaggy/spt-HeadVoiceSelector/blob/main/Core/UI/NewHeadVoiceDrawer.cs) and [server selector](https://github.com/sgtlaggy/spt-HeadVoiceSelector-server/blob/master/Mod.cs); the current native APIs provide the required UI and preview without another mod dependency.

Validation completed:

- Standalone Core client/server and serial Debug solution build pass with zero warnings/errors against the configured compile baselines.
- **268** gameplay/storage checks pass, including **32** new production voice-route checks covering USEC/BEAR saves in both gameplay modes, preserved identity/other teammates, rejected wrong-faction/non-voice/missing values and unknown teammate IDs. The fixture uses isolated temporary databases. NU1900 remains the unavailable NuGet vulnerability feed warning.
- English/Russian/Chinese locale JSON parses and contains the centralized voice-error key. Localization callsite and whitespace checks pass.
- With Tarkov/SPT closed, deployed Core/server/matching addon Debug DLLs and symbols; all six source/destination SHA-256 hashes match. Added only the new locale key to installed resources and verified all existing locale data remained unchanged. Replaced files were backed up under the ignored test-artifact folder. Live settings and teammate databases were not changed.

Deployed DLL SHA-256 hashes:

- Core: `A582B73304ECDD5D72A96FB8DDD350F3F74897742E4990A16795E22C7E42449D`
- Server: `207BD76FF8C702693315BDAA594C70A2B653F7A07DD6D1246839DAD73B432FC7`
- Addon: `1E6FC20A403DB7F3195636F7C87D6ABF49130D2D1F3AF5BEA540A72B177748B9`

In-game row alignment at supported UI scales, popup interaction/close/reopen behavior, preview audio and saved voice on the next follower spawn remain unqualified. Existing unrelated local work, including the Debug health preview, remains present; these checks do not qualify it. No release package or Git commit was created.

### Follow-up - Allow all voices

The requested selector now uses every voice template in the native solver's loaded voice dictionary, including USEC, BEAR, Scav/boss and factionless templates. This supersedes the initial faction-filtered selection above. Server validation still requires an owned teammate and an existing voice template, with no faction restriction.

The serial Debug solution build passes with zero warnings/errors. **280** gameplay/storage checks pass, including **44** voice checks: teammates of either PMC faction can save USEC, BEAR, Scav and factionless voices in both gameplay modes; unknown/non-voice/missing values and unknown teammate IDs remain rejected. The cached NU1900 vulnerability-feed warning remains. Whitespace checks pass.

With Tarkov/SPT closed, redeployed the Core/server/matching addon Debug DLLs and symbols; all six source/destination hashes match. Existing locale text, settings and live profile databases were not changed by this follow-up. Updated DLL hashes:

- Core: `56667B992F574CFFE9DEF1FB77E4AAF9E16F86A95BB5F4A4C1B915F95B8BBAA1`
- Server: `9A8A95E0C1436B6C37B4B087C47CD504DA456B47A320F276DEA387D094F46514`
- Addon: `22C3B9BE86AEFC090476FD8D79A4C2EB6D63541574C6E042A32555CF23CE33D7`

Live UI/audio and next-spawn voice qualification remain pending.

## 2026-10-26 - Saved teammate HP and paid Therapist test

The Debug profile overlay now reads actual saved HP. Its HEAL confirmation requests a server quote for HP restoration using the native per-part rounding, configured health-point price, Therapist loyalty coefficient, player Charisma discount and player trial eligibility. It charges unlocked stash roubles, updates Therapist turnover, verifies the player file, and atomically commits teammate HP with the completion receipt. It never changes the player's health. HP effects are rejected in this first pass; raid injury persistence, passive hideout recovery and medkit treatment remain pending.

Validation:

- Debug client build passes with zero warnings/errors. The server and fixture builds pass against the SPT 4.1.0 baseline; cached NU1900 vulnerability-feed warnings remain.
- `tests/TeammateTreatment` passes **26 checks** against native runtime tables and native player-file saves: paid/free treatment, quote side effects, incorrect/stale/expired quotes, insufficient funds, repeated completed tokens, actual teammate-only health changes, saved funds, turnover, interrupted paid/unpaid intents, conflicting recovery, failed player writes and the native cached-save-hash failure case.
- Existing gameplay/storage smoke passes **316 checks**. Locale JSON and seven treatment keys in English, Russian and Simplified Chinese validate. Whitespace checks pass; fixture binaries and private exports remain ignored.
- With Tarkov and SPT stopped, deployed the Debug Core/server DLLs and symbols and merged only the seven new treatment locale entries into installed locales. Source/destination SHA256 values match: Core `2F6337C8746916954D1AE500E6F0E5CF268FFFFB8BAD7AE4D9AFD9DD961FA4C0`; server `EA5D166D04AEE8F89C61D13FD02099A36241895CF3D80DBA266BF98561D79072`.
- Applied the explicitly requested one-time injury to Hawk in pitAlex's Allegiance roster using the offline database utility. Import changed exactly one document, retained an encrypted database backup, and a fresh export confirms **242/440 HP**. Other teammate documents and the player's health/money were not edited. Previous DLLs, symbols and locale files are backed up under the ignored HealthPreview artifacts folder.

The earlier overlay positioning and animation-completion hiding were accepted in-game. The new paid confirmation, live stash/Trader refresh, persistence across game restart and animation after server-confirmed treatment still require in-game qualification. The user will launch the server/game. No release package or Git commit was created.

### Follow-up - Native hideout HP recovery and payment refresh

The user's live test and logs establish one completed Hawk treatment for **7,635 roubles**, with subsequent health reads and no repeat charge. The original whole-stash update logged an out-of-sync warning and successfully used its saved-snapshot fallback. Treatment now uses the existing native money-only update path shared with insurance, with explicit verification of all returned rouble IDs/counts. Normal recovery polls contain no stash/payment snapshot.

Saved health now catches up using native per-body-part regeneration values and loop timing with the player's active HealthRegeneration bonuses and Hideout Management boost. The open profile polls that interval. Legacy/future timestamps, partial-loop remainders, full-health caps and unsupported effects have explicit handling. Quotes permit deterministic elapsed recovery, including negligible floating-point drift across different polling frequencies; the final debit can decrease but cannot exceed the confirmed quote. A Debug diagnostic compares the actual native player rate with the server teammate rate. The player's health and active follower controllers remain untouched.

Installed metadata confirms that missing custom-body armband mappings already use the first native mesh after logging an error. A menu-preview-only prefix selects that same supported key before dressing and emits one compatibility notice per missing body ID, retaining native fallback behavior without mutating mesh tables or saved clothing.

- Debug client build passes with zero warnings/errors. Server/fixture builds pass against the SPT 4.1.0 baseline, with the cached NU1900 advisory-feed warning.
- **44** treatment/recovery checks pass, including native per-part rate arithmetic, hideout/skill boosts, bonus deduplication, unpowered bonuses, elapsed catch-up versus repeated polls, clock initialization/clamping, full-health limits, conservative effects handling, lightweight snapshots and lower actual payment after recovery during confirmation. All previous payment durability checks remain covered.
- Existing gameplay/storage smoke passes **316** checks. Whitespace checks pass.
- After the user closed SPT (Tarkov was already closed), deployed matching Debug DLLs/symbols with verified hashes: Core `632E7E488CA81C0CD08A723AC9ACB9F2E918259520C8885E4FB95BDB98BE7E5D`; server `9618962FAFE52AEE3E3B8C6ECE25EB60BF82343D80EB73BF704B5A9D12AE1833`.
- The offline utility changed exactly Hawk's profile document: **440/440 → 242/440 HP** with a fresh recovery checkpoint. A fresh export verifies the result. It retained an encrypted database backup; prior DLLs/symbols and private exports are under the ignored HealthPreview artifacts folder. No player funds/health or other teammate documents were edited.

Live verification of rate equality, visible passive increments, money-only refresh without fallback warnings, natural recovery's completed-animation hide, and the scoped armband compatibility notice remains pending. Recovery begins at the saved checkpoint, so Hawk may be above 242 HP by the next profile open. Raid injury persistence, effect recovery and medkit treatment remain outside this pass. No release package or commit was created.

### Follow-up - Native recovery Buff readout

The user's next screenshot shows saved passive recovery progressing to 374/440 HP. Current client logs report matching native-player and teammate totals of **668.682 HP/hour**, equivalent to the player's displayed **11.14 HP/min**. The missing indicator was caused by the display adapter's fixed zero `HealthRate` and the side-selection prefab's hidden `Buff` child.

The Debug health overlay now passes the latest server rate through an optional display-controller callback in native HP/min units and enables the existing `Buff` child. The native `BuffableHealthParameterPanel` owns blue positive colour, two-decimal text, up-arrow visibility and animation. Its ordinary one-second updater retains the indicator between server polls, and the server rate refreshes on each recovery response. Other snapshot controllers keep their default zero rate; footer placement and completed-healing hiding remain unchanged.

The Debug client build passes with zero warnings/errors and whitespace checks pass. With Tarkov closed and SPT still running, deployed only the client DLL/symbols, backed up the previous pair, and verified installed SHA256 `9E84FBB5298A7E380CB3A04F5A89BD1A0813E14B014E265D53EFB91755389830`. No server/profile changes or new injury were applied. Live visibility/colour/animation of the updated indicator remains pending the user's next game launch.

### Follow-up - HP-only raid retention, deployment deferred

The user confirmed the native blue Buff value/up arrow working in-game, then requested raid health retention without bleeding, fractures or other persistent effects. The new [health contract](../Teammate-Health.md) covers the completed source implementation:

- Extraction, fallen teammate and resolved Team Escape payloads carry each part's current HP and original spawn maximum, without effect fields. Death snapshots remain available after controller disposal and do not depend on successful gear serialization.
- The server normalizes HP back to saved maxima, applies the configured native blacked/death multipliers (installed 10%/30%), clears every saved effect, and starts a fresh recovery checkpoint. A profile and its internal per-raid HP receipt commit together, preventing duplicate reports from undoing later healing.
- Saved-teammate activation preserves recovered injuries. The spawn multiplier scales current/maximum together. Original maxima survive surgery/transit; generated allies and Debug field recruits retain their existing full-health behavior. Living transit followers keep native carried health/effects until final raid end.

Validation: the Debug client build passes with zero warnings/errors. The minimum-baseline server build passes; the treatment fixture build retains the known NU1900 advisory-feed warning. **65** server health/treatment checks pass, including canonical HP normalization, effect clearing, blacked/death defaults, malformed/partial snapshot rejection, unchanged resources/maxima, Team Escape payload preservation, real encrypted outcome/receipt persistence, duplicate protection, and injured next-spawn state. **14** source-linked client capture checks pass, including surgery, repeated activation/transit, detached payloads, fallen snapshots, teardown and next-raid multiplier changes. Existing gameplay/storage smoke passes **316** checks. Whitespace checks pass.

Tested Debug DLLs/symbols and a hash/check manifest are preserved under ignored `tests/artifacts/RaidHealthRetention/2026-10-26/ready`. Core SHA256: `B3EF54C8889EB57ECAC4D144E6F6392909FB1C36C88AA0F0933B7FDDD04973DC`; server SHA256: `20D5CCA52E2BD83CB80F5C293F89C73AFB0B08E471104397BB36A5A5C739EAB8`.

Concurrent local teammate-editor work rebuilt the main server output after these checks. The preserved server snapshot was recovered from the fixture-local DLL actually used by the passing 65 checks, with its matching symbols and verified hash. Rebuild and validate the combined current tree before any later combined deployment; the snapshot is evidence of this tested boundary, not a substitute for checking newer unrelated work.

**Not deployed:** the process guard still observed SPT.Server after the first close confirmation, and the user explicitly chose to leave deployment pending. This pass replaced no installed binaries and edited no live profile/database. Native spawn/extraction/death/Team Escape/transit qualification must follow deployment; fixture success does not establish those live outcomes. No release archive or Git commit was created.
