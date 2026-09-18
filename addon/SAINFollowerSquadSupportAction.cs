using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SAIN.Layers;

namespace pitTeam.SAINAddon;

internal sealed class SAINFollowerSquadSupportAction(BotOwner owner) : BotAction(owner, nameof(SAINFollowerSquadSupportAction)), IBotAction
{
    private SAINFollowerSquadSupportObjective? Objective => SAINFollowerRuntime.GetSquadSupport(BotOwner);
    public override void Start() { base.Start(); Objective?.Pause(); }
    public override void Update(CustomLayer.ActionData data) => Objective?.Tick();
    public override void OnSteeringTicked() => Objective?.Steer();
    public override void Stop() { Objective?.Pause(); base.Stop(); }
}
