using EFT;
using pitTeam.BigBrain;
using UnityEngine;

namespace pitTeam.GoingRogue.Combat
{
    internal sealed partial class KnightVanillaCombat
    {
        private FollowerCombatCommon common;
        private bool subscribed;
        private int positionRevision;
        internal BotLogicDecision? LastSelectedDecision { get; set; }
        internal float RetryUntil { get; private set; }
        BotLogicDecision? IGoonCombatSelector.LastSelectedDecision { get => LastSelectedDecision; set => LastSelectedDecision = value; }
        float IGoonCombatSelector.RetryUntil => RetryUntil;
        void IGoonCombatSelector.Begin(FollowerCombatCommon context) => Begin(context);
        void IGoonCombatSelector.InvalidatePosition() => InvalidatePosition();

        internal void Begin(FollowerCombatCommon context)
        {
            common = context;
            if (!subscribed)
            {
                Subscribe();
                subscribed = true;
            }
            _enterFightLayerTime = Time.time;
            _badCovers = 0;
            LastSelectedDecision = null;
            _pointOfSearch = null;
        }

        public override void Dispose()
        {
            if (subscribed) Unsubscribe();
            subscribed = false;
            _enterFightLayerTime = -1000f;
            _pointOfSearch = null;
            LastSelectedDecision = null;
        }

        internal void InvalidatePosition() => _pointOfSearch = null;

        private CustomNavigationPoint SelectCommonCover(bool checkCurrent, bool assault)
        {
            if (checkCurrent && _pointOfSearch != null && !_pointOfSearch.IsSpotted &&
                _pointOfSearch.IsFreeById(_owner.Id)) return _pointOfSearch;
            var enemy = _owner.Memory.GoalEnemy;
            if (enemy == null) return null;
            var anchor = FollowerCombatCommon.GetEnemyAnchor(enemy);
            var center = Vector3.Lerp(_owner.Position, anchor, assault ? 0.5f : 0.35f);
            _pointOfSearch = common.GetClosestShootCover(center, _isCoridorZone ? 16f : 40f,
                maxDistanceFromBot: _isCoridorZone ? 16f : 40f, avoidBossFireLane: true);
            return _pointOfSearch;
        }

        public override AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision()
        {
            var enemy = _owner.Memory.GoalEnemy;
            if (!common.HasActiveCombatEnemy(enemy))
                return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, "knight.noEnemy");
            var revision = KnightCombatLayer.PositionRevision(_owner);
            if (revision != positionRevision)
            {
                positionRevision = revision;
                _pointOfSearch = null;
            }
            var selected = SelectVanillaDecision();
            // The shared medical provider already ran once in the owning objective.
            // A native timer cannot bypass that result or run the provider a second time.
            if (selected.Action == BotLogicDecision.heal || selected.Action == BotLogicDecision.healStimulators)
                return Hold("knight.medicalDeferred");

            if (TryAdaptCautiousDecision(enemy, selected, out var cautious)) return cautious;

            if (selected.Action == BotLogicDecision.runToCover || selected.Action == BotLogicDecision.attackMoving ||
                selected.Action == BotLogicDecision.goToCoverPoint)
            {
                var reason = "knight.cover." + selected.Reason;
                if (common.TryCommitSelectedCombatCoverWithAction(enemy, _pointOfSearch, reason, selected.Action))
                    return common.CreateMoveToCommittedCoverDecision(reason);
                _badCovers++;
                return common.TryGetImmediateShootDecision("knight.noCoverFire") ?? Hold("knight.noCover");
            }
            if (selected.Action == BotLogicDecision.runToEnemy || selected.Action == BotLogicDecision.goToEnemy)
            {
                // The shared search action follows permitted memory rather than an unseen
                // player's live transform. Visible assaults retain Knight's run/walk choice.
                if (!enemy.IsVisible)
                    return common.CreateMemoryOnlyEnemySearchDecision(enemy, "knight.assaultSearch");
            }
            return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(
                selected.Action, "knight.native." + selected.Reason, selected.Data);
        }

        private AICoreActionResult<BotLogicDecision, CoreActionResultParams> Hold(string reason)
        {
            RetryUntil = Time.time + 1f;
            common.HoldFor(1f);
            return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, reason);
        }
    }
}
