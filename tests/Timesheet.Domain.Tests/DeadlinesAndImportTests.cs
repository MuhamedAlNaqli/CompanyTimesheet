using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Timesheet.Domain.Tests;

[TestClass]
public class DeadlineTests
{
    [TestMethod]
    public void SubmissionDeadline_ShouldBeThe25th_OrTheNextWorkingDay()
    {
        Assert.AreEqual(T.D(2026, 9, 25), Deadlines.Submission(2026, 9, T.September), "Friday");
        Assert.AreEqual(T.D(2026, 10, 26), Deadlines.Submission(2026, 10, new WorkCalendar()), "25.10.2026 is a Sunday");

        var holiday = new WorkCalendar(new[] { new Holiday(T.D(2026, 10, 26), "Praznik") });
        Assert.AreEqual(T.D(2026, 10, 27), Deadlines.Submission(2026, 10, holiday));
    }

    [TestMethod]
    public void ApprovalDeadline_ShouldBeTheLastDayOfTheMonth()
    {
        Assert.AreEqual(T.D(2026, 9, 30), Deadlines.Approval(2026, 9));
        Assert.AreEqual(T.D(2028, 2, 29), Deadlines.Approval(2028, 2));
    }

    [TestMethod]
    public void EmployeeReminders_ShouldStartThreeDaysBeforeTheDeadline()
    {
        var deadline = T.D(2026, 9, 25);
        Assert.IsNull(ReminderRules.ForEmployee(T.D(2026, 9, 20), TimesheetStatus.Draft, deadline));
        Assert.AreEqual(ReminderKind.SubmitSoon, ReminderRules.ForEmployee(T.D(2026, 9, 22), TimesheetStatus.Draft, deadline));
        Assert.AreEqual(ReminderKind.SubmitSoon, ReminderRules.ForEmployee(T.D(2026, 9, 25), TimesheetStatus.Draft, deadline));
        Assert.AreEqual(ReminderKind.SubmitOverdue, ReminderRules.ForEmployee(T.D(2026, 9, 26), TimesheetStatus.Draft, deadline));
        Assert.AreEqual(ReminderKind.FixReturned, ReminderRules.ForEmployee(T.D(2026, 9, 5), TimesheetStatus.Returned, deadline));
        Assert.IsNull(ReminderRules.ForEmployee(T.D(2026, 9, 28), TimesheetStatus.Submitted, deadline), "nothing to do once submitted");
        Assert.IsNull(ReminderRules.ForEmployee(T.D(2026, 9, 28), TimesheetStatus.Approved, deadline));
    }

    [TestMethod]
    public void ApproverReminders_ShouldFollowTheApprovalDeadline()
    {
        var deadline = T.D(2026, 9, 30);
        Assert.IsNull(ReminderRules.ForApprover(T.D(2026, 9, 26), deadline, waitingCount: 0));
        Assert.AreEqual(ReminderKind.ApproveWaiting, ReminderRules.ForApprover(T.D(2026, 9, 26), deadline, 3));
        Assert.AreEqual(ReminderKind.ApproveOverdue, ReminderRules.ForApprover(T.D(2026, 10, 1), deadline, 3));
    }
}

[TestClass]
public class EmployeeCodeAndImportTests
{
    [DataTestMethod]
    [DataRow("Z-255", true, "Z-255")]
    [DataRow("  Z-255 ", true, "Z-255")]
    [DataRow("Z- 255", false, "Z- 255")]
    [DataRow("z-255", false, "z-255")]
    [DataRow("Z-25", false, "Z-25")]
    [DataRow("Z-2555", false, "Z-2555")]
    [DataRow("", false, "")]
    public void Code_ShouldBeZDashThreeDigits(string text, bool valid, string normalised)
    {
        Assert.AreEqual(valid, EmployeeCode.TryParse(text, out var code));
        Assert.AreEqual(normalised, code);
    }

    private const string Csv = "﻿Br.,Ime i prezime\r\nZ-002,Amra Test\r\nZ-005,\"Test, Zarez\"\r\nZ-081,  Dva   Razmaka \r\nZ- 255,Pogrešna Šifra\r\nZ-002,Duplikat\r\nZ-300,\r\n\r\nZ-010,Šifra Postoji\r\n";

    [TestMethod]
    public void Preview_ShouldReportErrorsAndWarningsBeforeAnythingIsSaved()
    {
        var existing = new Dictionary<string, string> { ["Z-010"] = "Drugo Ime" };
        var preview = EmployeeListImporter.Preview(Csv, existing);

        Assert.AreEqual(7, preview.Rows.Count, "the header and the empty line are not rows");
        Assert.AreEqual("Amra Test", preview.Rows[0].Name, "the byte order mark and the header are skipped");
        Assert.AreEqual("Test, Zarez", preview.Rows[1].Name, "a quoted comma stays in the name");

        var trimmed = preview.Rows.Single(r => r.Code == "Z-081");
        Assert.AreEqual("Dva Razmaka", trimmed.Name);
        Assert.IsTrue(trimmed.Issues.Any(i => i.Severity == ImportSeverity.Warning));
        Assert.IsFalse(trimmed.HasErrors);

        Assert.IsTrue(preview.Rows.Single(r => r.Name == "Pogrešna Šifra").HasErrors, "Z- 255 is not a valid code");
        Assert.IsTrue(preview.Rows.Single(r => r.Name == "Duplikat").HasErrors, "the same code twice");
        Assert.IsTrue(preview.Rows.Single(r => r.Code == "Z-300").HasErrors, "no name");

        var existingName = preview.Rows.Single(r => r.Code == "Z-010");
        Assert.IsFalse(existingName.HasErrors);
        Assert.AreEqual(ImportSeverity.Warning, existingName.Issues.Single().Severity, "a known code with another name is only a warning");

        Assert.AreEqual(3, preview.ErrorCount);
        Assert.AreEqual(2, preview.WarningCount);
        Assert.AreEqual(4, preview.Valid.Count());
    }

    [TestMethod]
    public void ReimportingTheSameList_ShouldNotCreateProblems()
    {
        var first = EmployeeListImporter.Preview("Br.,Ime i prezime\nZ-001,Ana Anić\nZ-002,Bob Bobić\n");
        var existing = first.Valid.ToDictionary(r => r.Code, r => r.Name);
        var second = EmployeeListImporter.Preview("Br.,Ime i prezime\nZ-001,Ana Anić\nZ-002,Bob Bobić\n", existing);

        Assert.AreEqual(0, second.ErrorCount);
        Assert.AreEqual(0, second.WarningCount);
    }

    /// <summary>
    /// The real employee list holds personal data and stays out of the repository. Set TIMESHEET_EMPLOYEE_LIST to its path to
    /// check the acceptance criterion of milestone M1: 112 employees, 1 trimmed name, 0 errors.
    /// </summary>
    [TestMethod]
    public void TheRealEmployeeList_ShouldImportWithoutErrors()
    {
        var path = Environment.GetEnvironmentVariable("TIMESHEET_EMPLOYEE_LIST");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) Assert.Inconclusive("TIMESHEET_EMPLOYEE_LIST is not set.");

        var preview = EmployeeListImporter.Preview(File.ReadAllText(path, Encoding.UTF8));
        Assert.AreEqual(0, preview.ErrorCount);
        Assert.AreEqual(112, preview.Rows.Count);
        Assert.AreEqual(1, preview.WarningCount);
    }
}
