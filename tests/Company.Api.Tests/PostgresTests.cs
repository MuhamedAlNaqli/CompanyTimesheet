using System.Net;
using System.Net.Http.Json;
using Company.Api.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Company.Api.Data;
using Company.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Timesheet.Domain;

namespace Company.Api.Tests;

/// <summary>
/// The same behaviour on PostgreSQL, the database of production. The rest of the suite runs on SQLite, which does not enforce
/// the length of a text, treats dates differently and has no row version, so these tests are the only place where the real
/// database is met. They run when the environment variable COMPANY_API_TEST_POSTGRES holds a connection string of a user who
/// may create databases (for example "Host=localhost;Username=postgres;Password=...;Database=postgres"); each test makes a
/// database of its own and drops it. Otherwise they are reported as skipped.
/// </summary>
[TestClass]
public class PostgresTests
{
    private const string Variable = "COMPANY_API_TEST_POSTGRES";

    private static async Task<(ApiFactory Factory, string Database, string AdminConnection)> StartAsync()
    {
        var admin = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(admin)) Assert.Inconclusive($"Set {Variable} to run the tests on PostgreSQL.");

        var database = "company_test_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var target = new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;
        var factory = new ApiFactory(new Dictionary<string, string?> { ["Database:Provider"] = "postgres", ["ConnectionStrings:Default"] = target });
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");
        await factory.LinkAsync("Z-002", @"corp\benjamin");
        await factory.UpdateAsync("Z-001", new(null, null, "Z-002", null, null, null, null, null));
        return (factory, database, admin);
    }

    private static async Task DropAsync(ApiFactory factory, string database, string admin)
    {
        factory.Dispose();
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    [TestMethod]
    public async Task TheMigrationsBuildTheDatabase_AndTheWholeWorkflowRunsOnIt()
    {
        var (factory, database, admin) = await StartAsync();
        try
        {
            var amra = factory.As(@"corp\amra");
            var benjamin = factory.As(@"corp\benjamin");
            const string url = "/api/timesheet/2026/9";

            await amra.PostAsync(url + "/fill-standard-days", null);
            var submitted = await (await amra.PostAsync(url + "/submit", null)).ReadAsync<SheetDto>();
            Assert.AreEqual(SheetStatus.Submitted, submitted!.Status);
            Assert.AreEqual(DateTimeKind.Utc, submitted.Workflow!.Revisions.Single().SubmittedUtc.Kind);

            var pending = (await benjamin.GetFromJsonAsync<List<PendingApprovalDto>>("/api/approvals"))!;
            Assert.AreEqual("Z-001", pending.Single().EmployeeCode, "waiting for Benjamin: one indexed query");

            await benjamin.PostAsJsonAsync("/api/approvals/Z-001/2026/9/return", new ReturnRequest("Ispravi"));
            await amra.PostAsJsonAsync(url + "/messages/0/answer", new AnswerRequest("Gotovo"));
            await amra.PostAsync(url + "/submit", null);
            var approved = await (await benjamin.PostAsync("/api/approvals/Z-001/2026/9/approve", null)).ReadAsync<SheetDto>();
            Assert.AreEqual(SheetStatus.Approved, approved!.Status);
            Assert.AreEqual(2, approved.Revision);

            var revision = await (await benjamin.GetAsync("/api/approvals/Z-001/2026/9/revisions/1")).ReadAsync<SheetDto>();
            Assert.IsFalse(revision!.Editable);
        }
        finally
        {
            await DropAsync(factory, database, admin);
        }
    }

    [TestMethod]
    public async Task TextsAtTheLimitAreSaved_AndTextsOverItAreRefusedBeforeTheDatabaseSeesThem()
    {
        var (factory, database, admin) = await StartAsync();
        try
        {
            var amra = factory.As(@"corp\amra");
            var atLimit = await amra.PostAsJsonAsync("/api/timesheet/2026/9/overtime",
                new OvertimeRequest(new DateOnly(2026, 9, 7), new TimeOnly(18, 0), new TimeOnly(20, 0), new string('x', 500)));
            Assert.AreEqual(HttpStatusCode.OK, atLimit.StatusCode, "500 characters fit the column");

            var over = await amra.PostAsJsonAsync("/api/timesheet/2026/9/overtime",
                new OvertimeRequest(new DateOnly(2026, 9, 8), new TimeOnly(18, 0), new TimeOnly(20, 0), new string('x', 501)));
            Assert.AreEqual(HttpStatusCode.BadRequest, over.StatusCode, "PostgreSQL would have answered 22001 and the server a 500");
        }
        finally
        {
            await DropAsync(factory, database, admin);
        }
    }

    [TestMethod]
    public async Task OvertimeAcrossTheTurnOfTheMonth_AndRacingEdits_BehaveOnPostgreSQL()
    {
        var (factory, database, admin) = await StartAsync();
        try
        {
            var amra = factory.As(@"corp\amra");
            await amra.PostAsJsonAsync("/api/timesheet/2026/9/overtime", new OvertimeRequest(new DateOnly(2026, 9, 30), new TimeOnly(23, 0), new TimeOnly(1, 0), "Ispravka"));
            var october = await amra.GetFromJsonAsync<SheetDto>("/api/timesheet/2026/10");
            Assert.AreEqual(60, october!.Summary.OvertimeMinutes);

            var opens = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => amra.GetAsync("/api/timesheet/2026/11")));
            Assert.IsTrue(opens.All(r => r.StatusCode == HttpStatusCode.OK), "four first openings of a new month at once");

            var edits = await Task.WhenAll(Enumerable.Range(1, 6).Select(day =>
                amra.PutAsJsonAsync($"/api/timesheet/2026/11/days/2026-11-{day + 1:00}", new SetEntryRequest(Codes.RegularWork, 480))));
            Assert.IsTrue(edits.All(r => r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict), string.Join(",", edits.Select(r => (int)r.StatusCode)));
        }
        finally
        {
            await DropAsync(factory, database, admin);
        }
    }

    [TestMethod]
    public async Task ADatabaseOfTheFirstVersion_IsUpgraded_AndItsWaitingSheetsKeepTheirChain()
    {
        var admin = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(admin)) Assert.Inconclusive($"Set {Variable} to run the tests on PostgreSQL.");

        var database = "company_test_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var target = new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;
        try
        {
            await using (var db = new CompanyDbContext(new DbContextOptionsBuilder<CompanyDbContext>().UseNpgsql(target).Options))
            {
                // The database as the first release made it, with a timesheet that waits at the second step of a chain of two
                await db.GetService<IMigrator>().MigrateAsync("20261031211941_InitialCreate");
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO employees (\"Code\", \"Name\", \"IsActive\", \"IsFinalApprover\", \"IsAdmin\", \"IsFinance\") VALUES ('Z-001','Amra',true,false,false,false),('Z-002','Benjamin',true,false,false,false),('Z-003','Emir',true,true,false,false)");
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO month_sheets (\"EmployeeCode\", \"Year\", \"Month\", \"Status\", \"Revision\", \"CreatedUtc\", \"UpdatedUtc\", \"Version\", \"Chain\", \"Step\") VALUES ('Z-001', 2026, 9, 'Submitted', 1, now(), now(), 3, 'Z-002,Z-003', 1)");
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO sheet_revisions (\"SheetId\", \"Revision\", \"SubmittedUtc\", \"Json\") SELECT \"Id\", 1, now(), '{{}}' FROM month_sheets");
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO audit_events (\"AtUtc\", \"Actor\", \"Action\", \"EntityType\", \"EntityId\") VALUES (now(), 'Z-001', 'Old', 'MonthSheet', '1')");

                // ... and the current version on top of it
                await db.Database.MigrateAsync();
            }

            await using (var db = new CompanyDbContext(new DbContextOptionsBuilder<CompanyDbContext>().UseNpgsql(target).Options))
            {
                var sheet = await db.Sheets.WithDetails().SingleAsync();
                CollectionAssert.AreEqual(new[] { "Z-002", "Z-003" }, sheet.ChainCodes.ToArray(), "the chain, in its order");
                Assert.AreEqual("Z-003", sheet.CurrentApproverCode, "the step the sheet is at");
                Assert.AreEqual(1, (await db.SheetRevisions.SingleAsync()).SchemaVersion, "an old revision is of layout 1");

                // The log written before the chain existed stays as it is, and what comes after it is chained
                var old = await db.AuditEvents.SingleAsync();
                Assert.IsNull(old.Hash);
                Assert.IsTrue((await AuditChain.VerifyAsync(db)).IsIntact);
                db.AuditEvents.Add(new AuditEvent { AtUtc = DateTime.UtcNow, Actor = "Z-001", Action = "New", EntityType = "MonthSheet", EntityId = "1" });
                await db.SaveChangesAsync();
                var result = await AuditChain.VerifyAsync(db);
                Assert.IsTrue(result.IsIntact, result.Problem);
                Assert.AreEqual(1, result.Checked);
                Assert.AreEqual(1, result.Unchained);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
