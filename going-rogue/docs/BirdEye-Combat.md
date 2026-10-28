# Birdeye combat

Core base: [Marksman combat](../../docs/Combat-Tactics.md#marksman-combat-behavior), [cover contract](../../docs/Combat-Tactics.md#cover-contract), [commands](../../docs/Commands.md), [architecture](../../docs/Architecture.md). Shared Goon ownership and Knight coordination: [Knight combat](Knight-Combat.md#native-bosslogic-and-managed-goons).

## Brain and native comparison

`BirdEyeCombatLayer` is a role/brain-scoped BigBrain layer for living, registered Going Rogue followers with a local human leader. It uses the same readiness gate, Core action driver and reversible native-layer lease as Knight and Big Pipe. Both native `BirdEyeFightLayer` and `BirdEyeHoldLayer` are suspended while owned; danger and malfunction layers retain priority. Release restores initialized layers without activating/subscribing them again. Ordinary hostile Birdeye and other modes retain their existing behavior.

The old plugin's `BirdEyeFightLayer` largely delegated to `FollowerSniperLayer`, adding close defensive/holder behavior and secondary switching. The current native `BirdEyeAbstractLayer` supplies a **60m preferred / 40m minimum** firing geometry; its fight layer combines stationary fire, cover/firing-point search and a team-triggered aggressive approach. The native hold layer controls a selected firing area with separate searchers.

The managed brain keeps that sniper geometry through `BirdEyeSniperPositioning` and uses Core's actual `FollowerCombatSniper` for combat execution. The character callback runs only at fresh planning, after medical/urgent work, close weapons, arrival/movement commitments, team support and player Need Help. It selects a shared shooting-cover candidate around the native preferred position, then requires Core's exact-point firing-lane, floor, alternate-threat, standoff, displacement and complete-path admission. Unknown/non-finite targets are refused. Rejected candidates fall back to ordinary Marksman selection; probing is limited to once per second. Native finder publication and blind team-triggered rushing are replaced by these shared transactions.

## Weapons and commands

Close defense and eligible proactive automatic support use Marksman's existing loaded secondary/holster admission, penetration/threat policy, bounded draw/readiness hold and concrete safe movement. A useful current-weapon shot retains priority. An automatic secondary remains selected in close danger; normal-range sniper support returns to the primary after the threat leaves that range. Birdeye positions use the existing `sniper.FireSupport.position.*` intent, so weapon return and firing-position arrival adjustment/wait retain the same owning code. Reloads, ammunition, medicine, grenades and shot safety have no separate Birdeye implementation.

Before follower initialization, a scoped addon patch assigns the runtime Marksman tactic for local Going Rogue Birdeye. This precedes proficiency-template capture and enables Core's Marksman suppression/Need Sniper routing without editing the saved locked tactic. Native generated skills and the existing Proficiency controls retain their current contracts.

Birdeye accepts There, Regroup, Need Help and Need Sniper through Core. Suppression uses the existing admitted automatic-secondary fallback when available. He retains the sniper refusal for assault Go Forward. The player/50m delegation rules for Pipe's offensive push do not turn Birdeye into an assault follower.

Knight's published firing assignment takes priority over Birdeye's autonomous distance preference when ordinary planning is available. It must match the current admitted target and pass Core's backline/Marksman position gates. Neither character-specific position search runs when Core's existing player-distance/recent-fight policy calls for autonomous regroup. Ordinary useful-shot, pressure and committed-cover routing still runs before that regroup choice, and active movement is not interrupted by distance alone. Knight's admitted-target sharing uses the existing same-human coordinator. Human ownership remains intact; player commands, including consumed Regroup and Need Help, urgent work and productive fire protect their current tasks. Birdeye follows independently through Core when Knight is absent or unavailable.

## Profiles and qualification

The server marks Birdeye raid-ready and hides his top profile statistics. Existing native raid kit generation, last-kit preview, boss container/reserves, Proficiency-only editing, fixed gear/donation restrictions, native spawn-inventory protection and no loot returns apply. Birdeye uses the same one-time automatic test-roster seed as Knight and Big Pipe. Existing members are acknowledged without duplication; removing a seeded member preserves the receipt and does not grant him again.

Production fixtures cover the addon policy, native geometry, Knight assignment, guarded routing, secondary preparation/retention, primary return, shared arrival behavior, lifecycle and native-layer restoration. Installed metadata checks resolve the compiled EFT/Core/BigBrain APIs and the follower Init patch. Native navigation, actual weapon draws/reloads, simultaneous Goons fights, suppression/Need Sniper reception, external SAIN configurations and command timing require live raid qualification. See [validation](../Validation.md).
