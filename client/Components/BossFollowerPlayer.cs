
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

            settingModif.AccuratySpeedCoef = 1f;

            if (bossRole == WildSpawnType.followerBirdEye)
            {
                settingModif.VisibleDistCoef = 0.8f; // tone down the visibility of BirdEye
            }
        }

        protected override void SetFollowerSettings(BotOwner bot)
        {

            base.SetFollowerSettings(bot);

            bot.Settings.FileSettings.Look.LOOK_THROUGH_GRASS = false;

            bot.Settings.FileSettings.Boss.EFFECT_REGENERATION_PER_MIN = 60f;

            if (bot.IsRole(WildSpawnType.followerBirdEye))
            {
                //bot.Settings.FileSettings.Core.GainSightCoef = 0.1f;
                bot.Settings.FileSettings.Cover.SOUND_TO_GET_SPOTTED = 10f;
                bot.Settings.FileSettings.Cover.SPOTTED_COVERS_RADIUS = 12f;
                bot.Settings.FileSettings.Shoot.LOW_DIST_TO_CHANGE_WEAPON = 30f;
                bot.Settings.FileSettings.Shoot.FAR_DIST_TO_CHANGE_WEAPON = 68f;
                bot.Settings.FileSettings.Shoot.DIST_TO_CHANGE_TO_MAIN = 60f;
                bot.Settings.FileSettings.Aiming.SCATTERING_DIST_MODIF = 0.2f;
                bot.Settings.FileSettings.Aiming.HARD_AIM = 0.9f;
                bot.Settings.FileSettings.Mind.MAX_AGGRO_BOT_DIST = 200f;
                bot.Settings.FileSettings.Look.MAX_VISION_GRASS_METERS = 1.5f;
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
