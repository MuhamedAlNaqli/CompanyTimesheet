using System.Globalization;
using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Services;

/// <summary>
/// What is due for one person today. The rules are the domain library's <see cref="ReminderRules"/>; this service only
/// gathers the facts (the employee's months, the sheets waiting for them) so the banner in the app and the scheduled task
/// show the same thing. It says what is due, not how to word it.
/// </summary>
public sealed class ReminderService
{
    private readonly CompanyDbContext _db;
    private readonly WorkflowService _workflow;
    private readonly TimeProvider _time;
    private const int MonthsLookedBack = 6;
    private const int NudgeLifetimeDays = 7;
    private readonly DateOnly? _startFrom;

    public ReminderService(CompanyDbContext db, WorkflowService workflow, TimeProvider time, IConfiguration configuration)
    {
        _db = db;
        _workflow = workflow;
        _time = time;

        // Months before the go-live month are ignored, so nobody is told off for a month that was never meant to be entered
        var start = configuration["Reminders:StartFrom"];
        if (DateOnly.TryParseExact(start + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) _startFrom = parsed;
    }

    public async Task<RemindersDto> ForAsync(string employeeCode)
    {
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var items = new List<ReminderDto>();

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Code == employeeCode);
        if (employee == null || !employee.IsActive) return new RemindersDto(today, items);

        // Returned sheets wait for the employee whatever the month
        var returned = await _db.Sheets.AsNoTracking().Where(s => s.EmployeeCode == employeeCode && s.Status == TimesheetStatus.Returned)
            .OrderBy(s => s.Year).ThenBy(s => s.Month).Select(s => new { s.Year, s.Month }).ToListAsync();
        foreach (var sheet in returned)
        {
            var deadline = await SubmissionDeadlineAsync(sheet.Year, sheet.Month);
            items.Add(new ReminderDto(ReminderType.FixReturned, sheet.Year, sheet.Month, 1, deadline));
        }

        // Every month from the first one that counts up to this one: a month that was not submitted stays overdue until it is.
        // Without a start month the last six are looked at, so a long absence does not fill the screen.
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var firstMonth = thisMonth.AddMonths(-MonthsLookedBack);
        if (_startFrom is { } begin && begin > firstMonth) firstMonth = new DateOnly(begin.Year, begin.Month, 1);
        if (employee.ActiveFrom is { } hired && hired > firstMonth) firstMonth = new DateOnly(hired.Year, hired.Month, 1);

        var sheets = await _db.Sheets.AsNoTracking().Where(s => s.EmployeeCode == employeeCode && (s.Year * 12 + s.Month) >= firstMonth.Year * 12 + firstMonth.Month)
            .Select(s => new { s.Year, s.Month, s.Status }).ToListAsync();

        for (var first = firstMonth; first <= thisMonth; first = first.AddMonths(1))
        {
            var last = new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month));
            if (_startFrom is { } start && last < start) continue;
            if (employee.ActiveFrom is { } from && from > last) continue;
            if (employee.ActiveTo is { } to && to < first) continue;

            var status = sheets.FirstOrDefault(s => s.Year == first.Year && s.Month == first.Month)?.Status ?? TimesheetStatus.Draft;
            if (status != TimesheetStatus.Draft) continue;

            var deadline = await SubmissionDeadlineAsync(first.Year, first.Month);
            var kind = ReminderRules.ForEmployee(today, status, deadline);
            if (kind != null) items.Add(new ReminderDto(kind.Value.ToDto(), first.Year, first.Month, 1, deadline));
        }

        // Timesheets that wait for this person as an approver
        var waiting = await _workflow.PendingAsync(employeeCode);
        if (waiting.Count > 0)
        {
            var oldest = waiting.OrderBy(w => w.Year).ThenBy(w => w.Month).First();
            var deadline = Deadlines.Approval(oldest.Year, oldest.Month);
            var kind = ReminderRules.ForApprover(today, deadline, waiting.Count);
            if (kind != null) items.Add(new ReminderDto(kind.Value.ToDto(), oldest.Year, oldest.Month, waiting.Count, deadline));
        }

        await AddNudgesAsync(employeeCode, items);
        return new RemindersDto(today, items);
    }

    /// <summary>
    /// A supervisor's request to submit a month, as long as it is recent and the month is still not submitted. It is the supervisor's
    /// word, not a rule: the app shows who asked, and the notification is new each time (it carries the moment it was sent).
    /// </summary>
    private async Task AddNudgesAsync(string employeeCode, List<ReminderDto> items)
    {
        var since = _time.GetUtcNow().UtcDateTime.AddDays(-NudgeLifetimeDays);
        var recent = await _db.TeamNudges.AsNoTracking().Where(n => n.EmployeeCode == employeeCode && n.CreatedUtc >= since).ToListAsync();
        foreach (var latest in recent.GroupBy(n => (n.Year, n.Month)).Select(g => g.OrderByDescending(n => n.CreatedUtc).First()))
        {
            var status = await _db.Sheets.AsNoTracking().Where(s => s.EmployeeCode == employeeCode && s.Year == latest.Year && s.Month == latest.Month)
                .Select(s => (TimesheetStatus?)s.Status).FirstOrDefaultAsync() ?? TimesheetStatus.Draft;
            if (status is not (TimesheetStatus.Draft or TimesheetStatus.Returned)) continue;

            var from = await _db.Employees.AsNoTracking().Where(e => e.Code == latest.FromCode).Select(e => e.Name).FirstOrDefaultAsync() ?? latest.FromCode;
            var deadline = await SubmissionDeadlineAsync(latest.Year, latest.Month);
            items.Add(new ReminderDto(ReminderType.Nudged, latest.Year, latest.Month, 1, deadline, from, DateTime.SpecifyKind(latest.CreatedUtc, DateTimeKind.Utc)));
        }
    }

    private async Task<DateOnly> SubmissionDeadlineAsync(int year, int month) =>
        Deadlines.Submission(year, month, await Reference.CalendarAsync(_db, year));
}
