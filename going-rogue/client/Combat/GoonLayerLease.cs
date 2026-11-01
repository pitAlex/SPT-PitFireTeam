using System;
using System.Collections.Generic;
using System.Linq;
using EFT;

namespace pitTeam.GoingRogue.Combat;

// Reversible bot-local suspension. Restoring an initialized layer must not call
// Activate() again and duplicate its native event subscriptions.
internal sealed class GoonLayerLease : IDisposable
{
    private readonly BotOwner owner;
    private readonly List<AICoreLayer<BotLogicDecision>> suspended = new();
    internal BaseBrain Brain { get; }
    internal GoonLayerLease(BotOwner bot) { owner = bot; Brain = bot.Brain.BaseBrain; }
    internal void Refresh()
    {
        foreach (var layer in Brain._activeLayers.ToArray())
        {
            if (!IsConflict(layer)) continue;
            if (!suspended.Contains(layer)) suspended.Add(layer);
            owner.Brain.Agent.Deactivate(layer.Name());
            Brain._activeLayers.Remove(layer);
        }
    }
    private static bool IsConflict(AICoreLayer<BotLogicDecision> layer)
        => layer is BossKnightFightLayer || layer is BigPipeFightLayer || layer is BirdEyeFightLayer || layer is BirdEyeHoldLayer ||
           layer is AssaultBuildingLayer || layer is ControlBuildingLayer ||
           layer is AssaultHaveEnemyLayer || layer is WarnPlayerLayer || layer is PeacefulRequestLayer ||
           layer.Name() == "SAIN : Combat Layer" || layer.Name() == "SAIN : Squad Layer";
    public void Dispose()
    {
        if (owner != null && !owner.IsDead && ReferenceEquals(owner.Brain?.BaseBrain, Brain))
        {
            foreach (var layer in suspended)
            {
                if (!Brain._layers.Values.Contains(layer) || Brain._activeLayers.Contains(layer)) continue;
                var index = Brain._activeLayers.FindIndex(other => other.Priority < layer.Priority);
                if (index < 0) Brain._activeLayers.Add(layer);
                else Brain._activeLayers.Insert(index, layer);
            }
        }
        suspended.Clear();
    }
}
