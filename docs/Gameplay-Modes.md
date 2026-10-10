# Gameplay modes

pitFireTeam has two gameplay modes. Each player profile chooses one on its first **My Squad** visit; later changes use **My Squad → Mode**.

| Behavior | [Guns for Hire](Guns-for-Hire.md) | [Allegiance](Allegiance.md) |
|---|---|---|
| Starting mode | Chosen on first My Squad visit | Chosen on first My Squad visit |
| Building the roster | Manual hiring and eligible field recruitment | Field recruitment only |
| Settings | Saved preferences and ordinary restrictions | Fixed values for the settings listed in its guide |
| PMC recruitment | Existing same-side rules | Selected solo friendlies from either faction |
| Friendly Chance Multiplier | Disabled | 1–5, affecting future friendship rolls |
| Friendly Encounters penalties | No new penalties or effect | Independent 24-hour recruited-friendly kill penalties |
| PMC karma | Shared kill/extraction rules | Shared rules plus quiet peaceful-raid recovery |

Each linked guide owns its mode's gameplay behavior. [My Squad](My-Squad-Screen.md#part-3-mode) owns tab layout, controls and screen feedback. Loadout Management's equipment policies are a separate setting; they are not additional gameplay modes.

## First My Squad visit

Until the initial choice succeeds, My Squad hides its normal panels and tabs and presents two mode cards. Existing accounts also make this choice once; closing the screen before choosing leaves it pending. Choosing the already-active Guns for Hire default still confirms the mode with the server and completes onboarding.

The server persists `firstTimeVisit = true` when the initial choice completes, once per player profile across both mode databases. A squad in either database suppresses the welcome invitation, including when the newly selected roster is empty. Existing rosters are retained.

A successful launcher **Wipe Profile** also clears that account's squads in both modes and resets `firstTimeVisit`, including its welcome-invitation state. The next My Squad visit presents the mode cards again and an empty account can receive a new welcome invitation.

An account with no squad receives one free same-faction PMC friend invitation. The server generates a random level-1 faction kit and identity, fixes level to 1 and XP/common skill progress/mastery to zero, and preserves these values through invitation preview and acceptance. It adds no random weapon specialty or fabricated raid history. The invitation uses ordinary recruitment acceptance and equipment policies; it does not immediately add a teammate or group member.

After the normal My Squad roster/settings refresh finishes, the client acknowledges completion and the server starts a fixed two-second delivery deadline. The exact candidate, selected mode and delivery intent survive restarts. Closing My Squad after acknowledgment does not cancel server delivery. Switching modes defers the welcome invitation until its originally selected mode is active again; it cannot enter the other mode's inbox. A squad acquired before delivery suppresses it. Acceptance, decline, deletion, retries and lost responses never grant another welcome invitation. [Teammate storage](Teammate-Storage.md#first-visit-and-welcome-invitation) owns persistence and crash recovery.

## Separate rosters

Each mode has its own roster database, including equipment, teammate settings and pending recruitment requests. Allegiance starts empty and does not import the Guns for Hire legacy roster. Switching selects the other database without copying members or converting the inactive roster's loadouts. See [Teammate storage](Teammate-Storage.md) for paths, identities, request gates and recovery.

Switching clears the outgoing squad selection, refreshes roster/social data and invalidates delayed invitations from the outgoing mode. It does not delete the outgoing roster. Switching is unavailable during raids; the in-raid Squad Settings overlay exposes Settings only.

## Saved settings and restoration

The selected mode persists in the hidden `00 GameplayMode` config entry. Before entering Allegiance, [GameplayModeRuntime](../client/Modules/GameplayModeRuntime.cs) atomically snapshots all currently bound mod settings except the mode and Allegiance-only Friendly Chance Multiplier to `<config path>.guns-for-hire.json`. Returning to Guns for Hire restores that snapshot, including after restarting the game. The multiplier retains its separate saved Allegiance preference.

Allegiance leaves saved config preferences intact. Gameplay consumers, the Settings UI and server synchronization resolve its fixed values through `GameplayModeRuntime.GetEffectiveValue`. Editing or reloading the BepInEx cfg cannot override those values while Allegiance is active. The fixed-value table belongs to [Allegiance](Allegiance.md#settings-policy).

Mode changes use the coordinated transition rather than a live cfg reload. Reloading a different mode value during the session is rejected. A missing or invalid Guns for Hire snapshot fails restoration without overwriting it or recreating it from potentially forced values left by older builds.

## Transition coordination

Transitions wait for registered post-raid reports and serialize server settings requests. The server serializes teammate operations with database selection so a request cannot cross roster roots while it runs. A lost settings response is checked against the server's actual mode before rolling back; a failed transition restores the preceding client mode/settings and attempts server reconciliation. These rules are implemented by [GameplayModeRuntime](../client/Modules/GameplayModeRuntime.cs) and the existing teammate operation gate.

## Shared boundaries

Both modes retain follower lifecycle, command safety, perception and permission-to-fire rules. Allegiance friendship does not grant sight of a bot or authority to fire. Neither mode depends on the optional SAIN addon to perform recruitment or relationship repair; [external-SAIN compatibility](SAIN-Compatibility.md) remains Core-owned.

[PMC karma](PMC-Karma.md) is one player-profile value shared across both modes. It is independent of Allegiance's encounter penalties and of player-Scav/Fence standing. A mode switch cannot reset the Allegiance penalty ledger or its real-time expiry.
