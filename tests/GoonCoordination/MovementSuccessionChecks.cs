using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.GoingRogue.Combat;
using static Program;

internal static class MovementSuccessionChecks
{
    internal static void Run()
    {
        // Every spawn composition, then loss of each commander through each native
        // liveness boundary. No enemy contact is required for peaceful following.
        foreach (bool hasKnight in new[] { false, true })
        foreach (bool hasPipe in new[] { false, true })
        foreach (bool hasBird in new[] { false, true })
        foreach (string loss in new[] { "dead", "health", "inactive", "removed" })
        {
            GoonUnitTactics.Reset();ExUsecCombatExtension.Reset();
            BossPlayers.Followers.Clear();GoonCombatLayer.Owned.Clear();
            var human = new pitAIBossPlayer();
            BotFollowerPlayer Add(WildSpawnType role, pitAIBossPlayer owner = null)
            {
                var bot = new BotOwner();bot.Profile.Info.Settings.Role = role;
                var follower = new BotFollowerPlayer { Bot = bot, Leader = owner ?? human };
                BossPlayers.Followers.Add(follower);GoonCombatLayer.Owned.Add(bot);return follower;
            }
            var knight = hasKnight ? Add(WildSpawnType.bossKnight) : null;
            var pipe = hasPipe ? Add(WildSpawnType.followerBigPipe) : null;
            var bird = hasBird ? Add(WildSpawnType.followerBirdEye) : null;
            var rogue = Add(WildSpawnType.exUsec);
            var pmc = Add(WildSpawnType.pmcUSEC);
            var foreign = Add(WildSpawnType.bossKnight, new pitAIBossPlayer());
            void Refresh() { GoonUnitTactics.RefreshAll();ExUsecCombatExtension.RefreshAnchors(); }
            void AssertLeader(BotFollowerPlayer expected)
            {
                foreach (var f in new[] { pipe, bird, rogue }.Where(f => f != null && !f.Bot.IsDead &&
                    f.Bot.GetPlayer.HealthController.IsAlive && f.Bot.BotState == EBotState.Active &&
                    BossPlayers.Followers.Contains(f) && GoonCombatLayer.Owned.Contains(f.Bot)))
                {
                    var anchor = expected == f ? null : expected?.Bot;
                    Check(f.CompanionLeader == anchor, "spawn/death movement hierarchy: " + hasKnight + "/" + hasPipe + "/" + hasBird + "/" + loss);
                    if (expected != null && expected == pipe && f != pipe)
                        Check(f.CompanionLeaderControlsPosition, "Pipe positional leadership works with saved determination");
                }
                Check(pmc.CompanionLeader == null, "ordinary PMC keeps human following");
                Check(knight == null || expected != knight || knight.CompanionLeader == null || knight.CompanionLeader == knight.Bot,
                    "Knight cannot follow a subordinate");
            }
            Refresh();AssertLeader(knight ?? pipe);
            // Holding the elected commander does not elect a different leader.
            var first = knight ?? pipe;
            if (first != null) { first.Command = FollowerCommandType.HoldPosition;Refresh();AssertLeader(first); }
            void Lose(BotFollowerPlayer f)
            {
                if (f == null) return;
                if (loss == "dead") f.Bot.IsDead = true;
                if (loss == "health") f.Bot.GetPlayer.HealthController.IsAlive = false;
                if (loss == "inactive") f.Bot.BotState = EBotState.Inactive;
                if (loss == "removed") BossPlayers.Followers.Remove(f);
            }
            Lose(knight);Refresh();
            AssertLeader(pipe);
            if (bird != null && pipe != null)
            {
                Check(bird.CompanionLeader == pipe.Bot, "Birdeye follows Pipe after Knight loss");
                Check(bird.UnitAggression == null && bird.UnitDetermination == null,
                    "Pipe succession does not restore Knight combat percentages");
            }
            Lose(pipe);Refresh();
            AssertLeader(null);
            if (bird != null) Check(bird.CompanionLeader == null, "Birdeye returns to human and never becomes commander");
            GoonUnitTactics.Reset();ExUsecCombatExtension.Reset();
            Check(rogue.CompanionLeader == null && (bird == null || bird.CompanionLeader == null), "teardown clears succession anchors");
        }
        BossPlayers.Followers.Clear();GoonCombatLayer.Owned.Clear();
    }
}
