// Replica of SAIN 4.5.1 SquadDecisionClass (Solarint, MIT; SAIN-LICENSE.txt).
// Native squad branches remain below the explicit follower regroup extension.
using EFT;
using SAIN.Components;
using SAIN.Models.Enums;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using SAIN.SAINComponent.Classes.Info;
using UnityEngine;

using SAIN.SAINComponent;
using pitTeam.Modules;
namespace pitTeam.SAINAddon;

public class SAINFollowerSquadDecision : BotBase
{
    public SAINFollowerSquadDecision(BotComponent sain)
        : base(sain) { }

    private BotSquadContainer Squad
    {
        get { return Bot.Squad; }
    }

    public bool GetDecision(out ESquadDecision Decision, Enemy enemy)
    {
        Decision = ESquadDecision.None;
        if (!Squad.BotInGroup || !SainPlayerSquadBridge.TryGetPlayerLeader(BotOwner, out Player leader) || leader.HealthController?.IsAlive != true)
        {
            return false;
        }

        // BEGIN addon regroup objective
        // Only commands and ongoing regroup belong here. New Auto waits for the
        // native solo result at the decision publication boundary.
        var regroup = SAINFollowerRuntime.GetRegroup(BotOwner);
        // A settled arrival must yield immediately to an existing native squad-support opportunity.
        if (regroup?.Settling == true && EnemyDecision(out Decision, enemy))
        {
            regroup.Complete("squadSupport");
            return true;
        }
        if (regroup?.GetDecision() == true)
        {
            Decision = ESquadDecision.Regroup;
            return true;
        }
        // END addon regroup objective

        if (EnemyDecision(out Decision, enemy))
        {
            return true;
        }

        return false;
    }

    float SquaDecision_RadioCom_MaxDistSq = 1200f;
    float SquadDecision_MyEnemySeenRecentTime = 10f;

    private bool EnemyDecision(out ESquadDecision Decision, Enemy enemy)
    {
        Decision = ESquadDecision.None;
        Enemy myEnemy = Bot.GoalEnemy;

        if (shallPushSuppressedEnemy(myEnemy))
        {
            Decision = ESquadDecision.PushSuppressedEnemy;
            return true;
        }
        if (myEnemy != null)
        {
            if (myEnemy.IsVisible || myEnemy.TimeSinceSeen < SquadDecision_MyEnemySeenRecentTime)
            {
                return false;
            }
        }
        if (shallGroupSearch())
        {
            Decision = ESquadDecision.GroupSearch;
            return true;
        }

        foreach (var member in Bot.Squad.Members.Values)
        {
            if (member == null || member.BotOwner == BotOwner || member.BotOwner.IsDead)
            {
                continue;
            }
            if (!HasRadioComms && (Bot.Transform.Position - member.Transform.Position).sqrMagnitude > SquaDecision_RadioCom_MaxDistSq)
            {
                continue;
            }
            if (myEnemy != null && member.HasEnemy)
            {
                if (myEnemy.EnemyPlayer == member.GoalEnemy.EnemyPlayer)
                {
                    if (shallSuppressEnemy(member))
                    {
                        Decision = ESquadDecision.Suppress;
                        return true;
                    }
                    if (shallHelp(member))
                    {
                        Decision = ESquadDecision.Help;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static readonly float PushSuppressedEnemyMaxPathDistance = 75f;
    private static readonly float PushSuppressedEnemyMaxPathDistanceSprint = 100f;
    private static readonly float PushSuppressedEnemyLowAmmoRatio = 0.5f;

    private bool shallPushSuppressedEnemy(Enemy enemy)
    {
        if (
            enemy != null
            && !Bot.Decision.SelfActionDecisions.LowOnAmmo(PushSuppressedEnemyLowAmmoRatio)
            && Bot.Info.PersonalitySettings.Rush.CanRushEnemyReloadHeal
        )
        {
            bool inRange = false;
            float modifier = enemy.Status.VulnerableAction == EEnemyAction.UsingSurgery ? 1.25f : 1f;
            if (enemy.Path.PathLength < PushSuppressedEnemyMaxPathDistanceSprint * modifier && BotOwner?.CanSprintPlayer == true)
            {
                inRange = true;
            }
            else if (enemy.Path.PathLength < PushSuppressedEnemyMaxPathDistance * modifier)
            {
                inRange = true;
            }

            if (
                inRange
                && (Bot.Memory.Health.HealthStatus == ETagStatus.Healthy || Bot.Memory.Health.HealthStatus == ETagStatus.Injured)
                && Bot.Squad.SquadInfo.SquadIsSuppressEnemy(enemy.EnemyPlayer.ProfileId, out var suppressingMember)
                && suppressingMember != Bot
            )
            {
                var enemyStatus = enemy.Status;
                if (enemy.Status.VulnerableAction != EEnemyAction.None)
                {
                    return true;
                }
                ETagStatus enemyHealth = enemy.EnemyPlayer.HealthStatus;
                if (enemyHealth == ETagStatus.Dying || enemyHealth == ETagStatus.BadlyInjured)
                {
                    return true;
                }
                else if (enemy.EnemyPlayer.IsInPronePose)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool HasRadioComms
    {
        get { return Bot.PlayerComponent.Equipment.GearInfo.HasEarPiece; }
    }

    float SquadDecision_SuppressFriendlyDistStart = 30f;
    float SquadDecision_SuppressFriendlyDistEnd = 50f;

    private bool shallSuppressEnemy(BotComponent member)
    {
        if (Bot.GoalEnemy?.SuppressionTarget == null)
        {
            return false;
        }
        if (Bot.GoalEnemy?.IsVisible == true)
        {
            return false;
        }
        if (member.Decision.CurrentCombatDecision != ECombatDecision.Retreat)
        {
            return false;
        }

        float memberDistance = (member.Transform.Position - BotOwner.Position).magnitude;
        float ammo = Bot.Decision.SelfActionDecisions.AmmoRatio;

        if (Bot.Decision.CurrentSquadDecision == ESquadDecision.Suppress)
        {
            return memberDistance <= SquadDecision_SuppressFriendlyDistEnd && ammo >= 0.1f;
        }
        return memberDistance <= SquadDecision_SuppressFriendlyDistStart && ammo >= 0.5f;
    }

    private bool shallGroupSearch(BotComponent member)
    {
        bool squadSearching =
            member.Decision.CurrentCombatDecision == ECombatDecision.Search
            || member.Decision.CurrentSquadDecision == ESquadDecision.Search;
        if (squadSearching)
        {
            return true;
        }
        return false;
    }

    private bool shallGroupSearch()
    {
        if (Bot.Info.Profile.IsBoss && Bot.Info.Profile.WildSpawnType != WildSpawnType.bossKnight)
        {
            //return false;
        }

        foreach (var member in Bot.Squad.Members.Values)
        {
            if (member.Decision.CurrentCombatDecision == ECombatDecision.Search && Bot.GoalEnemy != null && doesMemberShareEnemy(member))
            {
                return true;
            }
        }
        return false;
    }

    private bool doesMemberShareEnemy(BotComponent member)
    {
        if (member == null || member.ProfileId == Bot.ProfileId || member.BotOwner?.IsDead == true)
        {
            return false;
        }

        return member.GoalEnemy != null && member.GoalEnemy.EnemyPlayer.ProfileId == Bot.GoalEnemy.EnemyPlayer.ProfileId;
    }

    float SquadDecision_StartHelpFriendDist = 30f;
    float SquadDecision_EndHelpFriendDist = 45f;
    float SquadDecision_EndHelp_FriendsEnemySeenRecentTime = 8f;

    private bool shallHelp(BotComponent member)
    {
        float distance = member.GoalEnemy.Path.PathLength;
        bool visible = member.GoalEnemy.IsVisible;

        if (Bot.Decision.CurrentSquadDecision == ESquadDecision.Help && member.GoalEnemy.Seen)
        {
            return distance < SquadDecision_EndHelpFriendDist
                && member.GoalEnemy.TimeSinceSeen < SquadDecision_EndHelp_FriendsEnemySeenRecentTime;
        }
        return distance < SquadDecision_StartHelpFriendDist && visible;
    }

}
