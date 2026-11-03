using System.Net;
using System.Net.Http.Json;
using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Company.Api.Tests;

[TestClass]
public class TimesheetTests
{
    private const string Url = "/api/timesheet/2026/9";

    private static async Task<(ApiFactory Factory, HttpClient Amra)> StartAsync()
    {
        var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");
        await factory.LinkAsync("Z-002", @"corp\benjamin");
        return (factory, factory.As(@"corp\amra"));
    }

    private static async Task<SheetDto> Read(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<SheetDto>())!;
    }

    [TestMethod]
    public async Task ANewMonth_ShouldBeADraftWithTheStandardDaysAlreadyFilledIn()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            var sheet = await amra.GetFromJsonAsync<SheetDto>(Url);

            Assert.AreEqual(SheetStatus.Draft, sheet!.Status);
            Assert.IsTrue(sheet.Editable);
            Assert.AreEqual(30, sheet.Days.Count);
            Assert.AreEqual("U", sheet.Days[0].Weekday, "1 September 2026 is a Tuesday");
            Assert.AreEqual(DayKind.Weekend, sheet.Days.Single(d => d.Date.Day == 5).Type);
            Assert.AreEqual(176 * 60, sheet.Summary.ExpectedFundMinutes);
            Assert.AreEqual(176 * 60, sheet.Summary.FundMinutes, "everybody worked the standard day unless they say otherwise");
            Assert.AreEqual(0, sheet.Issues.Count);
            Assert.AreEqual(22, sheet.Days.Count(d => d.Entries.Any(e => e.Code == Codes.RegularWork && e.Minutes == 480)));
            Assert.IsTrue(sheet.Days.Where(d => d.Type != DayKind.Working).All(d => d.Entries.Count == 0), "weekends and holidays stay empty");
        }
    }

    [TestMethod]
    public async Task FillingTheStandardDays_ShouldGive176HoursAndNoFindings()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            var sheet = await Read(await amra.PostAsync(Url + "/fill-standard-days", null));

            Assert.AreEqual(176 * 60, sheet.Summary.FundMinutes);
            Assert.AreEqual(22, sheet.Days.Count(d => d.Entries.Any(e => e.Code == Codes.RegularWork && e.Minutes == 480)));
            Assert.AreEqual(0, sheet.Issues.Count);
            Assert.AreEqual(0, sheet.Days.Single(d => d.Date.Day == 5).Entries.Count, "weekends stay empty");

            var again = await Read(await amra.PostAsync(Url + "/fill-standard-days", null));
            Assert.AreEqual(176 * 60, again.Summary.FundMinutes, "a second fill changes nothing");
        }
    }

    [TestMethod]
    public async Task FillingTheStandardDays_ShouldSkipHolidaysAndDaysBeforeEmployment()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            await factory.Admin.PostAsJsonAsync("/api/admin/holidays", new HolidayDto(new DateOnly(2026, 9, 9), "Praznik"));
            await factory.Admin.PutAsJsonAsync("/api/admin/employees/Z-001/religious-holidays", new List<HolidayDto> { new(new DateOnly(2026, 9, 16), "Vjerski") });
            await factory.UpdateAsync("Z-001", new(null, null, null, null, null, null, new DateOnly(2026, 9, 3), null));

            var sheet = await Read(await amra.PostAsync(Url + "/fill-standard-days", null));
            Assert.AreEqual(0, sheet.Days.Single(d => d.Date.Day == 9).Entries.Count, "a state holiday uses 1024 and 2010");
            Assert.AreEqual(0, sheet.Days.Single(d => d.Date.Day == 16).Entries.Count, "the employee's religious holiday uses 1025");
            Assert.AreEqual(0, sheet.Days.Single(d => d.Date.Day == 2).Entries.Count, "before the employment started");
            Assert.AreEqual(1, sheet.Days.Single(d => d.Date.Day == 3).Entries.Count);
        }
    }

    [TestMethod]
    public async Task SettingAnEntry_ShouldReplaceTheBaseEntryOfTheDay()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            await Read(await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(Codes.RegularWork, 480)));
            var sheet = await Read(await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(Codes.AnnualLeaveA, 999)));

            var day = sheet.Days[0];
            Assert.AreEqual(1, day.Entries.Count, "one base entry per day");
            Assert.AreEqual(Codes.AnnualLeaveA, day.Entries[0].Code);
            Assert.AreEqual(0, day.Entries[0].Minutes, "a whole-day code carries no minutes");
        }
    }

    [TestMethod]
    public async Task HolidayHours_ShouldBeKeptAlongsideEachOtherAndAddUpToTheNorm()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            await factory.Admin.PostAsJsonAsync("/api/admin/holidays", new HolidayDto(new DateOnly(2026, 9, 9), "Praznik"));
            await Read(await amra.PutAsJsonAsync(Url + "/days/2026-09-09", new SetEntryRequest(Codes.HolidayWork, 300)));
            var sheet = await Read(await amra.PutAsJsonAsync(Url + "/days/2026-09-09", new SetEntryRequest(Codes.StateHoliday, 120)));

            Assert.AreEqual(2, sheet.Days.Single(d => d.Date.Day == 9).Entries.Count);
            Assert.IsTrue(sheet.Issues.Any(i => i.RuleId == "V-20"), "5 h worked + 2 h holiday is not 8 h");

            sheet = await Read(await amra.PutAsJsonAsync(Url + "/days/2026-09-09", new SetEntryRequest(Codes.StateHoliday, 180)));
            Assert.IsFalse(sheet.Issues.Any(i => i.RuleId == "V-20"), "5 h + 3 h = 8 h");
        }
    }

    [TestMethod]
    public async Task InvalidEntries_ShouldBeRefusedWithAClearCode()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            async Task<string> CodeOf(HttpResponseMessage r) { Assert.AreEqual(HttpStatusCode.BadRequest, r.StatusCode); return (await r.ReadAsync<ApiErrorDto>())!.Code; }

            Assert.AreEqual("unknown-code", await CodeOf(await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(9999, 480))));
            Assert.AreEqual("overtime-derived", await CodeOf(await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(Codes.Overtime, 120))));
            Assert.AreEqual("invalid-hours", await CodeOf(await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(Codes.RegularWork, 0))));
            Assert.AreEqual("invalid-hours", await CodeOf(await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(Codes.RegularWork, 1500))));
            Assert.AreEqual("outside-month", await CodeOf(await amra.PutAsJsonAsync(Url + "/days/2026-10-01", new SetEntryRequest(Codes.RegularWork, 480))));
            Assert.AreEqual("invalid-month", await CodeOf(await amra.GetAsync("/api/timesheet/2026/13")));
        }
    }

    [TestMethod]
    public async Task WeekendEntries_ShouldBeAcceptedButReportedAsBlocking()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            var sheet = await Read(await amra.PutAsJsonAsync(Url + "/days/2026-09-05", new SetEntryRequest(Codes.AnnualLeaveA, 0)));
            var issue = sheet.Issues.Single(i => i.RuleId == "V-02");
            Assert.AreEqual(IssueSeverity.Blocking, issue.Severity);
            Assert.AreEqual(new DateOnly(2026, 9, 5), issue.Date);
        }
    }

    [TestMethod]
    public async Task ClearingAnEntry_ShouldRemoveItAndRefuseAMissingOne()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            await Read(await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(Codes.AnnualLeaveA, 0)));
            var sheet = await Read(await amra.DeleteAsync(Url + "/days/2026-09-01/1022"));
            CollectionAssert.AreEqual(new[] { Codes.RegularWork }, sheet.Days[0].Entries.Select(e => e.Code).ToArray(), "without the leave the day is a standard day again");
            Assert.AreEqual(HttpStatusCode.NotFound, (await amra.DeleteAsync(Url + "/days/2026-09-01/1022")).StatusCode);
        }
    }

    // ---- Absence ranges ------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task AnAbsenceRange_ShouldBePreviewedThenApplied_SkippingWeekends()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            await Read(await amra.PostAsync(Url + "/fill-standard-days", null));
            var request = new AbsenceRangeRequest(Codes.AnnualLeaveA, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15));

            var preview = await (await amra.PostAsJsonAsync(Url + "/absences/preview", request)).ReadAsync<AbsencePreviewDto>();
            Assert.AreEqual(11, preview!.Days.Count);
            CollectionAssert.AreEqual(new[] { 5, 6, 12, 13 }, preview.Skipped.Select(s => s.Date.Day).ToArray());
            Assert.IsTrue(preview.Skipped.All(s => s.Reason == SkippedBecause.Weekend));

            var before = await amra.GetFromJsonAsync<SheetDto>(Url);
            Assert.AreEqual(0, before!.Summary.DaysByCode.GetValueOrDefault(Codes.AnnualLeaveA), "a preview saves nothing");

            var sheet = await Read(await amra.PostAsJsonAsync(Url + "/absences", request));
            Assert.AreEqual(11, sheet.Summary.DaysByCode[Codes.AnnualLeaveA]);
            Assert.AreEqual(88 * 60, sheet.Summary.MinutesByCode[Codes.RegularWork], "the days before were replaced, not doubled");
            Assert.AreEqual(176 * 60, sheet.Summary.FundMinutes);
            Assert.AreEqual(0, sheet.Issues.Count);
        }
    }

    [TestMethod]
    public async Task AnAbsenceRange_ShouldStayInsideTheMonthAndUseAbsenceCodes()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            var outside = await amra.PostAsJsonAsync(Url + "/absences/preview", new AbsenceRangeRequest(Codes.AnnualLeaveA, new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2)));
            Assert.AreEqual("outside-month", (await outside.ReadAsync<ApiErrorDto>())!.Code);

            var notAbsence = await amra.PostAsJsonAsync(Url + "/absences", new AbsenceRangeRequest(Codes.RegularWork, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2)));
            Assert.AreEqual("invalid-absence", (await notAbsence.ReadAsync<ApiErrorDto>())!.Code);
        }
    }

    // ---- Overtime ------------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task Overtime_ShouldBeSplitIntoCodesAndFlowIntoTheDaysAndTotals()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            var sheet = await Read(await amra.PostAsJsonAsync(Url + "/overtime",
                new OvertimeRequest(new DateOnly(2026, 9, 7), new TimeOnly(20, 0), new TimeOnly(22, 30), "Hitna ispravka")));

            var record = sheet.Overtime.Single();
            Assert.AreEqual(150, record.TotalMinutes);
            Assert.AreEqual(120, record.Allocations.Single(a => a.Code == Codes.Overtime).Minutes);
            Assert.AreEqual(30, record.Allocations.Single(a => a.Code == Codes.NightOvertime).Minutes);
            Assert.AreEqual(2, sheet.Days.Single(d => d.Date.Day == 7).OvertimeAllocations.Count);
            Assert.AreEqual(150, sheet.Summary.OvertimeMinutes);
            Assert.AreEqual(120, sheet.Summary.MinutesByCode[Codes.Overtime]);
            Assert.IsFalse(sheet.Issues.Any(i => i.RuleId is "V-03" or "V-13"));
        }
    }

    [TestMethod]
    public async Task OvertimeInsideTheRegularWindow_ShouldBeAcceptedButFlagged()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            var sheet = await Read(await amra.PostAsJsonAsync(Url + "/overtime",
                new OvertimeRequest(new DateOnly(2026, 9, 7), new TimeOnly(10, 0), new TimeOnly(12, 0), "Pogrešno")));

            Assert.AreEqual(120, sheet.Overtime.Single().TotalMinutes, "the total is computed, never typed");
            Assert.IsTrue(sheet.Overtime.Single().HasRegularWindowConflict);
            Assert.AreEqual(IssueSeverity.Blocking, sheet.Issues.Single(i => i.RuleId == "V-03").Severity);
        }
    }

    [TestMethod]
    public async Task OvertimeWithoutADescription_ShouldBeFlagged_AndEqualTimesRefused()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            var sheet = await Read(await amra.PostAsJsonAsync(Url + "/overtime", new OvertimeRequest(new DateOnly(2026, 9, 7), new TimeOnly(18, 0), new TimeOnly(20, 0), "  ")));
            Assert.AreEqual(IssueSeverity.Blocking, sheet.Issues.Single(i => i.RuleId == "V-13").Severity);

            var equal = await amra.PostAsJsonAsync(Url + "/overtime", new OvertimeRequest(new DateOnly(2026, 9, 7), new TimeOnly(18, 0), new TimeOnly(18, 0), "x"));
            Assert.AreEqual("invalid-interval", (await equal.ReadAsync<ApiErrorDto>())!.Code);
        }
    }

    [TestMethod]
    public async Task OvertimeAcrossTheEndOfTheMonth_ShouldKeepTheNextMonthsPartOutOfThisMonthsTotals()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            var sheet = await Read(await amra.PostAsJsonAsync(Url + "/overtime",
                new OvertimeRequest(new DateOnly(2026, 9, 30), new TimeOnly(23, 0), new TimeOnly(1, 0), "Noćno dežurstvo")));

            Assert.AreEqual(120, sheet.Overtime.Single().TotalMinutes);
            Assert.AreEqual(2, sheet.Overtime.Single().Allocations.Count, "the record shows both dates");
            Assert.AreEqual(60, sheet.Summary.OvertimeMinutes, "only 23:00 to 24:00 belongs to September");
        }
    }

    [TestMethod]
    public async Task OvertimeCanBeChangedAndDeleted()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            var sheet = await Read(await amra.PostAsJsonAsync(Url + "/overtime", new OvertimeRequest(new DateOnly(2026, 9, 7), new TimeOnly(18, 0), new TimeOnly(19, 0), "A")));
            var id = sheet.Overtime.Single().Id;

            sheet = await Read(await amra.PutAsJsonAsync($"{Url}/overtime/{id}", new OvertimeRequest(new DateOnly(2026, 9, 8), new TimeOnly(18, 0), new TimeOnly(20, 0), "B")));
            Assert.AreEqual(120, sheet.Overtime.Single().TotalMinutes);
            Assert.AreEqual("B", sheet.Overtime.Single().Description);

            sheet = await Read(await amra.DeleteAsync($"{Url}/overtime/{id}"));
            Assert.AreEqual(0, sheet.Overtime.Count);
            Assert.AreEqual(HttpStatusCode.NotFound, (await amra.DeleteAsync($"{Url}/overtime/{id}")).StatusCode);
        }
    }

    // ---- Who sees and edits what -----------------------------------------------------------------------------------------

    [TestMethod]
    public async Task EveryEmployeeHasOnlyTheirOwnTimesheet()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            await Read(await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(Codes.AnnualLeaveA, 0)));

            var benjamin = factory.As(@"corp\benjamin");
            var theirs = await benjamin.GetFromJsonAsync<SheetDto>(Url);
            CollectionAssert.AreEqual(new[] { Codes.RegularWork }, theirs!.Days[0].Entries.Select(e => e.Code).ToArray(), "a colleague's leave is not theirs");
        }
    }

    [TestMethod]
    public async Task ASubmittedOrApprovedTimesheet_ShouldBeLocked_AReturnedOneEditable()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            await Read(await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(Codes.RegularWork, 480)));

            async Task SetStatus(TimesheetStatus status)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CompanyDbContext>();
                var sheet = db.Sheets.Single(s => s.EmployeeCode == "Z-001" && s.Month == 9);
                sheet.Status = status;
                await db.SaveChangesAsync();
            }

            foreach (var status in new[] { TimesheetStatus.Submitted, TimesheetStatus.Approved })
            {
                await SetStatus(status);
                var locked = await amra.PutAsJsonAsync(Url + "/days/2026-09-02", new SetEntryRequest(Codes.RegularWork, 480));
                Assert.AreEqual(HttpStatusCode.Conflict, locked.StatusCode);
                Assert.AreEqual("locked", (await locked.ReadAsync<ApiErrorDto>())!.Code);
                Assert.AreEqual(HttpStatusCode.Conflict, (await amra.PostAsync(Url + "/fill-standard-days", null)).StatusCode);
                Assert.AreEqual(HttpStatusCode.Conflict, (await amra.PostAsJsonAsync(Url + "/overtime", new OvertimeRequest(new DateOnly(2026, 9, 7), new TimeOnly(18, 0), new TimeOnly(19, 0), "x"))).StatusCode);

                var read = await amra.GetFromJsonAsync<SheetDto>(Url);
                Assert.IsFalse(read!.Editable, "reading is always allowed");
                Assert.AreEqual(status.ToString(), read.Status.ToString());
            }

            await SetStatus(TimesheetStatus.Returned);
            Assert.AreEqual(HttpStatusCode.OK, (await amra.PutAsJsonAsync(Url + "/days/2026-09-02", new SetEntryRequest(Codes.RegularWork, 480))).StatusCode);
        }
    }

    [TestMethod]
    public async Task TheTimesheet_ShouldNeedARegisteredEmployee()
    {
        using var factory = new ApiFactory();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(Url)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await factory.As(@"corp\nobody").GetAsync(Url)).StatusCode);
    }

    [TestMethod]
    public async Task EveryChange_ShouldBeAuditedWithTheEmployeeAsActor()
    {
        var (factory, amra) = await StartAsync();
        using (factory)
        {
            await Read(await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(Codes.RegularWork, 480)));
            await Read(await amra.PostAsJsonAsync(Url + "/overtime", new OvertimeRequest(new DateOnly(2026, 9, 7), new TimeOnly(18, 0), new TimeOnly(19, 0), "x")));

            var events = await factory.Admin.GetFromJsonAsync<List<AuditEventDto>>("/api/admin/audit?take=200");
            var entry = events!.First(e => e.Action == "EntrySet");
            Assert.AreEqual("Z-001", entry.Actor);
            Assert.AreEqual("Z-001/2026-09", entry.EntityId);
            Assert.IsTrue(events.Any(e => e.Action == "OvertimeAdded" && e.Actor == "Z-001"));
        }
    }
}
