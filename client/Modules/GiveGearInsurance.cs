using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;

namespace pitTeam.Modules
{
    internal static class GiveGearInsurance
    {
        private static readonly HashSet<string> Candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<BotOwner> Recipients = new HashSet<BotOwner>();
        private static Player _owner;
        private static IEftSession _session;
        private static bool _settled;

        internal static void Reset()
        {
            Candidates.Clear();
            Recipients.Clear();
            _owner = null;
            _session = null;
            _settled = false;
        }

        // A committed handover is not a permanent gift yet: the PMC can still die or lose it.
        // Keep native coverage untouched until the final live inventories are available.
        internal static void Track(IEftSession session, Player owner, BotOwner recipient, IEnumerable<string> retainedIds)
        {
            if (_settled || owner?.IsYourPlayer != true || recipient == null ||
                (recipient.Side != EPlayerSide.Usec && recipient.Side != EPlayerSide.Bear)) return;
            if (_owner != null && _owner.ProfileId != owner.ProfileId) return;
            _owner = owner;
            _session = session;
            Candidates.UnionWith(retainedIds.Where(id => !string.IsNullOrEmpty(id)));
            Recipients.Add(recipient);
        }

        internal static void EndRaid(string profileId)
        {
            if (_settled || _owner == null || _owner.ProfileId != profileId) return;
            _settled = true;
            if (Candidates.Count == 0) return;
            try
            {
                // Include other field PMCs in case a gift changed hands. Remember original
                // recipients too: dismissal alone does not undo a gift or its invitation.
                var bots = Recipients.Concat(BossPlayers.GetFollowersByBoss(profileId)
                    .Where(f => !f.IsSquadMate).Select(f => f.GetBot())).Distinct();
                var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var bot in bots)
                {
                    if (bot == null || bot.IsDead ||
                        (bot.Side != EPlayerSide.Usec && bot.Side != EPlayerSide.Bear)) continue;
                    var player = bot.GetPlayer;
                    if (player == null || player.Destroyed || player.HealthController == null)
                        throw new InvalidOperationException("PMC gift recipient state unavailable.");
                    if (!player.HealthController.IsAlive) continue;
                    var equipment = player.InventoryController?.Inventory?.Equipment;
                    if (equipment == null) throw new InvalidOperationException("Living PMC gift inventory unavailable.");
                    retained.UnionWith(equipment.GetAllItems()
                        .Where(i => Candidates.Contains(i.Id) && !InteractableObjects.IsProtectedFollowerEquipment(i))
                        .Select(i => (string)i.Id));
                }
                var recovered = _owner.InventoryController?.Inventory?.Equipment;
                if (recovered == null) throw new InvalidOperationException("Player gift recovery inventory unavailable.");
                retained.ExceptWith(recovered.GetAllItems().Select(i => (string)i.Id));
                Cancel(_session, _owner.Profile, retained);
            }
            catch (Exception ex)
            {
                // Never guess ownership or delete coverage when the final snapshot failed.
                FollowerInsuranceRaidReports.Fail();
                Logger.LogError("[GiveGear:Insurance] Final gift ownership capture failed: " + ex);
            }
        }

        // Final retained PMC gifts only. Existing exact-ID server cleanup also removes
        // queued/staged native copies, including those imported after this report.
        internal static void Cancel(IEftSession session, Profile profile, IEnumerable<string> retainedIds)
        {
            var ids = new HashSet<string>(retainedIds, StringComparer.OrdinalIgnoreCase);
            if (ids.Count == 0) return;
            var report = FollowerInsuranceRaidReports.Prepare(JsonConvert.SerializeObject(new { ItemIds = ids.ToArray() }));
            try
            {
                foreach (var owner in new[] { profile, session?.Profile }.Where(p => p != null).Distinct())
                    owner.InsuredItems = owner.InsuredItems?.Where(policy => !ids.Contains(policy.ItemId)).ToArray();
                var insurance = session?.InsuranceCompany;
                if (insurance != null)
                {
                    foreach (var policy in insurance.InsuredItems.Where(item => ids.Contains(item.Id)).ToArray())
                        insurance.InsuredItems.Remove(policy);
                    insurance._missingInsuredItems?.RemoveAll(policy => ids.Contains(policy.ItemId));
                }
                FollowerInsuranceRaidDisplay.CancelGifts(ids);
            }
            finally
            {
                _ = Task.Run(() =>
                {
                    try { report.Send("/singleplayer/pitfireteam/give-gear/insurance-cancel"); }
                    catch (Exception ex) { Logger.LogError("[GiveGear:Insurance] Gift committed, coverage cancellation report failed: " + ex); }
                });
            }
        }
    }
}
