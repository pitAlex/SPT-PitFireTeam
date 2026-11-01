using System;
using EFT;

namespace pitTeam.Modules
{
    // Optional companions may share one command verdict. Core still admits targets,
    // computes ordinary willingness and owns command publication/execution.
    public sealed class CompanionAdvanceOrderContext
    {
        public BotOwner Follower { get; }
        public IPlayer Requester { get; }
        public EnemyInfo Target { get; }
        public int Sequence { get; }
        private readonly Func<BotOwner, EnemyInfo, bool> evaluateNormal;
        internal CompanionAdvanceOrderContext(BotOwner follower, IPlayer requester, EnemyInfo target,
            int sequence, Func<BotOwner, EnemyInfo, bool> evaluateNormal)
        {
            Follower = follower; Requester = requester; Target = target;
            Sequence = sequence; this.evaluateNormal = evaluateNormal;
        }
        public bool EvaluateNormal(BotOwner follower, EnemyInfo target) => evaluateNormal(follower, target);
    }
}
