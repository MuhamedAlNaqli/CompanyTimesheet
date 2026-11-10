using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Services;

/// <summary>
/// The figures of the start screen of an employee: this month (the fund, the overtime, where the timesheet stands), the year so far
/// (hours worked, overtime, how many of the past months were submitted) and what waits for the person to approve. It only counts what the
/// rest of the server keeps.
/// </summary>
public sealed class EmployeeOverviewService
{
    private readonly CompanyDbContext _db;
    private readonly TimesheetService _sheets;
    private readonly WorkflowService _workflow;
    private readonly TimeProvider _time;

    public EmployeeOverviewService(CompanyDbContext db, TimesheetService sheets, WorkflowService workflow, TimeProvider time)
    {
        _db = db;
        _sheets = sheets;
        _workflow = workflow;
        _time = time;
    }

    public async Task<EmployeeOverviewDto> BuildAsync(string employeeCode)
    {
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var current = await _sheets.GetAsync(employeeCode, today.Year, today.Month);

        var employee = await _db.Employees.AsNoTracking().FirstAsync(e => e.Code == employeeCode);
        var mine = await _db.Sheets.AsNoTracking().Where(s => s.EmployeeCode == employeeCode && s.Year == today.Year).ToListAsync();
        var ids = mine.Select(s => s.Id).ToList();

        // Hours of regular work in the year, and the overtime of the year (a record that goes past midnight is counted whole)
        var worked = await _db.SheetEntries.AsNoTracking().Where(e => ids.Contains(e.SheetId) && e.Code == Codes.RegularWork).SumAsync(e => (int?)e.Minutes) ?? 0;
        var overtime = (await _db.SheetOvertime.AsNoTracking().Where(o => ids.Contains(o.SheetId)).ToListAsync())
            .Sum(o => new OvertimeInterval(o.Date, o.Start, o.End).TotalMinutes);

        // The months before this one in which the person was employed, and how many of them were handed in
        var past = Enumerable.Range(1, today.Month - 1)
            .Where(m => employee.ActiveFrom is not { } from || new DateOnly(today.Year, m, 1).AddMonths(1).AddDays(-1) >= from)
            .Where(m => employee.ActiveTo is not { } to || new DateOnly(today.Year, m, 1) <= to)
            .ToList();
        var handedIn = past.Count(m => mine.Any(s => s.Month == m && s.Status is TimesheetStatus.Submitted or TimesheetStatus.Approved));

        var pending = employee.IsAdmin ? 0 : (await _workflow.PendingAsync(employeeCode)).Count;

        return new EmployeeOverviewDto(
            today.Year, today.Month, current.Status,
            current.Summary.FundMinutes, current.Summary.ExpectedFundMinutes, current.Summary.OvertimeMinutes,
            worked, overtime, handedIn, past.Count, pending);
    }
}
