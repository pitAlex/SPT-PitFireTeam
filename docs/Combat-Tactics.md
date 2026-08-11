# Combat Tactics Notes

Last updated: 2026-08-11

## Scope

- This document covers the core follower combat path under `client/BigBrain`.
- It only mentions the optional SAIN addon combat path where a boss command crosses the core/addon boundary.
- Treat this as current runtime documentation, not a backlog.

## Runtime Ownership

Core combat is objective-routed.

- `FollowerCombatLogicBase` owns objective selection and command handoff.
- Rifleman/default combat is implemented by `FollowerCombatDefaultObjective` plus `FollowerCombatDefault`.
- Marksman combat is implemented by `FollowerCombatSniperObjective` plus `FollowerCombatSniper`.
- Combat regroup is implemented by `FollowerCombatRegroupObjective`.
- Ordered suppression is implemented by `FollowerCombatSuppressionObjective`.
- Launcher suppression intent is implemented by `FollowerCombatGrenadierObjective`.
- Ordered marksman support is implemented by `FollowerCombatNeedSniperObjective`.
- Shared state and primitives live in `FollowerCombatCommon`.
- Push selection and push commitment live in `FollowerCombatPush`.

Objective ownership matters: heal, dogfight, grenade, and immediate fire can interrupt the current objective, but they do not automatically change the owning objective. Explicit combat regroup activates the regroup objective. Explicit suppression activates the suppression objective for eligible followers: Rifleman/default followers by default, and Marksman followers only for the automatic-secondary fallback case. Explicit Need Sniper activates the marksman support objective for Marksman followers. Explicit push returns control to the active tactic's primary objective and lets that tactic build a push plan.

## Enemy Acquisition And Retention

Enemy acquisition, squad enemy sharing, target commitments, and retained-contact stabilization are separate responsibilities.

Acquisition and sharing sources:

- `FollowerCalcGoalEnemyAcquire.HandleCalcGoal(...)` is the idle/sibling sync path. It runs from the core `BotCalcGoal.CalcGoalForBot()` postfix for followers that are not attention-suppressed and do not already have an enemy. When one follower sees a valid forward candidate, it promotes that enemy for itself, then tries to create and promote the same enemy for sibling followers that have no current/live goal enemy. This path registers non-prioritized retention after promotion, but `FollowerContactEnemyRetention` does not perform the squad fan-out itself.
- `AIBossPlayer.RegisterContactEnemyForFollower(...)` is the boss-contact injection path. Contact/OverThere-style cues, NeedHelp fallback, and ordered launcher target resolution can report and create `EnemyInfo` records for followers, optionally promote one contact as the goal, and register retention with the command-priority flag.
- `FollowerAwareness` is the reaction path. Direct hits and close incoming fire can create/promote the immediate hostile attacker when the current goal is missing, dead, stale/non-shootable, or clearly farther than the incoming threat. When no push mission is active this retarget clears retained contact once before installing the new goal, so a stale far contact cannot immediately restore over the immediate attacker. When a push mission is active, direct-hit interruption is registered as a temporary target instead of cancelling or replacing the mission.
- Personal contact is separate from squad/shared memory. `EnemyInfo.HaveSeen` or group sense/real-memory may keep an enemy known, but repair and promotion paths must not treat that flag alone as the follower personally seeing the enemy. Personal seen timestamps are written from direct visible/can-shoot contact or explicit contact priority, and memory-only setup/soft acquisitions cannot start proactive auto-push or marksman close-search by themselves.
- `addPlayerToBoss` is treated as hostile relationship sharing, not contact proof. Unscoped vanilla/SAIN `GoalEnemy` setter attempts from that cause are blocked for followers until direct/personal contact exists; explicit boss-command, direct-hit, and group-attacker paths remain scoped reaction inputs.

`FollowerCombatTargetCommitments` owns mission-versus-temporary target arbitration:

- Ordered push registers the ordered kill target as an `OrderedPush` mission. Auto-push registers the first committed push target as an `AutoPush` mission.
- Auto-push mission creation rejects memory-only setup/soft targets that have no personal contact record. The enemy can stay in memory for later validation, but the bot should hold, rescan, support, or regroup until there is actual contact or an explicit order.
- A mission is separate from `Memory.GoalEnemy`. `GoalEnemy` may temporarily point at another valid threat, but the mission target remains recoverable until it dies, becomes unrecoverable, or an explicit new combat order clears the mission.
- Non-null `GoalEnemy` setter attempts are gated while a mission exists. The mission target is allowed, a currently accepted temporary target is allowed, and unrelated switches are blocked and recorded as `targetCommitmentBlocked:*`.
- Temporary targets are accepted for local self-defense, visible shootable contact, close visible contact, or direct-hit interruption. When the temporary target dies or stays hidden/unengageable past its short grace window, combat restores the mission target and resumes the push.
- Boss-under-attack support uses the same temporary-target path while a mission exists. It can interrupt for a valid engagement opportunity, but it does not force a hidden or weak boss attacker over an active mission.
- Retention may yield while an accepted temporary target is active, but it must not adopt that temporary target as the retained mission contact.

`FollowerContactEnemyRetention` is a per-follower stabilizer, not the owner of target selection:

- It stores at most one retained contact per follower profile id.
- `RegisterCurrentGoal(...)` only retains a live current goal if it has fresh visible/can-shoot contact or personal seen timestamps still inside `enemyRemember`; stale unseen goals are not retained.
- A prioritized retained contact cannot be replaced by a different non-prioritized contact while the retained window is active.
- Invisible bookkeeping refreshes do not extend retention forever; non-visible refreshes are capped by the original last-contact window.
- `ShouldBlockGoalEnemyClear(...)` intercepts `GoalEnemy = null` for followers and blocks the clear only while the retained enemy is still live, still inside its retention window, and still matches the current goal.
- Unscoped non-null `GoalEnemy` setter attempts also consult retention while a retained contact is active. SAIN/vanilla can switch to a different goal only when that candidate is a live visible/can-shoot contact that retention would adopt; otherwise the set is blocked before the follower flickers away and then restores back.
- `TryRestore(...)` recreates/promotes the retained enemy as `Memory.GoalEnemy`, sets `PriorityIndex = 0`, repairs personal memory, and forces `Memory.IsPeace = false`.
- Retention yields when the follower already has a different live visible/can-shoot goal enemy, when the retained enemy dies/expires, or when code calls `ClearAndAllowNextGoalClear(...)` for explicit reset/retarget. If a target mission is active, yielding to a different enemy requires `FollowerCombatTargetCommitments` to accept that enemy as a temporary target.

Current consumers:

- Core combat uses retention to keep combat active briefly after vanilla memory clears, to restore committed push targets, and to let immediate-fire actions finish when short visibility/memory flicker would otherwise end them.
- Ordered/explicit retarget paths use `ClearAndAllowNextGoalClear(...)` before installing a new goal so the old retained contact cannot bounce the follower back.
- The SAIN addon reads retained contact from core and syncs it into `SAINEnemyController` through `SAINFollowerRuntimeBridge.TrySyncFollowerEnemyState(...)`; this is a bridge use, not SAIN owning core target selection.
- When SAIN is installed without the addon, the core SAIN patches can block SAIN enemy-clear behavior while a core retained contact is active.

Implementation rule:

- New enemy-sharing logic should live in acquisition/contact/reaction code, not inside `FollowerContactEnemyRetention`.
- New mission/temporary target policy should live in `FollowerCombatTargetCommitments`, not in retention or individual action classes.
- New combat actions should register or refresh retention only after a real goal enemy is selected or explicitly assigned.
- New retarget/reset logic must either use the existing force-goal helpers or clear retention before setting `GoalEnemy = null`, otherwise the retained contact can restore the old target on the next combat check.
- Do not use retention to keep a non-visible stale target alive indefinitely. Fresh visible/can-shoot contact, explicit command priority, or a still-recent personal seen timestamp must justify the retained window.

## Boss Combat Commands

Combat command state lives on `BotFollowerPlayer` and is intentionally separate from persisted profile settings.

- `EPhraseTrigger.HoldPosition` applies a temporary `0%` effective combat-aggression override in combat.
- `EPhraseTrigger.Gogogo` clears that temporary override and returns each follower to its saved aggression.
- `EPhraseTrigger.GoForward` becomes `PushEnemy` when the follower has an active combat enemy.
- `EPhraseTrigger.Suppress` becomes `SuppressEnemy` for focused followers or eligible squad suppressors.
- `EPhraseTrigger.NeedSniper` becomes `NeedSniper` for Marksman combat.
- `EPhraseTrigger.NeedHelp` fakes a boss-under-attack event against the closest valid enemy.
- Picked-up followers use a personality/odds gate before accepting combat `HoldPosition` and ordered `GoForward` push. Saved squadmates obey normally.
- Picked-up followers also use that personality model for autonomous protection willingness. Low-protection pickups behave closer to `On Your Own`: they tolerate more boss distance before regrouping and can skip boss-under-attack protection routes.
- Core combat reads `EffectiveCombatAggression` through `FollowerCombatCommon.GetAggression01()`.
- Tactic changes reset saved aggression to that tactic's default: Rifleman uses `50%`, Marksman uses `30%`.
- The override is cleared when the follower is safely out of combat and patrol can resume.
- `BotReceiverPhraseOverridePatch` suppresses vanilla follower receiver handling for `Stop`, `HoldPosition`, and `Gogogo`, so `AIBossPlayer` owns these commands.

SAIN addon note:

- `SAINFollowerCombatLayer` treats the temporary HoldPosition aggression override as boss-protection/regroup intent.
- This keeps the command behavior aligned with core `0%` aggression even though SAIN addon decisions do not use `FollowerCombatCommon`.

### Hold Position

Combat `HoldPosition` is not a normal movement hold. It temporarily sets effective aggression to `0%`.

Expected behavior:

- Rifleman suppresses proactive push/search pressure.
- Rifleman can still take a small local firing-position move behind or lateral to the boss line when the current hold spot has no shot. This is not a push: it avoids forward positions and stays on a short local route.
- Marksman suppresses proactive automatic close-search/auto-search.
- Defensive behavior still works: immediate fire, dogfight, heal, boss protection, and survival can still interrupt.
- The saved aggression value is not changed.
- `Gogogo` clears the override immediately.
- The override also clears after combat when the follower is safe to return to patrol.
- Picked-up followers may reject the hold with `Negative`. Higher-level recruits are more likely to act independent, while lower-level recruits are more likely to accept.
- Their stable independence bias also lowers autonomous boss protection, so a pickup who dislikes being commanded is also less likely to abandon their own fight just because the player was hit.

Out of combat, `HoldPosition` is handled by the request layer as a normal hold command and can crouch/hold in place depending on how the command was issued.

### Go Forward / Push Enemy

Combat `GoForward` becomes `PushEnemy` if the follower already has an active enemy.

Picked-up follower behavior:

- Picked-up followers evaluate the order before accepting it.
- Better follower gear versus the current enemy's gear increases acceptance.
- Lower-level recruits are more likely to refuse because pushing feels dangerous.
- Higher-level recruits can also refuse from cockiness/independence rather than fear.
- Refusal responds with `Negative`; acceptance creates the same `PushEnemy` command as a saved squadmate.
- The same protection willingness affects non-command rifleman decisions: low-protection pickups are slower to regroup to the player and may skip boss-under-attack support unless their personality is more loyal.

Rifleman/default behavior:

- The command is consumed into a durable ordered-push objective.
- The objective latches the current combat enemy as the ordered kill target and registers it as a target mission. It keeps pursuing until that target dies, becomes unrecoverable, or an explicit new combat order cancels/replaces the mission.
- Ordered push runs as full ordered pressure rather than a timed aggression pulse; medical, reload, and immediate survival actions can interrupt the current action, but they do not clear the ordered target. Active or pending medical work blocks new push phases until heal logic starts or the medical work clears.
- Boss-under-attack/help retargets do not cancel ordered push. If another enemy becomes a valid temporary target, such as a direct-hit interruption, close visible threat, or visible shootable engagement opportunity, the follower can handle that immediate fight and then resume the ordered target.
- Explicit new boss orders cancel ordered push. Combat `CoverMe` and `NeedHelp` request ordered-push cancellation before applying their own boss-protection/support behavior; regroup, suppression, and Need Sniper interrupt through their normal objective handoff.
- Ordered push first tries to build a committed firing-position move using the enemy's current body position.
- When an ordered firing-position move reaches a reachable pressure point, the ordered-push objective honors the shared arrival hold before selecting another point. This lets the follower hold, face, and fight from the best reached pressure point instead of reselecting tiny adjacent firing points every tick against an unreachable or marksman-style target.
- If no firing position exists, it falls back to `FollowerCombatPush.EngageEnemy(true, ...)`.
- The selected push is committed as `push.*`, so the bot should finish the chosen push phase unless interrupted by real danger, enemy loss, enemy change, or another explicit command.
- Ordered push refreshes enemy retention during the committed push so it should not forget the enemy mid-route.
- If the enemy becomes visible/shootable during direct movement, the push can convert into immediate fire or short suppression rather than churn into unrelated movement.
- Riflemen with a shotgun first primary can latch a loaded automatic second primary for mid-or-farther fights; once latched it stays through the fight to avoid distance-bucket weapon churn, unless the automatic secondary runs dry with no reload ammo, at which point the fight is locked back to first primary.
- Automatic-secondary switching is penetration-guarded against the first primary's current ammo: the secondary ammo may trail by up to 15 penetration, matching EFT's armor-resistance penetration window, so small gaps like 35 vs 40 still allow rate of fire to compensate while gaps like 20 vs 40 stay on the primary.
- Rifleman weapon switching is owned by combat decisions only: if EFT automatically swaps to a support weapon, such as pistol fallback instead of reload, follower combat should not force an immediate primary swap back unless this tactic explicitly made the secondary switch first.
- Follower reload speech remaps vanilla `NeedAmmo` calls made during an active `BotReload` transaction to `OnWeaponReload`; genuine `NeedAmmo` calls outside an active reload remain unchanged.
- Riflemen with a usable grenade or rocket launcher in either primary slot can use it through the grenadier objective. The objective owns launcher selection, range, ballistic-arc validation, impact safety, and optional movement to a firing point; once those checks pass, it returns the ordinary `shootFromPlace` weapon action. The action feeds the low-arc aim point into EFT's normal aim-and-trigger worker, preserving normal cadence and repeated fire without using the one-shot `SuppressShoot` controller.
- A launcher in `FirstPrimaryWeapon` is a real combat primary. Its validated normal-fire attempt may continue for up to two seconds against the last personally seen impact point while EFT finishes aiming, but impact, friendly, distance, and ballistic-arc safety are revalidated throughout. If the launcher is empty, cooling down, lacks a safe opportunity, or combat must hand off to dogfight/healing/retreat, the follower switches to a loaded holster weapon instead of waiting with the launcher raised.
- A launcher in `SecondPrimaryWeapon` remains a temporary suppression weapon. It keeps the existing suppression opportunity, cooldown, and return-to-primary lifecycle; it does not receive first-primary visual grace or emergency holster policy.
- Heal retreat movement is weapon-independent: reaching the committed heal destination ends movement even when EFT's `IsInCover` flag is late, while a four-second no-progress retreat invalidates that destination and replans instead of leaving the follower stationary.
- Launcher loaded-round checks include revolver/cylinder launcher camoras and chamber-only launchers. Empty but reloadable equipped launchers stay combat-usable; when the ordinary fire action empties one, the grenadier preparation step opens a bounded reload window, tolerates EFT's automatic pistol switch while hands settle, reselects the launcher, and reloads compatible loose rounds carried in vest, pockets, backpack, or secure fallback. Rocket launchers such as the RShG-2 are treated as single-use: they are usable only while already loaded and never enter the reload-wait fallback.
- Confirmed launcher fire emits a short squad danger event. While that event is active, other followers do not start or continue autonomous pushes around the same impact area, even if their current enemy identity is different. Explicit player push orders still remain command-owned.
- Other nearby Riflemen can react to the push event as support instead of starting a duplicate independent push: shoot from current cover, take a support shot, move to push-support cover, or move to a firing point.

Marksman behavior:

- Generic `GoForward`/push is not a marksman assault order.
- Marksman supports team push/search by finding a shooting position or support cover.
- Marksman support positions reject wrong-level candidates when the boss/team is vertically separated, and reject directly close candidates that are actually path-separated from the boss.
- If close automatic support is allowed by marksman aggression and safety checks, marksman can use automatic secondary behavior.
- At support/reposition ranges, marksman switches back to the primary rifle instead of keeping an automatic secondary selected from a stale or non-close contact.
- If a nearby Rifleman can reasonably take the push, marksman should prefer support/reposition instead of becoming the primary pusher.

### Suppression Order

Combat `Suppress` becomes `SuppressEnemy` and is consumed by `FollowerCombatSuppressionObjective` for eligible followers.

Objective behavior:

- If the boss is looking at a follower, only that follower receives the order. Because the boss is looking at the follower rather than the enemy, the follower chooses from its current enemy or boss-visible contact instead of using the boss look ray as the suppress target.
- If no follower is focused, the order can fan out to eligible suppressors instead of picking only the nearest Rifleman/default.
- Squad suppression skips followers already healing, recently damaged, under fire, dogfighting, actively shooting, in close visible contact, or already committed to emergency/fight movement.
- Squad suppression allows no more than one grenadier. The grenadier is chosen from launcher-capable Rifleman/default followers by usable hostile target distance, direct launch lane, friendly impact safety, and friendly lane safety. Boss order-ray launcher targets are considered within `120m`; boss-visible contacts are a fallback for scoring.
- Rifleman/default followers suppress with suppress-capable current weapons. If there is no active Rifleman/default in the squad, a Marksman with a loaded automatic second primary may join squad suppression and can switch to that secondary for the ordered burst.
- The command is consumed into an objective, like regroup, so it is not polled inside the normal Default decision tree.
- It does not interrupt active healing or an already active fight action; it waits until that action's normal end logic allows a switch.
- It targets the current enemy's best known shoot/suppress point.
- Launcher-capable ordered suppression first checks whether the launcher can fire safely from the current position, then whether it can move to a suppress-from point and fire.
- If launcher support is unavailable, primary weapon suppression first checks whether the bot already has a safe lane from place.
- If a better suppress-from point exists, primary weapon suppression can move there and suppress from that point.
- If direct primary fire is blocked but suppression was explicitly ordered, it may still suppress the obstructed known point, as long as friendly shot safety passes.
- Ordered weapon area suppression records `weaponSuppressArea` when the strict lane classifier cannot prove a direct/foliage lane but friendly lane safety still allows a short pressure burst.
- The objective has an ordered-suppression protected window so one short obstruction or controller completion does not instantly cancel it.
- Ordered weapon suppression has a hard 2-second cap. It should create a burst of pressure, not keep firing until EFT's suppression controller empties magazines.
- Explicit follow-up combat orders, such as Push Enemy, break the suppression action immediately instead of waiting for that protected window.
- Ordered grenade-launcher suppression activates the grenadier objective instead of firing through the weapon-suppression action. The grenadier objective has a `5s` opportunity window: it prepares the launcher target, moves to a firing point if needed, re-checks the launch lane from the reached position, and starts ordinary weapon fire when the enemy is visible and explosive safety passes. The straight-rifle `GoalEnemy.CanShoot` flag is intentionally not an activation requirement because a validated launcher arc may clear the obstruction that blocked that flag.
- If the grenadier objective starts launcher fire, the follower says `GetInCover`; if an ordered window expires with no safe normal-fire opportunity, the follower says `Negative`, releases any support-launcher switch it owned, and returns to the primary combat objective.
- Emergency actions such as dogfight, healing, and reload-retreat discard the active grenadier objective and return control to the normal combat router. A point-blank dogfight with a first-primary launcher first requests the loaded holster fallback and holds while the asynchronous hands switch settles.
- Launcher voice lines, artillery warnings, squad danger events, and `launcherInit` recorder events happen only after the bot has reached/confirmed the launch position and the ordinary fire action can start.
- Multi-shot and cylinder launchers remain in the normal fire action while the enemy remains visible and the validated arc remains safe, allowing repeated shots during the same engagement. Empty reloadable launchers return to the preparation/reload step and then resume normal fire; they do not complete after one observed shot.
- If the selected launcher impact becomes unsafe, no launch lane exists from the reached position, or the launcher action cannot start, ordered suppression falls back to weapon suppression instead of completing the order as failed. If weapon suppression cannot be created either, the suppression objective completes as no-action and answers `Negative`.
- When a launcher in `FirstPrimaryWeapon` exhausts its grenadier opportunity without a safe shot, or a point-blank dogfight makes the launcher unusable, a loaded non-launcher holster weapon becomes a pending combat fallback. The objective router holds ordinary rifle decisions while EFT's asynchronous hands switch settles, then normal combat continues with the pistol. Successful launcher fire, active launcher reload, an empty holster, and unrelated objective interruption do not request this fallback.
- Successful first-primary launcher fire, enemy loss, healing, and normal objective interruption do not start the old autonomous suppression cooldown. A genuine `5s` failure still starts the retry cooldown before requesting the pistol fallback so the router cannot immediately select the rejected launcher again. Support launchers retain their temporary-use cooldown and pending return to the main weapon.
- A requested support-launcher fallback is only complete once the second-primary launcher is no longer selected/active. `TryChangeToMain()` can return success before EFT finishes the weapon change, so the pending fallback remains alive through weapon-readiness waits. If combat ends during that asynchronous switch, patrol finishes returning to the primary before out-of-combat reload maintenance can consider the empty support launcher. Patrol does not switch away from a first-primary launcher.
- Completed support-launcher attempts force a primary-weapon fallback before the normal combat objective resumes, and ordinary hold/regroup actions keep guarding against a delayed unowned support-launcher switch. A first-primary launcher instead remains the real main weapon: ordinary fire is allowed only under a grenadier reason after explosive safety succeeds. Cylinder launchers use camora loaded-round counts rather than conventional magazine counts for reload-retreat and recorder diagnostics.
- Immediately before every ordinary launcher-fire update, the action re-checks enemy visibility, launcher distance, friendly impact safety, friendly lane safety, and the ballistic arc. `launcherNormalFireHold` records a late safety rejection if conditions change after planning; `launcherNormalFire` records the current rifle `CanShoot` flag, trigger state, and applied aim raise while the normal fire worker runs.
- If the bot is already holding a second-primary grenade launcher when ordered suppression falls back to weapon fire, the objective switches back to the primary and retries briefly instead of completing as no-action.
- Launcher planning calculates a raised ballistic path for lane validation: a blocked straight ray to the impact point can still be accepted when the sampled launcher arc is clear or only soft foliage. Because launcher targets are enemy root positions on the ground, terrain contact close enough to damage the target counts as a valid impact instead of an arc obstruction. That tolerance is limited by both the loaded grenade's explosion radius and the existing friendly-clear radius; hard geometry earlier on the arc still blocks the shot. The launcher action supplies that same raised point to EFT's ordinary aiming and trigger worker so validated arcs are not discarded by the outer straight-rifle fire gate.

Autonomous suppression remains separate:

- Default can still choose `autoSuppress.*` as a normal tactical branch.
- Autonomous suppression is shorter and more conservative than ordered suppression.
- Point-blank suppression is not allowed to continue just because visibility/shootability is flickering. If the target is within point-blank contact range and there is no confirmed foliage obstruction near the lane, follower suppression clears instead of blind-firing around hard geometry.
- Ordered/objective suppression can be interrupted after its protected opening burst when the follower needs healing or reload-retreat. The order should create pressure, not pin the follower in place while hurt or empty.

Out of combat, patrol performs one reload-maintenance pass per carried weapon until the next combat-to-patrol handoff. For each first primary, second primary, and holster weapon, it checks compatible loose ammo, tops off inserted and loose spare magazines for external-magazine weapons with whatever rounds are available, then only switches/reloads an external-magazine weapon if a reachable loose spare magazine has at least two more rounds than the current magazine. Weapons picked up through follower loot commands or backpack handoff are treated as carried loot and skipped by patrol reload maintenance, even if they share magazines or ammo with spawned weapons. The only acquired-weapon exception is a reloadable launcher physically occupying `FirstPrimaryWeapon`: patrol may select it once and consume compatible loose launcher rounds because it is the follower's actual combat primary, while acquired ordinary weapons and support-slot launchers remain excluded. Magazines installed in another carried weapon are not treated as spare reload candidates, even if both weapons accept the same magazine type. Chamber, revolver, shotgun, and internal-magazine weapons do not use spare-mag comparison: if compatible loose ammo exists, patrol may switch once, perform the normal reload, then mark that weapon done. Each completed slot decision waits two seconds before the next slot is considered, and all patrol reload flags/timers reset when the patrol layer exits. Repeated patrol reload switches or rejected reload starts are tracked per weapon id across patrol restarts for a short give-up window; when a weapon spends that budget, patrol marks it done and stops inviting EFT reload/selector fallbacks until the cooldown expires.

### Need Sniper Order

`NeedSniper` is a marksman-only combat objective implemented by `FollowerCombatNeedSniperObjective`.

Objective behavior:

- The command is consumed by `FollowerSniperCombatLogic`, not by `FollowerCombatSniper`'s normal autonomous router.
- It does not pollute normal marksman support/reposition branches.
- The sniper rejects or ignores the order when self-preservation must win: active/pending heal work, under fire, recent hit, or point-blank visible shootable threat.
- If accepted, it tries immediate fire first.
- If current cover can shoot, it shoots from cover.
- It switches back to the marksman primary at support ranges so an earlier close-quarter secondary does not degrade ordered support.
- Otherwise it finds a firing cover or firing position using the enemy's current position when possible.
- It is allowed to use closer/forward/lateral firing positions than autonomous marksman support, because this is an explicit player support request.
- On arrival, it arms a short committed `sniper.NeedSniper.positionHold` settle window before reassessing.
- It completes when it reaches a valid shot, movement ends for stable immediate fire, settles without a lane, the enemy disappears, or a stronger interruption takes over.
- If it cannot build a lane, the battle recorder emits `objectiveDiagnostic` entries with support-cover rejection, current-position rejection, support-position rejection, and boss direct/path/vertical separation context.

### Need Help Order

`NeedHelp` is not a normal follower command and does not activate its own combat objective. It is a boss-side emergency support signal handled in `AIBossPlayer`.

Runtime behavior:

- The boss selects the closest valid enemy relative to the boss/player.
- Candidate enemies are gathered from boss-tracked enemies, boss-group enemies, visible contact enemies, and SAIN contact fallback enemies when SAIN is installed.
- Invalid candidates are ignored: dead/inactive bots, the player, followers, and friendly bot roles.
- The boss logic is marked as manually under attack by that enemy.
- Each active follower is told to prioritize that enemy through `PrioritizeEnemy(...)`, which either promotes an existing `EnemyInfo` or adds the enemy to follower memory.
- The normal combat router then reacts through existing boss-under-attack support/protection logic.

Expected behavior:

- Rifleman/default followers can break passive hold into boss protection/support if a valid protection decision can be prepared.
- Marksman followers can react through their boss-under-attack/support scan, unless self-preservation, healing, or a stronger current fight wins.
- Because this is a fake boss-under-attack signal, it intentionally reuses existing protection/support decision rules instead of creating a separate "NeedHelp" movement mode.
- If no valid enemy can be found, the phrase does nothing and does not disturb current follower decisions.

## Shared Commitment Model

The new stabilization model is based on commitments. A commitment means "keep doing this unless a real interrupt happens", not "ignore combat".

Commitment types:

- `initialDecision`: one-shot opener prepared when combat starts.
- `committedGrenadeDecision`: active grenade throw sequence; stays latched until throw completes or is canceled for immediate danger.
- Regular follower grenade throws keep a hard `15m` minimum and a `40m` outer envelope, but the usable far limit is selected-grenade aware: impact grenades can use the full envelope, while timed grenades scale by fuse length so short `1.5s` fuses do not try far throws that longer `3s` to `3.5s` fuses can support. Direct visibility is not required, but the follower must have a known enemy position and the throw must pass friendly safety checks. Launcher suppression owns longer grenade-like pressure.
- `suppressionObjective`: ordered Rifleman suppression state; owns ordered suppress-fire setup and completion.
- `needSniperObjective`: ordered Marksman support state; owns ordered firing-position search and settle.
- `committedPushDecision`: push/search pressure chosen by `FollowerCombatPush`.
- `committedMovementDecision`: non-push movement chosen by a tactic.
- `committedCoverPoint`: selected cover point reused across movement and cover-fire checks.
- `committedPositionDecision`: arrival hold after reaching cover or a point.
- `committedHealCover`: selected safe/heal cover reused until healing can start or becomes invalid.

Shared rule: movement and cover commitments stabilize travel; arrival holders stabilize the moment after travel. Tactics decide which tactical reasons are allowed to break those commitments. Arrival holds only become cover holds when the bot is actually in or near that cover; plain path-end arrival falls back to a position hold so a stale remembered cover cannot invalidate the hold and immediately recommit another `runToCover`.

## Rifleman Router Order

Rifleman/default combat currently routes in this order:

1. Refresh shooting-cover state and validate committed cover.
2. Immediate fight: dogfight and direct in-fight shooting, unless damage pressure should defer exposed fire into recovery.
3. Continue committed grenade throw.
4. Consume combat-start `initialDecision`.
5. Medical decision: heal, stim, or move to heal cover.
6. Recovery decision when exposed, under fire, or recently damaged.
7. Boss-distance regroup precheck, deferred while protected commitments are active.
8. Continue committed arrival hold.
9. Continue committed push.
10. Continue committed non-push movement.
11. Continue committed cover movement/fire setup.
12. Boss-distance regroup after commitments are checked.
13. Low-aggression regroup, including temporary boss `HoldPosition` combat override.
14. Combat `HoldPosition` local support: a bounded behind/lateral firing-position move if the follower can improve its angle without advancing toward the enemy.
15. Boss-under-attack protection.
16. Ally support, but only if a valid support decision can be prepared.
17. Autonomous suppression.
18. New grenade activation.
19. Ordered push.
20. Visible enemy handling.
21. Generic advance/push.
22. Cover hold, fallback shoot, suppress, or boss hold.

Explicit command objectives sit above this tactic router in `FollowerCombatLogicBase`:

- explicit regroup can switch to regroup objective
- explicit Rifleman suppression can switch to suppression objective
- explicit Need Sniper can switch to NeedSniper objective for Marksman
- explicit push switches into the ordered-push objective
- NeedHelp does not switch objectives directly; it marks boss-under-attack and lets each active tactic's protection/support branch respond.

Important policy:

- Boss-distance regroup does not break active push/help/heal-style commitments.
- Autonomous boss-distance regroup defers briefly after recent personal contact so a follower can finish or stabilize a local fight before retreating to the boss.
- Explicit regroup breaks ordered push; explicit push refreshes/restarts the ordered-push objective for the current target.
- Explicit suppression is objective-owned and does not live as a Default-local router branch.
- Ally support does not break a hold unless the support decision can be prepared first.
- Hold breakers prepare the next action when possible, so the router does not break into a branch that cannot actually execute.

## Push Model

`FollowerCombatPush` owns push decisions and push commitment lifecycle.

Default and marksman can ask push code for a pressure plan, but they still keep tactic-specific policy:

- Default push means assault pressure toward the enemy, an approach cover, or a search point.
- Marksman push support means finding a better shooting/support position, not rushing like default.
- Rifleman push support uses the same push event but keeps Rifleman policy: eligible nearby helpers support from cover or a nearby firing point and avoid duplicating the active pusher's destination.
- Push events are globally locked: one emitter owns the active push event, helpers cannot emit their own push while that event is active, and a short cooldown prevents emit/release/emit chains.
- Auto-push registers the first committed push target as an `AutoPush` mission. Temporary threats can pause the push and take `GoalEnemy`, but they do not overwrite that mission; when the temporary target clears, the original push target is restored if still alive.
- Against marksman enemies, push does not manufacture a fake hold if no valid firing-position push exists. It returns no push decision and lets the tactic continue routing.
- Ordered push is allowed to reach a pressure/firing point and hold it briefly through the shared committed-position hold, so unreachable marksman-style contacts become "fight from here and rescan" instead of repeated adjacent point selection.

Committed push breaks for:

- missing/dead mission enemy that cannot be restored from retention or target commitment
- enemy identity change, unless the new goal is an accepted temporary target that pauses the mission
- stable visible/shootable contact when the push action cannot safely keep moving and firing
- under-fire/recent-hit pressure
- explicit non-push command override
- run-to-enemy when sprint is impossible or the bot is not actually sprinting
- unseen push/search routes that become urban NavMesh detours away from the boss

Push enemy retention is refreshed during committed push so a contact/order push does not forget the enemy mid-route.

Automatic unseen search rushes are also gated before activation: if the enemy anchor is far outside the boss regroup envelope, the bot should not sprint into a blind push just because a search route exists. Ordered pushes remain command-owned. Urban route protection is applied while selecting cover on Streets and Ground Zero: a candidate whose NavMesh route is a major detour compared with the direct route to that same candidate is rejected before commitment. It does not interrupt an already active push by comparing unrelated boss and movement-target distances. When unseen search is blocked while the follower is already inside the boss regroup envelope, default combat should hold the enemy lane instead of reactivating the regroup objective and immediately completing it again.

## Movement And Arrival Holds

Non-push movement commitments are shared in `FollowerCombatCommon`.

Movement commitment is used for travel actions such as:

- `runToCover`
- `goToPoint`
- `goToPointTactical`
- `attackMoving`
- `attackMovingWithSuppress`
- `attackRetreat`
- `runToEnemy` / `goToEnemy` when not owned by push

Movement breaks for:

- under fire or recent hit
- visible shootable contact
- close visible contact
- boss-under-attack when the movement is not protected by active push/help policy
- arrival at the destination
- sprint impossibility for `runToEnemy`

The synthetic `moveToHealPoint.attackMoving` escape is the pressure exception: normal visibility, incoming fire, and recent damage keep the committed point active while its moving-fire overlay takes safe direct shots or tightly gated recent-contact suppression. Explosive danger and true point-blank contact can still force an immediate survival handoff.

When a movement action reaches cover or a point, common end logic arms a committed holder. The holder returns `holdPosition` for a short think window so the bot does not immediately reselect the same cover or churn into another travel action.

Arrival holders break for:

- explicit push order
- recent hit/damage
- under fire only when the follower is not actually in cover
- settled boss-under-attack support
- settled ally support when a valid support action can be prepared
- real enemy contact
- boss-distance regroup only when the hold reason is not protected
- timer expiry

If the follower is in cover and only under fire, the hold is extended instead of ended. Suppression against valid cover is treated as a reason to stay down, rescan, and look toward recent incoming-fire points, not as proof that the cover failed.

## Cover Contract

Cover is a shared lifecycle, not tactic-local geometry.

Cover lifecycle:

1. Select a candidate cover.
2. Commit that cover.
3. Move to the committed cover.
4. Detect arrival using EFT cover state, committed-cover proximity, or a go-to-point arrival that still matches the exact committed destination. A stale `GoToSomePointData.IsCome()` or merely assigned cover id cannot complete another cover-bound action.
5. Arm an arrival hold.
6. During hold, scan for immediate fire, damage pressure, suppression direction, support, protection, and regroup.
7. Keep holding if nothing stronger wins.

Cover selection is planner-owned and performance-bounded:

- `Covers.GetCoverPoints(...)` supplies one broad local candidate pool for a follower's combat evaluation frame.
- `FollowerCombatCommon` reuses that pool across later decision branches, including an empty result, instead of enumerating EFT cover again in the same frame.
- Navigation distance is memoized per candidate for the frame; intent-specific selectors rank the retained candidates for shooting, advance, retreat/heal, support, or bossward movement.
- On Streets and Ground Zero only, the same cached navigation distance rejects a cover candidate when reaching that cover would require a major building-scale detour relative to the direct distance to that cover.
- `GetClosestCoverPoint(...)` remains a convenience API outside this combat evaluation path; core combat does not use it as its final tactical selector.
- Cover-affined actions execute the committed cover or explicit point. `attackMoving` must not run its own vanilla `TryPointGetting` search and overwrite the planner's destination.

Cover intent matters:

- Shooting cover can stand up if a standing-height shot lane exists.
- Safe/retreat cover should not force standing fire unless a real lane is verified.
- Cover move actions should not keep running after proximity/arrival says the destination was reached.

## Grenades

Grenade use is now an explicit committed branch.

New grenade activation is checked after support/protection and before push. Once a grenade starts, the committed grenade decision moves near the top of routing so out-of-bounds, push, or cover replanning cannot cancel it accidentally.

Grenade activation requires:

- grenade config enabled
- valid combat enemy with a known position/person
- distance at least 15m, with the far limit decided from the selected grenade's timer/fuse up to the 40m outer envelope
- no active bot request or medical use
- safe throw position: the follower must be settled in cover that hides from the target, rather than using `cannot shoot` alone as permission to throw
- cooldown reservation
- not dogfighting, under fire, recently hit, or in a fresh first-seen window
- not already holding a reliable immediate-fire lane
- boss/followers not too close to the target

Committed grenade can still cancel if the throw becomes unsafe before release, the current throw arc no longer validates from the live weapon-root origin, combat enemy disappears before release, the grenade controller disappears, or the sequence times out. Regular grenade accepts and rejects are recorded as `grenadeEvent` entries so battle records can distinguish distance, timer/fuse, pressure, cover, cooldown, controller, and trajectory failures.

Cooldown is tied to the actual throw release, not just grenade decision initialization. The explicit runtime gate allows the chosen grenade action to attempt the throw, but `DoThrow` still respects follower individual and group cooldowns so one suppress-grenade action cannot chain multiple throws.

## Healing And Stims

Medical decisions are shared in `FollowerCombatCommon`.

Current behavior:

- Emergency healing can move to committed heal cover.
- Heal cover is reused instead of repeatedly selecting new cover.
- Heal action and stim action have timeout exits.
- Black limb pain during combat can prefer painkiller/stim use when available.
- Badly injured state can use health-support stims when available.
- Heal completion refreshes movement penalties without restoring full max health.
- The explicit force-heal hotkey restores body-part health and clears active light/heavy bleeding effects.
- First-aid refresh corrects vanilla med selection for active bleeding: if the selected med advertises bleed treatment but lacks enough remaining resource for the bleed-removal cost, followers prefer another usable med instead of looping the depleted one.
- Out of combat, patrol healing looks for a nearby cover point once before starting `HealAction`, then uses the same sprinting `runToHeal` cover action as combat healing. If no valid cover is found within 60m, healing can fall back to the current position. The cover search result is kept for the whole post-combat recovery sequence, including multiple first-aid/surgery actions, so patrol does not re-search or move to a different cover between med uses. Followers say `StartHeal` once per patrol heal sequence: when they start moving to found cover, or when they start healing if no cover movement is available.
- Between out-of-combat med uses, patrol treats `FirstAid.Damaged + HaveSmth2Use`, pending surgery, and recoverable top-off work as predicted future healing even while vanilla's `HEAL_DELAY_SEC` makes `Have2Do`/`ShallStartUse()` temporarily false. The same `HealAction` remains active and stationary through that cooldown; `FollowAction` only resumes after no treatable medical work remains.
- During an ordered push, pending medical work that is temporarily retry-blocked keeps one stable hold decision until the retry becomes available. It can still break immediately for a point-blank self-defense threat instead of ending and recreating the same hold every frame.
- Ordered-push arrival holds use the same committed-position lifecycle for tactical firing points and recovery cover. They remain stable until their exact commitment expires or a real medical/immediate-threat interrupt is ready, rather than falling through the generic unsupported-action end path.
- Post-combat full recovery uses vanilla first-aid/surgery plus the manual first-aid top-off path during a bounded recovery window. The first actual patrol `HealAction.Start()` starts a 12 second timer; during that window top-off can keep starting med animations for damaged limbs. Once the timer elapses, patrol stops starting new top-off actions, waits until medical use has ended and the main weapon is selected and ready, force-restores health through the same path as the explicit force-heal hotkey, clears the post-combat flag, and resumes movement.
- When retreating to heal but sprint/mobility is poor, recent contact can choose visible fire or suppression instead of walking with no pressure.
- A synthetic heal hide point uses one committed `moveToHealPoint.attackMoving` action. The follower keeps moving to that exact point under ordinary visibility/fire pressure and uses the shared moving-fire overlay for safe direct shots or recent-contact suppression; it does not end and recreate a plain `goToPoint` every time the enemy becomes shootable.
- Objective end routing classifies medical decisions from both `Action` and `Reason`. Generic movement actions with `runToHeal`, `moveToHeal`, or `moveToHealPoint` reasons remain medical retreats and must reach the shared heal-movement end checks before regroup, push, or combat-release logic can end them.
- Heal-cover arrival is distance/cover-id validated; a stale `GoToSomePointData.IsCome()` from an older destination cannot start `healInCover`. Active healing is cancelled when a visible enemy has a shootable lane, or when incoming-fire pressure catches the follower outside real cover.

Heal-cover exception: arriving at heal cover hands off to healing instead of normal cover hold.

## Rifleman Combat Behavior

Rifleman/default tactic is built around two main objectives:

- stay in useful cover near the boss when there is no good attack opportunity
- push/search/pressure when enemy state and aggression justify it

Rifleman aggression:

- `50%` is the default balanced baseline.
- Weapon aggression overrides can multiply the saved/effective aggression at combat read time. Current list: SR-2M Veresk uses `0.6x`, so saved `50%` behaves as `30%` while that weapon is active.
- Ammo penetration affects proactive automatic pushes against PMCs, raiders, bosses, and boss followers. Under `30` pen only allows proactive auto-push at very close range, unless it is early PMC gameplay around player level `22` or lower; bosses/raiders are always treated as high-level. High-capacity armor-wear setups can soften this from blocked to cautious only when the current ammo has at least `26` pen, high armor damage, and a drum-sized rifle/heavy-caliber magazine. `30-38` pen is normal until around player level `28`, then switches to cautious push style unless magazine capacity, caliber, and armor-damage score are high enough to plausibly wear armor down.
- High-capacity armor-wear logic is conservative: rifle/heavy calibers such as `5.45`, `5.56`, `7.62`, `.300`, `9x39`, and `.366` can benefit from `45+` round mags against PMCs and usually need `60+` against bosses/raiders; small calibers such as `9x19`, `9x21`, `4.6`, and `5.7` need much larger capacity and only normalize against PMCs, not bosses/raiders.
- Low-capacity weapons below the standard `30`-round rifle baseline can dampen proactive auto-push. Shotguns are handled by their close-range exception, and DMR/sniper-class weapons with a `20`-round magazine are not penalized for capacity, so their proactive push behavior is governed by ammo penetration and actual remaining ammo.
- Lower values bias toward boss-local cover, support, and regroup.
- `0%` suppresses proactive push/search pressure and is also the temporary behavior used by the combat `HoldPosition` command.
- Higher values allow farther and more frequent push/search/pressure decisions when threat checks allow them.

Default situational behavior:

- under damage/incoming-fire pressure, use one shared recovery predicate before both support selection and hold termination, then commit to one recovery maneuver before support, protection, regroup, or autonomous push can redirect the bot: sprint to credible cover within `12m` only when its route is screened from the enemy, otherwise move to the exact committed cover while facing and suppressing the threat; reject routes that cut through close enemy space or advance through an exposed fire lane
- sprint movement treats EFT's transient door status `0` as pending rather than as a closed-door veto, and only reports sprint engaged from the player's real movement context; if sprint never physically engages, recovery hands off to threat-facing moving fire
- moving/hold fire smooths the live enemy target across action changes and validates alignment against EFT's adjusted firearm fire-port vector rather than the perpendicular `WeaponRoot.forward` transform axis
- recovery movement ignores `bossUnderAttack` and boss-distance breaks until it reaches cover, becomes invalid/stalled, or a point-blank fight forces local self-defense; normal committed retreat movement recognizes physical/path arrival even when EFT's `IsInCover` flag is late
- if no credible cover exists, stand and fight a visible shootable enemy, otherwise suppress or threat-face in place for a `1.25s` commitment before retrying cover instead of ending and reselecting every frame
- a memory-only autonomous push rejection uses the same bounded stable no-cover hold contract; it breaks for renewed pressure, damage, visible fire, enemy loss, or hold expiry instead of ending as `notInCover` every frame
- if visible and shootable, shoot immediately using the corrected follower `EnemyInfo` senses
- if visible but not shootable, prefer firing cover or pressure movement
- dogfight ownership follows vanilla-style exit rules: do not end dogfight just because visibility/shootability flickers. The action clears stale movement on entry, then stays responsible for facing the threat, stopping unsafe fire, and micro-repositioning until the enemy is gone, out of dogfight range, or reload/cover-after-leave rules end it. An injured suppression-retreat may end dogfight only after its suppression target, safety lane, optional cover, and suppress controller have all initialized; that prepared decision is consumed by the next selection. Point-blank contact blocks this retreat handoff so injury cannot turn a live muzzle-range fight into per-frame dogfight/suppression churn. While dogfight is active, movement may step forward, backward, or sideways, but look/aim stays locked to the live fight target body/current position, falling back to the last known point only if live target data is unavailable. At point-blank range, if normal `CanShoot` flickers off but the muzzle has no hard obstruction to the live target chest, dogfight can use a direct close-contact shot after aim alignment and friendly-lane safety pass. In the close visible dogfight window, recent personal contact can also use a short SAIN-like continuity shot toward the last known/contact chest point when the enemy flickers out of `IsVisible`/`CanShoot`, but only if the weapon lane has no hard obstruction and friendly-lane safety passes. Dogfight always uses standing posture so its strafe/back-out movement is never converted into static crouched fire.
- when a committed run-to-cover temporarily loses `GoalEnemy`, recent incoming-fire direction keeps the walking fallback combat-facing and permits safety-gated suppression. If sprint becomes physically available again under pressure, the action retries it at a bounded one-second cadence instead of waiting for combat pressure to disappear.
- heal-cover movement is sticky against non-visible recent-hit pressure; it only breaks for immediate fire on a true point-blank visible shootable threat.
- if heal-cover movement reaches its committed cover but EFT has not yet marked the bot in cover and the bot is still under fire, keep the `runToHeal` action alive instead of ending/reselecting it every tick. Existing stall handling can still reject the cover if it remains ineffective.
- if enemy is unseen but recent enough, push/search can continue using retained enemy memory
- blocked/far push-search holds use a bounded think window instead of ending and reselecting every frame; pending medical work prevents automatic or ordered push phases from reopening until healing/recovery can take ownership
- if too far from boss and not protected by push/help/heal, switch to regroup objective
- if boss is attacked, prepare protection/support before breaking passive holds
- if ally is engaged, prepare support before breaking passive holds; a support firing point must have a complete NavMesh path and share the follower's or boss's current floor, and a point that makes no progress for four seconds is excluded briefly so deterministic scoring cannot select it again immediately
- shoot-from-place uses standing fire at close/medium range. Crouch is only eligible at `50m+`, with a living visible shootable enemy, no current/recent damage pressure, a stable distant firing situation (cover or no recent incoming threat), and both crouch-height lanes clear. The action stands before entry and reasserts standing after vanilla updates whenever that policy rejects crouch. Prone remains limited to `80m+` and now inherits the same stable distant-fire gate.

## Marksman Combat Behavior

Marksman is not default with different numbers. It has separate policy but shares common commitments.

Marksman aggression is tactic-relative:

- `30%` is the default marksman baseline.
- Weapon aggression overrides still apply at combat read time, after temporary command overrides. SR-2M Veresk currently uses `0.6x`.
- Marksman close automatic search uses the same ammo penetration push gate as Rifleman. Low-pen weapons do not proactively close on high-armor targets except at very close range, and mid-pen weapons switch to cautious close support after the player reaches later progression.
- `0%` blocks proactive automatic close-search/auto-search behavior, but does not block defensive automatic secondary use in close-quarter danger.
- Around `50%` keeps the current marksman support/reposition style but allows more willing close automatic support when conditions are safe.
- Higher values can make marksman use automatic-weapon offensive search more like Rifleman in distance and threat tolerance.
- Aggression affects proactive marksman automatic search/pressure only; normal marksman firing-position selection, support cover, and defensive secondary switching remain marksman policy.

Marksman behavior:

- prefers firing positions and support cover
- uses committed movement and arrival holds like default
- uses prepared-break decisions so support/protection only breaks hold when the next action is valid
- ignores generic assault push behavior unless marksman policy asks for close support/search
- can switch to automatic secondary for close-quarter fights
- does not run proactive automatic close-search while temporary boss `HoldPosition` aggression override is active
- switches back to primary when returning to support/reposition marksman decisions, and when combat drops before patrol reload maintenance starts
- supports team push/search through firing-position support, not blind rushing
- hands boss-distance regroup to the shared regroup objective

Marksman hold behavior scans periodically for:

- better shooting spot
- push support
- boss-under-attack support
- ally support
- boss-distance regroup
- timeout

## Regroup Objective

Combat regroup is objective-owned.

Regroup behavior:

- explicit combat regroup activates the regroup objective
- explicit push can leave regroup and activate the ordered-push objective
- autonomous boss-distance regroup waits through a short recent-fight grace window when the follower still has fresh personal enemy contact, recent hit/damage pressure, visible contact, or shootable contact
- extreme separation bypasses that grace so a follower who is very far out of bounds still rejoins
- hot contact regroup stays combat-active and moves bossward using withdraw-style movement
- cooled contact regroup runs directly toward boss / sampled boss position
- regroup may use bossward cover, but reaching that cover is only an intermediate step
- regroup completion is based on reaching the boss/bossward objective
- cooled `regroup.run` ignores `GoToSomePointData.IsCome()` from an older route and remains owned until the boss-distance arrival check succeeds; objective-owned withdraw targets only accept `IsCome()` when the active point still matches their exact target
- the run action refreshes its own sampled bossward point after exact arrival, at a bounded cadence, so a boss who changes floors or moves inside the broad anchor-refresh threshold cannot leave the follower standing at a completed intermediate point

Regroup movement also has its own short commitment so the bot does not recalculate bossward movement every tick.

## Battle Recorder

The battle recorder is separate from normal plugin logging.

It records combat-only JSONL timelines under the live client plugin folder's `BattleRecords/` directory. See `LOCAL.md` for the machine-local live client plugin folder.

It is intended to compare observed behavior with code behavior:

- decision changes
- action phases
- movement and aim/look state
- enemy/boss positions
- visibility and shootability
- source-tagged `GoalEnemy` transitions, including allowed sets, allowed clears, and retention-blocked clears
- grenades
- health/healing state
- churn and bad transition clusters
- recovery cover commitments/no-cover retry windows and distance-aware `posturePolicy` allow/veto events
- relevant hostile SAIN bot state while it is fighting the player/followers, plus a short post-contact tail
- source-tagged temporary combat-aggression override set/clear transitions

Use it to validate whether a bug is tactical routing, action execution, perception, or visual/player interpretation.

Schema version 4 retains the opponent-side `sainOpponentDecision` transitions and sampled `sainOpponentSnapshot` records introduced in version 3, and adds `combatAggressionOverride` events with the action, command/lifecycle source, previous value, current value, and pending clear timing. SAIN decisions are probed at their 10 Hz decision cadence while full snapshots retain the configured recorder interval; a low-frequency discovery probe also catches SAIN targeting the player before follower combat begins. The opponent record includes SAIN layer/action and combat/squad/self decisions, target contact, cover and active-path state, movement, aim/fire/suppression, health/medical state, and personality timing. It is intentionally relationship-gated: a SAIN bot is emitted only while it targets the player/follower, a recorded follower targets it, or for five seconds after that relationship disappears. This keeps the recorded timeline focused on the actual fight instead of writing snapshots for every SAIN bot in the raid.

Enemy provenance fields record the `BotSettingsClass.Cause` stored in `EnemyInfo.GroupInfo`, a coarse cause category, and whether that cause normally needs an awareness gate. Treat this as acquisition provenance rather than final truth: the cause is usually the first group-add reason and later reports may update enemy memory without changing the cause. `contact` fields compare personal seen timestamps with group sense/real-visibility timestamps, and `geometry` fields record straight distance, planar distance, and vertical delta so wall/building separation can be distinguished from pure floor separation.

Enemy visibility fields in snapshots include the corrected follower-facing `EnemyInfo` values plus validation context. Follower `isVisible`, `canShoot`, and `distance` are normalized after `EnemyInfo.CheckLookEnemy`; combat read paths also refresh distance/direction from the live enemy transform so stale retained enemies do not carry `float.MaxValue` distances into decisions. Stale `isVisible` / `canShoot` flags are cleared if their personal seen timestamps are not fresh, which prevents old room-to-room contacts from re-entering close visible dogfight. `isVisible` means direct follower-visible contact rather than sense/green-sense memory, and `canShoot` means a verified fire lane from the follower. `visibleType` is recorded for context, and `reliableShootLane` is a diagnostic hard-lane check for comparing corrected senses against the stricter head/body fire policy.

Sense correction owns general `EnemyInfo` truth. Keep extra checks only when they validate action-local constraints the corrected senses do not express: friendly fire lanes, crouch/prone weapon height, current aim-lane safety, soft foliage/grass suppression lanes, and short recent-contact continuity. Do not add new decision-level "is the visible/canShoot flag really true?" gates unless the correction layer cannot represent that condition.

Performance boundary: correction is a hot path because it runs after follower `EnemyInfo.CheckLookEnemy`. It only performs line probes for plausible visibility candidates: close contacts where foliage matters, raw visible/shootable contacts, raw direct-visible contacts, or contacts that were directly visible on the previous tick. Distant non-visible enemies are demoted using corrected distance without additional raycasts.

## Implementation Boundaries

Keep these boundaries stable:

- `FollowerCombatLogicBase`: objective routing and command handoff.
- `FollowerCombatDefault`: default tactic policy and default-only break choices.
- `FollowerCombatSniper`: marksman tactic policy and marksman-only break choices.
- `FollowerCombatPush`: push decision setup and committed push lifecycle.
- `FollowerCombatCommon`: shared commitments, cover, heal, grenade, visibility, support helpers, and common end conditions.
- Actions under `client/BigBrain/Actions`: execute a chosen decision; avoid moving planning policy here unless the action owns a runtime destination refresh.

## Future Guidance

- Do not add tactic-local state when the behavior is a shared commitment primitive.
- Do not break holds just because an opportunity might exist; prepare the next decision first.
- Do not emit movement decisions without a valid destination already assigned.
- Do not let boss-distance regroup interrupt push/help/heal commitments.
- Do not let `Enemy.IsVisible()` alone break push instantly; use stable visible plus shootable/contact gates.
- Prefer small, explicit interrupt reasons so battle records can explain churn.
- Keep comments focused on why a branch exists, not what the next line literally does.
