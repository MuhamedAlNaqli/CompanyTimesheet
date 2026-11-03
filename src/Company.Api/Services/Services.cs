using System.Globalization;
using System.Security.Claims;
using System.Text;
using Company.Api.Auth;
using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Services;

/// <summary>Writes the audit trail. Rows are only ever added; the caller saves them together with the change they describe.</summary>
public sealed class AuditService
{
    private readonly CompanyDbContext _db;
    private readonly IHttpContextAccessor _http;
    private readonly TimeProvider _time;

    public AuditService(CompanyDbContext db, IHttpContextAccessor http, TimeProvider time)
    {
        _db = db;
        _http = http;
        _time = time;
    }

    public void Record(string action, string entityType, string entityId, string? details = null, string? actor = null)
    {
        var user = _http.HttpContext?.User ?? new ClaimsPrincipal();
        _db.AuditEvents.Add(new AuditEvent
        {
            AtUtc = new DateTime(_time.GetUtcNow().UtcDateTime.Ticks / 10 * 10, DateTimeKind.Utc),
            Actor = actor ?? Identities.Actor(user),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details != null && details.Length > 2000 ? details.Substring(0, 2000) : details
        });
    }
}

/// <summary>Checks the organisation chart before a change is saved, and builds approval chains from it</summary>
public sealed class ChartService
{
    private readonly CompanyDbContext _db;

    private readonly CompanyFeatures _features;

    public ChartService(CompanyDbContext db, CompanyFeatures features)
    {
        _db = db;
        _features = features;
    }

    /// <summary>Without the legal department in use nobody ends a chain: a timesheet would wait for a person who never opens the app</summary>
    public Person ToPerson(Employee e) => new(e.Code, e.SupervisorCode, _features.FinanceAndLegal && e.IsFinalApprover, e.DesignatedApproverCode);

    public async Task<List<Person>> LoadAsync() =>
        (await _db.Employees.AsNoTracking().ToListAsync()).Select(ToPerson).ToList();

    public async Task<ChainOutcome> ChainOfAsync(string employeeCode)
    {
        var people = (await LoadAsync()).ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        return ApprovalChainBuilder.Build(employeeCode, code => people.GetValueOrDefault(code));
    }

    /// <summary>Whether the chart stays valid when this person replaces the stored one (or is added)</summary>
    public async Task<ChainProblem> CheckWithAsync(Person changed)
    {
        var people = await LoadAsync();
        people.RemoveAll(p => p.Code.Equals(changed.Code, StringComparison.OrdinalIgnoreCase));
        people.Add(changed);
        return ApprovalChainBuilder.CheckChart(people);
    }
}

/// <summary>
/// Proposes which directory account belongs to which employee by comparing names, so the administrator does not type
/// 112 links by hand. Case, accents and the order of first and last name do not matter.
/// </summary>
public static class AccountMatcher
{
    public static string NormalizeName(string name)
    {
        var text = (name ?? "").Replace("đ", "dj").Replace("Đ", "Dj");
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder();
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            plain.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        var tokens = plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(t => t, StringComparer.Ordinal);
        return string.Join(' ', tokens);
    }

    public static IReadOnlyList<AccountMatchDto> Match(
        IEnumerable<DirectoryUserDto> users,
        IReadOnlyList<(string Code, string Name)> employees,
        ISet<string> linkedAccounts)
    {
        var byName = employees.GroupBy(e => NormalizeName(e.Name)).ToDictionary(g => g.Key, g => g.ToList());
        var results = new List<AccountMatchDto>();
        foreach (var user in users)
        {
            var account = AccountNormalizer.Normalize(user.Account);
            if (linkedAccounts.Contains(account))
            {
                results.Add(new AccountMatchDto(user.Account, user.DisplayName, null, "already-linked"));
                continue;
            }

            if (!byName.TryGetValue(NormalizeName(user.DisplayName), out var found))
                results.Add(new AccountMatchDto(user.Account, user.DisplayName, null, "unmatched"));
            else if (found.Count > 1)
                results.Add(new AccountMatchDto(user.Account, user.DisplayName, null, "ambiguous"));
            else
                results.Add(new AccountMatchDto(user.Account, user.DisplayName, found[0].Code, "matched"));
        }

        return results;
    }
}
