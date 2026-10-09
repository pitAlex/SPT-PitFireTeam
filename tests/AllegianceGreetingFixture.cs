using System;
using EFT;
using pitTeam.Modules;
using pitTeam.Patches;
using UnityEngine;

public static class AllegianceGreetingChecks
{
    private static int count;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
    public static int Run()
    {
        foreach (bool sain in new[] { false, true })
        foreach (float random in new[] { 0f, 1f })
        {
            pitTeam.pitFireTeam.IsSAINInstalled = sain;
            GameplayModeRuntime.IsAllegiance = true;
            UnityEngine.Random.Fraction = random;
            var human = new Player();
            var bot = new BotOwner { ProfileId = "greeting-" + sain + random };
            bot.GetPlayer.MarkSpeaking = true;
            var greeting = new AllegianceFriendlyGreeting();
            void Tick(float t) { Time.time=t; greeting.Update(bot,human); }
            bot.GetPlayer.Position = new Vector3 { x = 50.01f };
            Tick(100); Tick(110);
            Check(bot.GetPlayer.VoiceCalls==0,"Beyond 50m cannot greet");
            bot.GetPlayer.Position = new Vector3 { x = 50f };
            bot.LookSensor.InSector=false; Tick(120);
            bot.LookSensor.InSector=true; bot.LookSensor.Clear=false; Tick(130);
            bot.LookSensor.Clear=true; bot.LookSensor.VisibleDist=49f; Tick(140);
            Check(bot.GetPlayer.VoiceCalls==0,"Requires native sector, clear sight and visible distance");
            bot.LookSensor.VisibleDist=100f;
            Tick(150); Tick(150.5f);
            Check(bot.GetPlayer.VoiceCalls==0,"First sight at 50m starts two-second delay");
            float first=152f;
            Tick(first-0.5f);
            Check(bot.GetPlayer.VoiceCalls==0,"Distance-based first delay has not elapsed");
            bot.GetPlayer.Speaker.Busy=true; Tick(first);
            Check(bot.GetPlayer.VoiceCalls==0,"Does not interrupt busy speech");
            bot.GetPlayer.Speaker.Busy=false;
            FollowerForcedPhraseGate.ArmRecruitmentResponse(bot,EPhraseTrigger.Negative,2f);
            Tick(first+0.5f);
            Check(bot.GetPlayer.VoiceCalls==0,"Does not replace recruitment reply scope");
            FollowerForcedPhraseGate.Clear(bot);
            Tick(first+1f);
            Check(bot.GetPlayer.VoiceCalls==1 && bot.GetPlayer.Spoken==EPhraseTrigger.HoldFire,"First exact phrase passes vanilla and real Harmony SAIN blockers");
            bot.GetPlayer.Speaker.Speaking=false;
            Tick(first+1.5f);
            Check(bot.GetPlayer.VoiceCalls==1,"Second randomized delay is separate");
            Tick(first+1.75f+2f*random);
            Check(bot.GetPlayer.VoiceCalls==1,"Random second delay has not elapsed");
            bot.LookSensor.Clear=false; Tick(first+20f);
            Check(bot.GetPlayer.VoiceCalls==1,"Second phrase waits while sight is lost");
            bot.LookSensor.Clear=true; Tick(first+21f);
            Check(bot.GetPlayer.VoiceCalls==2,"Second phrase resumes on reacquisition");
            bot.GetPlayer.Speaker.Speaking=false;
            Tick(first+50f); Tick(first+100f);
            Check(bot.GetPlayer.VoiceCalls==2,"No repeat on later meetings");
            greeting=new AllegianceFriendlyGreeting();
            FollowerForcedPhraseGate.Clear(bot);
            Tick(400);
            bot.IsFollower=true; Tick(410);
            Check(bot.GetPlayer.VoiceCalls==2,"Recruitment cancels pending greeting");
            bot.IsFollower=false; GameplayModeRuntime.IsAllegiance=false; Tick(420);
            bot.IsDead=true; GameplayModeRuntime.IsAllegiance=true; Tick(430);
            Check(bot.GetPlayer.VoiceCalls==2,"Mode and death guards");
            bot.IsDead=false; bot.GetPlayer.HealthController.IsAlive=false; Tick(440);
            bot.GetPlayer.HealthController.IsAlive=true; human.HealthController.IsAlive=false; Tick(450);
            Check(bot.GetPlayer.VoiceCalls==2,"Living bot and human required");
            human.HealthController.IsAlive=true; bot.BotState=EBotState.Inactive; Tick(460);
            Check(bot.GetPlayer.VoiceCalls==2,"Inactive bot stays silent");
            bot.BotState=EBotState.Active; Tick(470);
            Check(bot.GetPlayer.VoiceCalls==3,"New raid state can greet again");

            greeting=new AllegianceFriendlyGreeting();
            FollowerForcedPhraseGate.Clear(bot); bot.GetPlayer.Speaker.Speaking=false;
            bot.GetPlayer.MarkSpeaking=false; Tick(500); Tick(510);
            Check(bot.GetPlayer.VoiceCalls==4 && !FollowerForcedPhraseGate.TryGetArmedPhrase(bot,out _),"Rejected speaker output clears its own scope without spending a greeting");
            bot.GetPlayer.MarkSpeaking=true; Tick(512);
            Check(bot.GetPlayer.VoiceCalls==5,"Failed playback can retry");
            bot.GetPlayer.Speaker.Speaking=false; Tick(530);
            Check(bot.GetPlayer.VoiceCalls==6,"Failed playback still allows two successful greetings");
            bot.GetPlayer.Speaker.Speaking=false; Tick(550);
            Check(bot.GetPlayer.VoiceCalls==6,"Two successful lines exhaust the retry sequence");
        }
        foreach (bool sain in new[] { false, true })
        foreach (float distance in new[] { 0f, 5f, 25f, 50f })
        foreach (float random in new[] { 0f, 1f })
        {
            pitTeam.pitFireTeam.IsSAINInstalled=sain;
            UnityEngine.Random.Fraction=random;
            UnityEngine.Random.ValueFraction=random;
            var bot=new BotOwner { ProfileId="distance-"+sain+distance+random };
            bot.GetPlayer.MarkSpeaking=true;
            bot.GetPlayer.Position=new Vector3 { x=distance };
            var greeting=new AllegianceFriendlyGreeting(); var human=new Player();
            void Tick(float t) { Time.time=t; greeting.Update(bot,human); }
            Tick(600);
            float first=600f+0.2f+1.8f*distance/50f;
            Tick(first-0.01f);
            Check(bot.GetPlayer.VoiceCalls==0,"First line waits for distance-based delay at "+distance+"m");
            Tick(first+0.001f);
            Check(bot.GetPlayer.VoiceCalls==1,"First line meets distance-based deadline at "+distance+"m");
            bot.GetPlayer.Speaker.Speaking=false;
            float second=first+0.001f+1f+2f*random;
            Tick(second-0.01f);
            Check(bot.GetPlayer.VoiceCalls==1,"Second line waits for 1–3s deadline");
            Tick(second+0.001f);
            bool repeat=distance>=50f || (distance>0f && random<distance/50f);
            Check(bot.GetPlayer.VoiceCalls==(repeat ? 2 : 1),"Second line evaluates distance chance at random deadline");
        }
        foreach (bool sain in new[] { false, true })
        foreach (float distance in new[] { 0f, 5f, 25f, 45f, 50f })
        foreach (bool belowThreshold in new[] { false, true })
        {
            pitTeam.pitFireTeam.IsSAINInstalled=sain;
            UnityEngine.Random.Fraction=0;
            UnityEngine.Random.ValueFraction=Math.Max(0f,distance/50f-(belowThreshold ? 0.001f : 0f));
            UnityEngine.Random.Rolls=0;
            var bot=new BotOwner { ProfileId="chance-"+sain+distance+belowThreshold };
            bot.GetPlayer.MarkSpeaking=true;
            bot.GetPlayer.Position=new Vector3 { x=50f };
            var greeting=new AllegianceFriendlyGreeting(); var human=new Player();
            void Tick(float t) { Time.time=t; greeting.Update(bot,human); }
            Tick(700); Tick(702);
            Check(bot.GetPlayer.VoiceCalls==1 && UnityEngine.Random.Rolls==0,"First line is unconditional and does not spend a chance roll");
            bot.GetPlayer.Speaker.Speaking=false;
            bot.GetPlayer.Position=new Vector3 { x=distance };
            bot.LookSensor.Clear=false; Tick(703);
            bot.LookSensor.Clear=true; bot.GetPlayer.Speaker.Busy=true; Tick(704);
            Check(UnityEngine.Random.Rolls==0,"Second roll waits for sight and idle speech");
            bot.GetPlayer.Speaker.Busy=false;
            bool repeat=distance>=50f || (belowThreshold && distance>0f);
            bot.GetPlayer.MarkSpeaking=false; Tick(705);
            Check(UnityEngine.Random.Rolls==1 && bot.GetPlayer.VoiceCalls==(repeat ? 2 : 1),"Second chance uses current range and the exact threshold");
            bot.GetPlayer.MarkSpeaking=true;
            bot.GetPlayer.Position=new Vector3 { x=50f }; UnityEngine.Random.ValueFraction=0;
            Tick(707); bot.GetPlayer.Speaker.Speaking=false; Tick(710); Tick(720);
            Check(UnityEngine.Random.Rolls==1 && bot.GetPlayer.VoiceCalls==(repeat ? 3 : 1),"Playback retries preserve an accepted roll and later meetings cannot reroll a skip");
        }
        return count;
    }
}
