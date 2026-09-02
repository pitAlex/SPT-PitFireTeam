using EFT;
using pitTeam.BigBrain;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace pitTeam.Modules
{
    /// <summary>
    /// Owns follower body-part selection after the shared follower correction has resolved its
    /// verified head/body lanes and EFT has resolved any additional active body-part lanes.
    /// Selection is intentionally allocation-free because GetVisiblePartToShoot can run every frame.
    /// </summary>
    internal static class FollowerAimTargetPolicy
    {
        private const float MinimumHeadPreference = 10f;
        private const float NeutralHeadPreference = 33f;
        private const float MaximumHeadPreference = 60f;
        private const float MinimumRetargetSeconds = 0.1f;

        private static readonly HashSet<object> RegisteredSainFollowers =
            new HashSet<object>(ReferenceComparer.Instance);

        internal static float GetHeadPreference(float precisionPercent)
        {
            float precision = FollowerProficiencyModifierValues.NormalizePercent(precisionPercent);
            if (precision <= FollowerProficiencyModifierValues.DefaultPercent)
            {
                return MinimumHeadPreference +
                       (NeutralHeadPreference - MinimumHeadPreference) *
                       (precision / FollowerProficiencyModifierValues.DefaultPercent);
            }

            return NeutralHeadPreference +
                   (MaximumHeadPreference - NeutralHeadPreference) *
                   ((precision - FollowerProficiencyModifierValues.DefaultPercent) /
                    FollowerProficiencyModifierValues.DefaultPercent);
        }

        internal static void RegisterSainFollower(object? sainBot)
        {
            if (sainBot != null)
            {
                RegisteredSainFollowers.Add(sainBot);
            }
        }

        internal static void UnregisterSainFollower(object? sainBot)
        {
            if (sainBot != null)
            {
                RegisteredSainFollowers.Remove(sainBot);
            }
        }

        internal static bool IsRegisteredSainFollower(object? sainBot)
        {
            return sainBot != null && RegisteredSainFollowers.Contains(sainBot);
        }

        /// <summary>
        /// Returns false for non-followers so EFT/SAIN can retain normal ownership. For followers,
        /// only parts that are both visible and have a verified shot lane are eligible. Precision
        /// controls head preference only when a non-head alternative also exists; a sole exposed
        /// head is always valid and an occluded body is never introduced as a fallback.
        /// </summary>
        internal static bool TrySelectFollowerShootPoint(
            EnemyInfo? enemyInfo,
            out Vector3 shootPoint,
            out bool hasShootPoint)
        {
            shootPoint = Vector3.zero;
            hasShootPoint = false;
            BotOwner? botOwner = enemyInfo?.Owner;
            if (botOwner == null ||
                !FollowerProficiency.TryGetValues(botOwner, out FollowerProficiencyValues? proficiency) ||
                proficiency == null)
            {
                return false;
            }

            if (botOwner.WeaponManager?.UnderbarrelLauncherController?.IsActive == true)
            {
                shootPoint = enemyInfo!.CurrPosition;
                hasShootPoint = true;
                return true;
            }

            if (!enemyInfo!.CanShoot)
            {
                return true;
            }

            bool hasCorrectedParts = FollowerEnemyInfoCorrection.TryGetVerifiedShootParts(
                enemyInfo,
                out bool correctedHead,
                out bool correctedBody);

            float now = Time.time;
            EnemyPart? selected = enemyInfo.LastPartToShoot;
            bool previousPartEligible = IsEligiblePart(
                enemyInfo,
                selected,
                hasCorrectedParts,
                correctedHead,
                correctedBody);
            bool previousRetargetTimerActive = enemyInfo._nextPartRndTime > now;
            if (previousPartEligible &&
                previousRetargetTimerActive)
            {
                shootPoint = GetShootPoint(
                    selected!,
                    hasCorrectedParts,
                    correctedHead,
                    correctedBody);
                hasShootPoint = true;
                return true;
            }

            EnemyPart? head = GetEligiblePart(
                enemyInfo,
                BodyPartType.head,
                hasCorrectedParts,
                correctedHead,
                correctedBody);
            EnemyPart? body = GetEligiblePart(
                enemyInfo,
                BodyPartType.body,
                hasCorrectedParts,
                correctedHead,
                correctedBody);
            EnemyPart? leftArm = GetEligiblePart(
                enemyInfo,
                BodyPartType.leftArm,
                hasCorrectedParts,
                correctedHead,
                correctedBody);
            EnemyPart? rightArm = GetEligiblePart(
                enemyInfo,
                BodyPartType.rightArm,
                hasCorrectedParts,
                correctedHead,
                correctedBody);
            EnemyPart? leftLeg = GetEligiblePart(
                enemyInfo,
                BodyPartType.leftLeg,
                hasCorrectedParts,
                correctedHead,
                correctedBody);
            EnemyPart? rightLeg = GetEligiblePart(
                enemyInfo,
                BodyPartType.rightLeg,
                hasCorrectedParts,
                correctedHead,
                correctedBody);

            int nonHeadCount = CountNonNull(body, leftArm, rightArm, leftLeg, rightLeg);
            float precisionPercent = proficiency.Modifiers.GetPrecisionPercent();
            float headPreference = GetHeadPreference(precisionPercent);
            bool forcedHead = head != null && nonHeadCount == 0;
            bool headRollAttempted = head != null && nonHeadCount > 0;
            bool headRollSucceeded = headRollAttempted && MyExtensions.RandomBool(headPreference);
            EnemyPart? nextPart;
            if (head != null &&
                (forcedHead || headRollSucceeded))
            {
                nextPart = head;
            }
            else
            {
                nextPart = SelectRandomNonHeadPart(
                    nonHeadCount,
                    body,
                    leftArm,
                    rightArm,
                    leftLeg,
                    rightLeg);
            }

            if (nextPart == null)
            {
                BattleRecorder.RecordAimTargetSelection(
                    botOwner,
                    enemyInfo,
                    selected,
                    previousPartEligible,
                    previousRetargetTimerActive,
                    precisionPercent,
                    headPreference,
                    hasCorrectedParts,
                    correctedHead,
                    correctedBody,
                    head != null,
                    body != null,
                    nonHeadCount,
                    forcedHead,
                    headRollAttempted,
                    headRollSucceeded,
                    null,
                    null);
                return true;
            }

            enemyInfo.LastPartToShoot = nextPart;
            enemyInfo._nextPartRndTime = now + Mathf.Max(
                MinimumRetargetSeconds,
                BotInternalSettingsController.Core.SHOOT_TO_CHANGE_RND_PART_DELTA);
            shootPoint = GetShootPoint(
                nextPart,
                hasCorrectedParts,
                correctedHead,
                correctedBody);
            hasShootPoint = true;
            BattleRecorder.RecordAimTargetSelection(
                botOwner,
                enemyInfo,
                selected,
                previousPartEligible,
                previousRetargetTimerActive,
                precisionPercent,
                headPreference,
                hasCorrectedParts,
                correctedHead,
                correctedBody,
                head != null,
                body != null,
                nonHeadCount,
                forcedHead,
                headRollAttempted,
                headRollSucceeded,
                nextPart,
                shootPoint);
            return true;
        }

        private static EnemyPart? GetEligiblePart(
            EnemyInfo enemyInfo,
            BodyPartType bodyPartType,
            bool hasCorrectedParts,
            bool correctedHead,
            bool correctedBody)
        {
            if (!enemyInfo._allParts.TryGetValue(bodyPartType, out EnemyPart part) ||
                !IsEligiblePart(
                    enemyInfo,
                    part,
                    hasCorrectedParts,
                    correctedHead,
                    correctedBody))
            {
                return null;
            }

            return part;
        }

        private static bool IsEligiblePart(
            EnemyInfo enemyInfo,
            EnemyPart? part,
            bool hasCorrectedParts,
            bool correctedHead,
            bool correctedBody)
        {
            if (part == null)
            {
                return false;
            }

            if (hasCorrectedParts)
            {
                if (part.BodyPartType == BodyPartType.head)
                {
                    return correctedHead;
                }

                if (part.BodyPartType == BodyPartType.body)
                {
                    return correctedBody;
                }
            }

            return part.CanShoot &&
                   enemyInfo._allPartsVision.TryGetValue(part.BodyPartType, out EnemyPartVision vision) &&
                   vision.Visible;
        }

        private static Vector3 GetShootPoint(
            EnemyPart part,
            bool hasCorrectedParts,
            bool correctedHead,
            bool correctedBody)
        {
            bool correctionVerified = hasCorrectedParts &&
                                      ((part.BodyPartType == BodyPartType.head && correctedHead) ||
                                       (part.BodyPartType == BodyPartType.body && correctedBody));
            return correctionVerified
                ? part.Position
                : part.GetPartPositionWithOffset();
        }

        private static int CountNonNull(
            EnemyPart? body,
            EnemyPart? leftArm,
            EnemyPart? rightArm,
            EnemyPart? leftLeg,
            EnemyPart? rightLeg)
        {
            int count = 0;
            if (body != null) count++;
            if (leftArm != null) count++;
            if (rightArm != null) count++;
            if (leftLeg != null) count++;
            if (rightLeg != null) count++;
            return count;
        }

        private static EnemyPart? SelectRandomNonHeadPart(
            int count,
            EnemyPart? body,
            EnemyPart? leftArm,
            EnemyPart? rightArm,
            EnemyPart? leftLeg,
            EnemyPart? rightLeg)
        {
            if (count <= 0)
            {
                return null;
            }

            int index = UnityEngine.Random.Range(0, count);
            if (body != null && index-- == 0) return body;
            if (leftArm != null && index-- == 0) return leftArm;
            if (rightArm != null && index-- == 0) return rightArm;
            if (leftLeg != null && index-- == 0) return leftLeg;
            return rightLeg;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static ReferenceComparer Instance { get; } = new ReferenceComparer();

            public new bool Equals(object? left, object? right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(object value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }
    }
}
