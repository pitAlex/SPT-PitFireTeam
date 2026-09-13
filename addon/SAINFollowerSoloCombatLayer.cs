using EFT;
using SAIN.Extensions;
using SAIN.Layers.Combat.Solo.Cover;
using SAIN.Models.Enums;
using SAIN.Preset.Shared.Enums;

using SAIN.Layers;
namespace pitTeam.SAINAddon;

// Replica of SAIN 4.5.1 CombatSoloLayer, Copyright Solarint (MIT; SAIN-LICENSE.txt).
// Intentional differences: addon identity/registration, ownership gate, internal-action
// resolution, and releasing our activation state on handoff. No follower command policy.
public class SAINFollowerSoloCombatLayer : SAINLayer
{
    public const int LayerPriority = 74;
    public const string Name = "pitTeam.SAIN.SoloCombat";
    public SAINFollowerSoloCombatLayer(BotOwner bot, int priority)
        : base(bot, priority, Name, ESAINLayer.Combat)
    {
        SAINFollowerRuntime.RegisterSoloLayer(bot, this);
    }
    public override Action GetNextAction()
    {
        _lastSelfDecision = _currentSelfDecision;
        _lastDecision = _currentDecision;

        if (_doSurgeryAction)
        {
            _doSurgeryAction = false;
            return new Action(SAINActionTypes.Get("Solo.Cover.DoSurgeryAction"), $"Surgery");
        }

        switch (_lastDecision)
        {
            case ECombatDecision.MoveToEngage:
                return new Action(SAINActionTypes.Get("Solo.MoveToEngageAction"), $"{_lastDecision}");

            case ECombatDecision.MeleeAttack:
                return new Action(SAINActionTypes.Get("Solo.MeleeAttackAction"), $"{_lastDecision}");

            case ECombatDecision.FightZombies:
                return new Action(SAINActionTypes.Get("Solo.FightZombiesAction"), $"{_lastDecision}");

            case ECombatDecision.RushEnemy:
                return new Action(SAINActionTypes.Get("Solo.RushEnemyAction"), $"{_lastDecision}");

            case ECombatDecision.ThrowGrenade:
                return new Action(SAINActionTypes.Get("Solo.ThrowGrenadeAction"), $"{_lastDecision}");

            case ECombatDecision.ShiftCover:
                return new Action(SAINActionTypes.Get("Solo.Cover.ShiftCoverAction"), $"{_lastDecision}");

            case ECombatDecision.SeekCover:
            case ECombatDecision.Retreat:
                string label;
                if (_lastSelfDecision != ESelfActionType.None)
                {
                    label = $"{_lastDecision} + {_lastSelfDecision}";
                }
                else
                {
                    label = $"{_lastDecision}";
                }

                return new Action(SAINActionTypes.Get("Solo.Cover.SeekCoverAction"), label);

            case ECombatDecision.ShootDistantEnemy:
            case ECombatDecision.StandAndShoot:
                return new Action(SAINActionTypes.Get("Solo.StandAndShootAction"), $"{_lastDecision}");

            case ECombatDecision.Search:
                return new Action(SAINActionTypes.Get("Solo.SearchAction"), $"{_lastDecision}");

            case ECombatDecision.Freeze:
                return new Action(SAINActionTypes.Get("Solo.FreezeAction"), $"{_lastDecision}");

            default:
                return new Action(SAINActionTypes.Get("Solo.StandAndShootAction"), $"DEFAULT! {_lastDecision}");
        }
    }

    public override bool IsActive()
    {
        if (!BotOwner.IsBotActive() || !pitFireTeam.UseSainFollowerCombat(BotOwner))
        {
            CheckActiveChanged(false);
            return false;
        }

        bool active = GetBotComponent() && _currentDecision != ECombatDecision.None;
        CheckActiveChanged(active);
        return active;
    }

    public override bool IsCurrentActionEnding()
    {
        if (base.IsCurrentActionEnding())
        {
            return true;
        }

        // this is dumb im sorry
        if (!_doSurgeryAction && _currentSelfDecision == ESelfActionType.Surgery && Bot.Cover.CoverInUse != null)
        {
            _doSurgeryAction = true;
            return true;
        }

        if (_lastSelfDecision == ESelfActionType.Surgery && _currentSelfDecision != ESelfActionType.Surgery)
        {
            return true;
        }
        return _currentDecision != _lastDecision;
    }

    private bool _doSurgeryAction;

    private ECombatDecision _lastDecision = ECombatDecision.None;
    private ESelfActionType _lastSelfDecision = ESelfActionType.None;
    public ECombatDecision _currentDecision
    {
        get { return Bot.Decision.CurrentCombatDecision; }
    }

    public ESelfActionType _currentSelfDecision
    {
        get { return Bot.Decision.CurrentSelfDecision; }
    }

    public override void Stop()
    {
        _doSurgeryAction = false;
        CheckActiveChanged(false);
        base.Stop();
    }
}
