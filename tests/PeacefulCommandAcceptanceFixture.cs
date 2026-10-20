using System;
using System.Collections.Generic;

// Execute extracted production command handlers; stand-ins expose admission and routing only.
enum EPhraseTrigger { Negative, Roger }
enum EInteraction { NoGesture, OkGesture }
enum EBotState { Active, Inactive }
enum FollowerCommandType { None, PushEnemy }
interface IPlayer { }
class Player : IPlayer { }
class Memory { public bool HaveEnemy; }
class Talk { public EPhraseTrigger Last; public int Calls; public void TrySay(EPhraseTrigger phrase, bool delay) { Last = phrase; Calls++; } }
class Gesture { public EInteraction Last; public void TryGestus(EInteraction value, bool force) { Last = value; } }
class BotOwner
{
    public Memory Memory = new Memory();
    public Talk BotTalk = new Talk();
    public Gesture Gesture = new Gesture();
    public bool ActiveEnemy, IsDead, LootLocked;
    public EBotState BotState = EBotState.Active;
    public BotFollowerPlayer Data = new BotFollowerPlayer();
}
class BotFollowerPlayer
{
    public bool IsSquadMate, Handoff, Crouch;
    public int Holds, Overrides, Clears, Resets, OverrideClears;
    public float Aggression;
    public FollowerCommandType Command;
    public bool HasCombatHandoffSignal() => Handoff;
    public bool TryPeekActiveCommand(out FollowerCommandType command, out int target, out float until)
    { command = Command; target = 0; until = 0; return command != FollowerCommandType.None; }
    public void ClearCommand(string reason) { Clears++; Command = FollowerCommandType.None; }
    public void SetTemporaryCombatAggressionOverride(float value, string reason) { Overrides++; Aggression = value; }
    public void ClearTemporaryCombatAggressionOverride(string reason) { OverrideClears++; }
    public void SetHoldPosition(float duration, bool crouch) { if (!float.IsPositiveInfinity(duration)) throw new Exception("hold duration changed"); Holds++; Crouch = crouch; }
    public void ResetCombatPercentageOverrides(string reason) { Resets++; }
}
class BossPlayers
{
    public static BossPlayers Instance = new BossPlayers();
    public BotFollowerPlayer GetFollower(BotOwner owner) => owner.Data;
}
static class Mathf { public static bool Approximately(float a, float b) => Math.Abs(a - b) < .0001f; }
class AIBossPlayer
{
    public List<BotOwner> Followers = new List<BotOwner>();
    const float PhraseCommandDistance = 35;
    public static bool AcceptRoll;
    public static int Rolls;
    static bool IsPickedUpFollower(BotFollowerPlayer data) => !data.IsSquadMate;
    static bool HasActiveCombatEnemy(BotOwner owner) => owner.ActiveEnemy;
    static bool RollPickupFollowerHoldAcceptance(BotOwner owner, BotFollowerPlayer data, IPlayer requester, string source) { Rolls++; return AcceptRoll; }
    static bool IsFollowerLockedByLootCommand(BotOwner owner) => owner.LootLocked;
    BotOwner FindLookedAtFollower(Player player, float distance, float dot, float radius) => null;
    public static bool Hold(BotOwner owner, bool crouch, string source) => ApplyHoldIntent(owner, owner?.Data, new Player(), crouch, source);
    public void Aggression(float? value) => ApplyTemporaryCombatAggressionCommand(new Player(), value, EPhraseTrigger.Roger);
    /* HOLD HANDLER */
    /* COMBAT CONTEXT */
    /* AGGRESSION HANDLER */
}
static class Entry
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static BotOwner Owner(bool squadmate, int combat)
    {
        var owner = new BotOwner(); owner.Data.IsSquadMate = squadmate;
        owner.Memory.HaveEnemy = combat == 1;
        owner.ActiveEnemy = combat == 2;
        owner.Data.Handoff = combat == 3;
        AIBossPlayer.Rolls = 0; AIBossPlayer.AcceptRoll = false;
        return owner;
    }
    public static int Main()
    {
        try
        {
            foreach (bool squadmate in new[] { false, true })
            foreach (bool crouch in new[] { false, true })
            foreach (int combat in new[] { 0, 1, 2, 3 })
            foreach (bool accept in new[] { false, true })
            {
                var owner = Owner(squadmate, combat); AIBossPlayer.AcceptRoll = accept;
                owner.Data.Command = FollowerCommandType.PushEnemy;
                Check(AIBossPlayer.Hold(owner, crouch, crouch ? "HoldGesture" : "StopPhrase"), "hold handled");
                bool rolled = combat != 0 && !squadmate;
                bool rejected = rolled && !accept;
                Check(AIBossPlayer.Rolls == (rolled ? 1 : 0), "Only combat recruits roll; no peaceful RNG consumption");
                Check(owner.Data.Holds == (combat == 0 ? 1 : 0), "Peaceful hold uses request layer");
                Check(owner.Data.Overrides == (combat != 0 && !rejected ? 1 : 0), "Accepted combat hold retains aggression ownership");
                Check(owner.Data.Clears == (combat != 0 && !rejected ? 1 : 0), "Only accepted combat hold replaces push");
                Check(owner.Gesture.Last == (rejected ? EInteraction.NoGesture : EInteraction.OkGesture), "Correct acknowledgement");
                Check(owner.BotTalk.Calls == (rejected ? 1 : 0), "Only rejected combat orders say Negative");
                if (combat == 0) Check(owner.Data.Crouch == crouch && owner.Data.OverrideClears == 1, "Peaceful Stop/gesture posture preserved");

                owner = Owner(squadmate, combat); AIBossPlayer.AcceptRoll = accept;
                var boss = new AIBossPlayer(); boss.Followers.Add(owner); boss.Aggression(0f);
                Check(AIBossPlayer.Rolls == (rolled ? 1 : 0), "Hold Position phrase only rolls during combat");
                Check(owner.Data.Overrides == (rejected ? 0 : 1), "Hold Position override unchanged except peaceful admission");
                Check(owner.BotTalk.Last == (rejected ? EPhraseTrigger.Negative : EPhraseTrigger.Roger), "Phrase acknowledgement preserved");
            }
            var skipped = Owner(false, 0); skipped.LootLocked = true;
            var commander = new AIBossPlayer(); commander.Followers.Add(skipped); commander.Aggression(0f);
            Check(skipped.Data.Overrides == 0 && AIBossPlayer.Rolls == 0, "Loot lock preserved");
            skipped.LootLocked = false; skipped.IsDead = true; commander.Aggression(0f);
            Check(skipped.Data.Overrides == 0, "Dead followers excluded");
            skipped.IsDead = false; skipped.BotState = EBotState.Inactive; commander.Aggression(0f);
            Check(skipped.Data.Overrides == 0, "Inactive followers excluded");
            Check(!AIBossPlayer.Hold(null, false, "StopPhrase"), "Null hold target remains safe");
            Console.WriteLine(checks + " peaceful/combat command acceptance checks passed (controlled handler fixture, not live EFT).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
