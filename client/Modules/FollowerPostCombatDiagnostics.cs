using EFT;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.Modules
{
    // Per-follower, passive and available in Release. No recorder or per-frame log allocation.
    internal sealed class FollowerPostCombatDiagnostics
    {
        private float nextSampleAt;
        private float stationarySince = -1f;
        private float nextLogAt;
        private Vector3 stationaryAnchor;
        private int messages;

        public void Update(BotOwner owner, BotFollowerPlayer follower)
        {
            float now = Time.time;
            if (now < nextSampleAt) return;
            nextSampleAt = now + 0.5f;

            if (pitFireTeam.UseSainFollowerCombat(owner) ||
                FollowerCombatLayer.IsFollowerCombatLayerActive(owner) ||
                FollowerMedical.IsUsingMedical(owner))
            {
                stationarySince = -1f;
                return;
            }

            string layer = owner.Brain?.BaseBrain?.CurLayerInfo?.Name();
            string reason = owner.Brain?.Agent?.LastResult().Reason;
            bool waiting = layer == "pitTeam.FollowerCombat" ||
                (layer == "pitTeam.FollowerPatrol" &&
                 (reason == pitTeam.BigBrain.FollowerPatrolLayer.CombatReadinessWaitReason ||
                  FollowerMedical.IsPostCombatFullHealActive(owner)));
            if (!waiting)
            {
                stationarySince = -1f;
                return;
            }

            if (stationarySince < 0f || (owner.Position - stationaryAnchor).sqrMagnitude > 0.25f)
            {
                stationarySince = now;
                stationaryAnchor = owner.Position;
                nextLogAt = now + 15f;
                messages = 0;
            }
            if (now < nextLogAt || messages >= 4) return;
            nextLogAt = now + 30f;
            messages++;

            EnemyInfo goal = owner.Memory?.GoalEnemy;
            pitFireTeam.Log.LogWarning(
                $"[PostCombat] follower={owner.Profile?.Nickname} id={owner.ProfileId} " +
                $"stationaryFor={now - stationarySince:F1} layer={layer} action={reason} " +
                $"readiness={follower.DescribePatrolCombatBlock()} haveEnemy={owner.Memory?.HaveEnemy} " +
                $"goal={goal?.ProfileId ?? "none"} goalAlive={BotFollowerPlayer.IsEnemyInfoAlive(goal)} " +
                $"visible={goal?.IsVisible} canShoot={goal?.CanShoot} " +
                $"firstAidPending={owner.Medecine?.FirstAid?.Have2Do} firstAidUsing={owner.Medecine?.FirstAid?.Using} " +
                $"surgeryPending={owner.Medecine?.SurgicalKit?.HaveWork} surgeryUsing={owner.Medecine?.SurgicalKit?.Using} " +
                $"weaponReady={owner.WeaponManager?.IsWeaponReady} moverPaused={owner.Mover?.Pause} " +
                $"{FollowerMedical.DescribePostCombatRecovery(owner)} {SainGoalEnemyBridge.DescribeGoalEnemy(owner)}");
        }
    }
}
