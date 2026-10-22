using System;
using EFT;
using EFT.InventoryLogic;
using pitTeam.Modules;

namespace pitTeam.BigBrain.Actions
{
    internal sealed class GestureCommandAction
    {
        private FollowerLootRequest ActiveLootRequest;
        private int bodyLootReportedMovesSucceeded;
        private bool bodyLootWeaponListDirty, bodyLootHadEligibleButNoSpace;
        private Weapon pendingBodyPrimaryWeaponSelection, pendingBodySecondaryWeaponPromotion;
        private object activeBodyLootCorpse = new object();
        private readonly BotOwner BotOwner = new BotOwner();
        private string completion;
        private int searched, queued;
        private void TryMarkBodyLootSearchedForBoss() { searched++; }
        private void ClearBodyLootState(string reason) { completion = reason; }
        private void QueuePostLootPrimaryWeaponSelection(Weapon weapon, string reason) { if (weapon != null) queued++; }
        private void QueuePostLootSecondaryWeaponPromotion(Weapon weapon, string reason) { if (weapon != null) queued++; }
        /* FINISH BODY LOOT */

        public static int Main()
        {
            int checks = 0;
            Action<bool, string> check = (value, label) => { if (!value) throw new Exception(label); checks++; };
            foreach (var mode in new[] { FollowerLootMode.LootAndGetGear, FollowerLootMode.LootAndGetWeapon })
            {
                var request = new FollowerLootRequest(mode);
                request.CompleteGearExchange();
                // A restarted action has fresh local counters but the same accepted request.
                var resumed = new GestureCommandAction { ActiveLootRequest = request,
                    bodyLootReportedMovesSucceeded = 0, bodyLootWeaponListDirty = true,
                    pendingBodyPrimaryWeaponSelection = new Weapon(), pendingBodySecondaryWeaponPromotion = new Weapon() };
                resumed.FinishBodyLootNoMoreMoves();
                check(resumed.completion == "TakeBodyGear:done", "Committed exchange survives action-local counter reset");
                check(resumed.BotOwner.BotTalk.Calls == 1 && resumed.BotOwner.BotTalk.Last == EPhraseTrigger.Ready,
                    "Both recruit modes request Ready through shared successful completion");
                check(resumed.searched == 1 && resumed.queued == 2 && resumed.BotOwner.WeaponManager.Updates == 1,
                    "Search marking and post-loot weapon handoff remain intact");
            }
            foreach (bool ordinarySuccess in new[] { false, true })
            foreach (bool noSpace in new[] { false, true })
            {
                var action = new GestureCommandAction { ActiveLootRequest = new FollowerLootRequest(),
                    bodyLootReportedMovesSucceeded = ordinarySuccess ? 1 : 0, bodyLootHadEligibleButNoSpace = noSpace };
                action.FinishBodyLootNoMoreMoves();
                check(action.completion == (ordinarySuccess ? "TakeBodyGear:done" : "TakeBodyGear:noSpace"),
                    "Ordinary loot classification remains separate from a failed or empty search");
                check(action.BotOwner.BotTalk.Last == (ordinarySuccess ? EPhraseTrigger.Ready : noSpace ? EPhraseTrigger.Negative : EPhraseTrigger.LootNothing),
                    "No Ready is requested for empty/dogtag-only or no-room outcomes");
            }
            var replacement = new GestureCommandAction { ActiveLootRequest = new FollowerLootRequest(FollowerLootMode.LootAndGetGear) };
            replacement.FinishBodyLootNoMoreMoves();
            check(replacement.completion != "TakeBodyGear:done" && replacement.BotOwner.BotTalk.Last != EPhraseTrigger.Ready,
                "A replacement request cannot inherit successful completion");
            Console.WriteLine("Recruit loot completion: " + checks + " production finalizer assertions passed; not native audio qualification.");
            return 0;
        }
    }
}
namespace EFT.InventoryLogic { internal sealed class Weapon { } }
namespace EFT
{
    internal enum EPhraseTrigger { Ready, Negative, LootNothing }
    internal sealed class Talk
    {
        internal int Calls; internal EPhraseTrigger Last;
        internal void TrySay(EPhraseTrigger phrase, bool unused) { Calls++; Last = phrase; }
    }
    internal sealed class Weapons { internal int Updates; internal void UpdateWeaponsList() { Updates++; } }
    internal sealed class BotOwner { internal readonly Talk BotTalk = new Talk(); internal readonly Weapons WeaponManager = new Weapons(); }
}
namespace pitTeam.Modules { internal static class InteractableObjects { internal static void MarkBodyLootTargetChecked(object corpse) { } } }
