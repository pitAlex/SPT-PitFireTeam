namespace pitTeam.Utils
{
    internal static class FollowerSprintDistancePolicy
    {
        internal static bool ShouldSprint(float distance, bool sprintRequested, float startDistance, float stopDistance)
        {
            return sprintRequested ? distance > stopDistance : distance >= startDistance;
        }
    }
}
