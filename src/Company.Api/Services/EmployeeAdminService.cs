using Company.Api.Auth;
using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Services;

/// <summary>
/// The administrator's changes of an employee: name, roles, place in the chart, employment dates, and whether the person is active.
/// The rules that protect the company (somebody can always administer, nobody is deactivated while timesheets wait for them, the
/// chart has no loops) are here, not in the endpoint.
/// </summary>
public sealed class EmployeeAdminService
{
    private readonly CompanyDbContext _db;
    private readonly ChartService _chart;
    private readonly AuditService _audit;
    private readonly EmployeeLookupCache _cache;

    public EmployeeAdminService(CompanyDbContext db, ChartService chart, AuditService audit, EmployeeLookupCache cache)
    {
        _db = db;
        _chart = chart;
        _audit = audit;
        _cache = cache;
    }

    /// <summary>Adds a person. The code is the one given, or the next free Z-nnn; the supervisor must already exist.</summary>
    public async Task<Employee> CreateAsync(CreateEmployeeRequest request)
    {
        var name = Limits.Check(request.Name, Limits.Name, "The name");
        if (name.Length == 0) throw new SheetException(400, "name-required", "The name cannot be empty.");

        var code = Limits.Check(request.Code, Limits.Code, "The code");
        if (code.Length == 0) code = await NextCodeAsync();
        else if (code.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new SheetException(400, "invalid-code", "The code can have letters, digits, dash, dot or underscore.");
        if (await _db.Employees.AnyAsync(e => e.Code == code))
            throw new SheetException(409, "code-taken", $"Code {code} already belongs to somebody else.");

        var supervisor = Clear(request.SupervisorCode ?? "");
        if (supervisor != null && !await _db.Employees.AnyAsync(e => e.Code == supervisor))
            throw new SheetException(400, "unknown-person", "The supervisor is not an employee.");

        var email = Clear(request.Email ?? "");
        if (email != null && (email.Length > 200 || !email.Contains('@') || email.Contains(' ')))
            throw new SheetException(400, "invalid-email", "The e-mail address is not valid.");
        if (request.ActiveFrom != null && request.ActiveTo != null && request.ActiveTo < request.ActiveFrom)
            throw new SheetException(400, "invalid-period", "The end of employment is before its start.");

        var employee = new Employee
        {
            Code = code, Name = name, SupervisorCode = supervisor, Email = email, IsAdmin = request.IsAdmin,
            ActiveFrom = request.ActiveFrom, ActiveTo = request.ActiveTo
        };
        _db.Employees.Add(employee);
        _audit.Record("EmployeeCreated", "Employee", code, Describe(employee));
        await _db.SaveChangesAsync();
        _cache.Clear();
        return employee;
    }

    /// <summary>The next Z-nnn after the highest one in use (people whose codes do not look like that are ignored)</summary>
    private async Task<string> NextCodeAsync()
    {
        var codes = await _db.Employees.AsNoTracking().Select(e => e.Code).ToListAsync();
        var highest = codes.Where(c => c.StartsWith("Z-") && int.TryParse(c.AsSpan(2), out _)).Select(c => int.Parse(c.AsSpan(2))).DefaultIfEmpty(0).Max();
        return $"Z-{highest + 1:000}";
    }

    public async Task<Employee> UpdateAsync(string code, UpdateEmployeeRequest request)
    {
        var employee = await _db.Employees.Include(e => e.Accounts).FirstOrDefaultAsync(e => e.Code == code)
                       ?? throw new SheetException(404, "not-found", "The employee was not found.");

        var before = Describe(employee);
        var wasActiveAdmin = employee.IsAdmin && employee.IsActive;

        if (request.Name != null)
        {
            var name = request.Name.Trim();
            if (name.Length == 0) throw new SheetException(400, "name-required", "The name cannot be empty.");
            employee.Name = Limits.Check(name, Limits.Name, "The name");
        }

        if (request.IsActive.HasValue) employee.IsActive = request.IsActive.Value;
        if (request.IsFinalApprover.HasValue) employee.IsFinalApprover = request.IsFinalApprover.Value;
        if (request.IsAdmin.HasValue) employee.IsAdmin = request.IsAdmin.Value;
        if (request.IsFinance.HasValue) employee.IsFinance = request.IsFinance.Value;
        if (request.ActiveFrom.HasValue) employee.ActiveFrom = request.ActiveFrom;
        if (request.ActiveTo.HasValue) employee.ActiveTo = request.ActiveTo;
        if (request.ClearActiveFrom == true) employee.ActiveFrom = null;
        if (request.ClearActiveTo == true) employee.ActiveTo = null;
        if (request.Email != null)
        {
            var email = Clear(request.Email);
            if (email != null && (email.Length > 200 || !email.Contains('@') || email.Contains(' ')))
                throw new SheetException(400, "invalid-email", "The e-mail address is not valid.");
            employee.Email = email;
        }

        if (request.SupervisorCode != null) employee.SupervisorCode = Clear(request.SupervisorCode);
        if (request.DesignatedApproverCode != null) employee.DesignatedApproverCode = Clear(request.DesignatedApproverCode);

        // Somebody has to be able to administer the system
        if (wasActiveAdmin && !(employee.IsAdmin && employee.IsActive)
            && !await _db.Employees.AnyAsync(e => e.IsActive && e.IsAdmin && e.Code != employee.Code))
            throw new SheetException(409, "last-admin", "This is the last active administrator.");

        // A timesheet must not wait for a person who can no longer sign in
        if (request.IsActive == false)
        {
            var reports = await _db.Employees.CountAsync(e => e.IsActive && e.Code != employee.Code && (e.SupervisorCode == employee.Code || e.DesignatedApproverCode == employee.Code));
            var waiting = await _db.Sheets.CountAsync(s => s.Status == TimesheetStatus.Submitted && s.CurrentApproverCode == employee.Code);
            if (reports > 0 || waiting > 0)
                throw new SheetException(409, "has-reports",
                    $"{employee.Name} still has {reports} people reporting to them and {waiting} timesheets waiting for them. Move those first (change \"reports to\", or hand the waiting steps to somebody else).");
        }

        if (employee.SupervisorCode != null && !await _db.Employees.AnyAsync(e => e.Code == employee.SupervisorCode)
            || employee.DesignatedApproverCode != null && !await _db.Employees.AnyAsync(e => e.Code == employee.DesignatedApproverCode))
            throw new SheetException(400, "unknown-person", "The supervisor or the designated approver is not an employee.");

        var problem = await _chart.CheckWithAsync(_chart.ToPerson(employee));
        if (problem != ChainProblem.None)
            throw new SheetException(409, problem == ChainProblem.Cycle ? "chart-cycle" : "chart-invalid",
                problem == ChainProblem.Cycle ? "This would make someone their own supervisor." : "The chart would be invalid.");

        _audit.Record("EmployeeUpdated", "Employee", employee.Code, $"{before} -> {Describe(employee)}");
        await _db.SaveChangesAsync();
        _cache.Clear();
        return employee;
    }

    /// <summary>An empty string clears a code</summary>
    private static string? Clear(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Describe(Employee e) =>
        $"name={e.Name}, active={e.IsActive}, supervisor={e.SupervisorCode}, final={e.IsFinalApprover}, designated={e.DesignatedApproverCode}, admin={e.IsAdmin}, finance={e.IsFinance}, email={e.Email}";
}
