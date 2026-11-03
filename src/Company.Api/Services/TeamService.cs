using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Services;

/// <summary>
/// The people below a supervisor: who they are as a tree, who has not submitted the month, and a reminder to one of them by name.
/// A person's place in the tree is their supervisor; a person with nobody above reports to their designated approver, as in the
/// printed sheet of the team.
/// </summary>
public sealed class TeamService
{
    /// <summary>A person is reminded at most this often, whoever asks, so a supervisor and the one above cannot pile on</summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromHours(4);

    private readonly CompanyDbContext _db;
    private readonly AuditService _audit;
    private readonly TimeProvider _time;

    public TeamService(CompanyDbContext db, AuditService audit, TimeProvider time)
    {
        _db = db;
        _audit = audit;
        _time = time;
    }

    /// <summary>Everybody below the supervisor, depth first, each person right before their own reports</summary>
    public async Task<IReadOnlyList<TeamNode>> TreeOfAsync(string supervisorCode)
    {
        var people = await _db.Employees.AsNoTracking().Where(e => e.IsActive).ToListAsync();
        var children = people.Where(e => e.Code != supervisorCode)
            .GroupBy(e => e.SupervisorCode ?? e.DesignatedApproverCode ?? "")
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList());

        var result = new List<TeamNode>();
        var seen = new HashSet<string> { supervisorCode };

        void Walk(string parent, int depth)
        {
            if (!children.TryGetValue(parent, out var reports)) return;
            foreach (var person in reports)
            {
                // The designated approver of the top person may report to them: never walk into a person twice
                if (!seen.Add(person.Code)) continue;
                result.Add(new TeamNode(person, parent == supervisorCode ? null : parent, depth));
                Walk(person.Code, depth + 1);
            }
        }

        Walk(supervisorCode, 0);
        return result;
    }

    public async Task<TeamStatusDto> StatusAsync(string supervisorCode, int year, int month)
    {
        CheckMonth(year, month);
        var tree = await TreeOfAsync(supervisorCode);
        if (tree.Count == 0) throw new SheetException(404, "no-team", "Nobody reports to you.");

        var codes = tree.Select(n => n.Person.Code).ToList();
        var sheets = await _db.Sheets.AsNoTracking().Where(s => s.Year == year && s.Month == month && codes.Contains(s.EmployeeCode))
            .Select(s => new { s.EmployeeCode, s.Status, s.CurrentApproverCode }).ToListAsync();
        var names = await _db.Employees.AsNoTracking().ToDictionaryAsync(e => e.Code, e => e.Name);
        var nudges = (await _db.TeamNudges.AsNoTracking().Where(n => n.Year == year && n.Month == month && codes.Contains(n.EmployeeCode))
                .Select(n => new { n.EmployeeCode, n.CreatedUtc }).ToListAsync())
            .GroupBy(n => n.EmployeeCode).ToDictionary(g => g.Key, g => g.Max(n => n.CreatedUtc));

        var members = new List<TeamMemberDto>();
        foreach (var node in tree)
        {
            var sheet = sheets.FirstOrDefault(s => s.EmployeeCode == node.Person.Code);
            var status = sheet?.Status ?? TimesheetStatus.Draft;
            var waitingFor = status == TimesheetStatus.Submitted && sheet?.CurrentApproverCode != null ? names.GetValueOrDefault(sheet.CurrentApproverCode) : null;
            members.Add(new TeamMemberDto(
                node.Person.Code, node.Person.Name, node.ParentCode, node.Depth, status.ToDto(), waitingFor,
                CanRemind(node.Person, status, year, month), nudges.TryGetValue(node.Person.Code, out var at) ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null));
        }

        var deadline = Deadlines.Submission(year, month, await Reference.CalendarAsync(_db, year));
        return new TeamStatusDto(year, month, deadline, members);
    }

    /// <summary>Asks one person below the caller to submit the month. Returns the moment it was sent.</summary>
    public async Task<DateTime> RemindAsync(string supervisorCode, string employeeCode, int year, int month)
    {
        CheckMonth(year, month);
        var node = (await TreeOfAsync(supervisorCode)).FirstOrDefault(n => n.Person.Code == employeeCode)
                   ?? throw new SheetException(404, "not-in-team", "This person does not report to you.");

        var status = await _db.Sheets.AsNoTracking().Where(s => s.EmployeeCode == employeeCode && s.Year == year && s.Month == month)
            .Select(s => (TimesheetStatus?)s.Status).FirstOrDefaultAsync() ?? TimesheetStatus.Draft;
        if (!CanRemind(node.Person, status, year, month))
            throw new SheetException(409, "nothing-to-remind", "The timesheet of this month is already submitted, or the person has nothing to submit for it.");

        var now = _time.GetUtcNow().UtcDateTime;
        var last = await _db.TeamNudges.Where(n => n.EmployeeCode == employeeCode && n.Year == year && n.Month == month)
            .OrderByDescending(n => n.CreatedUtc).Select(n => (DateTime?)n.CreatedUtc).FirstOrDefaultAsync();
        if (last != null && now - DateTime.SpecifyKind(last.Value, DateTimeKind.Utc) < MinimumGap)
            throw new SheetException(409, "already-reminded", $"{node.Person.Name} was reminded a moment ago. Try again later.");

        _db.TeamNudges.Add(new TeamNudge { EmployeeCode = employeeCode, FromCode = supervisorCode, Year = year, Month = month, CreatedUtc = now });
        _audit.Record("TeamReminderSent", "MonthSheet", $"{employeeCode}/{year}-{month:00}", $"from {supervisorCode}");
        await _db.SaveChangesAsync();
        return now;
    }

    /// <summary>Whether a reminder makes sense: the month is not in the future, the person was employed in it and has not submitted</summary>
    private bool CanRemind(Employee person, TimesheetStatus status, int year, int month)
    {
        if (status is not (TimesheetStatus.Draft or TimesheetStatus.Returned)) return false;

        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var first = new DateOnly(year, month, 1);
        if (first > today) return false;

        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        if (person.ActiveFrom is { } from && from > last) return false;
        if (person.ActiveTo is { } to && to < first) return false;
        return true;
    }

    private static void CheckMonth(int year, int month)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            throw new SheetException(400, "invalid-month", "Choose a month between 2000 and 2100.");
    }
}

/// <summary>A person of the tree: who, whose report they are (null for a direct report of the caller) and how deep</summary>
public sealed record TeamNode(Employee Person, string? ParentCode, int Depth);
