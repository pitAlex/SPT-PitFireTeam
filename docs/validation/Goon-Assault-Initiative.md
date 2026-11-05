# Goon assault initiative — 2026-11-05

Contracts: [Knight](../../going-rogue/docs/Knight-Combat.md#assault-roles-and-unit-initiative), [Big Pipe](../../going-rogue/docs/BigPipe-Combat.md#assault-initiative), [Birdeye](../../going-rogue/docs/BirdEye-Combat.md), [Core combat](../Combat-Tactics.md), [commands](../Commands.md).

The Shoreline record `20261105-062352-Shoreline.jsonl` showed Knight with a recruited Rogue but no second Goon retaining the solo stance, walking long remembered-contact approaches, and cycling optional protection/recovery holds against ordinary Scavs despite no actual damage. This change addresses role priority and unit intent; it does not change aiming, generated skills, equipment, perception or shot admission.

Knight plus a living owned Rogue now qualifies for the existing Knight-led unit stance. Knight and Pipe can choose favourable bounded assaults before optional escort/support work. Explicit Help, orders, medicine, weapon preparation, launcher opportunities, explosives and usable immediate fire retain priority. Harmless ordinary-Scav pressure can permit continued advance, but actual damage, critical health and dangerous threat assessments still block it. Published authority carries this specific pressure admission without issuing a new directive revision. Same-target tactical/cover arrival pauses retain an existing advance; recovery, changed targets and explicit orders do not.

Approaches reuse Core's complete-path, physically supported, remembered-contact planner. Safe distant steps select run above 30m, with native sprint capability gates; close steps walk. Only marked Knight/Pipe point decisions apply sprint after the native point update. Ordinary point movement retains its original order. Birdeye retains the supporter/Marksman policy and existing Knight → Pipe → player selection. Rogue combat percentages remain 60% without Knight and 68% with Knight; Pipe's Knight-unit baseline remains 70%.

## Controlled validation

- Production Knight/Pipe objective and safety fixtures: 1,475 checks.
- Production composition, commander/delegation and pressure-admission fixtures: 920 checks.
- Production Core point-action native-update and movement admission: 220 checks.
- Existing suites: group-follow/path/floor 82; Team Split 326; regroup 83; Rogue routing 58; On Your Own 342; companion anchors 130; SAIN addon combat 1,705.
- Debug Core + Going Rogue build: zero warnings/errors. Installed SPT 4.1.6 API resolution: 753 references and 20 patch targets; shared Rifleman support-planner parity passes. No typed SAIN dependency added.
- `git diff --check` passes. These checks use controlled EFT inputs and metadata; they do not establish observed Unity navigation or raid combat behavior.

Failed-path regroup recovery and cumulative medical-retreat distance are separate investigations and are not changed here.

## Deployment

Deployed Debug Core and Going Rogue client DLL/PDB outputs with Tarkov stopped, previous-file backups and all four installed SHA-256 hashes verified. [Deployment receipt](../../tests/artifacts/goon-assault/deployed-4b8b2a68aea541339441392f3afa7be1/manifest.json).

Core DLL: `41F85DB7FFF696471E02FC78B8576F00D4C32045EF6B61E7CECDD344F845A7E7`. Going Rogue DLL: `9743E9E7CB21B8E47A13A0064B31FFDB78131F9842120E340BE2FEB07A3361A9`. No server/profile files were written and no game/server process was started. Live raid behavior remains unqualified.
