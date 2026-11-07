using pitTeam.GoingRogue.Quests;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace pitTeam.GoingRogue.Server;

internal static class KnightQuestWaits
{
    // EFT reads availableAfter as Int32 even though SPT stores it as double?.
    // Preserve valid fractional status-history timestamps; only unlock deadlines
    // need whole seconds. Repair saved records regardless of their current status.
    internal static bool NormalizeAvailableAfter(BotBase profile)
    {
        bool changed = false;
        foreach (var quest in profile.Quests ?? [])
        {
            if (!KnightQuestPolicy.Owns(quest.QId.ToString()) || quest.AvailableAfter is not { } value ||
                !double.IsFinite(value)) continue;
            double whole = Math.Ceiling(value);
            if (whole == value) continue;
            quest.AvailableAfter = whole;
            changed = true;
        }
        return changed;
    }

    // SPT starts another wait when accepting a delayed quest absent from the
    // profile. Backfill the normal pending record from its prerequisite's real
    // success time, including quests introduced after that success was saved.
    internal static bool Synchronize(BotBase profile, IEnumerable<Quest> definitions)
    {
        bool changed = false;
        foreach (var definition in definitions.Where(q => KnightQuestPolicy.Owns(q.Id.ToString())))
        {
            var waits = definition.Conditions.AvailableForStart?
                .Where(c => c.ConditionType == "Quest" && c.AvailableAfter > 0).ToArray() ?? [];
            if (waits.Length == 0) continue;
            double deadline = 0, completedAt = 0;
            bool known = true;
            foreach (var wait in waits)
            {
                var targets = wait.Target?.IsList == true ? wait.Target.List :
                    wait.Target?.Item is { } target ? [target] : [];
                var prerequisite = profile.Quests?.FirstOrDefault(q => q.Status == QuestStatusEnum.Success &&
                    wait.Status?.Contains(q.Status) == true && targets?.Contains(q.QId.ToString()) == true);
                if (prerequisite?.StatusTimers?.TryGetValue(QuestStatusEnum.Success, out double success) != true ||
                    !double.IsFinite(success) || success <= 0) { known = false; break; }
                completedAt = Math.Max(completedAt, success);
                deadline = Math.Max(deadline, success + wait.AvailableAfter!.Value);
            }
            if (!known) continue;
            // Never shorten the native prerequisite wait when success includes milliseconds.
            deadline = Math.Ceiling(deadline);
            var current = profile.Quests?.FirstOrDefault(q => q.QId == definition.Id);
            if (current == null)
            {
                profile.Quests ??= [];
                profile.Quests.Add(new QuestStatus
                {
                    QId = definition.Id, StartTime = 0, Status = QuestStatusEnum.AvailableAfter,
                    StatusTimers = new() { [QuestStatusEnum.AvailableAfter] = completedAt },
                    CompletedConditions = [], AvailableAfter = deadline
                });
                changed = true;
            }
            else if (current.Status == QuestStatusEnum.AvailableAfter && current.AvailableAfter != deadline)
            {
                current.AvailableAfter = deadline;
                changed = true;
            }
        }
        return changed;
    }
}
