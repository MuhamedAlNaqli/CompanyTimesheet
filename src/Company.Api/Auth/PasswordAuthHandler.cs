using System.Security.Claims;
using System.Text.Encodings.Web;
using Company.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Company.Api.Auth;

/// <summary>
/// Sign-in with the session token the server handed out at login ("Authorization: Bearer ..."). The caller is known as
/// <c>local:username</c>, which the administrator's credentials link to the employee, so everything after this (roles, the employee
/// code) works as it does for a Windows or Entra account.
/// </summary>
public sealed class PasswordAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "Password";
    public const string SessionClaim = "session_id";
    public const string MustChangeClaim = "must_change_password";
    public const string AccountPrefix = "local:";

    private readonly TimeProvider _time;

    public PasswordAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TimeProvider time)
        : base(options, logger, encoder) => _time = time;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();

        var hash = Passwords.HashToken(header["Bearer ".Length..].Trim());
        var db = Context.RequestServices.GetRequiredService<CompanyDbContext>();
        var now = _time.GetUtcNow().UtcDateTime;

        var session = await db.AuthSessions.AsNoTracking().FirstOrDefaultAsync(s => s.TokenHash == hash && s.ExpiresUtc > now);
        if (session == null) return AuthenticateResult.Fail("The session has ended. Sign in again.");

        var credential = await db.Credentials.AsNoTracking().FirstOrDefaultAsync(c => c.EmployeeCode == session.EmployeeCode);
        if (credential == null) return AuthenticateResult.Fail("The account has no credentials.");

        var claims = new List<Claim> { new(ClaimTypes.Name, AccountPrefix + credential.Username), new(SessionClaim, session.Id.ToString()) };
        if (credential.MustChange) claims.Add(new Claim(MustChangeClaim, "1"));

        var identity = new ClaimsIdentity(claims, Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme));
    }
}
