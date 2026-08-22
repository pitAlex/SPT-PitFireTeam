using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Thin wrapper for vanilla first-aid/surgery logic. Layer end conditions own timeouts and
    /// stuck-state cleanup; this action only lets the medical node update.
    /// </summary>
    internal class HealAction : CustomLogic
    {
        private HealNode baseLogic;
        private float nextMedicalRefreshAt;
        public HealAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new HealNode(botOwner);
        }

        public override void Start()
        {
            base.Start();
            FollowerRecovery.StopShooting(BotOwner);
            FollowerMedical.MarkPostCombatFullHealActionStarted(BotOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            FollowerRecovery.StopShooting(BotOwner);
            if (BotOwner?.Medecine?.Using != true && Time.time >= nextMedicalRefreshAt)
            {
                nextMedicalRefreshAt = Time.time + 0.5f;
                FollowerMedical.RefreshMedicalWork(BotOwner);
            }

            baseLogic.UpdateNodeByBrain(data);
            FollowerRecovery.StopShooting(BotOwner);

            if (BotOwner?.Medecine?.Using != true)
            {
                FollowerMedical.TryStartFirstAidTopOff(BotOwner);
            }
        }
    }
}
