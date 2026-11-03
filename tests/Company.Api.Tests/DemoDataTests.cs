using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Company.Api.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Company.Api.Tests;

/// <summary>The sample people of a test on one PC (Demo:Seed)</summary>
[TestClass]
public class DemoDataTests
{
    private static ApiFactory Start(bool seed = true) => new(new Dictionary<string, string?>
    {
        ["Authentication:Mode"] = "Password",
        ["Bootstrap:AdminUsername"] = "admin",
        ["Bootstrap:AdminPassword"] = "First-password-1",
        ["Demo:Seed"] = seed ? "true" : "false"
    });

    private static async Task<HttpClient> SignIn(ApiFactory factory, string user, string password = "Demo-password-1")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiHeaders.RequestedWith, ApiHeaders.RequestedWithValue);
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user, password));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var login = (await response.Content.ReadFromJsonAsync<LoginResultDto>())!;
        Assert.IsFalse(login.MustChangePassword, "the sample people can start at once");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        return client;
    }

    [TestMethod]
    public async Task TheSamplePeopleCanSignInAndHaveTheirMonthsAndTheirChart()
    {
        using var factory = Start();

        var tarik = await SignIn(factory, "tarik");
        var pending = (await tarik.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!;
        CollectionAssert.AreEquivalent(new[] { "Z-001", "Z-005" }, pending.Select(p => p.EmployeeCode).ToArray(), "Lejla and Maja submitted last month");

        var team = (await tarik.GetFromJsonAsync<TeamStatusDto>($"/api/team/{DateTime.Today.Year}/{DateTime.Today.Month}/status"))!;
        CollectionAssert.AreEquivalent(new[] { "Z-001", "Z-004", "Z-005" }, team.Members.Select(m => m.Code).ToArray());

        var last = DateTime.Today.AddMonths(-1);
        var lejla = await SignIn(factory, "lejla");
        var sheet = (await lejla.GetFromJsonAsync<SheetDto>($"/api/timesheet/{last.Year}/{last.Month}"))!;
        Assert.AreEqual(SheetStatus.Submitted, sheet.Status);
        Assert.AreEqual(1, sheet.Overtime.Count);

        // The administrator of the first start is still there and must choose their own password
        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add(ApiHeaders.RequestedWith, ApiHeaders.RequestedWithValue);
        var adminLogin = (await (await admin.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "First-password-1"))).Content.ReadFromJsonAsync<LoginResultDto>())!;
        Assert.IsTrue(adminLogin.MustChangePassword);
    }

    [TestMethod]
    public async Task WithoutTheSetting_OrOnADatabaseThatAlreadyHasPeople_NothingIsCreated()
    {
        using (var off = Start(seed: false))
        {
            var client = off.CreateClient();
            client.DefaultRequestHeaders.Add(ApiHeaders.RequestedWith, ApiHeaders.RequestedWithValue);
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("lejla", "Demo-password-1"))).StatusCode);
        }

        using var factory = Start();
        await SignIn(factory, "lejla");
        // A second start on the same database keeps what is there: nothing is created twice (the unique user names would refuse it)
        using var again = new ApiFactory(new Dictionary<string, string?>
        {
            ["Authentication:Mode"] = "Password",
            ["Demo:Seed"] = "true",
            ["ConnectionStrings:Default"] = $"Data Source={factory.DatabasePath}"
        });
        await SignIn(again, "tarik");
    }
}
