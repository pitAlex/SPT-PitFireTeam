# Knight quests

Core base: [architecture](../../docs/Architecture.md), [localization](../../docs/Localization.md) and [Knight trader](Knight-Trader.md). Further relationship/recruitment changes remain in the [roadmap](../Roadmap.md).

Going Rogue registers its first three legacy quests through SPT's native `CustomQuestService`. The addon embeds the definitions and EN/RU/GE/RO locales, with English fallback for other locales. No Virtual's Custom Quest Loader, Virtual's Custom Quests content pack or WTT dependency is required. Native EFT/SPT owns quest status, counter persistence, item handover, restart handling, rewards, notifications and the second quest's delay. Quest 3's container placement remains unconfigured pending the user's target ID; the quest and special item are registered, but its launcher cannot yet be recovered from the map.

The first quest uses `/files/quest/icon/KnightCompetitionQuest.jpg`. My Land uses `/files/quest/icon/KnightMyLandQuest.jpg`, the enhanced Customs stronghold screenshot, with dialogue describing occupying the base rather than reclaiming an existing home. They robbed us uses the original `/files/quest/icon/KnightThieves.jpg`. Artwork is served from the addon's `Resources/quests/`; PNG working artwork is not deployed.

## Who let the dogs out?

- ID: `6775d9957e2dbcb3bd0a02c7`; available at player level 20.
- Eliminate five targets on each of Woods, Shoreline and Lighthouse, with a living allied Knight within 80 metres. Only the human player's kills count.
- The player and Knight must survive. Knight's death on one of those maps fails the active/ready task. The legacy native exit conditions also fail KIA, Left and MIA on those maps.
- Rewards: 32,500 XP and +0.1 Knight standing. Restartable.

The description has two clearly separate paragraphs: Knight's original competition dialogue, then plain player-facing requirements in third person. The second paragraph states five personal kills per map, automatic Knight deployment when entering an objective map alone as a PMC with the quest active, and the 80-metre distance at each kill. It explicitly rules out leaving Knight in one place while hunting elsewhere and states that either the player or Knight dying in a shared quest raid fails the task. It also states that dying in a raid without Knight does not fail it. It is not phrased as Knight asking the player to keep him alive. EN/RU/GE/RO descriptions separate the blocks with `\n\n`; native `NotesTaskDescription` assigns the localized description directly to `TMP_Text.text`, preserving the blank line. Objective rows and the note retain the actual kill/proximity/survival requirements.

The old counter patch admitted any Goon's presence for the personal-kill objectives, despite their text requiring Knight. The new admission requires Knight specifically. Ordinary teammate, Big Pipe and Birdeye kills do not count.

## My Land

- ID: `6775da597e2dbcb3bd0a02c9`; level 20 and successful completion of the first quest, followed by the legacy 3,600-second delay.
- Eliminate eight targets in Customs Scav Base, using the native victim-zone condition `prapor_27_1`, with a living allied Knight within 80 metres. The human player and Knight can contribute; other teammates cannot.
- Knight's death on Customs fails the active/ready task. The legacy native player exit conditions also fail KIA, Left and MIA on Customs.
- Rewards: 24,000 XP, +0.1 Knight standing and $2,000. Restartable.

Neither task needs custom zone creation. The legacy text mentioned Ran Through failures, but neither JSON exit condition included that status; the port preserves those actual conditions and corrects the text. Obsolete parenthetical promises about legacy chat invitation unlocks are removed. Automatic test seeding of Knight, Big Pipe and Birdeye is disabled. Quest completion does not yet grant permanent invitations, exUsec friendship or encounter/recruitment changes.

## They robbed us!

The Goons stayed at the Lighthouse chalet, where Pipe left some of his equipment. When they returned the next day, his launcher alone was missing and the rest of his gear was untouched. Knight concludes that the thief specifically wanted the launcher and waited for an opportunity. His inquiries lead to Streets of Tarkov; the dialogue leaves the person behind the theft unidentified for quest 4. This theft is separate from conquering the Customs stronghold in My Land.

The description is two paragraphs of in-character dialogue only. Its straightforward retrieval objective does not need explicit mode, FIR, team-composition, distance or failure explanations in Knight's speech or an additional note. Native objective rows retain the find/handover item requirements.

- ID: `677687f2fa281ca31708b17a`; level 20 and My Land successfully completed, with no additional delay.
- Find and hand over one exact special launcher (`64e3b0f4e3b6f5a4a37e8c1d`) Found in Raid. The ordinary M32A1 cannot satisfy either native objective. Native quest inventory and item handover own possession and consumption.
- Streets of Tarkov retrieval, without a kill objective, companion proximity/spawn requirement or custom death-failure condition. Teammates may accompany the player. Going Rogue remains mandatory.
- Rewards: 15,000 XP, +0.1 Knight standing and $2,000. Original restartable flag and no failure conditions retained.
- Native `CustomItemService` clones the current M32A1 definition with legacy quest-item/ungivable/unbuyable flags, size and handbook price, preserving current game fields without modifying the ordinary weapon. The original five-item default preset and attachment IDs are registered before native preset initialization. The addon owns the new item through SPT's mod-item cache; ordinary PMC loot and flea listings do not gain this quest item. Item and quest text live in the embedded addon locales.

**Pending container binding:** the user will supply the current target container ID. No old ID, guessed destination or automatic container authoring/resolution system is installed. Map loot injection is deliberately deferred until that binding can be verified. The original two sports bags are reference material only. A profile that already possesses the correct FIR item can satisfy the native objectives; the addon does not grant it automatically.

## Quest companion deployment

On a PMC raid with no selected group member, an enabled Knight trader and a Started task, Going Rogue requests the task's companion automatically. The addon admits the first task only on Woods, Shoreline and Lighthouse, and My Land only on Customs. Core's selection path requires Going Rogue and an empty group; other modes and raids with any selected teammate do not request a quest companion. These checks are independent of the roster and do not require a Knight invitation.

The server separately checks the active mode's registered companion policy, PMC raid configuration, selected-member count, quest status/map, level 20 and enabled trader. It generates the native role and reuses the normal boss reserve/magazine provisioning without saving a teammate or default loadout. Knight joins through the boss-ally path with `squadMate: false`; Core waits for both registration and addon combat readiness. Fresh Going Rogue bosses retain their generated health instead of receiving the legacy extra head/limb multipliers. Existing saved-party preparation and transit reuse remain in place.

For the initial test, accept Who let the dogs out, leave the group empty and enter Woods, Shoreline or Lighthouse. Human kills with a living Knight within 80 metres should advance that map's native counter. After extracting, invite a regular teammate and enter another raid: quest Knight is absent and the custom kill counters do not advance. A raid with no participating Knight also cannot trigger either task's player-exit failure counter, even if the player dies. Knight participation is scoped to the current GameWorld and cannot carry into a later raid without him; after Knight participated, follower cleanup cannot erase that participation to evade player-death failure. Switching modes preserves saved progress while disabling quest listing/acceptance/completion, auto-spawn and custom counter updates.

## Admission and ownership

The addon filters its quests from native quest listings outside Going Rogue without deleting saved profile progress. Accept/complete/item-handover requests reuse Knight's server mode, level-20 and native disabled/access checks. In-raid custom counters are inactive outside Going Rogue, including failure counters. The third task uses native item possession/handover rather than the companion kill/failure counters.

The native static `ConditionCounterManager.Test(int, TaskConditionCounter, ConditionCheck[])` prefix admits only the local player's quest instances. Existing Core teammate-credit calls temporarily expose the actual killer profile; personal and team objectives gate those identities separately. Unrelated counters are unchanged.

Knight is an allied recruited member without Core squadmate quest-credit privileges. The addon records his successful local follower registration for the current GameWorld, then captures kill/death identity before native follower cleanup. The participation marker lets native exit failures work after cleanup without affecting a later raid where Knight never joined; raid selection and world changes reset it. His kills submit native kill/location/victim-zone checks only to My Land's counter manager; a scoped gate prevents that event from touching any other counter. It never replaces the quest controller's profile or changes general teammate quest credit. Victim callbacks are deduplicated per GameWorld. Knight death uses native quest status transitions, including restartable failure, and is captured before cleanup can lose ownership.

Source: `server/KnightQuestModule.cs`, `shared/KnightQuestPolicy.cs`, `client/Quests/KnightQuestRuntime.cs` and `server/Resources/quests/` in this addon. Fixtures/build/installed metadata evidence is recorded in [validation](../Validation.md). Live quest UI, zone credit, extraction/failure/restart and reward delivery require EFT qualification.
