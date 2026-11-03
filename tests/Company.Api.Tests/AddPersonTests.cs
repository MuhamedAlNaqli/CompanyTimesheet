using System.Net;
using System.Net.Http.Json;
using Company.Api.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Company.Api.Tests;

/// <summary>The administrator adds a person, and an administrator is not reminded about timesheets that are not theirs to write</summary>
[TestClass]
public class AddPersonTests
{
    private static async Task<(HttpStatusCode Status, string? Code)> Refusal(HttpResponseMessage response) =>
        (response.StatusCode, response.IsSuccessStatusCode ? null : (await response.ReadAsync<ApiErrorDto>())!.Code);

    [TestMethod]
    public async Task ANewPerson_GetsTheNextFreeCode_WhenNoneIsGiven()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);

        var response = await factory.Admin.PostAsJsonAsync("/api/admin/employees", new CreateEmployeeRequest(null, "  Nova Osoba ", "Z-001", "nova@firma.ba"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var created = (await response.ReadAsync<EmployeeDto>())!;

        Assert.AreEqual("Z-005", created.Code, "SmallList has Z-001 to Z-004");
        Assert.AreEqual("Nova Osoba", created.Name);
        Assert.AreEqual("Z-001", created.SupervisorCode);
        Assert.AreEqual("nova@firma.ba", created.Email);
        Assert.IsTrue(created.IsActive);

        var listed = await factory.Admin.GetFromJsonAsync<List<EmployeeDto>>("/api/admin/employees");
        Assert.IsTrue(listed!.Any(e => e.Code == "Z-005"));
    }

    [TestMethod]
    public async Task ACodeThatIsGiven_IsUsed_ButNotTwice_AndMustLookLikeACode()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);

        Assert.AreEqual(HttpStatusCode.OK, (await factory.Admin.PostAsJsonAsync("/api/admin/employees", new CreateEmployeeRequest("X-77", "Ručni Kod"))).StatusCode);

        Assert.AreEqual((HttpStatusCode.Conflict, "code-taken"), await Refusal(await factory.Admin.PostAsJsonAsync("/api/admin/employees", new CreateEmployeeRequest("Z-001", "Dupli"))));
        Assert.AreEqual((HttpStatusCode.BadRequest, "invalid-code"), await Refusal(await factory.Admin.PostAsJsonAsync("/api/admin/employees", new CreateEmployeeRequest("Z 9/", "Loš kod"))));
        Assert.AreEqual((HttpStatusCode.BadRequest, "too-long"), await Refusal(await factory.Admin.PostAsJsonAsync("/api/admin/employees", new CreateEmployeeRequest("Z-1234567890", "Predug kod"))));
    }

    [TestMethod]
    public async Task ANewPerson_NeedsAName_AKnownSupervisor_AndAnEmailThatLooksLikeOne()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);

        Assert.AreEqual((HttpStatusCode.BadRequest, "name-required"), await Refusal(await factory.Admin.PostAsJsonAsync("/api/admin/employees", new CreateEmployeeRequest(null, "   "))));
        Assert.AreEqual((HttpStatusCode.BadRequest, "unknown-person"), await Refusal(await factory.Admin.PostAsJsonAsync("/api/admin/employees", new CreateEmployeeRequest(null, "Neko", "Z-404"))));
        Assert.AreEqual((HttpStatusCode.BadRequest, "invalid-email"), await Refusal(await factory.Admin.PostAsJsonAsync("/api/admin/employees", new CreateEmployeeRequest(null, "Neko", null, "nije adresa"))));

        var all = await factory.Admin.GetFromJsonAsync<List<EmployeeDto>>("/api/admin/employees");
        Assert.AreEqual(5, all!.Count, "nothing was added by the refused calls (the administrator and the four of the list)");
    }

    [TestMethod]
    public async Task OnlyAnAdministrator_MayAddAPerson()
    {
        using var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");
        using var amra = factory.As(@"corp\amra");

        Assert.AreEqual(HttpStatusCode.Forbidden, (await amra.PostAsJsonAsync("/api/admin/employees", new CreateEmployeeRequest(null, "Neko"))).StatusCode);
    }

    [TestMethod]
    public async Task AnAdministrator_HasNoTimesheetReminders()
    {
        using var factory = new ApiFactory();
        var me = await factory.Admin.GetFromJsonAsync<MeDto>("/api/me");
        Assert.IsTrue(me!.IsAdmin);

        var reminders = await factory.Admin.GetFromJsonAsync<RemindersDto>("/api/reminders");
        Assert.AreEqual(0, reminders!.Items.Count, "an administrator only administers");
    }
}
