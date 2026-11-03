using System.Net;
using Company.Api.Client;
using Company.Api.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Company.Api.Tests;

/// <summary>The typed client against the real API (in process): what the desktop app will call</summary>
[TestClass]
public class ClientTests
{
    private static async Task<(ApiFactory Factory, CompanyApiClient Client)> StartAsync(string account = @"corp\amra")
    {
        var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");
        return (factory, new CompanyApiClient(factory.As(account)));
    }

    [TestMethod]
    public async Task TheClient_ShouldWalkThroughAMonth()
    {
        var (factory, client) = await StartAsync();
        using (factory)
        {
            var me = await client.GetMeAsync();
            Assert.AreEqual("Z-001", me.Code);

            var codes = await client.GetTimeCodesAsync();
            Assert.AreEqual(22, codes.Count);

            var sheet = await client.FillStandardDaysAsync(2026, 9);
            Assert.AreEqual(176 * 60, sheet.Summary.FundMinutes);

            var request = new AbsenceRangeRequest(Codes.AnnualLeaveA, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15));
            var preview = await client.PreviewAbsenceAsync(2026, 9, request);
            Assert.AreEqual(11, preview.Days.Count);
            sheet = await client.ApplyAbsenceAsync(2026, 9, request);
            Assert.AreEqual(11, sheet.Summary.DaysByCode[Codes.AnnualLeaveA]);

            sheet = await client.AddOvertimeAsync(2026, 9, new OvertimeRequest(new DateOnly(2026, 9, 7), new TimeOnly(20, 0), new TimeOnly(22, 30), "Rad"));
            var id = sheet.Overtime.Single().Id;
            sheet = await client.UpdateOvertimeAsync(2026, 9, id, new OvertimeRequest(new DateOnly(2026, 9, 7), new TimeOnly(20, 0), new TimeOnly(21, 0), "Rad"));
            Assert.AreEqual(60, sheet.Overtime.Single().TotalMinutes);
            sheet = await client.DeleteOvertimeAsync(2026, 9, id);
            Assert.AreEqual(0, sheet.Overtime.Count);

            sheet = await client.SetEntryAsync(2026, 9, new DateOnly(2026, 9, 16), new SetEntryRequest(Codes.RegularWork, 360));
            Assert.AreEqual(360, sheet.Days.Single(d => d.Date.Day == 16).Entries.Single().Minutes);
            sheet = await client.ClearEntryAsync(2026, 9, new DateOnly(2026, 9, 16), Codes.RegularWork);
            Assert.AreEqual(480, sheet.Days.Single(d => d.Date.Day == 16).Entries.Single().Minutes, "the standard day comes back");

            Assert.AreEqual(2026, (await client.GetSheetAsync(2026, 9)).Year);
        }
    }

    [TestMethod]
    public async Task Errors_ShouldArriveAsApiExceptionsWithTheCodeOfTheServer()
    {
        var (factory, client) = await StartAsync();
        using (factory)
        {
            var invalid = await Assert.ThrowsExceptionAsync<ApiException>(() => client.SetEntryAsync(2026, 9, new DateOnly(2026, 9, 1), new SetEntryRequest(9999, 480)));
            Assert.AreEqual("unknown-code", invalid.Code);
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.Status);

            var month = await Assert.ThrowsExceptionAsync<ApiException>(() => client.GetSheetAsync(2026, 13));
            Assert.AreEqual("invalid-month", month.Code);
        }

        var (stranger, notLinked) = await StartAsync(@"corp\nobody");
        using (stranger)
        {
            var refused = await Assert.ThrowsExceptionAsync<ApiException>(() => notLinked.GetMeAsync());
            Assert.AreEqual("not-registered", refused.Code, "the desktop app can tell the user to ask the administrator");
        }
    }

    [TestMethod]
    public async Task ASignInTheServerDoesNotAccept_ShouldBeAnUnauthorizedException()
    {
        using var factory = new ApiFactory();
        var client = new CompanyApiClient(factory.CreateClient());

        var ex = await Assert.ThrowsExceptionAsync<ApiException>(() => client.GetMeAsync());
        Assert.AreEqual("unauthorized", ex.Code);
        Assert.AreEqual(HttpStatusCode.Unauthorized, ex.Status);
    }

    [TestMethod]
    public async Task AServerThatIsNotThere_ShouldBeReportedAsUnreachable()
    {
        var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1"), Timeout = TimeSpan.FromSeconds(3) };
        var ex = await Assert.ThrowsExceptionAsync<ApiException>(() => new CompanyApiClient(http).GetMeAsync());
        Assert.AreEqual("unreachable", ex.Code);
    }

    [TestMethod]
    public void PlainHttp_ShouldOnlyBeAllowedForThisComputer()
    {
        CompanyApiClient.EnsureSecure(new Uri("https://timesheet.company.local"));
        CompanyApiClient.EnsureSecure(new Uri("http://localhost:5055"));
        CompanyApiClient.EnsureSecure(new Uri("http://127.0.0.1:5055"));
        Assert.ThrowsException<InvalidOperationException>(() => CompanyApiClient.EnsureSecure(new Uri("http://timesheet.company.local")));
        Assert.ThrowsException<InvalidOperationException>(() => CompanyApiClient.EnsureSecure(new Uri("ftp://x")));
        Assert.ThrowsException<InvalidOperationException>(() => CompanyApiClient.CreateWindowsHttpClient(new Uri("http://10.0.0.5")));
    }
}
