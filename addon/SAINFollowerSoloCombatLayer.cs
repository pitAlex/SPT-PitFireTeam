using EFT;
using SAIN.Extensions;
using SAIN.Layers.Combat.Solo.Cover;
using SAIN.Models.Enums;
using SAIN.Preset.Shared.Enums;

using SAIN.Layers;
namespace pitTeam.SAINAddon;

// Replica of SAIN 4.5.1 CombatSoloLayer, Copyright Solarint (MIT; SAIN-LICENSE.txt).
// Intentional differences: addon identity/registration, ownership gate, internal-action
// resolution, post-combat linger, recorder hooks, and the bounded engagement attempt.
public class SAINFollowerSoloCombatLayer : SAINLayer
{
    public const int LayerPriority = 74;
    public const string Name = "pitTeam.SAIN.SoloCombat";
    public SAINFollowerSoloCombatLayer(BotOwner bot, int priority)
        : base(bot, priority, Name, ESAINLayer.Combat)
    {
        SAINFollowerRuntime.RegisterSoloLayer(bot, this);
    }
    private bool lingerAction;
    private bool preparationAction;
    private bool pushHoldAction;
    private bool relocationAction;
    private bool UseRelocation => _currentDecision == ECombatDecision.MoveToEngage &&
        _currentSelfDecision == ESelfActionType.None && SAINFollowerRuntime.GetRelocation(BotOwner)?.OwnsAction == true;
    private bool UsePushHold => _currentSelfDecision == ESelfActionType.None &&
        (_currentDecision == ECombatDecision.StandAndShoot || _currentDecision == ECombatDecision.ShootDistantEnemy) &&
        (SAINFollowerRuntime.GetPush(BotOwner)?.HoldsPosition == true ||
         SAINFollowerRuntime.GetMarksman(BotOwner)?.Preparing == true);

    public override Action GetNextAction()
    {
        Action next = SelectAction();
        SAINFollowerRuntime.GetRecorder(BotOwner)?.Selected(Name, next.Type, next.Reason);
        return next;
    }

    private Action SelectAction()
    {
        // BEGIN addon post-combat handoff
        SAINFollowerCombatPhase phase = SAINFollowerRuntime.GetCombatPhase(BotOwner);
        lingerAction = phase != SAINFollowerCombatPhase.Combat && phase != SAINFollowerCombatPhase.Ambush;
        if (lingerAction) return new Action(typeof(SAINFollowerLingerAction), "linger");
        preparationAction = phase == SAINFollowerCombatPhase.Ambush;
        if (preparationAction)
        {
            _doSurgeryAction = false;
            return new Action(typeof(SAINFollowerPreparationAction), $"heardPreparation + {_currentDecision}");
        }
        // END addon post-combat handoff
        _lastSelfDecision = _currentSelfDecision;
        _lastDecision = _currentDecision;

        if (_doSurgeryAction)
        {
            _doSurgeryAction = false;
            return new Action(SAINActionTypes.Get("Solo.Cover.DoSurgeryAction"), $"Surgery");
        }

        // BEGIN addon relocation
        relocationAction = UseRelocation;
        if (relocationAction) return new Action(typeof(SAINFollowerRelocationAction), "combatGesture");
        // END addon relocation
        // BEGIN addon push hold
        pushHoldAction = UsePushHold;
        if (pushHoldAction) return new Action(typeof(SAINFollowerPushHoldAction), SAINFollowerRuntime.GetMarksman(BotOwner)?.Preparing == true ? "marksmanWeaponPrepare" : SAINFollowerRuntime.GetPush(BotOwner)?.Phase == SAINPushPhase.Pressure ? "pushArrivalHold" : "pushPlanningHold");
        // END addon push hold
        switch (_lastDecision)
        {
            case ECombatDecision.MoveToEngage:
                return new Action(typeof(SAINFollowerMoveToEngageAction), $"{_lastDecision}");

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

        // BEGIN addon post-combat handoff
        if (GetBotComponent())
        {
            SAINFollowerCombatPhase phase = SAINFollowerRuntime.GetCombatPhase(BotOwner);
            if (phase != SAINFollowerCombatPhase.Combat && phase != SAINFollowerCombatPhase.Ambush)
            {
                _doSurgeryAction = false;
                CheckActiveChanged(phase == SAINFollowerCombatPhase.Linger);
                return phase == SAINFollowerCombatPhase.Linger;
            }
        }
        // END addon post-combat handoff
        bool active = GetBotComponent() && _currentDecision != ECombatDecision.None;
        CheckActiveChanged(active);
        return active;
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
        SAINFollowerCombatPhase phase = SAINFollowerRuntime.GetCombatPhase(BotOwner);
        if (phase is not (SAINFollowerCombatPhase.Combat or SAINFollowerCombatPhase.Ambush))
        {
            base.IsCurrentActionEnding(); // Drain decision events without restarting linger.
            return !lingerAction;
        }
        if (lingerAction || preparationAction != (phase == SAINFollowerCombatPhase.Ambush)) return true;
        if (preparationAction)
        {
            base.IsCurrentActionEnding(); // Drain publications; retain the local cover commitment.
            return false;
        }
        // END addon post-combat handoff
        // BEGIN addon relocation
        if (relocationAction != UseRelocation) return true;
        // END addon relocation
        // BEGIN addon push hold
        if (pushHoldAction != UsePushHold) return true;
        // END addon push hold
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
        SAINFollowerRuntime.GetRecorder(BotOwner)?.Ended(Name, "layerStopped");
        lingerAction = false;
        preparationAction = false;
        pushHoldAction = false;
        relocationAction = false;
        _doSurgeryAction = false;
        CheckActiveChanged(false);
        base.Stop();
    }
}
