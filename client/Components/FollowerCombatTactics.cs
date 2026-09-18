namespace pitTeam.Components;

// Persisted tactic identity is separate from shared role and fallback behavior.
public static class FollowerCombatTactics
{
    public static bool UsesSainCombat(FollowerCombatTactic tactic) =>
        tactic == FollowerCombatTactic.SainMan || tactic == FollowerCombatTactic.SAINShooter;
    public static bool IsMarksman(FollowerCombatTactic tactic) =>
        tactic == FollowerCombatTactic.Marksman || tactic == FollowerCombatTactic.SAINShooter;
    public static FollowerCombatTactic CoreTactic(FollowerCombatTactic tactic) =>
        tactic == FollowerCombatTactic.SainMan ? FollowerCombatTactic.Balanced :
        tactic == FollowerCombatTactic.SAINShooter ? FollowerCombatTactic.Marksman : tactic;
}
