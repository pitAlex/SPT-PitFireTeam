using EFT;
using HarmonyLib;
using pitTeam.Modules;
using System.Reflection;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerBushVisionPatch
    {
        private static readonly BotGlobalLookData VanillaLookDefaults = new BotGlobalLookData();

        private struct LookState
        {
            public bool Applied;
            public float MaxVisionGrassMeters;
            public float MaxVisionGrassMetersFlare;
            public float MaxVisionGrassMetersOpt;
            public float MaxVisionGrassMetersFlareOpt;
            public float GoalToFullDisappearGreen;
            public float NoGreenDist;
            public float NoGrassDist;
            public bool LookThroughGrass;
            public float LookThroughGrassDistMeters;
            public float InsideBushCoef;
            public float LookThroughPeriodByHit;
        }

        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(typeof(EnemyInfo), nameof(EnemyInfo.CheckLookEnemy));
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find EnemyInfo.CheckLookEnemy for follower bush-vision patch.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(typeof(SAINFollowerBushVisionPatch), nameof(Prefix_CheckLookEnemy)),
                postfix: new HarmonyMethod(typeof(SAINFollowerBushVisionPatch), nameof(Postfix_CheckLookEnemy)));

            Modules.Logger.LogInfo("[Init] SAIN follower bush vision patch applied.");
        }

        private static void Prefix_CheckLookEnemy(EnemyInfo __instance, ref LookState __state)
        {
            BotOwner owner = __instance?.Owner;
            BotGlobalLookData look = owner?.Settings?.FileSettings?.Look;
            if (owner == null || look == null || !BossPlayers.IsFollower(owner))
            {
                return;
            }

            __state = new LookState
            {
                Applied = true,
                MaxVisionGrassMeters = look.MAX_VISION_GRASS_METERS,
                MaxVisionGrassMetersFlare = look.MAX_VISION_GRASS_METERS_FLARE,
                MaxVisionGrassMetersOpt = look.MAX_VISION_GRASS_METERS_OPT,
                MaxVisionGrassMetersFlareOpt = look.MAX_VISION_GRASS_METERS_FLARE_OPT,
                GoalToFullDisappearGreen = look.GOAL_TO_FULL_DISSAPEAR_GREEN,
                NoGreenDist = look.NO_GREEN_DIST,
                NoGrassDist = look.NO_GRASS_DIST,
                LookThroughGrass = look.LOOK_THROUGH_GRASS,
                LookThroughGrassDistMeters = look.LOOK_THROUGH_GRASS_DIST_METERS,
                InsideBushCoef = look.INSIDE_BUSH_COEF,
                LookThroughPeriodByHit = look.LOOK_THROUGH_PERIOD_BY_HIT,
            };

            float maxVisionGrassMeters = FollowerProficiency.TryGetValues(owner, out FollowerProficiencyValues? proficiency)
                ? proficiency.Vanilla.Vision.MAX_VISION_GRASS_METERS
                : VanillaLookDefaults.MAX_VISION_GRASS_METERS;
            bool lookThroughGrass = proficiency?.Vanilla.Vision.LOOK_THROUGH_GRASS ??
                VanillaLookDefaults.LOOK_THROUGH_GRASS;
            if (maxVisionGrassMeters <= 0f)
            {
                maxVisionGrassMeters = VanillaLookDefaults.MAX_VISION_GRASS_METERS;
            }

            look.MAX_VISION_GRASS_METERS = maxVisionGrassMeters;
            look.MAX_VISION_GRASS_METERS_FLARE = VanillaLookDefaults.MAX_VISION_GRASS_METERS_FLARE;
            look.MAX_VISION_GRASS_METERS_OPT = 1f / maxVisionGrassMeters;
            look.MAX_VISION_GRASS_METERS_FLARE_OPT = VanillaLookDefaults.MAX_VISION_GRASS_METERS_FLARE_OPT;
            look.GOAL_TO_FULL_DISSAPEAR_GREEN = VanillaLookDefaults.GOAL_TO_FULL_DISSAPEAR_GREEN;
            look.NO_GREEN_DIST = VanillaLookDefaults.NO_GREEN_DIST;
            look.NO_GRASS_DIST = VanillaLookDefaults.NO_GRASS_DIST;
            look.LOOK_THROUGH_GRASS = lookThroughGrass;
            look.LOOK_THROUGH_GRASS_DIST_METERS = VanillaLookDefaults.LOOK_THROUGH_GRASS_DIST_METERS;
            look.INSIDE_BUSH_COEF = VanillaLookDefaults.INSIDE_BUSH_COEF;
            look.LOOK_THROUGH_PERIOD_BY_HIT = VanillaLookDefaults.LOOK_THROUGH_PERIOD_BY_HIT;
        }

        private static void Postfix_CheckLookEnemy(EnemyInfo __instance, LookState __state)
        {
            if (!__state.Applied)
            {
                return;
            }

            BotGlobalLookData look = __instance?.Owner?.Settings?.FileSettings?.Look;
            if (look == null)
            {
                return;
            }

            look.MAX_VISION_GRASS_METERS = __state.MaxVisionGrassMeters;
            look.MAX_VISION_GRASS_METERS_FLARE = __state.MaxVisionGrassMetersFlare;
            look.MAX_VISION_GRASS_METERS_OPT = __state.MaxVisionGrassMetersOpt;
            look.MAX_VISION_GRASS_METERS_FLARE_OPT = __state.MaxVisionGrassMetersFlareOpt;
            look.GOAL_TO_FULL_DISSAPEAR_GREEN = __state.GoalToFullDisappearGreen;
            look.NO_GREEN_DIST = __state.NoGreenDist;
            look.NO_GRASS_DIST = __state.NoGrassDist;
            look.LOOK_THROUGH_GRASS = __state.LookThroughGrass;
            look.LOOK_THROUGH_GRASS_DIST_METERS = __state.LookThroughGrassDistMeters;
            look.INSIDE_BUSH_COEF = __state.InsideBushCoef;
            look.LOOK_THROUGH_PERIOD_BY_HIT = __state.LookThroughPeriodByHit;
        }
    }
}
