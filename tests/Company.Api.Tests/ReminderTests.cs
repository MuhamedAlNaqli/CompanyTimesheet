using System.Net.Http.Json;
using Company.Api.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Company.Api.Tests;

/// <summary>A server clock that shows the date the test chooses</summary>
internal sealed class FixedTime : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTime(int year, int month, int day) => _now = new DateTimeOffset(year, month, day, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

[TestClass]
public class ReminderTests
{
    private static async Task<(ApiFactory Factory, HttpClient Amra, HttpClient Benjamin)> StartAsync(int year, int month, int day, bool fromSeptember = true)
    {
        var factory = new ApiFactory(fromSeptember ? new Dictionary<string, string?> { ["Reminders:StartFrom"] = "2026-09" } : null) { Time = new FixedTime(year, month, day) };
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");
        await factory.LinkAsync("Z-002", @"corp\benjamin");
        await factory.LinkAsync("Z-003", @"corp\emir");
        await factory.UpdateAsync("Z-001", new(null, null, "Z-002", null, null, null, null, null));
        await factory.UpdateAsync("Z-002", new(null, null, "Z-003", null, null, null, null, null));
        await factory.UpdateAsync("Z-003", new(null, null, null, true, null, null, null, null));
        return (factory, factory.As(@"corp\amra"), factory.As(@"corp\benjamin"));
    }

    private static async Task<List<ReminderDto>> Due(HttpClient client) =>
        (await client.GetFromJsonAsync<RemindersDto>("/api/reminders"))!.Items.ToList();

    [TestMethod]
    public async Task TheEmployeeIsRemindedThreeDaysBeforeTheDeadline_AndNotBefore()
    {
        var (early, amraEarly, _) = await StartAsync(2026, 9, 21);
        using (early) Assert.AreEqual(0, (await Due(amraEarly)).Count, "the deadline is Friday 25 September");

        var (soon, amraSoon, _) = await StartAsync(2026, 9, 22);
        using (soon)
        {
            var item = (await Due(amraSoon)).Single();
            Assert.AreEqual(ReminderType.SubmitSoon, item.Kind);
            Assert.AreEqual(new DateOnly(2026, 9, 25), item.Deadline);
            Assert.AreEqual(9, item.Month);
        }
    }

    [TestMethod]
    public async Task APassedDeadline_ShouldBeOverdue_UntilTheSheetIsSubmitted()
    {
        var (factory, amra, benjamin) = await StartAsync(2026, 9, 26);
        using (factory)
        {
            Assert.AreEqual(ReminderType.SubmitOverdue, (await Due(amra)).Single().Kind);

            await amra.PostAsync("/api/timesheet/2026/9/fill-standard-days", null);
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);
            Assert.AreEqual(0, (await Due(amra)).Count, "a submitted sheet needs nothing from the employee");

            var approver = (await Due(benjamin)).Single(i => i.Kind is ReminderType.ApproveWaiting or ReminderType.ApproveOverdue);
            Assert.AreEqual(ReminderType.ApproveWaiting, approver.Kind);
            Assert.AreEqual(1, approver.Count);
            Assert.AreEqual(new DateOnly(2026, 9, 30), approver.Deadline);
        }
    }

    [TestMethod]
    public async Task ApprovalIsOverdueOnceTheMonthIsOver()
    {
        var (factory, amra, benjamin) = await StartAsync(2026, 10, 1);
        using (factory)
        {
            await amra.PostAsync("/api/timesheet/2026/9/fill-standard-days", null);
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);

            Assert.AreEqual(ReminderType.ApproveOverdue, (await Due(benjamin)).Single(i => i.Kind is ReminderType.ApproveWaiting or ReminderType.ApproveOverdue).Kind);
            Assert.IsTrue((await Due(benjamin)).Any(i => i.Kind == ReminderType.SubmitOverdue), "Benjamin has his own timesheet to submit as well");
            Assert.IsFalse((await Due(factory.As(@"corp\emir"))).Any(i => i.Kind is ReminderType.ApproveWaiting or ReminderType.ApproveOverdue), "the lawyer's turn has not come");
        }
    }

    [TestMethod]
    public async Task AReturnedSheet_IsDueForTheEmployeeUntilItIsSubmittedAgain()
    {
        var (factory, amra, benjamin) = await StartAsync(2026, 9, 10);
        using (factory)
        {
            await amra.PostAsync("/api/timesheet/2026/9/fill-standard-days", null);
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);
            await benjamin.PostAsJsonAsync("/api/approvals/Z-001/2026/9/return", new ReturnRequest("Ispravi"));

            Assert.AreEqual(ReminderType.FixReturned, (await Due(amra)).Single().Kind);
            Assert.AreEqual(0, (await Due(benjamin)).Count);

            await amra.PostAsJsonAsync("/api/timesheet/2026/9/messages/0/answer", new AnswerRequest("Gotovo"));
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);
            Assert.AreEqual(0, (await Due(amra)).Count);
            Assert.AreEqual(ReminderType.ApproveWaiting, (await Due(benjamin)).Single().Kind);
        }
    }

    [TestMethod]
    public async Task TheDeadlineMovesToTheNextWorkingDayWhenThe25thIsAWeekend()
    {
        // 25 October 2026 is a Sunday, so the deadline is Monday the 26th
        var (sunday, amraSunday, _) = await StartAsync(2026, 10, 25);
        using (sunday)
        {
            var item = (await Due(amraSunday)).Single(i => i.Month == 10);
            Assert.AreEqual(ReminderType.SubmitSoon, item.Kind);
            Assert.AreEqual(new DateOnly(2026, 10, 26), item.Deadline);
        }

        var (tuesday, amraTuesday, _) = await StartAsync(2026, 10, 27);
        using (tuesday) Assert.AreEqual(ReminderType.SubmitOverdue, (await Due(amraTuesday)).Single(i => i.Month == 10).Kind);
    }

    [TestMethod]
    public async Task EarlierMonthsAreIgnoredBeforeTheStartMonth_ButOverdueAfterwards()
    {
        var (withStart, amraWith, _) = await StartAsync(2026, 9, 22);
        using (withStart) CollectionAssert.AreEqual(new[] { 9 }, (await Due(amraWith)).Select(i => i.Month).ToArray());

        var (withoutStart, amraWithout, _) = await StartAsync(2026, 9, 22, fromSeptember: false);
        using (withoutStart)
        {
            var items = await Due(amraWithout);
            Assert.AreEqual(ReminderType.SubmitOverdue, items.Single(i => i.Month == 8).Kind, "August was never submitted");
            Assert.AreEqual(ReminderType.SubmitSoon, items.Single(i => i.Month == 9).Kind);
        }
    }

    [TestMethod]
    public async Task NobodyIsRemindedAboutMonthsOutsideTheirEmployment()
    {
        var (factory, amra, _) = await StartAsync(2026, 9, 26);
        using (factory)
        {
            await factory.UpdateAsync("Z-001", new(null, null, null, null, null, null, new DateOnly(2026, 10, 1), null));
            Assert.AreEqual(0, (await Due(amra)).Count, "employed only from October");

            await factory.UpdateAsync("Z-001", new(null, null, null, null, null, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 31)));
            Assert.AreEqual(0, (await Due(amra)).Count, "employment ended in August");
        }
    }
}
