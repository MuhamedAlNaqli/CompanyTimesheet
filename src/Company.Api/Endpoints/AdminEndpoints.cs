using System.Security.Claims;
using Company.Api.Auth;
using Company.Api.Contracts;
using Company.Api.Data;
using Company.Api.Services;
using Company.Api.Texts;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

using static Company.Api.Endpoints.EndpointResults;

namespace Company.Api.Endpoints;

/// <summary>Master data and settings for the administrator. Every change is audited in the same save.</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(Identities.AdminPolicy);

        // ---- Employees ---------------------------------------------------------------------------------------------

        admin.MapGet("/employees", async (CompanyDbContext db) =>
            Results.Ok((await db.Employees.AsNoTracking().Include(e => e.Accounts).OrderBy(e => e.Code).ToListAsync()).Select(ToDto)));

        admin.MapGet("/employees/{code}", async (string code, CompanyDbContext db) =>
        {
            var employee = await db.Employees.AsNoTracking().Include(e => e.Accounts).FirstOrDefaultAsync(e => e.Code == code);
            return employee == null ? NotFound("employee") : Results.Ok(ToDto(employee));
        });

        admin.MapPost("/employees", (CreateEmployeeRequest request, EmployeeAdminService service) =>
            Run(async () => ToDto(await service.CreateAsync(request))));

        admin.MapPut("/employees/{code}", (string code, UpdateEmployeeRequest request, EmployeeAdminService service) =>
            Run(async () => ToDto(await service.UpdateAsync(code, request))));

        admin.MapPost("/employees/import/preview", async (CsvRequest request, CompanyDbContext db) =>
        {
            var existing = await db.Employees.AsNoTracking().ToDictionaryAsync(e => e.Code, e => e.Name);
            var preview = EmployeeListImporter.Preview(request.Csv ?? "", existing);
            return Results.Ok(ToDto(preview));
        });

        admin.MapPost("/employees/import", async (CsvRequest request, CompanyDbContext db, AuditService audit, EmployeeLookupCache cache) =>
        {
            var existing = await db.Employees.ToDictionaryAsync(e => e.Code);
            var preview = EmployeeListImporter.Preview(request.Csv ?? "", existing.ToDictionary(p => p.Key, p => p.Value.Name));

            int created = 0, updated = 0;
            foreach (var row in preview.Valid)
            {
                if (existing.TryGetValue(row.Code, out var employee))
                {
                    if (employee.Name == row.Name) continue;
                    audit.Record("EmployeeRenamed", "Employee", row.Code, $"{employee.Name} -> {row.Name}");
                    employee.Name = row.Name;
                    updated++;
                }
                else
                {
                    var added = new Employee { Code = row.Code, Name = row.Name };
                    db.Employees.Add(added);
                    existing[row.Code] = added;
                    audit.Record("EmployeeCreated", "Employee", row.Code, row.Name);
                    created++;
                }
            }

            audit.Record("EmployeesImported", "Employee", "*", $"created {created}, updated {updated}, skipped {preview.ErrorCount}");
            await db.SaveChangesAsync();
            cache.Clear();
            return Results.Ok(new ImportResultDto(created, updated, preview.ErrorCount));
        });

        // ---- Accounts ----------------------------------------------------------------------------------------------

        admin.MapPost("/employees/{code}/accounts", async (string code, AccountRequest request, CompanyDbContext db, AuditService audit, EmployeeLookupCache cache) =>
        {
            var account = AccountNormalizer.Normalize(request.Account);
            if (account.Length == 0) return Results.BadRequest(new ApiErrorDto("account-required", "The account cannot be empty."));
            if (!await db.Employees.AnyAsync(e => e.Code == code)) return NotFound("employee");

            var owner = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Account == account);
            if (owner != null)
                return owner.EmployeeCode == code
                    ? Results.Ok()
                    : Results.Conflict(new ApiErrorDto("account-taken", $"The account is already linked to {owner.EmployeeCode}."));

            db.Accounts.Add(new EmployeeAccount { EmployeeCode = code, Account = account });
            audit.Record("AccountLinked", "Employee", code, account);
            await db.SaveChangesAsync();
            cache.Clear();
            return Results.Ok();
        });

        admin.MapDelete("/employees/{code}/accounts", async (string code, string account, CompanyDbContext db, AuditService audit, EmployeeLookupCache cache) =>
        {
            var normalized = AccountNormalizer.Normalize(account);
            var link = await db.Accounts.FirstOrDefaultAsync(a => a.EmployeeCode == code && a.Account == normalized);
            if (link == null) return NotFound("account");

            db.Accounts.Remove(link);
            audit.Record("AccountUnlinked", "Employee", code, normalized);
            await db.SaveChangesAsync();
            cache.Clear();
            return Results.NoContent();
        });

        // Proposes links between directory users and employees by name; nothing is saved
        admin.MapPost("/accounts/match", async (List<DirectoryUserDto> users, CompanyDbContext db) =>
        {
            var employees = (await db.Employees.AsNoTracking().Where(e => e.IsActive).ToListAsync()).Select(e => (e.Code, e.Name)).ToList();
            var linked = (await db.Accounts.AsNoTracking().Select(a => a.Account).ToListAsync()).ToHashSet();
            return Results.Ok(AccountMatcher.Match(users ?? new List<DirectoryUserDto>(), employees, linked));
        });

        // ---- Holidays ----------------------------------------------------------------------------------------------

        admin.MapGet("/holidays/{year:int}", async (int year, CompanyDbContext db) =>
            Results.Ok((await db.StateHolidays.AsNoTracking().Where(h => h.Date.Year == year).OrderBy(h => h.Date).ToListAsync())
                .Select(h => new HolidayDto(h.Date, h.Name))));

        admin.MapPost("/holidays", async (HolidayDto holiday, CompanyDbContext db, AuditService audit) =>
        {
            var name = holiday.Name?.Trim() ?? "";
            if (name.Length == 0) return Results.BadRequest(new ApiErrorDto("name-required", "The holiday needs a name."));
            if (TooLong(name, Limits.Name, "The name") is { } tooLong) return tooLong;

            var existing = await db.StateHolidays.FirstOrDefaultAsync(h => h.Date == holiday.Date);
            if (existing != null) existing.Name = name;
            else db.StateHolidays.Add(new StateHoliday { Date = holiday.Date, Name = name });

            audit.Record(existing == null ? "HolidayAdded" : "HolidayRenamed", "StateHoliday", holiday.Date.ToString("yyyy-MM-dd"), name);
            await db.SaveChangesAsync();
            return Results.Ok(new HolidayDto(holiday.Date, name));
        });

        admin.MapDelete("/holidays/{date}", async (DateOnly date, CompanyDbContext db, AuditService audit) =>
        {
            var holiday = await db.StateHolidays.FirstOrDefaultAsync(h => h.Date == date);
            if (holiday == null) return NotFound("holiday");

            db.StateHolidays.Remove(holiday);
            audit.Record("HolidayRemoved", "StateHoliday", date.ToString("yyyy-MM-dd"), holiday.Name);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapGet("/employees/{code}/religious-holidays", async (string code, CompanyDbContext db) =>
            Results.Ok((await db.ReligiousHolidays.AsNoTracking().Where(h => h.EmployeeCode == code).OrderBy(h => h.Date).ToListAsync())
                .Select(h => new HolidayDto(h.Date, h.Name))));

        // Replaces the religious holidays of one employee with the given list
        admin.MapPut("/employees/{code}/religious-holidays", async (string code, List<HolidayDto> holidays, CompanyDbContext db, AuditService audit) =>
        {
            if (!await db.Employees.AnyAsync(e => e.Code == code)) return NotFound("employee");

            var list = (holidays ?? new List<HolidayDto>()).GroupBy(h => h.Date).Select(g => g.First()).ToList();
            var longest = list.Select(h => h.Name?.Trim() ?? "").OrderByDescending(n => n.Length).FirstOrDefault() ?? "";
            if (TooLong(longest, Limits.Name, "The name") is { } tooLong) return tooLong;
            db.ReligiousHolidays.RemoveRange(db.ReligiousHolidays.Where(h => h.EmployeeCode == code));
            db.ReligiousHolidays.AddRange(list.Select(h => new ReligiousHoliday { EmployeeCode = code, Date = h.Date, Name = h.Name?.Trim() ?? "" }));
            audit.Record("ReligiousHolidaysSet", "Employee", code, string.Join(", ", list.Select(h => h.Date.ToString("yyyy-MM-dd"))));
            await db.SaveChangesAsync();
            return Results.Ok(list);
        });

        // ---- Codes of the legend -----------------------------------------------------------------------------------

        admin.MapGet("/codes", async (CompanyDbContext db) =>
        {
            var used = (await db.SheetEntries.AsNoTracking().Select(e => e.Code).Distinct().ToListAsync()).ToHashSet();
            var rows = await db.Codes.AsNoTracking().OrderBy(c => c.Code).ToListAsync();
            return Results.Ok(rows.Select(c => ToDto(c, used.Contains(c.Code))));
        });

        admin.MapPost("/codes", async (SaveCodeRequest request, CompanyDbContext db, AuditService audit) =>
        {
            if (CheckCode(request, isNew: true) is { } refusal) return refusal;
            if (await db.Codes.AnyAsync(c => c.Code == request.Code))
                return Results.Conflict(new ApiErrorDto("code-exists", $"The code {request.Code} already exists."));

            var row = new CodeDefinition
            {
                Code = request.Code,
                NameBs = request.NameBs.Trim(),
                NameEn = string.IsNullOrWhiteSpace(request.NameEn) ? request.NameBs.Trim() : request.NameEn.Trim(),
                Colour = request.Colour,
                Unit = request.Unit,
                IsActive = request.IsActive,
                IsSystem = false
            };
            db.Codes.Add(row);
            audit.Record("CodeAdded", "Code", row.Code.ToString(), $"{row.NameBs} ({row.Colour}, {row.Unit})");
            await db.SaveChangesAsync();
            await CodeCatalog.LoadAsync(db);
            return Results.Ok(ToDto(row, false));
        });

        admin.MapPut("/codes/{code:int}", async (int code, SaveCodeRequest request, CompanyDbContext db, AuditService audit) =>
        {
            var row = await db.Codes.FirstOrDefaultAsync(c => c.Code == code);
            if (row == null) return NotFound("code");
            if (CheckCode(request, isNew: false) is { } refusal) return refusal;

            var inUse = await db.SheetEntries.AnyAsync(e => e.Code == code);
            var changesMeaning = request.Colour != row.Colour || request.Unit != row.Unit;
            if (row.IsSystem && (changesMeaning || !request.IsActive))
                return Results.BadRequest(new ApiErrorDto("system-code", "The rules of the program rely on this code: it can only be renamed."));
            if (inUse && changesMeaning)
                return Results.BadRequest(new ApiErrorDto("code-in-use", "Sheets already use this code, so its colour and unit cannot change. Switch it off and add a new code."));

            var details = $"{row.NameBs} -> {request.NameBs.Trim()}, active {row.IsActive} -> {request.IsActive}, {row.Colour}/{row.Unit} -> {request.Colour}/{request.Unit}";
            row.NameBs = request.NameBs.Trim();
            row.NameEn = string.IsNullOrWhiteSpace(request.NameEn) ? row.NameBs : request.NameEn.Trim();
            row.Colour = request.Colour;
            row.Unit = request.Unit;
            row.IsActive = request.IsActive;
            audit.Record("CodeChanged", "Code", code.ToString(), details);
            await db.SaveChangesAsync();
            await CodeCatalog.LoadAsync(db);
            return Results.Ok(ToDto(row, inUse));
        });

        // ---- Timesheets --------------------------------------------------------------------------------------------

        // Hands the step a submitted timesheet waits at to somebody else (the approver left, or is away for long)
        admin.MapPost("/sheets/{code}/{year:int}/{month:int}/reassign", async (string code, int year, int month, ReassignRequest request, ClaimsPrincipal user, WorkflowService workflow) =>
        {
            try
            {
                return Results.Ok(await workflow.ReassignAsync(Identities.EmployeeCode(user)!, code, year, month, request.ToCode, request.Reason));
            }
            catch (SheetException ex)
            {
                return Results.Json(new ApiErrorDto(ex.Code, ex.Message), statusCode: ex.Status);
            }
        });

        // ---- Audit -------------------------------------------------------------------------------------------------

        // Checks that nothing of the log was changed, removed or inserted after it was written
        admin.MapGet("/audit/verify", async (CompanyDbContext db) =>
        {
            var result = await AuditChain.VerifyAsync(db);
            return Results.Ok(new AuditVerificationDto(result.IsIntact, result.Checked, result.Unchained, result.FirstBrokenId, result.Problem));
        });

        admin.MapGet("/audit", async (CompanyDbContext db, int? take) =>
        {
            var events = await db.AuditEvents.AsNoTracking().OrderByDescending(e => e.Id).Take(Math.Clamp(take ?? 100, 1, 1000)).ToListAsync();
            return Results.Ok(events.Select(e => new AuditEventDto(e.Id, e.AtUtc, e.Actor, e.Action, e.EntityType, e.EntityId, e.Details)));
        });
    }

    private static IResult? TooLong(string text, int max, string what) =>
        text.Length > max ? Results.BadRequest(new ApiErrorDto("too-long", $"{what} can have at most {max} characters; this one has {text.Length}.")) : null;

    private static CodeDefinitionDto ToDto(CodeDefinition c, bool inUse) =>
        new(c.Code, c.NameBs, c.NameEn, c.Colour, c.Unit, c.IsActive, c.IsSystem, inUse);

    /// <summary>What a code may be. Overtime (red) is worked out from the intervals by the program, so a new code cannot be overtime.</summary>
    private static IResult? CheckCode(SaveCodeRequest request, bool isNew)
    {
        if (request.Code < CodeCatalog.FirstCode || request.Code > CodeCatalog.LastCode)
            return Results.BadRequest(new ApiErrorDto("invalid-code", $"The code is a number from {CodeCatalog.FirstCode} to {CodeCatalog.LastCode}."));
        var name = request.NameBs?.Trim() ?? "";
        if (name.Length == 0) return Results.BadRequest(new ApiErrorDto("name-required", "The code needs a name."));
        if (TooLong(name, Limits.Name, "The name") is { } tooLong) return tooLong;
        if (TooLong(request.NameEn?.Trim() ?? "", Limits.Name, "The English name") is { } tooLongEn) return tooLongEn;
        if (!Enum.TryParse<CodeUnit>(request.Unit, out _) || !Enum.TryParse<CodeColour>(request.Colour, out var colour))
            return Results.BadRequest(new ApiErrorDto("invalid-kind", "Unknown colour or unit."));
        if (isNew && colour == CodeColour.Red)
            return Results.BadRequest(new ApiErrorDto("overtime-code", "Overtime codes come with the program: overtime is worked out from the intervals."));
        return null;
    }

    private static IResult NotFound(string what) => Results.NotFound(new ApiErrorDto("not-found", $"The {what} was not found."));

    private static EmployeeDto ToDto(Employee e) => new(
        e.Code, e.Name, e.IsActive, e.SupervisorCode, e.IsFinalApprover, e.DesignatedApproverCode, e.IsAdmin, e.ActiveFrom, e.ActiveTo,
        e.Accounts.Select(a => a.Account).OrderBy(a => a).ToList(), e.IsFinance, e.Email);

    private static ImportPreviewDto ToDto(EmployeeImportPreview preview) => new(
        preview.Rows.Select(r => new ImportRowDto(
            r.Line, r.Code, r.Name,
            r.Issues.Where(i => i.Severity == ImportSeverity.Error).Select(ImportTexts.Message).ToList(),
            r.Issues.Where(i => i.Severity == ImportSeverity.Warning).Select(ImportTexts.Message).ToList())).ToList(),
        preview.ErrorCount, preview.WarningCount);
}
