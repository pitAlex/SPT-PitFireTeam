// Real Harmony around production SAIN speech exceptions; native speech/Unity use stand-ins.
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using pitTeam;
using pitTeam.Modules;
using pitTeam.Patches;
using UnityEngine;

public static class NativeSpeech
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool BotPrefix(BotTalk __instance, EPhraseTrigger type) => !pitFireTeam.IsSAINInstalled;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool PlayerPrefix(Player __instance, EPhraseTrigger phrase) => !pitFireTeam.IsSAINInstalled || !__instance.IsAI;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool ManualPrefix(BotTalk __instance) => !pitFireTeam.IsSAINInstalled;
}
public class NativeSpeechPlayer
{
    public Player Player { get; set; }
    public int Outputs;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool PlayVoiceLine(EPhraseTrigger phrase) { Outputs++; return true; }
}
public class NativeSpeechTalk
{
    public BotOwner Owner;
    public NativeSpeechPlayer Output;
    public int Generated, Updates;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool Say(EPhraseTrigger phrase) { Generated++; return Output.PlayVoiceLine(phrase); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ManualUpdate() { Updates++; }
}
namespace pitTeam.Modules
{
    public static class SainBotOwnerAccessor
    {
        public static BotOwner Get(object instance) => (instance as NativeSpeechTalk)?.Owner;
    }
}
public static class RecruitmentSpeechChecks
{
    private static int count;
    private static readonly MethodInfo Reply = typeof(FollowRequestPatch).GetMethod("TrySayRecruitmentResponse", BindingFlags.NonPublic | BindingFlags.Static);
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
    private static MethodInfo Hook(string name) => typeof(SAINPatch).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
    public static void Install()
    {
        var harmony = new Harmony("pitFireTeam.tests.recruitment-speech");
        harmony.Patch(typeof(BotTalk).GetMethod("Say"), prefix: new HarmonyMethod(typeof(BotTalkSayPatch).GetMethod("PatchPrefix", BindingFlags.NonPublic | BindingFlags.Static)));
        foreach (string name in new[] { "BotPrefix", "PlayerPrefix", "ManualPrefix" })
            harmony.Patch(typeof(NativeSpeech).GetMethod(name), prefix: new HarmonyMethod(Hook("BypassSainTalkPatchForFollower")));
        harmony.Patch(typeof(NativeSpeechPlayer).GetMethod("PlayVoiceLine"), prefix: new HarmonyMethod(Hook("UseVanillaTalkForFollower")));
        harmony.Patch(typeof(NativeSpeechTalk).GetMethod("Say"), prefix: new HarmonyMethod(Hook("DisableSainTalkSayForFollower")));
        harmony.Patch(typeof(NativeSpeechTalk).GetMethod("ManualUpdate"), prefix: new HarmonyMethod(Hook("DisableSainTalkUpdateForFollower")));
    }
    public static int Run()
    {
        Time.time = 100f;
        pitFireTeam.IsSAINInstalled = true;
        var candidate = new BotOwner { ProfileId = "voice-candidate" };
        var other = new BotOwner { ProfileId = "other" };
        var output = new NativeSpeechPlayer { Player = candidate.GetPlayer };
        var talk = new NativeSpeechTalk { Owner = candidate, Output = output };
        Check(!NativeSpeech.BotPrefix(candidate.BotTalk, EPhraseTrigger.Negative), "Ordinary SAIN bot keeps native speech suppression");
        FollowerForcedPhraseGate.ArmRecruitmentResponse(candidate, EPhraseTrigger.Negative, 1.5f);
        candidate.BotTalk.SetSilence(0f);candidate.BotTalk.Say(EPhraseTrigger.Negative,true);
        Check(candidate.BotTalk.IsSilenced && candidate.GetPlayer.Spoken==EPhraseTrigger.None, "EFT zero-duration silence blocks same-frame immediate speech at the production gate");
        foreach (var phrase in new[] { EPhraseTrigger.Negative, EPhraseTrigger.DontKnow, EPhraseTrigger.Toxic })
        {
            candidate.BotTalk.QueueRequests = true;
            Reply.Invoke(null, new object[] { candidate, phrase, null });
            Check(candidate.BotTalk.Queued == 0 && candidate.GetPlayer.Spoken == phrase, "Exact reply traverses both SAIN blockers without queued EFT speech: " + phrase);
            Check(FollowerForcedPhraseGate.IsRecruitmentResponse(candidate, phrase), "Reply owns a timed candidate scope: " + phrase);
        }
        Check(!NativeSpeech.BotPrefix(candidate.BotTalk, EPhraseTrigger.Roger), "Unrelated candidate reply is not granted a bypass");
        Check(!NativeSpeech.PlayerPrefix(candidate.GetPlayer, EPhraseTrigger.Roger), "Unrelated Player.Say is not granted a bypass");
        Check(!NativeSpeech.ManualPrefix(candidate.BotTalk), "Candidate EFT ManualUpdate remains SAIN-owned");
        Check(!NativeSpeech.BotPrefix(other.BotTalk, EPhraseTrigger.Toxic), "Other bots cannot use the candidate scope");
        foreach (var phrase in new[] { EPhraseTrigger.MumblePhrase, EPhraseTrigger.OnMutter, EPhraseTrigger.OnFight })
        {
            Check(!talk.Say(phrase) && talk.Generated == 0, "New competing chatter never enters native cache: " + phrase);
            Check(!output.PlayVoiceLine(phrase) && output.Outputs == 0, "Already-pending chatter is stopped at final output: " + phrase);
        }
        Check(talk.Say(EPhraseTrigger.OnEnemyGrenade) && talk.Generated == 1 && output.Outputs == 1, "Native urgent warning remains available");
        Check(output.PlayVoiceLine(EPhraseTrigger.OnBeingHurt), "Native injury speech remains available");
        talk.ManualUpdate(); Check(talk.Updates == 1, "Candidate native talk update continues");
        var otherOutput = new NativeSpeechPlayer { Player = other.GetPlayer };
        Check(otherOutput.PlayVoiceLine(EPhraseTrigger.OnMutter), "Untargeted ordinary chatter is unchanged");
        Time.time += 1.51f;
        Check(!NativeSpeech.BotPrefix(candidate.BotTalk, EPhraseTrigger.Toxic), "Speech exception expires");
        Check(talk.Say(EPhraseTrigger.OnMutter), "Chatter resumes after deadline");
        FollowerForcedPhraseGate.Arm(candidate, EPhraseTrigger.Roger, 2f);
        Check(!NativeSpeech.BotPrefix(candidate.BotTalk, EPhraseTrigger.Roger), "General forced phrase cannot grant recruitment bypass");
        Check(output.PlayVoiceLine(EPhraseTrigger.OnMutter), "General forced phrase does not suppress ordinary native chatter");
        FollowerForcedPhraseGate.ArmRecruitmentResponse(candidate, EPhraseTrigger.Roger, 2.5f);
        candidate.BotTalk.Say(EPhraseTrigger.Roger, true);
        Check(candidate.GetPlayer.Spoken == EPhraseTrigger.Roger, "Pending acceptance can speak before follower registration");
        candidate.GetPlayer.Spoken=EPhraseTrigger.None;candidate.BotTalk.SetSilence(2f);
        typeof(FollowRequestPatch).GetMethod("TrySayControlledFollowerPhrase",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{candidate,EPhraseTrigger.Roger,EFT.Interactive.EInteraction.OkGesture,300});
        pitTeam.Utils.Utils.Pending();
        Check(candidate.GetPlayer.Spoken==EPhraseTrigger.Roger && !candidate.BotTalk.IsSilenced,"Delayed affirmative clears the pending-conversion silence before the same-frame production speech gate");
        candidate.IsFollower = true;
        FollowerForcedPhraseGate.Clear(candidate);
        Check(NativeSpeech.BotPrefix(candidate.BotTalk, EPhraseTrigger.Roger) && NativeSpeech.ManualPrefix(candidate.BotTalk), "Conversion retains existing follower EFT speech ownership");
        Check(!talk.Say(EPhraseTrigger.OnMutter), "Converted follower retains existing native chatter suppression");
        candidate.IsFollower = false;
        FollowerForcedPhraseGate.ArmRecruitmentResponse(candidate, EPhraseTrigger.Negative, 2f);
        FollowerForcedPhraseGate.Clear(candidate);
        Check(!NativeSpeech.BotPrefix(candidate.BotTalk, EPhraseTrigger.Negative), "Clearing scope revokes candidate bypass");
        Check(!FollowerForcedPhraseGate.IsRecruitmentResponse(null, EPhraseTrigger.Negative), "Null scope is safe");
        var human = new Player { IsAI = false };
        Check(NativeSpeech.PlayerPrefix(human, EPhraseTrigger.Roger), "Human speech is unchanged");
        pitFireTeam.IsSAINInstalled = false;
        candidate.BotTalk.QueueRequests = true;
        Reply.Invoke(null, new object[] { candidate, EPhraseTrigger.Negative, false });
        Check(candidate.BotTalk.Queued == 1 && !FollowerForcedPhraseGate.IsRecruitmentResponse(candidate, EPhraseTrigger.Negative), "SAIN absence preserves original TrySay queue without new scope");
        return count;
    }
}
