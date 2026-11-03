using System.Net;
using System.Net.Http.Json;
using Company.Api.Contracts;
using Company.Api.Data;
using Company.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Company.Api.Tests;

/// <summary>The structure the review asked for: the chain as a table, the version counted in one place, the audit log as a chain of hashes, revisions with a layout version</summary>
[TestClass]
public class StructureTests
{
    private static async Task<(ApiFactory Factory, HttpClient Amra, HttpClient Benjamin)> StartAsync()
    {
        var factory = new ApiFactory();
        await factory.ImportAsync(ApiFactory.SmallList);
        await factory.LinkAsync("Z-001", @"corp\amra");
        await factory.LinkAsync("Z-002", @"corp\benjamin");
        await factory.LinkAsync("Z-003", @"corp\emir");
        await factory.UpdateAsync("Z-001", new(null, null, "Z-002", null, null, null, null, null));
        await factory.UpdateAsync("Z-002", new(null, null, "Z-003", null, null, null, null, null));
        await factory.UpdateAsync("Z-003", new(null, null, null, true, null, null, null, null));
        return (factory, factory.As(@"corp\amra"), factory.As(@"corp\benjamin"));
    }

    private static long Scalar(ApiFactory factory, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={factory.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void Execute(ApiFactory factory, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={factory.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // ---- the chain is a table ---------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task TheChainOfASubmittedSheet_IsStoredAsOrderedRows()
    {
        var (factory, amra, benjamin) = await StartAsync();
        using (factory)
        {
            await amra.PostAsync("/api/timesheet/2026/9/fill-standard-days", null);
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);

            using var db = Open(factory);
            var sheet = await db.Sheets.WithDetails().SingleAsync();
            CollectionAssert.AreEqual(new[] { "Z-002", "Z-003" }, sheet.ChainCodes.ToArray());
            CollectionAssert.AreEqual(new[] { 0, 1 }, sheet.ChainSteps.OrderBy(s => s.Position).Select(s => s.Position).ToArray());
            Assert.AreEqual("Z-002", sheet.CurrentApproverCode);

            await benjamin.PostAsync("/api/approvals/Z-001/2026/9/approve", null);
            Assert.AreEqual("Z-003", (await db.Sheets.AsNoTracking().SingleAsync()).CurrentApproverCode);

            // A step of the chain is a person of the company: the database refuses anything else
            Assert.ThrowsException<SqliteException>(() => Execute(factory, "INSERT INTO sheet_chain_steps (SheetId, Position, EmployeeCode) VALUES (1, 7, 'Z-999')"));
        }
    }

    [TestMethod]
    public async Task ASheetBelongsToAnEmployeeOfTheCompany()
    {
        var (factory, _, _) = await StartAsync();
        using (factory)
        {
            Assert.ThrowsException<SqliteException>(() => Execute(factory,
                "INSERT INTO month_sheets (EmployeeCode, Year, Month, Status, Revision, CreatedUtc, UpdatedUtc, Version, Step) VALUES ('Z-404', 2026, 9, 'Draft', 0, '2026-09-01', '2026-09-01', 0, 0)"));
        }
    }

    // ---- the version is counted by the context ----------------------------------------------------------------------------------

    [TestMethod]
    public async Task AChangeToAnythingOfASheet_CountsItsVersion_WithoutTheCodeAskingFor_It()
    {
        var (factory, amra, _) = await StartAsync();
        using (factory)
        {
            await amra.GetAsync("/api/timesheet/2026/9");

            await using var first = Open(factory);
            await using var second = Open(factory);
            var a = await first.Sheets.WithDetails().SingleAsync();
            var b = await second.Sheets.WithDetails().SingleAsync();
            var before = a.Version;

            // Directly, the way a future piece of code might do it: no Touch, no call of any service
            a.Entries.Add(new SheetEntry { Date = new DateOnly(2026, 9, 5), Code = Codes.RegularWork, Minutes = 480 });
            await first.SaveChangesAsync();
            Assert.AreEqual(before + 1, a.Version);

            b.Entries.Add(new SheetEntry { Date = new DateOnly(2026, 9, 6), Code = Codes.RegularWork, Minutes = 480 });
            await Assert.ThrowsExceptionAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }
    }

    // ---- the audit log is a chain -----------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task TheAuditLogIsAChain_AndAChangeBehindTheApplicationsBackIsFound()
    {
        var (factory, amra, benjamin) = await StartAsync();
        using (factory)
        {
            await amra.PostAsync("/api/timesheet/2026/9/fill-standard-days", null);
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);
            await benjamin.PostAsync("/api/approvals/Z-001/2026/9/approve", null);

            var intact = (await factory.Admin.GetFromJsonAsync<AuditVerificationDto>("/api/admin/audit/verify"))!;
            Assert.IsTrue(intact.IsIntact, intact.Problem);
            Assert.IsTrue(intact.Checked > 10, "everything written so far is part of the chain");
            Assert.AreEqual(Scalar(factory, "SELECT COUNT(*) FROM audit_events WHERE Hash IS NOT NULL"), intact.Checked);

            // A database administrator changes the text of an old event
            var id = Scalar(factory, "SELECT Id FROM audit_events WHERE Action = 'Submitted'");
            Execute(factory, $"UPDATE audit_events SET Details = 'nothing happened' WHERE Id = {id}");
            var changed = (await factory.Admin.GetFromJsonAsync<AuditVerificationDto>("/api/admin/audit/verify"))!;
            Assert.IsFalse(changed.IsIntact);
            Assert.AreEqual(id, changed.FirstBrokenId);
        }
    }

    [TestMethod]
    public async Task ARemovedEventBreaksTheChainAtTheEventAfterIt()
    {
        var (factory, amra, _) = await StartAsync();
        using (factory)
        {
            await amra.PutAsJsonAsync("/api/timesheet/2026/9/days/2026-09-01", new SetEntryRequest(Codes.RegularWork, 240));
            await amra.PutAsJsonAsync("/api/timesheet/2026/9/days/2026-09-02", new SetEntryRequest(Codes.RegularWork, 240));

            var middle = Scalar(factory, "SELECT Id FROM audit_events WHERE Action = 'EntrySet' ORDER BY Id LIMIT 1");
            Execute(factory, $"DELETE FROM audit_events WHERE Id = {middle}");

            var result = (await factory.Admin.GetFromJsonAsync<AuditVerificationDto>("/api/admin/audit/verify"))!;
            Assert.IsFalse(result.IsIntact);
            Assert.AreEqual(middle + 1, result.FirstBrokenId, "the event that followed the removed one");
        }
    }

    [TestMethod]
    public void TheHashDependsOnEveryFieldAndOnThePreviousHash()
    {
        var e = new AuditEvent { AtUtc = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), Actor = "Z-001", Action = "A", EntityType = "T", EntityId = "1", Details = "d" };
        var baseline = AuditChain.Compute(null, e);

        Assert.AreEqual(baseline, AuditChain.Compute(null, e), "the same input gives the same hash");
        Assert.AreNotEqual(baseline, AuditChain.Compute("abc", e));
        Assert.AreNotEqual(baseline, AuditChain.Compute(null, new AuditEvent { AtUtc = e.AtUtc, Actor = "Z-002", Action = "A", EntityType = "T", EntityId = "1", Details = "d" }));
        Assert.AreNotEqual(baseline, AuditChain.Compute(null, new AuditEvent { AtUtc = e.AtUtc.AddTicks(10), Actor = "Z-001", Action = "A", EntityType = "T", EntityId = "1", Details = "d" }));
        Assert.AreNotEqual(baseline, AuditChain.Compute(null, new AuditEvent { AtUtc = e.AtUtc, Actor = "Z-001", Action = "A", EntityType = "T", EntityId = "1", Details = null }));
        Assert.AreEqual(64, baseline.Length);
    }

    // ---- stored revisions carry the version of their layout ---------------------------------------------------------------------

    [TestMethod]
    public async Task ARevisionIsReadByTheLayoutItWasWrittenIn_AndAnUnreadableOneIsRefusedClearly()
    {
        var (factory, amra, benjamin) = await StartAsync();
        using (factory)
        {
            await amra.PostAsync("/api/timesheet/2026/9/fill-standard-days", null);
            await amra.PostAsync("/api/timesheet/2026/9/submit", null);
            Assert.AreEqual(1, Scalar(factory, "SELECT SchemaVersion FROM sheet_revisions"));
            Assert.AreEqual(HttpStatusCode.OK, (await benjamin.GetAsync("/api/approvals/Z-001/2026/9/revisions/1")).StatusCode);

            Execute(factory, "UPDATE sheet_revisions SET SchemaVersion = 99");
            var tooNew = await benjamin.GetAsync("/api/approvals/Z-001/2026/9/revisions/1");
            Assert.AreEqual(HttpStatusCode.Conflict, tooNew.StatusCode);
            Assert.AreEqual("revision-too-new", (await tooNew.ReadAsync<ApiErrorDto>())!.Code);

            Execute(factory, "UPDATE sheet_revisions SET SchemaVersion = 1, Json = '{ not json'");
            var damaged = await benjamin.GetAsync("/api/approvals/Z-001/2026/9/revisions/1");
            Assert.AreEqual("revision-unreadable", (await damaged.ReadAsync<ApiErrorDto>())!.Code);
        }
    }

    private static CompanyDbContext Open(ApiFactory factory) =>
        new(new DbContextOptionsBuilder<CompanyDbContext>().UseSqlite($"Data Source={factory.DatabasePath}").Options);
}
