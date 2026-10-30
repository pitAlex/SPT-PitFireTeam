# Teammate health

Core owns saved teammate HP, post-raid normalization, hideout recovery and treatment. The optional combat addon consumes the resulting runtime health without owning persistence. [My Squad](My-Squad-Screen.md#skills-panel) describes the native health overlay and recovery indicator; [Team Escape](Team-Escape.md) owns escape decisions and [Loadout Management](Loadout-Management.md) owns gear.

## Raid retention

Normal extraction, fallen teammate reports and resolved player-death Team Escape outcomes carry HP for all seven real body parts. The payload contains only each part's current HP and its original spawn maximum. It cannot carry bleeding, fractures, destroyed-part status, stimulant buffs or other effects. The server also clears any existing saved effects when it accepts the outcome.

Saved maximum HP remains canonical. The server converts current raid HP using `savedMaximum * raidCurrent / originalSpawnMaximum`, capped to the saved maximum. This reverses temporary health multipliers and retains surgery's HP loss without promoting a repaired limb's reduced runtime maximum into full saved health. The client captures original maxima once when a saved teammate activates and retains them across transit.

SPT's configured post-raid multipliers apply: a surviving zero-HP part returns at `healthMultipliers.blacked` of its saved maximum; a lost/dead teammate returns with every part at `healthMultipliers.death`. Installed defaults are 10% and 30%. No cursed/hatchet-run equipment penalty is inferred for teammates. Energy, hydration, temperature and maximum HP are not overwritten by this HP-only outcome.

In Restricted, raid HP retention requires Field Upkeep; a missing/null preference means disabled. Immersive and Realistic retain raid injuries automatically. The server checks this policy when processing the final raid outcome, independently of the selected loadout. Turning Field Upkeep off does not heal existing wounds or disable recovery, paid treatment, wounded portrait styling or full-health raid admission. It only discards new post-raid HP changes in Restricted, leaving saved HP and its recovery timestamp untouched.

A malformed, partial or legacy payload leaves existing HP untouched. HP commits require the native per-raid server identity. The internal `health/raid-outcomes-v1` receipt set records each raid/teammate pair in the same database batch as its profile, so repeating a report cannot reapply an old injury after recovery or payment. Disabled outcomes also receive a receipt, preventing a retry after re-enabling Field Upkeep from applying discarded injuries. Existing equipment/stat outcome processing keeps its own ownership.

## Next spawn and recovery

Fetching a saved teammate for a new raid applies elapsed hideout recovery and the full-health readiness gate before creating the spawn clone. Health multipliers scale both current and maximum HP, preserving the ratio; only fully recovered saved teammates can enter a new raid. The clone drops saved effects. Saved-teammate activation does not heal damage taken during activation; generated allies and Debug field recruits keep their existing full-health activation behavior.

## Raid readiness

The native teammate body-part overlay, recovery indicator and paid HEAL button are included in all client builds, including Proficiency-only companion profiles. They read saved HP and appear only while wounded. Field Upkeep controls retention at the raid-result boundary, not access to treatment: disabling it cannot hide or bypass existing injuries. Only the optional player/server regeneration-rate comparison remains Debug-only.

A teammate is wounded while any of the seven native body parts is below its maximum HP; missing/invalid health is not raid-ready. Roster summaries and a lightweight health-status route expose that state after applying elapsed recovery. The roster tints the native `PortraitRoot/Portrait/Background` image and `PortraitRoot/pitFireTeam_PortraitBorder` outline red for wounded teammates, restoring their original colours when they finish recovering or receive treatment. The outer tile retains its faction/companion colour and hover styling.

Manual raid invitations decline wounded teammates through the existing native decline notification and system-message path, using the central localized template `{0} is still wounded and cannot join a raid yet`. Auto-join excludes wounded teammates without changing the saved auto-join preference. The spawn endpoint rechecks full health as a final admission boundary. Recovery is refreshed before each check; companion combat availability and the existing proper-kit requirement remain independent. Already-running native followers are not dismissed or modified by this menu gate.

The visible roster requests HP readiness on opening and polls every three seconds without loading full profile/inventory snapshots or rebuilding portrait models. A component attached to the roster panel owns this polling, so it continues when the panel moves into side selection and its main-menu owner becomes inactive. Cached portraits refresh after raids and profile returns without needing a loadout edit. Fully healed teammate checkpoints are left untouched by polling.

Transit continues the same raid: the carried native health/effect snapshot remains authoritative and no post-raid treatment is performed on living transit followers. Original spawn maxima remain available until final teardown; captured outcome payloads remain independent of later runtime mutations/disposal.

An accepted raid result sets a fresh `Health.UpdateTime` checkpoint. The existing server recovery calculation and Therapist payment journal then operate on the retained HP. The server owns healing/payment; the client never applies its snapshot to the player's own health. Effects are intentionally discarded at the final raid boundary, so they cannot block passive recovery or paid HP treatment afterward. Stash-medkit treatment is not implemented.

## Source and qualification

| Boundary | Source |
|---|---|
| Passive HP capture and original maxima | `client/Modules/FollowerRaidHealthRetention.cs` |
| Spawn eligibility/activation | `client/Patches/BotsControllerPatch.cs` |
| Extraction/death payloads | `client/Modules/InteractableObjects.cs`, `FollowerDeathEscapeResolver*.cs`, `NpcMessage.cs` |
| HP-only server policy | `server/Services/FriendlyTeammateRaidHealthPolicy.cs` |
| Profile/receipt commit and spawn clone | `server/Services/FriendlyTeammateService.cs` |
| Recovery and treatment | `server/Services/FriendlyTeammateRegeneration.cs`, `FriendlyTeammateService.Treatment.cs` |

Fixture/build evidence and live raid qualification are separate. See the [1.0.1 validation ledger](validation/Release-1.0.1.md) for initial retention checks and remaining extraction/death/transit tests, and the [1.1.0 validation ledger](validation/Release-1.1.0.md) for the Field Upkeep policy and normal-build health UI checks.
