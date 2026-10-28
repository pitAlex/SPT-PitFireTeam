using pitTeam.Server.Models;
using SPTarkov.Server.Core.Constants;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace pitTeam.Server.Services;

public partial class FriendlyTeammateService
{
    private const string TreatmentDocument = "pending-treatment.json";

    public FriendlyTeammateTreatmentResponse GetTeammateHealth(MongoId sessionId, FriendlyTeammateTreatmentRequest request, bool quote)
    {
        lock (CreationLock)
        {
            RecoverTeammateTreatment(sessionId);
            var teammate = FindTreatmentTeammate(sessionId, request.Aid);
            if (teammate.Health == null) throw new FriendlyTeammateException("ProfileHealingUnavailable");
            AdvanceTeammateRecovery(sessionId, teammate);
            var response = TreatmentSnapshot(sessionId, teammate, request.SyncPayment);
            if (!quote) return response;
            if (FriendlyModeRequestGate.HasActiveRaid) throw new FriendlyTeammateException("SettingsUnavailableDuringRaid");
            var existing = storage.Read<FriendlyTeammateTreatmentJournal>(sessionId, TreatmentDocument);
            // Do not replace a pending receipt after an uncertain payment response.
            if (existing?.State == "paying") throw new FriendlyTeammateException("ProfileHealingRecoveryFailed");
            var journal = new FriendlyTeammateTreatmentJournal
            {
                Token = Guid.NewGuid().ToString("N"), Aid = request.Aid!, ExpiresUtc = DateTime.UtcNow.AddMinutes(2),
                Price = CalculateTreatmentPrice(GetPlayerProfile(sessionId), teammate.Health),
                HealthBefore = TreatmentHealthSignature(teammate.Health), HealthAfter = cloner.Clone(teammate.Health)!
            };
            foreach (var name in FriendlyTeammateTreatmentPricing.BodyParts)
                journal.HealthAfter.BodyParts![name].Health!.Current = journal.HealthAfter.BodyParts[name].Health!.Maximum;
            journal.HealthAfter.UpdateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            storage.Write(sessionId, TreatmentDocument, journal);
            response.Price = journal.Price;
            response.QuoteToken = journal.Token;
            return response;
        }
    }

    public FriendlyTeammateTreatmentResponse HealTeammate(MongoId sessionId, FriendlyTeammateTreatmentRequest request)
    {
        lock (CreationLock)
        {
            RecoverTeammateTreatment(sessionId);
            var journal = storage.Read<FriendlyTeammateTreatmentJournal>(sessionId, TreatmentDocument);
            if (journal == null || string.IsNullOrWhiteSpace(request.QuoteToken) || journal.Token != request.QuoteToken || journal.Aid != request.Aid)
                throw new FriendlyTeammateException("ProfileHealingQuoteExpired");
            var teammate = FindTreatmentTeammate(sessionId, request.Aid);
            if (journal.State == "complete") return TreatmentSnapshot(sessionId, teammate);
            if (FriendlyModeRequestGate.HasActiveRaid || journal.State != "pending" || journal.ExpiresUtc <= DateTime.UtcNow)
                throw new FriendlyTeammateException("ProfileHealingQuoteExpired");
            var player = GetPlayerProfile(sessionId);
            if (saveServer.IsProfileInvalidOrUnloadable(sessionId))
                throw new FriendlyTeammateException("ProfileHealingRecoveryFailed");
            // A read/poll may have checkpointed natural recovery since the quote. Accept only
            // that deterministic progression, never a changed injury/effect/max-health state.
            var expectedHealth = jsonUtil.Deserialize<BotBaseHealth>(journal.HealthBefore)!;
            if (teammate.Health?.UpdateTime is double checkpoint)
                FriendlyTeammateRegeneration.Advance(expectedHealth, FriendlyTeammateRegeneration.Rates(player, globalTable),
                    FriendlyTeammateRegeneration.LoopSeconds(globalTable), checkpoint);
            // Summing individual ticks versus one elapsed batch can differ by double rounding.
            // Normalize only negligible HP drift; maxima, effects and the checkpoint stay strict.
            foreach (string part in FriendlyTeammateTreatmentPricing.BodyParts)
            {
                if (expectedHealth.BodyParts?.GetValueOrDefault(part)?.Health?.Current is not double expected
                    || teammate.Health?.BodyParts?.GetValueOrDefault(part)?.Health?.Current is not double actual
                    || Math.Abs(expected - actual) > 0.000001)
                    throw new FriendlyTeammateException("ProfileHealingQuoteExpired");
                expectedHealth.BodyParts[part].Health!.Current = actual;
            }
            if (TreatmentHealthSignature(expectedHealth) != TreatmentHealthSignature(teammate.Health))
                throw new FriendlyTeammateException("ProfileHealingQuoteExpired");
            AdvanceTeammateRecovery(sessionId, teammate);
            int currentPrice = CalculateTreatmentPrice(player, teammate.Health!);
            if (currentPrice > journal.Price) throw new FriendlyTeammateException("ProfileHealingQuoteExpired");
            journal.Price = currentPrice;
            journal.HealthBefore = TreatmentHealthSignature(teammate.Health);
            journal.HealthAfter = cloner.Clone(teammate.Health)!;
            foreach (string part in FriendlyTeammateTreatmentPricing.BodyParts)
                journal.HealthAfter.BodyParts![part].Health!.Current = journal.HealthAfter.BodyParts[part].Health!.Maximum;
            if (FriendlyTeammateTreatmentPricing.MissingHealth(teammate.Health) <= 0)
            {
                CompleteTeammateTreatment(sessionId, journal);
                return TreatmentSnapshot(sessionId, teammate);
            }

            var payment = cloner.Clone(player)!;
            try { DeductRoublesFromPlayerStash(payment, journal.Price); }
            catch (FriendlyTeammateException) { throw new FriendlyTeammateException("ProfileHealingInsufficientFunds"); }
            // Stock HealthTreatment pays Therapist, counting the payment toward her turnover.
            payment.TradersInfo![Traders.THERAPIST].SalesSum = (payment.TradersInfo[Traders.THERAPIST].SalesSum ?? 0) + journal.Price;
            traderHelper.LevelUp(Traders.THERAPIST, payment);
            journal.PlayerBefore = TreatmentPlayerSignature(player);
            journal.PlayerAfter = TreatmentPlayerSignature(payment);
            journal.State = "paying";
            storage.Write(sessionId, TreatmentDocument, journal);
            player.Inventory = payment.Inventory;
            player.TradersInfo![Traders.THERAPIST] = payment.TradersInfo[Traders.THERAPIST];
            // On failure leave the intent recoverable. Never refund a payment whose teammate
            // commit may already have succeeded, and never repeat a completed token's charge.
            SaveTreatmentPlayerVerified(sessionId, journal.PlayerAfter);
            CompleteTeammateTreatment(sessionId, journal);
            logger.Info($"[TeammateTreatment] aid={journal.Aid} price={journal.Price} receipt={journal.Token}");
            return TreatmentSnapshot(sessionId, FindTreatmentTeammate(sessionId, request.Aid));
        }
    }

    public void RecoverTeammateTreatment(MongoId sessionId)
    {
        lock (CreationLock)
        {
            var journal = storage.Read<FriendlyTeammateTreatmentJournal>(sessionId, TreatmentDocument);
            if (journal?.State != "paying") return;
            string current = TreatmentPlayerSignature(GetPlayerProfile(sessionId));
            if (current == journal.PlayerAfter)
            {
                SaveTreatmentPlayerVerified(sessionId, journal.PlayerAfter!);
                CompleteTeammateTreatment(sessionId, journal);
            }
            else if (current == journal.PlayerBefore)
            {
                SaveTreatmentPlayerVerified(sessionId, journal.PlayerBefore!);
                journal.State = "pending";
                storage.Write(sessionId, TreatmentDocument, journal);
            }
            else throw new FriendlyTeammateException("ProfileHealingRecoveryFailed");
        }
    }

    private void CompleteTeammateTreatment(MongoId sessionId, FriendlyTeammateTreatmentJournal journal)
    {
        var teammate = storage.Read<BotBase>(sessionId, journal.Aid + ".json");
        if (teammate == null || TreatmentHealthSignature(teammate.Health) != journal.HealthBefore)
            throw new FriendlyTeammateException("ProfileHealingRecoveryFailed");
        teammate.Health = cloner.Clone(journal.HealthAfter);
        journal.State = "complete";
        storage.WriteBatch(sessionId, new Dictionary<string, string>
        {
            [journal.Aid + ".json"] = jsonUtil.Serialize(teammate)!,
            [TreatmentDocument] = jsonUtil.Serialize(journal)!
        });
    }

    private BotBase FindTreatmentTeammate(MongoId sessionId, string? aid)
    {
        if (!int.TryParse(aid, out int accountId) || accountId <= 0 || accountId.ToString() != aid)
            throw new FriendlyTeammateException("ProfileHealingUnavailable");
        return storage.Read<BotBase>(sessionId, accountId + ".json")
            ?? throw new FriendlyTeammateException("ProfileHealingUnavailable");
    }

    private int CalculateTreatmentPrice(PmcData player, BotBaseHealth health)
    {
        var price = globalTable.Configuration.Health.HealPrice;
        if (player.TradersInfo == null || !player.TradersInfo.TryGetValue(Traders.THERAPIST, out var trader)
            || trader.Unlocked != true || trader.Disabled == true)
            throw new FriendlyTeammateException("ProfileHealingUnavailable");
        int level = Math.Clamp((int)((player.Skills?.Common?.FirstOrDefault(skill => skill.Id == SkillTypes.Charisma)?.Progress ?? 0) / 100), 0, 50);
        var bonus = globalTable.Configuration.SkillsSettings.Charisma.BonusSettings.LevelBonusSettings;
        long raids = player.Stats?.Eft?.OverallCounters?.Items?.FirstOrDefault(item =>
            item.Key?.SetEquals(["Sessions", "Pmc"]) == true)?.Value ?? 0;
        bool free = price.TrialLevels > 0 && player.Info?.Level <= price.TrialLevels && price.TrialRaids > 0 && raids <= price.TrialRaids;
        return FriendlyTeammateTreatmentPricing.Calculate(FriendlyTeammateTreatmentPricing.MissingHealth(health),
            (float)price.HealthPointPrice, (float)(traderHelper.GetLoyaltyLevel(Traders.THERAPIST, player).HealPriceCoefficient ?? 100),
            trader.LoyaltyLevel ?? 1, level, (float)bonus.HealthRestoreDiscount, (float)bonus.HealthRestoreTraderDiscount, free);
    }

    private void AdvanceTeammateRecovery(MongoId sessionId, BotBase teammate, bool raidSpawn = false)
    {
        if ((!raidSpawn && FriendlyModeRequestGate.HasActiveRaid) || teammate.Health == null
            || !FriendlyTeammateRegeneration.IsWounded(teammate.Health)) return;
        var rates = FriendlyTeammateRegeneration.Rates(GetPlayerProfile(sessionId), globalTable);
        if (FriendlyTeammateRegeneration.Advance(teammate.Health, rates, FriendlyTeammateRegeneration.LoopSeconds(globalTable), DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
            storage.Write(sessionId, teammate.Aid + ".json", teammate);
    }

    private FriendlyTeammateTreatmentResponse TreatmentSnapshot(MongoId sessionId, BotBase teammate, bool payment = true)
    {
        var player = GetPlayerProfile(sessionId);
        return new()
        {
            Health = cloner.Clone(teammate.Health)!,
            RegenerationPerSecond = FriendlyTeammateRegeneration.Rates(player, globalTable),
            LoopSeconds = FriendlyTeammateRegeneration.LoopSeconds(globalTable),
            InsurancePlayerRoubles = payment ? player.Inventory?.Items?.Where(IsPaymentMoney).ToList() : null,
            InsurancePlayerTraders = payment ? player.TradersInfo : null
        };
    }

    private string TreatmentHealthSignature(BotBaseHealth? health) => jsonUtil.Serialize(health) ?? "null";
    private string TreatmentPlayerSignature(PmcData player)
    {
        var trader = player.TradersInfo![Traders.THERAPIST];
        // Resupply deadlines and trader availability can change independently on restart.
        // Verify only the relationship fields native payment actually changes.
        return CreationMoneySignature(player) + "|" + jsonUtil.Serialize(new { trader.SalesSum, trader.Standing, trader.LoyaltyLevel });
    }

    private void SaveTreatmentPlayerVerified(MongoId sessionId, string expected)
    {
        SavePlayerMoneyVerified(sessionId, CreationMoneySignature(GetPlayerProfile(sessionId)), "ProfileHealingRecoveryFailed");
        if (TreatmentPlayerSignature(ReadSavedPlayerProfile(sessionId)!.CharacterData!.PmcData!) != expected)
            throw new FriendlyTeammateException("ProfileHealingRecoveryFailed");
    }
}
