
using EFT;
using System.Collections.Generic;

using pitTeam.Modules;

namespace pitTeam.Components
{
    public class BossFollowerPlayer : BotFollowerPlayer
    {

        private void MakeAllyBossEnemy(BotOwner rival, Player enemy)
        {
            BotGroupEnemyInfo groupInfo;
            rival.BotsGroup.Enemies.TryGetValue(enemy, out groupInfo);

            if (groupInfo == null)
            {
                rival.BotsGroup.AddEnemy(enemy, EBotEnemyCause.addPlayerToBoss);
                rival.BotsGroup.Enemies.TryGetValue(enemy, out groupInfo);
            }
            if (groupInfo == null)
            {
                groupInfo = new BotGroupEnemyInfo(enemy, rival.BotsGroup, EBotEnemyCause.addPlayerToBoss);

                rival.Memory.AddEnemy(enemy, groupInfo, false);
            }
        }
        public BossFollowerPlayer(BotOwner bot, pitAIBossPlayer player, WildSpawnType bossRole) : base(bot, player, false, bossRole)
        {

            NpcMessage.RemoveNpc(bot.ProfileId, false);

            // when questing with bosses, there will not be any messages from them
            if (player.realPlayer.Side != EPlayerSide.Savage && (!Utils.Props.BossFollowersType.Contains(bossRole) || !Utils.Utils.FlagGet("questGoons")))
                NpcMessage.AddNpc(bot, false, true);

        }

        protected override void SetFollowerSettings(BotOwner bot)
        {

            base.SetFollowerSettings(bot);

            bot.Settings.FileSettings.Look.LOOK_THROUGH_GRASS = Proficiency.Vanilla.Boss.LOOK_THROUGH_GRASS;

            bot.Settings.FileSettings.Boss.EFFECT_REGENERATION_PER_MIN = 60f;

            if (bot.IsRole(WildSpawnType.followerBirdEye))
            {
                FollowerVanillaBirdEyeValues birdEye = Proficiency.Vanilla.BirdEye;
                //bot.Settings.FileSettings.Core.GainSightCoef = 0.1f;
                bot.Settings.FileSettings.Cover.SOUND_TO_GET_SPOTTED = birdEye.SOUND_TO_GET_SPOTTED;
                bot.Settings.FileSettings.Cover.SPOTTED_COVERS_RADIUS = birdEye.SPOTTED_COVERS_RADIUS;
                bot.Settings.FileSettings.Shoot.LOW_DIST_TO_CHANGE_WEAPON = birdEye.LOW_DIST_TO_CHANGE_WEAPON;
                bot.Settings.FileSettings.Shoot.FAR_DIST_TO_CHANGE_WEAPON = birdEye.FAR_DIST_TO_CHANGE_WEAPON;
                bot.Settings.FileSettings.Shoot.DIST_TO_CHANGE_TO_MAIN = birdEye.DIST_TO_CHANGE_TO_MAIN;
                bot.Settings.FileSettings.Aiming.SCATTERING_DIST_MODIF = birdEye.SCATTERING_DIST_MODIF;
                bot.Settings.FileSettings.Aiming.HARD_AIM = birdEye.HARD_AIM;
                bot.Settings.FileSettings.Mind.MAX_AGGRO_BOT_DIST = 200f;
                bot.Settings.FileSettings.Look.MAX_VISION_GRASS_METERS = birdEye.MAX_VISION_GRASS_METERS;
            }

            //bot.Tactic.AggressionChange(-1f);

            // ensure Goons are enemies to other bosses - SAIN & MOAR fix
            List<WildSpawnType> allies = new List<WildSpawnType>
            {
                WildSpawnType.bossKnight,
                WildSpawnType.followerBigPipe,
                WildSpawnType.followerBirdEye,
                WildSpawnType.bossZryachiy,
                WildSpawnType.followerZryachiy,
            };

            foreach (var keyValuePair in bot.BotsController.BotSpawner.Groups)
            {
                foreach (BotsGroup botsGroup in keyValuePair.Value.GetGroups(true))
                {

                    if ((_player.bossGroup != null && botsGroup.Id == _player.bossGroup.Id) || botsGroup.Id == bot.BotsGroup.Id) continue;

                    WildSpawnType type = botsGroup.InitialBotType;
                    string t = type.ToString().ToLower();

                    if (!allies.Contains(type) && (t.StartsWith("boss") || t.StartsWith("follower")))
                    {
                        botsGroup.AddEnemy(bot.GetPlayer, EBotEnemyCause.initial);

                        for (int i = 0; i < botsGroup.MembersCount; i++)
                        {
                            BotOwner member = botsGroup.Member(i);
                            bot.BotsGroup.AddEnemy(member.GetPlayer, EBotEnemyCause.initial);
                        }
                    }
                }
            }
        }

    }
}
