using Company.Api.Auth;
using Microsoft.EntityFrameworkCore;

namespace Company.Api.Data;

public static class Bootstrap
{
    /// <summary>
    /// Creates the first administrator from configuration when no employee exists yet. Afterwards the administrator
    /// imports the employee list and links the accounts; the setting has no effect once employees exist.
    /// </summary>
    public static async Task RunAsync(CompanyDbContext db, IConfiguration configuration, ILogger logger)
    {
        if (await db.Employees.AnyAsync()) return;

        var accounts = configuration.GetSection("Bootstrap:AdminAccounts").Get<string[]>()?
            .Select(AccountNormalizer.Normalize).Where(a => a.Length > 0).Distinct().ToList() ?? new List<string>();

        // With the sign-in by password the first administrator gets a user name and a first password, which must be changed at once
        var username = AccountNormalizer.Normalize(configuration["Bootstrap:AdminUsername"]);
        var password = configuration["Bootstrap:AdminPassword"];
        var withPassword = username.Length > 0 && Passwords.UsernameRefusal(username) == null && Passwords.Refusal(password, username) == null;
        if (withPassword) accounts.Add(PasswordAuthHandler.AccountPrefix + username);

        if (accounts.Count == 0)
        {
            logger.LogWarning("No employees exist and neither Bootstrap:AdminUsername with Bootstrap:AdminPassword nor Bootstrap:AdminAccounts is set: nobody can sign in as administrator.");
            return;
        }

        var code = configuration["Bootstrap:AdminCode"] ?? "Z-000";
        var admin = new Employee { Code = code, Name = "Administrator", IsAdmin = true };
        foreach (var account in accounts) admin.Accounts.Add(new EmployeeAccount { EmployeeCode = code, Account = account });
        db.Employees.Add(admin);
        if (withPassword)
            db.Credentials.Add(new EmployeeCredential { EmployeeCode = code, Username = username, PasswordHash = Passwords.Hash(password!), MustChange = true, ChangedUtc = DateTime.UtcNow });
        db.AuditEvents.Add(new AuditEvent
        {
            AtUtc = DateTime.UtcNow,
            Actor = "bootstrap",
            Action = "AdminCreated",
            EntityType = "Employee",
            EntityId = code,
            Details = string.Join(", ", accounts)
        });
        await db.SaveChangesAsync();
        logger.LogInformation("First administrator {Code} created for {Accounts}.", code, string.Join(", ", accounts));
    }
}
