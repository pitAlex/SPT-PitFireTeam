using EFT;
using HarmonyLib;
using pitTeam.BigBrain;
using pitTeam.Components;
using pitTeam.Modules;

namespace pitTeam.GoingRogue.Combat;

public sealed class ExUsecCombatLayer : GoonCombatLayer
{
    public ExUsecCombatLayer(BotOwner owner, int priority)
        : base(owner, priority, "exUsec", "ExUsec", "ExUsec") { }
    internal override FollowerCombatLogicBase CreateLogic(BotOwner owner) => new ExUsecCombatLogic(owner);
}

[HarmonyPatch(typeof(BotFollowerPlayer), nameof(BotFollowerPlayer.Init))]
internal static class ExUsecFollowerTacticPatch
{
    private static void Prefix(BotFollowerPlayer __instance)
    {
        // Fix the runtime tactic before proficiency capture, retaining numeric preferences.
        if (GameplayModeAddons.Current == GameplayMode.Allegiance &&
            __instance.GetBoss()?.realPlayer?.IsYourPlayer == true &&
            __instance.GetBot()?.Profile?.Info?.Settings?.Role == WildSpawnType.exUsec)
            __instance.SetCombatTacticFromString("Rifleman");
    }
}

internal sealed class ExUsecCombatLogic : GoonMemberCombatLogic
{
    internal ExUsecCombatLogic(BotOwner owner) : base(owner) { }
    protected override FollowerCombatObjectiveBase CreateDefaultObjective(BotOwner owner, FollowerCombatCommon common)
        => new ExUsecCombatObjective(owner, common);
    public override void StartDecision() { currentObjective = CombatObjectiveKind.Default; defaultObjective.StartDecision(); }
}
