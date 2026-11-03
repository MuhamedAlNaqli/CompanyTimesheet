using System.Security.Claims;
using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.EntityFrameworkCore;

namespace Company.Api.Auth;

public static class AccountNormalizer
{
    /// <summary>Sign-in names are compared without regard to case and surrounding spaces</summary>
    public static string Normalize(string? account) => (account ?? "").Trim().ToLowerInvariant();
}

public static class Roles
{
    public const string Admin = "Admin";

    /// <summary>Someone who has people reporting to them, or is a designated approver</summary>
    public const string Approver = "Approver";

    public const string FinalApprover = "FinalApprover";

    public const string Finance = "Finance";
}

public static class Identities
{
    public const string EmployeeCodeClaim = "emp_code";
    public const string ResolvedClaim = "emp_resolved";

    public const string RegisteredPolicy = "Registered";
    public const string AdminPolicy = "Admin";
    public const string FinancePolicy = "Finance";

    private const string ObjectIdClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string TenantIdClaim = "http://schemas.microsoft.com/identity/claims/tenantid";

    /// <summary>
    /// The names a caller is known by, any of which may be what the administrator linked to the employee.
    /// A Windows caller (and the test sign-in) is known by DOMAIN\user and the user principal name from the directory.
    /// An Entra caller is known by the immutable object id of the token (written "entra:tenant/object", see
    /// /api/auth/diagnostics) and the user principal name. The e-mail address, preferred_username and the display name of a token
    /// can be changed by the user or are not verified, so they are never used to decide who somebody is.
    /// </summary>
    public static IReadOnlyList<string> Candidates(ClaimsPrincipal user)
    {
        var names = new List<string?>();
        var objectId = user.FindFirstValue("oid") ?? user.FindFirstValue(ObjectIdClaim);
        if (objectId != null)
        {
            var tenant = user.FindFirstValue("tid") ?? user.FindFirstValue(TenantIdClaim);
            names.Add(tenant == null ? $"entra:{objectId}" : $"entra:{tenant}/{objectId}");
        }
        else
        {
            names.Add(user.Identity?.Name);
        }

        foreach (var type in new[] { ClaimTypes.Upn, "upn" })
            names.AddRange(user.FindAll(type).Select(c => c.Value));

        return names.Select(AccountNormalizer.Normalize).Where(n => n.Length > 0).Distinct().ToList();
    }

    public static string? EmployeeCode(ClaimsPrincipal user) => user.FindFirstValue(EmployeeCodeClaim);

    /// <summary>The audit actor: the employee code when known, otherwise the sign-in name</summary>
    public static string Actor(ClaimsPrincipal user) =>
        EmployeeCode(user) ?? user.Identity?.Name ?? "unknown";
}

/// <summary>Who an account is and what they may do</summary>
public sealed record EmployeeIdentity(string Code, bool IsAdmin, bool IsFinalApprover, bool IsFinance, bool IsApprover);

/// <summary>
/// Remembers for half a minute who an account is, so that a request does not repeat four queries. Only a found employee is
/// remembered, and the administrator's changes (a link, a role, a deactivation) empty it at once.
/// </summary>
public sealed class EmployeeLookupCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Expires, EmployeeIdentity Value)> _items = new();

    public bool TryGet(string key, out EmployeeIdentity? value)
    {
        if (_items.TryGetValue(key, out var item) && item.Expires > DateTime.UtcNow)
        {
            value = item.Value;
            return true;
        }

        value = null;
        return false;
    }

    public void Set(string key, EmployeeIdentity value) => _items[key] = (DateTime.UtcNow + Lifetime, value);

    public void Clear() => _items.Clear();
}

/// <summary>After sign-in, finds the employee the account is linked to and adds the employee code and the roles to the caller</summary>
public sealed class EmployeeClaimsTransformation : IClaimsTransformation
{
    private readonly CompanyDbContext _db;
    private readonly EmployeeLookupCache _cache;
    private readonly ILogger<EmployeeClaimsTransformation> _logger;
    private readonly CompanyFeatures _features;

    public EmployeeClaimsTransformation(CompanyDbContext db, EmployeeLookupCache cache, ILogger<EmployeeClaimsTransformation> logger, CompanyFeatures features)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
        _features = features;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.HasClaim(c => c.Type == Identities.ResolvedClaim)) return principal;

        var candidates = Identities.Candidates(principal);
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(Identities.ResolvedClaim, "1"));

        var found = await FindAsync(candidates);
        if (found != null)
        {
            identity.AddClaim(new Claim(Identities.EmployeeCodeClaim, found.Code));
            if (found.IsAdmin) identity.AddClaim(new Claim(ClaimTypes.Role, Roles.Admin));
            if (_features.FinanceAndLegal && found.IsFinalApprover) identity.AddClaim(new Claim(ClaimTypes.Role, Roles.FinalApprover));
            if (_features.FinanceAndLegal && found.IsFinance) identity.AddClaim(new Claim(ClaimTypes.Role, Roles.Finance));
            if (found.IsApprover) identity.AddClaim(new Claim(ClaimTypes.Role, Roles.Approver));
        }

        principal.AddIdentity(identity);
        return principal;
    }

    private async Task<EmployeeIdentity?> FindAsync(IReadOnlyList<string> candidates)
    {
        var key = string.Join("|", candidates);
        if (_cache.TryGet(key, out var known)) return known;

        // Names that lead to two different employees are a mistake of the links: nobody is signed in as either
        var codes = await _db.Accounts.AsNoTracking().Where(a => candidates.Contains(a.Account)).Select(a => a.EmployeeCode).Distinct().ToListAsync();
        if (codes.Count > 1)
        {
            _logger.LogWarning("The sign-in names {Names} are linked to different employees ({Codes}); nobody is signed in", key, string.Join(", ", codes));
            return null;
        }

        if (codes.Count == 0) return null;

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Code == codes[0] && e.IsActive);
        if (employee == null) return null;

        var approver = await _db.Employees.AnyAsync(e => e.IsActive && (e.SupervisorCode == employee.Code || e.DesignatedApproverCode == employee.Code));
        var result = new EmployeeIdentity(employee.Code, employee.IsAdmin, employee.IsFinalApprover, employee.IsFinance, approver);
        _cache.Set(key, result);
        return result;
    }
}

public static class AuthorizationSetup
{
    public static void AddCompanyAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IClaimsTransformation, EmployeeClaimsTransformation>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ApiAuthorizationResultHandler>();
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.AddPolicy(Identities.RegisteredPolicy, p => p.RequireAuthenticatedUser().RequireClaim(Identities.EmployeeCodeClaim));
            options.AddPolicy(Identities.AdminPolicy, p => p.RequireAuthenticatedUser().RequireClaim(Identities.EmployeeCodeClaim).RequireRole(Roles.Admin));
            options.AddPolicy(Identities.FinancePolicy, p => p.RequireAuthenticatedUser().RequireClaim(Identities.EmployeeCodeClaim).RequireRole(Roles.Admin, Roles.Finance));
        });
    }
}

/// <summary>Answers a refusal with a body the desktop app can show: signed in but not registered, or not allowed</summary>
public sealed class ApiAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
        {
            var registered = Identities.EmployeeCode(context.User) != null;
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(registered
                ? new ApiErrorDto("forbidden", "You are not allowed to do this.")
                : new ApiErrorDto("not-registered", "Your account is not linked to an employee. Ask the administrator."));
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
