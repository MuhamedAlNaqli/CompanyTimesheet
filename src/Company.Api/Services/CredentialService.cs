using Company.Api.Auth;
using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Company.Api.Services;

/// <summary>
/// Sign-in with a user name and password that the administrator creates: login (with a lock after repeated failures), logout, the
/// employee's own change of password, and the administrator's setting and removing of credentials.
/// </summary>
public sealed class CredentialService
{
    private const int FailuresBeforeLock = 5;
    private static readonly TimeSpan LockTime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan NormalSession = TimeSpan.FromHours(12);
    private static readonly TimeSpan RememberedSession = TimeSpan.FromDays(30);

    private readonly CompanyDbContext _db;
    private readonly AuditService _audit;
    private readonly TimeProvider _time;
    private readonly EmployeeLookupCache _cache;

    public CredentialService(CompanyDbContext db, AuditService audit, TimeProvider time, EmployeeLookupCache cache)
    {
        _db = db;
        _audit = audit;
        _time = time;
        _cache = cache;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    // ---- The employee ----------------------------------------------------------------------------------------------

    public async Task<LoginResultDto> LoginAsync(LoginRequest request)
    {
        var username = AccountNormalizer.Normalize(request.Username);
        var password = request.Password ?? "";
        var refusal = new SheetException(401, "invalid-credentials", "The user name or the password is not right.");

        var credential = await _db.Credentials.FirstOrDefaultAsync(c => c.Username == username);
        if (credential == null)
        {
            Passwords.SpendTheTimeOfACheck(password);
            throw refusal;
        }

        if (credential.LockedUntilUtc is { } until && until > Now)
            throw new SheetException(423, "locked", $"Too many wrong passwords. Try again in {Math.Ceiling((until - Now).TotalMinutes)} minutes, or ask the administrator.");

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Code == credential.EmployeeCode);
        if (employee == null || !employee.IsActive || !Passwords.Verify(password, credential.PasswordHash))
        {
            credential.FailedCount++;
            if (credential.FailedCount >= FailuresBeforeLock)
            {
                credential.FailedCount = 0;
                credential.LockedUntilUtc = Now + LockTime;
                _audit.Record("AccountLocked", "Credential", credential.EmployeeCode, $"after {FailuresBeforeLock} wrong passwords", actor: credential.EmployeeCode);
            }

            await _db.SaveChangesAsync();
            throw refusal;
        }

        credential.FailedCount = 0;
        credential.LockedUntilUtc = null;

        // Old sessions of this person that have run out are removed here, so the table does not grow
        var expired = await _db.AuthSessions.Where(s => s.EmployeeCode == credential.EmployeeCode && s.ExpiresUtc <= Now).ToListAsync();
        _db.AuthSessions.RemoveRange(expired);

        var token = Passwords.NewToken();
        var expires = Now + (request.Remember ? RememberedSession : NormalSession);
        _db.AuthSessions.Add(new AuthSession { TokenHash = Passwords.HashToken(token), EmployeeCode = credential.EmployeeCode, CreatedUtc = Now, ExpiresUtc = expires });
        _audit.Record("SignedIn", "Credential", credential.EmployeeCode, request.Remember ? "remembered" : null, actor: credential.EmployeeCode);
        await _db.SaveChangesAsync();

        return new LoginResultDto(token, DateTime.SpecifyKind(expires, DateTimeKind.Utc), credential.MustChange);
    }

    public async Task LogoutAsync(int sessionId)
    {
        var session = await _db.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null) return;

        _db.AuthSessions.Remove(session);
        await _db.SaveChangesAsync();
    }

    public async Task ChangePasswordAsync(string employeeCode, ChangePasswordRequest request)
    {
        var credential = await _db.Credentials.FirstOrDefaultAsync(c => c.EmployeeCode == employeeCode)
                         ?? throw new SheetException(404, "no-credentials", "This account has no password sign-in.");

        if (!Passwords.Verify(request.Current ?? "", credential.PasswordHash))
            throw new SheetException(400, "wrong-password", "The current password is not right.");
        if (Passwords.Refusal(request.New, credential.Username) is { } problem)
            throw new SheetException(400, "weak-password", problem);
        if (request.New == request.Current)
            throw new SheetException(400, "same-password", "Choose a password different from the current one.");

        credential.PasswordHash = Passwords.Hash(request.New);
        credential.MustChange = false;
        credential.ChangedUtc = Now;
        _audit.Record("PasswordChanged", "Credential", employeeCode, null, actor: employeeCode);
        await _db.SaveChangesAsync();
    }

    // ---- The administrator -------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<CredentialDto>> ListAsync() =>
        (await _db.Credentials.AsNoTracking().OrderBy(c => c.Username).ToListAsync())
        .Select(c => new CredentialDto(c.EmployeeCode, c.Username, c.MustChange, c.LockedUntilUtc is { } until && until > Now ? DateTime.SpecifyKind(until, DateTimeKind.Utc) : null)).ToList();

    /// <summary>Creates or replaces the user name and password of an employee. Every session of that employee ends.</summary>
    public async Task SetAsync(string employeeCode, SetCredentialsRequest request)
    {
        if (!await _db.Employees.AnyAsync(e => e.Code == employeeCode))
            throw new SheetException(404, "not-found", "The employee was not found.");

        var username = AccountNormalizer.Normalize(request.Username);
        if (Passwords.UsernameRefusal(username) is { } nameProblem) throw new SheetException(400, "invalid-username", nameProblem);
        if (Passwords.Refusal(request.Password, username) is { } passwordProblem) throw new SheetException(400, "weak-password", passwordProblem);

        var taken = await _db.Credentials.AnyAsync(c => c.Username == username && c.EmployeeCode != employeeCode);
        var linked = await _db.Accounts.AnyAsync(a => a.Account == PasswordAuthHandler.AccountPrefix + username && a.EmployeeCode != employeeCode);
        if (taken || linked) throw new SheetException(409, "username-taken", "Another employee already has this user name.");

        var credential = await _db.Credentials.FirstOrDefaultAsync(c => c.EmployeeCode == employeeCode);
        if (credential == null)
        {
            credential = new EmployeeCredential { EmployeeCode = employeeCode };
            _db.Credentials.Add(credential);
        }

        await DropLinkAsync(employeeCode);
        _db.Accounts.Add(new EmployeeAccount { EmployeeCode = employeeCode, Account = PasswordAuthHandler.AccountPrefix + username });

        credential.Username = username;
        credential.PasswordHash = Passwords.Hash(request.Password);
        credential.MustChange = request.MustChange;
        credential.FailedCount = 0;
        credential.LockedUntilUtc = null;
        credential.ChangedUtc = Now;

        _db.AuthSessions.RemoveRange(await _db.AuthSessions.Where(s => s.EmployeeCode == employeeCode).ToListAsync());
        _audit.Record("CredentialsSet", "Credential", employeeCode, $"user name {username}, must change: {request.MustChange}");
        await _db.SaveChangesAsync();
        _cache.Clear();
    }

    public async Task RemoveAsync(string employeeCode)
    {
        var credential = await _db.Credentials.FirstOrDefaultAsync(c => c.EmployeeCode == employeeCode)
                         ?? throw new SheetException(404, "not-found", "This employee has no user name.");

        // Nobody is left unable to administer
        if (await _db.Employees.AnyAsync(e => e.Code == employeeCode && e.IsAdmin && e.IsActive)
            && !await _db.Credentials.AnyAsync(c => c.EmployeeCode != employeeCode && _db.Employees.Any(e => e.Code == c.EmployeeCode && e.IsAdmin && e.IsActive)))
            throw new SheetException(409, "last-admin", "This is the last administrator who can sign in.");

        await DropLinkAsync(employeeCode);
        _db.AuthSessions.RemoveRange(await _db.AuthSessions.Where(s => s.EmployeeCode == employeeCode).ToListAsync());
        _db.Credentials.Remove(credential);
        _audit.Record("CredentialsRemoved", "Credential", employeeCode, credential.Username);
        await _db.SaveChangesAsync();
        _cache.Clear();
    }

    private async Task DropLinkAsync(string employeeCode) =>
        _db.Accounts.RemoveRange(await _db.Accounts.Where(a => a.EmployeeCode == employeeCode && a.Account.StartsWith(PasswordAuthHandler.AccountPrefix)).ToListAsync());
}
