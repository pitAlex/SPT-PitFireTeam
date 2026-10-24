using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Modules;
using SAIN.Layers;
using UnityEngine;

namespace pitTeam.SAINAddon;

// Presentation only: cancel the previous SAIN path and burst, retain a horizontal
// look, then make one gentle lateral scan. Combat selection stays in the layers.
public sealed class SAINFollowerLingerAction(BotOwner bot) : BotAction(bot, nameof(SAINFollowerLingerAction)), IBotAction
{
    private bool active;
    private float scanAt;
    private Vector3 entryDirection;
    private Vector3 scanDirection;

    public override void Start()
    {
        base.Start();
        active = true;
        Bot.Mover.Stop();
        Bot.Shoot.EndShoot();
        entryDirection = BotOwner.LookDirection;
        entryDirection.y = 0f;
        entryDirection = entryDirection.sqrMagnitude > 0.01f ? entryDirection.normalized : Vector3.forward;
        scanDirection = Quaternion.Euler(0f, UnityEngine.Random.value < 0.5f ? -50f : 50f, 0f) * entryDirection;
        scanAt = Time.time + 0.5f;
    }

    public override void Update(CustomLayer.ActionData data)
    {
        if (CanLinger()) Bot.Shoot.EndShoot();
    }

    public override void OnSteeringTicked()
    {
        if (!CanLinger()) return;
        Bot.Shoot.EndShoot();
        Vector3 direction = Time.time < scanAt ? entryDirection : scanDirection;
        Bot.Steering.LookToPoint(Bot.Transform.WeaponRoot + direction * 20f);
    }

    private bool CanLinger() => active && pitFireTeam.UseSainFollowerCombat(BotOwner) &&
        !SAINFollowerCombatHandoff.HasLiveEnemy(Bot) && !SainAddonBridge.IsUsingMedical(BotOwner);

    public override void Stop()
    {
        active = false;
        base.Stop();
    }
}
