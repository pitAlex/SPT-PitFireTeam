using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;
using EFT.Quests;
using HarmonyLib;
using pitTeam.GoingRogue.Quests;
using pitTeam.Modules;
using UnityEngine;

internal static class QuestRuntimeChecks
{
    private static int checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    public static void Main()
    {
        new Harmony("pitTeam.fixture.knight-quests").PatchAll(typeof(KnightQuestRuntime).Assembly);
        var world = new GameWorld(); Singleton<GameWorld>.Instance = world;
        var player = new Player { ProfileId = "human", IsYourPlayer = true, GameWorld = world };
        player.Profile.Id = player.ProfileId;
        player.QuestController.Profile = player.Profile;
        world.MainPlayer = player;
        var knight = new Player { ProfileId = "knight", GameWorld = world };
        knight.Profile.Id = knight.ProfileId; knight.Profile.Info.Settings.Role = WildSpawnType.bossKnight;
        var owner = new BotOwner { ProfileId = knight.ProfileId, Profile = knight.Profile, GetPlayer = knight };
        var companion = new Follower { Bot = owner, Boss = new Leader { realPlayer = player } };
        BossPlayers.Followers.Add(companion);
        GameplayModeRuntime.IsAllegiance = true;
        KnightQuestRuntime.RegisterKnight(owner, player);
        var first = new Quest { Id = KnightQuestPolicy.Competition };
        var second = new Quest { Id = KnightQuestPolicy.MyLand };
        var third = new Quest { Id = KnightQuestPolicy.Thieves };
        var ordinary = new Quest { Id = "ordinary" };
        player.QuestController.Quests.AddRange(new[] { first, second, third, ordinary });
        TaskConditionCounter Add(Quest quest, string id)
        {
            var counter = new TaskConditionCounter { Id = id, Conditional = quest };
            quest.ConditionCountersManager.Counters.Add(counter); return counter;
        }
        var woods = Add(first, KnightQuestPolicy.WoodsKills);
        var myLand = Add(second, KnightQuestPolicy.MyLandKills);
        var unrelated = Add(ordinary, "ordinary-counter");
        var failure = Add(first, KnightQuestPolicy.CompetitionFailure);
        var myLandFailure = Add(second, KnightQuestPolicy.MyLandFailure);
        void Tick(TaskConditionCounter counter) => ConditionCounterManager.Test(1, counter, new ConditionCheck[0]);
        Tick(woods); Check(woods.Value == 1, "human kill admitted with nearby owned living Knight");
        owner.Position = new Vector3 { X = 80 }; Tick(woods); Check(woods.Value == 2, "80m boundary included");
        owner.Position.X = 80.01f; Tick(woods); Check(woods.Value == 2, "outside 80m cannot credit player kill");
        owner.Position.X = 0;
        foreach (bool living in new[] { false, true })
        {
            owner.IsDead = !living; player.HealthController.IsAlive = living;
            Tick(woods);
            Check(woods.Value == (living ? 3 : 2), "living companion and player gate");
        }
        player.QuestController.Profile = new Profile { Id = "ordinary-follower" };
        Tick(woods); Tick(myLand);
        Check(woods.Value == 3 && myLand.Value == 0, "Core forwarded ordinary follower kills cannot count for either task");
        player.QuestController.Profile = knight.Profile;
        pitTeam.Patches.PlayerKilledPatch.IsForwardingRosterKill = true;
        Tick(woods); Tick(myLand); Tick(unrelated);
        Check(woods.Value == 3 && myLand.Value == 0 && unrelated.Value == 1,
            "generic roster kill reaches ordinary counters while Knight companion counters wait for their own death handler");
        pitTeam.Patches.PlayerKilledPatch.IsForwardingRosterKill = false;
        unrelated.Value = 0;
        Tick(woods); Tick(myLand);
        Check(woods.Value == 3 && myLand.Value == 1, "Knight attribution excluded from personal task but admitted for team task");
        player.QuestController.Profile = player.Profile;
        GameplayModeRuntime.IsAllegiance = false;
        Tick(woods); Tick(myLand); Tick(failure); Tick(unrelated);
        Check(woods.Value == 3 && myLand.Value == 1 && failure.Value == 0 && unrelated.Value == 1, "other mode leaves custom progress/failure untouched and ordinary counters working");
        GameplayModeRuntime.IsAllegiance = true;
        player.HealthController.IsAlive = false; owner.IsDead = true; Tick(failure);
        Check(failure.Value == 1, "native player exit failure can count after player/Knight death");
        player.HealthController.IsAlive = true; owner.IsDead = false;
        TaskConditionCounter ai = new TaskConditionCounter { Id = KnightQuestPolicy.WoodsKills, Conditional = new Quest() };
        Tick(ai); Check(ai.Value == 0, "non-local conditional cannot gain custom credit");

        Player Victim(string id, bool inside = true) => new Player { ProfileId = id, GameWorld = world, TriggerZones = inside ? new List<string> { "prapor_27_1" } : new List<string>() };
        var damage = new EFT.Ballistics.DamageInfo { Weapon = new object() };
        var victim = Victim("scav-one");
        victim.OnBeenKilledByAggressor(knight, damage, EBodyPart.Head);
        Check(myLand.Value == 2 && unrelated.Value == 1 && woods.Value == 3, "actual death hook credits only My Land from Knight kill");
        victim.OnBeenKilledByAggressor(knight, damage, EBodyPart.Head);
        Check(myLand.Value == 2, "repeated callback cannot double credit same victim");
        Victim("outside", false).OnBeenKilledByAggressor(knight, damage, EBodyPart.Head);
        Check(myLand.Value == 2, "victim zone copied into native condition checks");
        Victim("other-killer").OnBeenKilledByAggressor(new Player { ProfileId = "pipe" }, damage, EBodyPart.Head);
        Check(myLand.Value == 2, "Pipe or unrelated bot cannot receive Knight credit");
        owner.Position.X = 90; Victim("too-far").OnBeenKilledByAggressor(knight, damage, EBodyPart.Head);
        Check(myLand.Value == 2, "remote Knight kill not forwarded"); owner.Position.X = 0;
        player.Location = "woods";
        first.QuestStatus = EQuestStatus.AvailableForFinish;
        knight.RemoveFollowersOnDeath = true;
        knight.OnBeenKilledByAggressor(player, damage, EBodyPart.Head);
        Check(first.QuestStatus == EQuestStatus.Fail && second.QuestStatus == EQuestStatus.Started,
            "Knight death captured before cleanup fails ready task on its map, without failing task on another map");
        Check(BossPlayers.Followers.Count == 0 && player.QuestController.Profile == player.Profile, "death cleanup and profile ownership untouched");
        KnightQuestRuntime.Reset(); BossPlayers.Followers.Add(companion); knight.HealthController.IsAlive = true;
        first.QuestStatus = EQuestStatus.Started; player.Location = "bigmap";
        knight.OnBeenKilledByAggressor(player, damage, EBodyPart.Head);
        Check(second.QuestStatus == EQuestStatus.Fail && first.QuestStatus == EQuestStatus.Started, "Customs death fails My Land only");
        Check(third.QuestStatus == EQuestStatus.Started && KnightQuestPolicy.Owns(third.Id) &&
            !KnightQuestPolicy.UsesMap(third.Id, "TarkovStreets"), "retrieval task remains mode-owned without companion spawning or Knight-death failure");
        Check(KnightQuestPolicy.UsesMap(KnightQuestPolicy.Competition, "LIGHTHOUSE") && !KnightQuestPolicy.UsesMap(KnightQuestPolicy.Competition, "factory4_day"), "map matching is case insensitive and scoped");
        KnightQuestRuntime.Reset();
        BossPlayers.Followers.Clear();
        int previousKills = woods.Value, previousFailure = failure.Value;
        Tick(woods); Tick(myLand); Tick(failure);
        Check(woods.Value == previousKills && failure.Value == previousFailure, "new raid with ordinary teammate and no Knight cannot advance kills or failure");
        GameplayModeRuntime.IsAllegiance = false;
        KnightQuestRuntime.RegisterKnight(owner, player);
        Tick(woods); Tick(failure);
        Check(woods.Value == previousKills && failure.Value == previousFailure, "other mode cannot register quest Knight or advance counters");
        GameplayModeRuntime.IsAllegiance = true;
        KnightQuestRuntime.RegisterKnight(owner, player);
        Tick(failure);
        Check(failure.Value == previousFailure + 1, "participant marker preserves native exit failure after follower cleanup");
        player.GameWorld = world = new GameWorld { MainPlayer = player }; Singleton<GameWorld>.Instance = world;
        Tick(failure);
        Check(failure.Value == previousFailure + 1, "a different raid cannot inherit prior Knight participation");
        KnightQuestRuntime.Reset(); BossPlayers.Followers.Clear();
        player.HealthController.IsAlive = false;
        int firstBeforeDeath = failure.Value, secondBeforeDeath = myLandFailure.Value;
        Tick(failure); Tick(myLandFailure);
        Check(failure.Value == firstBeforeDeath && myLandFailure.Value == secondBeforeDeath,
            "dead player without Knight participation cannot advance either native failure counter");
        KnightQuestRuntime.RegisterKnight(owner, player);
        Tick(failure); Tick(myLandFailure);
        Check(failure.Value == firstBeforeDeath + 1 && myLandFailure.Value == secondBeforeDeath + 1,
            "player death still reaches both native failure counters when Knight participated, even after follower cleanup");
        player.GameWorld = world = new GameWorld { MainPlayer = player }; Singleton<GameWorld>.Instance = world;
        Tick(failure); Tick(myLandFailure);
        Check(failure.Value == firstBeforeDeath + 1 && myLandFailure.Value == secondBeforeDeath + 1,
            "a later raid without Knight cannot inherit participation and fail on player death");
        player.HealthController.IsAlive = true;
        KnightQuestRuntime.Reset();
        player.Location = "TarkovStreets";
        var badge = new Quest { Id=KnightQuestPolicy.BadgeBandit };
        player.QuestController.Quests.Add(badge);
        var badgeKills = Add(badge, KnightQuestPolicy.BadgeBanditKills);
        var badgeFailure = Add(badge, KnightQuestPolicy.BadgeBanditFailure);
        void BadgeTick() => ConditionCounterManager.Test(1,badgeKills,new[]{new ConditionCheck(typeof(ConditionKills)).Test(new Hit{Target="Savage",Role="bossKolontay"})});
        BadgeTick(); Check(badgeKills.Value==0,"solo player without Goons cannot get Badge Bandit credit");
        foreach (var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye})
        {
            KnightQuestRuntime.Reset(); BossPlayers.Followers.Clear(); badge.QuestStatus=EQuestStatus.Started;
            var goonPlayer=new Player{ProfileId="goon-"+role,GameWorld=world,Location="TarkovStreets"};
            var goon=new BotOwner{ProfileId=goonPlayer.ProfileId,GetPlayer=goonPlayer,Profile=goonPlayer.Profile};goon.Profile.Info.Settings.Role=role;
            goonPlayer.Profile.Info.Settings.Role=role;
            BossPlayers.Followers.Add(new Follower{Bot=goon,Boss=new Leader{realPlayer=player}});
            KnightQuestRuntime.RegisterKnight(goon,player);
            int start=badgeKills.Value;BadgeTick();Check(badgeKills.Value==start+1,"player can kill Kollontay with nearby "+role);
            goon.Position=goonPlayer.Position=new Vector3{X=81};BadgeTick();Check(badgeKills.Value==start+1,"a distant Goon cannot admit player credit");goon.Position=goonPlayer.Position=new Vector3();
            var kollontay=Victim("kolontay-"+role,false);kollontay.Side=EPlayerSide.Savage;kollontay.Profile.Info.Settings.Role=WildSpawnType.bossKolontay;
            kollontay.OnBeenKilledByAggressor(goonPlayer,new EFT.Ballistics.DamageInfo{Weapon=new object()},EBodyPart.Head);
            Check(badgeKills.Value==start+2,"native Savage/Kollontay input credits "+role+" kill");
            int counted=badgeKills.Value;
            Victim("scav-"+role,false).OnBeenKilledByAggressor(goonPlayer,new EFT.Ballistics.DamageInfo{Weapon=new object()},EBodyPart.Head);
            Check(badgeKills.Value==counted,"ordinary victims do not satisfy Kollontay role condition");
            goonPlayer.OnBeenKilledByAggressor(player,new EFT.Ballistics.DamageInfo(),EBodyPart.Head);
            Check(badge.QuestStatus==EQuestStatus.Fail,"death of "+role+" fails Badge Bandit");
        }
        KnightQuestRuntime.Reset();BossPlayers.Followers.Clear();
        Tick(badgeFailure);Check(badgeFailure.Value==0,"unaccompanied death cannot fail Badge Bandit");
        var registeredPipe=new BotOwner{ProfileId="registered-pipe",Profile=new Profile()};
        registeredPipe.Profile.Info.Settings.Role=WildSpawnType.followerBigPipe;
        KnightQuestRuntime.RegisterKnight(registeredPipe,player);
        player.HealthController.IsAlive=false;
        Tick(badgeFailure);Check(badgeFailure.Value==1,"player death after Pipe registration reaches native failure even after cleanup");
        player.GameWorld=world=new GameWorld{MainPlayer=player};Singleton<GameWorld>.Instance=world;
        Tick(badgeFailure);Check(badgeFailure.Value==1,"next raid without Goons cannot inherit Badge Bandit failure participation");
        KnightQuestRuntime.RegisterKnight(registeredPipe,player);GameplayModeRuntime.IsAllegiance=false;
        Tick(badgeFailure);Check(badgeFailure.Value==1,"another mode cannot fail Badge Bandit despite prior Goon participation");
        GameplayModeRuntime.IsAllegiance=true; player.HealthController.IsAlive=true; player.Location="woods";
        var spotted=new Quest{Id=KnightQuestPolicy.EnemySpotted}; player.QuestController.Quests.Add(spotted);
        var spottedKills=Add(spotted,KnightQuestPolicy.EnemySpottedKills);
        var spottedFailure=Add(spotted,KnightQuestPolicy.EnemySpottedFailure);
        void PlayerPmcKill() => ConditionCounterManager.Test(1,spottedKills,new[]{
            new ConditionCheck(typeof(ConditionKills)).Test(new Hit{Target="AnyPmc"}),new ConditionCheck(typeof(ConditionLocation)).Test("woods")});
        foreach(var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye})
        {
            KnightQuestRuntime.Reset();BossPlayers.Followers.Clear();spotted.QuestStatus=EQuestStatus.Started;
            var actor=new Player{ProfileId="spotter-"+role,GameWorld=world,Location="woods"};actor.Profile.Id=actor.ProfileId;actor.Profile.Info.Settings.Role=role;
            var bot=new BotOwner{ProfileId=actor.ProfileId,Profile=actor.Profile,GetPlayer=actor};
            BossPlayers.Followers.Add(new Follower{Bot=bot,Boss=new Leader{realPlayer=player}});KnightQuestRuntime.RegisterKnight(bot,player);
            int count=spottedKills.Value;PlayerPmcKill();Check(spottedKills.Value==count,"human kills cannot satisfy Birdeye-only objective with "+role);
            var pmc=Victim("pmc-"+role,false);pmc.Side=EPlayerSide.Bear;
            bot.Position=actor.Position=new Vector3{X=120};
            pmc.OnBeenKilledByAggressor(actor,damage,EBodyPart.Head);
            Check(spottedKills.Value==count+(role==WildSpawnType.followerBirdEye?1:0),"only Birdeye gets native PMC credit at legacy 120m boundary: "+role);
            count=spottedKills.Value;
            bot.Position=actor.Position=new Vector3{X=121};Victim("remote-"+role,false).OnBeenKilledByAggressor(actor,damage,EBodyPart.Head);
            Check(spottedKills.Value==count,"beyond-120m kills cannot advance Enemy Spotted");
            bot.Position=actor.Position=new Vector3{X=90};
            var scav=Victim("wrong-side-"+role,false);scav.Side=EPlayerSide.Savage;scav.OnBeenKilledByAggressor(actor,damage,EBodyPart.Head);
            Check(spottedKills.Value==count,"Scav kills cannot satisfy native AnyPmc condition");
            bot.Position=actor.Position=new Vector3{X=40};Victim("near-pmc-"+role,false).OnBeenKilledByAggressor(actor,damage,EBodyPart.Head);
            Check(spottedKills.Value==count+(role==WildSpawnType.followerBirdEye?1:0),"nearby Knight/Pipe kills still cannot replace Birdeye: "+role);
            count=spottedKills.Value;
            actor.Location="shoreline";Victim("wrong-map-"+role,false).OnBeenKilledByAggressor(actor,damage,EBodyPart.Head);
            Check(spottedKills.Value==count,"native Woods condition rejects kills on another map");actor.Location="woods";
            int failures=spottedFailure.Value;BossPlayers.Followers.Clear();player.HealthController.IsAlive=false;Tick(spottedFailure);player.HealthController.IsAlive=true;
            Check(spottedFailure.Value==failures+(role==WildSpawnType.followerBirdEye?1:0),"only Birdeye participation admits exit failure after cleanup: "+role);
            BossPlayers.Followers.Add(new Follower{Bot=bot,Boss=new Leader{realPlayer=player}});
            actor.RemoveFollowersOnDeath=true;actor.OnBeenKilledByAggressor(player,damage,EBodyPart.Head);
            Check((spotted.QuestStatus==EQuestStatus.Fail)==(role==WildSpawnType.followerBirdEye),"only Birdeye death fails Enemy Spotted, captured before cleanup: "+role);
        }
        KnightQuestRuntime.Reset();BossPlayers.Followers.Clear();int priorFail=spottedFailure.Value;Tick(spottedFailure);
        Check(spottedFailure.Value==priorFail,"a later unaccompanied raid cannot inherit Birdeye exit failures");
        Check(!KnightQuestPolicy.CanCredit(KnightQuestPolicy.EnemySpottedKills,false,true,true,40,false,true),"another mode cannot credit Birdeye's kills");
        KnightQuestRuntime.Reset();BossPlayers.Followers.Clear();BossPlayers.Followers.Add(companion);
        player.Location="bigmap";second.QuestStatus=EQuestStatus.Started;owner.Position=new Vector3();knight.HealthController.IsAlive=true;
        KnightQuestRuntime.RegisterKnight(owner,player);int landBefore=myLand.Value;
        var landScav=Victim("my-land-scav");landScav.Side=EPlayerSide.Savage;landScav.OnBeenKilledByAggressor(knight,damage,EBodyPart.Head);
        Check(myLand.Value==landBefore+1,"My Land retains native Any target credit for Knight's Scav kills");
        KnightQuestRuntime.Reset();BossPlayers.Followers.Clear();player.Location="shoreline";
        var pier=new Quest{Id=KnightQuestPolicy.OffLimits};player.QuestController.Quests.Add(pier);
        var pierKills=Add(pier,KnightQuestPolicy.OffLimitsKills);
        void PierPlayerKill(bool inside=true) => ConditionCounterManager.Test(1,pierKills,new[]{new ConditionCheck(typeof(ConditionKills)).Test(new Hit{Target="Any"}),
            new ConditionCheck(typeof(ConditionInZone)).Test(inside?new List<string>{KnightQuestPolicy.PierZone}:new List<string>())});
        PierPlayerKill();Check(pierKills.Value==0,"no pair cannot advance the pier objective");
        Follower PierGoon(WildSpawnType role)
        {
            var actor=new Player{ProfileId="pier-"+role,GameWorld=world,Location="shoreline"};actor.Profile.Id=actor.ProfileId;actor.Profile.Info.Settings.Role=role;
            var follower=new Follower{Bot=new BotOwner{ProfileId=actor.ProfileId,Profile=actor.Profile,GetPlayer=actor},Boss=new Leader{realPlayer=player}};
            BossPlayers.Followers.Add(follower);KnightQuestRuntime.RegisterKnight(follower.Bot,player);return follower;
        }
        var pipe=PierGoon(WildSpawnType.followerBigPipe);PierPlayerKill();Check(pierKills.Value==0,"Pipe alone does not substitute for the required pair");
        var bird=PierGoon(WildSpawnType.followerBirdEye);PierPlayerKill();Check(pierKills.Value==1,"human kill inside the pier counts with Pipe and Birdeye alive nearby");
        PierPlayerKill(false);Check(pierKills.Value==1,"victim outside the native pier zone does not count");
        player.QuestController.Profile=new Profile{Id="ordinary-teammate"};PierPlayerKill();Check(pierKills.Value==1,"ordinary teammate credit cannot replace human or Goon credit");player.QuestController.Profile=player.Profile;
        foreach(var member in new[]{pipe,bird})
        {
            int prior=pierKills.Value;var target=Victim("pier-target-"+member.Bot.ProfileId,false);target.Side=EPlayerSide.Savage;target.TriggerZones.Add(KnightQuestPolicy.PierZone);
            target.OnBeenKilledByAggressor(member.Bot.GetPlayer,damage,EBodyPart.Head);
            Check(pierKills.Value==prior+1,"native Any/InZone input credits "+member.Bot.ProfileId);
            target.OnBeenKilledByAggressor(member.Bot.GetPlayer,damage,EBodyPart.Head);Check(pierKills.Value==prior+1,"duplicate corpse callback cannot add another pier kill");
            Victim("outside-pier-"+member.Bot.ProfileId,false).OnBeenKilledByAggressor(member.Bot.GetPlayer,damage,EBodyPart.Head);
            Check(pierKills.Value==prior+1,"Goon kill outside pier zone cannot count");
        }
        int priorPier=pierKills.Value;bird.Bot.Position=bird.Bot.GetPlayer.Position=new Vector3{X=120};
        var edge=Victim("pier-sniper-edge",false);edge.TriggerZones.Add(KnightQuestPolicy.PierZone);edge.OnBeenKilledByAggressor(bird.Bot.GetPlayer,damage,EBodyPart.Head);
        Check(pierKills.Value==priorPier+1,"Birdeye's legacy 120m kill allowance works while Pipe remains nearby");
        bird.Bot.Position=bird.Bot.GetPlayer.Position=new Vector3{X=121};edge=Victim("pier-sniper-remote",false);edge.TriggerZones.Add(KnightQuestPolicy.PierZone);edge.OnBeenKilledByAggressor(bird.Bot.GetPlayer,damage,EBodyPart.Head);
        Check(pierKills.Value==priorPier+1,"remote Birdeye cannot bypass his personal 120m limit through nearby Pipe");
        bird.Bot.Position=bird.Bot.GetPlayer.Position=new Vector3();pipe.Bot.Position=pipe.Bot.GetPlayer.Position=new Vector3{X=81};
        edge=Victim("pier-pipe-remote",false);edge.TriggerZones.Add(KnightQuestPolicy.PierZone);edge.OnBeenKilledByAggressor(pipe.Bot.GetPlayer,damage,EBodyPart.Head);
        Check(pierKills.Value==priorPier+1,"remote Pipe cannot bypass his personal 80m limit through nearby Birdeye");
        bird.Bot.Position=new Vector3{X=81};PierPlayerKill();Check(pierKills.Value==priorPier+1,"human credit needs one member of the pair within 80m");
        pipe.Bot.Position=pipe.Bot.GetPlayer.Position=bird.Bot.Position=bird.Bot.GetPlayer.Position=new Vector3();
        var extraKnight=PierGoon(WildSpawnType.bossKnight);extraKnight.Bot.GetPlayer.OnBeenKilledByAggressor(player,damage,EBodyPart.Head);
        Check(pier.QuestStatus==EQuestStatus.Started,"Knight death does not fail Pipe/Birdeye's pier task");
        foreach(var member in new[]{pipe,bird})
        {
            KnightQuestRuntime.Reset();BossPlayers.Followers.Clear();BossPlayers.Followers.Add(pipe);BossPlayers.Followers.Add(bird);
            pipe.Bot.GetPlayer.HealthController.IsAlive=bird.Bot.GetPlayer.HealthController.IsAlive=true;
            KnightQuestRuntime.RegisterKnight(pipe.Bot,player);KnightQuestRuntime.RegisterKnight(bird.Bot,player);
            pier.QuestStatus=EQuestStatus.AvailableForFinish;member.Bot.GetPlayer.RemoveFollowersOnDeath=true;
            member.Bot.GetPlayer.OnBeenKilledByAggressor(player,damage,EBodyPart.Head);
            Check(pier.QuestStatus==EQuestStatus.Fail,"death of either required Goon fails the ready task before cleanup: "+member.Bot.ProfileId);
        }
        // No native exit-failure counter is registered for Off Limits. Only the
        // direct companion-death transitions above can fail it.
        pier.QuestStatus=EQuestStatus.Started;player.HealthController.IsAlive=false;
        foreach(string exit in new[]{"Killed","Left","MissingInAction","Survived"})
        {
            pier.ConditionCountersManager.Test(1,new ConditionCheck(typeof(ConditionExitStatus)).Test(exit));
            Check(pier.QuestStatus==EQuestStatus.Started,"player exit cannot fail Off Limits: "+exit);
        }
        player.GameWorld=world=new GameWorld{MainPlayer=player};Singleton<GameWorld>.Instance=world;
        Check(!KnightQuestPolicy.CanCredit(KnightQuestPolicy.OffLimitsKills,false,true,true,0,true,false),"other mode cannot credit the pier task");
        KnightQuestRuntime.Reset();BossPlayers.Followers.Clear();player.Location="lighthouse";player.HealthController.IsAlive=true;
        var hunt=new Quest{Id=KnightQuestPolicy.AFavor};player.QuestController.Quests.Add(hunt);
        var huntKills=Add(hunt,KnightQuestPolicy.AFavorKills);var huntFailure=Add(hunt,KnightQuestPolicy.AFavorFailure);
        void HuntPlayerKill(string map="lighthouse",string role="bossPartisan") => ConditionCounterManager.Test(1,huntKills,new[]{
            new ConditionCheck(typeof(ConditionKills)).Test(new Hit{Target="Savage",Role=role}),new ConditionCheck(typeof(ConditionLocation)).Test(map)});
        HuntPlayerKill();Check(huntKills.Value==0,"Partizan kill without Birdeye does not count");
        bird=PierGoon(WildSpawnType.followerBirdEye);bird.Bot.GetPlayer.Location="lighthouse";
        bird.Bot.Position=bird.Bot.GetPlayer.Position=new Vector3{X=120};
        HuntPlayerKill();Check(huntKills.Value==1,"player can kill Partizan with Birdeye within 120m");
        foreach(var role in new[]{WildSpawnType.bossKnight,WildSpawnType.followerBigPipe,WildSpawnType.followerBirdEye})
        {
            var member=role==WildSpawnType.followerBirdEye?bird:PierGoon(role);member.Bot.GetPlayer.Location="lighthouse";
            int before=huntKills.Value;
            var target=Victim("hunt-"+role,false);target.Side=EPlayerSide.Savage;target.Profile.Info.Settings.Role=WildSpawnType.bossPartisan;
            target.OnBeenKilledByAggressor(member.Bot.GetPlayer,damage,EBodyPart.Head);
            Check(huntKills.Value==before+(role==WildSpawnType.followerBirdEye?1:0),"only Birdeye forwarded kill counts: "+role);
            target.OnBeenKilledByAggressor(member.Bot.GetPlayer,damage,EBodyPart.Head);
            Check(huntKills.Value==before+(role==WildSpawnType.followerBirdEye?1:0),"Partizan death cannot double count");
        }
        int huntBefore=huntKills.Value;
        HuntPlayerKill("woods");HuntPlayerKill("lighthouse","bossKolontay");
        Check(huntKills.Value==huntBefore,"native Lighthouse/Partizan conditions reject another map or boss");
        bird.Bot.Position=bird.Bot.GetPlayer.Position=new Vector3{X=121};HuntPlayerKill();
        var remote=Victim("remote-partizan",false);remote.Side=EPlayerSide.Savage;remote.Profile.Info.Settings.Role=WildSpawnType.bossPartisan;
        remote.OnBeenKilledByAggressor(bird.Bot.GetPlayer,damage,EBodyPart.Head);
        Check(huntKills.Value==huntBefore,"nearby other Goons cannot bypass remote Birdeye admission");
        bird.Bot.Position=bird.Bot.GetPlayer.Position=new Vector3();
        player.QuestController.Profile=new Profile{Id="ordinary-follower"};HuntPlayerKill();player.QuestController.Profile=player.Profile;
        Check(huntKills.Value==huntBefore,"ordinary forwarded follower kills do not count for hunt");
        GameplayModeRuntime.IsAllegiance=false;HuntPlayerKill();Check(huntKills.Value==huntBefore,"other mode cannot progress hunt");GameplayModeRuntime.IsAllegiance=true;
        hunt.QuestStatus=EQuestStatus.AvailableForFinish;bird.Bot.GetPlayer.RemoveFollowersOnDeath=true;
        bird.Bot.GetPlayer.OnBeenKilledByAggressor(player,damage,EBodyPart.Head);
        Check(hunt.QuestStatus==EQuestStatus.Fail,"Birdeye death fails ready hunt before cleanup");
        int huntFails=huntFailure.Value;player.HealthController.IsAlive=false;Tick(huntFailure);
        Check(huntFailure.Value==huntFails+1,"player death fails accompanied hunt even after registry cleanup");
        KnightQuestRuntime.Reset();Tick(huntFailure);Check(huntFailure.Value==huntFails+1,"unaccompanied next raid cannot fail hunt");
        Console.WriteLine("PASS: " + checks + " Knight quest runtime/Harmony checks. Native quest/navigation behavior requires EFT qualification.");
    }
}

namespace Comfort.Common { public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace UnityEngine
{
    public struct Vector3 { public float X; public static float Distance(Vector3 a, Vector3 b) => Math.Abs(a.X - b.X); }
}
namespace EFT
{
    public enum EPlayerSide { Usec, Bear, Savage }
    public enum EBodyPart { Head }
    public enum WildSpawnType { assault, bossKnight, followerBigPipe, followerBirdEye, bossKolontay, bossPartisan }
    public interface IPlayer { string ProfileId { get; } }
    public class Settings { public WildSpawnType Role; }
    public class Info { public Settings Settings = new Settings(); }
    public class Profile { public string Id; public Info Info = new Info(); }
    public class Health { public bool IsAlive = true; public object BodyPartEffects; }
    public class Inventory { public List<string> EquippedInSlotsTemplateIds = new List<string>(); }
    public class GameWorld { public Player MainPlayer; }
    public class BotOwner { public string ProfileId; public Profile Profile; public bool IsDead; public Vector3 Position; public Player GetPlayer; }
    public class Player : IPlayer
    {
        public string ProfileId { get; set; }
        public EPlayerSide Side;
        public bool IsYourPlayer, RemoveFollowersOnDeath;
        public GameWorld GameWorld;
        public Profile Profile = new Profile();
        public Health HealthController = new Health();
        public Quests.QuestController QuestController = new Quests.QuestController();
        public string Location = "bigmap";
        public Vector3 Position;
        public int CurrentHour;
        public List<string> TriggerZones = new List<string>();
        public Inventory Inventory = new Inventory();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnBeenKilledByAggressor(IPlayer aggressor, Ballistics.DamageInfo damageInfo, EBodyPart bodyPart)
        {
            HealthController.IsAlive = false;
            if (RemoveFollowersOnDeath) BossPlayers.Followers.Clear();
        }
    }
}
namespace EFT.Ballistics { public class DamageInfo { public object Weapon; } }
namespace EFT.Quests
{
    public enum EQuestStatus { Started, AvailableForFinish, Fail }
    public class Quest
    {
        public string Id;
        public EQuestStatus QuestStatus;
        public ConditionCounterManager ConditionCountersManager = new ConditionCounterManager();
        public void SetStatus(EQuestStatus status, bool notify, bool fromServer) { QuestStatus = status; }
    }
    public class TaskConditionCounter { public string Id; public Quest Conditional; public int Value; }
    public class Hit { public string Target; public string Role; }
    public class ConditionKills { }
    public class ConditionLocation { }
    public class ConditionExitStatus { }
    public class ConditionInZone { }
    public struct ConditionCheck { public Type Type; public object Value; public ConditionCheck(Type type) { Type = type; Value = null; } public ConditionCheck Test(object value) { Value = value; return this; } }
    public class ConditionCounterManager
    {
        public List<TaskConditionCounter> Counters = new List<TaskConditionCounter>();
        public void Test(int value, params ConditionCheck[] checks) { foreach (var counter in Counters) Test(value, counter, checks); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Test(int valueToAdd, TaskConditionCounter counter, ConditionCheck[] checks)
        {
            if (counter.Id == KnightQuestPolicy.AFavorKills && (!checks.Any(c=>c.Type==typeof(ConditionKills) && c.Value is Hit hit && hit.Target=="Savage" && hit.Role=="bossPartisan") ||
                !checks.Any(c=>c.Type==typeof(ConditionLocation) && string.Equals(c.Value as string,"lighthouse",StringComparison.OrdinalIgnoreCase)))) return;
            if (counter.Id == KnightQuestPolicy.BadgeBanditKills && !checks.Any(c=>c.Type==typeof(ConditionKills) && c.Value is Hit hit && hit.Target=="Savage" && hit.Role=="bossKolontay")) return;
            if (counter.Id == KnightQuestPolicy.OffLimitsKills && (!checks.Any(c=>c.Type==typeof(ConditionKills) && c.Value is Hit hit && hit.Target=="Any") ||
                !checks.Any(c=>c.Type==typeof(ConditionInZone) && c.Value is List<string> zones && zones.Contains(KnightQuestPolicy.PierZone)))) return;
            if (counter.Id == KnightQuestPolicy.MyLandKills && checks.Any(c=>c.Type==typeof(ConditionKills)) && !checks.Any(c=>c.Type==typeof(ConditionKills) && c.Value is Hit hit && hit.Target=="Any")) return;
            if (counter.Id == KnightQuestPolicy.EnemySpottedKills && (!checks.Any(c=>c.Type==typeof(ConditionKills) && c.Value is Hit hit && hit.Target=="AnyPmc") ||
                !checks.Any(c=>c.Type==typeof(ConditionLocation) && string.Equals(c.Value as string,"woods",StringComparison.OrdinalIgnoreCase)))) return;
            if (counter.Id != KnightQuestPolicy.BadgeBanditKills && counter.Id != KnightQuestPolicy.EnemySpottedKills && counter.Id != KnightQuestPolicy.OffLimitsKills && counter.Id != KnightQuestPolicy.AFavorKills && checks.Any(c => c.Type == typeof(ConditionInZone) && !((List<string>)c.Value).Contains("prapor_27_1"))) return;
            counter.Value += valueToAdd;
        }
    }
    public class QuestController
    {
        public Profile Profile;
        public List<Quest> Quests = new List<Quest>();
        public object CheckHitConditionCounter(string target, List<string> equipment, object weapon, EBodyPart part, float distance, string role, int hour, object effects) => weapon == null ? null : new Hit { Target=target, Role=role };
    }
}
namespace pitTeam.Modules
{
    public static class GameplayModeRuntime { public static bool IsAllegiance; }
    public class Leader { public Player realPlayer; }
    public class Follower { public BotOwner Bot; public Leader Boss; public BotOwner GetBot() => Bot; public Leader GetBoss() => Boss; }
    public static class BossPlayers { public static List<Follower> Followers = new List<Follower>(); public static List<Follower> GetFollowers() => Followers; }
    public static class Logger { public static void LogError(string text) { throw new Exception(text); } public static void LogInfo(string text) {} }
}
namespace pitTeam.Patches {public static class PlayerKilledPatch {public static bool IsForwardingRosterKill;}}
