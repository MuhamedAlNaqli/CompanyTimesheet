using System.Net.Http.Json;
using Company.Api.Auth;
using Company.Api.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Company.Api.Tests;

/// <summary>The API on a fresh SQLite file with the development sign-in (the account comes from the X-Test-User header)</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The SQLite file of this server, for tests that look at (or tamper with) the database itself</summary>
    public string DatabasePath => _database;

    private readonly string _database = Path.Combine(Path.GetTempPath(), $"company-api-test-{Guid.NewGuid():N}.db");
    private readonly Dictionary<string, string?> _settings;

    public ApiFactory(Dictionary<string, string?>? settings = null)
    {
        _settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "sqlite",
            ["ConnectionStrings:Default"] = $"Data Source={_database}",
            ["Authentication:Mode"] = "Test",
            ["Features:WindowsLogin"] = "true", // Windows and Entra sign-in are tested; the default of the product is off
            ["Features:SheetExport"] = "true", // the file tests; the tests of the switched-off product say "false"
            ["Features:FinanceAndLegal"] = "true", // the finance and legal tests; the tests of the switched-off product say "false"
            ["Holidays:SeedFixed"] = "false", // the holiday tests count what they add; the seeding has tests of its own
            ["Bootstrap:AdminCode"] = "Z-000",
            ["Bootstrap:AdminAccounts:0"] = @"dev\admin"
        };
        if (settings != null) foreach (var pair in settings) _settings[pair.Key] = pair.Value;
    }

    /// <summary>The clock of the server; set before the first request</summary>
    public TimeProvider Time { get; set; } = TimeProvider.System;

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services => services.AddSingleton(Time));
        foreach (var pair in _settings) builder.UseSetting(pair.Key, pair.Value);
    }

    public HttpClient As(string account)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(AuthenticationSetup.TestHeader, account);
        client.DefaultRequestHeaders.Add(ApiHeaders.RequestedWith, ApiHeaders.RequestedWithValue);
        return client;
    }

    public HttpClient Admin => As(@"dev\admin");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var file in new[] { _database, _database + "-shm", _database + "-wal" })
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* temp file */ }
        }
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------

    public async Task<ImportResultDto> ImportAsync(string csv)
    {
        var response = await Admin.PostAsJsonAsync("/api/admin/employees/import", new CsvRequest(csv));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
    }

    public async Task LinkAsync(string code, string account)
    {
        var response = await Admin.PostAsJsonAsync($"/api/admin/employees/{code}/accounts", new AccountRequest(account));
        response.EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> UpdateAsync(string code, UpdateEmployeeRequest request) =>
        await Admin.PutAsJsonAsync($"/api/admin/employees/{code}", request);

    public const string SmallList = "Br.,Ime i prezime\nZ-001,Amra Adžamija\nZ-002,Benjamin Bašić\nZ-003,Emir Ferhatbegović\nZ-004,Damir Dizdarević\n";
}

internal static class Json
{
    public static Task<T?> ReadAsync<T>(this HttpResponseMessage response) => response.Content.ReadFromJsonAsync<T>();
}
