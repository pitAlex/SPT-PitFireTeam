using EFT;
using pitTeam.BigBrain;

namespace pitTeam.Modules;

// Per-follower passive core readers only. Never calls core decisions, target
// acquisition, movement, medical refresh or weapon switching.
public sealed class SainPushRiskBridge
{
    private readonly BotOwner owner;
    private readonly FollowerCombatCommon common;
    public SainPushRiskBridge(BotOwner owner) { this.owner = owner; common = new FollowerCombatCommon(owner); }
    public readonly struct Inputs(float ratio, float role, int weapon, bool medical, bool ready, bool restricted, bool shotgun)
    {
        public readonly float EquipmentRatio = ratio, RoleMultiplier = role;
        public readonly int WeaponPolicy = weapon;
        public readonly bool Medical = medical, WeaponReady = ready, MagazineRestricted = restricted, CloseShotgun = shotgun;
    }
    public Inputs Read(EnemyInfo enemy)
    {
        var weapon = owner.WeaponManager?.ShootController?.Item ?? owner.WeaponManager?.CurrentWeapon;
        var magazine = weapon?.GetCurrentMagazine();
        bool closeShotgun = false;
        bool restricted = magazine?.Cartridges != null && FollowerPushRiskPolicy.RestrictMagazine(
            magazine.Cartridges.Count, magazine.MaxCount, FollowerCombatCommon.IsAutomaticWeapon(weapon),
            FollowerCombatCommon.IsShotgunWeapon(weapon), FollowerCombatCommon.IsPrecisionRifleWeapon(weapon), out closeShotgun);
        // SAIN owns switching. An available secondary does not make the current gun ready.
        bool ready = FollowerCombatCommon.IsPushReadyLongGunActive(owner) &&
            owner.WeaponManager?.IsWeaponReady == true && owner.WeaponManager.Selector?.IsChanging != true;
        return new Inputs(FollowerPushRiskPolicy.EquipmentRatio(owner.AIData?.PowerOfEquipment ?? 0f,
            enemy?.Person?.AIData?.PowerOfEquipment ?? 0f),
            FollowerCombatRiflemanEngagement.GetCombatRoleThreatMultiplier(enemy?.Person?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault),
            (int)common.GetAutoPushWeaponThreatPolicy(enemy, activeWeaponOnly: true),
            common.HasReportedHealWorkForPush() || common.IsFollowerCriticallyWounded(), ready, restricted, closeShotgun);
    }
}
