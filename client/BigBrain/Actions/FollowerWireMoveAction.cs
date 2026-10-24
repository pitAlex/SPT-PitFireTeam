using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Utils;
using UnityEngine;

namespace pitTeam.BigBrain.Actions
{
    internal sealed class WireMoveData : CustomLayer.ActionData
    {
        internal readonly Vector3[] Route;
        internal readonly bool Escaping;
        internal WireMoveData(Vector3[] route, bool escaping) { Route = route; Escaping = escaping; }
    }

    internal sealed class FollowerWireMoveAction : CustomLogic
    {
        private bool issued;
        public FollowerWireMoveAction(BotOwner owner) : base(owner) { }
        public override void Start() { base.Start(); issued = false; }

        public override void Update(CustomLayer.ActionData data)
        {
            if (!Modules.FollowerHazardAvoidance.IsSupportedMap || !(data is WireMoveData move) || move.Route == null || move.Route.Length < 2) return;
            if (!issued)
            {
                issued = true;
                if (move.Escaping)
                {
                    FollowerMedical.CancelActiveMedical(BotOwner);
                    Modules.SainAddonBridge.ReleaseWireCover(BotOwner);
                }
                BotOwner.Mover.Pause = false;
                BotOwner.Mover.GoToByWay(move.Route, 0.25f);
            }
            FollowerRecovery.StopShooting(BotOwner);
            BotOwner.Mover.Pause = false;
            BotOwner.Mover.Sprint(false, false);
            BotOwner.GetPlayer?.EnableSprint(false);
            if (BotOwner.BotLay?.IsLay == true) BotOwner.BotLay.GetUp(false);
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            BotOwner.Steering.LookToMovingDirection();
            BotOwner.DoorOpener.UpdateDoorInteractionStatus();
        }

        public override void Stop()
        {
            BotOwner.Mover?.Stop();
            BotOwner.StopMove();
            issued = false;
            base.Stop();
        }
    }
}
