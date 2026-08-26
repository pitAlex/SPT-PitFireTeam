# SAIN Integration Ownership

**Status:** authoritative architecture contract

This document defines the boundary between the external SAIN plugin, the pitFireTeam core plugin, and the optional pitFireTeam SAIN addon.

## Terminology

- **SAIN plugin / SAIN mod**: the external `me.sol.sain` plugin.
- **SAIN addon**: pitFireTeam's optional `xyz.pit.fireteam.sainaddon` DLL under `addon/`.
- **Core combat**: pitFireTeam's custom follower combat brain under `client/BigBrain`.
- **SAIN combat**: follower combat decisions and actions owned by SAIN after the addon hands combat-brain ownership to SAIN.

## Authoritative addon purpose

The SAIN addon's purpose is to switch follower combat-brain ownership:

| Runtime | Follower combat-brain owner |
|---|---|
| SAIN not installed | pitFireTeam core combat |
| SAIN installed, addon absent | pitFireTeam core combat |
| SAIN installed, addon present | SAIN combat |

The addon contains the integration glue needed to select the SAIN combat path, prevent a conflicting follower layer from owning the same bot, bridge pitFireTeam commands into that path, and return lifecycle control safely. It may also contain tuning that is intentionally specific to followers while the SAIN brain owns combat.

The addon must not be required for:

- follower Vision, Precision, or Reaction controls,
- follower proficiency defaults or tactic-relative proficiency,
- normalization against a SAIN preset,
- fixes for external SAIN calculations that conflict with pitFireTeam followers generally,
- compatibility required while the core combat brain owns the follower.

The addon may tune SAIN-brain-specific decisions, actions, movement, search, cover, firing behavior, or other performance characteristics when those changes exist to improve the SAIN follower-brain experience. Such tuning must be explicitly scoped to addon-owned SAIN combat and documented as an intentional difference, not presented as a general SAIN compatibility fix.

Installing or removing the addon changes which combat brain owns the follower, so intentional brain-specific behavior and baselines may differ. It must not silently change the persisted meaning of the same proficiency percentages: `150%` remains a `1.5x` modifier against the finalized baseline for the active follower mode.

## Core ownership while SAIN is installed

The external SAIN plugin patches several EFT calculations whenever `GetSAIN(bot)` succeeds. Those calculations can remain active even when pitFireTeam core combat owns the follower and SAIN combat layers are disabled.

Therefore, follower-specific compatibility with SAIN calculations belongs to the core plugin and is gated by external SAIN presence, not addon presence. `FollowerSainProficiency`, the final follower aim-time modifier, and any required final Precision/Vision/Reaction compatibility boundary must behave consistently in both of these configurations:

- SAIN installed, addon absent,
- SAIN installed, addon present.

The addon may consume the already-finalized follower state and add intentional SAIN-brain-specific tuning. It must not be the only place that repairs a general external SAIN conflict, because the same conflict can affect followers when the addon is absent and core combat still owns them.

## Current implementation classification

The existing addon source contains follower-tuning patches for aim sway, hit accuracy, recoil, low light, foliage/bush handling, and personality/template fine-tuning. Each must be classified before the addon is re-enabled:

- If it deliberately improves behavior only while the SAIN brain owns follower combat, it may remain in the addon as documented SAIN-brain tuning.
- If it corrects an external SAIN conflict that also affects core-owned followers, the general fix belongs in the main plugin and must work without the addon.
- A patch may need to be split: the general compatibility correction lives in core, while an additional SAIN-brain-only tuning layer remains in the addon.

Command, enemy-state, friendly-fire, and lifecycle bridges remain addon concerns when they are necessary to operate the SAIN brain. Calling a SAIN API by itself does not make a general compatibility fix addon-owned; scope and runtime ownership decide placement.

## Review rule

For every SAIN-related change, answer these questions before choosing its project location:

1. Does this change select or operate the SAIN follower combat brain? If yes, it may belong in the addon.
2. Does this repair an external SAIN conflict that also exists while core combat owns the follower? If yes, it belongs in the main plugin and must not require the addon.
3. Does this deliberately improve only the addon-owned SAIN brain? If yes, it may remain in the addon and must be documented as brain-specific tuning.
4. Does a saved proficiency percentage retain the same multiplier meaning in both modes, even if their finalized brain-specific baselines differ? If not, the boundary is wrong.
