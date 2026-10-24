using System.Runtime.CompilerServices;
using EFT;
using pitTeam.Components;
using pitTeam.SAINAddon;
using SAIN.Components;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes.Info;
using SAIN.SAINComponent.Classes.WeaponFunction;

namespace SAIN.SAINComponent.Classes.Info { public class BotWeaponInfoClass {} }
namespace SAIN.SAINComponent.Classes.WeaponFunction {
    public class Firemode {
        public int Calls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void CheckSwapFireMode(BotComponent bot, BotWeaponInfoClass weaponInfo) { Calls++; }
    }
}
public static partial class CombatChecks {
    private static void TestIdleWeaponGuard() {
        foreach(var tactic in new[]{FollowerCombatTactic.SainMan,FollowerCombatTactic.SAINShooter}) {
            var b=RegroupBot("idleWeapon"+tactic,0);b.Follower.CombatTactic=tactic;Tick();
            var mode=new Firemode();var enemy=b.Sain.GoalEnemy;b.Sain.GoalEnemy=null;
            for(int i=0;i<1000;i++)mode.CheckSwapFireMode(b.Sain,null);
            Check(mode.Calls==0,"idle "+tactic+" skips SAIN mode changes and inspection routine");
            b.Sain.GoalEnemy=enemy;mode.CheckSwapFireMode(b.Sain,null);
            Check(mode.Calls==1,"combat "+tactic+" retains native mode selection");
            b.Memory.GoalEnemy=null;b.Follower.CombatIndependent=false;b.Follower.CanPatrol=false;b.Follower.CombatIndependenceRequested=false;
            mode.CheckSwapFireMode(b.Sain,null);
            Check(mode.Calls==1,"unaccepted heard contact cannot re-enable idle weapon fiddling");
            b.Follower.CombatIndependent=true;mode.CheckSwapFireMode(b.Sain,null);
            Check(mode.Calls==2,"independent real native combat retains fire-mode selection");
            enemy.EnemyPlayer.HealthController.IsAlive=false;mode.CheckSwapFireMode(b.Sain,null);
            Check(mode.Calls==2,"dead retained target does not enable post-combat fiddling");
            enemy.EnemyPlayer.HealthController.IsAlive=true;enemy.EnemyKnown=false;mode.CheckSwapFireMode(b.Sain,null);
            Check(mode.Calls==2,"forgotten target does not enable idle mode selection");
            b.Follower.CombatTactic=FollowerCombatTactic.Balanced;b.Sain.GoalEnemy=null;mode.CheckSwapFireMode(b.Sain,null);
            Check(mode.Calls==3,"Core tactics retain existing compatibility behavior");
            b.Follower=null;mode.CheckSwapFireMode(b.Sain,null);
            Check(mode.Calls==4,"ordinary SAIN bot retains native idle behavior");
        }
    }
}
