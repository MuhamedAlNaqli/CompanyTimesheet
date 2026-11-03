using System.Net;
using System.Net.Http.Json;
using Company.Api.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Company.Api.Tests;

[TestClass]
public class WorkflowTests
{
    private const string Url = "/api/timesheet/2026/9";

    /// <summary>Amra reports to Benjamin, who reports to Emir (the lawyer, final approver). Damir is outside the chain.</summary>
    private static async Task<(ApiFactory Factory, HttpClient Amra, HttpClient Benjamin, HttpClient Emir, HttpClient Damir)> StartAsync(bool withChart = true)
    {
        var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");
        await factory.LinkAsync("Z-002", @"corp\benjamin");
        await factory.LinkAsync("Z-003", @"corp\emir");
        await factory.LinkAsync("Z-004", @"corp\damir");
        if (withChart)
        {
            await factory.UpdateAsync("Z-001", new(null, null, "Z-002", null, null, null, null, null));
            await factory.UpdateAsync("Z-002", new(null, null, "Z-003", null, null, null, null, null));
            await factory.UpdateAsync("Z-003", new(null, null, null, true, null, null, null, null));
        }

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

    private static Task<HttpResponseMessage> Fill(HttpClient client) => client.PostAsync(Url + "/fill-standard-days", null);

    [TestMethod]
    public async Task Submitting_ShouldFreezeTheSheetAndHandItToTheFirstApprover()
    {
        var (factory, amra, benjamin, _, _) = await StartAsync();
        using (factory)
        {
            await Fill(amra);
            var sheet = await Read(await amra.PostAsync(Url + "/submit", null));

            Assert.AreEqual(SheetStatus.Submitted, sheet.Status);
            Assert.AreEqual(1, sheet.Revision);
            Assert.IsFalse(sheet.Editable);
            Assert.AreEqual("Z-002", sheet.Workflow!.CurrentApprover);
            CollectionAssert.AreEqual(new[] { "Z-002", "Z-003" }, sheet.Workflow.Chain.Select(c => c.Code).ToArray(), "the chain stops at the final approver");
            Assert.IsFalse(sheet.Workflow.CanSubmit);

            Assert.AreEqual("locked", await ErrorCode(await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(Codes.RegularWork, 300)), HttpStatusCode.Conflict));
            Assert.AreEqual("wrong-status", await ErrorCode(await amra.PostAsync(Url + "/submit", null), HttpStatusCode.Conflict), "a submitted sheet cannot be submitted twice");

            var waiting = (await benjamin.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!;
            Assert.AreEqual(1, waiting.Count);
            Assert.AreEqual("Z-001", waiting[0].EmployeeCode);
            Assert.AreEqual(0, (await amra.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!.Count);
        }
    }

    [TestMethod]
    public async Task Submitting_ShouldBeRefusedWhileBlockingFindingsRemain()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            await Fill(amra);
            await Read(await amra.PutAsJsonAsync(Url + "/days/2026-09-05", new SetEntryRequest(Codes.AnnualLeaveA, 0)));

            Assert.AreEqual("blocking-issues", await ErrorCode(await amra.PostAsync(Url + "/submit", null), HttpStatusCode.Conflict));
            Assert.AreEqual(SheetStatus.Draft, (await amra.GetFromJsonAsync<SheetDto>(Url))!.Status);
        }
    }

    [TestMethod]
    public async Task ANobodyAbove_SubmitsAndApprovesTheirOwnTimesheet()
    {
        var (factory, amra, benjamin, _, _) = await StartAsync(withChart: false);
        using (factory)
        {
            await Fill(amra);
            var submitted = await Read(await amra.PostAsync(Url + "/submit", null));
            Assert.AreEqual(SheetStatus.Submitted, submitted.Status);
            Assert.AreEqual("Z-001", submitted.Workflow!.CurrentApprover);
            Assert.IsTrue(submitted.Workflow.CanAct, "the owner decides on their own timesheet");
            Assert.IsFalse(submitted.Editable);

            var pending = (await amra.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!;
            Assert.AreEqual("Z-001", pending.Single().EmployeeCode, "it is in their own list for approval");

            Assert.AreEqual("forbidden", await ErrorCode(await benjamin.PostAsync("/api/approvals/Z-001/2026/9/approve", null), HttpStatusCode.Forbidden), "nobody else may");

            var approved = await Read(await amra.PostAsync("/api/approvals/Z-001/2026/9/approve", null));
            Assert.AreEqual(SheetStatus.Approved, approved.Status);
            CollectionAssert.AreEqual(new[] { HistoryKind.Submitted, HistoryKind.Approved }, approved.Workflow!.History.Select(h => h.Kind).ToArray());
        }
    }

    [TestMethod]
    public async Task ThePersonWithASupervisor_StillCannotApproveTheirOwnTimesheet()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            await Fill(amra);
            var submitted = await Read(await amra.PostAsync(Url + "/submit", null));
            Assert.IsFalse(submitted.Workflow!.CanAct);
            Assert.AreEqual("self-approval", await ErrorCode(await amra.PostAsync("/api/approvals/Z-001/2026/9/approve", null), HttpStatusCode.Forbidden));
        }
    }

    [TestMethod]
    public async Task EveryStepOfTheChainMustApprove_InOrder_ThenTheSheetIsApproved()
    {
        var (factory, amra, benjamin, emir, _) = await StartAsync();
        using (factory)
        {
            await Fill(amra);
            await Read(await amra.PostAsync(Url + "/submit", null));

            Assert.AreEqual("not-your-turn", await ErrorCode(await emir.PostAsync("/api/approvals/Z-001/2026/9/approve", null), HttpStatusCode.Forbidden), "the lawyer waits for the team lead");

            var afterFirst = await Read(await benjamin.PostAsync("/api/approvals/Z-001/2026/9/approve", null));
            Assert.AreEqual(SheetStatus.Submitted, afterFirst.Status);
            Assert.AreEqual("Z-003", afterFirst.Workflow!.CurrentApprover);
            Assert.IsFalse(afterFirst.Workflow.CanAct, "Benjamin has done his part");
            Assert.AreEqual("not-your-turn", await ErrorCode(await benjamin.PostAsync("/api/approvals/Z-001/2026/9/approve", null), HttpStatusCode.Forbidden));
            Assert.AreEqual(1, (await emir.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!.Count);

            var done = await Read(await emir.PostAsync("/api/approvals/Z-001/2026/9/approve", null));
            Assert.AreEqual(SheetStatus.Approved, done.Status);
            Assert.IsNull(done.Workflow!.CurrentApprover);
            CollectionAssert.AreEqual(new[] { HistoryKind.Submitted, HistoryKind.StepApproved, HistoryKind.Approved }, done.Workflow.History.Select(h => h.Kind).ToArray());
            Assert.AreEqual(0, (await emir.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!.Count);

            var mine = await amra.GetFromJsonAsync<SheetDto>(Url);
            Assert.AreEqual(SheetStatus.Approved, mine!.Status);
            Assert.IsFalse(mine.Editable, "an approved timesheet is locked");
            Assert.AreEqual("locked", await ErrorCode(await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(Codes.RegularWork, 300)), HttpStatusCode.Conflict));
        }
    }

    [TestMethod]
    public async Task OnlyThePeopleOfTheChainMayLookAtASheet()
    {
        var (factory, amra, benjamin, _, damir) = await StartAsync();
        using (factory)
        {
            await Fill(amra);
            await Read(await amra.PostAsync(Url + "/submit", null));

            Assert.AreEqual("forbidden", await ErrorCode(await damir.GetAsync("/api/approvals/Z-001/2026/9"), HttpStatusCode.Forbidden));
            Assert.AreEqual("forbidden", await ErrorCode(await damir.PostAsync("/api/approvals/Z-001/2026/9/approve", null), HttpStatusCode.Forbidden));
            Assert.AreEqual("forbidden", await ErrorCode(await damir.PostAsJsonAsync("/api/approvals/Z-001/2026/9/return", new ReturnRequest("x")), HttpStatusCode.Forbidden));

            var seen = await Read(await benjamin.GetAsync("/api/approvals/Z-001/2026/9"));
            Assert.AreEqual("Amra Adžamija", seen.Workflow!.OwnerName);
            Assert.IsFalse(seen.Editable, "an approver never edits");
            Assert.IsTrue(seen.Workflow.CanAct);
        }
    }

    [TestMethod]
    public async Task NobodyApprovesTheirOwnTimesheet()
    {
        var (factory, amra, _, _, _) = await StartAsync();
        using (factory)
        {
            await Fill(amra);
            await Read(await amra.PostAsync(Url + "/submit", null));

            Assert.AreEqual("self-approval", await ErrorCode(await amra.PostAsync("/api/approvals/Z-001/2026/9/approve", null), HttpStatusCode.Forbidden));
            Assert.AreEqual(SheetStatus.Submitted, (await amra.GetFromJsonAsync<SheetDto>(Url))!.Status);
        }
    }

    [TestMethod]
    public async Task AReturnedSheet_ShouldNeedEveryMessageAnswered_AndRestartTheChainAtTheFirstStep()
    {
        var (factory, amra, benjamin, emir, _) = await StartAsync();
        using (factory)
        {
            await Fill(amra);
            await Read(await amra.PostAsync(Url + "/submit", null));
            await Read(await benjamin.PostAsync("/api/approvals/Z-001/2026/9/approve", null));

            Assert.AreEqual("message-required", await ErrorCode(await emir.PostAsJsonAsync("/api/approvals/Z-001/2026/9/return", new ReturnRequest("  ")), HttpStatusCode.BadRequest));
            var returned = await Read(await emir.PostAsJsonAsync("/api/approvals/Z-001/2026/9/return", new ReturnRequest("Prekovremeni sati nisu jasni")));
            Assert.AreEqual(SheetStatus.Returned, returned.Status);

            var mine = await Read(await amra.GetAsync(Url));
            Assert.IsTrue(mine.Editable, "a returned sheet is editable again");
            Assert.AreEqual("Prekovremeni sati nisu jasni", mine.Workflow!.Messages.Single().Text);
            Assert.AreEqual("Z-003", mine.Workflow.Messages.Single().From);
            Assert.IsFalse(mine.Workflow.CanSubmit, "the message is not answered yet");
            Assert.AreEqual("unanswered-messages", await ErrorCode(await amra.PostAsync(Url + "/submit", null), HttpStatusCode.Conflict));

            var answered = await Read(await amra.PostAsJsonAsync(Url + "/messages/0/answer", new AnswerRequest("Dodan opis")));
            Assert.IsTrue(answered.Workflow!.CanSubmit);
            await Read(await amra.PutAsJsonAsync(Url + "/days/2026-09-01", new SetEntryRequest(Codes.RegularWork, 420)));

            var again = await Read(await amra.PostAsync(Url + "/submit", null));
            Assert.AreEqual(2, again.Revision);
            Assert.AreEqual("Z-002", again.Workflow!.CurrentApprover, "the second submission starts again with the team lead");
            Assert.AreEqual(2, again.Workflow.Revisions.Count);

            // The first submission is kept as it was, the second shows the correction
            var first = await Read(await benjamin.GetAsync("/api/approvals/Z-001/2026/9/revisions/1"));
            var second = await Read(await benjamin.GetAsync("/api/approvals/Z-001/2026/9/revisions/2"));
            Assert.AreEqual(480, first.Days[0].Entries.Single().Minutes);
            Assert.AreEqual(420, second.Days[0].Entries.Single().Minutes);
            Assert.IsFalse(first.Editable);

            Assert.AreEqual("not-found", await ErrorCode(await amra.GetAsync(Url + "/revisions/9"), HttpStatusCode.NotFound));
            Assert.AreEqual(480, (await Read(await amra.GetAsync(Url + "/revisions/1"))).Days[0].Entries.Single().Minutes, "the owner can look at their revisions too");
        }
    }

    [TestMethod]
    public async Task OnlyTheOwnerAnswers_AndOnlyWhileTheSheetIsReturned()
    {
        var (factory, amra, benjamin, _, _) = await StartAsync();
        using (factory)
        {
            await Fill(amra);
            await Read(await amra.PostAsync(Url + "/submit", null));
            Assert.AreEqual("wrong-status", await ErrorCode(await amra.PostAsJsonAsync(Url + "/messages/0/answer", new AnswerRequest("x")), HttpStatusCode.Conflict));

            await Read(await benjamin.PostAsJsonAsync("/api/approvals/Z-001/2026/9/return", new ReturnRequest("Ispravi")));
            Assert.AreEqual("not-found", await ErrorCode(await amra.PostAsJsonAsync(Url + "/messages/5/answer", new AnswerRequest("x")), HttpStatusCode.NotFound));
            Assert.AreEqual("message-required", await ErrorCode(await amra.PostAsJsonAsync(Url + "/messages/0/answer", new AnswerRequest(" ")), HttpStatusCode.BadRequest));
        }
    }

    [TestMethod]
    public async Task EveryStepIsWrittenToTheAuditLog()
    {
        var (factory, amra, benjamin, _, _) = await StartAsync();
        using (factory)
        {
            await Fill(amra);
            await Read(await amra.PostAsync(Url + "/submit", null));
            await Read(await benjamin.PostAsJsonAsync("/api/approvals/Z-001/2026/9/return", new ReturnRequest("Ispravi")));

            var log = (await factory.Admin.GetFromJsonAsync<List<AuditEventDto>>("/api/admin/audit?take=200"))!;
            var submitted = log.Single(e => e.Action == "Submitted");
            var returned = log.Single(e => e.Action == "Returned");
            Assert.AreEqual("Z-001", submitted.Actor);
            Assert.AreEqual("Z-002", returned.Actor);
            StringAssert.Contains(returned.Details!, "Ispravi");
        }
    }
}
