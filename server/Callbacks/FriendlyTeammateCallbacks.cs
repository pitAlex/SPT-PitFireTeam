using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Insurance;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Callbacks;

[Injectable]
public class FriendlyTeammateCallbacks(
    HttpResponseUtil httpResponse,
    FriendlyTeammateService teammateService,
    FriendlyServerSettingsService settingsService,
    FriendlyTeammateStorage storage,
    FriendlyTeammateInsuranceService teammateInsuranceService,
    JsonUtil jsonUtil,
    ISptLogger<FriendlyTeammateCallbacks> logger
)
{
    public ValueTask<string> InsureEquipment(string url, FriendlyTeammateInsuranceRequest request, MongoId sessionId)
    {
        try
        {
            return new ValueTask<string>(httpResponse.GetBody(teammateService.InsureTeammateEquipment(sessionId, request)));
        }
        catch (Exception ex)
        {
            logger.Warning($"[FollowerInsurance:Purchase] teammateAid='{request.Aid}' failed: {ex}");
            string key = ex is FriendlyTeammateException ? ex.Message : "FollowerInsurancePurchaseFailed";
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: key));
        }
    }

    public ValueTask<string> AugmentInsuranceCosts(
        string url,
        GetInsuranceCostRequestData request,
        MongoId sessionId,
        string? previousOutput)
    {
        if (string.IsNullOrWhiteSpace(previousOutput))
        {
            return new ValueTask<string>(httpResponse.NullResponse());
        }

        var body = jsonUtil.Deserialize<FriendlyTeammateBodyResponse<GetInsuranceCostResponseData>>(previousOutput);
        if (body?.Data == null || body.Err is not (null or 0))
        {
            return new ValueTask<string>(previousOutput);
        }

        teammateInsuranceService.AugmentInsuranceCosts(sessionId, request, body.Data);
        return new ValueTask<string>(httpResponse.GetBody(body.Data, body.Err ?? 0, body.ErrMsg));
    }

    public ValueTask<string> Create(string url, FriendlyTeammateCreateRequest request, MongoId sessionId)
    {
        try
        {
            return new ValueTask<string>(httpResponse.GetBody(teammateService.CreateTeammate(sessionId, request)));
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
        catch (Exception ex)
        {
            logger.Error($"Teammate purchase failed: {ex}");
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: "TeammateHirePurchaseFailed"));
        }
    }

    public ValueTask<string> PrepareCreation(string url, FriendlyTeammateCreateRequest request, MongoId sessionId)
    {
        try { return new(httpResponse.GetBody(teammateService.PrepareTeammateCreation(sessionId, request))); }
        catch (Exception ex)
        {
            logger.Warning($"Teammate quote failed: {ex}");
            return new(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError,
                errmsg: ex is FriendlyTeammateException ? ex.Message : "TeammateHirePriceFailed"));
        }
    }

    public ValueTask<string> CancelCreation(string url, FriendlyTeammateCreateRequest request, MongoId sessionId)
    {
        teammateService.CancelTeammateCreation(sessionId, request);
        return new(httpResponse.NullResponse());
    }

    public ValueTask<string> List(string url, EmptyRequestData _, MongoId sessionId)
    {
        teammateService.RecoverTeammateCreation(sessionId);
        return new ValueTask<string>(httpResponse.GetBody(teammateService.ListTeammates(sessionId)));
    }

    public ValueTask<string> ListAutoJoin(string url, EmptyRequestData _, MongoId sessionId)
    {
        return new ValueTask<string>(httpResponse.GetBody(teammateService.GetAutoJoinTeammateAccountIds(sessionId)));
    }

    public ValueTask<string> GetGameplayMode(string url, EmptyRequestData request, MongoId sessionId)
        => new(httpResponse.GetBody(new { gameplayMode = settingsService.LoadSettings().GameplayMode }));

    public ValueTask<string> SetServerSettings(string url, FriendlyServerSettingsRequest request, MongoId sessionId)
    {
        try
        {
            if (request.GameplayMode is not ("GunsForHire" or "Allegiance"))
                throw new FriendlyTeammateException("GameplayModeSwitchFailed");
            var previous = settingsService.LoadSettings();
            bool changingMode = previous.GameplayMode != request.GameplayMode;
            if (changingMode && FriendlyModeRequestGate.HasActiveRaid)
                throw new FriendlyTeammateException("SettingsUnavailableDuringRaid");
            if (changingMode)
            {
                teammateService.RecoverTeammateCreation(sessionId);
                teammateService.RecoverTeammateDeletion(sessionId);
                storage.PrepareMode(sessionId, request.IsAllegiance);
            }
              settingsService.SaveAndApply(request);
              if (changingMode) FriendlyModeRequestGate.ModeChanged();
            // A gameplay switch selects a different roster, not a loadout conversion of either roster.
            if (!changingMode && previous.LoadoutManagementMode != request.LoadoutManagementMode)
            {
                teammateService.LogLoadoutManagementModeChange(sessionId, previous.LoadoutManagementMode, request.LoadoutManagementMode);
                teammateService.ApplyLoadoutManagementModeChange(sessionId, previous.LoadoutManagementMode, request.LoadoutManagementMode);
            }
            return new(httpResponse.NullResponse());
        }
        catch (Exception ex)
        {
            logger.Error($"Gameplay/settings update failed: {ex}");
            return new(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError,
                errmsg: ex is FriendlyTeammateException ? ex.Message : "GameplayModeSwitchFailed"));
        }
    }
    public ValueTask<string> GetLostOnDeathSettings(string url, EmptyRequestData _, MongoId sessionId)
    {
        return new ValueTask<string>(httpResponse.GetBody(settingsService.GetLostOnDeathSettings()));
    }

    public ValueTask<string> GetStartupRecoveryNotice(string url, EmptyRequestData _, MongoId sessionId)
    {
        return new ValueTask<string>(httpResponse.GetBody(teammateService.GetStartupRecoveryNotice(sessionId)));
    }

    public ValueTask<string> AcknowledgeStartupRecoveryNotice(string url, EmptyRequestData _, MongoId sessionId)
    {
        teammateService.AcknowledgeStartupRecoveryNotice(sessionId);
        return new ValueTask<string>(httpResponse.NullResponse());
    }

    public ValueTask<string> GetProfile(string url, GetOtherProfileRequest request, MongoId sessionId)
    {
        try
        {
            return new ValueTask<string>(httpResponse.GetBody(teammateService.GetTeammateProfile(sessionId, request)));
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> GetProfileOptions(string url, FriendlyTeammateProfileOptionsRequest request, MongoId sessionId)
    {
        try
        {
            return new ValueTask<string>(httpResponse.GetBody(teammateService.GetProfileOptions(sessionId, request)));
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> SetSuit(string url, FriendlyTeammateSuitRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.SetTeammateSuit(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> Rename(string url, FriendlyTeammateRenameRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.RenameTeammate(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> SetLoadout(string url, FriendlyTeammateLoadoutRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.SetTeammateLoadout(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> SaveDefaultEquipment(string url, FriendlyTeammateDefaultEquipmentRequest request, MongoId sessionId)
    {
        try
        {
            var response = teammateService.SaveTeammateDefaultEquipment(sessionId, request);
            return new ValueTask<string>(httpResponse.GetBody(response));
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> BuyKit(string url, FriendlyTeammateBuyKitRequest request, MongoId sessionId)
    {
        try
        {
            var response = teammateService.BuyTeammateKit(sessionId, request);
            return new ValueTask<string>(httpResponse.GetBody(response));
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> RepairDefaultEquipment(string url, FriendlyTeammateRepairEquipmentRequest request, MongoId sessionId)
    {
        try
        {
            var response = teammateService.RepairTeammateDefaultEquipment(sessionId, request);
            return new ValueTask<string>(httpResponse.GetBody(response));
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> SetAggression(string url, FriendlyTeammateAggressionRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.SetTeammateAggression(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> SetProficiency(string url, FriendlyTeammateProficiencyRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.SetTeammateProficiency(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> SetTactic(string url, FriendlyTeammateTacticRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.SetTeammateTactic(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> SetAutoJoin(string url, FriendlyTeammateAutoJoinRequest request, MongoId sessionId)
    {
        try
        {
            teammateService.SetTeammateAutoJoin(sessionId, request);
            return new ValueTask<string>(httpResponse.NullResponse());
        }
        catch (FriendlyTeammateException ex)
        {
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError, errmsg: ex.Message));
        }
    }

    public ValueTask<string> Delete(string url, FriendlyTeammateDeleteRequest request, MongoId sessionId)
    {
        try
        {
            return new ValueTask<string>(
                httpResponse.GetBody(teammateService.DeleteTeammateWithPayment(sessionId, request))
            );
        }
        catch (Exception ex)
        {
            logger.Warning($"Teammate deletion failed: {ex}");
            return new ValueTask<string>(httpResponse.GetBody<object?>(null, err: BackendErrorCodes.UnknownTradingError,
                errmsg: ex is FriendlyTeammateException ? ex.Message : "TeammateDeleteFailed"));
        }
    }
}
