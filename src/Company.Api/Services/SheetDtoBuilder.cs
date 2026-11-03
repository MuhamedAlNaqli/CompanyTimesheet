using Company.Api.Contracts;
using Company.Api.Data;
using Company.Api.Texts;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Services;

/// <summary>
/// Turns a stored month into what the desktop app shows: days, overtime split into codes, totals, the expected fund, the
/// findings of the rules and the position in the approval chain. Everything derived comes from the domain library. This is
/// the one place that reads a sheet for display, for the employee, the approvers, the printed sheet and the reminders.
/// </summary>
public sealed class SheetDtoBuilder
{
    private readonly CompanyDbContext _db;
    private readonly Dictionary<int, WorkCalendar> _calendars = new();
    private readonly DeputyService _deputies;

    public SheetDtoBuilder(CompanyDbContext db, DeputyService deputies)
    {
        _db = db;
        _deputies = deputies;
    }

    public static bool IsEditable(MonthSheet sheet) => sheet.Status is TimesheetStatus.Draft or TimesheetStatus.Returned;

    public static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ---- Context -------------------------------------------------------------------------------------------------------

    internal sealed record Context(WorkCalendar Calendar, HashSet<DateOnly> Religious, EmployeeStatus Employee);

    /// <summary>The calendar of a year, read once per request (a printed sheet of many employees asks for it many times)</summary>
    public async Task<WorkCalendar> CalendarAsync(int year)
    {
        if (_calendars.TryGetValue(year, out var known)) return known;
        return _calendars[year] = await Reference.CalendarAsync(_db, year);
    }

    internal async Task<Context> ContextAsync(string employeeCode, int year)
    {
        var calendar = await CalendarAsync(year);
        var religious = (await _db.ReligiousHolidays.AsNoTracking().Where(h => h.EmployeeCode == employeeCode).Select(h => h.Date).ToListAsync()).ToHashSet();
        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Code == employeeCode)
                       ?? throw new SheetException(404, "not-found", "The employee was not found.");
        return new Context(calendar, religious, new EmployeeStatus(true, employee.ActiveFrom, employee.ActiveTo));
    }

    // ---- The month -------------------------------------------------------------------------------------------------------

    /// <summary>The month as <paramref name="viewer"/> sees it (the owner when null): only the owner may edit</summary>
    public async Task<SheetDto> BuildAsync(MonthSheet sheet, string? viewer = null)
    {
        var context = await ContextAsync(sheet.EmployeeCode, sheet.Year);
        var settings = WorkingTimeSettings.Default;
        bool InMonth(DateOnly d) => d.Year == sheet.Year && d.Month == sheet.Month;

        var entries = sheet.Entries.Select(e => new DayEntry(e.Date, e.Code, e.Minutes)).ToList();
        var records = sheet.Overtime.OrderBy(o => o.Date).ThenBy(o => o.Start).ToList();

        var classified = records.ToDictionary(
            r => r.Id,
            r => OvertimeClassifier.Classify(new OvertimeInterval(r.Date, r.Start, r.End), d => context.Calendar.DayTypeOf(d, context.Religious), settings));
        var monthAllocations = classified.Values.SelectMany(c => c.Allocations).Where(a => InMonth(a.Date)).ToList();

        // Work that began on the last day of the previous month and goes on after midnight belongs to this month in part
        var carried = await CarriedInAsync(sheet, context, settings);
        monthAllocations.AddRange(carried.SelectMany(c => c.Allocations));

        var summary = MonthlySummaryCalculator.Compute(entries, monthAllocations, settings, context.Calendar);
        var expected = MonthlySummaryCalculator.ExpectedFundMinutes(sheet.Year, sheet.Month, settings, context.Employee.ActiveFrom, context.Employee.ActiveTo);

        var data = new TimesheetData(
            sheet.EmployeeCode, sheet.Year, sheet.Month, entries,
            records.Select(r => new OvertimeRecord(r.Date, r.Start, r.End, r.Description)).ToList(),
            context.Calendar, context.Religious, settings, context.Employee);
        var issues = TimesheetValidator.Validate(data);

        var days = WorkCalendar.DaysOf(sheet.Year, sheet.Month).Select(date =>
        {
            var info = context.Calendar.Resolve(date, context.Religious);
            return new DayDto(
                date,
                BosnianTexts.WeekdayInitial(date),
                info.Type.ToDto(),
                info.HolidayName,
                sheet.Entries.Where(e => e.Date == date).OrderBy(e => e.Code).Select(e => new EntryDto(e.Code, e.Minutes)).ToList(),
                monthAllocations.Where(a => a.Date == date).Select(a => new AllocationDto(a.Date, a.Code, a.Minutes)).ToList());
        }).ToList();

        var overtime = records.Select(r =>
        {
            var c = classified[r.Id];
            return new OvertimeDto(
                r.Id, r.Date, r.Start, r.End, r.Description, c.TotalMinutes,
                c.Allocations.Select(a => new AllocationDto(a.Date, a.Code, a.Minutes)).ToList(),
                c.Conflicts.Count > 0,
                c.Allocations.Where(a => !InMonth(a.Date)).Sum(a => a.Minutes));
        }).ToList();

        var carriedIn = carried.Select(c => new CarriedOvertimeDto(c.Record.Date, c.Record.Start, c.Record.End, c.Record.Description, c.Allocations.Sum(a => a.Minutes))).ToList();

        return new SheetDto(
            sheet.Year, sheet.Month, sheet.Status.ToDto(), sheet.Revision,
            IsEditable(sheet) && Same(viewer ?? sheet.EmployeeCode, sheet.EmployeeCode),
            days, overtime,
            new SummaryDto(summary.MinutesByCode, summary.DaysByCode, summary.FundMinutes, expected, summary.OvertimeMinutes),
            issues.Select(i => new IssueDto(i.RuleId, i.Severity.ToDto(), i.Date, ValidationTexts.Message(i))).ToList(),
            await WorkflowOfAsync(sheet, viewer ?? sheet.EmployeeCode, issues.Any(i => i.Severity == Severity.Blocking)),
            carriedIn);
    }

    private sealed record Carried(SheetOvertime Record, IReadOnlyList<OvertimeAllocation> Allocations);

    /// <summary>
    /// The records of the previous month that cross midnight on its last day, with the allocations that fall in this month.
    /// The part before midnight is counted in the month it began; the part after it here, so no minute is paid twice or not at all.
    /// </summary>
    private async Task<List<Carried>> CarriedInAsync(MonthSheet sheet, Context context, WorkingTimeSettings settings)
    {
        var firstOfMonth = new DateOnly(sheet.Year, sheet.Month, 1);
        var lastOfPrevious = firstOfMonth.AddDays(-1);

        var previous = await (from o in _db.SheetOvertime.AsNoTracking()
                              join s in _db.Sheets.AsNoTracking() on o.SheetId equals s.Id
                              where s.EmployeeCode == sheet.EmployeeCode && s.Year == lastOfPrevious.Year && s.Month == lastOfPrevious.Month && o.Date == lastOfPrevious
                              select o).ToListAsync();
        if (previous.Count == 0) return new List<Carried>();

        // Around New Year the holidays of the previous year decide the type of the part before midnight
        var calendar = lastOfPrevious.Year == sheet.Year ? context.Calendar : await CalendarAsync(lastOfPrevious.Year);

        var result = new List<Carried>();
        foreach (var record in previous.OrderBy(o => o.Start))
        {
            var interval = new OvertimeInterval(record.Date, record.Start, record.End);
            if (!interval.CrossesMidnight || interval.TotalMinutes == 0) continue;

            var parts = OvertimeClassifier.Classify(interval, d => calendar.DayTypeOf(d, context.Religious), settings)
                .Allocations.Where(a => a.Date.Year == sheet.Year && a.Date.Month == sheet.Month).ToList();
            if (parts.Count > 0) result.Add(new Carried(record, parts));
        }

        return result;
    }

    // ---- Position in the approval chain --------------------------------------------------------------------------------

    private async Task<WorkflowDto> WorkflowOfAsync(MonthSheet sheet, string viewer, bool hasBlocking)
    {
        var chain = sheet.ChainCodes;
        var codes = chain.Append(sheet.EmployeeCode)
            .Concat(sheet.Messages.Select(m => m.FromCode))
            .Concat(sheet.Events.Select(e => e.ActorCode))
            .Concat(sheet.Events.Where(e => e.OnBehalfOf != null).Select(e => e.OnBehalfOf!))
            .Distinct().ToList();
        var names = await _db.Employees.AsNoTracking().Where(e => codes.Contains(e.Code)).ToDictionaryAsync(e => e.Code, e => e.Name);
        string Name(string code) => names.GetValueOrDefault(code, code);

        var isOwner = Same(viewer, sheet.EmployeeCode);
        var current = sheet.Status == TimesheetStatus.Submitted && sheet.Step < chain.Count ? chain[sheet.Step] : null;
        var revisions = sheet.Id == 0
            ? new List<RevisionDto>()
            : (await _db.SheetRevisions.AsNoTracking().Where(r => r.SheetId == sheet.Id).OrderBy(r => r.Revision).Select(r => new { r.Revision, r.SubmittedUtc }).ToListAsync())
                .Select(r => new RevisionDto(r.Revision, DateTime.SpecifyKind(r.SubmittedUtc, DateTimeKind.Utc))).ToList();

        // A deputy decides for the approver whose turn it is while that approver is away
        var actsForCurrent = current != null && !isOwner && !Same(viewer, current) && await _deputies.IsDeputyForAsync(viewer, current);

        return new WorkflowDto(
            sheet.EmployeeCode,
            Name(sheet.EmployeeCode),
            chain.Select(c => new ChainStepDto(c, Name(c))).ToList(),
            sheet.Step,
            current,
            current == null ? null : Name(current),
            sheet.Messages.OrderBy(m => m.Position).Select(m => new MessageDto(m.Position, m.FromCode, Name(m.FromCode), m.Text, m.Revision, m.Answer)).ToList(),
            sheet.Events.OrderBy(e => e.AtUtc).ThenBy(e => e.Id)
                .Select(e => new HistoryDto(DateTime.SpecifyKind(e.AtUtc, DateTimeKind.Utc), e.Kind.ToDto(), e.ActorCode, Name(e.ActorCode), e.OnBehalfOf, e.Revision, e.Step, e.Text)).ToList(),
            revisions,
            isOwner && IsEditable(sheet) && !hasBlocking && sheet.Messages.All(m => !string.IsNullOrWhiteSpace(m.Answer)),
            // The owner decides on their own timesheet only when nobody is above them (the chain is themselves alone)
            current != null && (Same(viewer, current) || actsForCurrent) && (!isOwner || (chain.Count == 1 && Same(chain[0], sheet.EmployeeCode))));
    }
}
