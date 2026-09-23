using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using SAIN.Components;
using SAIN.Preset.Shared.Enums;
using SAIN.Models.Enums;
using SAIN.SAINComponent.Classes;
using SAIN.SAINComponent.Classes.Decision;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;
using SAIN.SAINComponent.SubComponents.CoverFinder;

namespace pitTeam.SAINAddon;

internal enum SAINSquadSupportMode { None, OrderedSuppress, AllySupport, BossSupport, PushSupport }

// Squad intent owns target and lifetime. Native SAIN owns perception, firing and navigation.
internal sealed class SAINFollowerSquadSupportObjective(BotComponent bot, FiringPositionFinder finder)
{
    private readonly SainPushRiskBridge health = new(bot.BotOwner);
    private readonly CoverAnalyzer coverAnalyzer = new(bot, bot.Cover.CoverFinder);
    private Enemy target;
    private object ownedPath;
    private float deadline, burstUntil, arrivalUntil, nextScan, nextOpportunity, nextGruntOpportunity, inCoverSince = -1f;
    private float nextMove, nextValidation, lastTick = -1f, stalled, bestDistance;
    // Core cover-fire flicker grace; continuation never renews this timestamp.
    private float lastVisibleFireAt = -1f;
    private Vector3 lastVisibleFirePoint, lastVisibleFirePosition;
    private bool suppressionOwned, manualSuppressionOwned, shootingOwned, automaticWeapon, automaticWeaponReady;
    private SainMarksmanWeaponBridge? Weapons => SAINFollowerRuntime.GetMarksman(bot.BotOwner)?.Weapons;
    private SainSuppressionAim suppressionAim;
    private string fireState = "notChecked";
    private float fireCheckedAt = -1f;
    private string pendingEnemy;
    private float pendingUntil;
    private readonly Vector3[] failedPoints = new Vector3[4];
    private int failedCount;
    private string reason = "idle", source;
    private Vector3 anchor;
    private string failedEnemy;
    private Vector3 failedAnchor;
    private float failedUntil;
    internal SAINSquadSupportMode Mode { get; private set; }
    internal bool Active => Mode != SAINSquadSupportMode.None;
    // Publication ownership survives cancellation until the next native decision filter.
    internal bool OwnsAction { get; private set; }
    internal Vector3? Destination { get; private set; }
    internal string? EnemyId => target?.EnemyProfileId;
    private BotFollowerPlayer? Follower => BossPlayers.Instance?.GetFollower(bot.BotOwner);
    private pitAIBossPlayer? Boss => bot.BotOwner.BotFollower?.BossToFollow as pitAIBossPlayer;
    private bool stationarySupport;
    private bool StationaryFire => Ordered || stationarySupport;
    private bool CanSuppressHidden => Ordered || stationarySupport && Mode == SAINSquadSupportMode.BossSupport;
    private bool Grunt => SainAddonBridge.IsSainManSelected(bot.BotOwner);
    private bool Ordered => Mode == SAINSquadSupportMode.OrderedSuppress;

    internal static bool Valid(Enemy enemy) => enemy != null && enemy.WasValid && enemy.EnemyKnown &&
        Enemy.IsEnemyActive(enemy) && enemy.EnemyPlayer?.HealthController?.IsAlive == true &&
        enemy.LastKnownPosition.HasValue && Finite(enemy.LastKnownPosition.GetValueOrDefault());
    private static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) &&
        !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);
    private Enemy? Find(string id)
    {
        if (Valid(bot.GoalEnemy) && bot.GoalEnemy.EnemyProfileId == id) return bot.GoalEnemy;
        foreach (Enemy enemy in bot.EnemyController.KnownEnemies)
            if (Valid(enemy) && enemy.EnemyProfileId == id) return enemy;
        return null;
    }
    private bool Danger(ECombatDecision solo, ESelfActionType self) => self != ESelfActionType.None ||
        SainAddonBridge.IsUsingMedical(bot.BotOwner) || SainRegroupBridge.IsUnderFire(bot.BotOwner) ||
        bot.Medical.TimeSinceShot < 1f || bot.Suppression.IsHeavySuppressed ||
        bot.Memory.Health.HealthStatus == ETagStatus.BadlyInjured || bot.Memory.Health.HealthStatus == ETagStatus.Dying ||
        solo == ECombatDecision.Retreat || solo == ECombatDecision.DogFight || solo == ECombatDecision.MeleeAttack ||
        solo == ECombatDecision.AvoidGrenade || solo == ECombatDecision.ThrowGrenade || solo == ECombatDecision.FightZombies;

    internal void Observe()
    {
        if (!Active) return;
        if (bot.IsDead || Follower == null || !SainAddonBridge.IsAddonTacticSelected(bot.BotOwner) ||
            !SAINFollowerCombatHandoff.HasLiveEnemy(bot) || !Valid(target)) { Clear("contactLost"); return; }
        if (source == "sainSearch" && SAINFollowerRuntime.GetSearchLeader(bot.BotOwner) == null)
        { Clear("searchEnded"); return; }
        if (Mode == SAINSquadSupportMode.BossSupport && Follower.CombatIndependent) { Clear("independent"); return; }
        if (Follower.TryGetActiveCommand(out _, out _)) { Clear("replacementOrder"); return; }
        if (Time.time >= deadline || burstUntil > 0f && Time.time >= burstUntil || arrivalUntil > 0f && Time.time >= arrivalUntil)
            Finish(burstUntil > 0f ? "burstComplete" : "attemptComplete", Ordered && burstUntil <= 0f);
    }
    internal Enemy? PreferEnemy(Enemy? native)
    {
        Observe();
        if (!Active || native == null || native == target || native.IsVisible ||
            native.Seen && native.TimeSinceSeen < 2.5f || Danger(bot.Decision.CurrentCombatDecision, bot.Decision.CurrentSelfDecision)) return native;
        return target;
    }

    internal bool Filter(Enemy enemy, ECombatDecision solo, ESquadDecision squad, ESelfActionType self,
        out ESquadDecision result)
    {
        result = squad; OwnsAction = false; Observe();
        if (Danger(solo, self)) { if (Active) Clear("survival"); inCoverSince = -1f; return false; }
        var follower = Follower;
        if (follower == null) return false;
        bool pending = follower.TryGetActiveCommand(out var command, out _);
        if (pending && command == FollowerCommandType.SuppressEnemy)
        {
            string id = follower.SuppressEnemyTargetProfileId ?? bot.BotOwner.Memory?.GoalEnemy?.ProfileId;
            Enemy orderedTarget = Find(id);
            // Give SAIN its remaining Core command window to receive the accepted contact.
            if (orderedTarget == null)
            {
                if (pendingEnemy != id) { pendingEnemy = id; pendingUntil = Time.time + 3f; }
                if (Time.time >= pendingUntil)
                { follower.ClearCommand("SAIN:SuppressionContactUnavailable"); pendingEnemy = null; bot.BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false); }
                return false;
            }
            bool useAutomatic = follower.SuppressEnemyUseAutomaticSecondary && Weapons != null;
            pendingEnemy = null;
            follower.ClearCommand("SAIN:ConsumeSuppression");
            if ((useAutomatic ? !Weapons.SupportAvailable : !SainSquadSupportBridge.CanSuppress(bot.BotOwner)) || health.Read(orderedTarget.EnemyInfo).Medical)
            { bot.BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false); return false; }
            SAINFollowerRuntime.GetPush(bot.BotOwner)?.Clear("suppressionOrder");
            SAINFollowerRuntime.GetMarksman(bot.BotOwner)?.Clear("suppressionOrder");
            SAINFollowerRuntime.GetRegroup(bot.BotOwner)?.Clear("suppressionOrder");
            SAINFollowerRuntime.GetCover(bot.BotOwner)?.EndArrivalHold("suppressionOrder");
            Begin(orderedTarget, SAINSquadSupportMode.OrderedSuppress, "command");
            automaticWeapon = useAutomatic;
            if (automaticWeapon) deadline += 6f;
            // This explicit order replaces ordinary movement, but never medical/survival movement.
            bot.Cover.StopSeekingCover(); bot.Mover.Stop();
            bot.Suppression.ResetSuppressing(); bot.Shoot.EndShoot();
        }
        else if (pending) { pendingEnemy = null; return false; }
        else pendingEnemy = null;

        if (Active)
        {
            if (health.Read(target.EnemyInfo).Medical) { Clear("medicalPending"); return false; }
            // A different immediate threat belongs to native combat, never to support retargeting.
            if (enemy != target && enemy?.IsVisible == true && enemy.CanShoot) { Pause(); return false; }
            if (!StationaryFire && enemy == target && enemy.IsVisible && enemy.CanShoot)
            { Finish("nativeShot", false); return false; }
            if (automaticWeapon)
            {
                if (automaticWeaponReady && !Weapons.SupportReady) { Finish("supportWeaponLost", true); return false; }
                int preparation = Weapons.Prepare(true);
                if (preparation < 0) { Finish("supportWeaponUnavailable", true); return false; }
                if (preparation == 0) { OwnsAction = true; result = ESquadDecision.Suppress; return true; }
                if (!automaticWeaponReady) { automaticWeaponReady = true; deadline = Time.time + 6f; }
            }
            if (Ordered && !SainSquadSupportBridge.CanSuppress(bot.BotOwner)) { Finish("weaponUnavailable", true); return false; }
            OwnsAction = true; result = StationaryFire ? ESquadDecision.Suppress : ESquadDecision.Help; return true;
        }

        if (SAINFollowerRuntime.GetRegroup(bot.BotOwner)?.Active == true ||
            SAINFollowerRuntime.GetPush(bot.BotOwner)?.Active == true ||
            SAINFollowerRuntime.GetMarksman(bot.BotOwner)?.Ordered == true ||
            SAINFollowerRuntime.GetMarksman(bot.BotOwner)?.OwnsMovement == true ||
            SAINFollowerRuntime.GetMarksman(bot.BotOwner)?.Preparing == true ||
            bot.Mover.Moving || bot.Cover.CoverPoint_MovingTo != null)
        { inCoverSince = -1f; return false; }
        var coverState = bot.Cover.CoverSeekingState;
        if (Grunt && (coverState != ECoverSeekingState.NoCover && coverState != ECoverSeekingState.HoldInCover ||
            coverState == ECoverSeekingState.HoldInCover &&
            (bot.Cover.CoverInUse == null || bot.Cover.CoverInUse.Spotted || bot.Cover.CoverInUse.CoverData.IsBad)))
        { inCoverSince = -1f; return false; }
        if (Grunt && Time.time >= nextGruntOpportunity &&
            !(enemy?.IsVisible == true && enemy.CanShoot) &&
            (solo == ECombatDecision.SeekCover || solo == ECombatDecision.StandAndShoot || solo == ECombatDecision.ShootDistantEnemy ||
                solo == ECombatDecision.None && (squad == ESquadDecision.Help || squad == ESquadDecision.GroupSearch)) &&
            (squad == ESquadDecision.None || squad == ESquadDecision.Help || squad == ESquadDecision.GroupSearch))
        {
            nextGruntOpportunity = Time.time + 0.5f;
            Enemy bossTarget = BossSupportTarget(enemy);
            if (bossTarget != null && TryStartGruntSupport(bossTarget, SAINSquadSupportMode.BossSupport, "bossUnderAttack", null, null, out var bossDecision))
            { result = bossDecision; return true; }
            if (!(enemy?.IsVisible == true || enemy?.Seen == true && enemy.TimeSinceSeen < 2.5f) &&
                TryPushTarget(out Enemy pushTarget, out string pushSource, out Vector3? pusher, out Vector3? pushDestination) &&
                TryStartGruntSupport(pushTarget, SAINSquadSupportMode.PushSupport, pushSource, pusher, pushDestination, out var pushDecision))
            { result = pushDecision; return true; }
        }
        if ((solo != ECombatDecision.SeekCover && squad != ESquadDecision.Help && squad != ESquadDecision.GroupSearch) ||
            (squad != ESquadDecision.None && squad != ESquadDecision.Help && squad != ESquadDecision.GroupSearch) ||
            enemy?.IsVisible == true || enemy?.Seen == true && enemy.TimeSinceSeen < 2.5f)
        { inCoverSince = -1f; return false; }
        if (bot.Cover.CoverInUse == null || bot.Cover.CoverInUse.Spotted || bot.Cover.CoverInUse.CoverData.IsBad)
        { inCoverSince = -1f; return false; }
        if (inCoverSince < 0f) inCoverSince = Time.time;
        if (Time.time - inCoverSince < 1f || Time.time < nextOpportunity) return false;
        nextOpportunity = Time.time + 0.5f;
        Enemy candidate = SupportTarget(out string why, out Vector3? avoid);
        if (candidate == null) return false;
        if (Grunt)
        {
            if (!TryStartGruntSupport(candidate, SAINSquadSupportMode.AllySupport, why, null, avoid, out var allyDecision)) return false;
            result = allyDecision; return true;
        }
        if (health.Read(candidate.EnemyInfo).Medical) return false;
        if (failedEnemy != candidate.EnemyProfileId || (failedAnchor - candidate.LastKnownPosition.GetValueOrDefault()).sqrMagnitude >= 64f)
        { failedCount = 0; failedUntil = 0f; failedEnemy = candidate.EnemyProfileId; failedAnchor = candidate.LastKnownPosition.GetValueOrDefault(); }
        if (Time.time < failedUntil || failedCount >= failedPoints.Length) return false;
        if (!Plan(candidate, false, avoid, out Vector3 point)) return false;
        Begin(candidate, SAINSquadSupportMode.AllySupport, why); Commit(point);
        // A prepared successor may now break the passive arrival hold.
        SAINFollowerRuntime.GetCover(bot.BotOwner)?.EndArrivalHold("allySupport");
        SAINFollowerRuntime.GetMarksman(bot.BotOwner)?.Clear("squadSupport");
        OwnsAction = true; result = ESquadDecision.Help; return true;
    }

    private Enemy? BossSupportTarget(Enemy current)
    {
        var follower = Follower; var boss = Boss;
        // Core Rifleman protection gates, using native personal sight and known identity.
        if (follower == null || follower.CombatIndependent || follower.BossProtectionWillingness01 < 0.45f ||
            boss?.GetBossLogic()?.IsHitted != true ||
            bot.BotOwner.Memory.HaveEnemy && !(current?.Seen == true && current.TimeSinceSeen <= 2.5f)) return null;
        BotOwner attacker = boss.ClosestEnemy();
        return attacker?.GetPlayer?.HealthController?.IsAlive == true ? Find(attacker.ProfileId) : null;
    }
    private bool TryPushTarget(out Enemy enemy, out string why, out Vector3? pusher, out Vector3? avoid)
    {
        enemy = null; why = null; pusher = avoid = null;
        var boss = Boss; if (boss == null) return false;
        if (boss.CombatEvents.TryGetActivePushFor(bot.BotOwner, out var push) &&
            bot.GoalEnemy?.EnemyProfileId == push.EnemyProfileId && NearHelper(push.Owner))
        { enemy = Find(push.EnemyProfileId); why = "corePush"; pusher = push.Owner.Position; avoid = push.Destination; return enemy != null; }
        foreach (BotOwner ally in boss.Followers)
        {
            if (ally == null || ally == bot.BotOwner || ally.IsDead) continue;
            var advance = SAINFollowerRuntime.GetPush(ally);
            if (advance?.Mode == SAINPushMode.Automatic && advance.OwnsMovement && advance.Destination.HasValue &&
                bot.GoalEnemy?.EnemyProfileId == advance.EnemyId && NearHelper(ally))
            { enemy = Find(advance.EnemyId); why = "sainPush"; pusher = ally.Position; avoid = advance.Destination; return enemy != null; }
        }
        return false;
    }
    private bool TryStartGruntSupport(Enemy enemy, SAINSquadSupportMode mode, string why,
        Vector3? pusher, Vector3? avoid, out ESquadDecision result)
    {
        result = ESquadDecision.None;
        if (!Valid(enemy) || health.Read(enemy.EnemyInfo).Medical) return false;
        if (failedEnemy != enemy.EnemyProfileId || (failedAnchor - enemy.LastKnownPosition.GetValueOrDefault()).sqrMagnitude >= 64f)
        { failedCount = 0; failedUntil = 0f; failedEnemy = enemy.EnemyProfileId; failedAnchor = enemy.LastKnownPosition.GetValueOrDefault(); }
        if (Time.time < failedUntil) return false;
        bool fire = enemy.IsVisible && enemy.CanShoot;
        bool suppress = !fire && mode == SAINSquadSupportMode.BossSupport && !bot.Mover.Running &&
            bot.Decision.SelfActionDecisions.AmmoRatio >= 0.1f &&
            SainSquadSupportBridge.CanSuppress(bot.BotOwner) && SainSquadSupportBridge.ResolveSuppressionAim(bot, enemy).Ready;
        Vector3 point = default;
        if (!fire && !suppress && (failedCount >= failedPoints.Length ||
            !Plan(enemy, false, avoid, out point, pusher, mode == SAINSquadSupportMode.BossSupport))) return false;
        Begin(enemy, mode, why, fire || suppress);
        if (!stationarySupport) Commit(point);
        SAINFollowerRuntime.GetCover(bot.BotOwner)?.EndArrivalHold(why);
        OwnsAction = true; result = stationarySupport ? ESquadDecision.Suppress : ESquadDecision.Help;
        return true;
    }

    private Enemy? SupportTarget(out string why, out Vector3? avoid)
    {
        why = "allyFight"; avoid = null;
        var boss = Boss;
        if (boss == null) return null;
        // Core engagement is a cue for identity only; use the receiver's native known position.
        if (boss.IsPlayerEngaging(out string playerTarget, out _) && Find(playerTarget) is Enemy playerEnemy)
        { why = "playerFight"; return playerEnemy; }
        var searcher = !Grunt ? SAINFollowerRuntime.GetSearchLeader(bot.BotOwner) : null;
        if (searcher != null && NearHelper(searcher.BotOwner))
        { why = "sainSearch"; avoid = searcher.Position; return Find(searcher.GoalEnemy.EnemyProfileId); }
        if (!Grunt && boss.CombatEvents.TryGetActivePushFor(bot.BotOwner, out var push) &&
            bot.GoalEnemy?.EnemyProfileId == push.EnemyProfileId && NearHelper(push.Owner))
        { why = "corePush"; avoid = push.Destination; return Find(push.EnemyProfileId); }
        foreach (BotOwner ally in boss.Followers)
        {
            if (ally == null || ally == bot.BotOwner || ally.IsDead) continue;
            var advance = SAINFollowerRuntime.GetPush(ally);
            if (!Grunt && advance?.Mode == SAINPushMode.Automatic && advance.OwnsMovement && advance.Destination.HasValue &&
                bot.GoalEnemy?.EnemyProfileId == advance.EnemyId && NearHelper(ally))
            { why = "sainPush"; avoid = advance.Destination; return Find(advance.EnemyId); }
            var goal = ally.Memory?.GoalEnemy;
            if (goal?.IsVisible == true && goal.CanShoot && Find(goal.ProfileId) is Enemy contact) return contact;
        }
        return null;
    }
    private bool NearHelper(BotOwner ally) => ally != null && (ally.Position - bot.Position).sqrMagnitude <= 45f * 45f &&
        SainRegroupBridge.TryGetDistance(bot.Position, ally.Position, out float path) && path <= 65f;

    private bool Plan(Enemy enemy, bool ordered, Vector3? avoid, out Vector3 point, Vector3? pusher = null, bool bossSupport = false)
    {
        point = default;
        if (Time.time < nextScan) return false;
        nextScan = Time.time + 2f;
        if (Grunt && !ordered)
        {
            // Reuse at most four already-discovered native covers; no new overlap/search.
            int probes = 0;
            foreach (CoverPoint cover in bot.Cover.CoverPoints)
            {
                if (++probes > 4) break;
                if (cover == null || cover == bot.Cover.CoverInUse || cover.Spotted || cover.CoverData.IsBad) continue;
                Vector3 known = enemy.LastKnownPosition.GetValueOrDefault();
                if (!coverAnalyzer.RecheckCoverPoint(cover, known, (known - bot.NavMeshPosition).normalized, bot.NavMeshPosition, out _)) continue;
                // Native recheck can move the point: admit its final position, not the cached one.
                if (!Allowed(cover.Position, enemy, ordered, avoid, pusher, bossSupport)) continue;
                if (Physics.Linecast(cover.Position + Vector3.up * 1.5f, known + Vector3.up * 1.1f,
                    LayersMaskController.HighPolyWithTerrainNoGrassMask)) continue;
                point = cover.Position; return true;
            }
        }
        finder.Clear();
        if (!finder.Find(enemy) || !finder.Position.HasValue) return false;
        point = finder.Position.Value;
        return Allowed(point, enemy, ordered, avoid, pusher, bossSupport);
    }
    private bool Allowed(Vector3 point, Enemy enemy, bool ordered, Vector3? avoid, Vector3? pusher, bool bossSupport)
    {
        if (!Finite(point) || (point - bot.Position).sqrMagnitude <= 4f ||
            !SainRegroupBridge.SameLevel(point, bot.Position) || !SainRegroupBridge.IsDestinationAvailable(bot.BotOwner, point) ||
            !SainRegroupBridge.TryGetDistance(bot.Position, point, out float path) || path > (SainAddonBridge.IsShooterSelected(bot.BotOwner) ? 90f : 45f)) return false;
        if (avoid.HasValue && (point - avoid.Value).sqrMagnitude < 16f) return false;
        if (!ordered && failedEnemy == enemy.EnemyProfileId)
            for (int i = 0; i < failedCount; i++) if ((point - failedPoints[i]).sqrMagnitude < 16f) return false;
        Vector3 known = enemy.LastKnownPosition.GetValueOrDefault();
        if (!ordered)
        {
            // Supporting fire must not silently become another assault.
            if (pusher.HasValue)
            { if (!SainSquadSupportBridge.IsPushSupportPosition(point, pusher.Value, known)) return false; }
            else if (!bossSupport && (point - known).magnitude + 1.5f < (bot.Position - known).magnitude) return false;
            if (!Follower.CombatIndependent && SainPlayerSquadBridge.TryGetPlayerLeader(bot.BotOwner, out Player player) &&
                (point - player.Position).magnitude > SainRegroupBridge.GetTriggerDistance(bot.BotOwner)) return false;
        }
        if (SainAddonBridge.IsShooterSelected(bot.BotOwner))
        { Vector3 delta = point - known; delta.y = 0f; if (delta.sqrMagnitude < 256f) return false; }
        return true;
    }
    private void Begin(Enemy enemy, SAINSquadSupportMode mode, string why, bool stationary = false)
    {
        Clear("replaced"); target = enemy; Mode = mode; source = why; anchor = enemy.LastKnownPosition.GetValueOrDefault();
        suppressionAim = default; fireState = "notChecked"; fireCheckedAt = -1f;
        stationarySupport = stationary;
        deadline = Time.time + (StationaryFire ? 6f : 20f); burstUntil = arrivalUntil = 0f;
        nextMove = nextValidation = 0f; stalled = 0f; lastTick = -1f; Record("begin");
    }
    private void Commit(Vector3 point)
    {
        Destination = point; bestDistance = (point - bot.Position).magnitude;
        SainRegroupBridge.Claim(bot.BotOwner, point); Record("firingPosition");
    }
    internal void Tick()
    {
        Observe();
        if (!Active || !OwnsAction) { Pause(); return; }
        if (Danger(bot.Decision.CurrentCombatDecision, bot.Decision.CurrentSelfDecision)) { Clear("survival"); return; }
        if (automaticWeapon && Weapons?.SupportReady != true) { Pause(); return; }
        if (!Destination.HasValue)
        {
            if (Ordered && burstUntil <= 0f && !target.IsVisible && Time.time >= nextScan)
            {
                var aim = SainSquadSupportBridge.ResolveSuppressionAim(bot, target);
                if (!aim.Ready)
                { if (Plan(target, true, null, out Vector3 point)) Commit(point); }
                else nextScan = Time.time + 2f;
            }
            return;
        }
        Vector3 destination = Destination.Value;
        if ((bot.Position - destination).sqrMagnitude <= 4f && SainRegroupBridge.SameLevel(bot.Position, destination))
        {
            Pause();
            if (arrivalUntil <= 0f) { arrivalUntil = Time.time + 2f; Record("arrivalHold"); }
            return;
        }
        if (Time.time >= nextValidation)
        {
            nextValidation = Time.time + 1f;
            if (!SainRegroupBridge.IsDestinationAvailable(bot.BotOwner, destination) ||
                (anchor - target.LastKnownPosition.GetValueOrDefault()).sqrMagnitude >= 64f) { Finish("positionInvalidated", Ordered); return; }
            SainRegroupBridge.Claim(bot.BotOwner, destination);
        }
        float distance = (destination - bot.Position).magnitude;
        float elapsed = lastTick < 0f ? 0f : Mathf.Max(0f, Time.time - lastTick); lastTick = Time.time;
        if (distance < bestDistance - 0.35f) { bestDistance = distance; stalled = 0f; } else stalled += elapsed;
        if (stalled >= 4f) { Finish("stalled", Ordered); return; }
        if (Time.time < nextMove) return;
        nextMove = Time.time + 1f;
        bot.Mover.SetTargetPose(1f); bot.Mover.SetTargetMoveSpeed(1f);
        if (bot.Mover.WalkToPoint(destination, true)) ownedPath = bot.Mover.ActivePath;
        else Finish("pathRejected", Ordered);
    }
    internal void Steer()
    {
        Observe();
        if (!Active || !OwnsAction) return;
        if (Danger(bot.Decision.CurrentCombatDecision, bot.Decision.CurrentSelfDecision)) { Clear("survival"); return; }
        if (automaticWeapon && Weapons?.SupportReady != true) { StopSuppression(); bot.Steering.LookToLastKnownEnemyPosition(target); return; }
        bool arrived = !Destination.HasValue || (Destination.Value - bot.Position).sqrMagnitude <= 4f;
        if (!arrived) { bot.Steering.LookToMovingDirection(); return; }
        bool fired = false, keepAim = false;
        if (target.IsVisible && target.CanShoot)
        {
            StopSuppression(); shootingOwned = true; keepAim = true;
            fired = SainSquadSupportBridge.FireVisible(bot, target) && bot.BotOwner.ShootData.Shooting;
            if (fired)
            {
                var aiming = bot.BotOwner.AimingManager?.CurrentAiming;
                lastVisibleFireAt = aiming != null && Finite(aiming.RealTargetPoint) ? Time.time : -1f;
                lastVisibleFirePoint = aiming?.RealTargetPoint ?? default;
                lastVisibleFirePosition = bot.Position;
            }
            FireDiagnostic(fired ? "firingVisible" : "nativeAimOrTriggerPending", default);
        }
        else if (target.IsVisible && shootingOwned && lastVisibleFireAt >= 0f &&
            Time.time - lastVisibleFireAt < 0.5f &&
            (bot.Position - lastVisibleFirePosition).sqrMagnitude <= 0.75f * 0.75f &&
            bot.BotOwner.ShootData.Shooting && SainSquadSupportBridge.CanContinueVisibleFire(bot, lastVisibleFirePoint))
        {
            bot.Steering.LookToPoint(lastVisibleFirePoint);
            fired = keepAim = true;
            FireDiagnostic("visibleFlickerGrace", default);
        }
        else if (CanSuppressHidden)
        {
            SainSuppressionAim aim = default;
            string state;
            if (!SainSquadSupportBridge.CanSuppress(bot.BotOwner)) state = "weaponUnavailable";
            else if (bot.Mover.Running) state = "running";
            else if (bot.Decision.SelfActionDecisions.AmmoRatio < 0.1f) state = "lowAmmo";
            else
            {
                aim = SainSquadSupportBridge.ResolveSuppressionAim(bot, target);
                state = aim.Gate;
                if (aim.Ready && aim.Point is Vector3 point)
                {
                    bot.Steering.LookToPoint(point); keepAim = true;
                    if (shootingOwned) { bot.Shoot.EndShoot(); shootingOwned = false; }
                    // Preserve native alignment/weapon/friendly/trigger safety. The point is
                    // already admitted, so do not ask native path-point selection to veto it again.
                    if (bot.Steering.AngleToPointFromLookDir(point) > 10f) state = "aligning";
                    else
                    {
                        fired = FireSuppression(point, aim.Source == "coreRecentReport");
                        state = fired ? "firingSuppression" : "nativeTriggerRejected";
                    }
                }
            }
            FireDiagnostic(state, aim);
        }
        else StopSuppression();
        if (fired && StationaryFire && burstUntil <= 0f) { burstUntil = Time.time + 2f; arrivalUntil = 0f; Record("burstStarted"); }
        if (!fired)
        {
            StopSuppression();
            if ((!target.IsVisible || !target.CanShoot) && shootingOwned)
            { bot.Shoot.EndShoot(); shootingOwned = false; lastVisibleFireAt = -1f; }
            // A turn/trigger delay must not overwrite the admitted suppression point with
            // a different native path/look point in the same steering tick.
            if (!keepAim) bot.Steering.LookToLastKnownEnemyPosition(target);
        }
    }
    private void FireDiagnostic(string state, SainSuppressionAim aim)
    {
        bool changed = fireState != state || suppressionAim.Source != aim.Source || suppressionAim.Gate != aim.Gate;
        fireState = state; suppressionAim = aim; fireCheckedAt = Time.time;
        if (changed && SainCombatRecorderBridge.IsRecording)
            SainCombatRecorderBridge.RecordEvent(bot.BotOwner, "sainSuppressionFire", Snapshot);
    }
    internal void Pause()
    {
        if (ownedPath != null && ReferenceEquals(bot.Mover.ActivePath, ownedPath)) bot.Mover.Stop();
        ownedPath = null; lastTick = -1f; StopSuppression();
        if (shootingOwned) bot.Shoot.EndShoot();
        shootingOwned = false; lastVisibleFireAt = -1f;
    }
    private bool FireSuppression(Vector3 point, bool reported)
    {
        if (reported)
        {
            if (!manualSuppressionOwned)
            {
                // Native suppression's updater requires its own SuppressionTarget and
                // otherwise resets/replaces our admitted report on the next update.
                StopSuppression();
                bot.Suppression.ResetSuppressing();
                manualSuppressionOwned = true;
            }
            bool fired = bot.ManualShoot.TryShoot(target, point, true, EShootReason.Suppress);
            target.Status.EnemyIsSuppressed = fired;
            return fired;
        }
        if (manualSuppressionOwned) StopSuppression();
        suppressionOwned = true;
        return bot.Suppression.SuppressPosition(point, target);
    }
    private void StopSuppression()
    {
        if (suppressionOwned) bot.Suppression.ResetSuppressing();
        if (manualSuppressionOwned)
        {
            bot.ManualShoot.Reset();
            if (target != null) target.Status.EnemyIsSuppressed = false;
        }
        suppressionOwned = manualSuppressionOwned = false;
    }
    private void Finish(string why, bool negative)
    {
        failedEnemy = EnemyId; failedAnchor = target?.LastKnownPosition ?? anchor; failedUntil = Time.time + 4f;
        if (!Ordered && Destination.HasValue && why != "nativeShot" && failedCount < failedPoints.Length)
            failedPoints[failedCount++] = Destination.Value;
        if (negative) bot.BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
        // Keep completed action inert until the next publication replaces its squad decision.
        Clear(why);
    }
    internal void Clear(string why)
    {
        if (Active) Record(why);
        Pause();
        if (Destination.HasValue) SainRegroupBridge.Release(bot.BotOwner, Destination.Value);
        if (automaticWeapon) Weapons?.Cancel(why == "combatEnded" || why == "release" || why == "nativeStateReplaced" ? null : target);
        automaticWeapon = automaticWeaponReady = stationarySupport = false;
        Destination = null; target = null; Mode = SAINSquadSupportMode.None;
        finder.Clear(); inCoverSince = -1f;
        if (why == "release" || why == "combatEnded" || why == "nativeStateReplaced")
        { failedCount = 0; failedEnemy = null; failedUntil = 0f; pendingEnemy = null; pendingUntil = 0f; }
    }
    private void Record(string why)
    {
        reason = why;
        if (SainCombatRecorderBridge.IsRecording)
            SainCombatRecorderBridge.RecordEvent(bot.BotOwner, "sainSquadSupport", Snapshot);
    }
    internal object Snapshot => new { mode = Mode.ToString(), reason, source, stationarySupport, enemyId = EnemyId,
        destination = SAINFollowerRecorder.Point(Destination), ownsAction = OwnsAction,
        fireState, fireCheckedAt, suppressionSource = suppressionAim.Source, laneGate = suppressionAim.Gate,
        nativeSuppressionPoint = SAINFollowerRecorder.Point(suppressionAim.NativePoint),
        suppressionPoint = SAINFollowerRecorder.Point(suppressionAim.Point),
        remaining = Active ? Mathf.Max(0f, deadline - Time.time) : 0f, burstRemaining = Mathf.Max(0f, burstUntil - Time.time) };
}
