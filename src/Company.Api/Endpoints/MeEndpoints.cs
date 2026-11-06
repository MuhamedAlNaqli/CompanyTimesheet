using System.Security.Claims;
using Company.Api.Auth;
using Company.Api.Contracts;
using Company.Api.Data;
using Company.Api.Services;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Endpoints;

public static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder app, AuthMode mode)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

        // Any signed-in caller: what the server sees. This is how IT checks Windows or Entra sign-in on a test server.
        app.MapGet("/api/auth/diagnostics", async (ClaimsPrincipal user, CompanyDbContext db) =>
        {
            var candidates = Identities.Candidates(user);
            return Results.Ok(new AuthDiagnosticsDto(
                mode.ToString(),
                user.Identity?.Name,
                user.Identity?.AuthenticationType,
                user.Identity?.IsAuthenticated == true,
                candidates,
                Identities.EmployeeCode(user),
                user.Claims.Where(c => c.Type != Identities.ResolvedClaim).Select(c => new ClaimDto(c.Type, c.Value)).ToList()));
        });

        var registered = app.MapGroup("/api").RequireAuthorization(Identities.RegisteredPolicy);

        registered.MapGet("/me", async (ClaimsPrincipal user, CompanyDbContext db, ChartService chart, CompanyFeatures features, DeputyService deputies) =>
        {
            var code = Identities.EmployeeCode(user)!;
            var employee = await db.Employees.AsNoTracking().Include(e => e.Accounts).FirstAsync(e => e.Code == code);
            var outcome = await chart.ChainOfAsync(code);

            return Results.Ok(new MeDto(
                employee.Code,
                employee.Name,
                user.Identity?.Name ?? "",
                employee.IsAdmin,
                user.IsInRole(Roles.Approver) || await deputies.IsDeputyNowOrSoonAsync(code),
                features.FinanceAndLegal && employee.IsFinalApprover,
                employee.SupervisorCode,
                outcome.Chain?.Approvers ?? Array.Empty<string>(),
                mode.ToString(),
                features.FinanceAndLegal && employee.IsFinance,
                features.SheetExport,
                employee.ThemeMode,
                employee.Palette,
                employee.Language));
        });

        // What is due for the caller today: the rules are the domain library's, this only gathers the facts
        registered.MapGet("/reminders", (ClaimsPrincipal user, ReminderService service) => service.ForAsync(Identities.EmployeeCode(user)!));

        registered.MapGet("/timecodes", () => Results.Ok(TimeCodes.All.Select(c => new TimeCodeDto(
            c.Code, TimeCodeNames.Bs(c.Code), TimeCodeNames.En(c.Code), c.Colour.ToString(), c.Unit.ToString(), c.InMonthlyFund, c.IsOvertime, c.Active))));

        // The person chooses the theme and the colours of their own screen; it follows them to every computer
        registered.MapPut("/me/preferences", async (PreferencesRequest request, ClaimsPrincipal user, CompanyDbContext db) =>
        {
            var theme = request.ThemeMode?.Trim().ToLowerInvariant();
            var palette = request.Palette?.Trim().ToLowerInvariant();
            if (theme != null && !UiPreferences.ThemeModes.Contains(theme)) return Results.BadRequest(new ApiErrorDto("invalid-theme", "Unknown theme."));
            if (palette != null && !UiPreferences.Palettes.Contains(palette)) return Results.BadRequest(new ApiErrorDto("invalid-palette", "Unknown palette."));
            var language = request.Language?.Trim().ToLowerInvariant();
            if (language != null && !UiPreferences.Languages.Contains(language)) return Results.BadRequest(new ApiErrorDto("invalid-language", "Unknown language."));

            var code = Identities.EmployeeCode(user)!;
            var employee = await db.Employees.FirstAsync(e => e.Code == code);
            if (theme != null) employee.ThemeMode = theme;
            if (palette != null) employee.Palette = palette;
            if (language != null) employee.Language = language;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // The days of a month for the caller: weekends, state holidays and the caller's own religious holidays
        registered.MapGet("/calendar/{year:int}/{month:int}", async (int year, int month, ClaimsPrincipal user, CompanyDbContext db) =>
        {
            if (year < 2000 || year > 2100 || month < 1 || month > 12)
                return Results.BadRequest(new ApiErrorDto("invalid-month", "Choose a month between 2000 and 2100."));

            var calendar = await Reference.CalendarAsync(db, year);
            var code = Identities.EmployeeCode(user)!;
            var religious = await db.ReligiousHolidays.AsNoTracking().Where(h => h.EmployeeCode == code).ToListAsync();
            var religiousDates = religious.Select(h => h.Date).ToHashSet();

            var days = WorkCalendar.DaysOf(year, month).Select(date =>
            {
                var info = calendar.Resolve(date, religiousDates);
                var name = info.HolidayName ?? religious.FirstOrDefault(h => h.Date == date)?.Name;
                return new CalendarDayDto(date, BosnianTexts.WeekdayInitial(date), info.Type.ToDto(), name);
            }).ToList();
            return Results.Ok(days);
        });
    }
}
