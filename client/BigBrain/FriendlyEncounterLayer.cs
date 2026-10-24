using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Modules;

namespace pitTeam.BigBrain
{
    internal sealed class FriendlyEncounterLayer : CustomLayer
    {
        internal const int LayerPriority = 65;
        private FriendlyEncounterInteraction interaction;
        private bool failed;
        public FriendlyEncounterLayer(BotOwner botOwner, int priority) : base(botOwner, priority) { }
        public override string GetName() => "pitTeam.FriendlyEncounter";
        public override bool IsActive()
        {
            if (failed) return false;
            try
            {
                if (!AllegiancePmcFriendship.TryGetInteraction(BotOwner, out var current, out _))
                {
                    interaction?.End(BotOwner);
                    return false;
                }
                interaction = current;
                return interaction.CanWait(BotOwner, AllegiancePmcFriendship.EncounterPlayer);
            }
            catch (System.Exception ex)
            {
                Fail(ex);
                return false;
            }
        }
        public override void Start()
        {
            try { interaction?.Begin(BotOwner, AllegiancePmcFriendship.EncounterPlayer); }
            catch (System.Exception ex) { Fail(ex); }
        }
        public override void Stop() => interaction?.End(BotOwner);
        public override bool IsCurrentActionEnding() => interaction?.Waiting != true;
        public override Action GetNextAction() => new Action(typeof(Actions.FriendlyEncounterAction), "Wait for friendly interaction");
        private void Fail(System.Exception ex)
        {
            failed = true;
            interaction?.Cancel(BotOwner);
            Logger.LogError("Friendly interaction layer disabled after a runtime failure");
            Logger.LogError(ex);
        }
    }
}
