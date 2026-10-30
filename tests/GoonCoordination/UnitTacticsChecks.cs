using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.GoingRogue.Combat;
using static Program;

internal static class UnitTacticsChecks
{
    internal static void Run()
    {
        foreach(string partner in new[]{"pipe","bird","ordinary","dead","inactive","foreign","unowned"})
        {
            GoonUnitTactics.Reset();BossPlayers.Followers.Clear();GoonCombatLayer.Owned.Clear();
            var leader=new pitAIBossPlayer();
            BotFollowerPlayer Add(WildSpawnType role)
            {
                var bot=new BotOwner();bot.Profile.Info.Settings.Role=role;GoonCombatLayer.Owned.Add(bot);
                var follower=new BotFollowerPlayer{Bot=bot,Leader=leader};BossPlayers.Followers.Add(follower);return follower;
            }
            var knight=Add(WildSpawnType.bossKnight);GoonUnitTactics.RefreshAll();
            Check(knight.UnitDetermination==null&&knight.UnitAggression==null,"solo Knight keeps protective normal settings");
            var other=Add(partner=="bird"?WildSpawnType.followerBirdEye:partner=="ordinary"?WildSpawnType.pmcUSEC:WildSpawnType.followerBigPipe);
            if(partner=="dead")other.Bot.IsDead=true;
            if(partner=="inactive")other.Bot.BotState=EBotState.Inactive;
            if(partner=="foreign")other.Leader=new pitAIBossPlayer();
            if(partner=="unowned")GoonCombatLayer.Owned.Remove(other.Bot);
            other.Bot.Position=new(500,0,0);GoonUnitTactics.RefreshAll();
            bool unit=partner is "pipe" or "bird";
            Check(knight.UnitDetermination==(unit?100f:null)&&knight.UnitAggression==(unit?70f:null),"only another living managed Goon activates Knight's unit stance: "+partner);
            Check(other.UnitDetermination==(unit?100f:null),"unit membership excludes foreign, ordinary and unready actors: "+partner);
            Check(other.CompanionLeader==(unit?knight.Bot:null),"the other Goon receives Knight as unit leader, not only independence percentages: "+partner);
            if(!unit)continue;
            other.CommandOverride=true;knight.Bot.BotState=EBotState.Inactive;GoonUnitTactics.RefreshAll();
            Check(other.UnitDetermination==null&&other.CommandOverride&&other.DeathResets==0,"temporary Knight unavailability is not a death or command reset");
            Check(other.CompanionLeader==null,"temporary loss releases the unit anchor as well as percentages");
            knight.Bot.IsDead=true;GoonUnitTactics.RefreshAll();
            Check(!other.CommandOverride&&other.DeathResets==1,"confirmed death after an inactive gap still restores saved member settings");
            knight.Bot.IsDead=false;
            knight.Bot.BotState=EBotState.Active;GoonUnitTactics.RefreshAll();
            Check(other.UnitDetermination==100,"unit resumes when the living leader returns");
            knight.Bot.GetPlayer.HealthController.IsAlive=false;
            GoonUnitTactics.Release(knight.Bot);GoonUnitTactics.RefreshAll();
            Check(other.UnitDetermination==null&&!other.CommandOverride&&other.DeathResets==2,"confirmed Knight death restores member settings even after leader layer release");
            other.CommandOverride=true;GoonUnitTactics.RefreshAll();
            Check(other.CommandOverride&&other.DeathResets==2,"Knight death is applied once and cannot erase later player commands");
            knight.Bot.GetPlayer.HealthController.IsAlive=true;GoonUnitTactics.RefreshAll();
            other.Bot.IsDead=true;GoonUnitTactics.Release(other.Bot);GoonUnitTactics.RefreshAll();
            Check(knight.UnitDetermination==null&&knight.UnitAggression==null,"last partner's death restores solo Knight behavior");
        }
        GoonUnitTactics.Reset();BossPlayers.Followers.Clear();GoonCombatLayer.Owned.Clear();
        var local=new pitAIBossPlayer();
        var trio=new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye}.Select(role=>
        {
            var bot=new BotOwner();bot.Profile.Info.Settings.Role=role;GoonCombatLayer.Owned.Add(bot);
            var f=new BotFollowerPlayer{Bot=bot,Leader=local};BossPlayers.Followers.Add(f);return f;
        }).ToArray();
        GoonUnitTactics.RefreshAll();trio[1].Bot.IsDead=true;GoonUnitTactics.Release(trio[1].Bot);GoonUnitTactics.RefreshAll();
        Check(trio[0].UnitDetermination==100&&trio[2].UnitDetermination==100,"losing one of two partners retains Knight/Birdeye unit behavior");
        trio[0].CommandOverride=trio[2].CommandOverride=true;GoonUnitTactics.Reset();
        Check(trio[0].UnitDetermination==null&&trio[2].UnitDetermination==null&&trio[0].CommandOverride&&trio[2].CommandOverride,
            "mode/raid teardown removes only the addon baseline, not player command state");
        GoonUnitTactics.RefreshAll();GameplayModeAddons.Current=GameplayMode.Allegiance;GoonUnitTactics.RefreshAll();
        Check(trio[0].UnitDetermination==null&&trio[2].UnitDetermination==null&&trio[2].CommandOverride,
            "mode switching clears unit stance even before native layer teardown");
        GameplayModeAddons.Current=GameplayMode.GoingRogue;
        GoonUnitTactics.Reset();BossPlayers.Followers.Clear();GoonCombatLayer.Owned.Clear();
    }
}
