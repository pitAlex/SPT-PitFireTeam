# SAIN addon rework plan — SAIN 4.5.1

Original investigation: 2026-09-12. Status index updated: 2026-09-14.

This is a historical investigation and longer-term proposal. Its descriptions of old files, missing callbacks, a 4.5.0 addon reference, single-layer combat and unimplemented phases describe the pre-rework snapshot. Do not use those passages as the current work queue or restore those removed files.

Current state: both SAIN 4.5.1 combat replicas and opt-in SainMan ownership are implemented, followed by player leadership, linger and recovery, core proficiency repair, command/automatic regroup, bounded engagement, boss-oriented cover, accepted-goal combat entry, native-contact status markers and native SAIN recording. Private typed references are now `addon/refs/4.5.1`. See [current progress and validation](../ADDON-ANALYSIS.md) and [current ownership contract](SAIN-Integration.md).

Aggression-based personality settings are implemented at **100% GigaChad, 70% Chad, 50% Normal, 30% Rat, 0% Coward**, with interpolation and temporary combat-command overrides. Continue qualification from [the personality findings and implementation contract](SAIN-Personalities-and-Aggression.md). Other custom combat command translations remain deferred.

## Historical phase-one checkpoint (2026-09-13)

The optional addon exposes **SainMan** as a selectable follower tactic when SAIN and the addon are installed. Choosing it switches that follower's combat ownership to the addon. Other tactics retain core combat; absent or unready addon state uses the existing fallback without erasing the saved selection.

The addon has **two combat-layer replicas** of SAIN 4.5.1's PMC combat:

- **SAINFollowerSoloCombatLayer** (priority 74, ESAINLayer.Combat): native solo routing, activation, action ending, and surgery transitions. No custom follower commands or tactical behavior are added in phase 1.
- **SAINFollowerSquadCombatLayer** (priority 75, ESAINLayer.Squad): native squad routing and lifecycle, with the human player as squad leader.

Both derive from public SAINLayer because SAIN's concrete layers are internal. SAINActionTypes resolves and validates internal native actions once. Registration, per-follower ownership, and activation cleanup are infrastructure differences; native solo behavior is otherwise preserved.

The squad replica uses SAINFollowerSquadDecision, a copy of the native squad provider with only player-leader checks and positions substituted. Native branch order, thresholds, personality inputs, and currently disabled automatic regroup selection stay unchanged. Player-aware copies of RegroupAction and FollowSearchParty retain native movement/steering/search behavior. They follow the real player, never a substitute bot.

Core SainPlayerSquadBridge owns real SAIN membership, human leader identity/liveness/distance, and election/cleanup. LeaderComponent remains null because its BotComponent type cannot represent a human. Core SainSquadDecisionBridge dispatches the ready follower's native provider call to the addon calculator. SAIN's existing decision manager still publishes results and events. Handled None goes to native solo selection; it does not call the native AI-leader squad provider again.

UseSainFollowerCombat(botOwner) requires SainMan, both plugins, both constructed addon layers, native state, player binding, and readiness callbacks. Native solo/squad/extract/debug layers are suppressed only for followers; ready SainMan retains native urgent-threat and flash reactions above both replicas. General external-SAIN compatibility stays core-owned, with no Harmony patches in the addon.

The previously requested Chad assignment remains follower-scoped setup outside solo policy. Shared presets, existing proficiency, and configured enemy-memory durations are preserved. Opt-out/dismiss/raid teardown restore or release state. Native decision reset does not clear living enemy memory; core live combat signals govern patrol handoff.

The premature solo command extension and old one-layer class are removed. Custom combat commands and new follower tactical policies belong to later phases. This phase is replication, cleanup, and player-leader adaptation. Historical notes describing older addon behavior do not establish current command parity.

The sections below retain the original 2026-09-12 investigation and proposed sequence, including superseded file names and assumptions. [The phase-one checkpoint](SAIN-Addon-Phase1.md) records that initial scope; [ADDON-ANALYSIS.md](../ADDON-ANALYSIS.md) is authoritative for current implemented progress.

## 1. Inspected baseline

| Item | Verified state |
| --- | --- |
| pitFireTeam branch | `1.0.0`; unrelated combat changes were already present before this inspection |
| External SAIN source | Local snapshot at `F:\Projects\SPT-Tarkov\SPT-4.1.3\SAIN-4.5.1`; `Directory.Build.props` declares `4.5.1` and SPT `4.1.3` |
| Source provenance limit | Snapshot has no Git repository metadata; no upstream commit identity was established |
| Installed SAIN | `E:\SPTushanka\BepInEx\plugins\SAIN\SAIN.dll`, file version `4.5.1` |
| Addon compile reference | `client/libs4.1/SAIN.dll`, file version **4.5.0**; referenced by `addon/pitFireTeam.SAINAddon.csproj` |
| Current local game | `LOCAL.md` identifies SPT 4.1.5; keep the separate SPT 4.1.0 compile baseline |
| Current addon | 26 C# files; one custom squad-category layer, legacy compatibility patches, and a broad runtime bridge |
| BigBrain source | `F:\Projects\SPT-Tarkov\SPT-4.1.3\SPT-BigBrain-1.5.0` |

The installed DLL version and source version agree, but this inspection did not establish byte-for-byte correspondence between the snapshot and the installed assembly. Validate the actual runtime members before enabling interception. Version 4.5.0 support for the rebuilt addon is not implied by existing core compatibility.

## 2. What SAIN actually allows us to extend

### Combat layers

Both `CombatSoloLayer` and `CombatSquadLayer` are **internal** classes. A separate addon cannot subclass them through ordinary C# access. Their shared **public abstract `SAINLayer`** is the supported base we can reuse.

Recommended implementation:

- Add `SAINFollowerSoloCombatLayer : SAINLayer`, categorized as `ESAINLayer.Combat`.
- Replace the old `SAINFollowerCombatLayer` with `SAINFollowerSquadCombatLayer : SAINLayer`, categorized as `ESAINLayer.Squad`.
- Replicate the small native layer routing/lifecycle code, with explicit protected extension methods in our versions. Do not copy SAIN's entire solo combat system.
- Preserve `GetBotComponent`, `CheckActiveChanged`, base action-ending checks, and the inherited mover/NavMesh handoff.
- Preserve solo surgery transitions: cover arrival can change SeekCover into DoSurgery without changing the combat enum.
- Reuse native action types initially. Public actions can be referenced directly; internal action types can be resolved once and passed to BigBrain after constructor/type validation. BigBrain creates actions through `Activator.CreateInstance(Type, BotOwner)`.
- Keep source attribution and the supplied MIT notice with copied SAIN code. Record the source version and list intentional differences beside each replica.

Most native solo actions are internal too. `StandAndShootAction` and `ThrowGrenadeAction` are public, whereas Search, Rush, SeekCover, ShiftCover, MoveToEngage, Freeze, Surgery, and melee actions require type resolution or a local implementation. If one needs follower-specific changes later, copy only that action or use an accessible base. Do not patch a native action merely to avoid owning our variant.

**Important distinction:** replicating a layer creates an action-routing extension point. It does not make SAIN's decision tree overridable.

### Decision generation

`SAINDecisionClass` constructs concrete `EnemyDecisionClass` and `SquadDecisionClass` instances and exposes them through get-only properties. Their `GetDecision` methods are public but **non-virtual**. Subclassing them would not change the calls made by SAIN's manager.

`BotDecisionManager` runs at 10 Hz. Its normal ordering is:

1. Choose the enemy and check grenade avoidance.
2. No enemy: publish no combat decision.
3. Self action: publish SeekCover plus reload/medical work.
4. Special melee/zombie handling and dogfight.
5. Continue a committed move to cover.
6. Ask the squad decision provider.
7. Ask the solo enemy decision provider if squad returns no decision.
8. Publish the resulting decision through `SetDecisions` and `OnDecisionMade`.

Keep that manager and its publication path. The current addon calculates its own squad decision inside `IsActive()` but leaves `Bot.Decision.CurrentSquadDecision` describing SAIN's independent result. Native actions and `MemberInfo` consume the latter. For example, SearchAction tests the current squad decision for Help, and MemberInfo learns decisions through `OnDecisionMade`. Two disagreeing decision states are an architectural defect, even before a particular raid failure is reproduced.

Introduce a narrow **core-owned provider-dispatch boundary** at the verified `SquadDecisionClass.GetDecision(out ESquadDecision, Enemy)` entry point. When a follower is assigned to the ready addon brain, dispatch to the addon's player-led squad calculator. Otherwise execute native SAIN. Leave native decision publication, self-action handling, danger checks, and cover commitments intact.

The bridge must distinguish **not handled** from **handled with no squad decision**. The second result deliberately permits SAIN's solo stage; it must not accidentally fall back into the native bot-leader squad calculator. Do not run native squad calculation again when the addon intentionally declined squad action.

Initially keep native `EnemyDecisionClass.GetDecision` intact. If a concrete solo extension later needs to change selection, add the equivalent narrow provider boundary there. Do not create another 10 Hz manager, write decision backing fields, or invoke calculators from `IsActive()`.

This requires documenting a precise brain-selection bridge in the ownership contract. The addon owns calculators/actions; core owns interception of external methods. It is not permission to restore the old collection of general SAIN patches. A core hook is installed under `IsSAINInstalled`; dispatch to the alternate brain additionally requires its readiness and follower eligibility.

## 3. Player leadership needs a typed adapter

### Existing failure boundary

- Core `SAINPatch.PatchAssignSquadLeader` prevents SAIN from assigning a pitFireTeam follower as leader, regardless of addon presence.
- Addon `SAINFollowerSquadLeaderPatch` independently forces `BotSquadContainer.IAmLeader` to false.
- Neither installs a player leader.
- Native `SquadDecisionClass.GetDecision` rejects a squad with no `LeaderComponent`.
- `Squad.LeaderComponent`, `BotSquadContainer.LeaderComponent`, the member dictionaries, and `NewLeaderFound` use **BotComponent**, not Player/IPlayer.

Consequently, changing only `IAmLeader` or `LeaderId` cannot make native leader-dependent behavior work. This is especially relevant for a fresh all-follower squad; a recruited squad can instead retain an old AI leader until membership is repaired.

### Recommended representation

Create one player-led context per actual SAIN `Squad`, bound to the verified pitFireTeam boss and current EFT group. Its leader is the real player (`IPlayer`/`Player`, with SAIN's existing `PlayerComponent` where available). Its bot members remain real follower BotComponents.

The player is the authoritative leader for identity, alive/dead state, position, facing, and commands. Do not manufacture a BotOwner/BotComponent for the human or insert a fake bot into `Squad.Members`.

Core's squad integration adapter should:

1. Reconcile a follower's SAIN membership after the EFT group is assigned. Detach the real bot from its old SAIN squad, obtain the correct squad, and add it exactly once, preserving native subscription cleanup. `BotSquadContainer.RemoveFromSquad()` only reacquires `SquadInfo`; it does not itself call `AddMember` on the new squad.
2. Bind player ownership only after validating that the relevant membership belongs to the same player group. Never convert the recruit's entire previous squad or change ordinary members' leadership.
3. Replace the overlapping leadership patches with one scoped adapter. For a bound player-led squad, expose the player's leader ID and real liveness, suppress AI election/assignment, and clear any stale AI-leader reference during binding. Ordinary squads keep native behavior.
4. Keep `LeaderComponent` null for a human leader, since its declared type cannot represent one. Custom squad decisions/actions read the typed player context. `IAmLeader` then remains false for bot members for the correct reason; no extra unconditional getter patch is needed.
5. Reconcile leader-distance/group-presence consumers, including a single follower beyond SAIN's 50 m `HumanFriendClose` heuristic. Player membership must not disappear because the leader is far away.
6. On dismissal, player death/unspawn, group replacement, bot disposal, or raid cleanup, remove the binding and subscriptions. Restore ordinary SAIN membership/election only where the bot's actual post-dismiss group requires it. Let pitFireTeam's existing player-death/dismiss policy decide the follower's outcome.

Candidate verified boundaries are `BotSquads.GetSquad`, `Squad.AddMember`/`RemoveMember`, `Squad.findSquadLeader`/`assignSquadLeader`, `LeaderId`, `LeaderIsDeadorNull`, and follower lifecycle callbacks. Use the smallest subset needed after implementing explicit binding; avoid patching every manager method by default.

`NewLeaderFound` takes a BotComponent and cannot truthfully announce a human. Use a player-leader change event in our context for custom consumers, while preserving the native event for ordinary AI squads. Do not invoke it with a substitute follower and label that follower the player.

### Native consumers requiring explicit treatment

| Consumer | Treatment |
| --- | --- |
| Squad calculator's non-null bot-leader gate | Replaced by the provider described above |
| RegroupAction | Custom action targets the player's combat anchor, including explicit live-boss regroup and tight exit mode |
| FollowSearchParty | Custom action reads player-led search intent; an optional search point man is a task role, never the squad leader |
| BotSquadContainer leader distance/group presence | Resolve from the player context for bound followers |
| GroupTalk / SAINBotTalkClass | Core currently suppresses SAIN follower talk and uses the existing follower voice path; remove duplicate addon speech patches |
| SAINBotInfoClass extraction timing / BotExtractManager | Keep autonomous follower extraction disabled through current core ownership; do not synthesize a bot leader to satisfy extraction fields |
| Leader death/election | Use actual player liveness; do not allow the null BotComponent to trigger repeated AI election |

This is a player-leader adaptation of SAIN's squad system. It does not pretend the unmodified BotComponent leader API supports humans.

## 4. Layer ownership and fallback

Proposed priorities for the currently registered follower brains:

| Priority | Owner |
| --- | --- |
| 101 | Existing core tripwire handling |
| 85 | Native SAIN flashed layer when applicable |
| 80 | Native SAIN AvoidThreat: dogfight and grenade avoidance |
| 75 | New follower squad combat |
| 74 | New follower solo combat |
| 73 | Existing follower request layer |
| 72 | Existing core combat, active only for the core-owned fallback path |
| 71 | Existing follower patrol |

The two new layers are mutually exclusive according to the published decision, and both yield to urgent SAIN behavior. SAIN has additional layers beyond solo/squad: dogfight is routed by AvoidThreat, not by the ordinary solo action switch. Do not accidentally remove it while replacing the two combat layers.

The proposed 75/74 priorities avoid the old addon/request priority-73 tie and keep self-action cover/surgery above ordinary requests. Validate them against each supported runtime brain; SAIN uses different priorities for bosses, Goons, and Cultists. Do not automatically broaden role support beyond the currently registered brains.

Native solo and squad layers must be suppressed only for the followers owned by the ready replacement. In core-owned mode, retain existing no-addon suppression and fallback behavior. Registration must not globally remove combat layers from ordinary bots. Consolidate the old addon squad-disable patch with core's existing ownership gates and the recruitment-time conflict suppression.

Replace presence-only activation with a readiness handshake. Today `UseSainFollowerCombat` is just SAIN-present plus addon-present; callbacks are registered before bootstrap finishes, `_initialized` is set before work succeeds, and initialization failures are caught after partial patching. A failed addon can therefore disable core combat without supplying its replacement.

Validate version, required members, action constructors, layer registration, and callbacks before publishing readiness. Startup failure leaves core ownership active. Roll back only changes made by the failed addon initialization. If a bot lacks a SAIN component or a valid player-squad binding, keep that bot on its supported core/vanilla fallback until it can be handed over safely; this eligibility decision must be shared by all relevant gates. Do not rely on plugin presence for that per-bot state.

## 5. Cleanup inventory

These are intended dispositions, not changes already performed.

| Existing addon code | Disposition |
| --- | --- |
| SAINAddonPlugin / SAINRegroupBootstrap | Keep the entry point; replace placeholder bootstrap with two-layer startup, validated readiness, and owned teardown |
| SAINFollowerCombatLayer | Replace with the two thin layers; remove the competing calculator in IsActive and self-sustaining active-layer combat-context checks |
| SAINFollowerSquadDecisionCalculator | Rebase on 4.5.1's squad decision ordering; add player/command branches explicitly and publish through the native manager |
| SAINFollowerSquadLayerDisablePatch / SAINFollowerSquadLeaderPatch | Consolidate into core ownership/player-leadership adapters |
| SAINFollowerRecoilPatch | Remove legacy 7x/10x tuning and its cache; core FollowerSainProficiency already owns normalization and accuracy-to-recoil scaling |
| SAINFollowerPersonalityPatch | Remove the blanket BigPipe template, forced personality, and direct file-settings rewrite. Any required enemy-remember compatibility belongs in core; do not carry it over as a combat buff |
| SAINFollowerHitAccuracyPatch / SAINFollowerLowLightVisionPatch | Remove undocumented hit-immunity/night buffs from the addon; any retained policy must be justified against current core proficiency, not silently migrated |
| SAINFollowerAimSwayPatch | Remove the general-method patch; establish native behavior first. A justified general aiming correction would be core-owned |
| SAINFollowerBushVisionPatch | Remove the old temporary EFT look-setting rewrite; preserve current core foliage/proficiency policy. The newer core ray-mask bypass is not identical to the old rewrite |
| SAINFollowerTalkMutePatch / SAINFollowerGroupTalkDirectionPatch | Remove; core now owns follower speech and blocks SAIN-generated follower talk |
| SAINFollowerFriendlyFirePatch | Move necessary integration to core using existing FollowerShotSafety; validate both 4.5.1 overloads and avoid duplicate shot checks |
| SAINFollowerDoorPatch | Move the narrowly intended follower auto-close policy to core if retained; the verified 4.5.1 SelectDoor entry point still exists |
| SAINFollowerSearchCurrentEnemyLookPatch | Remove; use native search initially. Implement an intentional follower search variant inside our action if needed |
| SAINEnemyAcquireGatePatch / SAINFollowerEnemyRetentionService / SAINAddonToggles | Remove the disabled exploratory acquisition path; keep hostility/contact/acquisition ownership in core and check for actual gaps there |
| SAINFollowerRuntimeBridge | Replace with brain lifecycle and squad/search-role state only; remove blanket enemy expiry, KnownPlaces invalidation, and readiness-time crouch mutation |
| Regroup / DefaultBoss actions | Reuse complete-path checks, spacing, target claims, and tight-mode details where correct; separate explicit regroup from passive protection and add explicit command completion |
| Suppress action / SuppressionSafety | Retain action-local suppression lifecycle; use core safety directly and remove the trivial forwarding class if it has no remaining purpose |
| FollowBossSearch action | Rebase around explicit player-led search context; separate point-man role from leader identity and retain bounded movement/state cleanup |

Two cleanup details need tests rather than assumptions:

- Native `SAINFriendlyFireClass.CheckFriendlyFireStatus` returns None when its bot-member dictionary has at most one member. A player plus one follower is such a case; the core follower safety supplement is necessary to cover the human boss.
- The old regroup action has arrival stopping logic but no explicit command-clear/OnPosition completion path in that file. Verify the complete command lifecycle when replacing it; proximity alone is not a completed order.

Search-party maintenance currently allocates dictionaries/sets and uses `Keys.ToList()` on repeated updates. Use reusable per-squad state and event-driven membership cleanup. Do not preserve the old cache machinery solely because it exists.

## 6. Combat lifecycle and command scope

Treat solo, squad, and urgent SAIN interruptions as states within one follower combat session. A squad-to-solo switch or grenade interruption is not the end of combat and must not trigger post-combat full healing, clear combat independence, or erase an order. The old custom layer calls post-combat healing from every `Stop()`; replace that with aggregate session-exit handling.

Readiness queries should observe state. Actions should stop their own search/suppression/movement state through normal lifecycle methods. On attention/reset, core owns enemy/contact suppression; the addon cancels its order/action state. Never age every known enemy or call `OnEnemyKnownChanged(false)` on every enemy just to return to patrol.

Native `BotAction.Start` installs itself as the active SAIN action; native activation clears the action when leaving SAIN. Preserve that protocol and BigBrain's Stop ordering. Release callbacks must not clear a newly started solo/urgent action during a handoff. Any watchdog should log the exact stuck owner/action and release only verified stale owned state.

The rework must explicitly account for the current combat-only command set rejected by `FollowerRequestLayer`:

| Command/intent | Planned owner |
| --- | --- |
| RegroupNearBoss, including ExitLocated tight mode | Player-led squad objective/action with arrival, completion, interruption, and live-boss targeting |
| HoldPosition / Gogogo temporary aggression | Shared follower intent read by both providers; preserve persisted aggression |
| PushEnemy | Ordered combat intent using the core-selected target; respect existing tactic/weapon restrictions |
| SuppressEnemy | Bounded custom squad suppression action with core shot safety |
| NeedSniper | Marksman/support intent; do not route it into an unconditional rush |
| CombatComeToBossCover / CombatMoveToPointTactical | Explicit combat movement with validated destinations and protected medical/danger interruption rules |
| Attention | Core clears/suppresses contact; addon ends its current combat intent |
| Out-of-combat follow, hold, move, loot, doors | Existing core patrol/request flow |

Carry forward current combat-anchor/independence policy. The player is always leader, but a deliberately anchored independent patrol/combat mode need not chase the live player until a return/regroup command requires it.

Do not transplant the core Balanced/Marksman/Protector decision trees into SAIN. First establish the SAIN baseline; add tactic policies at the new provider/action extension points, while retaining existing command restrictions, proficiency, weapon-package policy, and the shared cover arrival/hold/break contract. Unsupported commands must not be silently accepted during qualification.

## 7. Implementation sequence and completion gates

1. **Player leadership foundation — implemented, raid qualification pending.** Legacy addon code is removed. The addon now supplies both combat replicas with Chad and player-leader squad adaptation; custom command policies are pending. Metadata, controlled lifecycle checks, and Debug builds pass. Validate real startup, single/multiple followers, recruitment of a native leader, dismissal, and raid teardown with the new recorder fields before combat work.
2. **Provision typed SAIN 4.5.1 references and build solo extensions.** Keep SPT baseline assemblies untouched. Mirror the small native solo layer routing and self-action transitions through public `SAINLayer`, then add the command-aware solo provider boundary. Reuse native enemy decisions when no custom intent applies. Do not defer command support until squad combat; it is the reason the solo layer needs an extension point. Keep new combat gated until readiness, action routing, surgery, danger precedence, and core fallback are verified.
3. **Qualify solo combat and commands.** With player leadership already bound, enable only the solo combat path plus an explicit handled/no-squad provider result. Verify command acceptance/completion/cancellation and native decision publication agree with the running action. Test one follower through fire, search, reload, cover, surgery, dogfight/grenade interruption, and patrol return.
4. **Integrate squad combat.** Add the separate squad-category layer and player-led calculator through the native manager. Port only the required custom actions; preserve the solo fallback and urgent self/danger handling. Gate: published decisions, MemberInfo, active actions, and order completion agree across multiple followers.
5. **Qualify each combat phase before wider deployment.** Update release notes before building, run targeted checks below, and deploy matching core/addon Debug outputs for the requested phase. Bounded raid evidence, not build success, establishes gameplay correctness.

At each stage remove the superseded approach rather than keeping two active paths. Do not re-enable the old addon as a temporary substitute for missing stages.

## 8. Validation plan

Automated checks should cover behavior at real ownership boundaries:

- Exact 4.5.1 member/action compatibility and failure rollback; addon missing, unsupported, partial startup, or unavailable for an individual follower.
- Native manager dispatch: self-action/danger/committed-cover precedence, handled/no-squad fallback into solo, one decision-publication path, and ordinary bot pass-through.
- Player plus one follower, multiple followers, two independent player contexts, mixed old squad membership during recruitment, and exact member/subscription counts after rebind/dismiss.
- Layer/action transitions: surgery start/end, solo-to-squad and back, temporary urgent interruption, one true post-combat exit, and no enemy-memory writes in readiness checks.
- Friendly-fire supplement with the boss crossing the shot lane, including the one-follower case.
- Command completion/cancellation and validated movement destinations; no stale protection/regroup action substituted for a missing native action type.
- Existing core SAIN compatibility checks still pass with addon absent and present. Do not imply 4.5.0 addon support without an explicit reference/runtime run.

Add passive recorder data for brain readiness/eligibility, SAIN squad GUID, actual player leader ID, member IDs, native combat/squad/self decision, custom intent, selected action, command, destination, and transition/end reason. Emit changes plus throttled recovery diagnostics, not reflection-heavy per-frame logging.

The raid matrix is: one saved teammate; several saved teammates; a recruit from an existing AI squad; visible combat into lost contact into patrol; reload/medical interruption; grenade/dogfight/tripwire interruption; every supported combat command; player movement across floors; leader/follower death; dismissal and raid teardown. Include an ordinary SAIN squad as the control. Start with the common PMC brain, then validate the other already-supported follower brains before claiming their support.

Deferred expansion: new follower brain types, new tactic designs beyond existing command obligations, arbitrary SAIN versions, and unrequested multiplayer support. These are not prerequisites for a bounded SAIN 4.5.1 qualification.

## 9. Evidence map

SAIN paths below are relative to the inspected 4.5.1 snapshot; pitFireTeam paths are relative to this repository. Line numbers refer to the inspection snapshot.

| Finding | Source |
| --- | --- |
| Internal solo/squad layers; public base | `SAIN/Layers/Combat/Solo/CombatSoloLayer.cs:9`, `SAIN/Layers/Combat/Squad/CombatSquadLayer.cs:11`, `SAIN/Layers/SAINLayer.cs:13` |
| Non-virtual decision providers; concrete construction | `SAIN/Classes/Bot/Decision/EnemyDecisionClass.cs:45`, `SquadDecisionClass.cs:21`, `SAINDecisionClass.cs:87` |
| Decision precedence/publication | `SAIN/Classes/Bot/Decision/BotDecisionManager.cs`, `getDecision`, `SetDecisions`, `ContinueMoveToCover` |
| MemberInfo depends on native decision events | `SAIN/Classes/BotManager/MemberInfo.cs`, constructor and `UpdateDecisions` |
| Bot-only leader/member types; election | `SAIN/Classes/BotManager/Squad.cs:33`, `:40`, `:429`, `:612`, `:646`, `:675` |
| Squad selection and rebind limitations | `SAIN/Classes/BotManager/BotSquads.cs`, `GetSquad`; `SAIN/Classes/Bot/Info/BotSquadClass.cs:23` |
| Human component separate from bot component | `SAIN/Components/PlayerComponent.cs:54`, `:70`, `:85` |
| Leader-dependent actions | `SAIN/Layers/Combat/Squad/RegroupAction.cs:13`, `FollowSearchParty.cs:49` |
| Urgent action ownership | `SAIN/Layers/SAINAvoidThreatLayer.cs`, `GetNextAction` / `CheckDecisionsForBot` |
| Single-bot native friendly-fire early return | `SAIN/Classes/Bot/Sense/SAINFriendlyFireClass.cs:42` |
| Existing contradictory leader hooks | `client/Patches/SAINPatches.cs:443`, `addon/SAINFollowerSquadLeaderPatch.cs` |
| Presence-only ownership and early callbacks | `client/friendlyPlugin.cs:326`, `addon/SAINAddonPlugin.cs:21`, `addon/SAINRegroupBootstrap.cs:21` |
| Current addon independent decision selection | `addon/SAINFollowerCombatLayer.cs`, `TryEvaluateFollowerDecision` |
| Overbroad release and readiness mutation | `addon/SAINFollowerRuntimeBridge.cs:366`, `:515`, `:546`, `:606`, `:651` |
| Recruitment updates enemy lists, not explicit SAIN squad binding | `client/Components/BotFollowerPlayer.cs:505`, `:3552`; `client/Modules/BossPlayers.cs:317` |
| Existing priority/command gates | `client/BigBrain/FollowerPatrolLayer.cs:17`, `client/BigBrain/FollowerRequestLayer.cs:95`, `docs/Commands.md` |
| Core proficiency/talk/vision ownership | `client/Modules/FollowerSainProficiency.cs`, `client/Patches/SAINPatches.cs:95`, `docs/SAIN-Integration.md` |

No implementation tests or raid tests were run for the original 2026-09-12 planning pass; subsequent validation is recorded in ADDON-ANALYSIS.md. Findings above are source/metadata observations; proposed behavior remains subject to the qualification gates.
