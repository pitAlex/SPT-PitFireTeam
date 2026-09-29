using pitTeam.Server.Models;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    private const string DeletionDocument = "pending-deletion.json";

    public void CaptureRecruitmentGearPrice(FriendlyRecruitRequestEntry candidate)
    {
        if (candidate.RecruitmentGearPrice.HasValue) return;
        if (!TryDeserializeRecruitProfile(candidate, out var profile))
            throw new FriendlyTeammateException("TeammateHirePriceFailed");
        candidate.RecruitmentGearPrice = CalculateRecruitmentGearPrice(profile);
    }

    public FriendlyTeammateDeleteResponse DeleteTeammateWithPayment(MongoId sessionId, FriendlyTeammateDeleteRequest request)
    {
        lock (CreationLock)
        {
            RecoverTeammateCreation(sessionId);
            RecoverTeammateDeletion(sessionId);
            if (!int.TryParse(request.AccountId, out int aid) || aid <= 0)
                throw new FriendlyTeammateException("TeammateDeleteFailed");
            var teammate = storage.ReadProfiles(sessionId).FirstOrDefault(profile => profile.Aid == aid);
            var receipt = storage.Read<FriendlyTeammateDeletionJournal>(sessionId, DeletionDocument);
            bool deleted = teammate != null ? DeleteTeammate(sessionId, teammate)
                : receipt?.Aid == aid && receipt.State == "complete";
            return new() { Deleted = deleted, PlayerStashItems = GetPlayerStashItems(GetPlayerProfile(sessionId)) };
        }
    }

    private bool DeleteTeammate(MongoId sessionId, BotBase teammate)
    {
        lock (CreationLock)
        {
            RecoverTeammateCreation(sessionId);
            RecoverTeammateDeletion(sessionId);
            int aid = teammate.Aid ?? throw new FriendlyTeammateException("TeammateDeleteFailed");
            // Another request/recovery may already have removed this profile.
            if (!storage.Exists(sessionId, $"{aid}.json")) return false;
            int price = GetTeammateSettings(sessionId, teammate).RecruitmentGearPrice ?? 0;
            if (price < 0) throw new FriendlyTeammateException("TeammateDeleteFailed");
            var player = GetPlayerProfile(sessionId);
            if (saveServer.IsProfileInvalidOrUnloadable(sessionId))
                throw new FriendlyTeammateException("TeammateDeleteFailed");
            var journal = new FriendlyTeammateDeletionJournal { Aid = aid, Price = price };
            if (price == 0) return CompleteTeammateDeletion(sessionId, journal);

            var originalItems = cloner.Clone(player.Inventory!.Items)!;
            journal.MoneyBefore = CreationMoneySignature(player);
            try { DeductRoublesFromPlayerStash(player, price); }
            catch (FriendlyTeammateException) { throw new FriendlyTeammateException("TeammateDeleteInsufficientFunds"); }
            journal.MoneyAfter = CreationMoneySignature(player);
            journal.State = "paying";
            try
            {
                storage.Write(sessionId, DeletionDocument, journal);
                saveServer.SaveProfileAsync(sessionId).GetAwaiter().GetResult();
                return CompleteTeammateDeletion(sessionId, journal);
            }
            catch
            {
                var saved = storage.Read<FriendlyTeammateDeletionJournal>(sessionId, DeletionDocument);
                if (saved?.Aid != aid || saved.State != "complete")
                {
                    player.Inventory.Items = originalItems;
                    saveServer.SaveProfileAsync(sessionId).GetAwaiter().GetResult();
                    journal.State = "pending";
                    storage.Write(sessionId, DeletionDocument, journal);
                }
                throw;
            }
        }
    }

    public void RecoverTeammateDeletion(MongoId sessionId)
    {
        lock (CreationLock)
        {
            var journal = storage.Read<FriendlyTeammateDeletionJournal>(sessionId, DeletionDocument);
            if (journal?.State != "paying") return;
            string money = CreationMoneySignature(GetPlayerProfile(sessionId));
            if (money == journal.MoneyAfter) CompleteTeammateDeletion(sessionId, journal);
            else if (money == journal.MoneyBefore)
            {
                journal.State = "pending";
                storage.Write(sessionId, DeletionDocument, journal);
            }
            else throw new FriendlyTeammateException("TeammateDeleteRecoveryFailed");
        }
    }

    private bool CompleteTeammateDeletion(MongoId sessionId, FriendlyTeammateDeletionJournal journal)
    {
        journal.State = "complete";
        bool deleted = storage.DeleteTeammate(sessionId, journal.Aid,
            new Dictionary<string, string> { [DeletionDocument] = jsonUtil.Serialize(journal)! });
        if (!deleted) throw new FriendlyTeammateException("TeammateDeleteRecoveryFailed");
        logger.Info($"Deleted teammate '{journal.Aid}' recruitmentGearPrice={journal.Price}");
        return true;
    }
}
