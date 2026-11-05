using EFT;
using System.Collections.Generic;

namespace pitTeam.Utils
{
    public class Props
    {
        private static float _def_coverSearchRadius = 80f;
        private static float _def_sprintDistance = 18f;
        private static float _def_regroupMinDistance = 10f;
        private static float _def_searchRadius = 50f;
        private static float _def_nearSearchRadius = 30f;

        private static float _def_bossInnerRadius = 30f;

        private static float _def_bossOuterRadius = 50f;

        private static float _def_bossMaxCoverDistance = 35f;
        private static float _def_bossMinCoverDistance = 10f;

        public static float coverSearchRadius;
        public static float sprintDistance;
        public static float regroupMinDistance;
        public static float searchRadius;
        public static float nearSearchRadius;

        public static float bossInnerRadius;

        public static float bossOuterRadius;

        public static float bossMaxCoverDistance;
        public static float bossMinCoverDistance;

        public static void Reset()
        {
            coverSearchRadius = _def_coverSearchRadius;
            sprintDistance = _def_sprintDistance;
            regroupMinDistance = _def_regroupMinDistance;
            searchRadius = _def_searchRadius;
            nearSearchRadius = _def_nearSearchRadius;
            bossInnerRadius = _def_bossInnerRadius;
            bossOuterRadius = _def_bossOuterRadius;
            bossMaxCoverDistance = _def_bossMaxCoverDistance;
            bossMinCoverDistance = _def_bossMinCoverDistance;
            Covers.ResetMaxCoversIritation();
        }

        public static void FactoryMapSett()
        {
            regroupMinDistance = 7f;
            bossInnerRadius = 20f;
            bossOuterRadius = 40f;
            coverSearchRadius = 40f;
            searchRadius = 40f;
            Covers.SetLowMaxCoversIritation();
        }

        public static List<WildSpawnType> friendlyBotTypes = new List<WildSpawnType> {
            WildSpawnType.shooterBTR,
            WildSpawnType.gifter,
            WildSpawnType.peacefullZryachiyEvent,
            WildSpawnType.bossZryachiy,
            WildSpawnType.followerZryachiy
        };

        public static List<WildSpawnType> BossFollowersType = new List<WildSpawnType> {
            WildSpawnType.bossKnight,
            WildSpawnType.followerBigPipe,
            WildSpawnType.followerBirdEye
        };

        public static List<string> BossFriendsIds = new List<string> {
            "1113579",
            "1113580",
            "1113581"
        };

        public static List<WildSpawnType> ZombieTypes = new List<WildSpawnType> {
            WildSpawnType.infectedAssault,
            WildSpawnType.infectedCivil,
            WildSpawnType.infectedLaborant,
            WildSpawnType.infectedPmc,
            WildSpawnType.infectedTagilla
        };

        public static string KnightTrader = "67768b19fa281ca31708b187";

        public static Dictionary<string, List<string>> Quests = new Dictionary<string, List<string>>
        {
            {
                "Knight",
                // order is important
                new List<string>
                {
                    "6775d9957e2dbcb3bd0a02c7",
                    "6775da597e2dbcb3bd0a02c9",
                    "67768936fa281ca31708b17c",
                }
            },
            {
                "BigPipe",
                // order is important
                new List<string>
                {
                    "67768936fa281ca31708b17c",
                    "677689ebfa281ca31708b180"
                }
            },
            {
                "BirdEye",
                // order is important
                new List<string>
                {
                    "67768936fa281ca31708b17c",
                    "6776899afa281ca31708b17e",
                    "677689ebfa281ca31708b180",
                    "67768a41fa281ca31708b182"
                }
            }
        };

        public static Dictionary<string, List<string>> QuestsHostile = new Dictionary<string, List<string>>
        {
            {
                "Knight",
                new List<string>
                {
                    "649570491bb4d158bc4d0168",
                    "6658a15615cbb1b2c6014d5b",
                    "66e01ad6835f78499f049180",
                    "626bdcc3a371ee3a7a3514c5",
                    "647710905320c660d91c15a5",
                    "6179ad0a6e9dd54ac275e3f2",
                    "6613f307fca4f2f386029409",
                    "6615141bfda04449120269a7"
                }
            }
        };

        public static Dictionary<string, List<string>> QuestsLocations = new Dictionary<
            string,
            List<string>
        >
        {
            {
                "6775d9957e2dbcb3bd0a02c7",
                new List<string> { "lighthouse", "bigmap", "shoreline", "woods" }
            },
            {
                "6775da597e2dbcb3bd0a02c9",
                new List<string> { "bigmap" }
            },
            {
                "67768936fa281ca31708b17c",
                new List<string> { "tarkovstreets" }
            },
            {
                "6776899afa281ca31708b17e",
                new List<string> { "woods" }
            },
            {
                "677689ebfa281ca31708b180",
                new List<string> { "shoreline" }
            },
            {
                "67768a41fa281ca31708b182",
                new List<string> { "lighthouse" }
            }
        };

        public static Dictionary<string, List<string>> QuestsTeamConditions = new Dictionary<
            string,
            List<string>
        >
        {
            {
                "Knight",
                new List<string> { "6776926bfa281ca31708b1a2" }
            },
            {
                "BigPipe",
                new List<string> { }
            },
            {
                "BirdEye",
                new List<string> { }
            },
            {
                "Any",
                new List<string>
                {
                    "677fe6a9f539f6d9230066e7",
                    "677fe82bf539f6d9230066f2"
                }
            },
        };
        public static Dictionary<string, List<string>> QuestsKillConditions = new Dictionary<
            string,
            List<string>
        >
        {
            {
                "Knight",
                new List<string> { }
            },
            {
                "BigPipe",
                new List<string> { }
            },
            {
                "BirdEye",
                new List<string>
                {
                    "677fe7a6f539f6d9230066ec",
                    "677fe999f539f6d9230066fb",
                }
            },
            {
                "Player",
                new List<string>
                {
                    "67768ec3fa281ca31708b190",
                    "67768f38fa281ca31708b194",
                    "6776900dfa281ca31708b198"
                }
            }
        };

        public static Dictionary<WildSpawnType, List<string>> QuestBosses = new Dictionary<
            WildSpawnType,
            List<string>
        >
        {
            {
                WildSpawnType.bossKolontay,
                new List<string> { "67768936fa281ca31708b17c" }
            }
        };
    }
}
