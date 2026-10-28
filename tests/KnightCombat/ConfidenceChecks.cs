using EFT;
using pitTeam.BigBrain;
using pitTeam.GoingRogue;
using pitTeam.GoingRogue.Combat;
using UnityEngine;

internal static class ConfidenceChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(var(role,factor) in new[]{("pmcUSEC",1.5f),("pmcBEAR",1.5f),("assault",2f),("assaultGroup",2f),
            ("cursedAssault",2f),("marksman",2f),("bossSanitar",1f),("followerBigPipe",1f),("exUsec",1f),
            ("pmcBot",1f),("arenaFighter",1f),("unknown",1f),(null,1f)})
            check(GoonCombatConfidence.PowerMultiplier(role)==factor,"explicit ordinary enemy role advantage: "+role);
        check(!GoonCombatConfidence.IsLowThreat(100,250,"pmcUSEC",1,1,0) &&
            GoonCombatConfidence.IsLowThreat(100,250,"assault",1,1,0),"Scavs receive a larger boss-relative advantage while equipment still matters");
        check(GoonCombatConfidence.IsLowThreat(100,100,"pmcUSEC",2,1,0) &&
            !GoonCombatConfidence.IsLowThreat(100,100,"pmcUSEC",3,1,0),"ordinary pair can be approached but existing two-contact bound remains");
        foreach(float invalid in new[]{0f,float.NaN,float.PositiveInfinity})
            check(!GoonCombatConfidence.IsLowThreat(invalid,100,"assault",1,1,0) &&
                !GoonCombatConfidence.IsLowThreat(100,invalid,"assault",1,1,0),"missing/invalid equipment fails autonomous admission closed");
        foreach(var actor in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe})
        foreach(var target in new[]{WildSpawnType.pmcUSEC,WildSpawnType.pmcBEAR,WildSpawnType.assault,WildSpawnType.marksman})
        {
            Time.time=100;var bot=new BotOwner();bot.Profile.Info.Settings.Role=actor;
            var enemy=bot.Memory.GoalEnemy;enemy.Person.Profile.Info.Settings.Role=target;enemy.Distance=40;
            var common=new FollowerCombatCommon();
            check(GoonCombatThreat.IsLowThreat(bot,enemy,common),"boss-versus-ordinary assessment does not require legacy AttackImmediately: "+actor+"/"+target);
            check(!common.Calls.Contains("lowThreat"),"ordinary matchup uses shared equipment/threat calculation, not cached legacy attack flag");
            var knight=new KnightVanillaCombat(bot,76);knight.Begin(common);
            check(!knight.TryAdaptCautiousDecision(enemy,new(BotLogicDecision.runToEnemy,"native"),out _),"native approach survives boss confidence when safety permits");
            var pipe=new BigPipeVanillaCombat(bot);pipe.Begin(common);
            check(pipe.GetDecision().Reason=="bigPipe.pressureSearch.walk","Big Pipe ordinary pressure search can follow positive boss confidence");
            foreach(string veto in new[]{"unknown","memoryOnly","weapon","largeGroup","noGear","dead"})
            {
                enemy.Known=veto!="unknown";enemy.MemoryOnly=veto=="memoryOnly";
                common.BlockWeaponThreat=veto=="weapon";bot.KnownEnemies=veto=="largeGroup"?3:1;
                bot.AIData.PowerOfEquipment=veto=="noGear"?0:100;enemy.Person.HealthController.IsAlive=veto!="dead";
                check(!GoonCombatThreat.IsLowThreat(bot,enemy,common),"boss confidence cannot bypass autonomous admission: "+veto);
            }
            enemy.Known=true;enemy.MemoryOnly=false;common.BlockWeaponThreat=false;bot.KnownEnemies=1;
            bot.AIData.PowerOfEquipment=100;enemy.Person.HealthController.IsAlive=true;common.RecoveryPressure=true;common.RecoveryMove=true;
            check(knight.TryAdaptCautiousDecision(enemy,new(BotLogicDecision.runToEnemy,"native"),out var recovery) && recovery.Reason=="medicalRecoveryCover","Knight incoming pressure retains recovery priority");
            check(pipe.GetDecision().Reason=="medicalRecoveryCover","Big Pipe incoming pressure retains recovery priority");
        }
        {
            var bot=new BotOwner();var common=new FollowerCombatCommon();bot.Memory.GoalEnemy.Person.Profile.Info.Settings.Role=WildSpawnType.bossSanitar;
            check(!GoonCombatThreat.IsLowThreat(bot,bot.Memory.GoalEnemy,common) && common.Calls.Contains("lowThreat"),"other bosses retain the previous threat contract");
        }
    }
}
