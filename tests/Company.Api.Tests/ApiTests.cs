using System.Net;
using System.Net.Http.Json;
using Company.Api.Auth;
using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Company.Api.Tests;

[TestClass]
public class SignInTests
{
    [TestMethod]
    public async Task Health_ShouldBeOpen_ButEverythingElseNeedsASignIn()
    {
        using var factory = new ApiFactory();
        using var anonymous = factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.OK, (await anonymous.GetAsync("/health")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/employees")).StatusCode);
    }

    [TestMethod]
    public async Task ASignedInAccountThatIsNotLinked_ShouldGetAClearAnswer()
    {
        using var factory = new ApiFactory();
        using var stranger = factory.As(@"corp\nobody");

        var response = await stranger.GetAsync("/api/me");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual("not-registered", (await response.ReadAsync<ApiErrorDto>())!.Code);
    }

    [TestMethod]
    public async Task TheBootstrapAdministrator_ShouldBeAbleToSignInOnAFreshInstallation()
    {
        using var factory = new ApiFactory();
        var me = await factory.Admin.GetFromJsonAsync<MeDto>("/api/me");

        Assert.AreEqual("Z-000", me!.Code);
        Assert.IsTrue(me.IsAdmin);
        Assert.AreEqual("Test", me.AuthenticationMode);
    }

    [TestMethod]
    public async Task Diagnostics_ShouldShowWhatTheServerSeesEvenForAnUnlinkedAccount()
    {
        using var factory = new ApiFactory();
        using var caller = factory.As(@"CORP\Amra.Adzamija");

        var diagnostics = await caller.GetFromJsonAsync<AuthDiagnosticsDto>("/api/auth/diagnostics");
        Assert.IsTrue(diagnostics!.IsAuthenticated);
        Assert.AreEqual(@"CORP\Amra.Adzamija", diagnostics.IdentityName);
        CollectionAssert.Contains(diagnostics.Candidates.ToList(), @"corp\amra.adzamija", "compared in lower case");
        Assert.IsNull(diagnostics.EmployeeCode);
    }

    [TestMethod]
    public async Task AccountsAreComparedWithoutRegardToCase()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"CORP\Amra.Adzamija");

        using var caller = factory.As(@"corp\AMRA.ADZAMIJA");
        var me = await caller.GetFromJsonAsync<MeDto>("/api/me");
        Assert.AreEqual("Z-001", me!.Code);
        Assert.AreEqual("Amra Adžamija", me.Name);
        Assert.IsFalse(me.IsAdmin);
    }

    [TestMethod]
    public async Task AnInactiveEmployee_ShouldNoLongerBeRecognised()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-002", @"corp\benjamin");
        Assert.AreEqual(HttpStatusCode.OK, (await factory.As(@"corp\benjamin").GetAsync("/api/me")).StatusCode);

        (await factory.UpdateAsync("Z-002", new UpdateEmployeeRequest(null, false, null, null, null, null, null, null))).EnsureSuccessStatusCode();
        Assert.AreEqual(HttpStatusCode.Forbidden, (await factory.As(@"corp\benjamin").GetAsync("/api/me")).StatusCode);
    }

    [TestMethod]
    public void BothMode_ShouldSendBearerTokensToEntraAndEverythingElseToWindows()
    {
        Assert.AreEqual(JwtBearerDefaults.AuthenticationScheme, AuthenticationSetup.SelectScheme("Bearer eyJhbGciOi..."));
        Assert.AreEqual(JwtBearerDefaults.AuthenticationScheme, AuthenticationSetup.SelectScheme("bearer abc"));
        Assert.AreEqual(NegotiateDefaults.AuthenticationScheme, AuthenticationSetup.SelectScheme("Negotiate YII..."));
        Assert.AreEqual(NegotiateDefaults.AuthenticationScheme, AuthenticationSetup.SelectScheme(""));
    }

    [TestMethod]
    public void ModesThatCannotWork_ShouldStopTheServerAtStartUp()
    {
        using var entraWithoutSettings = new ApiFactory(new() { ["Authentication:Mode"] = "Entra" });
        var ex = Assert.ThrowsException<InvalidOperationException>(() => entraWithoutSettings.CreateClient());
        StringAssert.Contains(ex.Message, "TenantId");

        using var bothWithoutSettings = new ApiFactory(new() { ["Authentication:Mode"] = "Both" });
        Assert.ThrowsException<InvalidOperationException>(() => bothWithoutSettings.CreateClient());
    }

    [TestMethod]
    public async Task WindowsAndEntraModes_ShouldStartWithTheirSchemes()
    {
        // The in-process test server cannot do a real Kerberos handshake (Negotiate needs Kestrel on a domain-joined
        // Windows server), so these tests check that each mode starts and registers the right schemes. The handshake is
        // tested against a real server with /api/auth/diagnostics (see docs/API_IT_GUIDE.md).
        using var windows = new ApiFactory(new() { ["Authentication:Mode"] = "Windows" });
        var windowsSchemes = windows.Services.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();
        Assert.IsNotNull(await windowsSchemes.GetSchemeAsync(NegotiateDefaults.AuthenticationScheme));
        Assert.IsNull(await windowsSchemes.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme));

        using var both = new ApiFactory(new()
        {
            ["Authentication:Mode"] = "Both", ["Authentication:Entra:TenantId"] = "00000000-0000-0000-0000-000000000001",
            ["Authentication:Entra:ClientId"] = "00000000-0000-0000-0000-000000000002"
        });
        var bothSchemes = both.Services.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();
        Assert.IsNotNull(await bothSchemes.GetSchemeAsync(NegotiateDefaults.AuthenticationScheme));
        Assert.IsNotNull(await bothSchemes.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme));
        Assert.IsNotNull(await bothSchemes.GetSchemeAsync(AuthenticationSetup.SmartScheme));
    }
}

[TestClass]
public class EmployeeAdminTests
{
    [TestMethod]
    public async Task OnlyAdministrators_ShouldReachTheAdminEndpoints()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");

        var response = await factory.As(@"corp\amra").GetAsync("/api/admin/employees");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual("forbidden", (await response.ReadAsync<ApiErrorDto>())!.Code);
        Assert.AreEqual(HttpStatusCode.OK, (await factory.Admin.GetAsync("/api/admin/employees")).StatusCode);
    }

    [TestMethod]
    public async Task Import_ShouldPreviewBeforeSaving_AndSkipBadRows()
    {
        using var factory = new ApiFactory();
        var csv = "﻿Br.,Ime i prezime\nZ-001,Amra Adžamija\nZ- 255,Pogrešna Šifra\nZ-002,  Dva   Razmaka \nZ-001,Duplikat\n";

        var preview = await (await factory.Admin.PostAsJsonAsync("/api/admin/employees/import/preview", new CsvRequest(csv))).ReadAsync<ImportPreviewDto>();
        Assert.AreEqual(4, preview!.Rows.Count);
        Assert.AreEqual(2, preview.ErrorCount, "the bad code and the duplicate");
        Assert.AreEqual(1, preview.WarningCount, "the extra spaces");
        var all = await factory.Admin.GetFromJsonAsync<List<EmployeeDto>>("/api/admin/employees");
        Assert.AreEqual(1, all!.Count, "a preview saves nothing: only the bootstrap administrator exists");

        var result = await factory.ImportAsync(csv);
        Assert.AreEqual(2, result.Created);
        Assert.AreEqual(2, result.Skipped);
        Assert.AreEqual("Dva Razmaka", (await factory.Admin.GetFromJsonAsync<EmployeeDto>("/api/admin/employees/Z-002"))!.Name);
    }

    [TestMethod]
    public async Task ReimportingTheSameList_ShouldNotCreateAnything()
    {
        using var factory = new ApiFactory();
        Assert.AreEqual(4, (await factory.ImportAsync(ApiFactory.SmallList)).Created);

        var again = await factory.ImportAsync(ApiFactory.SmallList);
        Assert.AreEqual(0, again.Created);
        Assert.AreEqual(0, again.Updated);
    }

    [TestMethod]
    public async Task Import_ShouldRenameAnExistingEmployeeWhenTheNameChanged()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);

        var result = await factory.ImportAsync("Br.,Ime i prezime\nZ-001,Amra Adžamija-Test\n");
        Assert.AreEqual(1, result.Updated);
        Assert.AreEqual("Amra Adžamija-Test", (await factory.Admin.GetFromJsonAsync<EmployeeDto>("/api/admin/employees/Z-001"))!.Name);
    }

    [TestMethod]
    public async Task AccountsCanBeLinkedAndUnlinked_ButNotSharedBetweenEmployees()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");

        var taken = await factory.Admin.PostAsJsonAsync("/api/admin/employees/Z-002/accounts", new AccountRequest(@"CORP\Amra"));
        Assert.AreEqual(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.AreEqual("account-taken", (await taken.ReadAsync<ApiErrorDto>())!.Code);

        Assert.AreEqual(HttpStatusCode.OK, (await factory.Admin.PostAsJsonAsync("/api/admin/employees/Z-001/accounts", new AccountRequest(@"corp\amra"))).StatusCode, "linking the same pair again is harmless");
        Assert.AreEqual(HttpStatusCode.NotFound, (await factory.Admin.PostAsJsonAsync("/api/admin/employees/Z-999/accounts", new AccountRequest("x"))).StatusCode);

        Assert.AreEqual(HttpStatusCode.NoContent, (await factory.Admin.DeleteAsync(@"/api/admin/employees/Z-001/accounts?account=corp\amra")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await factory.As(@"corp\amra").GetAsync("/api/me")).StatusCode);
    }

    [TestMethod]
    public async Task AccountMatching_ShouldProposeLinksByNameIgnoringAccentsAndOrder()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList + "Z-005,Damir Dizdarevic\n");
        await factory.LinkAsync("Z-003", @"corp\emir");

        var users = new List<DirectoryUserDto>
        {
            new(@"corp\amra.a", "Adzamija, Amra"),
            new(@"corp\benjamin.b", "BENJAMIN BAŠIĆ"),
            new(@"corp\damir.d", "Damir Dizdarević"),
            new(@"corp\emir", "Emir Ferhatbegović"),
            new(@"corp\someone", "Nepoznata Osoba"),
        };
        var matches = await (await factory.Admin.PostAsJsonAsync("/api/admin/accounts/match", users)).ReadAsync<List<AccountMatchDto>>();

        Assert.AreEqual("Z-001", matches!.Single(m => m.Account == @"corp\amra.a").EmployeeCode);
        Assert.AreEqual("matched", matches.Single(m => m.Account == @"corp\benjamin.b").Status);
        Assert.AreEqual("ambiguous", matches.Single(m => m.Account == @"corp\damir.d").Status, "Z-004 and Z-005 are the same name without accents");
        Assert.AreEqual("already-linked", matches.Single(m => m.Account == @"corp\emir").Status);
        Assert.AreEqual("unmatched", matches.Single(m => m.Account == @"corp\someone").Status);
    }
}

[TestClass]
public class ChartTests
{
    [TestMethod]
    public async Task TheChainInMe_ShouldFollowTheChartAndStopAtTheFinalApprover()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");

        // Z-001 -> team lead Z-002 -> lawyer Z-003 (final approver) -> finance Z-004
        await factory.UpdateAsync("Z-001", new(null, null, "Z-002", null, null, null, null, null));
        await factory.UpdateAsync("Z-002", new(null, null, "Z-003", null, null, null, null, null));
        await factory.UpdateAsync("Z-003", new(null, null, "Z-004", true, null, null, null, null));

        var me = await factory.As(@"corp\amra").GetFromJsonAsync<MeDto>("/api/me");
        CollectionAssert.AreEqual(new[] { "Z-002", "Z-003" }, me!.ApprovalChain.ToArray());
        Assert.AreEqual("Z-002", me.SupervisorCode);
        Assert.IsFalse(me.IsApprover);

        await factory.LinkAsync("Z-002", @"corp\benjamin");
        var lead = await factory.As(@"corp\benjamin").GetFromJsonAsync<MeDto>("/api/me");
        Assert.IsTrue(lead!.IsApprover, "someone with people reporting to them approves");
        CollectionAssert.AreEqual(new[] { "Z-003" }, lead.ApprovalChain.ToArray());
    }

    [TestMethod]
    public async Task ACycleOrAnUnknownSupervisor_ShouldBeRefused()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.UpdateAsync("Z-001", new(null, null, "Z-002", null, null, null, null, null));
        await factory.UpdateAsync("Z-002", new(null, null, "Z-003", null, null, null, null, null));

        var cycle = await factory.UpdateAsync("Z-003", new(null, null, "Z-001", null, null, null, null, null));
        Assert.AreEqual(HttpStatusCode.Conflict, cycle.StatusCode);
        Assert.AreEqual("chart-cycle", (await cycle.ReadAsync<ApiErrorDto>())!.Code);

        var self = await factory.UpdateAsync("Z-001", new(null, null, "Z-001", null, null, null, null, null));
        Assert.AreEqual(HttpStatusCode.Conflict, self.StatusCode, "nobody is their own supervisor");

        var unknown = await factory.UpdateAsync("Z-001", new(null, null, "Z-999", null, null, null, null, null));
        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);

        var stored = await factory.Admin.GetFromJsonAsync<EmployeeDto>("/api/admin/employees/Z-003");
        Assert.IsNull(stored!.SupervisorCode, "a refused change saves nothing");
    }

    [TestMethod]
    public async Task ASupervisorCanBeClearedWithAnEmptyString()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.UpdateAsync("Z-001", new(null, null, "Z-002", null, null, null, null, null));
        await factory.UpdateAsync("Z-001", new(null, null, "", null, null, null, null, null));

        Assert.IsNull((await factory.Admin.GetFromJsonAsync<EmployeeDto>("/api/admin/employees/Z-001"))!.SupervisorCode);
    }
}

[TestClass]
public class ReferenceDataTests
{
    [TestMethod]
    public async Task TimeCodes_ShouldBeServedFromTheDomainCatalogue()
    {
        using var factory = new ApiFactory();
        var codes = await factory.Admin.GetFromJsonAsync<List<TimeCodeDto>>("/api/timecodes");

        Assert.AreEqual(22, codes!.Count);
        Assert.IsTrue(codes.Single(c => c.Code == 1022).Unit == "Day");
        Assert.IsTrue(codes.Single(c => c.Code == 2023).IsOvertime);
    }

    [TestMethod]
    public async Task Holidays_ShouldBeMaintainedByTheAdministratorAndShowInTheCalendar()
    {
        using var factory = new ApiFactory();
        var add = await factory.Admin.PostAsJsonAsync("/api/admin/holidays", new HolidayDto(new DateOnly(2026, 9, 9), "Test praznik"));
        Assert.AreEqual(HttpStatusCode.OK, add.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await factory.Admin.PostAsJsonAsync("/api/admin/holidays", new HolidayDto(new DateOnly(2026, 9, 10), " "))).StatusCode);

        var list = await factory.Admin.GetFromJsonAsync<List<HolidayDto>>("/api/admin/holidays/2026");
        Assert.AreEqual(1, list!.Count);

        var days = await factory.Admin.GetFromJsonAsync<List<CalendarDayDto>>("/api/calendar/2026/9");
        Assert.AreEqual(30, days!.Count);
        Assert.AreEqual(DayKind.Holiday, days.Single(d => d.Date.Day == 9).Type);
        Assert.AreEqual("Test praznik", days.Single(d => d.Date.Day == 9).HolidayName);
        Assert.AreEqual(DayKind.Weekend, days.Single(d => d.Date.Day == 5).Type);
        Assert.AreEqual("N", days.Single(d => d.Date.Day == 27).Weekday, "27 September 2026 is a Sunday");
        Assert.AreEqual(22, days.Count(d => d.Type == DayKind.Working) + 1, "21 working days and the holiday make 22 weekdays");

        Assert.AreEqual(HttpStatusCode.NoContent, (await factory.Admin.DeleteAsync("/api/admin/holidays/2026-09-09")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await factory.Admin.DeleteAsync("/api/admin/holidays/2026-09-09")).StatusCode);
    }

    [TestMethod]
    public async Task ReligiousHolidays_ShouldBePerEmployee()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");
        await factory.LinkAsync("Z-002", @"corp\benjamin");

        var set = await factory.Admin.PutAsJsonAsync("/api/admin/employees/Z-001/religious-holidays",
            new List<HolidayDto> { new(new DateOnly(2026, 9, 16), "Vjerski praznik") });
        Assert.AreEqual(HttpStatusCode.OK, set.StatusCode);

        var mine = await factory.As(@"corp\amra").GetFromJsonAsync<List<CalendarDayDto>>("/api/calendar/2026/9");
        var theirs = await factory.As(@"corp\benjamin").GetFromJsonAsync<List<CalendarDayDto>>("/api/calendar/2026/9");
        Assert.AreEqual(DayKind.Holiday, mine!.Single(d => d.Date.Day == 16).Type);
        Assert.AreEqual(DayKind.Working, theirs!.Single(d => d.Date.Day == 16).Type, "a colleague works that day");

        Assert.AreEqual(HttpStatusCode.NotFound, (await factory.Admin.PutAsJsonAsync("/api/admin/employees/Z-999/religious-holidays", new List<HolidayDto>())).StatusCode);
    }

    [TestMethod]
    public async Task InvalidMonths_ShouldBeRefused()
    {
        using var factory = new ApiFactory();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await factory.Admin.GetAsync("/api/calendar/2026/13")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await factory.Admin.GetAsync("/api/calendar/1999/1")).StatusCode);
    }
}

[TestClass]
public class AuditTests
{
    [TestMethod]
    public async Task EveryAdminChange_ShouldBeRecordedWithWhoDidIt()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.UpdateAsync("Z-001", new(null, null, "Z-002", null, null, null, null, null));
        await factory.Admin.PostAsJsonAsync("/api/admin/holidays", new HolidayDto(new DateOnly(2026, 9, 9), "Praznik"));

        var events = await factory.Admin.GetFromJsonAsync<List<AuditEventDto>>("/api/admin/audit?take=50");
        var actions = events!.Select(e => e.Action).ToList();
        CollectionAssert.IsSubsetOf(new[] { "AdminCreated", "EmployeeCreated", "EmployeesImported", "EmployeeUpdated", "HolidayAdded" }, actions);
        Assert.AreEqual("Z-000", events.First(e => e.Action == "EmployeeUpdated").Actor, "the administrator's employee code");
        Assert.AreEqual("bootstrap", events.Single(e => e.Action == "AdminCreated").Actor);
        Assert.IsTrue(events.First(e => e.Action == "EmployeeUpdated").Details!.Contains("supervisor=Z-002"));
        Assert.IsTrue(events.Select(e => e.Id).SequenceEqual(events.Select(e => e.Id).OrderByDescending(i => i)), "newest first");
    }

    [TestMethod]
    public async Task TheAuditLog_ShouldBeAppendOnly()
    {
        using var factory = new ApiFactory();
        await factory.Admin.GetAsync("/api/me"); // starts the host and creates the first audit event

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanyDbContext>();

        var first = db.AuditEvents.First();
        first.Action = "Tampered";
        Assert.ThrowsException<InvalidOperationException>(() => db.SaveChanges());

        db.ChangeTracker.Clear();
        db.AuditEvents.Remove(db.AuditEvents.First());
        Assert.ThrowsException<InvalidOperationException>(() => db.SaveChanges());
    }

    [TestMethod]
    public async Task TheAuditEndpoint_ShouldBeForAdministratorsOnly()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");
        Assert.AreEqual(HttpStatusCode.Forbidden, (await factory.As(@"corp\amra").GetAsync("/api/admin/audit")).StatusCode);
    }
}
