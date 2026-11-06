using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Services;

/// <summary>
/// What the administrator sees first: how many people and codes there are, what needs attention (people without a user name, locked
/// accounts, nobody at the top of the chart, no holidays entered), how far the timesheets of this and the last month are, and the latest
/// events of the audit log. It only counts; every figure comes from the data the rest of the server keeps.
/// </summary>
public sealed class AdminOverviewService
{
    private readonly CompanyDbContext _db;
    private readonly TimeProvider _time;
    private readonly IConfiguration _configuration;

    public AdminOverviewService(CompanyDbContext db, TimeProvider time, IConfiguration configuration)
    {
        _db = db;
        _time = time;
        _configuration = configuration;
    }

    public async Task<AdminOverviewDto> BuildAsync()
    {
        var utcNow = _time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

        // A server that runs for years reaches a new year without a restart: the holidays of the year after are written here too
        if (_configuration.GetValue("Holidays:SeedFixed", false)) await HolidayCatalog.SeedAsync(_db, today.Year, today.Year + 1);

        var employees = await _db.Employees.AsNoTracking().Select(e => new { e.Code, e.IsActive, e.IsAdmin, e.SupervisorCode, e.DesignatedApproverCode, e.ActiveFrom, e.ActiveTo }).ToListAsync();
        var credentials = await _db.Credentials.AsNoTracking().Select(c => new { c.EmployeeCode, c.LockedUntilUtc }).ToListAsync();
        var withName = credentials.Select(c => c.EmployeeCode).ToHashSet();
        var active = employees.Where(e => e.IsActive).ToList();
        var known = employees.Select(e => e.Code).ToHashSet();

        var people = new OverviewPeopleDto(
            employees.Count,
            active.Count,
            employees.Count - active.Count,
            active.Count(e => !withName.Contains(e.Code)),
            credentials.Count(c => c.LockedUntilUtc is { } until && until > utcNow),
            active.Count(e => e.IsAdmin),
            // The top of the chart: nobody above them. One or two is normal; more means supervisors that were forgotten.
            active.Count(e => !e.IsAdmin && (e.SupervisorCode == null || e.SupervisorCode == "" || !known.Contains(e.SupervisorCode))));

        var codeRows = await _db.Codes.AsNoTracking().Select(c => new { c.Colour, c.IsActive }).ToListAsync();
        var activeCodes = codeRows.Where(c => c.IsActive).ToList();
        var codes = new OverviewCodesDto(
            codeRows.Count, activeCodes.Count, codeRows.Count - activeCodes.Count,
            activeCodes.Count(c => c.Colour == "Green"), activeCodes.Count(c => c.Colour == "Blue"),
            activeCodes.Count(c => c.Colour == "Orange"), activeCodes.Count(c => c.Colour == "Red"));

        var previous = today.AddMonths(-1);
        var holidaysThis = await _db.StateHolidays.CountAsync(h => h.Date.Year == today.Year);
        var holidaysNext = await _db.StateHolidays.CountAsync(h => h.Date.Year == today.Year + 1);

        var events = await _db.AuditEvents.AsNoTracking().OrderByDescending(e => e.Id).Take(6).ToListAsync();
        var audit = new OverviewAuditDto(
            await _db.AuditEvents.LongCountAsync(),
            events.Count == 0 ? null : DateTime.SpecifyKind(events[0].AtUtc, DateTimeKind.Utc),
            events.Select(e => new AuditEventDto(e.Id, e.AtUtc, e.Actor, e.Action, e.EntityType, e.EntityId, e.Details)).ToList());

        return new AdminOverviewDto(
            people, codes,
            await MonthAsync(employees.Select(e => (e.Code, e.IsActive, e.IsAdmin, e.ActiveFrom, e.ActiveTo)).ToList(), today.Year, today.Month),
            await MonthAsync(employees.Select(e => (e.Code, e.IsActive, e.IsAdmin, e.ActiveFrom, e.ActiveTo)).ToList(), previous.Year, previous.Month),
            new OverviewHolidaysDto(today.Year, holidaysThis, holidaysNext),
            audit);
    }

    /// <summary>The timesheets of one month: who has to submit, and how far each one is</summary>
    private async Task<OverviewMonthDto> MonthAsync(List<(string Code, bool IsActive, bool IsAdmin, DateOnly? From, DateOnly? To)> employees, int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        // Who has a timesheet this month: active people who are not administrators and were employed in it
        var expected = employees.Where(e => e.IsActive && !e.IsAdmin && (e.From == null || e.From <= last) && (e.To == null || e.To >= first)).Select(e => e.Code).ToHashSet();

        var sheets = await _db.Sheets.AsNoTracking().Where(s => s.Year == year && s.Month == month).Select(s => new { s.EmployeeCode, s.Status }).ToListAsync();
        var mine = sheets.Where(s => expected.Contains(s.EmployeeCode)).ToList();

        int Count(TimesheetStatus status) => mine.Count(s => s.Status == status);

        return new OverviewMonthDto(year, month, expected.Count, expected.Count - mine.Count, Count(TimesheetStatus.Draft), Count(TimesheetStatus.Submitted),
            Count(TimesheetStatus.Returned), Count(TimesheetStatus.Approved));
    }
}
