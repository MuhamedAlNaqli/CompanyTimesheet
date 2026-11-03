using Company.Api.Auth;
using Company.Api.Contracts;
using Company.Api.Services;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Data;

/// <summary>
/// Sample people and a few sample months for trying the product on one PC (<c>Demo:Seed</c> = true). It runs once, on a database that holds
/// only the first administrator, and never on a database that already has people. Everybody signs in with the same password, which is
/// meant for a throw-away test database and nothing else.
/// </summary>
public static class DemoData
{
    /// <summary>The password of the sample people unless Demo:Password says another (a demo on the internet must not use this one)</summary>
    public const string DefaultPassword = "Demo-password-1";

    // Code, name, user name, supervisor
    private static readonly (string Code, string Name, string User, string? Supervisor)[] People =
    {
        ("Z-001", "Lejla Hadžić", "lejla", "Z-002"),
        ("Z-002", "Tarik Begić", "tarik", "Z-003"),
        ("Z-003", "Selma Mujkić", "selma", null),
        ("Z-004", "Haris Delić", "haris", "Z-002"),
        ("Z-005", "Maja Perić", "maja", "Z-002"),
        ("Z-006", "Nermin Čolić", "nermin", "Z-003")
    };

    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        if (!configuration.GetValue<bool>("Demo:Seed")) return;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanyDbContext>();
        if (await db.Employees.CountAsync() > 1) return;

        var password = configuration["Demo:Password"] is { Length: >= Passwords.MinimumLength } chosen ? chosen : DefaultPassword;

        foreach (var (code, name, user, supervisor) in People)
        {
            db.Employees.Add(new Employee { Code = code, Name = name, SupervisorCode = supervisor });
            db.Credentials.Add(new EmployeeCredential { EmployeeCode = code, Username = user, PasswordHash = Passwords.Hash(password), MustChange = false, ChangedUtc = DateTime.UtcNow });
            db.Accounts.Add(new EmployeeAccount { EmployeeCode = code, Account = PasswordAuthHandler.AccountPrefix + user });
        }

        await db.SaveChangesAsync();

        // Last month: Lejla worked overtime, took leave and submitted, so Tarik has something to decide on; Maja submitted a plain month
        var last = DateTime.Today.AddMonths(-1);
        var sheets = scope.ServiceProvider.GetRequiredService<TimesheetService>();
        var workflow = scope.ServiceProvider.GetRequiredService<WorkflowService>();
        try
        {
            await sheets.AddOvertimeAsync("Z-001", last.Year, last.Month, new OvertimeRequest(new DateOnly(last.Year, last.Month, 8), new TimeOnly(19, 30), new TimeOnly(22, 0), "Podrška pri obračunu"));
            await sheets.ApplyAbsenceAsync("Z-001", last.Year, last.Month, new AbsenceRangeRequest(Codes.AnnualLeaveA, new DateOnly(last.Year, last.Month, 15), new DateOnly(last.Year, last.Month, 17)));
            await workflow.SubmitAsync("Z-001", last.Year, last.Month);
            await workflow.SubmitAsync("Z-005", last.Year, last.Month);
            logger.LogInformation("Sample data created: six people and two submitted months");
        }
        catch (SheetException ex)
        {
            // The people are there; a sample month that cannot be made (a rule of the calendar) is not worth stopping the server for
            logger.LogWarning("The sample months could not be created: {Reason}", ex.Message);
        }
    }
}
