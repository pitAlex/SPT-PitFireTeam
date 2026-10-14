using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Modules;

namespace pitTeam.BigBrain.Actions
{
    internal sealed class FriendlyEncounterAction : CustomLogic
    {
        private FriendlyEncounterInteraction interaction;
        private bool failed;
        public FriendlyEncounterAction(BotOwner botOwner) : base(botOwner) { }
        public override void Update(CustomLayer.ActionData data)
        {
            if (failed) return;
            try
            {
                if (AllegiancePmcFriendship.TryGetInteraction(BotOwner, out var current, out var greeting))
                {
                    interaction = current;
                    interaction.Update(BotOwner, AllegiancePmcFriendship.EncounterPlayer, greeting);
                }
            }
            catch (System.Exception ex)
            {
                failed = true;
                interaction?.Cancel(BotOwner);
                Logger.LogError("Friendly interaction action disabled after a runtime failure");
                Logger.LogError(ex);
            }
        }
    }
}
