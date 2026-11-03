using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Company.Api.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Company.Api.Tests;

/// <summary>A clock the test can move forward</summary>
internal sealed class MovableTime : TimeProvider
{
    public DateTimeOffset Now { get; set; }

    public MovableTime(int year, int month, int day) => Now = new DateTimeOffset(year, month, day, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>The people below a supervisor: the tree with who has submitted, the reminder to one of them, the summary for the approver, and the product switched off</summary>
[TestClass]
public class TeamTests
{
    private const string Sept = "/api/timesheet/2026/9";

    /// <summary>Amra and Damir report to Benjamin, who reports to Emir. Nobody is above Emir.</summary>
    private static async Task<(ApiFactory Factory, HttpClient Amra, HttpClient Benjamin, HttpClient Emir, HttpClient Damir, MovableTime Time)> StartAsync(
        Dictionary<string, string?>? settings = null)
    {
        var time = new MovableTime(2026, 10, 2);
        var factory = new ApiFactory(settings) { Time = time };
        await factory.ImportAsync(ApiFactory.SmallList);
        foreach (var (code, account) in new[] { ("Z-001", @"corp\amra"), ("Z-002", @"corp\benjamin"), ("Z-003", @"corp\emir"), ("Z-004", @"corp\damir") })
            await factory.LinkAsync(code, account);
        await factory.UpdateAsync("Z-001", new(null, null, "Z-002", null, null, null, null, null));
        await factory.UpdateAsync("Z-004", new(null, null, "Z-002", null, null, null, null, null));
        await factory.UpdateAsync("Z-002", new(null, null, "Z-003", null, null, null, null, null));
        return (factory, factory.As(@"corp\amra"), factory.As(@"corp\benjamin"), factory.As(@"corp\emir"), factory.As(@"corp\damir"), time);
    }

    private static async Task<TeamStatusDto> StatusOf(HttpClient client) =>
        (await client.GetFromJsonAsync<TeamStatusDto>("/api/team/2026/9/status"))!;

    [TestMethod]
    public async Task TheTree_ShouldShowEveryoneBelowTheCaller_WithWhoHasSubmitted()
    {
        var (factory, amra, benjamin, emir, _, _) = await StartAsync();
        using (factory)
        {
            await amra.PostAsync(Sept + "/fill-standard-days", null);
            await amra.PostAsync(Sept + "/submit", null);

            var lead = await StatusOf(benjamin);
            CollectionAssert.AreEqual(new[] { "Z-001", "Z-004" }, lead.Members.Select(m => m.Code).ToArray(), "the supervisor sees the people who report to them, by name");
            Assert.AreEqual(SheetStatus.Submitted, lead.Members.Single(m => m.Code == "Z-001").Status);
            Assert.AreEqual("Benjamin Bašić", lead.Members.Single(m => m.Code == "Z-001").WaitingForName);
            Assert.IsFalse(lead.Members.Single(m => m.Code == "Z-001").CanRemind, "a submitted month needs no reminder");
            Assert.AreEqual(SheetStatus.Draft, lead.Members.Single(m => m.Code == "Z-004").Status, "a month nobody opened is a draft");
            Assert.IsTrue(lead.Members.Single(m => m.Code == "Z-004").CanRemind);

            var top = await StatusOf(emir);
            CollectionAssert.AreEqual(new[] { "Z-002", "Z-001", "Z-004" }, top.Members.Select(m => m.Code).ToArray(), "a person comes right before their own reports");
            CollectionAssert.AreEqual(new[] { 0, 1, 1 }, top.Members.Select(m => m.Depth).ToArray());
            Assert.IsNull(top.Members[0].ParentCode);
            Assert.AreEqual("Z-002", top.Members[1].ParentCode);

            var none = await amra.GetAsync("/api/team/2026/9/status");
            Assert.AreEqual(HttpStatusCode.NotFound, none.StatusCode, "somebody with nobody below sees no team");
        }
    }

    [TestMethod]
    public async Task AReminder_ShouldReachThePerson_AndGoWhenTheyHaveSubmitted()
    {
        var (factory, amra, benjamin, emir, damir, time) = await StartAsync();
        using (factory)
        {
            Assert.IsFalse((await Due(amra)).Any(r => r.Kind == ReminderType.Nudged));

            var sent = await benjamin.PostAsync("/api/team/2026/9/remind/Z-001", null);
            Assert.AreEqual(HttpStatusCode.OK, sent.StatusCode, await sent.Content.ReadAsStringAsync());

            var nudge = (await Due(amra)).Single(r => r.Kind == ReminderType.Nudged);
            Assert.AreEqual("Benjamin Bašić", nudge.From);
            Assert.AreEqual((2026, 9), (nudge.Year, nudge.Month));
            Assert.IsNotNull(nudge.SentUtc);
            Assert.IsFalse((await Due(damir)).Any(r => r.Kind == ReminderType.Nudged), "only the person who was asked");
            Assert.IsNotNull((await StatusOf(benjamin)).Members.Single(m => m.Code == "Z-001").RemindedUtc, "the supervisor sees that it was sent");

            var again = await benjamin.PostAsync("/api/team/2026/9/remind/Z-001", null);
            Assert.AreEqual(HttpStatusCode.Conflict, again.StatusCode);
            Assert.AreEqual("already-reminded", (await again.ReadAsync<ApiErrorDto>())!.Code);

            time.Now = time.Now.AddHours(5);
            Assert.AreEqual(HttpStatusCode.OK, (await emir.PostAsync("/api/team/2026/9/remind/Z-001", null)).StatusCode, "a few hours later anybody above may ask again");
            Assert.AreEqual("Emir Ferhatbegović", (await Due(amra)).Single(r => r.Kind == ReminderType.Nudged).From, "the newest request is the one shown");

            await amra.PostAsync(Sept + "/fill-standard-days", null);
            await amra.PostAsync(Sept + "/submit", null);
            Assert.IsFalse((await Due(amra)).Any(r => r.Kind == ReminderType.Nudged), "nothing is left to ask for once it is submitted");
            time.Now = time.Now.AddHours(5);
            var late = await benjamin.PostAsync("/api/team/2026/9/remind/Z-001", null);
            Assert.AreEqual("nothing-to-remind", (await late.ReadAsync<ApiErrorDto>())!.Code);
        }
    }

    [TestMethod]
    public async Task AReminder_ShouldBeRefusedForSomebodyOutsideTheTeamOrAMonthThatIsNotDueYet()
    {
        var (factory, amra, benjamin, _, damir, _) = await StartAsync();
        using (factory)
        {
            Assert.AreEqual(HttpStatusCode.NotFound, (await amra.PostAsync("/api/team/2026/9/remind/Z-002", null)).StatusCode, "an employee cannot remind the one above");
            Assert.AreEqual(HttpStatusCode.NotFound, (await damir.PostAsync("/api/team/2026/9/remind/Z-001", null)).StatusCode, "a colleague is not a supervisor");
            Assert.AreEqual(HttpStatusCode.NotFound, (await benjamin.PostAsync("/api/team/2026/9/remind/Z-003", null)).StatusCode, "nor the one above");
            Assert.AreEqual("nothing-to-remind", (await (await benjamin.PostAsync("/api/team/2026/11/remind/Z-001", null)).ReadAsync<ApiErrorDto>())!.Code, "November has not started");
            Assert.AreEqual(HttpStatusCode.BadRequest, (await benjamin.PostAsync("/api/team/2026/13/remind/Z-001", null)).StatusCode);
        }
    }

    private static async Task<List<ReminderDto>> Due(HttpClient client) =>
        (await client.GetFromJsonAsync<RemindersDto>("/api/reminders"))!.Items.ToList();

    [TestMethod]
    public async Task TheSummary_ShouldSayWhatThereIsToReport_ForEachPerson()
    {
        var (factory, amra, benjamin, emir, damir, _) = await StartAsync();
        using (factory)
        {
            await amra.PostAsync(Sept + "/fill-standard-days", null);
            // Overtime, and a business trip from Wednesday to Friday
            await amra.PostAsJsonAsync(Sept + "/overtime", new OvertimeRequest(new DateOnly(2026, 9, 2), new TimeOnly(19, 30), new TimeOnly(22, 0), "BILLING"));
            await amra.PostAsJsonAsync(Sept + "/absences", new AbsenceRangeRequest(Codes.BusinessTripDomestic, new DateOnly(2026, 9, 9), new DateOnly(2026, 9, 11)));
            await damir.PostAsync(Sept + "/fill-standard-days", null);
            // Annual leave across a weekend is one period
            await damir.PostAsJsonAsync(Sept + "/absences", new AbsenceRangeRequest(Codes.AnnualLeaveA, new DateOnly(2026, 9, 17), new DateOnly(2026, 9, 21)));

            var response = await benjamin.GetAsync("/api/team/2026/9/summary/xlsx");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
            var lines = workbook.Worksheet(1).CellsUsed().Where(c => c.Address.ColumnNumber == 1).Select(c => c.GetString()).ToList();

            var amraAt = lines.IndexOf("Amra Adžamija");
            Assert.IsTrue(amraAt >= 0, string.Join(" | ", lines));
            Assert.AreEqual("Prekovremeni sati prikazani u tabeli ispod.", lines[amraAt + 1]);
            Assert.AreEqual($"{TimeCodeNames.Bs(Codes.BusinessTripDomestic)}: 09.09.-11.09.", lines[amraAt + 2]);

            var damirAt = lines.IndexOf("Damir Dizdarević");
            Assert.AreEqual($"{TimeCodeNames.Bs(Codes.AnnualLeaveA)}: 17.09.-21.09.", lines[damirAt + 1], "Thursday to Monday is one period; the weekend is not an absence");

            var table = workbook.Worksheet(1).RowsUsed().First(r => r.Cell(1).GetString() == "Uposlenik");
            Assert.AreEqual("Kratak opis posla", table.Cell(5).GetString());
            var overtime = table.RowBelow();
            Assert.AreEqual("Amra Adžamija", overtime.Cell(1).GetString());
            Assert.AreEqual("02.09.2026.", overtime.Cell(2).GetString());
            Assert.AreEqual("19:30 - 22:00", overtime.Cell(3).GetString());
            Assert.AreEqual("2,5", overtime.Cell(4).GetString());
            Assert.AreEqual("BILLING", overtime.Cell(5).GetString());

            Assert.IsTrue((await benjamin.GetAsync("/api/team/2026/9/summary/pdf")).IsSuccessStatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, (await amra.GetAsync("/api/team/2026/9/summary/xlsx")).StatusCode, "nobody reports to Amra");

            var wide = await emir.GetAsync("/api/team/2026/9/summary/xlsx");
            using var wideBook = new XLWorkbook(await wide.Content.ReadAsStreamAsync());
            var wideLines = wideBook.Worksheet(1).CellsUsed().Where(c => c.Address.ColumnNumber == 1).Select(c => c.GetString()).ToList();
            CollectionAssert.Contains(wideLines, "Direktno: Emir Ferhatbegović");
            CollectionAssert.Contains(wideLines, "Tim: Benjamin Bašić", "the people of a team lead have a group of their own");
        }
    }

    [TestMethod]
    public async Task TheSummaryAsText_ListsEverybody_WithTheOvertimeTableToPasteIntoAMail()
    {
        var (factory, amra, benjamin, _, damir, _) = await StartAsync(new Dictionary<string, string?> { ["Features:SheetExport"] = "false" });
        using (factory)
        {
            await amra.PostAsJsonAsync(Sept + "/overtime", new OvertimeRequest(new DateOnly(2026, 9, 2), new TimeOnly(19, 30), new TimeOnly(22, 0), "BILLING"));
            await amra.PostAsJsonAsync(Sept + "/absences", new AbsenceRangeRequest(Codes.BusinessTripDomestic, new DateOnly(2026, 9, 9), new DateOnly(2026, 9, 11)));
            await damir.GetAsync(Sept);

            var summary = (await benjamin.GetFromJsonAsync<TeamSummaryDto>("/api/team/2026/9/summary"))!;

            var people = summary.Groups.Single().People;
            CollectionAssert.AreEqual(new[] { "Amra Adžamija", "Damir Dizdarević" }, people.Select(p => p.Name).ToArray());
            Assert.AreEqual("Prekovremeni sati prikazani u tabeli ispod.", people[0].Lines[0]);
            Assert.AreEqual("Nema prekovremenih sati niti izostanaka.", people[1].Lines[0], "a person with nothing to report is still listed");
            Assert.AreEqual("2,5", summary.Overtime.Single().Hours);

            StringAssert.StartsWith(summary.Text, "Šihtarice za Septembar 2026");
            StringAssert.Contains(summary.Text, "Damir Dizdarević\nNema prekovremenih sati niti izostanaka.".Replace("\n", Environment.NewLine));
            StringAssert.Contains(summary.Text, "Amra Adžamija\t02.09.2026.\t19:30 - 22:00\t2,5\tBILLING");
            StringAssert.Contains(summary.Text, $"{TimeCodeNames.Bs(Codes.BusinessTripDomestic)}: 09.09.-11.09.");
            Assert.IsFalse(summary.Text.Contains("nije odobreno"), "the note of an unapproved sheet is for the screen, not for the message");
            Assert.IsNotNull(people[0].Note);
        }
    }

    [TestMethod]
    public async Task WithoutSheetExport_NoFileCanBeAskedFor_ButTheSummaryTextCan()
    {
        var (factory, amra, benjamin, _, _, _) = await StartAsync(new Dictionary<string, string?> { ["Features:SheetExport"] = "false" });
        using (factory)
        {
            Assert.IsFalse((await amra.GetFromJsonAsync<MeDto>("/api/me"))!.SheetExport);
            foreach (var url in new[] { Sept + "/print/xlsx", "/api/team/2026/9/print/pdf", "/api/team/2026/9/summary/xlsx", "/api/team/2026/9/summary/pdf" })
                Assert.AreEqual(HttpStatusCode.NotFound, (await benjamin.GetAsync(url)).StatusCode, url);
            Assert.AreEqual(HttpStatusCode.OK, (await benjamin.GetAsync("/api/team/2026/9/summary")).StatusCode);
        }
    }

    [TestMethod]
    public async Task AMonthCanBeSubmittedWithoutAnyEntry_BecauseTheStandardDaysAreTheDefault()
    {
        var (factory, amra, benjamin, _, _, _) = await StartAsync();
        using (factory)
        {
            var submitted = await amra.PostAsync(Sept + "/submit", null);
            Assert.AreEqual(HttpStatusCode.OK, submitted.StatusCode, await submitted.Content.ReadAsStringAsync());
            var sheet = (await submitted.ReadAsync<SheetDto>())!;
            Assert.AreEqual(SheetStatus.Submitted, sheet.Status);
            Assert.AreEqual(176 * 60, sheet.Summary.FundMinutes);
        }
    }

    [TestMethod]
    public async Task WithoutFinanceAndLegal_TheChainRunsToTheTop_AndTheirRolesAndExportAreOff()
    {
        var (factory, amra, benjamin, emir, damir, _) = await StartAsync(new Dictionary<string, string?> { ["Features:FinanceAndLegal"] = "false" });
        using (factory)
        {
            // Emir is the lawyer, flagged final approver, and Damir is above him: with the legal department switched off Emir does not end the chain
            await factory.UpdateAsync("Z-004", new(null, null, "", null, null, null, null, null));
            await factory.UpdateAsync("Z-003", new(null, null, "Z-004", true, null, null, null, null, IsFinance: true));

            var me = (await amra.GetFromJsonAsync<MeDto>("/api/me"))!;
            CollectionAssert.AreEqual(new[] { "Z-002", "Z-003", "Z-004" }, me.ApprovalChain.ToArray(), "the lawyer is just somebody in the chart");

            var emirMe = (await emir.GetFromJsonAsync<MeDto>("/api/me"))!;
            Assert.IsFalse(emirMe.IsFinalApprover);
            Assert.IsFalse(emirMe.IsFinance);
            Assert.AreEqual(HttpStatusCode.NotFound, (await factory.Admin.GetAsync("/api/export/2026/9/xlsx")).StatusCode, "the export is not served");
        }
    }

    [TestMethod]
    public async Task WithFinanceAndLegal_TheLawyerStillEndsTheChain()
    {
        var (factory, amra, _, _, _, _) = await StartAsync();
        using (factory)
        {
            await factory.UpdateAsync("Z-004", new(null, null, "", null, null, null, null, null));
            await factory.UpdateAsync("Z-003", new(null, null, "Z-004", true, null, null, null, null));
            CollectionAssert.AreEqual(new[] { "Z-002", "Z-003" }, (await amra.GetFromJsonAsync<MeDto>("/api/me"))!.ApprovalChain.ToArray());
        }
    }
}
