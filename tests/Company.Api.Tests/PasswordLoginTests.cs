using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Company.Api.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Company.Api.Tests;

/// <summary>Sign-in with a user name and password that the administrator creates (the default way in), and Windows sign-in switched off</summary>
[TestClass]
public class PasswordLoginTests
{
    private const string AdminPassword = "First-password-1";

    private static ApiFactory Start(MovableTime? time = null) => new(new Dictionary<string, string?>
    {
        ["Authentication:Mode"] = "Password",
        ["Bootstrap:AdminUsername"] = "admin",
        ["Bootstrap:AdminPassword"] = AdminPassword
    }) { Time = time ?? new MovableTime(2026, 10, 2) };

    private static HttpClient Anonymous(ApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiHeaders.RequestedWith, ApiHeaders.RequestedWithValue);
        return client;
    }

    private static async Task<(HttpClient Client, LoginResultDto Result)> SignIn(ApiFactory factory, string username, string password, bool remember = false)
    {
        var client = Anonymous(factory);
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password, remember));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<LoginResultDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Token);
        return (client, result);
    }

    private static async Task<string> ErrorCode(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code;
    }

    /// <summary>The administrator is signed in and has chosen their own password; the small list of people is imported</summary>
    private static async Task<HttpClient> AdminAsync(ApiFactory factory)
    {
        var (first, result) = await SignIn(factory, "admin", AdminPassword);
        Assert.IsTrue(result.MustChangePassword, "the first password is the administrator's to replace");
        var changed = await first.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(AdminPassword, "Admin-own-password-9"));
        Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode, await changed.Content.ReadAsStringAsync());

        var (admin, _) = await SignIn(factory, "admin", "Admin-own-password-9");
        var import = await admin.PostAsJsonAsync("/api/admin/employees/import", new CsvRequest(ApiFactory.SmallList));
        Assert.AreEqual(HttpStatusCode.OK, import.StatusCode, await import.Content.ReadAsStringAsync());
        return admin;
    }

    [TestMethod]
    public async Task TheFirstAdministrator_MustChooseAPasswordBeforeAnythingElse()
    {
        using var factory = Start();
        var (client, result) = await SignIn(factory, "admin", AdminPassword);
        Assert.IsTrue(result.MustChangePassword);

        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode, "who am I is allowed");
        Assert.AreEqual("password-change-required", await ErrorCode(await client.GetAsync("/api/admin/employees"), HttpStatusCode.Forbidden));
        Assert.AreEqual("password-change-required", await ErrorCode(await client.GetAsync("/api/reminders"), HttpStatusCode.Forbidden));

        Assert.AreEqual("weak-password", await ErrorCode(await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(AdminPassword, "short")), HttpStatusCode.BadRequest));
        Assert.AreEqual("wrong-password", await ErrorCode(await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("not it at all", "Another-long-one-1")), HttpStatusCode.BadRequest));
        Assert.AreEqual("same-password", await ErrorCode(await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(AdminPassword, AdminPassword)), HttpStatusCode.BadRequest));

        Assert.AreEqual(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(AdminPassword, "Admin-own-password-9"))).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/admin/employees")).StatusCode, "the same session works after the change");

        var again = await Anonymous(factory).PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", AdminPassword));
        Assert.AreEqual("invalid-credentials", await ErrorCode(again, HttpStatusCode.Unauthorized), "the first password no longer works");
    }

    [TestMethod]
    public async Task TheAdministratorCreatesAnEmployeesSignIn_AndTheEmployeeChoosesTheirOwnPassword()
    {
        using var factory = Start();
        var admin = await AdminAsync(factory);

        var set = await admin.PutAsJsonAsync("/api/admin/employees/Z-001/credentials", new SetCredentialsRequest("Amra.A", "Start-password-1", MustChange: true));
        Assert.AreEqual(HttpStatusCode.OK, set.StatusCode, await set.Content.ReadAsStringAsync());

        var (amra, login) = await SignIn(factory, "AMRA.a", "Start-password-1");
        Assert.IsTrue(login.MustChangePassword);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await amra.GetAsync("/api/timesheet/2026/9")).StatusCode);
        await amra.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("Start-password-1", "Amra-own-password-7"));

        var me = (await amra.GetFromJsonAsync<MeDto>("/api/me"))!;
        Assert.AreEqual("Z-001", me.Code);
        Assert.AreEqual(HttpStatusCode.OK, (await amra.GetAsync("/api/timesheet/2026/9")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await amra.GetAsync("/api/admin/employees")).StatusCode, "an employee is not an administrator");

        var list = (await admin.GetFromJsonAsync<List<CredentialDto>>("/api/admin/credentials"))!;
        Assert.AreEqual("amra.a", list.Single(c => c.EmployeeCode == "Z-001").Username);
        Assert.IsFalse(list.Single(c => c.EmployeeCode == "Z-001").MustChangePassword);
    }

    [TestMethod]
    public async Task AWrongPasswordIsRefusedTheSameWayAsAnUnknownName_AndFiveInARowLockTheAccount()
    {
        using var factory = Start();
        var admin = await AdminAsync(factory);
        await admin.PutAsJsonAsync("/api/admin/employees/Z-001/credentials", new SetCredentialsRequest("amra", "Start-password-1", false));

        var anonymous = Anonymous(factory);
        var unknown = await ErrorCode(await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody", "whatever-it-is")), HttpStatusCode.Unauthorized);
        var wrong = await ErrorCode(await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest("amra", "wrong-password-1")), HttpStatusCode.Unauthorized);
        Assert.AreEqual(unknown, wrong, "the answer does not tell which names exist");

        for (var i = 0; i < 4; i++) await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest("amra", "wrong-password-1"));
        Assert.AreEqual("locked", await ErrorCode(await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest("amra", "Start-password-1")), HttpStatusCode.Locked),
            "even the right password is refused while the account is locked");

        // The administrator sets a new password: the lock is lifted
        await admin.PutAsJsonAsync("/api/admin/employees/Z-001/credentials", new SetCredentialsRequest("amra", "Fresh-password-2", false));
        await SignIn(factory, "amra", "Fresh-password-2");
    }

    [TestMethod]
    public async Task ALock_EndsByItself_AfterFifteenMinutes()
    {
        var time = new MovableTime(2026, 10, 2);
        using var factory = Start(time);
        var admin = await AdminAsync(factory);
        await admin.PutAsJsonAsync("/api/admin/employees/Z-001/credentials", new SetCredentialsRequest("amra", "Start-password-1", false));

        var anonymous = Anonymous(factory);
        for (var i = 0; i < 5; i++) await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest("amra", "wrong-password-1"));
        Assert.AreEqual(HttpStatusCode.Locked, (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest("amra", "Start-password-1"))).StatusCode);

        time.Now = time.Now.AddMinutes(16);
        await SignIn(factory, "amra", "Start-password-1");
    }

    [TestMethod]
    public async Task LoggingOut_EndsTheSession_AndASessionRunsOutByItself()
    {
        var time = new MovableTime(2026, 10, 2);
        using var factory = Start(time);
        var admin = await AdminAsync(factory);
        await admin.PutAsJsonAsync("/api/admin/employees/Z-001/credentials", new SetCredentialsRequest("amra", "Start-password-1", false));

        var (amra, _) = await SignIn(factory, "amra", "Start-password-1");
        Assert.AreEqual(HttpStatusCode.NoContent, (await amra.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await amra.GetAsync("/api/me")).StatusCode);

        var (short12, shortLogin) = await SignIn(factory, "amra", "Start-password-1");
        var (remembered, rememberedLogin) = await SignIn(factory, "amra", "Start-password-1", remember: true);
        Assert.IsTrue(rememberedLogin.ExpiresUtc - shortLogin.ExpiresUtc > TimeSpan.FromDays(29));

        time.Now = time.Now.AddHours(13);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await short12.GetAsync("/api/me")).StatusCode, "12 hours");
        Assert.AreEqual(HttpStatusCode.OK, (await remembered.GetAsync("/api/me")).StatusCode, "remembered for 30 days");
        time.Now = time.Now.AddDays(31);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await remembered.GetAsync("/api/me")).StatusCode);
    }

    [TestMethod]
    public async Task TheServerKeepsNeitherThePasswordNorTheToken()
    {
        using var factory = Start();
        var admin = await AdminAsync(factory);
        await admin.PutAsJsonAsync("/api/admin/employees/Z-001/credentials", new SetCredentialsRequest("amra", "Start-password-1", false));
        var (_, login) = await SignIn(factory, "amra", "Start-password-1");

        using var connection = new SqliteConnection($"Data Source={factory.DatabasePath}");
        connection.Open();
        string Scalar(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar()?.ToString() ?? "";
        }

        var hash = Scalar("SELECT PasswordHash FROM employee_credentials WHERE Username = 'amra'");
        StringAssert.StartsWith(hash, "v1.600000.");
        Assert.IsFalse(hash.Contains("Start-password-1"));
        Assert.AreEqual("0", Scalar($"SELECT COUNT(*) FROM auth_sessions WHERE TokenHash = '{login.Token}'"), "only the hash of the token is stored");
        Assert.AreEqual("1", Scalar("SELECT COUNT(*) FROM audit_events WHERE Action = 'SignedIn' AND Actor = 'Z-001'"));
    }

    [TestMethod]
    public async Task ChangingOrRemovingCredentials_EndsTheirSessions_AndTheRulesProtectTheCompany()
    {
        using var factory = Start();
        var admin = await AdminAsync(factory);
        await admin.PutAsJsonAsync("/api/admin/employees/Z-001/credentials", new SetCredentialsRequest("amra", "Start-password-1", false));
        await admin.PutAsJsonAsync("/api/admin/employees/Z-002/credentials", new SetCredentialsRequest("benjamin", "Start-password-2", false));
        var (amra, _) = await SignIn(factory, "amra", "Start-password-1");

        Assert.AreEqual("username-taken", await ErrorCode(await admin.PutAsJsonAsync("/api/admin/employees/Z-002/credentials", new SetCredentialsRequest("amra", "Start-password-2", false)), HttpStatusCode.Conflict));
        Assert.AreEqual("weak-password", await ErrorCode(await admin.PutAsJsonAsync("/api/admin/employees/Z-002/credentials", new SetCredentialsRequest("benjamin", "short", false)), HttpStatusCode.BadRequest));
        Assert.AreEqual("invalid-username", await ErrorCode(await admin.PutAsJsonAsync("/api/admin/employees/Z-002/credentials", new SetCredentialsRequest("be", "Start-password-2", false)), HttpStatusCode.BadRequest));
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/admin/employees/Z-999/credentials", new SetCredentialsRequest("ghost", "Start-password-2", false))).StatusCode);

        Assert.AreEqual(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/employees/Z-001/credentials", new SetCredentialsRequest("amra", "Other-password-3", false))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await amra.GetAsync("/api/me")).StatusCode, "a new password ends the old sessions");

        Assert.AreEqual("last-admin", await ErrorCode(await admin.DeleteAsync("/api/admin/employees/Z-000/credentials"), HttpStatusCode.Conflict));
        Assert.AreEqual(HttpStatusCode.OK, (await admin.DeleteAsync("/api/admin/employees/Z-002/credentials")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Anonymous(factory).PostAsJsonAsync("/api/auth/login", new LoginRequest("benjamin", "Start-password-2"))).StatusCode);
    }

    [TestMethod]
    public async Task ADeactivatedEmployee_CannotSignIn_AndTheOpenSessionStopsWorking()
    {
        using var factory = Start();
        var admin = await AdminAsync(factory);
        await admin.PutAsJsonAsync("/api/admin/employees/Z-001/credentials", new SetCredentialsRequest("amra", "Start-password-1", false));
        var (amra, _) = await SignIn(factory, "amra", "Start-password-1");

        Assert.AreEqual(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/employees/Z-001", new UpdateEmployeeRequest(null, false, null, null, null, null, null, null))).StatusCode);

        Assert.AreEqual("invalid-credentials", await ErrorCode(await Anonymous(factory).PostAsJsonAsync("/api/auth/login", new LoginRequest("amra", "Start-password-1")), HttpStatusCode.Unauthorized));
        Assert.AreEqual(HttpStatusCode.Forbidden, (await amra.GetAsync("/api/timesheet/2026/9")).StatusCode, "no longer a registered employee");
    }

    [TestMethod]
    public void WindowsAndEntraSignIn_AreSwitchedOffUntilTheFlagSaysOtherwise()
    {
        foreach (var mode in new[] { "Windows", "Entra", "Both" })
        {
            using var off = new ApiFactory(new Dictionary<string, string?>
            {
                ["Authentication:Mode"] = mode,
                ["Features:WindowsLogin"] = "false",
                ["Authentication:Entra:TenantId"] = "t",
                ["Authentication:Entra:ClientId"] = "c"
            });
            var ex = Assert.ThrowsException<InvalidOperationException>(() => off.CreateClient(), mode);
            StringAssert.Contains(ex.ToString(), "Features:WindowsLogin");
        }

        using var windows = new ApiFactory(new Dictionary<string, string?> { ["Authentication:Mode"] = "Windows", ["Features:WindowsLogin"] = "true" });
        Assert.IsNotNull(windows.CreateClient());
    }

    [TestMethod]
    public async Task ThePasswordSignIn_IsNotOfferedByAServerInAnotherMode()
    {
        using var factory = new ApiFactory();
        var response = await Anonymous(factory).PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", AdminPassword));
        Assert.IsTrue(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized, "the Test server has no login route: " + response.StatusCode);
    }
}
