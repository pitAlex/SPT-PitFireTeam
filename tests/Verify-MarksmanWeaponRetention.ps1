param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/BigBrain/FollowerCombatSniper.cs')
$names=@('ApplyMarksmanWeaponPolicy','ShouldUseCloseIntentSecondary','IsCloseIntentDecisionReason',
    'IsAutomaticSupportIntentReason','ShouldSwitchBackToPrimaryForSniperDecision','IsMarksmanPrimaryRangeDecision',
    'TrySwitchToPrimaryForSniperDecision','CanUseCloseIntentSecondary','CanUseAutomaticSupportForCloseThreat')
$methods=foreach($name in $names){
    $matches=[regex]::Matches($source,'(?ms)^        (?:public|private|internal)[^\r\n]*\b'+$name+'\(.*?^        \}')
    if($matches.Count -ne 1){throw "Expected one Marksman weapon method: $name"};$matches[0].Value
}
$fixture=@'
#nullable enable
using System;
using Decision=AICoreActionResult<BotLogicDecision,CoreActionResultParams>;
public enum EquipmentSlot {FirstPrimaryWeapon,SecondPrimaryWeapon,Holster}
public enum BotLogicDecision {holdPosition,goToPoint,shootFromPlace}
public class CoreActionResultParams {}
public struct AICoreActionResult<T,P>{public string Reason;public AICoreActionResult(string r){Reason=r;}}
public class EnemyInfo {public float Distance;public bool Alive=true;}
public class Selector {
    public EquipmentSlot LastEquipmentSlot=EquipmentSlot.SecondPrimaryWeapon;public bool IsChanging;public int Returns;
    public void ChangeToMain(){Returns++;LastEquipmentSlot=EquipmentSlot.FirstPrimaryWeapon;}
}
public class Weapons {public Selector Selector=new Selector();}
public class BotOwner {public Weapons WeaponManager=new Weapons();}
public class CombatDistanceConfiguration {public static CombatDistanceConfiguration Instance=new CombatDistanceConfiguration();public float GetCloseQuarterDistance()=>25;}
namespace pitTeam.Modules {public static class FollowerEnemyTracking {public static float Distance(EnemyInfo enemy)=>enemy.Distance;}}
public class Common {
    public bool AutomaticSupport=true,Retreat;
    public bool IsUsingAutomaticMarksmanSupportOverNonAutomaticPrimary()=>AutomaticSupport;
    public bool IsCommittedCoverRetreatingFromEnemy(EnemyInfo enemy)=>Retreat;
}
public static class FollowerCombatCommon {public static bool HasActiveCombatEnemy(BotOwner owner,EnemyInfo? enemy)=>enemy?.Alive==true;}
public static class FollowerCombatSuppressionObjective {public static bool IsAutomaticSupportIntentReason(string? reason)=>reason?.StartsWith("suppressAutomatic")==true;}
public class Policy {
    public BotOwner BotOwner=new BotOwner();public Common CombatCommon=new Common();
    const string CloseWeaponPrepareHoldReason="sniper.closeWeaponPrepare",FireSupportHoldReason="sniper.fireSupportHold",
        SupportPositionHoldReason="sniper.FireSupport.positionHold",NoActionHoldReason="sniper.noActionHold",PositionHoldReason="sniper.positionHold";
    public void Apply(EnemyInfo enemy,string reason)=>ApplyMarksmanWeaponPolicy(enemy,new Decision(reason));
    /* PRODUCTION */
}
public static class WeaponChecks {
    public static int Run(){
        int checks=0;Action<bool,string> check=(ok,why)=>{checks++;if(!ok)throw new Exception(why);};
        foreach(var slot in new[]{EquipmentSlot.SecondPrimaryWeapon,EquipmentSlot.Holster})
        foreach(float distance in new[]{24.9f,25f,25.1f,60f})
        foreach(string reason in new[]{"sniper.FireSupport.position.birdEye.native.goToPoint","sniper.FireSupport.position.birdEye.knight.goToPoint","sniper.noActionHold"}){
            var policy=new Policy();policy.BotOwner.WeaponManager.Selector.LastEquipmentSlot=slot;
            policy.Apply(new EnemyInfo{Distance=distance},reason);
            check(policy.BotOwner.WeaponManager.Selector.Returns==(distance>25?1:0),"automatic secondary retains close danger, then yields to rifle: "+slot+"/"+distance+"/"+reason);
        }
        foreach(string reason in new[]{"sniper.closeSearch","sniper.closeAutoSuppress","sniper.closeWeaponPrepare","sniper.closeImmediateShoot"}){
            var policy=new Policy();policy.Apply(new EnemyInfo{Distance=60},reason);
            check(policy.BotOwner.WeaponManager.Selector.Returns==0,"explicit automatic preparation/movement remains single-owned: "+reason);
        }
        var changing=new Policy();changing.BotOwner.WeaponManager.Selector.IsChanging=true;
        changing.Apply(new EnemyInfo{Distance=60},"sniper.FireSupport.position.birdEye.knight.goToPoint");
        check(changing.BotOwner.WeaponManager.Selector.Returns==0,"no competing draw while selector is changing");
        var nonAutomatic=new Policy();nonAutomatic.CombatCommon.AutomaticSupport=false;
        nonAutomatic.Apply(new EnemyInfo{Distance=60},"sniper.FireSupport.position.birdEye.native.goToPoint");
        check(nonAutomatic.BotOwner.WeaponManager.Selector.Returns==0,"Marksman policy cannot claim an unrelated weapon switch");
        return checks;
    }
}
'@
Add-Type -TypeDefinition $fixture.Replace('/* PRODUCTION */',($methods -join "`n")) -Language CSharp
$count=[WeaponChecks]::Run()
"PASS: $count production Marksman secondary retention/primary-return checks; native selector is a controlled stand-in."
