param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerBloodLossRecovery.cs')
$fixture = @'
namespace EFT {
    public enum EBodyPart { Head, Chest, LeftArm, RightArm, Common }
    public enum EDamageType { Bullet, LightBleeding, HeavyBleeding, Medicine }
}
namespace EFT.Ballistics { public struct DamageInfo { public EDamageType DamageType; } }
namespace EFT.HealthSystem {
    public interface IHealthEffect {}
    public interface IBleeding : IHealthEffect {}
    public sealed class Bleed : IBleeding {}
    public sealed class Fracture : IHealthEffect {}
    public static class DamageHelper { public static DamageInfo MedKitUse = new() { DamageType = EDamageType.Medicine }; }
    public static class HealthHelper { public static EBodyPart[] RealBodyParts = { EBodyPart.Head, EBodyPart.Chest, EBodyPart.LeftArm, EBodyPart.RightArm }; }
    public sealed class ActiveHealthController {
        public bool IsAlive = true;
        public int Bleeds;
        public event Action<EBodyPart, float, DamageInfo> HealthChangedEvent;
        public event Action<IHealthEffect> HealerDoneEvent;
        private readonly float[] current = { 40, 40, 40, 40 };
        public (float Current, float Maximum) GetBodyPartHealth(EBodyPart part, bool _) => (current[(int)part], 40);
        public bool IsBodyPartDestroyed(EBodyPart part) => current[(int)part] == 0;
        public T FindExistingEffect<T>(EBodyPart part) where T : class, IHealthEffect => Bleeds > 0 ? new Bleed() as T : null;
        public void ChangeHealth(EBodyPart part, float delta, DamageInfo damage) {
            int index = (int)part;
            float before = current[index];
            current[index] = Math.Clamp(before + delta, 0, 40);
            HealthChangedEvent?.Invoke(part, current[index] - before, damage);
        }
        public void Publish(IHealthEffect effect) => HealerDoneEvent?.Invoke(effect);
        public void Check(EBodyPart part, float expected, string label) {
            if (current[(int)part] != expected) throw new Exception(label);
        }
    }
}
public static class BloodLossChecks {
    public static void Run() {
        var health = new ActiveHealthController { Bleeds = 2 };
        using var recovery = new pitTeam.Utils.FollowerBloodLossRecovery(health);
        health.ChangeHealth(EBodyPart.Chest, -12, new() { DamageType = EDamageType.Bullet });
        foreach (EBodyPart part in HealthHelper.RealBodyParts) {
            health.ChangeHealth(part, -3, new() { DamageType = EDamageType.LightBleeding });
            health.ChangeHealth(part, -2, new() { DamageType = EDamageType.HeavyBleeding });
        }
        health.ChangeHealth(EBodyPart.Head, 2, DamageHelper.MedKitUse);
        health.ChangeHealth(EBodyPart.RightArm, -40, new() { DamageType = EDamageType.HeavyBleeding });
        health.Publish(new Fracture());
        health.Check(EBodyPart.Head, 37, "Unrelated effects do not restore bleed loss");
        health.Bleeds--;
        health.Publish(new Bleed());
        health.Check(EBodyPart.LeftArm, 35, "One remaining bleed defers group recovery");
        health.Bleeds--;
        health.Publish(new Bleed());
        health.Check(EBodyPart.Head, 40, "Earlier healing consumes part of the outstanding loss");
        health.Check(EBodyPart.Chest, 28, "Direct hit damage is preserved");
        health.Check(EBodyPart.LeftArm, 40, "Other living parts recover bleeding HP");
        health.Check(EBodyPart.RightArm, 0, "A destroyed part still requires surgery");
        health.ChangeHealth(EBodyPart.Chest, -2, new() { DamageType = EDamageType.Bullet });
        health.Publish(new Bleed());
        health.Check(EBodyPart.Chest, 26, "A prior treatment cannot pay out a second time");
        recovery.Dispose();
        health.ChangeHealth(EBodyPart.Head, -3, new() { DamageType = EDamageType.LightBleeding });
        health.Publish(new Bleed());
        health.Check(EBodyPart.Head, 37, "Dismissal releases both event subscriptions");
    }
}
'@
Add-Type -TypeDefinition ($source + "`n" + $fixture) -Language CSharp
[BloodLossChecks]::Run()
Write-Output 'Passed 8 follower blood-loss recovery checks using the production event handlers.'
