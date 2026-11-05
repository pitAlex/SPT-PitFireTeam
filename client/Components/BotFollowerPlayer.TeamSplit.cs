using EFT;
using UnityEngine;

namespace pitTeam.Components
{
    public partial class BotFollowerPlayer
    {
        public bool TeamSplitCombatActive { get; private set; }
        public BotOwner TeamSplitCommander { get; private set; }
        private float? teamSplitAggression;
        internal bool TeamSplitHandoffPending { get; private set; }
        private BotOwner teamSplitHelpEnemy;
        private float teamSplitHelpUntil;

        public void SetTeamSplitCombat(bool active, float? aggression = null, BotOwner commander = null)
        {
            teamSplitAggression = active ? aggression : null;
            TeamSplitCommander = active ? commander : null;
            if (TeamSplitCombatActive == active) return;
            TeamSplitCombatActive = active;
            teamSplitHelpEnemy = null;
            if (!active) { TeamSplitHandoffPending = false; return; }
            ResetCombatPercentageOverrides("TeamSplit");
            ClearCommand("TeamSplit");
            SetCombatRegroupBossAnchor(false);
            TeamSplitHandoffPending = true;
        }

        internal void CompleteTeamSplitHandoff() => TeamSplitHandoffPending = false;

        internal void RequestTeamSplitHelp(BotOwner enemy)
        {
            if (!TeamSplitCombatActive) return;
            teamSplitHelpEnemy = enemy;
            teamSplitHelpUntil = Time.time + 8f;
        }

        public bool TryGetTeamSplitHelpEnemy(out BotOwner enemy)
        {
            enemy = teamSplitHelpEnemy;
            var human = GetBoss()?.realPlayer;
            return TeamSplitCombatActive && Time.time < teamSplitHelpUntil &&
                enemy != null && !enemy.IsDead && enemy.GetPlayer?.HealthController?.IsAlive == true &&
                human != null && (_bot.Position - human.Position).sqrMagnitude <= 40f * 40f;
        }
    }
}
