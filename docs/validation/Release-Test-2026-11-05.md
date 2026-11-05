# Release test candidate — 2026-11-05

Scope: Core **1.1.1** plus Going Rogue **1.0.0**, with matching client/server Release builds for SPT 4.1.6. This is preparation for testing, not a public release or proof of live gameplay. The external SAIN plugin and separate SAIN addon are optional and are not included in these two packages. The initial 0.1.0 package/deployment below is superseded by the 1.0.0 version correction.

## Description and cleanup

[Going Rogue's description](../../GoingRogueDescription.md) documents Allegiance integration, level-20 trader access, seven quests, permanent companions, current Rogue recruitment/penalties, equipment restrictions and self-contained quest loading. Team Split and Team Combine are described under Command, including eligibility, peaceful behavior, combat phrase exclusion (including Attention), gestures, per-member 40m Need Help, Status Report and Combine's ordinary player Regroup.

Removed the obsolete Version 1 task note and the Going Rogue roadmap that repeated implemented behavior. Retained future proposals in [Core roadmap](../Roadmap.md) and the quest-chain extension note in [Knight quests](../../going-rogue/docs/Knight-Quests.md). Superseded incremental quest/UI/temporary-kit validation notes are removed from the current addon ledger; unique route-safety, support, current checks and qualification evidence remain. Removed dead historical document links without presenting old evidence as current. Source artwork and current engineering contracts are retained.

## Build and package checks

Release client and server builds pass with zero warnings/errors against the existing 4.1.0 minimum SPT API baseline. Current version metadata is Core 1.1.1 / Going Rogue 1.0.0. Installed SPT 4.1.6 compatibility and fresh feature fixtures are recorded with the package manifest. No native quest dialogue, objectives, live profile or database is changed by release preparation.

Fresh controlled checks: Team Split 326; Knight/Pipe combat 1,475; Goon coordination 920; point movement 220; native trader/quest catalog 1,947; storage/modes 650; quest runtime 113; zones 27; trader visibility 25; Rogue alliance 257; Rogue reports 20; native waves 11; AcceptScreen preparation 43; companion previews 107; menu filtering 13 — **6,154 assertions**. Server-backed trader/storage fixtures execute Release outputs. The initial parallel project-reference build failed without compiler diagnostics; rebuilding those fixture hosts serially (`-m:1 -nr:false`) succeeded. Product Release builds passed independently.

Installed API checks resolve 225 Core server types / 862 members and load 560 mod types; Core client resolves 921 types / 2,323 members. Going Rogue Release resolves 749 Core/EFT/BigBrain/Harmony references and 20 patch targets, plus shared Rifleman planner parity. Zone/preview native-boundary checks also pass. No new live startup, trader transaction, quest completion, UI rendering or raid result is claimed.

Archives must extract directly to `BepInEx/` and `SPT_Runtime/`, include current resources and exclude references, profiles/databases, test kits, artwork source, build caches and PDBs. Separate symbols are retained for local test diagnostics. Each packaged entry is read back and hash-compared to staging, with package SHA-256 recorded in the manifest.

## Live test checklist — pending

| Area | Required observation |
|---|---|
| Fresh installation | Server/client startup; Core alone; then matching addon pair; no missing dependency/resource errors |
| Modes and trader | Two gameplay modes; Knight visible only in Allegiance; level 19 locked, level 20 accessible; leaving/returning preserves progress |
| Quest chain | Seven native tasks, rewards, required Goon previews/spawns, quest 3 saved bag selection, Off Limits pier trigger, native Partizan event/chance ownership |
| Roster and progression | Permanent pinned Goons, level 60/Sherpa icon, native starting skills and learned progress, Proficiency-only controls, AcceptScreen kit equals spawn/transit kit |
| Combat compositions | Each Goon alone, Knight + Rogue, Knight + Pipe, Pipe + Rogue, Birdeye support, full squad; removal/death restores the correct commander/settings |
| Assault behavior | Knight/Pipe take favourable initiative; distant running and close walking; harassment differs from damage; cover arrivals preserve unit intent; reload/medical/grenade/fire safety still interrupt |
| Team commands | Both under Command with filtering on/off; binding survives eligibility/split changes; peaceful arming; ordinary combat phrases affect only player side; Attention resets both sides and shared memory; Birdeye accepts Need Sniper; gestures, Status Report and individual 40m Help; Combine regroups to player |
| Eligibility loss | Commander death/change, last subordinate loss and last player-side loss discard split without issuing an automatic Combine regroup |
| Diplomacy | First-quest Rogue friendship; full-chain/standing recruitment; hostile tasks; Goon/PMC and Scav encounter rules; ambient betrayal versus saved-member exemptions |
| Gear and ownership | Plate-only donation, secondary/supply admission, preserved reload room, rejected body/container commands, native-kit protection and recovery of player gifts |
| Core regression | Ordinary follower combat/commands, recruited-Scav neutrality, staged Give Gear/Swap Gear, protected loans during PMC invitation, death/interruption during Give Items |

Terrain limitations remain in the public Known Issues: bushes, stairs/floors, stuck routes and teleport recovery. Failed-path commander-regroup recovery and cumulative medical-retreat distance remain separate open investigations. Do not require reproduction of an identical rare fight to collect useful recorder evidence.

## Initial artifacts and installation — superseded by version correction

Verified candidate directory: `F:\Projects\releases\SPT-PitFireTeam\release-test-20261105-131f2af7` (machine-local artifact location, not an installation instruction).

| Archive | Entries | SHA-256 |
|---|---:|---|
| `pitFireTeam-1.1.1.zip` | 26 | `E5EC11AC7C8994FFECC8C79AE0AF8A4F13CFF926F047199FBE1D67228C38A849` |
| `pitFireTeam-GoingRogue-0.1.0.zip` | 12 | `C622995D3A834E268C3D59D49F216394E541C31532F6B7979064CC0A8F084086` |
| `symbols.zip` | 4 | `0B9921943993A7E0CF624E31B31FAAD92FA6533632D2924DC9D79E0B80A6795D` |

Each entry was read back and hash-compared to staging. Core includes client resources, locale files, courier portrait, server deps and LiteDB; Going Rogue includes the current seven quest images, Knight portrait and addon client/server outputs. Runtime SPT/Harmony/BigBrain assemblies are excluded; the local server Harmony dependency matches the installed runtime exactly. The existing server addon folder `pitFireTeam-GoingRogue` is preserved to avoid duplicate mod installation. An initial staging-only folder mismatch was caught, the rejected candidate was deleted, and these archives use the corrected path.

Full file hashes and source snapshot metadata are in `manifest.json` beside the archives. The local test candidate also includes the addon description, installation notes and this checklist. Preparation did not install files; the subsequent authorized test deployment is recorded below. No profile/database was edited, no game/server process was launched, and nothing was published or committed.

## Test deployment — 2026-11-05

At the user's request, deployed the verified Release packages to the game, with both Tarkov and SPT.Server stopped. All **42** client/server runtime, resource and matching symbol files were backed up where present, installed and SHA-256 verified. No saved configuration, teammate database, player profile or battle record was replaced. The separate SAIN addon and external dependencies were not changed.

[Deployment manifest and backups](../../tests/artifacts/release-test-20261105/deployed-0acd39192ea34b4193a44f0ae782a086/manifest.json). Installed DLL SHA-256:

- Core client: `2A33DA93A697836E06A7DFA71D7C8F6853D989825AB56F1CDBA640730483C44E`
- Core server: `CC4DFDF88BAFB4B8FD533B17B6AF35552735E46BBD16B55E91F2E603D6F30054`
- Going Rogue client: `67E1DEF421B73C89CE3BFFC392FB5CDC773AEFBB0CE87F98CE806A4CC2586869`
- Going Rogue server: `27CD34DD64AEB6D542AF0C1382605DEAAF33BE66EAA4554922C45745B47E0005`

The game/server were not launched. The live checklist remains pending.

## Going Rogue 1.0.0 version correction — deployment pending

At the user's request, Going Rogue client/server project versions, BepInEx metadata and native SPT mod metadata now report **1.0.0**. Core remains **1.1.1** and the addon minimum dependency remains Core 1.1.1. Both Release builds pass with zero warnings/errors; compiled assembly/plugin metadata is verified at 1.0.0 / 1.0.0.0. Installed API resolution passes 749 references and 20 patch targets with shared Rifleman planner parity. No gameplay logic changed.

The current candidate is `F:\Projects\releases\SPT-PitFireTeam\release-test-20261105-55aa8e2f`, containing verified `pitFireTeam-1.1.1.zip`, `pitFireTeam-GoingRogue-1.0.0.zip` and `symbols.zip`. Archive SHA-256 respectively: `DD16D4E63AEAA7603DF746EB0B17B505184A72A07EE033A2E64DC6F20C8FB85A`, `9131B62DFD1F7966101FDB7AF38D10FFDA8532E32326699DCE60E773AA482464`, `A99AE03174BF056881D6A7FAC60C0AC31025BA8DB2DAFB40038643EAADFF37EF`. The candidate manifest contains all entry hashes.

Deployment was blocked before any copy because SPT.Server PID 9480 and Tarkov PID 34412 were running. The installed addon therefore remains the previously deployed 0.1.0 build until both are stopped and the pending deployment completes.

## Final release archives — 2026-11-05

At the user's release/commit request, finalized `pitFireTeam-1.1.1.zip` and **`pitFireTeam-GoingRogue-1.0.0.zip`** in the release destination configured in LOCAL.md. Their SHA-256 hashes match the corrected 1.0.0 candidate above. Rebuilt both Release projects successfully with zero warnings/errors; all archived entries match current Release outputs/resources. Rechecked compiled client/server addon metadata at 1.0.0 and the 1.0.0.0 assembly versions. The 10 production NPC message checks pass alongside the previously recorded feature qualification.

Both runtime archives have direct `BepInEx/` and `SPT_Runtime/` roots, exclude PDBs/private references/state, and preserve the established mod directories. Separate diagnostic symbols remain with the test candidate. Final manifest: `pitFireTeam-1.1.1-GoingRogue-1.0.0-manifest.json` beside the ZIPs, including per-file and archive hashes. All current repository source, documentation and fixture changes are included in the requested commit; generated artifacts and machine-local notes stay outside Git. No tag, remote push or external publication is performed by creating these archives.

## Attention and split sniper correction — Release refresh and deployment

The later correction restores Attention as the existing global reset for all followers and shared group memory, regardless of split state, and admits Birdeye to the existing Need Sniper handler while split. The split latch remains armed. Current source/description changes follow the earlier release commit. Validation: 394 split/command/memory checks and 1,705 SAIN addon combat checks; Release build zero warnings/errors; 749 installed references/20 patch targets and shared planner parity pass. See [Team Split validation](Team-Split.md) for exact scope and pending live qualification.

Both final ZIPs were refreshed; prior same-named archives were backed up. Current SHA-256: Core `44277DBA1831A60677E6A795DB1B6AF6F37E4A6ABF9D9D66A425F93D4A44BB20`; Going Rogue 1.0.0 `DC475C16170621D46BB0C8BAEE7BF326E25FF541F96DC58BDD8AF1E37DB607FA`. All entries match the current Release outputs/resources.

The previously pending 1.0.0 installation is now complete. Both processes were stopped; all 42 runtime/resource/symbol files were backed up where present, copied and hash-verified. [Current deployment receipt](../../tests/artifacts/release-test-20261105/deployed-8f8f470efb4d4f6d958386023daf0b14/manifest.json). No live profile/database was edited or game/server process launched. Earlier 0.1.0 installation/hash statements above describe historical deployments only.
