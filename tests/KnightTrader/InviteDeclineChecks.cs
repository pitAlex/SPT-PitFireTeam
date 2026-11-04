using System.Reflection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HarmonyLib;
using pitTeam.Server.Callbacks;
using pitTeam.Server.Models;
using pitTeam.Server.Services;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Eft.Ws;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;

internal static class InviteDeclineChecks
{
    private static bool accept;
    private static GroupCharacter? character;
    private static UserDialogInfo? sender;
    private static string? reason;
    private static readonly List<WsNotificationEvent> notifications = [];
    private static readonly List<(UserDialogInfo Sender, string Text, MessageType Type)> personal = [];
    private static readonly List<string> system = [];
    private static TaskCompletionSource done = new();

    private static bool Admission(out GroupCharacter? groupCharacter, out string? rejectionReason,
        out UserDialogInfo? rejectionSender, ref bool __result)
    {
        groupCharacter = character; rejectionReason = reason; rejectionSender = sender; __result = accept; return false;
    }
    private static bool Notification(WsNotificationEvent notificationMessage, ref Task __result)
    {
        notifications.Add(notificationMessage); __result = Task.CompletedTask;
        if (accept) done.TrySetResult();
        return false;
    }
    private static bool Personal(UserDialogInfo sender, string message)
    {
        personal.Add((sender, message, MessageType.UserMessage)); done.TrySetResult(); return false;
    }
    private static bool SystemMessage(string message)
    { system.Add(message); done.TrySetResult(); return false; }

    internal static void Run(Action<bool, string> check)
    {
        var harmony = new Harmony("pit.fixture.companion-invite-declines");
        var admission = typeof(FriendlyTeammateService).GetMethods().Single(method =>
            method.Name == nameof(FriendlyTeammateService.TryGetRaidGroupCharacter) && method.GetParameters().Length == 5);
        void Patch(MethodInfo target, string prefix) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(InviteDeclineChecks), prefix));
        Patch(admission, nameof(Admission));
        Patch(AccessTools.Method(typeof(NotificationSendHelper), nameof(NotificationSendHelper.SendMessageAsync)), nameof(Notification));
        Patch(AccessTools.Method(typeof(FriendlyTeammateService), nameof(FriendlyTeammateService.SendCompanionInvitationDecline)), nameof(Personal));
        Patch(AccessTools.Method(typeof(MailSendService), nameof(MailSendService.SendSystemMessageToPlayer)), nameof(SystemMessage));
        try
        {
            var callback = new FriendlyTeammateMatchCallbacks(
                (FriendlyTeammateService)RuntimeHelpers.GetUninitializedObject(typeof(FriendlyTeammateService)), null!, null!, null!,
                (NotificationSendHelper)RuntimeHelpers.GetUninitializedObject(typeof(NotificationSendHelper)),
                (MailSendService)RuntimeHelpers.GetUninitializedObject(typeof(MailSendService)), null!);
            var session = new MongoId();
            foreach (string nickname in new[] { "Knight", "Big Pipe", "Birdeye", "Rogue", "ordinary PMC", "accepted" })
            {
                notifications.Clear(); personal.Clear(); system.Clear(); done = new(TaskCreationOptions.RunContinuationsAsynchronously);
                accept = nickname == "accepted";
                character = new() { Id = new MongoId(), Aid = 123, Info = new() { Nickname = nickname } };
                sender = nickname is "ordinary PMC" or "accepted" ? null : new() { Id = character.Id, Aid = 123, Info = new() { Nickname = nickname } };
                reason = accept ? null : "localized refusal for " + nickname;
                check(callback.SendGroupInvite("", new() { To = "123" }, session, "existing response").Result == "existing response",
                    "native invitation route response preserved: " + nickname);
                check(done.Task.Wait(TimeSpan.FromSeconds(5)), "delayed native reply dispatch finishes: " + nickname);
                if (accept)
                    check(notifications.Single() is FriendlyGroupMatchInviteAccept && personal.Count == 0 && system.Count == 0,
                        "successful invitation sends no decline chat");
                else
                {
                    check(notifications.Single() is FriendlyGroupMatchInviteDecline decline && decline.Aid == "123" && decline.Nickname == nickname,
                        "native decline notification retains invited identity: " + nickname);
                    if (sender != null)
                        check(personal.Count == 1 && personal[0].Sender == sender && personal[0].Text == reason &&
                            personal[0].Type == MessageType.UserMessage && system.Count == 0,
                            "companion sends one personal hint and no duplicate system message: " + nickname);
                    else
                        check(personal.Count == 0 && system.SequenceEqual([reason!]), "ordinary rejection keeps its system message");
                }
            }
            notifications.Clear(); personal.Clear(); system.Clear(); done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            accept = false; reason = "stale mode refusal"; sender = new() { Id = new MongoId(), Aid = 123 };
            callback.SendGroupInvite("", new() { To = "123" }, session, "existing response").GetAwaiter().GetResult();
            FriendlyModeRequestGate.ModeChanged();
            check(!done.Task.Wait(TimeSpan.FromMilliseconds(1400)) && notifications.Count == 0 && personal.Count == 0 && system.Count == 0,
                "mode switch suppresses pending native decline and personal reply together");
            harmony.Unpatch(AccessTools.Method(typeof(FriendlyTeammateService), nameof(FriendlyTeammateService.SendCompanionInvitationDecline)),
                HarmonyPatchType.Prefix, harmony.Id);
            Persistence(check);
        }
        finally { harmony.UnpatchSelf(); }
    }

    private static void Persistence(Action<bool, string> check)
    {
        var session = new MongoId();
        var profile = new SptProfile { DialogueRecords = [] };
        var save = (SaveServer)RuntimeHelpers.GetUninitializedObject(typeof(SaveServer));
        typeof(SaveServer).GetField("profiles", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(save,
            new ConcurrentDictionary<MongoId, SptProfile>([new(session, profile)]));
        var templates = (TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Items))!.SetValue(templates, new Dictionary<MongoId, TemplateItem>());
        var dependencies = new Dictionary<Type, object> { [typeof(SaveServer)] = save, [typeof(TemplateTable)] = templates,
            [typeof(TimeUtil)] = new TimeUtil(), [typeof(NotifierHelper)] = new NotifierHelper(null!),
            [typeof(NotificationSendHelper)] = RuntimeHelpers.GetUninitializedObject(typeof(NotificationSendHelper)) };
        T Make<T>() => (T)typeof(T).GetConstructors().Single().Invoke(typeof(T).GetConstructors().Single().GetParameters()
            .Select(parameter => dependencies.GetValueOrDefault(parameter.ParameterType)).ToArray());
        dependencies[typeof(ProfileHelper)] = Make<ProfileHelper>();
        dependencies[typeof(DialogueHelper)] = Make<DialogueHelper>();
        dependencies[typeof(MailSendService)] = Make<MailSendService>();
        var service = Make<FriendlyTeammateService>();
        notifications.Clear();
        foreach (string nickname in new[] { "Knight", "Big Pipe", "Birdeye", "Rogue" })
        {
            var sender = new UserDialogInfo { Id = new MongoId(), Aid = 123, Info = new() { Nickname = nickname } };
            service.SendCompanionInvitationDecline(session, sender, "first hint");
            var dialog = profile.DialogueRecords![sender.Id];
            dialog.Pinned = true;
            service.SendCompanionInvitationDecline(session, sender, "second hint");
            check(dialog.Messages!.Select(message => message.Text).SequenceEqual(["first hint", "second hint"]),
                "native mail persists first and repeat decline in one conversation: " + nickname);
            check(dialog.Users!.Count(user => user.Id == sender.Id) == 1 && dialog.Users!.Single().Info?.Nickname == nickname,
                "native conversation retains one named companion sender: " + nickname);
            check(dialog.Type == MessageType.UserMessage && dialog.Pinned == true && dialog.New == 2 && dialog.AttachmentsNew == 0 &&
                dialog.Messages!.All(message => message.UserId == sender.Id && message.MessageType == MessageType.UserMessage && message.HasRewards != true),
                "repeat hints preserve conversation state without reward attachments: " + nickname);
        }
        check(notifications.Count == 8 && notifications.All(notification => notification is WsChatMessageReceived),
            "native mail emits one chat notification for each persisted decline");
    }
}
