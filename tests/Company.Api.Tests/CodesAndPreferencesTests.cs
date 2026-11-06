using System.Net;
using System.Net.Http.Json;
using Company.Api.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Company.Api.Tests;

/// <summary>The codes of the legend kept by the administrator, and the look of the program kept per person</summary>
[TestClass]
public class CodesAndPreferencesTests
{
    [TestCleanup]
    public void ForgetTheCatalogue()
    {
        TimeCodes.Reset();
        TimeCodeNames.Reset();
    }

    [TestMethod]
    public async Task TheLegendIsSeededIntoTheDatabase_AsSystemCodes()
    {
        using var factory = new ApiFactory();

        var codes = await factory.Admin.GetFromJsonAsync<List<CodeDefinitionDto>>("/api/admin/codes");

        Assert.AreEqual(22, codes!.Count);
        Assert.IsTrue(codes.All(c => c.IsSystem && c.IsActive));
        Assert.AreEqual("Redovan rad", codes.Single(c => c.Code == 1010).NameBs);
    }

    [TestMethod]
    public async Task ACodeTheAdministratorAdds_IsOfferedToEveryoneAndCanBeEntered()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");

        var add = await factory.Admin.PostAsJsonAsync("/api/admin/codes", new SaveCodeRequest(1050, "Edukacija", "", "Green", "Day", true));
        Assert.AreEqual(HttpStatusCode.OK, add.StatusCode);
        var added = await add.Content.ReadFromJsonAsync<CodeDefinitionDto>();
        Assert.IsFalse(added!.IsSystem);
        Assert.AreEqual("Edukacija", added.NameEn, "an empty English name falls back to the Bosnian one");

        var amra = factory.As(@"corp\amra");
        var codes = await amra.GetFromJsonAsync<List<TimeCodeDto>>("/api/timecodes");
        Assert.AreEqual(23, codes!.Count);
        var offered = codes.Single(c => c.Code == 1050);
        Assert.AreEqual("Day", offered.Unit);
        Assert.IsTrue(offered.IsActive);

        // The rules use the new code at once, with no new version of the program
        var apply = await amra.PostAsJsonAsync("/api/timesheet/2026/9/absences",
            new AbsenceRangeRequest(1050, new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 15)));
        Assert.AreEqual(HttpStatusCode.OK, apply.StatusCode);
    }

    [TestMethod]
    public async Task ACodeThatExists_OrIsOutOfRange_OrIsOvertime_IsRefused()
    {
        using var factory = new ApiFactory();

        Assert.AreEqual(HttpStatusCode.Conflict, (await factory.Admin.PostAsJsonAsync("/api/admin/codes", new SaveCodeRequest(1010, "Opet", "", "Green", "Hours", true))).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await factory.Admin.PostAsJsonAsync("/api/admin/codes", new SaveCodeRequest(12, "Premalo", "", "Green", "Hours", true))).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await factory.Admin.PostAsJsonAsync("/api/admin/codes", new SaveCodeRequest(1051, " ", "", "Green", "Hours", true))).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await factory.Admin.PostAsJsonAsync("/api/admin/codes", new SaveCodeRequest(1051, "Fantazija", "", "Purple", "Hours", true))).StatusCode);

        var overtime = await factory.Admin.PostAsJsonAsync("/api/admin/codes", new SaveCodeRequest(1051, "Moj prekovremeni", "", "Red", "Hours", true));
        Assert.AreEqual(HttpStatusCode.BadRequest, overtime.StatusCode, "overtime is worked out from the intervals, not typed in");
    }

    [TestMethod]
    public async Task ASystemCode_CanBeRenamed_ButNotSwitchedOffOrChanged()
    {
        using var factory = new ApiFactory();

        var rename = await factory.Admin.PutAsJsonAsync("/api/admin/codes/1010", new SaveCodeRequest(1010, "Redovno radno vrijeme", "", "Green", "Hours", true));
        Assert.AreEqual(HttpStatusCode.OK, rename.StatusCode);
        Assert.AreEqual("Redovno radno vrijeme", TimeCodeNames.Bs(1010), "the new name is in use at once");

        Assert.AreEqual(HttpStatusCode.BadRequest, (await factory.Admin.PutAsJsonAsync("/api/admin/codes/1010", new SaveCodeRequest(1010, "Redovan rad", "", "Green", "Hours", false))).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await factory.Admin.PutAsJsonAsync("/api/admin/codes/1010", new SaveCodeRequest(1010, "Redovan rad", "", "Orange", "Hours", true))).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await factory.Admin.PutAsJsonAsync("/api/admin/codes/1999", new SaveCodeRequest(1999, "Nema", "", "Green", "Hours", true))).StatusCode);
    }

    [TestMethod]
    public async Task ACodeThatIsSwitchedOff_CannotBeEnteredAnyMore_ButItsMeaningIsFrozenOnceUsed()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");
        await factory.Admin.PostAsJsonAsync("/api/admin/codes", new SaveCodeRequest(1050, "Edukacija", "", "Green", "Day", true));
        var amra = factory.As(@"corp\amra");
        await amra.PostAsJsonAsync("/api/timesheet/2026/9/absences", new AbsenceRangeRequest(1050, new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 14)));

        // Sheets use it now: the colour cannot change any more, but the name can and it can be switched off
        Assert.AreEqual(HttpStatusCode.BadRequest, (await factory.Admin.PutAsJsonAsync("/api/admin/codes/1050", new SaveCodeRequest(1050, "Edukacija", "", "Orange", "Day", true))).StatusCode);
        var off = await factory.Admin.PutAsJsonAsync("/api/admin/codes/1050", new SaveCodeRequest(1050, "Edukacija (stara)", "", "Green", "Day", false));
        Assert.AreEqual(HttpStatusCode.OK, off.StatusCode);

        var again = await amra.PostAsJsonAsync("/api/timesheet/2026/9/absences", new AbsenceRangeRequest(1050, new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 21)));
        Assert.AreEqual(HttpStatusCode.BadRequest, again.StatusCode, "a switched-off code is no longer accepted for new entries");

        var sheet = await amra.GetFromJsonAsync<SheetDto>("/api/timesheet/2026/9");
        Assert.IsTrue(sheet!.Days.SelectMany(d => d.Entries).Any(e => e.Code == 1050), "the old entry still reads");
    }

    [TestMethod]
    public async Task OnlyTheAdministrator_MayChangeTheCodes()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");
        var amra = factory.As(@"corp\amra");

        Assert.AreEqual(HttpStatusCode.Forbidden, (await amra.PostAsJsonAsync("/api/admin/codes", new SaveCodeRequest(1050, "Edukacija", "", "Green", "Day", true))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await amra.GetAsync("/api/admin/codes")).StatusCode);
    }

    [TestMethod]
    public async Task TheChoiceOfThemeAndPalette_IsKeptOnTheAccount_AndComesBackWithMe()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");
        var amra = factory.As(@"corp\amra");

        var first = await amra.GetFromJsonAsync<MeDto>("/api/me");
        Assert.IsNull(first!.ThemeMode);
        Assert.IsNull(first.Palette);

        Assert.AreEqual(HttpStatusCode.NoContent, (await amra.PutAsJsonAsync("/api/me/preferences", new PreferencesRequest("dark", "logosoft"))).StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, (await amra.PutAsJsonAsync("/api/me/preferences", new PreferencesRequest(null, "blue"))).StatusCode);

        var me = await amra.GetFromJsonAsync<MeDto>("/api/me");
        Assert.AreEqual("dark", me!.ThemeMode, "an unchanged part stays as it was");
        Assert.AreEqual("blue", me.Palette);

        // Another person is not affected
        await factory.LinkAsync("Z-002", @"corp\benjamin");
        var other = await factory.As(@"corp\benjamin").GetFromJsonAsync<MeDto>("/api/me");
        Assert.IsNull(other!.ThemeMode);
    }

    [TestMethod]
    public async Task AThemeOrPaletteThatDoesNotExist_IsRefused()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");
        var amra = factory.As(@"corp\amra");

        Assert.AreEqual(HttpStatusCode.BadRequest, (await amra.PutAsJsonAsync("/api/me/preferences", new PreferencesRequest("neon", null))).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await amra.PutAsJsonAsync("/api/me/preferences", new PreferencesRequest(null, "pink"))).StatusCode);
    }

    [TestMethod]
    public async Task TheLanguageIsKeptOnTheAccount_AndAnUnknownOneIsRefused()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corpmra");
        var amra = factory.As(@"corpmra");

        Assert.IsNull((await amra.GetFromJsonAsync<MeDto>("/api/me"))!.Language);
        Assert.AreEqual(HttpStatusCode.NoContent, (await amra.PutAsJsonAsync("/api/me/preferences", new PreferencesRequest(null, null, "en"))).StatusCode);
        Assert.AreEqual("en", (await amra.GetFromJsonAsync<MeDto>("/api/me"))!.Language);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await amra.PutAsJsonAsync("/api/me/preferences", new PreferencesRequest(null, null, "de"))).StatusCode);
    }

    [TestMethod]
    public async Task TheFindingsOfTheRules_AreWrittenInEnglishWhenTheProgramAsksForIt()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corpmra");
        var amra = factory.As(@"corpmra");

        // 7.5 hours on a working day is not the daily norm
        await amra.PutAsJsonAsync("/api/timesheet/2026/9/days/2026-09-14", new SetEntryRequest(1010, 450));
        var bosnian = await amra.GetFromJsonAsync<SheetDto>("/api/timesheet/2026/9");
        Assert.IsTrue(bosnian!.Issues.Any(i => i.Message.Contains("razlikuju se od dnevne norme")));

        amra.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        var english = await amra.GetFromJsonAsync<SheetDto>("/api/timesheet/2026/9");
        Assert.IsTrue(english!.Issues.Any(i => i.Message.Contains("differ from the daily norm")));
        Assert.IsFalse(english.Issues.Any(i => i.Message.Contains("razlikuju se")));
    }

    [TestMethod]
    public void TheRequestLanguage_IsEnglishOnlyWhenAsked_AndTheMailsFollowThePerson()
    {
        Assert.AreEqual("en", Company.Api.Texts.RequestLanguage.From("en-GB,en;q=0.9"));
        Assert.AreEqual("en", Company.Api.Texts.RequestLanguage.From("EN"));
        Assert.AreEqual("bs", Company.Api.Texts.RequestLanguage.From("bs-Latn-BA"));
        Assert.AreEqual("bs", Company.Api.Texts.RequestLanguage.From(null));

        var item = new ReminderDto(ReminderType.SubmitOverdue, 2026, 9, 1, new DateOnly(2026, 9, 25));
        Assert.AreEqual("Your timesheet is late", Company.Api.Texts.ReminderTexts.Title(item, "en"));
        Assert.AreEqual("Šihtarica kasni", Company.Api.Texts.ReminderTexts.Title(item));
        StringAssert.Contains(Company.Api.Texts.ReminderTexts.Line(item, "en"), "September 2026");
        StringAssert.Contains(Company.Api.Texts.ReminderTexts.Body("Amra", new[] { item }, "en"), "Dear Amra");
    }

    [TestMethod]
    public async Task TheOverviewOfTheAdministrator_CountsPeopleCodesHolidaysAndSheets()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corpmra");

        var first = await factory.Admin.GetFromJsonAsync<AdminOverviewDto>("/api/admin/overview");
        Assert.AreEqual(5, first!.People.Total, "the administrator and the four people of the list");
        Assert.AreEqual(5, first.People.Active);
        Assert.AreEqual(1, first.People.Administrators);
        Assert.AreEqual(22, first.Codes.Total);
        Assert.AreEqual(12, first.Codes.Green);
        Assert.AreEqual(6, first.Codes.Red);
        Assert.AreEqual(0, first.Holidays.ThisYear, "no holidays entered: the overview says so");
        Assert.AreEqual(4, first.ThisMonth.Expected, "the four employees have a timesheet this month; the administrator has none");
        Assert.AreEqual(4, first.ThisMonth.NotStarted);
        Assert.IsTrue(first.Audit.Events > 0);
        Assert.IsTrue(first.Audit.Recent.Count > 0);

        var year = DateTime.Today.Year;
        await factory.Admin.PostAsJsonAsync("/api/admin/holidays", new HolidayDto(new DateOnly(year, 5, 1), "Praznik rada"));
        await factory.Admin.PostAsJsonAsync("/api/admin/codes", new SaveCodeRequest(1050, "Edukacija", "", "Green", "Day", true));
        var today = DateTime.Today;
        await factory.As(@"corpmra").GetFromJsonAsync<SheetDto>($"/api/timesheet/{today.Year}/{today.Month}");

        var second = await factory.Admin.GetFromJsonAsync<AdminOverviewDto>("/api/admin/overview");
        Assert.AreEqual(1, second!.Holidays.ThisYear);
        Assert.AreEqual(23, second.Codes.Total);
        Assert.AreEqual(3, second.ThisMonth.NotStarted, "Amra opened her month");
        Assert.AreEqual(1, second.ThisMonth.Draft);
    }

    [TestMethod]
    public async Task TheOverview_IsOnlyForTheAdministrator()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corpmra");

        Assert.AreEqual(HttpStatusCode.Forbidden, (await factory.As(@"corpmra").GetAsync("/api/admin/overview")).StatusCode);
    }

    [TestMethod]
    public async Task TheFixedStateHolidays_AreWrittenForThisAndNextYear_AndTheAdministratorsChangesAreKept()
    {
        using var factory = new ApiFactory(new Dictionary<string, string?> { ["Holidays:SeedFixed"] = "true" });
        var year = DateTime.Today.Year;

        var thisYear = await factory.Admin.GetFromJsonAsync<List<HolidayDto>>($"/api/admin/holidays/{year}");
        var nextYear = await factory.Admin.GetFromJsonAsync<List<HolidayDto>>($"/api/admin/holidays/{year + 1}");
        Assert.AreEqual(6, thisYear!.Count);
        Assert.AreEqual(6, nextYear!.Count);
        CollectionAssert.AreEqual(new[] { new DateOnly(year, 1, 1), new DateOnly(year, 1, 2), new DateOnly(year, 3, 1), new DateOnly(year, 5, 1), new DateOnly(year, 5, 2), new DateOnly(year, 11, 25) },
            thisYear.Select(h => h.Date).ToArray());
        Assert.AreEqual("Praznik rada", thisYear.Single(h => h.Date.Month == 5 && h.Date.Day == 1).Name);

        // The administrator removes one and renames another: a later look at the overview does not write them again
        await factory.Admin.DeleteAsync($"/api/admin/holidays/{year}-03-01");
        await factory.Admin.PostAsJsonAsync("/api/admin/holidays", new HolidayDto(new DateOnly(year, 11, 25), "Dan državnosti"));
        var overview = await factory.Admin.GetFromJsonAsync<AdminOverviewDto>("/api/admin/overview");
        Assert.AreEqual(5, overview!.Holidays.ThisYear);
        var after = await factory.Admin.GetFromJsonAsync<List<HolidayDto>>($"/api/admin/holidays/{year}");
        Assert.IsFalse(after!.Any(h => h.Date == new DateOnly(year, 3, 1)));
        Assert.AreEqual("Dan državnosti", after.Single(h => h.Date.Month == 11).Name);

        var events = await factory.Admin.GetFromJsonAsync<List<AuditEventDto>>("/api/admin/audit?take=50");
        Assert.AreEqual(2, events!.Count(e => e.Action == "HolidaysSeeded"), "this year and the next one, written by the system");
        Assert.AreEqual("system", events.First(e => e.Action == "HolidaysSeeded").Actor);
    }

    [TestMethod]
    public async Task AYearThatTheAdministratorFilledIn_IsNotSeeded_AndTheSeedingCanBeSwitchedOff()
    {
        using var off = new ApiFactory();
        Assert.AreEqual(0, (await off.Admin.GetFromJsonAsync<List<HolidayDto>>($"/api/admin/holidays/{DateTime.Today.Year}"))!.Count, "switched off: nothing is written");

        using var on = new ApiFactory(new Dictionary<string, string?> { ["Holidays:SeedFixed"] = "true" });
        var next = DateTime.Today.Year + 1;
        // Written by the system at start; the administrator's own list for another year is not touched
        var far = await on.Admin.GetFromJsonAsync<List<HolidayDto>>($"/api/admin/holidays/{next + 5}");
        Assert.AreEqual(0, far!.Count, "only this year and the next one are written");
    }

    [TestMethod]
    public async Task SeveralEventsInOneSave_KeepTheAuditChainIntact()
    {
        // Seeding writes two events in one save, and the import of a list writes one for every person and one for the import
        using var factory = new ApiFactory(new Dictionary<string, string?> { ["Holidays:SeedFixed"] = "true" });
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.Admin.PostAsJsonAsync("/api/admin/holidays", new HolidayDto(new DateOnly(DateTime.Today.Year, 12, 31), "Test"));

        var verification = await factory.Admin.GetFromJsonAsync<AuditVerificationDto>("/api/admin/audit/verify");
        Assert.IsTrue(verification!.IsIntact, verification.Problem);
        Assert.IsTrue(verification.Checked >= 8, "the events of the seeding, the import and the holiday");

        var events = (await factory.Admin.GetFromJsonAsync<List<AuditEventDto>>("/api/admin/audit?take=100"))!.OrderBy(e => e.Id).ToList();
        CollectionAssert.AreEqual(new[] { "AdminCreated", "HolidaysSeeded", "HolidaysSeeded" }, events.Take(3).Select(e => e.Action).ToArray());
    }
}
