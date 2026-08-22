using Comfort.Common;
using EFT;
using EFT.Quests;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;

using System.Reflection;
using UnityEngine;

namespace pitTeam.Patches
{
    /** Patch kill counts in order to prevent our quests from counting if required teammate is missing **/
    internal class ConditionCounterPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(ConditionCounterManager),
                nameof(ConditionCounterManager.Test),
                new[]
                {
                    typeof(int),
                    typeof(EFT.Quests.TaskConditionCounter),
                    typeof(EFT.Quests.ConditionCheck[])
                });
        }

        [PatchPrefix]
        private static bool PatchPrefix(int valueToAdd, EFT.Quests.TaskConditionCounter counter, EFT.Quests.ConditionCheck[] checks)
        {
            try
            {

                if (!Singleton<AbstractGame>.Instantiated) return true;

                if (GamePlayerOwner.MyPlayer == null || GamePlayerOwner.MyPlayer.HealthController == null || !GamePlayerOwner.MyPlayer.HealthController.IsAlive)
                {
                    return true;
                }

                string ProfileId = GamePlayerOwner.MyPlayer.ProfileId;
                Player player = GamePlayerOwner.MyPlayer;

                if (!BossPlayers.IsPlayerBoss(ProfileId))
                {
                    return true;
                }

                var followers = BossPlayers.GetFollowersByBoss(ProfileId);

                // Goon quests that require the player to kill
                if (Utils.Props.QuestsKillConditions["Player"].Contains(counter.Id))
                {
                    bool hasAGoon = false;
                    foreach (var follower in followers)
                    {
                        BotOwner bot = follower.GetBot();
                        if (bot.IsRole(WildSpawnType.bossKnight) || bot.IsRole(WildSpawnType.followerBigPipe) || bot.IsRole(WildSpawnType.followerBirdEye))
                        {
                            hasAGoon = true;
                            break;
                        }
                    }
                    if (!hasAGoon) return false;

                    return !Utils.Utils.FlagGet("knightKill_" + ProfileId) && !Utils.Utils.FlagGet("pipeKill_" + ProfileId) && !Utils.Utils.FlagGet("birdEyeKill_" + ProfileId);
                }

                bool hasKnight = false;
                bool hasPipe = false;
                bool hasBirdEye = false;

                // Knight quests that require Knight to kill
                if (Utils.Props.QuestsKillConditions["Knight"].Contains(counter.Id))
                {


                    if (followers == null || followers.Count == 0)
                    {
                        return false;
                    }

                    foreach (var follower in followers)
                    {
                        BotOwner bot = follower.GetBot();
                        if (bot.IsRole(WildSpawnType.bossKnight))
                        {
                            if (Vector3.Distance(bot.GetPlayer.Transform.position, player.Transform.position) <= 80)
                                hasKnight = true;
                            break;
                        }
                    }

                    if (hasKnight)
                    {
                        return Utils.Utils.FlagGet("knightKill_" + ProfileId);
                    }

                    return false;
                }

                // BigPipe quests that require BigPipe to kill
                if (Utils.Props.QuestsKillConditions["BigPipe"].Contains(counter.Id))
                {
                    if (followers == null || followers.Count == 0)
                    {
                        return false;
                    }

                    foreach (var follower in followers)
                    {
                        BotOwner bot = follower.GetBot();
                        if (bot.IsRole(WildSpawnType.followerBigPipe))
                        {
                            if (Vector3.Distance(bot.GetPlayer.Transform.position, player.Transform.position) <= 80)
                                hasPipe = true;
                            break;
                        }
                    }

                    if (hasPipe)
                    {
                        return Utils.Utils.FlagGet("pipeKill_" + ProfileId);
                    }

                    return false;
                }

                // BirdEye quests that require BirdEye to kill
                if (Utils.Props.QuestsKillConditions["BirdEye"].Contains(counter.Id))
                {
                    if (followers == null || followers.Count == 0)
                    {
                        return false;
                    }

                    foreach (var follower in followers)
                    {
                        BotOwner bot = follower.GetBot();
                        if (follower.GetBot().IsRole(WildSpawnType.followerBirdEye))
                        {
                            if (Vector3.Distance(bot.GetPlayer.Transform.position, player.Transform.position) <= 120)
                                hasBirdEye = true;
                            break;
                        }
                    }

                    if (hasBirdEye)
                    {
                        return Utils.Utils.FlagGet("birdEyeKill_" + ProfileId);
                    }

                    return false;
                }


                bool hasTeamer = false;
                // Knight quests that require Knight as teammate - player or Knight can kill
                if (Utils.Props.QuestsTeamConditions["Knight"].Contains(counter.Id))
                {
                    if (followers == null || followers.Count == 0)
                    {
                        return false;
                    }

                    foreach (var follower in followers)
                    {
                        BotOwner bot = follower.GetBot();
                        if (bot.IsRole(WildSpawnType.bossKnight))
                        {
                            if (Vector3.Distance(bot.GetPlayer.Transform.position, player.Transform.position) <= 80)
                                hasTeamer = true;
                            break;
                        }
                    }

                    return hasTeamer;
                }

                // BigPipe quests that require BigPipe as teammate - player or BigPipe can kill
                if (Utils.Props.QuestsTeamConditions["BigPipe"].Contains(counter.Id))
                {
                    if (followers == null || followers.Count == 0)
                    {
                        return false;
                    }

                    foreach (var follower in followers)
                    {
                        BotOwner bot = follower.GetBot();
                        if (bot.IsRole(WildSpawnType.followerBigPipe))
                        {
                            if (Vector3.Distance(bot.GetPlayer.Transform.position, player.Transform.position) <= 80)
                                hasTeamer = true;
                            break;
                        }
                    }

                    return hasTeamer;
                }

                // BirdEye quests that require BirdEye as teammate - player or BirdEye can kill
                if (Utils.Props.QuestsTeamConditions["BirdEye"].Contains(counter.Id))
                {
                    if (followers == null || followers.Count == 0)
                    {
                        return false;
                    }

                    foreach (var follower in followers)
                    {
                        BotOwner bot = follower.GetBot();
                        if (bot.IsRole(WildSpawnType.followerBirdEye))
                        {
                            if (Vector3.Distance(bot.GetPlayer.Transform.position, player.Transform.position) <= 120)
                                hasTeamer = true;
                            break;
                        }
                    }

                    return hasTeamer;
                }

                // Goons quests that require any goon as teammate - player or them can kill
                if (Utils.Props.QuestsTeamConditions["Any"].Contains(counter.Id))
                {
                    if (followers == null || followers.Count == 0)
                    {
                        return false;
                    }

                    foreach (var follower in followers)
                    {
                        BotOwner bot = follower.GetBot();
                        if (Vector3.Distance(bot.GetPlayer.Transform.position, player.Transform.position) <= 80)
                        {
                            hasTeamer = true;
                            break;
                        }
                    }

                    return hasTeamer;
                }
            }
            catch (System.Exception e)
            {
                Modules.Logger.LogError(e);
            }

            return true;
        }
    }
}
