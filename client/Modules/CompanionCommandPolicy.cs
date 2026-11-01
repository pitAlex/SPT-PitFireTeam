using System;

namespace pitTeam.Modules
{
    // Optional brains choose willingness; Core owns targets, admission and execution.
    public sealed class CompanionCommandPolicy
    {
        private readonly Func<float, float, float, float> pushAcceptance;
        private readonly Func<string, float> combatPowerMultiplier;
        public bool AlwaysAcceptCombatPoint { get; }
        public bool AllowsPush => pushAcceptance != null;
        public float? OnYourOwnAggression { get; }

        public CompanionCommandPolicy(bool alwaysAcceptCombatPoint,
            Func<float, float, float, float> pushAcceptance)
            : this(alwaysAcceptCombatPoint, pushAcceptance, null) { }

        public CompanionCommandPolicy(bool alwaysAcceptCombatPoint,
            Func<float, float, float, float> pushAcceptance, Func<string, float> combatPowerMultiplier)
            : this(alwaysAcceptCombatPoint, pushAcceptance, combatPowerMultiplier, null) { }

        public CompanionCommandPolicy(bool alwaysAcceptCombatPoint,
            Func<float, float, float, float> pushAcceptance, Func<string, float> combatPowerMultiplier,
            float? onYourOwnAggression)
        {
            AlwaysAcceptCombatPoint = alwaysAcceptCombatPoint;
            this.pushAcceptance = pushAcceptance;
            this.combatPowerMultiplier = combatPowerMultiplier;
            OnYourOwnAggression = onYourOwnAggression.HasValue &&
                !float.IsNaN(onYourOwnAggression.Value) && !float.IsInfinity(onYourOwnAggression.Value)
                ? Math.Max(0f, Math.Min(100f, onYourOwnAggression.Value)) : (float?)null;
        }

        public float RelativeCombatPower(string enemyRole)
        {
            float multiplier = combatPowerMultiplier?.Invoke(enemyRole) ?? 1f;
            return float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0f ? 1f : multiplier;
        }

        public float PushAcceptanceChance(float ownEquipment, float enemyEquipment, float threat)
        {
            float chance = pushAcceptance?.Invoke(ownEquipment, enemyEquipment, threat) ?? 0f;
            return float.IsNaN(chance) || float.IsInfinity(chance) ? 0f : Math.Max(0f, Math.Min(1f, chance));
        }
    }
}
