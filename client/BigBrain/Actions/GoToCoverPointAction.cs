using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Simple non-combat cover movement action. The caller provides the target cover point in action
    /// data, and this action drives vanilla movement toward that point.
    /// </summary>
    internal class GoToCoverPointAction : CustomLogic
    {
        private readonly GoToCoverPoint baseLogic;
        private CustomNavigationPoint? lastPoint;
        private MoveToCoverActionResultData? cachedCoverData;

        /// <summary>
        /// Payload for non-combat cover movement. Carries the exact cover destination chosen by the
        /// layer so the action does not perform cover selection itself.
        /// </summary>
        internal sealed class GoToCoverPointActionData : CustomLayer.ActionData
        {
            public CustomNavigationPoint? Point { get; }

            public GoToCoverPointActionData(CustomNavigationPoint? point)
            {
                Point = point;
            }
        }

        public GoToCoverPointAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GoToCoverPoint(botOwner);
        }

        public override void Start()
        {
            lastPoint = null;
            cachedCoverData = null;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            CustomNavigationPoint? point = null;
            if (data is GoToCoverPointActionData goToCoverData && goToCoverData.Point != null)
            {
                point = goToCoverData.Point;
            }

            if (!ReferenceEquals(point, lastPoint))
            {
                lastPoint = point;
                cachedCoverData = point != null ? new MoveToCoverActionResultData(point) : null;
            }

            baseLogic.UpdateNodeByBrain(cachedCoverData);
        }
    }
}
