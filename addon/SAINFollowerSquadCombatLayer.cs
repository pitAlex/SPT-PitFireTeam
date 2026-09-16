using EFT;
using SAIN.Components;
using SAIN.Extensions;
using SAIN.Layers.Combat.Solo;
using SAIN.Models.Enums;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.Decision;

using SAIN.Layers;
namespace pitTeam.SAINAddon;

// Replica of SAIN 4.5.1 CombatSquadLayer, Copyright Solarint (MIT; SAIN-LICENSE.txt).
// Player-leader substitutions are confined to the squad provider and leader-dependent actions.
public class SAINFollowerSquadCombatLayer : SAINLayer
{
    public const int LayerPriority = 75;
    public const string Name = "pitTeam.SAIN.SquadCombat";
    public SAINFollowerSquadCombatLayer(BotOwner bot, int priority)
        : base(bot, priority, Name, ESAINLayer.Squad)
    {
        SAINFollowerRuntime.RegisterSquadLayer(bot, this);
    }
    private bool lingerAction;

    public override Action GetNextAction()
    {
        Action next = SelectAction();
        SAINFollowerRuntime.GetRecorder(BotOwner)?.Selected(Name, next.Type, next.Reason);
        return next;
    }

    private Action SelectAction()
    {
        // BEGIN addon post-combat handoff
        lingerAction = SAINFollowerRuntime.GetCombatPhase(BotOwner) != SAINFollowerCombatPhase.Combat;
        if (lingerAction) return new Action(typeof(SAINFollowerLingerAction), "linger");
        // END addon post-combat handoff
        LastActionDecision = Bot.Decision.CurrentSquadDecision;
        switch (LastActionDecision)
        {
            case ESquadDecision.Regroup:
                return new Action(typeof(SAINFollowerSquadRegroupAction), $"{LastActionDecision}");

            case ESquadDecision.Suppress:
                return new Action(SAINActionTypes.Get("Squad.SuppressAction"), $"{LastActionDecision}");

            case ESquadDecision.Search:
                return new Action(SAINActionTypes.Get("Solo.SearchAction"), $"{LastActionDecision}");

            case ESquadDecision.GroupSearch:
                if (Bot.Squad.IAmLeader)
                {
                    return new Action(SAINActionTypes.Get("Solo.SearchAction"), $"{LastActionDecision} : Lead Search Party");
                }
                return new Action(typeof(SAINFollowerFollowSearchPartyAction), $"{LastActionDecision} : Follow Squad Leader");

            case ESquadDecision.Help:
                return new Action(SAINActionTypes.Get("Solo.SearchAction"), $"{LastActionDecision}");

            case ESquadDecision.PushSuppressedEnemy:
                return new Action(SAINActionTypes.Get("Solo.RushEnemyAction"), $"{LastActionDecision}");

            default:
                return new Action(typeof(SAINFollowerSquadRegroupAction), $"DEFAULT!");
        }
    }

    public override bool IsActive()
    {
        if (!BotOwner.IsBotActive() || !pitFireTeam.UseSainFollowerCombat(BotOwner))
        {
            CheckActiveChanged(false);
            return false;
        }

        if (GetBotComponent())
        {
            // BEGIN addon post-combat handoff
            if (SAINFollowerRuntime.GetCombatPhase(BotOwner) != SAINFollowerCombatPhase.Combat)
            {
                CheckActiveChanged(false); // Solo owns the shared linger window.
                return false;
            }
            // END addon post-combat handoff
            BotComponent bot = Bot;
            if (bot != null && bot.BotActive)
            {
                SAINDecisionClass decisions = bot.Decision;
                if (
                    decisions.CurrentSelfDecision == ESelfActionType.None
                    && decisions.CurrentCombatDecision != ECombatDecision.DogFight
                    && decisions.CurrentSquadDecision != ESquadDecision.None
                )
                {
                    CheckActiveChanged(true);
                    return true;
                }
            }
        }
        CheckActiveChanged(false);
        return false;
    }

    public override bool IsCurrentActionEnding()
    {
        bool ending = ShouldEndAction();
        if (ending) SAINFollowerRuntime.GetRecorder(BotOwner)?.Ended(Name, "decisionOrHandoff");
        return ending;
    }

    private bool ShouldEndAction()
    {
        // BEGIN addon post-combat handoff
        if (SAINFollowerRuntime.GetCombatPhase(BotOwner) != SAINFollowerCombatPhase.Combat)
        {
            base.IsCurrentActionEnding(); // Drain decision events without restarting linger.
            return !lingerAction;
        }
        if (lingerAction) return true;
        // END addon post-combat handoff
        // BEGIN addon regroup ending
        if (LastActionDecision == ESquadDecision.Regroup &&
            Bot.Decision.CurrentSquadDecision == ESquadDecision.Regroup &&
            SAINFollowerRuntime.GetRegroup(BotOwner)?.Active != true)
        {
            // Observe can complete between native publications. Keep the completed action
            // quiet until the publisher replaces Regroup instead of reselecting it each frame.
            base.IsCurrentActionEnding();
            return false;
        }
        // END addon regroup ending
        if (base.IsCurrentActionEnding())
        {
            return true;
        }
        BotComponent bot = Bot;
        if (bot != null && bot.BotActive && bot.Decision.CurrentSquadDecision != LastActionDecision)
        {
            return true;
        }
        return false;
    }

    private ESquadDecision LastActionDecision = ESquadDecision.None;

    public override void Stop()
    {
        SAINFollowerRuntime.GetRecorder(BotOwner)?.Ended(Name, "layerStopped");
        lingerAction = false;
        CheckActiveChanged(false);
        base.Stop();
    }
}
