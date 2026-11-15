using System;
using System.Collections.Generic;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;

namespace pitTeam.Utils
{
    internal sealed class FollowerBloodLossRecovery : IDisposable
    {
        private readonly ActiveHealthController health;
        private readonly Dictionary<EBodyPart, float> bloodLoss = new();

        internal FollowerBloodLossRecovery(ActiveHealthController health)
        {
            this.health = health;
            health.HealthChangedEvent += OnHealthChanged;
            health.HealerDoneEvent += OnTreatmentEffect;
        }

        public void Dispose()
        {
            health.HealthChangedEvent -= OnHealthChanged;
            health.HealerDoneEvent -= OnTreatmentEffect;
            bloodLoss.Clear();
        }

        private void OnHealthChanged(EBodyPart part, float change, DamageInfo damage)
        {
            bool isBleedingDamage = change < 0f &&
                (damage.DamageType == EDamageType.LightBleeding || damage.DamageType == EDamageType.HeavyBleeding);
            if (!bloodLoss.TryGetValue(part, out float lost) && !isBleedingDamage) return;

            var current = health.GetBodyPartHealth(part, false);
            float outstanding = isBleedingDamage ? lost - change : lost - Math.Max(0f, change);
            outstanding = Math.Min(outstanding, current.Maximum - current.Current);
            if (current.Current > 0f && outstanding > 0f)
                bloodLoss[part] = outstanding;
            else
                bloodLoss.Remove(part);
        }

        private void OnTreatmentEffect(IHealthEffect effect)
        {
            // EFT publishes the affected bleed here after native treatment and its item cost.
            // Wait for the last bleed so subsequent ticks cannot immediately undo the recovery.
            if (effect is not IBleeding || bloodLoss.Count == 0 || !health.IsAlive ||
                health.FindExistingEffect<IBleeding>(EBodyPart.Common) != null) return;

            foreach (EBodyPart part in HealthHelper.RealBodyParts)
            {
                if (!bloodLoss.TryGetValue(part, out float lost)) continue;
                bloodLoss.Remove(part);
                if (!health.IsBodyPartDestroyed(part))
                    health.ChangeHealth(part, lost, DamageHelper.MedKitUse);
            }
        }
    }
}
