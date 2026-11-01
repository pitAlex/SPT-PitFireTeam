using EFT;
using pitTeam.BigBrain;
using pitTeam.BigBrain.Actions;

namespace pitTeam.GoingRogue.Combat
{
    public sealed class KnightCombatLayer : GoonCombatLayer
    {
        private KnightSquadCoordinator coordinator;
        public KnightCombatLayer(BotOwner owner, int priority)
            : base(owner, priority, "bossKnight", "Knight", "Knight") { }
        internal static bool IsOwned(BotOwner owner) => GoonCombatLayer.IsOwned(owner, "bossKnight");
        internal override FollowerCombatLogicBase CreateLogic(BotOwner owner) => new KnightCombatLogic(owner);
        internal override bool InitializeRole()
        {
            if (BotOwner.Brain.BaseBrain is not BossKnightLayersStrategy strategy || BotOwner.Boss == null) return false;
            if (BotOwner.Boss.BossLogic == null) BotOwner.Boss.Activate();
            if (BotOwner.Boss.BossLogic is not BossKnight nativeBoss) return false;
            BotOwner.Boss.IamBoss = true; strategy._fightLayer.SetBossData(nativeBoss);
            coordinator = new KnightSquadCoordinator(BotOwner);
            return true;
        }
        internal override void RefreshRole() => coordinator?.Refresh();
        internal override void ReleaseRole() { coordinator?.Dispose(); coordinator = null; }
        internal override Action CreateAction(BotOwner owner, AICoreActionResult<BotLogicDecision, CoreActionResultParams> decision)
            => decision.Action == BotLogicDecision.lay
                ? new Action(typeof(KnightLayAction), decision.Reason,
                    new FollowerCombatActionData(decision.Action, decision.Reason, decision.Data))
                : FollowerCombatLayer.CreateBigBrainAction(owner, decision);
    }
}