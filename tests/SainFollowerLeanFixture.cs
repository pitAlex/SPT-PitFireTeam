using System.Runtime.CompilerServices;
using EFT;
using pitTeam.Components;
using pitTeam.SAINAddon;
using SAIN.Components;
using SAIN.Models.Enums;

namespace SAIN.Components {
    public partial class BotComponent { public bool SAINLayersActive => ActiveLayer != ESAINLayer.None; }
}
namespace SAIN.SAINComponent.Classes.Mover {
    public class LeanClass {
        public readonly BotComponent Bot;
        public int NativeCalls;
        public bool NativeAllowsLean = true;
        public LeanClass(BotComponent bot) { Bot = bot; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool CheckCanLeanByState(out bool resetLean) {
            NativeCalls++;
            resetLean = !NativeAllowsLean;
            return NativeAllowsLean;
        }
        public bool Probe(out bool resetLean) => CheckCanLeanByState(out resetLean);
    }
}
public static partial class CombatChecks {
    private static void TestFollowerLeanGuard() {
        var grunt = RegroupBot("postCombatLean", 0);
        grunt.Follower.CombatTactic = FollowerCombatTactic.SainMan;
        Tick();
        var lean = new SAIN.SAINComponent.Classes.Mover.LeanClass(grunt.Sain);
        grunt.Sain.ActiveLayer = ESAINLayer.None;
        Check(!lean.Probe(out bool reset) && reset && lean.NativeCalls == 0,
            "inactive Grunt releases native retained-enemy lean during Core movement");
        grunt.Sain.ActiveLayer = ESAINLayer.Combat;
        Check(lean.Probe(out reset) && !reset && lean.NativeCalls == 1,
            "active Grunt combat retains native SAIN lean selection");
        grunt.Sain.ActiveLayer = ESAINLayer.None;
        grunt.Follower.CombatTactic = FollowerCombatTactic.SAINShooter;
        Check(!lean.Probe(out reset) && reset && lean.NativeCalls == 1,
            "inactive Shooter also releases native lean");
        grunt.Follower.CombatTactic = FollowerCombatTactic.Balanced;
        Check(lean.Probe(out reset) && !reset && lean.NativeCalls == 2,
            "Core tactic leaves native lean policy untouched");
        grunt.Follower = null;
        Check(lean.Probe(out reset) && !reset && lean.NativeCalls == 3,
            "ordinary SAIN bot keeps native lean policy");
    }
}
