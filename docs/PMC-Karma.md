# PMC karma

Core adds friendly encounter karma rules to the native PMC profile's `karmaValue`. This is independent of Allegiance's 24-hour Friendly Encounters penalties and of player-Scav/Fence standing. The same PMC value is used in both modes and remains between 0 and 1.

## Changes

| Event | Karma | Sound |
|---|---|---|
| Human PMC kills a raid recruit or an unrecruited friendly | −0.025 per victim, minimum 0 | Native negative sound when karma actually decreases |
| Human PMC extracts alive with raid recruits | +0.02 per distinct living recruit, maximum 1 | One native positive sound for the combined gain when karma actually increases |
| Completed Allegiance raid had selected friendlies, with no qualifying friendly kill or recruitment | +0.005 once, maximum 1 | Silent |

Spawned and saved squadmates are excluded from the kill/extraction rules, including automatically spawned Goons. Actual successful recruitment registers the bot identity; `IsSquadMate == false` alone does not establish recruitment. Unrecruited eligibility is captured before the player's first damaging hit can trigger retaliation. Guns for Hire uses its existing enabled same-side friendly relationship; Allegiance uses its selected, unrevoked candidates, including the opposite faction. A different aggressor's kill does not charge the player. Player Scav raids do not change PMC karma.

Run-through counts as extraction. Player death, missing-in-action, abandonment and simulated Team Escape never grant recruited extraction karma. Quiet recovery permits completed killed/missing-in-action raids if its other requirements hold; abandonment/disconnection does not qualify. Any successful recruitment suppresses quiet recovery, even when that recruit later dies, is dismissed or fails to extract. Thus recruitment cannot stack the 0.005 bonus with 0.02 rewards. Multiple selected friendlies still produce only one quiet bonus. Merely greeting/requesting a bot without successful conversion does not count as recruitment.

Transit grants neither extraction nor quiet recovery and preserves the current deployment's kill/recruitment history and recruit identities until final raid completion.

## Ownership and persistence

- [PmcKarmaRuntime](../client/Modules/PmcKarmaRuntime.cs) captures relationships, successful recruitment and living followers before native cleanup.
- The existing death and pre-damage handlers call it independently of kill-message settings and encounter-penalty reports. [PmcKarmaRaidEndPatch](../client/Patches/PmcKarmaRaidEndPatch.cs) snapshots `LocalGame.Stop` outcomes.
- [PmcKarmaPolicy](../shared/PmcKarmaPolicy.cs) owns amounts, caps, recovery eligibility and shared report/result DTOs.
- [PmcKarmaRouter](../server/Routers/Static/PmcKarmaRouter.cs) exposes `POST /singleplayer/pitfireteam/pmc-karma`; [PmcKarmaService](../server/Services/PmcKarmaService.cs) serializes it through the existing mode/request gate.
- Native PMC karma and per-raid/victim receipts persist together in the player's SPT profile. Receipts use the existing mod receipt convention under `spt.migrations`, with the `pitFireTeam/pmc-karma/` prefix. They are shared across modes, not written to either roster/encounter-penalty database. A replay returns the original sound decision without applying another change.
- Backend acknowledgements require the on-disk native karma and receipt. SPT's cached attempted-save hash cannot convert a failed disk write into a successful report. Unconfirmed persistence logs an error and emits no sound.
- HTTP runs off the Unity thread. Confirmed results return to the captured game synchronization context, update the owned readonly `Profile.KarmaValue` instance field via verified metadata, and call native `GUISounds.PlayKarmaSound`. This avoids `KarmaClientController` dropping extraction notifications after `GameWorld` has been torn down. No sound asset or global karma controller is replaced.
- Reports retain event order even when HTTP workers start out of order, so a kill immediately before extraction applies its loss before the capped extraction reward.

## Verification

Run `tests/Verify-PmcKarma.ps1` against the installed game, and `dotnet run --project tests/PmcKarma -- <SPT runtime root>` for native backend persistence checks. Client fixtures exercise classification, lifecycle, readonly-field reconciliation, sound decisions, retries, extraction identities and transit. Backend checks use real SPT JSON, profile models and `SaveServer` against temporary profiles, covering persistence/restart, per-user receipts, clamps and failed-write verification. Existing recruitment, hostility and Friendly Encounters fixtures remain separate regression checks. Actual raid playback and native UI timing still require in-game qualification.
