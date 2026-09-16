# Core architecture

This is the source map for pitFireTeam's main plugin. Detailed behavior belongs to the [topic documents](README.md). The optional [SAIN addon](../addon/docs/Integration.md) references this base and owns only its alternative combat brain.

## Components

| Area | Owner and entry points |
|---|---|
| Startup, configuration, patch installation | [friendlyPlugin](../client/friendlyPlugin.cs) |
| Player commands and follower state | [AIBossPlayer](../client/Components/AIBossPlayer.cs), [BotFollowerPlayer](../client/Components/BotFollowerPlayer.cs), [BossPlayers](../client/Modules/BossPlayers.cs) |
| Peaceful follow and requests | [FollowerPatrolLayer](../client/BigBrain/FollowerPatrolLayer.cs), [FollowerRequestLayer](../client/BigBrain/FollowerRequestLayer.cs), [FollowAction](../client/BigBrain/Actions/FollowAction.cs), [GestureCommandAction](../client/BigBrain/Actions/GestureCommandAction.cs) |
| Core combat | [FollowerCombatLayer](../client/BigBrain/FollowerCombatLayer.cs), [FollowerCombatLogicBase](../client/BigBrain/FollowerCombatLogicBase.cs), [FollowerCombatCommon](../client/BigBrain/FollowerCombatCommon.cs) |
| Recruitment and group conversion | [BotGroupRequestPatch](../client/Patches/BotGroupRequestPatch.cs), [BossPlayers](../client/Modules/BossPlayers.cs) |
| Acquisition and reaction | [FollowerCalcGoalEnemyAcquire](../client/Modules/FollowerCalcGoalEnemyAcquire.cs), [FollowerAwareness](../client/Utils/FollowerAwareness.cs), [Enemy](../client/Utils/Enemy.cs) |
| UI and status | [SquadControlMenuUi](../client/Components/SquadControlMenuUi.cs), [PingTeamates](../client/Utils/PingTeamates.cs), [My Squad](My-Squad-Screen.md) |
| Optional external-SAIN compatibility | [SAINPatches](../client/Patches/SAINPatches.cs), [compatibility contract](SAIN-Compatibility.md) |
| Recording | [BattleRecorder](../client/Modules/BattleRecorder.cs), [core recording contract](Combat-Tactics.md#battle-recorder) |

## Lifecycle and invariants

Core converts/recruits followers, assigns player ownership, reconciles groups/hostility, installs follower layers and releases state on dismissal/raid teardown. Saved squadmates and raid pickups share the follower framework, but differ in profile persistence and command eligibility; [Commands](Commands.md) owns those distinctions.

Core owns peaceful follow/request execution and post-combat recovery. Combat has one owner at a time; an absent/unready optional brain falls back to core. Optional callbacks may provide readiness, release/reset and passive data, but general compatibility cannot depend on the addon being loaded.

The shared cover lifecycle is selection, travel, arrival, hold, then a concrete next decision. Immediate fire, pressure/recent hits, ally or boss protection and compromised cover can preempt a hold. Distance regroup is a fallback after productive combat/commitments. Heal-cover arrival yields directly to treatment. [Combat tactics](Combat-Tactics.md) defines the detailed priority and timing.

Per-follower proficiency uses independent data/settings; no global preset is edited to tune one follower. [Proficiency](Friendly-AI-Performance-Settings.md) owns Vision, Precision and Reaction. Tactical aggression is a separate input.

## Server responsibilities

The server owns mod teammate profiles, persistence, customization, social/group integration and raid outcomes. Teammates are mod-owned profiles, not full independent stock `SptProfile` accounts. Runtime bot spawning is a client/game-session responsibility.

| Area | Source and contract |
|---|---|
| Teammate CRUD, equipment and profile state | [FriendlyTeammateService](../server/Services/FriendlyTeammateService.cs), [callbacks](../server/Callbacks/FriendlyTeammateCallbacks.cs), [static routes](../server/Routers/Static/FriendlyTeammateStaticRouter.cs) |
| Encrypted database and recovery | [FriendlyTeammateStorage](../server/Services/FriendlyTeammateStorage.cs), [storage contract](Teammate-Storage.md) |
| Social list/profile integration | [social routes](../server/Routers/Static/FriendlyTeammateSocialRouter.cs) |
| Group/ready/spawn flow | [match routes](../server/Routers/Static/FriendlyTeammateMatchRouter.cs) |
| Post-raid results and returned equipment | [FriendlyPostRaidService](../server/Services/FriendlyPostRaidService.cs), [Team Escape](Team-Escape.md), [Loadout Management](Loadout-Management.md) |
| Recruit requests | [FriendlyRecruitService](../server/Services/FriendlyRecruitService.cs) |
| Insurance work in progress | [Follower Insurance](Follower-Insurance.md) |

Use the current router declarations as the authority for method/route signatures. Older task notes claiming all tactics were hardcoded to Default or profile customization was unimplemented are superseded by the feature documents and current source.

## Development boundaries

Build/reference policy is in [SPT compatibility](SPT-Compatibility.md). Machine-local paths, release packaging destinations and the bug tracker are in [LOCAL.md](../LOCAL.md). Commands, looting, UI and storage each have one owning topic; avoid copying their full contracts into this overview or AGENTS.md.
