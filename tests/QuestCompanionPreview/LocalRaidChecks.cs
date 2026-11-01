using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.UI.Matchmaker;
using HarmonyLib;
using pitTeam.Modules;
using pitTeam.Patches;

internal static class LocalRaidChecks
{
    internal static void Run(Action<bool,string> check, Profile profile, RaidSettings settings)
    {
        settings.RaidMode=ERaidMode.Local;
        var offline=new RaidSettings {IsPmc=true,LocationId="woods",RaidMode=ERaidMode.Online};
        // Native Ready resets IsPveOffline and applies this stored copy. This is
        // the failure that slipped past preview-only fixtures: no invited member
        // exists, and the quest visual has already been removed on Close.
        settings.IsPveOffline=false;
        settings.RaidMode=offline.RaidMode;
        var app=new TarkovApplication(settings,profile);
        app.OnReadyToStartMatchingAsync().GetAwaiter().GetResult();
        check(app.NetworkCalls==1 && !settings.Local,"unpatched native branch reproduces the recorded busy-server route after offline-settings restoration");
        app.NetworkCalls=0;
        new TarkovApplicationLocalRaidStartPatch().Enable();
        app.OnReadyToStartMatchingAsync().GetAwaiter().GetResult();
        check(app.LocalCalls==1 && app.NetworkCalls==0 && settings.IsPveOffline,
            "real Harmony launch prefix prevents recorded network matching for solo quest Knight after visual cleanup");
        check(MainMenuControllerPatch.GroupPlayers.Count==0 && CompanionRaidPreparation.QuestProfile==null,
            "quest launch guard needs neither invited membership nor a cached visual/profile");
        settings.RaidMode=ERaidMode.Online;
        check(SyntheticTeammateRaidGuard.KeepLocal(profile,settings,offline) && settings.Local && offline.Local,
            "confirmation keeps both native settings copies local before Ready restores the offline copy");
        check(settings.LocationId=="woods" && offline.LocationId=="woods",
            "local-mode normalization preserves map selection");
        for(int pass=0;pass<4;pass++)
        {
            settings.RaidMode=ERaidMode.Online;offline.RaidMode=ERaidMode.Online;
            SyntheticTeammateRaidGuard.KeepLocal(profile,settings,offline);
            settings.IsPveOffline=false;settings.RaidMode=offline.RaidMode;
            app.OnReadyToStartMatchingAsync().GetAwaiter().GetResult();
        }
        check(app.LocalCalls==5 && app.NetworkCalls==0,"repeated Back/Next settings restoration never sends an eligible companion raid online");
        void NativeUnchanged(string why)
        {
            settings.RaidMode=ERaidMode.Online;settings.IsPveOffline=false;offline.RaidMode=ERaidMode.Coop;
            int network=app.NetworkCalls;
            check(!SyntheticTeammateRaidGuard.KeepLocal(profile,settings,offline) && offline.RaidMode==ERaidMode.Coop,why+" preserves native settings");
            app.OnReadyToStartMatchingAsync().GetAwaiter().GetResult();
            check(app.NetworkCalls==network+1 && !settings.Local && !settings.IsPveOffline,why+" cannot inherit a previous quest launch");
        }
        profile.QuestsData[0].CompletedConditions.Add(new MongoID(pitTeam.GoingRogue.Quests.KnightQuestPolicy.WoodsKills));
        NativeUnchanged("completed objective on the selected map");
        profile.QuestsData[0].CompletedConditions.Clear();
        settings.LocationId="factory";NativeUnchanged("back to a non-quest map");settings.LocationId="woods";
        GameplayModeRuntime.IsGoingRogue=false;NativeUnchanged("switch to Allegiance");GameplayModeRuntime.IsGoingRogue=true;
        settings.IsPmc=false;NativeUnchanged("Scav side");settings.IsPmc=true;
        profile.QuestsData[0].Status=EFT.Quests.EQuestStatus.Success;NativeUnchanged("completed quest");profile.QuestsData[0].Status=EFT.Quests.EQuestStatus.Started;
        profile.Trader.Disabled=true;NativeUnchanged("disabled Knight");profile.Trader.Disabled=false;
        profile.Info.Level=19;NativeUnchanged("below level 20");profile.Info.Level=20;
        app.Session.Profile=null;settings.RaidMode=ERaidMode.Online;
        int before=app.NetworkCalls;app.OnReadyToStartMatchingAsync().GetAwaiter().GetResult();
        check(app.NetworkCalls==before+1,"missing session profile cannot reuse cached quest admission");app.Session.Profile=profile;
        foreach(bool goingRogue in new[]{true,false})
        {
            GameplayModeRuntime.IsGoingRogue=goingRogue;
            MainMenuControllerPatch.GroupPlayers.Add(new RaidPlayer(new(){Id="invited",AccountId="saved-member"}));
            settings.RaidMode=ERaidMode.Online;offline.RaidMode=ERaidMode.Online;
            check(SyntheticTeammateRaidGuard.KeepLocal(profile,settings,offline) && settings.Local && offline.Local,
                "inviting a saved teammate retains the common local-raid policy in both modes");
            int local=app.LocalCalls;app.OnReadyToStartMatchingAsync().GetAwaiter().GetResult();
            check(app.LocalCalls==local+1,"saved teammate starts through the native local branch");
            MainMenuControllerPatch.GroupPlayers.Clear();
        }
        GameplayModeRuntime.IsGoingRogue=true;
        check(!SyntheticTeammateRaidGuard.TryForceLocalRaid(null,"test"),"missing application is safe");
        check(!SyntheticTeammateRaidGuard.TryForceLocalRaid(new TarkovApplication(null,profile),"test"),"missing raid settings is safe");
    }
}

namespace EFT
{
    public class Session {public Profile Profile;}
    public class TarkovApplication
    {
        private readonly RaidSettings _raidSettings;
        public Session Session;public int LocalCalls,NetworkCalls;
        public TarkovApplication(RaidSettings settings,Profile profile){_raidSettings=settings;Session=new(){Profile=profile};}
        public Session GetClientBackEndSession()=>Session;
        // Same native branch selector verified in installed MoveNext. No Unity,
        // HTTP or real raid is started by this host.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public Task OnReadyToStartMatchingAsync()
        {
            if(_raidSettings.Local)LocalCalls++;else NetworkCalls++;
            return Task.CompletedTask;
        }
    }
}
namespace SPT.Reflection.Patching
{
    [AttributeUsage(AttributeTargets.Method)]public class PatchPrefixAttribute:Attribute{}
    public abstract class ModulePatch
    {
        protected abstract MethodBase GetTargetMethod();
        public void Enable()=>new Harmony("fixture.localraid").Patch(GetTargetMethod(),prefix:new HarmonyMethod(GetType().GetMethods(BindingFlags.NonPublic|BindingFlags.Static).Single(m=>m.IsDefined(typeof(PatchPrefixAttribute)))));
    }
}
namespace pitTeam
{
    internal static class pitFireTeam {internal static FixtureLog Log=new();}
    internal class FixtureLog {internal void LogWarning(string message){}}
}
