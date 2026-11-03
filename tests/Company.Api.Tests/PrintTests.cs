using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Company.Api.Contracts;
using Company.Api.Printing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Company.Api.Tests;

[TestClass]
public class PrintTests
{
    private const string Url = "/api/timesheet/2026/9";

    private static async Task<(ApiFactory Factory, HttpClient Amra, HttpClient Benjamin, HttpClient Emir, HttpClient Damir)> StartAsync()
    {
        var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        foreach (var (code, account) in new[] { ("Z-001", @"corp\amra"), ("Z-002", @"corp\benjamin"), ("Z-003", @"corp\emir"), ("Z-004", @"corp\damir") })
            await factory.LinkAsync(code, account);
        await factory.UpdateAsync("Z-001", new(null, null, "Z-002", null, null, null, null, null));
        await factory.UpdateAsync("Z-002", new(null, null, "Z-003", null, null, null, null, null));
        await factory.UpdateAsync("Z-003", new(null, null, null, true, null, null, null, null));
        return (factory, factory.As(@"corp\amra"), factory.As(@"corp\benjamin"), factory.As(@"corp\emir"), factory.As(@"corp\damir"));
    }

    /// <summary>September 2026 for Amra: standard days, annual leave on 1 and 2 September, overtime on Saturday the 5th</summary>
    private static async Task FillAsync(HttpClient amra)
    {
        await amra.PostAsync(Url + "/fill-standard-days", null);
        await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(Codes.AnnualLeaveA, 0));
        await amra.PutAsJsonAsync(Url + "/days/2026-09-02", new SetEntryRequest(Codes.AnnualLeaveA, 0));
        await amra.PostAsJsonAsync(Url + "/overtime", new OvertimeRequest(new DateOnly(2026, 9, 5), new TimeOnly(8, 30), new TimeOnly(11, 0), "Provisioning"));
    }

    private static async Task ApproveAsync(HttpClient amra, HttpClient benjamin, HttpClient emir)
    {
        await amra.PostAsync(Url + "/submit", null);
        await benjamin.PostAsync("/api/approvals/Z-001/2026/9/approve", null);
        await emir.PostAsync("/api/approvals/Z-001/2026/9/approve", null);
    }

    private static XLWorkbook Open(byte[] bytes) => new(new MemoryStream(bytes));

    private static IXLRow FindRow(IXLWorksheet sheet, int code, int fromRow = 1) =>
        sheet.RowsUsed().First(r => r.RowNumber() >= fromRow && r.Cell(3).Value.IsNumber && (int)r.Cell(3).Value.GetNumber() == code);

    [TestMethod]
    public async Task TheExcelSheet_ShouldFollowTheLayoutOfTheCurrentSheet()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            await FillAsync(amra);
            var response = await amra.GetAsync(Url + "/print/xlsx");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            StringAssert.Contains(response.Content.Headers.ContentType!.MediaType!, "spreadsheetml");
            Assert.AreEqual("Sihtarica_Z-001_2026-09.xlsx", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));

            using var workbook = Open(await response.Content.ReadAsByteArrayAsync());
            var sheet = workbook.Worksheet("Šihtarica");

            // The legend lists every code, in its group colour
            Assert.IsTrue(sheet.CellsUsed().Any(c => c.GetString().StartsWith("1022") && c.Style.Fill.BackgroundColor.Color.Name == "ff92d050"));

            // A block of nine rows, with the code and the name of the employee merged over it
            var regular = FindRow(sheet, Codes.RegularWork);
            Assert.AreEqual("Z-001", sheet.Cell(regular.RowNumber(), 1).GetString());
            Assert.AreEqual("Amra Adžamija", sheet.Cell(regular.RowNumber(), 2).GetString());
            var codes = Enumerable.Range(0, 9).Select(i => (int)sheet.Cell(regular.RowNumber() + i, 3).Value.GetNumber()).ToArray();
            CollectionAssert.AreEqual(new[] { 1010, 1024, 2010, 2020, 2021, 2022, 2023, 2024, 2025 }, codes);

            // Day columns start at the fifth column: absences are typed into the regular-work row as the code, working days as hours
            Assert.AreEqual(1022, regular.Cell(5).Value.GetNumber());
            Assert.AreEqual(1022, regular.Cell(6).Value.GetNumber());
            Assert.AreEqual(8, regular.Cell(7).Value.GetNumber());
            Assert.IsTrue(regular.Cell(9).IsEmpty(XLCellsUsedOptions.Contents), "Saturday the 5th has no regular hours");

            // The weekend column is dark red in every row; the overtime row has the hours of Saturday
            Assert.AreEqual("ffc00000", regular.Cell(9).Style.Fill.BackgroundColor.Color.Name);
            var weekendOvertime = FindRow(sheet, Codes.WeekendOvertime, regular.RowNumber());
            Assert.AreEqual(2.5, weekendOvertime.Cell(9).Value.GetNumber());
            Assert.AreEqual("ffc00000", weekendOvertime.Cell(9).Style.Fill.BackgroundColor.Color.Name);
            Assert.AreEqual("ffe6b8b7", weekendOvertime.Cell(5).Style.Fill.BackgroundColor.Color.Name, "overtime rows are pink");
            Assert.AreEqual("ff92d050", regular.Cell(7).Style.Fill.BackgroundColor.Color.Name, "regular rows are green");

            Assert.AreEqual(XLPageOrientation.Landscape, sheet.PageSetup.PageOrientation);
        }
    }

    [TestMethod]
    public async Task TheOvertimeRegister_ShouldListWhoWorkedWhenAndWhy()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            await FillAsync(amra);
            using var workbook = Open(await (await amra.GetAsync(Url + "/print/xlsx")).Content.ReadAsByteArrayAsync());
            var register = workbook.Worksheet("Prekovremeni rad");

            Assert.AreEqual("Uposlenik", register.Cell(1, 1).GetString());
            Assert.AreEqual("Amra Adžamija", register.Cell(2, 1).GetString());
            Assert.AreEqual("05.09.2026.", register.Cell(2, 2).GetString());
            Assert.AreEqual("08:30-11:00", register.Cell(2, 3).GetString());
            Assert.AreEqual(2.5, register.Cell(2, 4).Value.GetNumber());
            Assert.AreEqual("Provisioning", register.Cell(2, 5).GetString());
        }
    }

    [TestMethod]
    public async Task HolidayAndNightRows_ShouldAppearOnlyWhenTheyAreUsed()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            await factory.Admin.PostAsJsonAsync("/api/admin/holidays", new HolidayDto(new DateOnly(2026, 9, 9), "Praznik"));
            await amra.PutAsJsonAsync(Url + "/days/2026-09-09", new SetEntryRequest(Codes.StateHoliday, 480));
            await amra.PutAsJsonAsync(Url + "/days/2026-09-10", new SetEntryRequest(Codes.RegularNightWork, 240));

            var sheet = (await amra.GetFromJsonAsync<SheetDto>(Url))!;
            var rows = LegacySheet.Rows(sheet);

            CollectionAssert.AreEqual(new[] { 1010, 1024, 2010, 2011, 2020, 2021, 2022, 2023, 2024, 2025 }, rows.Select(r => r.Code.Code).ToArray());
            Assert.AreEqual(8m, rows.Single(r => r.Code.Code == Codes.StateHoliday).Days[8]);
            Assert.AreEqual(4m, rows.Single(r => r.Code.Code == Codes.RegularNightWork).Days[9]);
        }
    }

    [TestMethod]
    public async Task ThePdf_ShouldBeProduced()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            await FillAsync(amra);
            var response = await amra.GetAsync(Url + "/print/pdf");
            Assert.AreEqual("application/pdf", response.Content.Headers.ContentType!.MediaType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.AreEqual("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.IsTrue(bytes.Length > 3000);
        }
    }

    [TestMethod]
    public async Task ASheetThatIsNotApproved_ShouldSayItOnThePrintout()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            await FillAsync(amra);
            using var workbook = Open(await (await amra.GetAsync(Url + "/print/xlsx")).Content.ReadAsByteArrayAsync());
            Assert.IsTrue(workbook.Worksheet("Šihtarica").CellsUsed().Any(c => c.GetString().Contains("nije odobreno")));
        }
    }

    [TestMethod]
    public async Task OnlyTheOwnerAndTheChainMayPrintASheet_AndTheFormatIsChecked()
    {
        var (factory, amra, benjamin, _, damir) = await StartAsync();
        using (factory)
        {
            await FillAsync(amra);
            await amra.PostAsync(Url + "/submit", null);

            Assert.AreEqual(HttpStatusCode.OK, (await benjamin.GetAsync("/api/approvals/Z-001/2026/9/print/xlsx")).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await damir.GetAsync("/api/approvals/Z-001/2026/9/print/xlsx")).StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, (await amra.GetAsync(Url + "/print/docx")).StatusCode);
        }
    }

    [TestMethod]
    public async Task FinanceReceivesTheApprovedSheetsOfEverybody_AndNothingElse()
    {
        var (factory, amra, benjamin, emir, damir) = await StartAsync();
        using (factory)
        {
            await FillAsync(amra);
            await factory.Admin.PutAsJsonAsync("/api/admin/employees/Z-004", new UpdateEmployeeRequest(null, null, null, null, null, null, null, null, IsFinance: true));
            Assert.IsTrue((await damir.GetFromJsonAsync<MeDto>("/api/me"))!.IsFinance);

            Assert.AreEqual(HttpStatusCode.NotFound, (await damir.GetAsync("/api/export/2026/9/xlsx")).StatusCode, "nothing is approved yet");
            Assert.AreEqual(HttpStatusCode.Forbidden, (await amra.GetAsync("/api/export/2026/9/xlsx")).StatusCode, "an ordinary employee has no access");
            Assert.AreEqual(HttpStatusCode.Forbidden, (await benjamin.GetAsync("/api/export/2026/9/xlsx")).StatusCode, "an approver is not finance");

            await amra.PostAsync(Url + "/submit", null);
            await benjamin.PostAsync("/api/approvals/Z-001/2026/9/approve", null);
            Assert.AreEqual(HttpStatusCode.NotFound, (await damir.GetAsync("/api/export/2026/9/xlsx")).StatusCode, "half way through the chain is not approved");

            await emir.PostAsync("/api/approvals/Z-001/2026/9/approve", null);
            var response = await damir.GetAsync("/api/export/2026/9/xlsx");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var workbook = Open(await response.Content.ReadAsByteArrayAsync());
            Assert.AreEqual(1, workbook.Worksheet("Šihtarica").CellsUsed().Count(c => c.GetString() == "Amra Adžamija"));
            Assert.IsTrue((await factory.Admin.GetAsync("/api/export/2026/9/pdf")).IsSuccessStatusCode, "the administrator may export too");

            var log = (await factory.Admin.GetFromJsonAsync<List<AuditEventDto>>("/api/admin/audit?take=200"))!;
            Assert.IsTrue(log.Any(e => e.Action == "ApprovedSheetsPrinted" && e.Actor == "Z-004"));
        }
    }

    [TestMethod]
    public async Task ManyEmployees_ShouldBreakIntoPagesOfWholeBlocks()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            await FillAsync(amra);
            var sheet = (await amra.GetFromJsonAsync<SheetDto>(Url))!;
            var employees = Enumerable.Range(1, 9).Select(i => new PrintedEmployee($"Z-{i:000}", $"Zaposlenik {i}", sheet)).ToList();

            using var workbook = Open(LegacySheet.ToExcel(2026, 9, employees));
            var worksheet = workbook.Worksheet("Šihtarica");
            Assert.AreEqual(2, worksheet.PageSetup.RowBreaks.Count, "four employees to a page: breaks after the 4th and the 8th");
            Assert.IsTrue(worksheet.PageSetup.RowBreaks.All(b => (b - FindRow(worksheet, Codes.RegularWork).RowNumber() + 1) % 9 == 0), "a break falls between two blocks");

            var pdf = LegacySheet.ToPdf(2026, 9, employees);
            Assert.AreEqual("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        }
    }

    [TestMethod]
    public async Task ASupervisorPrintsTheMonthOfEverybodyWhoReportsToThem()
    {
        var (factory, amra, benjamin, emir, damir) = await StartAsync();
        using (factory)
        {
            await factory.UpdateAsync("Z-004", new(null, null, "Z-002", null, null, null, null, null));
            await FillAsync(amra);
            await amra.PostAsync(Url + "/submit", null);

            var response = await benjamin.GetAsync("/api/team/2026/9/print/xlsx");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("Sihtarice_tima_2026-09.xlsx", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));

            using var workbook = Open(await response.Content.ReadAsByteArrayAsync());
            var sheet = workbook.Worksheet("Šihtarica");
            var names = sheet.CellsUsed().Select(c => c.GetString()).ToList();
            Assert.IsTrue(names.Any(n => n.StartsWith("Amra Adžamija") && n.Contains("Predano na odobravanje")), "Amra, with her status");
            Assert.IsTrue(names.Any(n => n.StartsWith("Damir Dizdarević") && n.Contains("U pripremi")), "Damir has not opened the month: an empty draft");
            Assert.IsFalse(names.Any(n => n.StartsWith("Benjamin")), "the supervisor is not in his own team sheet");
            Assert.IsFalse(names.Any(n => n.StartsWith("Emir")), "nor the people above him");

            Assert.AreEqual("%PDF", System.Text.Encoding.ASCII.GetString(await (await benjamin.GetAsync("/api/team/2026/9/print/pdf")).Content.ReadAsByteArrayAsync(), 0, 4));

            Assert.AreEqual(HttpStatusCode.NotFound, (await amra.GetAsync("/api/team/2026/9/print/xlsx")).StatusCode, "nobody reports to Amra");
        }
    }
}
