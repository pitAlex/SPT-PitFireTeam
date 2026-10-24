// Native animation and BigBrain hosts for the production social layer/action.
using System;
using EFT;
using EFT.Interactive;
namespace pitTeam.Modules { internal static class TeammateBackpackInspection { __HEAL_WORK_GATE__ } }
namespace EFT.AnimatedInteractionsSubsystem
{
    public class AnimatedInteractions
    {
        public bool IsScheduleDenied; public bool CanInteract=true;
        public Controller.AnimatedInteractionsController _controller=new();
        public bool IsInteractionPlaying {
            get=>_controller._model._currentInteraction!=default;
            set=>_controller._model._currentInteraction=value ? EInteraction.FriendlyGesture : default;
        }
    }
}
namespace EFT.AnimatedInteractionsSubsystem.Controller { public class AnimatedInteractionsController {public Models.AnimatedInteractionsModel _model=new();} }
namespace EFT.AnimatedInteractionsSubsystem.Models { public class AnimatedInteractionsModel { public EInteraction _currentInteraction; } }
namespace DrakiaXYZ.BigBrain.Brains
{
    public abstract class CustomLayer
    {
        public BotOwner BotOwner; public int Priority;
        protected CustomLayer(BotOwner bot,int priority){BotOwner=bot;Priority=priority;}
        public abstract string GetName();public abstract bool IsActive();public abstract Action GetNextAction();public abstract bool IsCurrentActionEnding();
        public virtual void Start(){} public virtual void Stop(){}
        public class ActionData{}
        public class Action {public Type Type;public Action(Type type,string reason){Type=type;}}
    }
    public abstract class CustomLogic
    {
        public BotOwner BotOwner;protected CustomLogic(BotOwner bot){BotOwner=bot;}
        public abstract void Update(CustomLayer.ActionData data);
    }
}
