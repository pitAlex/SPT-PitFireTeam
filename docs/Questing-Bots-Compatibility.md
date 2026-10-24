# Questing Bots compatibility

Core owns the optional [Questing Bots](https://github.com/dwesterwick/SPTQuestingBots) follower handoff in [FollowerQuestingBots](../client/Modules/FollowerQuestingBots.cs). It applies to saved squadmates and field recruits, with or without the SAIN addon. There is no typed Questing Bots dependency or shared configuration change.

## Follower ownership

Before follower initialization, Core discovers `QuestingBots.Components.BotObjectiveManager` and calls its public `StopQuesting()` once native initial quest selection has finished. Components and initial selection may arrive after conversion, so the follower update hub retries at 0.5-second intervals. Once ready, Core saves that component's original `IsQuestingAllowed` value, suspends it if enabled and reasserts suspension if it is enabled again while following. Bots already disabled by Questing Bots remain disabled. This avoids capturing the temporary false permission before native initial setup enables questing.

Dismissal, removal and failed follower initialization release the stored state. A living bot whose permission was changed receives its original value through the native property's private setter; Questing Bots 1.1.0 has no public resume method. Death and raid teardown detach without restarting objectives. Cleanup also removes destroyed Unity bots by their managed reference, even when Unity reports them as null; it detaches the saved health listener without reading destroyed bot state or resuming its objective. Raid teardown clears the stored records in `finally`. Exact bot/component ownership, idempotent release and death/update-hub unsubscription prevent a departed follower from retaining callbacks. Missing Questing Bots is a silent no-op; incompatible API or invocation failures are contained and logged once.

The current API is verified against the installed 1.1.0 assembly and [version-matched objective-manager source](https://github.com/dwesterwick/SPTQuestingBots/blob/313d0275e07e84566c3676cc6f194612306026cd/Client/Components/BotObjectiveManager.cs). Native objective assignments, brain layers, groups, identity, combat and spawning remain owned by their existing systems. This handoff does not override Questing Bots for ordinary friendly encounters; peaceful social waiting and combat help retain their own [encounter admission](Allegiance.md#friendly-greetings).

## Validation boundary

`tests/Verify-QuestingBotsHandoff.ps1` checks installed signatures and exercises the production bridge/update hub with controlled native state, including late initialization, originally disabled objectives, duplicate calls, re-enabling, departure, death, teardown, multiple followers, exceptions and an absent mod. It does not simulate Unity scheduling or establish native quest resumption/navigation in a live raid. Dated results belong in the [validation ledger](../addon/docs/Validation.md).
