using DrakiaXYZ.BigBrain.Brains;
using EFT;
using SAIN.Layers;

namespace pitTeam.SAINAddon;

internal sealed class SAINFollowerRelocationAction(BotOwner bot) : BotAction(bot, nameof(SAINFollowerRelocationAction)), IBotAction
{
    private SAINFollowerRelocationObjective? Objective => SAINFollowerRuntime.GetRelocation(BotOwner);
    public override void Start() { base.Start(); Objective?.Pause(); }
    public override void Update(CustomLayer.ActionData data) => Objective?.Tick();
    public override void OnSteeringTicked()
    {
        if (Objective?.Active != true || Objective.OwnsAction != true) return;
        var enemy = Bot.GoalEnemy;
        if (Shoot.ShootAnyVisibleEnemies(enemy)) { Bot.Steering.SteerByPriority(enemy); return; }
        if (Bot.Mover.Moving && Bot.Steering.LookToMovingDirection()) return;
        Bot.Steering.SteerByPriority(enemy);
    }
    public override void Stop() { Objective?.Pause(); base.Stop(); }
}
