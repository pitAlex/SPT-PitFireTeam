using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.Utils
{
    internal class BotDetails
    {
        public string Aid { get; set; }
        public string Tactic { get; set; }

        public float Aggression { get; set; }

        public FollowerProficiencyModifierValues Proficiency { get; set; } = new FollowerProficiencyModifierValues();

        public string Equipment { get; set; }

        public string Voice { get; set; }

        public string Head { get; set; }
    }
    internal class BotChar
    {
        public MongoID Id { get; set; }
        public string Name { get; set; }
    }
    internal class BotVoice : BotChar
    {
        public string Voice { get; set; }
    }
    internal class BotTactic : BotChar
    {
        public string Tactic { get; set; }
    }
    internal class BotCharacteristics
    {
        public List<BotVoice> Voices { get; set; }
        public List<BotChar> Heads { get; set; }
        public List<BotChar> Equipment { get; set; }
        public List<BotTactic> Tactics { get; set; }
    }

    internal class PitConfig
    {
        public bool ScavSquad { get; set; }
        public int ScavSquadSize { get; set; }
        public int Pickups { get; set; }
        public bool Restrictions { get; set; }
    }

    public class Utils
    {
        private const float GroundLikeOriginCheckHeight = 0.05f;
        private const float GroundLikeOriginCheckDistance = 0.35f;
        private static readonly float[] GroundOriginFireProbeHeights = { 0.6f, 1.2f };
        private static Dictionary<string, bool> flags = new Dictionary<string, bool>();

        private static Dictionary<string, int> values = new Dictionary<string, int>();
        [ThreadStatic]
        private static RaycastHit[]? _singleRaycastHitBuffer;


        /** Get distance between 2 points via navigation path **/
        public static float GetNavDistance(Vector3 point1, Vector3 point2, NavMeshPath existingMesh = null)
        {
            NavMeshPath navMeshPath = existingMesh != null ? existingMesh : new NavMeshPath();
            navMeshPath.ClearCorners();
            bool resut = NavMesh.CalculatePath(point1, point2, -1, navMeshPath);

            if (resut && navMeshPath.status == NavMeshPathStatus.PathComplete)
            {
                return navMeshPath.CalculatePathLength();
            }
            else
            {
                return Vector3.Distance(point2, point1);
            }
        }

        public static bool TryGetCompletePathDistance(Vector3 point1, Vector3 point2, out float distance, NavMeshPath existingMesh = null)
        {
            distance = 0f;
            NavMeshPath navMeshPath = existingMesh != null ? existingMesh : new NavMeshPath();
            navMeshPath.ClearCorners();
            if (!NavMesh.CalculatePath(point1, point2, -1, navMeshPath) ||
                navMeshPath.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            distance = navMeshPath.CalculatePathLength();
            return true;
        }

        public static bool IsWithinDistance(Vector3 point1, Vector3 point2, float distance)
        {
            return (point1 - point2).sqrMagnitude <= distance * distance;
        }

        /** Shortcut to check if bot has boss **/
        public static bool HasBoss(BotOwner botOwner)
        {
            return botOwner.BotFollower.HaveBoss && botOwner.BotFollower.BossToFollow is pitAIBossPlayer;
        }
        /** Shortcut to get the boss the follower has **/
        public static pitAIBossPlayer GetBoss(BotOwner botOwner)
        {
            return (pitAIBossPlayer)botOwner.BotFollower.BossToFollow;
        }
        /** Recreation of javascript SetTimeout **/
        public static TimerManager.ITimer SetTimeout(Action func, int timer, bool isLopped = false)
        {
            var timeout = StaticManager.Instance.TimerManager.MakeTimer(TimeSpan.FromMilliseconds(timer), isLopped);
            timeout.OnTimer += () =>
            {
                try
                {
                    func();
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError("Exception in SetTimeout");
                    Modules.Logger.LogError(ex);
                }
            };


            return timeout;
        }
        /** Shortcut to EFT method of doign MakeTimer in relation to bot activity **/
        public static TimerManager.ITimer SetBotTimer(Action func, float seconds)
        {
            var Timer = StaticManager.Instance.TimerManager.MakeTimer(TimeSpan.FromSeconds(seconds), false);

            Timer.OnTimer += () =>
            {
                try
                {
                    func();
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError("Exception in SetBotTimer");
                    Modules.Logger.LogError(ex);
                }
            };

            return Timer;
        }


        public static void FlagSet(string flag, bool value)
        {
            flags[flag] = value;
        }

        public static bool FlagGet(string flag)
        {
            flags.TryGetValue(flag, out var value);
            return value || false;
        }

        public static void ValueSet(string flag, int value)
        {
            values[flag] = value;
        }

        public static int ValueGet(string flag)
        {
            values.TryGetValue(flag, out var value);
            return value;
        }

        public static void FlagsClear()
        {
            flags.Clear();
        }


        public static void ValuesClear()
        {
            values.Clear();
        }

        public static bool PlayerHasKnightQuest(Profile playerProfile)
        {
            if (FlagGet("knightKiller_" + playerProfile.ProfileId)) return false;

            if (FlagGet("knightFriend_" + playerProfile.ProfileId)) return true;

            if (playerProfile.QuestsData == null) return false;

            if (!playerProfile.TryGetTraderInfo(Props.KnightTrader, out var traderInfo))
            {
                FlagSet("knightKiller_" + playerProfile.ProfileId, true);
                return false;
            }

            bool hasKnightQuest = false;
            bool hasKnightHostileQuest = false;

            foreach (var data in playerProfile.QuestsData)
            {
                if (Props.QuestsHostile["Knight"].Contains(data.Id))
                {

                    if (data.Status == EFT.Quests.EQuestStatus.Started)
                    {
                        hasKnightHostileQuest = true;

                    }
                }
                if (data.Id == Props.Quests["Knight"][0])
                {
                    if (data.Status == EFT.Quests.EQuestStatus.Success || data.Status == EFT.Quests.EQuestStatus.Started)
                    {
                        hasKnightQuest = true;

                    }
                }
            }

            if (hasKnightHostileQuest)
            {
                FlagSet("knightKiller_" + playerProfile.ProfileId, true);
                return false;
            }

            if (hasKnightQuest)
            {
                FlagSet("knightFriend_" + playerProfile.ProfileId, true);
                return true;
            }

            FlagSet("knightKiller_" + playerProfile.ProfileId, true);
            return false;
        }

        public static float GetScaledValue(float baseValue, float increment, int level, float maxValue)
        {
            return Math.Min(baseValue + (increment * level), maxValue);
        }
        /** Optimized version of AIUtility.IsDangerPositionFarEnough **/
        public static bool IsDangerPositionFarEnough(Vector3 positionToCheck, IEnumerable<Vector3> positionsIMustCare, float minSDistToEnemy)
        {
            foreach (var pos in positionsIMustCare)
            {
                if ((pos - positionToCheck).sqrMagnitude < minSDistToEnemy)
                {
                    return false;
                }
            }
            return true;
        }

        /** Optimized version of GClass344.CanShootToTarget **/
        public static bool CanShootToTarget(ShootToPoint shootToPoint, Vector3 firePos, LayerMask mask, bool doubleSide = false)
        {
            if (shootToPoint == null) return false;

            // Reuse a tiny per-thread buffer to avoid hot-path allocations.
            RaycastHit[] hits = _singleRaycastHitBuffer ??= new RaycastHit[1];

            if (CanShootToTargetFromOrigin(shootToPoint, firePos, mask, hits, doubleSide))
            {
                return true;
            }

            if (IsGroundLikeFireOrigin(firePos))
            {
                for (int i = 0; i < GroundOriginFireProbeHeights.Length; i++)
                {
                    Vector3 probeOrigin = firePos + Vector3.up * GroundOriginFireProbeHeights[i];
                    if (CanShootToTargetFromOrigin(shootToPoint, probeOrigin, mask, hits, false))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool CanShootToTargetFromOrigin(
            ShootToPoint shootToPoint,
            Vector3 firePos,
            LayerMask mask,
            RaycastHit[] hits,
            bool doubleSide)
        {
            Vector3 direction = shootToPoint.Point - firePos;
            float distance = direction.magnitude;
            if (distance <= 0.001f)
            {
                return false;
            }

            if (Physics.RaycastNonAlloc(new Ray(firePos, direction), hits, distance * shootToPoint.DistCoef, mask) == 0)
            {
                return true;
            }

            return doubleSide &&
                   Physics.RaycastNonAlloc(new Ray(shootToPoint.Point, -direction), hits, distance, mask) == 0;
        }

        private static bool IsGroundLikeFireOrigin(Vector3 firePos)
        {
            return Physics.Raycast(
                firePos + Vector3.up * GroundLikeOriginCheckHeight,
                Vector3.down,
                GroundLikeOriginCheckDistance + GroundLikeOriginCheckHeight,
                LayersMaskController.HighPolyWithTerrainMask);
        }

        public static bool CanShootToTarget(ShootToPoint shootToPoint, CustomNavigationPoint point, LayerMask mask, bool doubleSide = false)
        {
            bool flag = CanShootToTarget(shootToPoint, point.FirePosition, mask, doubleSide);
            point.CanIShootToEnemy = flag;
            return flag;
        }

        public static bool CanHide(Vector3 posToHide, Vector3 wallVector, IEnumerable<Vector3> positionsIMustCare, float minSDistToEnemy, bool useRaycast, bool useAng = true)
        {
            return PointsSearchHelper.CanIHide(posToHide, wallVector, positionsIMustCare, minSDistToEnemy, useRaycast, useAng);
        }

        public static float Random(float a, float b)
        {
            return UnityEngine.Random.Range(a, b);
        }

        public static int RandomSing()
        {
            return UnityEngine.Random.value < 0.5f ? -1 : 1;
        }
    }
}
