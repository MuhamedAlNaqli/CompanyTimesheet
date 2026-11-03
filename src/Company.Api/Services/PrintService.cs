using Company.Api.Contracts;
using Company.Api.Data;
using Company.Api.Printing;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Services;

public sealed record PrintedFile(byte[] Bytes, string ContentType, string FileName);

/// <summary>
/// The printed timesheet in the layout of the current Excel sheet: one employee's month (for the owner and the people of the
/// chain), or the approved months of everybody (for finance and the administrator). Opening the sheets of others is audited.
/// </summary>
public sealed class PrintService
{
    private const string ExcelType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string PdfType = "application/pdf";

    // Drawing a sheet takes memory and processor time; a few at a time is plenty for a company of this size
    private static readonly SemaphoreSlim Renders = new(2);

    private readonly CompanyDbContext _db;
    private readonly TimesheetService _sheets;
    private readonly SheetDtoBuilder _builder;
    private readonly WorkflowService _workflow;
    private readonly AuditService _audit;
    private readonly TeamService _team;

    public PrintService(CompanyDbContext db, TimesheetService sheets, SheetDtoBuilder builder, WorkflowService workflow, AuditService audit, TeamService team)
    {
        _team = team;
        _db = db;
        _sheets = sheets;
        _builder = builder;
        _workflow = workflow;
        _audit = audit;
    }

    /// <summary>The caller's own month, in any status (the status is printed on it)</summary>
    public async Task<PrintedFile> OwnAsync(string employeeCode, int year, int month, string format)
    {
        CheckFormat(format);
        var sheet = await _sheets.LoadOrCreateAsync(employeeCode, year, month);
        return await RenderAsync(new[] { sheet }, year, month, format, single: true);
    }

    /// <summary>Somebody else's month, for the people of its approval chain</summary>
    public async Task<PrintedFile> ReviewAsync(string viewerCode, string ownerCode, int year, int month, string format)
    {
        CheckFormat(format);
        var sheet = await _workflow.FindForReviewAsync(viewerCode, ownerCode, year, month);
        await _workflow.AuditViewAsync(sheet, viewerCode, "SheetPrinted", format);
        return await RenderAsync(new[] { sheet }, year, month, format, single: true);
    }

    /// <summary>
    /// The month of everybody the caller supervises (their direct reports, and the people they are the designated approver
    /// for), on one sheet, whatever the status: the status is printed under each name.
    /// </summary>
    public async Task<PrintedFile> TeamAsync(string supervisorCode, int year, int month, string format)
    {
        CheckFormat(format);
        var team = await _db.Employees.AsNoTracking()
            .Where(e => e.IsActive && e.Code != supervisorCode && (e.SupervisorCode == supervisorCode || (e.SupervisorCode == null && e.DesignatedApproverCode == supervisorCode)))
            .OrderBy(e => e.Code).Select(e => e.Code).ToListAsync();
        if (team.Count == 0) throw new SheetException(404, "no-team", "Nobody reports to you.");

        var stored = await _db.Sheets.AsNoTracking().WithDetails()
            .Where(s => s.Year == year && s.Month == month && team.Contains(s.EmployeeCode)).ToListAsync();

        // A month nobody has opened yet is an empty draft; it is printed but not saved
        var sheets = team.Select(code => stored.FirstOrDefault(s => s.EmployeeCode == code)
                                         ?? new MonthSheet { EmployeeCode = code, Year = year, Month = month }).ToList();

        _audit.Record("TeamSheetPrinted", "MonthSheet", $"{year}-{month:00}", $"{supervisorCode}: {team.Count} employees, {format}");
        await _db.SaveChangesAsync();
        return await RenderAsync(sheets, year, month, format, single: false, team: true);
    }

    /// <summary>
    /// The message a supervisor sends on for approval: for everybody below them, what there is to report (overtime, absences, business
    /// trips), grouped by the team lead they report to, and the register of overtime. Whatever the status; unapproved sheets are marked.
    /// </summary>
    public async Task<PrintedFile> SummaryAsync(string supervisorCode, int year, int month, string format)
    {
        CheckFormat(format);
        var groups = await SummaryGroupsAsync(supervisorCode, year, month, format);

        var isExcel = format.Equals("xlsx", StringComparison.OrdinalIgnoreCase);
        await Renders.WaitAsync();
        try
        {
            return isExcel
                ? new PrintedFile(TeamSummary.ToExcel(year, month, groups), ExcelType, $"Pregled_za_odobravanje_{year}-{month:00}.xlsx")
                : new PrintedFile(TeamSummary.ToPdf(year, month, groups), PdfType, $"Pregled_za_odobravanje_{year}-{month:00}.pdf");
        }
        finally
        {
            Renders.Release();
        }
    }

    /// <summary>The same message as text, for the screen and for copying into a mail</summary>
    public async Task<TeamSummaryDto> SummaryTextAsync(string supervisorCode, int year, int month) =>
        TeamSummary.ToDto(year, month, await SummaryGroupsAsync(supervisorCode, year, month, "text"));

    private async Task<IReadOnlyList<SummaryGroup>> SummaryGroupsAsync(string supervisorCode, int year, int month, string format)
    {
        var tree = await _team.TreeOfAsync(supervisorCode);
        if (tree.Count == 0) throw new SheetException(404, "no-team", "Nobody reports to you.");

        var codes = tree.Select(n => n.Person.Code).ToList();
        var stored = await _db.Sheets.AsNoTracking().WithDetails().Where(s => s.Year == year && s.Month == month && codes.Contains(s.EmployeeCode)).ToListAsync();
        var supervisor = await _db.Employees.AsNoTracking().Where(e => e.Code == supervisorCode).Select(e => e.Name).FirstAsync();
        var names = tree.ToDictionary(n => n.Person.Code, n => n.Person.Name);

        var people = new List<(string? Parent, SummaryPerson Person)>();
        foreach (var node in tree)
        {
            var sheet = stored.FirstOrDefault(s => s.EmployeeCode == node.Person.Code) ?? new MonthSheet { EmployeeCode = node.Person.Code, Year = year, Month = month };
            people.Add((node.ParentCode, new SummaryPerson(node.Person.Name, await _builder.BuildAsync(sheet))));
        }

        // Direct reports first, then one group for each team lead who has people of their own
        var grouped = people.GroupBy(p => p.Parent ?? "").OrderBy(g => g.Key == "" ? 0 : 1).ToList();
        var groups = grouped.Select(g => new SummaryGroup(
            grouped.Count == 1 ? null : g.Key == "" ? $"Direktno: {supervisor}" : $"Tim: {names[g.Key]}",
            g.Select(p => p.Person).ToList())).ToList();

        _audit.Record("TeamSummaryPrinted", "MonthSheet", $"{year}-{month:00}", $"{supervisorCode}: {people.Count} employees, {format}");
        await _db.SaveChangesAsync();
        return groups;
    }

    /// <summary>Every approved timesheet of the month, for finance</summary>
    public async Task<PrintedFile> ApprovedAsync(int year, int month, string format)
    {
        CheckFormat(format);
        var sheets = await _db.Sheets.AsNoTracking().WithDetails()
            .Where(s => s.Year == year && s.Month == month && s.Status == TimesheetStatus.Approved).OrderBy(s => s.EmployeeCode).ToListAsync();
        if (sheets.Count == 0) throw new SheetException(404, "no-approved-sheets", "No timesheet has been approved for this month yet.");

        _audit.Record("ApprovedSheetsPrinted", "MonthSheet", $"{year}-{month:00}", $"{sheets.Count} timesheets, {format}");
        await _db.SaveChangesAsync();
        return await RenderAsync(sheets, year, month, format, single: false);
    }

    private async Task<PrintedFile> RenderAsync(IReadOnlyList<MonthSheet> sheets, int year, int month, string format, bool single, bool team = false)
    {
        var codes = sheets.Select(s => s.EmployeeCode).Distinct().ToList();
        var names = await _db.Employees.AsNoTracking().Where(e => codes.Contains(e.Code)).ToDictionaryAsync(e => e.Code, e => e.Name);

        var employees = new List<PrintedEmployee>();
        foreach (var sheet in sheets)
        {
            var built = await _builder.BuildAsync(sheet);
            employees.Add(new PrintedEmployee(sheet.EmployeeCode, names.GetValueOrDefault(sheet.EmployeeCode, sheet.EmployeeCode), built,
                team ? LegacySheet.StatusText(built.Status) : null));
        }

        var statusLine = team ? "Šihtarice zaposlenika koje nadgledate" : single
            ? "Status: " + LegacySheet.StatusText(employees[0].Sheet.Status) + (employees[0].Sheet.Status == SheetStatus.Approved ? "" : " (nije odobreno)")
            : "Odobrene šihtarice";
        var isExcel = format.Equals("xlsx", StringComparison.OrdinalIgnoreCase);
        var stem = single ? $"Sihtarica_{employees[0].Code}_{year}-{month:00}" : team ? $"Sihtarice_tima_{year}-{month:00}" : $"Sihtarice_{year}-{month:00}";

        await Renders.WaitAsync();
        try
        {
            return isExcel
                ? new PrintedFile(LegacySheet.ToExcel(year, month, employees, statusLine), ExcelType, stem + ".xlsx")
                : new PrintedFile(LegacySheet.ToPdf(year, month, employees, statusLine), PdfType, stem + ".pdf");
        }
        finally
        {
            Renders.Release();
        }
    }

    private static void CheckFormat(string format)
    {
        if (!format.Equals("xlsx", StringComparison.OrdinalIgnoreCase) && !format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
            throw new SheetException(400, "invalid-format", "Choose xlsx or pdf.");
    }
}
