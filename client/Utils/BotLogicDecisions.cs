using EFT;
using UnityEngine;
using UnityEngine.AI;

using StandardBrain = CoreActionResultParams;

namespace pitTeam.Utils
{
    public class BotLogicDecisions
    {
        public static readonly float sprintDistance = 15f;
        public static AICoreActionResult<BotLogicDecision, StandardBrain> RegroupToBoss(BotOwner bot)
        {

            BotRequest request = bot.BotRequestController.CurRequest;

            IPlayer requester = request != null ? bot.BotRequestController.CurRequest.Requester : null;

            if (requester == null && bot.BotFollower.HaveBoss)
            {
                requester = bot.BotFollower.BossToFollow.Player();
            }

            if (requester == null)
            {
                return new AICoreActionResult<BotLogicDecision, StandardBrain>(BotLogicDecision.followerPatrol, "requester.None");
            }

            Vector3 requestPos = requester.Transform.position;

            // try to find a valid position within the sphere
            Vector3? finPos = null;
            NavMeshHit navMeshHit;
            for (int i = 0; i < 15; i++) // Adjust the number of attempts as needed
            {
                Vector3 randomPosition = requestPos + UnityEngine.Random.insideUnitSphere * 20f;

                if (!NavMesh.SamplePosition(randomPosition, out navMeshHit, 2f, -1)) continue;

                if (!finPos.HasValue)
                {
                    finPos = navMeshHit.position;
                }
                else if ((requestPos - navMeshHit.position).sqrMagnitude < (requestPos - finPos.Value).sqrMagnitude)
                {
                    finPos = navMeshHit.position;
                }
            }

            if (!finPos.HasValue)
            {
                float minR = Mathf.Min(1f, 10 * 0.19f);
                float maxR = Mathf.Min(5f, 10 * 0.65f);
                float num2 = (float)Utils.RandomSing() * Utils.Random(minR, maxR);
                float num3 = (float)Utils.RandomSing() * Utils.Random(minR, maxR);
                float x = num2 + requestPos.x;
                float z = num3 + requestPos.z;
                if (NavMesh.SamplePosition(new Vector3(x, requestPos.y, z), out navMeshHit, 2f, -1))
                {
                    finPos = navMeshHit.position;
                }
            }
            // no valid point found
            if (!finPos.HasValue)
            {
                if (request != null)
                {
                    request.Complete();
                }
                return new AICoreActionResult<BotLogicDecision, StandardBrain>(BotLogicDecision.followerPatrol, "requester.badPosition");
            }

            bot.GoToSomePointData.SetPoint(finPos.Value);

            bool shouldSprint01 = (finPos.Value - bot.GetPlayer.Transform.position).sqrMagnitude >= sprintDistance * sprintDistance;

            return new AICoreActionResult<BotLogicDecision, StandardBrain>(BotLogicDecision.goToPoint, shouldSprint01 ? "regroupToBossFast" : "regroupToBoss");
        }
    }
}
