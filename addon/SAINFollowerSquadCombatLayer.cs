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
    public override Action GetNextAction()
    {
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
        CheckActiveChanged(false);
        base.Stop();
    }
}
