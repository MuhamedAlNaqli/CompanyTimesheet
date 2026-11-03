using System.Net;
using System.Net.Http.Json;
using Company.Api.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Company.Api.Tests;

/// <summary>A deputy decides for an approver who is away, for the period the approver chose</summary>
[TestClass]
public class DeputyTests
{
    private const string Sept = "/api/timesheet/2026/9";

    /// <summary>Amra and Damir report to Benjamin, who reports to Emir (the top). Damir is therefore a colleague of Amra, not an approver.</summary>
    private static async Task<(ApiFactory Factory, HttpClient Amra, HttpClient Benjamin, HttpClient Emir, HttpClient Damir, MovableTime Time)> StartAsync()
    {
        var time = new MovableTime(2026, 10, 2);
        var factory = new ApiFactory { Time = time };
        await factory.ImportAsync(ApiFactory.SmallList);
        foreach (var (code, account) in new[] { ("Z-001", @"corp\amra"), ("Z-002", @"corp\benjamin"), ("Z-003", @"corp\emir"), ("Z-004", @"corp\damir") })
            await factory.LinkAsync(code, account);
        await factory.UpdateAsync("Z-001", new(null, null, "Z-002", null, null, null, null, null));
        await factory.UpdateAsync("Z-004", new(null, null, "Z-002", null, null, null, null, null));
        await factory.UpdateAsync("Z-002", new(null, null, "Z-003", null, null, null, null, null));
        return (factory, factory.As(@"corp\amra"), factory.As(@"corp\benjamin"), factory.As(@"corp\emir"), factory.As(@"corp\damir"), time);
    }

    private static Task<HttpResponseMessage> Add(HttpClient client, string deputy, string from, string to) =>
        client.PostAsJsonAsync("/api/deputies", new AddDeputyRequest(deputy, DateOnly.Parse(from), DateOnly.Parse(to)));

    private static async Task<string> ErrorCode(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<ApiErrorDto>())!.Code;
    }

    [TestMethod]
    public async Task ADeputy_SeesWhatWaitsForTheApproverWhoIsAway_AndApprovesInTheirName()
    {
        var (factory, amra, benjamin, emir, damir, _) = await StartAsync();
        using (factory)
        {
            Assert.AreEqual(HttpStatusCode.OK, (await Add(benjamin, "Z-004", "2026-10-01", "2026-10-15")).StatusCode);
            await amra.PostAsync(Sept + "/submit", null);

            var pending = (await damir.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!;
            Assert.AreEqual("Z-001", pending.Single().EmployeeCode);
            Assert.AreEqual("Benjamin Bašić", pending.Single().ActingForName);
            Assert.IsTrue((await damir.GetFromJsonAsync<MeDto>("/api/me"))!.IsApprover, "a deputy gets the approvals tab");
            Assert.IsNull((await benjamin.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!.Single().ActingForName);

            var review = (await damir.GetFromJsonAsync<SheetDto>("/api/approvals/Z-001/2026/9"))!;
            Assert.IsTrue(review.Workflow!.CanAct, "the deputy may decide");

            var approved = await damir.PostAsync("/api/approvals/Z-001/2026/9/approve", null);
            Assert.AreEqual(HttpStatusCode.OK, approved.StatusCode, await approved.Content.ReadAsStringAsync());
            var sheet = (await approved.ReadAsync<SheetDto>())!;
            Assert.AreEqual("Z-003", sheet.Workflow!.CurrentApprover, "it moved on to the next step, as if Benjamin had approved");
            var step = sheet.Workflow.History.Single(h => h.Kind == HistoryKind.StepApproved);
            Assert.AreEqual("Z-004", step.Actor);
            Assert.AreEqual("Z-002", step.OnBehalfOf, "the history says who decided and for whom");
            Assert.AreEqual(0, (await damir.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!.Count);
        }
    }

    [TestMethod]
    public async Task ADeputyWhoReturnsASheet_WritesTheMessageInTheNameOfTheApprover()
    {
        var (factory, amra, benjamin, _, damir, _) = await StartAsync();
        using (factory)
        {
            await Add(benjamin, "Z-004", "2026-10-01", "2026-10-15");
            await amra.PostAsync(Sept + "/submit", null);

            var returned = await damir.PostAsJsonAsync("/api/approvals/Z-001/2026/9/return", new ReturnRequest("Dodaj opis."));
            Assert.AreEqual(HttpStatusCode.OK, returned.StatusCode, await returned.Content.ReadAsStringAsync());

            var sheet = (await amra.GetFromJsonAsync<SheetDto>(Sept))!;
            Assert.AreEqual(SheetStatus.Returned, sheet.Status);
            Assert.AreEqual("Z-002", sheet.Workflow!.Messages.Single().From, "the employee answers to their supervisor, not to the deputy");
            Assert.AreEqual("Z-002", sheet.Workflow.History.Single(h => h.Kind == HistoryKind.Returned).OnBehalfOf);
        }
    }

    [TestMethod]
    public async Task OutsideThePeriod_TheDeputyIsNobody()
    {
        var (factory, amra, benjamin, _, damir, time) = await StartAsync();
        using (factory)
        {
            await Add(benjamin, "Z-004", "2026-10-05", "2026-10-10");
            await amra.PostAsync(Sept + "/submit", null);

            Assert.AreEqual(0, (await damir.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!.Count, "not yet");
            Assert.AreEqual(HttpStatusCode.Forbidden, (await damir.GetAsync("/api/approvals/Z-001/2026/9")).StatusCode);
            Assert.AreEqual("forbidden", await ErrorCode(await damir.PostAsync("/api/approvals/Z-001/2026/9/approve", null), HttpStatusCode.Forbidden));

            time.Now = time.Now.AddDays(4);
            Assert.AreEqual(1, (await damir.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!.Count, "from the fifth");

            time.Now = time.Now.AddDays(7);
            Assert.AreEqual(0, (await damir.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!.Count, "after the tenth");
            Assert.AreEqual(HttpStatusCode.Forbidden, (await damir.PostAsync("/api/approvals/Z-001/2026/9/approve", null)).StatusCode);
        }
    }

    [TestMethod]
    public async Task ADeputy_CannotApproveTheirOwnTimesheet()
    {
        var (factory, _, benjamin, _, damir, _) = await StartAsync();
        using (factory)
        {
            await Add(benjamin, "Z-004", "2026-10-01", "2026-10-15");
            await damir.PostAsync(Sept + "/submit", null);

            Assert.AreEqual(0, (await damir.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!.Count, "their own sheet is not on their list of things to approve");

            var refused = await damir.PostAsync("/api/approvals/Z-004/2026/9/approve", null);
            Assert.AreEqual("self-approval", await ErrorCode(refused, HttpStatusCode.Forbidden));
        }
    }

    [TestMethod]
    public async Task ThePeriods_AreChecked_AndOnlyTheApproverOrTheAdministratorRemovesOne()
    {
        var (factory, _, benjamin, emir, damir, _) = await StartAsync();
        using (factory)
        {
            Assert.AreEqual("invalid-deputy", await ErrorCode(await Add(benjamin, "Z-002", "2026-10-01", "2026-10-15"), HttpStatusCode.BadRequest));
            Assert.AreEqual("invalid-period", await ErrorCode(await Add(benjamin, "Z-004", "2026-10-15", "2026-10-01"), HttpStatusCode.BadRequest));
            Assert.AreEqual("invalid-period", await ErrorCode(await Add(benjamin, "Z-004", "2026-09-01", "2026-09-20"), HttpStatusCode.BadRequest), "already over");
            Assert.AreEqual("invalid-period", await ErrorCode(await Add(benjamin, "Z-004", "2026-10-01", "2028-10-01"), HttpStatusCode.BadRequest), "longer than a year");
            Assert.AreEqual(HttpStatusCode.NotFound, (await Add(benjamin, "Z-999", "2026-10-01", "2026-10-15")).StatusCode);

            var added = (await (await Add(benjamin, "Z-004", "2026-10-01", "2026-10-15")).ReadAsync<DeputyDto>())!;
            Assert.AreEqual("overlap", await ErrorCode(await Add(benjamin, "Z-003", "2026-10-10", "2026-10-20"), HttpStatusCode.Conflict));
            Assert.AreEqual(HttpStatusCode.OK, (await Add(benjamin, "Z-003", "2026-10-16", "2026-10-20")).StatusCode, "the next period does not overlap");

            var mine = (await benjamin.GetFromJsonAsync<DeputiesDto>("/api/deputies"))!;
            Assert.AreEqual(2, mine.Mine.Count);
            Assert.AreEqual("Damir Dizdarević", mine.Mine[0].DeputyName);
            Assert.AreEqual(1, (await damir.GetFromJsonAsync<DeputiesDto>("/api/deputies"))!.ForOthers.Count, "the deputy sees whom they stand in for");

            var candidates = (await benjamin.GetFromJsonAsync<List<PersonDto>>("/api/deputies/candidates"))!;
            Assert.IsFalse(candidates.Any(c => c.Code == "Z-002"), "not oneself");
            Assert.IsTrue(candidates.Any(c => c.Code == "Z-004"));

            Assert.AreEqual(HttpStatusCode.Forbidden, (await emir.DeleteAsync($"/api/deputies/{added.Id}")).StatusCode, "not their deputy to remove");
            Assert.AreEqual(HttpStatusCode.OK, (await benjamin.DeleteAsync($"/api/deputies/{added.Id}")).StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, (await benjamin.DeleteAsync($"/api/deputies/{added.Id}")).StatusCode);
        }
    }

    [TestMethod]
    public async Task TheAdministrator_CanSetAndRemoveADeputyForAnApproverWhoCannot()
    {
        var (factory, amra, _, _, damir, _) = await StartAsync();
        using (factory)
        {
            var response = await factory.Admin.PostAsJsonAsync("/api/admin/employees/Z-002/deputies", new AddDeputyRequest("Z-004", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 15)));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            var deputy = (await response.ReadAsync<DeputyDto>())!;
            Assert.AreEqual("Z-002", deputy.ApproverCode);

            await amra.PostAsync(Sept + "/submit", null);
            Assert.AreEqual(1, (await damir.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!.Count);

            Assert.AreEqual(1, (await factory.Admin.GetFromJsonAsync<List<DeputyDto>>("/api/admin/deputies"))!.Count);
            Assert.AreEqual(HttpStatusCode.OK, (await factory.Admin.DeleteAsync($"/api/deputies/{deputy.Id}")).StatusCode);
            Assert.AreEqual(0, (await damir.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!.Count);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await amra.GetAsync("/api/admin/deputies")).StatusCode);
        }
    }
}
