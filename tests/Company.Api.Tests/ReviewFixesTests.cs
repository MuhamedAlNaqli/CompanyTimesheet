using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Company.Api.Auth;
using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Company.Api.Tests;

/// <summary>What the code review of the employee side found, as tests</summary>
[TestClass]
public class ReviewFixesTests
{
    private static async Task<(ApiFactory Factory, HttpClient Amra, HttpClient Benjamin, HttpClient Emir, HttpClient Damir)> StartAsync(TimeProvider? time = null, Dictionary<string, string?>? settings = null)
    {
        var factory = new ApiFactory(settings);
        if (time != null) factory.Time = time;
        await factory.ImportAsync(ApiFactory.SmallList);
        foreach (var (code, account) in new[] { ("Z-001", @"corp\amra"), ("Z-002", @"corp\benjamin"), ("Z-003", @"corp\emir"), ("Z-004", @"corp\damir") })
            await factory.LinkAsync(code, account);
        await factory.UpdateAsync("Z-001", new(null, null, "Z-002", null, null, null, null, null));
        await factory.UpdateAsync("Z-002", new(null, null, "Z-003", null, null, null, null, null));
        await factory.UpdateAsync("Z-003", new(null, null, null, true, null, null, null, null));
        return (factory, factory.As(@"corp\amra"), factory.As(@"corp\benjamin"), factory.As(@"corp\emir"), factory.As(@"corp\damir"));
    }

    private static async Task<SheetDto> Read(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<SheetDto>())!;
    }

    private static async Task<string> ErrorCode(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<ApiErrorDto>())!.Code;
    }

    // ---- F-01: overtime across the turn of the month ----------------------------------------------------------------------

    [TestMethod]
    public async Task OvertimeAfterMidnightOnTheLastDay_ShouldBeCountedInTheNextMonth()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            // Wednesday 30 September 23:00 to Thursday 1 October 01:00, both working days: night overtime (2021) on both sides
            var september = await Read(await amra.PostAsJsonAsync("/api/timesheet/2026/9/overtime",
                new OvertimeRequest(new DateOnly(2026, 9, 30), new TimeOnly(23, 0), new TimeOnly(1, 0), "Hitna ispravka")));
            Assert.AreEqual(60, september.Summary.OvertimeMinutes, "September counts the hour until midnight");
            Assert.AreEqual(120, september.Overtime.Single().TotalMinutes, "the record is two hours");
            Assert.AreEqual(60, september.Overtime.Single().MinutesInNextMonth, "and says that an hour belongs to October");

            var october = await Read(await amra.GetAsync("/api/timesheet/2026/10"));
            Assert.AreEqual(60, october.Summary.OvertimeMinutes, "October counts the hour after midnight");
            var first = october.Days[0];
            Assert.AreEqual(new AllocationDto(new DateOnly(2026, 10, 1), Codes.NightOvertime, 60), first.OvertimeAllocations.Single());
            var carried = october.CarriedIn!.Single();
            Assert.AreEqual(new DateOnly(2026, 9, 30), carried.Date);
            Assert.AreEqual(60, carried.MinutesInThisMonth);
            Assert.AreEqual(0, october.Overtime.Count, "it is not a record of October, so it cannot be edited there");
        }
    }

    [TestMethod]
    public async Task OvertimeAfterMidnightOnNewYearsEve_ShouldGoToJanuary_WithTheTypeOfTheDayInTheNewYear()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            // 1 January 2027 is a Friday and a state holiday: the part after midnight is night overtime on a holiday (2025)
            await factory.Admin.PostAsJsonAsync("/api/admin/holidays", new HolidayDto(new DateOnly(2027, 1, 1), "Nova godina"));
            await amra.PostAsJsonAsync("/api/timesheet/2026/12/overtime", new OvertimeRequest(new DateOnly(2026, 12, 31), new TimeOnly(23, 0), new TimeOnly(2, 0), "Dežurstvo"));

            var january = await Read(await amra.GetAsync("/api/timesheet/2027/1"));
            Assert.AreEqual(120, january.Summary.OvertimeMinutes);
            Assert.AreEqual(Codes.NightHolidayOvertime, january.Days[0].OvertimeAllocations.Single().Code);
        }
    }

    [TestMethod]
    public async Task OvertimeThatDoesNotCrossMidnight_ShouldNotLeakIntoTheNextMonth()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            await amra.PostAsJsonAsync("/api/timesheet/2026/9/overtime", new OvertimeRequest(new DateOnly(2026, 9, 30), new TimeOnly(18, 0), new TimeOnly(20, 0), "Redovan prekovremeni"));
            var october = await Read(await amra.GetAsync("/api/timesheet/2026/10"));
            Assert.AreEqual(0, october.Summary.OvertimeMinutes);
            Assert.AreEqual(0, october.CarriedIn!.Count);
        }
    }

    // ---- F-02: nothing changes without the header of the app ---------------------------------------------------------------

    [TestMethod]
    public async Task ARequestThatChangesDataNeedsTheHeaderOfTheApp_AndMustNotComeFromAnotherSite()
    {
        var (factory, _, _, _, _) = await StartAsync();
        using (factory)
        {
            var bare = factory.CreateClient();
            bare.DefaultRequestHeaders.Add(AuthenticationSetup.TestHeader, @"corp\amra");

            Assert.AreEqual("missing-header", await ErrorCode(await bare.PostAsync("/api/timesheet/2026/9/fill-standard-days", null), HttpStatusCode.BadRequest), "what a web page can send");
            Assert.AreEqual(HttpStatusCode.OK, (await bare.GetAsync("/api/timesheet/2026/9")).StatusCode, "reading needs nothing");

            var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/timesheet/2026/9/fill-standard-days");
            foreign.Headers.Add(AuthenticationSetup.TestHeader, @"corp\amra");
            foreign.Headers.Add(ApiHeaders.RequestedWith, ApiHeaders.RequestedWithValue);
            foreign.Headers.Add("Origin", "https://evil.example");
            Assert.AreEqual("forbidden-origin", await ErrorCode(await bare.SendAsync(foreign), HttpStatusCode.Forbidden));

            var own = factory.As(@"corp\amra");
            Assert.AreEqual(HttpStatusCode.OK, (await own.PostAsync("/api/timesheet/2026/9/fill-standard-days", null)).StatusCode);
        }
    }

    // ---- F-03: who an account is ----------------------------------------------------------------------------------------------

    [TestMethod]
    public void AnEntraCaller_IsKnownByTheObjectId_NotByAnythingTheUserCanChange()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("name", "Jane Doe"),
            new Claim("preferred_username", "jane@corp.com"),
            new Claim("email", "boss@corp.com"),
            new Claim("oid", "11111111-2222"),
            new Claim("tid", "tenant-1"),
            new Claim("upn", "Jane@Corp.com")
        }, "Bearer", "name", ClaimTypes.Role);

        CollectionAssert.AreEquivalent(new[] { "entra:tenant-1/11111111-2222", "jane@corp.com" }, Identities.Candidates(new ClaimsPrincipal(identity)).ToList());
    }

    [TestMethod]
    public void AWindowsCaller_IsKnownByTheWindowsNameAndTheUpn()
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, @"CORP\Amra"), new Claim(ClaimTypes.Upn, "amra@corp.com") }, "Negotiate");
        CollectionAssert.AreEquivalent(new[] { @"corp\amra", "amra@corp.com" }, Identities.Candidates(new ClaimsPrincipal(identity)).ToList());
    }

    [TestMethod]
    public async Task NamesThatLeadToTwoEmployees_SignNobodyIn()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<CompanyDbContext>().UseSqlite(connection).Options;
        await using var db = new CompanyDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Employees.AddRange(new Employee { Code = "Z-001", Name = "Ena" }, new Employee { Code = "Z-002", Name = "Eda" });
        db.Accounts.AddRange(new EmployeeAccount { EmployeeCode = "Z-001", Account = @"corp\ena" }, new EmployeeAccount { EmployeeCode = "Z-002", Account = "ena@corp.com" });
        await db.SaveChangesAsync();

        var transformation = new EmployeeClaimsTransformation(db, new EmployeeLookupCache(), NullLogger<EmployeeClaimsTransformation>.Instance, new CompanyFeatures(true));
        var both = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, @"CORP\ena"), new Claim(ClaimTypes.Upn, "ena@corp.com") }, "Negotiate"));
        Assert.IsNull((await transformation.TransformAsync(both)).FindFirst(Identities.EmployeeCodeClaim), "two different employees: refused");

        var one = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, @"CORP\ena") }, "Negotiate"));
        Assert.AreEqual("Z-001", (await transformation.TransformAsync(one)).FindFirst(Identities.EmployeeCodeClaim)!.Value);
    }

    // ---- F-04: lengths -------------------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task TextThatIsTooLong_IsRefusedWithAMessage_NotWithAServerError()
    {
        var (factory, amra, benjamin, _, _) = await StartAsync();
        using (factory)
        {
            Assert.AreEqual("too-long", await ErrorCode(await amra.PostAsJsonAsync("/api/timesheet/2026/9/overtime",
                new OvertimeRequest(new DateOnly(2026, 9, 7), new TimeOnly(18, 0), new TimeOnly(20, 0), new string('x', 501))), HttpStatusCode.BadRequest));
            var ok = await amra.PostAsJsonAsync("/api/timesheet/2026/9/overtime",
                new OvertimeRequest(new DateOnly(2026, 9, 7), new TimeOnly(18, 0), new TimeOnly(20, 0), new string('x', 500)));
            Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode, "500 characters is the limit");

            await amra.PostAsync("/api/timesheet/2026/9/fill-standard-days", null);
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);
            Assert.AreEqual("too-long", await ErrorCode(await benjamin.PostAsJsonAsync("/api/approvals/Z-001/2026/9/return", new ReturnRequest(new string('y', 2001))), HttpStatusCode.BadRequest));
            await Read(await benjamin.PostAsJsonAsync("/api/approvals/Z-001/2026/9/return", new ReturnRequest("Ispravi")));
            Assert.AreEqual("too-long", await ErrorCode(await amra.PostAsJsonAsync("/api/timesheet/2026/9/messages/0/answer", new AnswerRequest(new string('z', 2001))), HttpStatusCode.BadRequest));

            Assert.AreEqual(HttpStatusCode.BadRequest, (await factory.Admin.PostAsJsonAsync("/api/admin/holidays", new HolidayDto(new DateOnly(2026, 9, 9), new string('h', 151)))).StatusCode);
        }
    }

    // ---- F-06: two requests at once ---------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task TwoFirstOpeningsOfANewMonth_BothSucceed()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => amra.GetAsync("/api/timesheet/2026/9")));
            Assert.IsTrue(responses.All(r => r.StatusCode == HttpStatusCode.OK), string.Join(",", responses.Select(r => (int)r.StatusCode)));
        }
    }

    [TestMethod]
    public async Task EditsThatRaceEachOther_AreAcceptedOrToldToReopen_NeverAServerError()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            await amra.GetAsync("/api/timesheet/2026/9");
            var responses = await Task.WhenAll(Enumerable.Range(1, 6).Select(day =>
                amra.PutAsJsonAsync($"/api/timesheet/2026/9/days/2026-09-{day + 6:00}", new SetEntryRequest(Codes.RegularWork, 480))));
            Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict), string.Join(",", responses.Select(r => (int)r.StatusCode)));
        }
    }

    // ---- F-07, F-18: people who leave, and mistakes of the administrator ---------------------------------------------------------

    [TestMethod]
    public async Task APersonWhoStillHasPeopleOrSheetsWaiting_CannotBeDeactivated()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            var refused = await factory.UpdateAsync("Z-002", new(null, false, null, null, null, null, null, null));
            Assert.AreEqual("has-reports", await ErrorCode(refused, HttpStatusCode.Conflict));

            await factory.UpdateAsync("Z-001", new(null, null, "Z-004", null, null, null, null, null));
            await amra.PostAsync("/api/timesheet/2026/9/fill-standard-days", null);
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);
            Assert.AreEqual(HttpStatusCode.OK, (await factory.UpdateAsync("Z-002", new(null, false, null, null, null, null, null, null))).StatusCode, "nobody reports to Benjamin now");

            Assert.AreEqual("has-reports", await ErrorCode(await factory.UpdateAsync("Z-004", new(null, false, null, null, null, null, null, null)), HttpStatusCode.Conflict), "Amra's sheet waits for Damir");
        }
    }

    [TestMethod]
    public async Task TheLastAdministratorCannotBeRemoved()
    {
        var (factory, _, _, _, _) = await StartAsync();
        using (factory)
        {
            Assert.AreEqual("last-admin", await ErrorCode(await factory.UpdateAsync("Z-000", new(null, null, null, null, null, false, null, null)), HttpStatusCode.Conflict));
            Assert.AreEqual("last-admin", await ErrorCode(await factory.UpdateAsync("Z-000", new(null, false, null, null, null, null, null, null)), HttpStatusCode.Conflict));
        }
    }

    [TestMethod]
    public async Task AStepThatWaitsForSomebodyWhoLeft_CanBeHandedToSomebodyElse_WithAReason()
    {
        var (factory, amra, benjamin, _, damir) = await StartAsync();
        using (factory)
        {
            await amra.PostAsync("/api/timesheet/2026/9/fill-standard-days", null);
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);

            Assert.AreEqual("message-required", await ErrorCode(await factory.Admin.PostAsJsonAsync("/api/admin/sheets/Z-001/2026/9/reassign", new ReassignRequest("Z-004", " ")), HttpStatusCode.BadRequest));
            var handed = await Read(await factory.Admin.PostAsJsonAsync("/api/admin/sheets/Z-001/2026/9/reassign", new ReassignRequest("Z-004", "Benjamin je napustio firmu")));
            Assert.AreEqual("Z-004", handed.Workflow!.CurrentApprover);
            Assert.AreEqual(HistoryKind.Reassigned, handed.Workflow.History.Last().Kind);

            Assert.AreEqual("not-your-turn", await ErrorCode(await benjamin.PostAsync("/api/approvals/Z-001/2026/9/approve", null), HttpStatusCode.Forbidden));
            Assert.AreEqual(SheetStatus.Submitted, (await Read(await damir.PostAsync("/api/approvals/Z-001/2026/9/approve", null))).Status);
            Assert.AreEqual(1, (await factory.Admin.GetFromJsonAsync<List<AuditEventDto>>("/api/admin/audit?take=300"))!.Count(e => e.Action == "Reassigned"));
        }
    }

    [TestMethod]
    public async Task EmploymentDatesCanBeClearedAgain_AndNothingIsEnteredOutsideThem()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            await factory.UpdateAsync("Z-001", new(null, null, null, null, null, null, new DateOnly(2026, 9, 10), null));
            Assert.AreEqual("outside-employment", await ErrorCode(await amra.PutAsJsonAsync("/api/timesheet/2026/9/days/2026-09-03", new SetEntryRequest(Codes.RegularWork, 480)), HttpStatusCode.BadRequest));
            Assert.AreEqual("outside-employment", await ErrorCode(await amra.PostAsJsonAsync("/api/timesheet/2026/9/overtime",
                new OvertimeRequest(new DateOnly(2026, 9, 3), new TimeOnly(18, 0), new TimeOnly(19, 0), "Prije zaposlenja")), HttpStatusCode.BadRequest));

            var cleared = await factory.UpdateAsync("Z-001", new(null, null, null, null, null, null, null, null, ClearActiveFrom: true));
            Assert.IsNull((await cleared.ReadAsync<EmployeeDto>())!.ActiveFrom);
            Assert.AreEqual(HttpStatusCode.OK, (await amra.PutAsJsonAsync("/api/timesheet/2026/9/days/2026-09-03", new SetEntryRequest(Codes.RegularWork, 480))).StatusCode);
        }
    }

    // ---- F-09, F-10: who may look, and the trace of it -------------------------------------------------------------------------

    [TestMethod]
    public async Task OpeningAColleaguesSheet_IsWrittenToTheAuditLog()
    {
        var (factory, amra, benjamin, _, _) = await StartAsync();
        using (factory)
        {
            await amra.PostAsync("/api/timesheet/2026/9/fill-standard-days", null);
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);

            await benjamin.GetAsync("/api/approvals/Z-001/2026/9");
            await benjamin.GetAsync("/api/approvals/Z-001/2026/9/print/xlsx");
            await benjamin.GetAsync("/api/approvals/Z-001/2026/9/revisions/1");
            await amra.GetAsync("/api/timesheet/2026/9");

            var log = (await factory.Admin.GetFromJsonAsync<List<AuditEventDto>>("/api/admin/audit?take=300"))!;
            Assert.AreEqual(1, log.Count(e => e.Action == "SheetViewed" && e.Actor == "Z-002"));
            Assert.AreEqual(1, log.Count(e => e.Action == "SheetPrinted" && e.Actor == "Z-002"));
            Assert.AreEqual(1, log.Count(e => e.Action == "RevisionViewed" && e.Actor == "Z-002"));
            Assert.AreEqual(0, log.Count(e => e.Action.Contains("Viewed") && e.Actor == "Z-001"), "the owner looking at their own sheet leaves nothing");
        }
    }

    [TestMethod]
    public async Task APersonMovedAwayFromTheOwner_NoLongerSeesASheetThatIsNoLongerWaitingForThem()
    {
        var (factory, amra, benjamin, emir, damir) = await StartAsync();
        using (factory)
        {
            await amra.PostAsync("/api/timesheet/2026/9/fill-standard-days", null);
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);
            await benjamin.PostAsJsonAsync("/api/approvals/Z-001/2026/9/return", new ReturnRequest("Ispravi"));

            Assert.AreEqual(HttpStatusCode.OK, (await emir.GetAsync("/api/approvals/Z-001/2026/9")).StatusCode, "the lawyer is above Amra today");

            await factory.UpdateAsync("Z-001", new(null, null, "Z-004", null, null, null, null, null));
            Assert.AreEqual("forbidden", await ErrorCode(await emir.GetAsync("/api/approvals/Z-001/2026/9"), HttpStatusCode.Forbidden), "Amra reports to Damir now, and Emir never acted");
            Assert.AreEqual(HttpStatusCode.OK, (await benjamin.GetAsync("/api/approvals/Z-001/2026/9")).StatusCode, "Benjamin returned it: he took part");
            Assert.AreEqual(HttpStatusCode.OK, (await damir.GetAsync("/api/approvals/Z-001/2026/9")).StatusCode, "Damir is the supervisor today");
        }
    }

    [TestMethod]
    public async Task ASupervisorDoesNotSeeADraftThatWasNeverSubmitted()
    {
        var (factory, amra, benjamin, _, _) = await StartAsync();
        using (factory)
        {
            await amra.GetAsync("/api/timesheet/2026/9");
            Assert.AreEqual("forbidden", await ErrorCode(await benjamin.GetAsync("/api/approvals/Z-001/2026/9"), HttpStatusCode.Forbidden));
        }
    }

    // ---- F-16: an answer stays as it was ------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task AnAnswerCannotBeWrittenOver()
    {
        var (factory, amra, benjamin, _, _) = await StartAsync();
        using (factory)
        {
            await amra.PostAsync("/api/timesheet/2026/9/fill-standard-days", null);
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);
            await benjamin.PostAsJsonAsync("/api/approvals/Z-001/2026/9/return", new ReturnRequest("Ispravi"));
            await Read(await amra.PostAsJsonAsync("/api/timesheet/2026/9/messages/0/answer", new AnswerRequest("Prvi odgovor")));

            Assert.AreEqual("already-answered", await ErrorCode(await amra.PostAsJsonAsync("/api/timesheet/2026/9/messages/0/answer", new AnswerRequest("Drugi odgovor")), HttpStatusCode.Conflict));
            Assert.AreEqual("Prvi odgovor", (await amra.GetFromJsonAsync<SheetDto>("/api/timesheet/2026/9"))!.Workflow!.Messages.Single().Answer);
        }
    }

    // ---- F-17: a month that was skipped stays overdue ----------------------------------------------------------------------------

    [TestMethod]
    public async Task AMonthThatWasSkippedLongAgo_IsStillRemindedAbout()
    {
        var (factory, amra, _, _, _) = await StartAsync(new FixedTime(2026, 11, 2));
        using (factory)
        {
            var due = (await amra.GetFromJsonAsync<RemindersDto>("/api/reminders"))!.Items;
            var months = due.Where(d => d.Kind == ReminderType.SubmitOverdue).Select(d => d.Month).OrderBy(m => m).ToArray();
            CollectionAssert.AreEqual(new[] { 5, 6, 7, 8, 9, 10 }, months, "six months back, as nothing was submitted; November is not due until the 22nd");

            await amra.PostAsync("/api/timesheet/2026/8/fill-standard-days", null);
            await amra.PostAsync("/api/timesheet/2026/8/submit", null);
            Assert.IsFalse((await amra.GetFromJsonAsync<RemindersDto>("/api/reminders"))!.Items.Any(d => d.Month == 8 && d.Kind is ReminderType.SubmitSoon or ReminderType.SubmitOverdue), "a submitted month is not due");
        }
    }

    [TestMethod]
    public async Task TheStartMonthStillLimitsTheReminders()
    {
        var (factory, amra, _, _, _) = await StartAsync(new FixedTime(2026, 11, 2), new Dictionary<string, string?> { ["Reminders:StartFrom"] = "2026-10" });
        using (factory)
        {
            var months = (await amra.GetFromJsonAsync<RemindersDto>("/api/reminders"))!.Items.Select(d => d.Month).OrderBy(m => m).ToArray();
            CollectionAssert.AreEqual(new[] { 10 }, months, "earlier months are before the start; November is not due yet");
        }
    }

    // ---- F-12: dates leave the server as UTC ----------------------------------------------------------------------------------------

    [TestMethod]
    public async Task EveryMomentInTheAnswerIsMarkedAsUtc()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            await amra.PostAsync("/api/timesheet/2026/9/fill-standard-days", null);
            var submitted = await Read(await amra.PostAsync("/api/timesheet/2026/9/submit", null));
            Assert.AreEqual(DateTimeKind.Utc, submitted.Workflow!.Revisions.Single().SubmittedUtc.Kind);
            Assert.AreEqual(DateTimeKind.Utc, submitted.Workflow.History.First().AtUtc.Kind);
        }
    }

    // ---- F-20: a Test server only on this computer ------------------------------------------------------------------------------

    [TestMethod]
    public void TheTestSignIn_RefusesToListenOnTheNetwork()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration["Authentication:Mode"] = "Test";
        builder.Configuration["urls"] = "http://0.0.0.0:5000";
        Assert.ThrowsException<InvalidOperationException>(() => builder.AddCompanyAuthentication());

        var local = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        local.Configuration["Authentication:Mode"] = "Test";
        local.Configuration["urls"] = "http://localhost:5091;http://127.0.0.1:5092";
        local.AddCompanyAuthentication();
    }
}
