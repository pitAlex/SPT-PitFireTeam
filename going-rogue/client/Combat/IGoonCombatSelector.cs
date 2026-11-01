using System;
using EFT;
using pitTeam.BigBrain;

namespace pitTeam.GoingRogue.Combat
{
    internal interface IGoonCombatSelector : IDisposable
    {
        BotLogicDecision? LastSelectedDecision { get; set; }
        float RetryUntil { get; }
        void Begin(FollowerCombatCommon common);
        void InvalidatePosition();
        AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision();
        AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> current);
    }
}
