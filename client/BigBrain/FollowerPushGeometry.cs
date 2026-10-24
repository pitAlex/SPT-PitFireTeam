using UnityEngine;

namespace pitTeam.BigBrain;

// Shared by core push and the optional SAIN objective; no movement or perception ownership.
public static class FollowerPushGeometry
{
    public const float MaxForwardRoute = 30f;
    public const float MinForwardProgress = 2f;
    public const float MinForwardDot = 0.2f;
    public static bool IsForwardPosition(Vector3 bot, Vector3 enemy, Vector3 candidate,
        float minProgress = MinForwardProgress, float minDot = MinForwardDot)
    {
        Vector3 forward = enemy - bot; forward.y = 0f;
        Vector3 advance = candidate - bot; advance.y = 0f;
        Vector3 remaining = candidate - enemy; remaining.y = 0f;
        return advance.magnitude > minProgress && Vector3.Dot(advance.normalized, forward.normalized) >= minDot &&
            remaining.magnitude <= forward.magnitude - minProgress;
    }
}
