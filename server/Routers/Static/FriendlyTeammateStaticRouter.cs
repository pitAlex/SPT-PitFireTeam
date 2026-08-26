using pitTeam.Server.Callbacks;
using pitTeam.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Routers.Static;

[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class FriendlyTeammateStaticRouter(JsonUtil jsonUtil, FriendlyTeammateCallbacks callbacks)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<FriendlyTeammateCreateRequest>(
                "/singleplayer/pitfireteam/teammate/create",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.Create(url, info, sessionId)
            ),
            new RouteAction<EmptyRequestData>(
                "/singleplayer/pitfireteam/teammates",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.List(url, info, sessionId)
            ),
            new RouteAction<EmptyRequestData>(
                "/singleplayer/autoteam",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.ListAutoJoin(url, info, sessionId)
            ),
            new RouteAction<FriendlyServerSettingsRequest>(
                "/singleplayer/pitfireteam/settings",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.SetServerSettings(url, info, sessionId)
            ),
            new RouteAction<EmptyRequestData>(
                "/singleplayer/pitfireteam/lostondeath",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.GetLostOnDeathSettings(url, info, sessionId)
            ),
            new RouteAction<EmptyRequestData>(
                "/singleplayer/pitfireteam/recovery-notice",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.GetStartupRecoveryNotice(url, info, sessionId)
            ),
            new RouteAction<EmptyRequestData>(
                "/singleplayer/pitfireteam/recovery-notice/ack",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.AcknowledgeStartupRecoveryNotice(url, info, sessionId)
            ),
            new RouteAction<GetOtherProfileRequest>(
                "/singleplayer/pitfireteam/teammate/profile",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.GetProfile(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateProfileOptionsRequest>(
                "/singleplayer/pitfireteam/teammate/profile/options",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.GetProfileOptions(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateSuitRequest>(
                "/singleplayer/pitfireteam/teammate/profile/suit",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.SetSuit(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateRenameRequest>(
                "/singleplayer/pitfireteam/teammate/profile/rename",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.Rename(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateLoadoutRequest>(
                "/singleplayer/pitfireteam/teammate/profile/loadout",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.SetLoadout(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateDefaultEquipmentRequest>(
                "/singleplayer/pitfireteam/teammate/profile/default-equipment",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.SaveDefaultEquipment(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateBuyKitRequest>(
                "/singleplayer/pitfireteam/teammate/profile/buy-kit",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.BuyKit(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateRepairEquipmentRequest>(
                "/singleplayer/pitfireteam/teammate/profile/repair-equipment",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.RepairDefaultEquipment(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateAggressionRequest>(
                "/singleplayer/pitfireteam/teammate/profile/aggression",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.SetAggression(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateProficiencyRequest>(
                "/singleplayer/pitfireteam/teammate/profile/proficiency",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.SetProficiency(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateTacticRequest>(
                "/singleplayer/pitfireteam/teammate/profile/tactic",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.SetTactic(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateAutoJoinRequest>(
                "/singleplayer/pitfireteam/teammate/autojoin",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.SetAutoJoin(url, info, sessionId)
            ),
            new RouteAction<FriendlyTeammateDeleteRequest>(
                "/singleplayer/pitfireteam/teammate/delete",
                async (url, info, sessionId, output, cancellationToken) => await callbacks.Delete(url, info, sessionId)
            ),
        ]
    )
{ }
