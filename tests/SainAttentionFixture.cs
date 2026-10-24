using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.SAINAddon;
using SAIN.Preset.Shared.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace pitTeam.Components {
    public partial class pitAIBossPlayer {
        private float _lastAttentionCommandAt=-999;
        public Action AttentionClearing;
        public int AttentionCalls;
        public void AttentionFixture()=>HandleAttentionCommand();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void HandleAttentionCommand() {
            float now=Time.time;
            if(now-_lastAttentionCommandAt<.35f)return;
            _lastAttentionCommandAt=now;
            AttentionClearing?.Invoke();
            AttentionCalls++;
            foreach(var owner in Followers) {
                owner.Sain.GoalEnemy=null;
                owner.Sain.EnemyController.KnownEnemies.Clear();
                owner.Memory.GoalEnemy=null;
                SainAddonBridge.TryForceReleaseFollowerCombatState(owner);
            }
        }
    }
}
public static partial class CombatChecks {
    private static Enemy Rehear(BotOwner bot,string id) {
        var enemy=new Enemy{Seen=false,Heard=true};
        enemy.EnemyPlayer.ProfileId=id;enemy.Hearing.EnemyHeardFromPeace=true;
        enemy.KnownPlaces.LastKnownPosition=new Vector3(10,0,0);
        enemy.KnownPlaces.LastHeardPlace=enemy.KnownPlaces.LastKnownPlace=
            new EnemyPlace{SoundType=SAINSoundType.FootStep,Position=new Vector3(10,0,0)};
        bot.Sain.GoalEnemy=enemy;
        bot.Sain.EnemyController.KnownEnemies.Add(enemy);
        bot.BotsGroup.Enemies[enemy.EnemyPlayer]=new BotGroupEnemyInfo();
        return enemy;
    }
    private static void TestAttentionIgnore() {
        SainAttentionBridge.Apply(new Harmony("xyz.pit.fireteam.sainaddon"));
        var first=PreparationBot("attentionFirst",new Vector3(20,0,0),ECombatDecision.Freeze);
        var second=PreparationBot("attentionSecond",new Vector3(20,0,0),ECombatDecision.SeekCover);
        var a=first.Sain.GoalEnemy;var b=second.Sain.GoalEnemy;
        a.EnemyPlayer.ProfileId="attentionA";b.EnemyPlayer.ProfileId="attentionB";
        var boss=new pitAIBossPlayer();boss.Followers.Add(first);boss.Followers.Add(second);
        boss.AttentionClearing=()=>{
            Check(SAINFollowerRuntime.IsAttentionContactIgnored(first,a)&&SAINFollowerRuntime.IsAttentionContactIgnored(second,b),
                "accepted Attention captures both followers before shared native contacts are removed");
        };
        boss.AttentionFixture();boss.AttentionClearing=null;
        a=Rehear(first,"attentionA");b=Rehear(second,"attentionB");
        foreach(var d in new[]{ECombatDecision.Freeze,ECombatDecision.SeekCover,ECombatDecision.ShiftCover}) {
            first.Sain.Decision.Manager.Publish(d);second.Sain.Decision.Manager.Publish(d);
            Check(first.Sain.Decision.CurrentCombatDecision==ECombatDecision.None&&second.Sain.Decision.CurrentCombatDecision==ECombatDecision.None,
                "Attention blocks fresh heard-only preparation of recreated contacts: "+d);
        }
        Check(first.Sain.GoalEnemy==a&&second.Sain.GoalEnemy==b,"ignored hearing does not delete living native contact memory");
        Time.time+=30;
        first.Sain.Decision.Manager.Publish(ECombatDecision.SeekCover);
        Check(first.Sain.Decision.CurrentCombatDecision==ECombatDecision.None,"Attention ignore does not expire with time");
        float anchor=first.Leader.Position.x;
        first.GetPlayer.Position=new Vector3(100,0,0);
        Check(SAINFollowerRuntime.IsAttentionContactIgnored(first,a),"follower movement cannot unlock a player-sector ignore");
        first.Leader.Position=new Vector3(anchor+10,0,0);
        Check(SAINFollowerRuntime.IsAttentionContactIgnored(first,a),"exact Core sector boundary retains the ignore");
        first.Leader.Position=new Vector3(anchor+10.1f,0,0);Tick();
        first.Leader.Position=new Vector3(anchor,0,0);
        first.GetPlayer.Position=Vector3.zero;
        first.Sain.Decision.Manager.Publish(ECombatDecision.Freeze);
        Check(first.Sain.Decision.CurrentCombatDecision==ECombatDecision.Freeze,"periodic sector refresh releases the ignore even before another sound and does not relock on return");
        Check(SAINFollowerRuntime.IsAttentionContactIgnored(second,b),"another follower's player anchor is independent");

        var fresh=Rehear(second,"attentionNew");
        second.Sain.Decision.Manager.Publish(ECombatDecision.Freeze);
        Check(second.Sain.Decision.CurrentCombatDecision==ECombatDecision.Freeze,"new hostile identity remains eligible inside the ignored sector");
        second.Sain.GoalEnemy=b;
        second.Memory.GoalEnemy=new EnemyInfo{ProfileId=b.EnemyProfileId};
        Check(SAINFollowerCombatHandoff.AllowsDecision(second.Sain,ECombatDecision.StandAndShoot,ESelfActionType.None,b),
            "accepted combat overrides the heard-only ignore");
        second.Memory.GoalEnemy=null;
        Check(SAINFollowerCombatHandoff.AllowsDecision(second.Sain,ECombatDecision.AvoidGrenade,ESelfActionType.None,b),
            "Attention ignore never blocks urgent grenade avoidance");
        second.Follower.CombatIndependent=true;
        Check(SAINFollowerCombatHandoff.AllowsDecision(second.Sain,ECombatDecision.Search,ESelfActionType.None,b),
            "explicit independent combat retains its ordinary admission");
        second.Follower.CombatIndependent=false;

        Time.time+=1;boss.AttentionFixture();
        var unrecorded=Rehear(second,"debouncedContact");
        boss.AttentionFixture();
        Check(boss.AttentionCalls==2&&!SAINFollowerRuntime.IsAttentionContactIgnored(second,unrecorded),
            "debounced Attention cannot silently arm another ignore");
        Check(SAINFollowerRuntime.IsAttentionContactIgnored(second,b),"repeated accepted Attention preserves previously dismissed identities");

        CombatDistanceConfiguration.Instance.Factory=true;
        var factory=PreparationBot("attentionFactory",new Vector3(20,0,0),ECombatDecision.Freeze);
        var factoryEnemy=factory.Sain.GoalEnemy;SAINFollowerRuntime.RememberAttentionContacts(factory);
        anchor=factory.Leader.Position.x;
        factory.Leader.Position=new Vector3(anchor+8,0,0);
        Check(SAINFollowerRuntime.IsAttentionContactIgnored(factory,factoryEnemy),"Factory/Labs exact eight-metre boundary remains ignored");
        factory.Leader.Position=new Vector3(anchor+8.01f,0,0);
        Check(!SAINFollowerRuntime.IsAttentionContactIgnored(factory,factoryEnemy),"Factory/Labs sector change uses eight metres");
        CombatDistanceConfiguration.Instance.Factory=false;

        SAINFollowerRuntime.RememberAttentionContacts(second);
        second.Follower.CombatTactic=FollowerCombatTactic.Balanced;Tick();
        Check(!SAINFollowerRuntime.IsAttentionContactIgnored(second,b),"tactic opt-out clears addon Attention memory");
        second.Follower.CombatTactic=FollowerCombatTactic.SainMan;Tick();
        SAINFollowerRuntime.RememberAttentionContacts(second);
        SainAddonBridge.RaiseFollowerLifecycleEvent(second,FollowerLifecycleEvent.OnDismiss);
        Check(!SAINFollowerRuntime.IsAttentionContactIgnored(second,b),"dismissal clears addon Attention memory");
    }
}
