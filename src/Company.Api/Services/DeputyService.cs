using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Company.Api.Services;

/// <summary>
/// A deputy decides on the timesheets that wait for an approver who is away, for a period the approver (or the administrator) chose.
/// The history of the sheet records both: who decided, and for whom.
/// </summary>
public sealed class DeputyService
{
    private const int LongestPeriodDays = 366;

    private readonly CompanyDbContext _db;
    private readonly AuditService _audit;
    private readonly TimeProvider _time;

    public DeputyService(CompanyDbContext db, AuditService audit, TimeProvider time)
    {
        _db = db;
        _audit = audit;
        _time = time;
    }

    private DateOnly Today => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    /// <summary>The approvers this person stands in for today</summary>
    public async Task<IReadOnlyList<string>> ApproversActedForAsync(string deputyCode)
    {
        var today = Today;
        return await _db.Deputies.AsNoTracking().Where(d => d.DeputyCode == deputyCode && d.FromDate <= today && d.ToDate >= today)
            .Select(d => d.ApproverCode).Distinct().ToListAsync();
    }

    public async Task<bool> IsDeputyForAsync(string deputyCode, string approverCode)
    {
        var today = Today;
        return await _db.Deputies.AsNoTracking().AnyAsync(d => d.DeputyCode == deputyCode && d.ApproverCode == approverCode && d.FromDate <= today && d.ToDate >= today);
    }

    /// <summary>Whether this person has a period as a deputy that is now or still to come (they need the approvals tab)</summary>
    public async Task<bool> IsDeputyNowOrSoonAsync(string deputyCode)
    {
        var today = Today;
        return await _db.Deputies.AsNoTracking().AnyAsync(d => d.DeputyCode == deputyCode && d.ToDate >= today);
    }

    /// <summary>The periods that are running or still to come: the caller's own deputies, and the people the caller stands in for</summary>
    public async Task<DeputiesDto> ListAsync(string employeeCode)
    {
        var today = Today;
        var mine = await _db.Deputies.AsNoTracking().Where(d => d.ApproverCode == employeeCode && d.ToDate >= today).OrderBy(d => d.FromDate).ToListAsync();
        var forOthers = await _db.Deputies.AsNoTracking().Where(d => d.DeputyCode == employeeCode && d.ToDate >= today).OrderBy(d => d.FromDate).ToListAsync();
        var names = await NamesAsync(mine.Concat(forOthers));
        return new DeputiesDto(mine.Select(d => ToDto(d, names)).ToList(), forOthers.Select(d => ToDto(d, names)).ToList());
    }

    /// <summary>Every running or coming period, for the administrator</summary>
    public async Task<IReadOnlyList<DeputyDto>> ListAllAsync()
    {
        var today = Today;
        var all = await _db.Deputies.AsNoTracking().Where(d => d.ToDate >= today).OrderBy(d => d.ApproverCode).ThenBy(d => d.FromDate).ToListAsync();
        var names = await NamesAsync(all);
        return all.Select(d => ToDto(d, names)).ToList();
    }

    public async Task<DeputyDto> AddAsync(string actorCode, string approverCode, AddDeputyRequest request)
    {
        if (request.To < request.From) throw new SheetException(400, "invalid-period", "The period ends before it starts.");
        if (request.To.DayNumber - request.From.DayNumber > LongestPeriodDays) throw new SheetException(400, "invalid-period", "A period can be at most a year.");
        if (request.To < Today) throw new SheetException(400, "invalid-period", "The period is already over.");
        if (string.Equals(request.DeputyCode, approverCode, StringComparison.OrdinalIgnoreCase)) throw new SheetException(400, "invalid-deputy", "Nobody is their own deputy.");

        var approver = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Code == approverCode && e.IsActive)
                       ?? throw new SheetException(404, "not-found", "The approver was not found.");
        var deputy = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Code == request.DeputyCode && e.IsActive)
                     ?? throw new SheetException(404, "not-found", "The deputy is not an active employee.");

        var overlaps = await _db.Deputies.AnyAsync(d => d.ApproverCode == approverCode && d.FromDate <= request.To && d.ToDate >= request.From);
        if (overlaps) throw new SheetException(409, "overlap", "There is already a deputy for part of this period. Remove it first.");

        var entry = new ApproverDeputy { ApproverCode = approverCode, DeputyCode = deputy.Code, FromDate = request.From, ToDate = request.To };
        _db.Deputies.Add(entry);
        _audit.Record("DeputyAdded", "Deputy", approverCode, $"{deputy.Code} {request.From:yyyy-MM-dd}..{request.To:yyyy-MM-dd}", actorCode);
        await _db.SaveChangesAsync();
        return ToDto(entry, new Dictionary<string, string> { [approver.Code] = approver.Name, [deputy.Code] = deputy.Name });
    }

    public async Task RemoveAsync(string actorCode, bool isAdmin, int id)
    {
        var entry = await _db.Deputies.FirstOrDefaultAsync(d => d.Id == id) ?? throw new SheetException(404, "not-found", "There is no such deputy.");
        if (!isAdmin && !string.Equals(entry.ApproverCode, actorCode, StringComparison.OrdinalIgnoreCase))
            throw new SheetException(403, "forbidden", "Only the approver or the administrator can remove a deputy.");

        _db.Deputies.Remove(entry);
        _audit.Record("DeputyRemoved", "Deputy", entry.ApproverCode, $"{entry.DeputyCode} {entry.FromDate:yyyy-MM-dd}..{entry.ToDate:yyyy-MM-dd}", actorCode);
        await _db.SaveChangesAsync();
    }

    /// <summary>The colleagues who can be chosen as a deputy: every active employee except the person themselves</summary>
    public async Task<IReadOnlyList<PersonDto>> CandidatesAsync(string employeeCode) =>
        await _db.Employees.AsNoTracking().Where(e => e.IsActive && e.Code != employeeCode).OrderBy(e => e.Name).Select(e => new PersonDto(e.Code, e.Name)).ToListAsync();

    private async Task<Dictionary<string, string>> NamesAsync(IEnumerable<ApproverDeputy> entries)
    {
        var codes = entries.SelectMany(d => new[] { d.ApproverCode, d.DeputyCode }).Distinct().ToList();
        return await _db.Employees.AsNoTracking().Where(e => codes.Contains(e.Code)).ToDictionaryAsync(e => e.Code, e => e.Name);
    }

    private static DeputyDto ToDto(ApproverDeputy d, IReadOnlyDictionary<string, string> names) =>
        new(d.Id, d.ApproverCode, names.GetValueOrDefault(d.ApproverCode, d.ApproverCode), d.DeputyCode, names.GetValueOrDefault(d.DeputyCode, d.DeputyCode), d.FromDate, d.ToDate);
}
