using System;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using pitTeam.BigBrain.Actions;
using pitTeam.Modules;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain
{
    // Above our combat brains, below native grenade/BTR escape and the tripwire layer.
    internal sealed class FollowerWireLayer : CustomLayer
    {
        internal const int LayerPriority = 76;
        private Vector3[] route;
        private bool escaping;
        private float nextProbeAt, nextNavigationAt, stationarySince, lastProgressAt, startedAt;
        private Vector3 lastPosition;
        private FollowerCommandType command;
        private float commandUntil;
        private int moveIssue, pushIssue;
        private bool reportedFailure;

        public FollowerWireLayer(BotOwner owner, int priority) : base(owner, priority) { }
        public override string GetName() => "pitTeam.FollowerWire";

        public override bool IsActive()
        {
            try
            {
                if (!FollowerHazardAvoidance.IsSupportedMap || BotOwner == null || BotOwner.IsDead || !BossPlayers.IsFollower(BotOwner)) return false;
                if (route == null && Time.time < nextProbeAt) return false;
                if (BotOwner.BewareGrenade?.ShallRunAway() == true || BotOwner.BewareBTR?.ShallRunAway() == true ||
                    BotOwner.WeaponManager?.Grenades?.ThrowindNow == true) return false;
                if (route != null) return !HasFinished();
                nextProbeAt = Time.time + 0.5f;
                Vector3 position = BotOwner.Position;
                if ((position - lastPosition).sqrMagnitude > 0.0025f) stationarySince = Time.time;
                lastPosition = position;
                bool medical = FollowerMedical.IsUsingMedical(BotOwner);
                bool pending = BotOwner.Medecine?.FirstAid?.Have2Do == true || BotOwner.Medecine?.SurgicalKit?.HaveWork == true;
                bool inside = FollowerWireNavigation.IsInside(position);
                // Ordinary crossings keep their movement. Treatment cannot keep a settled bot in wire.
                if (inside && !medical && (!pending || Time.time - stationarySince < 0.75f)) return false;
                if (!inside && medical) return false;
                if (!FollowerWireNavigation.TryPlan(BotOwner, inside, out route, out escaping, ref nextNavigationAt)) return false;
                var follower = BossPlayers.Instance?.GetFollower(BotOwner);
                follower?.TryPeekActiveCommand(out command, out _, out commandUntil);
                moveIssue = follower?.MoveToPointIssueSequence ?? 0;
                pushIssue = follower?.PushEnemyIssueSequence ?? 0;
                startedAt = lastProgressAt = Time.time;
                lastPosition = position;
                return true;
            }
            catch (Exception ex)
            {
                nextProbeAt = Time.time + 5f;
                route = null;
                if (!reportedFailure)
                {
                    reportedFailure = true;
                    Modules.Logger.LogError($"[WireAwareness] Follower local route failed: {ex.Message}");
                }
                return false;
            }
        }

        public override Action GetNextAction()
        {
            Modules.Logger.LogInfo($"[WireAwareness] {BotOwner.ProfileId}: {(escaping ? "stepClearBeforeHeal" : "localBypass")}");
            return new Action(typeof(FollowerWireMoveAction), escaping ? "wireEscape" : "wireBypass", new WireMoveData(route, escaping));
        }

        public override bool IsCurrentActionEnding() => HasFinished();

        private bool HasFinished()
        {
            if (!FollowerHazardAvoidance.IsSupportedMap || route == null) return true;
            if (!escaping)
            {
                if (FollowerMedical.IsUsingMedical(BotOwner)) return true;
                var follower = BossPlayers.Instance?.GetFollower(BotOwner);
                if (follower != null)
                {
                    follower.TryPeekActiveCommand(out FollowerCommandType now, out _, out float until);
                    if (now != command || until != commandUntil || follower.MoveToPointIssueSequence != moveIssue ||
                        follower.PushEnemyIssueSequence != pushIssue) return true;
                }
            }
            Vector3 position = BotOwner.Position;
            if ((position - lastPosition).sqrMagnitude >= 0.0225f)
            {
                lastPosition = position;
                lastProgressAt = Time.time;
            }
            return (escaping ? !FollowerWireNavigation.IsInside(position) :
                (position - route[route.Length - 1]).sqrMagnitude <= 0.16f) ||
                Time.time - lastProgressAt > 3f || Time.time - startedAt > 15f;
        }

        public override void Stop()
        {
            route = null;
            nextProbeAt = Time.time + 2f;
            BotOwner.Mover?.Stop();
            base.Stop();
        }
    }
}
