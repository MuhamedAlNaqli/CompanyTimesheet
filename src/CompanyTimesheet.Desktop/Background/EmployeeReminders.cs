using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Company.Api.Client;
using Company.Api.Contracts;
using CompanyTimesheet.Desktop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Timesheet.Domain;

namespace CompanyTimesheet.Desktop.Background;

/// <summary>
/// The reminder of employee mode, run by the scheduled task. The company API decides what is due; this only asks and shows it.
/// When the server cannot be reached (a laptop outside the network) the last answer is reused for what only depends on a
/// date (the submission deadline), so a due date is still honoured, and nothing else is guessed.
/// </summary>
public static class EmployeeReminders
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(12);

    private static string CachePath => Path.Combine(ReminderState.FolderPath, "employee-reminders.json");

    /// <summary>Nobody wants a notification at night or on a day off: the task reminds on working days from 07:00 to 20:00</summary>
    public static bool IsQuiet(DateTime now) =>
        now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || now.Hour < 7 || now.Hour >= 20;

    /// <summary>What to tell the user now, or nothing</summary>
    public static async Task<IReadOnlyList<ReminderDto>> DueAsync(IServiceProvider services, ILogger logger, DateOnly today)
    {
        try
        {
            using var timeout = new CancellationTokenSource(AskTimeout);
            var answer = await services.GetRequiredService<CompanyApiClient>().GetRemindersAsync(timeout.Token);
            Save(answer);
            return answer.Items;
        }
        catch (Exception ex)
        {
            // Out of reach, a login page of a proxy instead of the answer, a refused sign-in: all the same to the user, who
            // is still reminded of the submission deadline from the last answer
            logger.LogInformation("The company server was not available for the reminder ({Type}: {Reason}); using the last known deadlines", ex.GetType().Name, ex.Message);
            return Offline(Load(), today);
        }
    }

    /// <summary>From the last answer, only the "submit" reminders can be recomputed without the server: they depend on the date alone</summary>
    public static IReadOnlyList<ReminderDto> Offline(RemindersDto? last, DateOnly today)
    {
        if (last == null) return Array.Empty<ReminderDto>();

        var result = new List<ReminderDto>();
        foreach (var item in last.Items.Where(i => i.Kind is ReminderType.SubmitSoon or ReminderType.SubmitOverdue))
        {
            var kind = ReminderRules.ForEmployee(today, TimesheetStatus.Draft, item.Deadline);
            if (kind != null) result.Add(item with { Kind = Enum.Parse<ReminderType>(kind.Value.ToString()) });
        }

        return result;
    }

    /// <summary>
    /// Whether to show the notification now. A new item is shown at once (for example a timesheet that has just arrived for
    /// approval); what the user was already told about repeats at most twice a day, as the consultant reminder does.
    /// </summary>
    public static bool ShouldNotify(ReminderState state, IReadOnlyList<ReminderDto> items, DateTime now)
    {
        if (items.Count == 0) return false;
        return Signature(items) != state.EmployeeSignature || state.CanNotify(now);
    }

    public static string Signature(IReadOnlyList<ReminderDto> items) =>
        string.Join("|", items.OrderBy(i => i.Kind).ThenBy(i => i.Year).ThenBy(i => i.Month).Select(i => $"{i.Kind}:{i.Year}-{i.Month}:{i.Count}:{i.SentUtc:O}"));

    /// <summary>The text of one notification for everything that is due</summary>
    public static (string Title, string Message) Compose(IReadOnlyList<ReminderDto> items)
    {
        var ordered = items.OrderBy(i => EmployeeFormat.ReminderOrder(i.Kind)).Select(EmployeeFormat.Reminder).ToList();
        return ordered.Count == 1
            ? ordered[0]
            : (ordered[0].Title, string.Join(" ", ordered.Select(o => o.Message)));
    }

    private static void Save(RemindersDto answer)
    {
        try
        {
            Directory.CreateDirectory(ReminderState.FolderPath);
            ReminderState.WriteAtomically(CachePath, JsonSerializer.Serialize(answer));
        }
        catch
        {
            // Without the file the offline reminder is simply silent
        }
    }

    private static RemindersDto? Load()
    {
        try
        {
            return File.Exists(CachePath) ? JsonSerializer.Deserialize<RemindersDto>(File.ReadAllText(CachePath)) : null;
        }
        catch
        {
            return null;
        }
    }
}
