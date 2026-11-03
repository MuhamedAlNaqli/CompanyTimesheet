using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Timesheet.Domain.Tests;

[TestClass]
public class ValidationTests
{
    private static List<DayEntry> Clean() => T.FullMonth(T.September);

    private static List<DayEntry> Without(List<DayEntry> entries, int day) => entries.Where(e => e.Date != T.Sep(day)).ToList();

    [TestMethod]
    public void ACleanMonth_ShouldHaveNoFindings()
    {
        Assert.AreEqual(0, TimesheetValidator.Validate(T.Data(Clean())).Count);
    }

    [TestMethod]
    public void V02_AnnualLeaveOnAWeekend_ShouldBlock()
    {
        var entries = Clean();
        entries.Add(DayEntry.Absence(T.Sep(5), Codes.AnnualLeaveA));

        var issue = T.Rules(T.Data(entries), "V-02").Single();
        Assert.AreEqual(Severity.Blocking, issue.Severity);
        Assert.AreEqual(T.Sep(5), issue.Date);
    }

    [TestMethod]
    public void V03_OvertimeInsideTheRegularWindow_ShouldBlock()
    {
        // Monday 07.09.2026, 10:00 to 12:00
        var data = T.Data(Clean(), new[] { T.Ot(T.Sep(7), T.At(10), T.At(12)) });
        Assert.AreEqual(Severity.Blocking, T.Rules(data, "V-03").Single().Severity);
    }

    [TestMethod]
    public void V04_WorkingDayWithoutAnEntry_ShouldWarn_AndNotForWeekendsOrStateHolidays()
    {
        var calendar = new WorkCalendar(new[] { new Holiday(T.Sep(9), "Praznik") });
        var entries = T.FullMonth(calendar);
        var issues = T.Rules(T.Data(Without(entries, 10), calendar: calendar), "V-04");
        Assert.AreEqual(T.Sep(10), issues.Single().Date);
        Assert.AreEqual(Severity.Warning, issues.Single().Severity);

        Assert.AreEqual(0, T.Rules(T.Data(entries, calendar: calendar), "V-04").Count, "9 Sept is a holiday: 1024 and 2010 are used, not a base entry");
    }

    [TestMethod]
    public void V05_TwoBaseEntriesOnOneDay_ShouldBlock()
    {
        var entries = Clean();
        entries.Add(DayEntry.Absence(T.Sep(1), Codes.AnnualLeaveA));
        Assert.AreEqual(Severity.Blocking, T.Rules(T.Data(entries), "V-05").Single().Severity);
    }

    [TestMethod]
    public void V06_RegularWorkOnAWeekend_ShouldBlock()
    {
        var entries = Clean();
        entries.Add(DayEntry.Work(T.Sep(6)));
        Assert.AreEqual(1, T.Rules(T.Data(entries), "V-06").Count);
    }

    [TestMethod]
    public void V07_RegularRowOnAStateHoliday_ShouldBlock()
    {
        var calendar = new WorkCalendar(new[] { new Holiday(T.Sep(9), "Praznik") });
        var entries = T.FullMonth(calendar);
        entries.Add(DayEntry.Work(T.Sep(9)));
        entries.Add(DayEntry.Absence(T.Sep(9), Codes.AnnualLeaveA));

        Assert.AreEqual(2, T.Rules(T.Data(entries, calendar: calendar), "V-07").Count);
    }

    [TestMethod]
    public void V08_UnknownOrInactiveEmployee_ShouldBlock()
    {
        Assert.AreEqual(1, T.Rules(T.Data(Clean(), employee: new EmployeeStatus(IsKnown: false)), "V-08").Count);
        Assert.AreEqual(1, T.Rules(T.Data(Clean(), employee: new EmployeeStatus(ActiveTo: T.D(2026, 8, 31))), "V-08").Count);
        Assert.AreEqual(0, T.Rules(T.Data(Clean(), employee: new EmployeeStatus(ActiveFrom: T.D(2020, 1, 1))), "V-08").Count);
    }

    [TestMethod]
    public void V09_V10_OverlappingAndDuplicateOvertime_ShouldBlock()
    {
        var overlap = T.Data(Clean(), new[] { T.Ot(T.Sep(1), T.At(18), T.At(20)), T.Ot(T.Sep(1), T.At(19), T.At(21)) });
        Assert.AreEqual(1, T.Rules(overlap, "V-09").Count);
        Assert.AreEqual(0, T.Rules(overlap, "V-10").Count);

        var duplicate = T.Data(Clean(), new[] { T.Ot(T.Sep(1), T.At(18), T.At(20)), T.Ot(T.Sep(1), T.At(18), T.At(20)) });
        Assert.AreEqual(1, T.Rules(duplicate, "V-10").Count);
        Assert.AreEqual(0, T.Rules(duplicate, "V-09").Count, "a duplicate is reported once, as a duplicate");

        var apart = T.Data(Clean(), new[] { T.Ot(T.Sep(1), T.At(18), T.At(19)), T.Ot(T.Sep(1), T.At(19), T.At(20)) });
        Assert.AreEqual(0, T.Rules(apart, "V-09").Count, "touching intervals do not overlap");
    }

    [TestMethod]
    public void V09_OvertimeAcrossMidnightAgainstTheNextDay_ShouldOverlap()
    {
        var data = T.Data(Clean(), new[] { T.Ot(T.Sep(5), T.At(21), T.At(2)), T.Ot(T.Sep(6), T.At(1), T.At(3)) });
        Assert.AreEqual(1, T.Rules(data, "V-09").Count);
    }

    [TestMethod]
    public void V11_MoreThan24HoursOnADate_ShouldBlock()
    {
        var entries = Clean();
        entries.Add(DayEntry.Work(T.Sep(5), 20m, Codes.HolidayWork));
        entries.Add(DayEntry.Work(T.Sep(5), 6m, Codes.RegularNightWork));
        Assert.AreEqual(1, T.Rules(T.Data(entries), "V-11").Count);
    }

    [TestMethod]
    public void V12_HoursOffTheGranularity_ShouldWarn()
    {
        var entries = Without(Clean(), 1);
        entries.Add(new DayEntry(T.Sep(1), Codes.RegularWork, 8 * 60 + 10));
        Assert.AreEqual(Severity.Warning, T.Rules(T.Data(entries), "V-12").Single().Severity);

        var overtime = T.Data(Clean(), new[] { T.Ot(T.Sep(1), T.At(18), T.At(19, 10)) });
        Assert.AreEqual(1, T.Rules(overtime, "V-12").Count);
    }

    [TestMethod]
    public void V13_MissingDescription_ShouldBlock()
    {
        var data = T.Data(Clean(), new[] { T.Ot(T.Sep(1), T.At(18), T.At(20), "  ") });
        Assert.AreEqual(Severity.Blocking, T.Rules(data, "V-13").Single().Severity);
    }

    [TestMethod]
    public void V14_RegularHoursOtherThanTheNorm_ShouldWarn()
    {
        var entries = Without(Clean(), 1);
        entries.Add(DayEntry.Work(T.Sep(1), 6m));
        Assert.AreEqual(Severity.Warning, T.Rules(T.Data(entries), "V-14").Single().Severity);
    }

    [TestMethod]
    public void V15_EqualStartAndEnd_ShouldBlock_ButAnEndBeforeTheStartMeansPastMidnight()
    {
        Assert.AreEqual(1, T.Rules(T.Data(Clean(), new[] { T.Ot(T.Sep(1), T.At(20), T.At(20)) }), "V-15").Count);
        Assert.AreEqual(0, T.Rules(T.Data(Clean(), new[] { T.Ot(T.Sep(1), T.At(23), T.At(1)) }), "V-15").Count);
    }

    [TestMethod]
    public void V01_ADeclaredTotalThatDoesNotMatchTheInterval_ShouldBlock()
    {
        // Typed by hand in the Excel sheet: "10:00 to 12:00" with a total of 1 h
        var wrong = new OvertimeRecord(T.Sep(1), T.At(18), T.At(20), "Rad", DeclaredTotalMinutes: 60);
        var right = wrong with { DeclaredTotalMinutes = 120 };
        Assert.AreEqual(1, T.Rules(T.Data(Clean(), new[] { wrong }), "V-01").Count);
        Assert.AreEqual(0, T.Rules(T.Data(Clean(), new[] { right }), "V-01").Count);
    }

    [TestMethod]
    public void V17_AnyOtherAbsenceOnAWeekend_ShouldBlock()
    {
        var entries = Clean();
        entries.Add(DayEntry.Absence(T.Sep(6), Codes.SickUpTo42Days));
        Assert.AreEqual(1, T.Rules(T.Data(entries), "V-17").Count);
        Assert.AreEqual(0, T.Rules(T.Data(entries), "V-02").Count, "annual leave has its own rule");
    }

    [TestMethod]
    public void V18_ManualCategories_ShouldWarn()
    {
        var overridden = new OvertimeRecord(T.Sep(1), T.At(18), T.At(20), "Rad", CategoriesOverridden: true);
        Assert.AreEqual(Severity.Warning, T.Rules(T.Data(Clean(), new[] { overridden }), "V-18").Single().Severity);
    }

    [TestMethod]
    public void V19_OvertimeOnABusinessTripDay_ShouldBlock()
    {
        var entries = Without(Clean(), 1);
        entries.Add(DayEntry.Absence(T.Sep(1), Codes.BusinessTripDomestic));
        var data = T.Data(entries, new[] { T.Ot(T.Sep(1), T.At(18), T.At(20)) });
        Assert.AreEqual(Severity.Blocking, T.Rules(data, "V-19").Single().Severity);
    }

    [TestMethod]
    public void Q35_OvertimeIsAllowedOnAnnualLeaveButNotOnSickLeave()
    {
        var annual = Without(Clean(), 1);
        annual.Add(DayEntry.Absence(T.Sep(1), Codes.AnnualLeaveA));
        var allowed = T.Data(annual, new[] { T.Ot(T.Sep(1), T.At(18), T.At(20)) });
        Assert.AreEqual(0, T.Rules(allowed, "V-19", "V-19b", "V-19c").Count);

        var sick = Without(Clean(), 1);
        sick.Add(DayEntry.Absence(T.Sep(1), Codes.SickUpTo42Days));
        var blocked = T.Data(sick, new[] { T.Ot(T.Sep(1), T.At(18), T.At(20)) });
        Assert.AreEqual(Severity.Blocking, T.Rules(blocked, "V-19b").Single().Severity);

        var paid = Without(Clean(), 1);
        paid.Add(DayEntry.Absence(T.Sep(1), Codes.PaidLeave));
        var unknown = T.Data(paid, new[] { T.Ot(T.Sep(1), T.At(18), T.At(20)) });
        Assert.AreEqual(Severity.Warning, T.Rules(unknown, "V-19c").Single().Severity, "not decided by the stakeholders yet");
    }

    [TestMethod]
    public void V20_StateHolidayHours_ShouldAddUpToTheDailyNorm()
    {
        var calendar = new WorkCalendar(new[] { new Holiday(T.Sep(9), "Praznik") });

        var full = T.FullMonth(calendar);
        full.Add(DayEntry.Work(T.Sep(9), 8m, Codes.HolidayWork));
        Assert.AreEqual(0, T.Rules(T.Data(full, calendar: calendar), "V-20").Count, "worked in full: 2010 = 8");

        var partly = T.FullMonth(calendar);
        partly.Add(DayEntry.Work(T.Sep(9), 5m, Codes.HolidayWork));
        partly.Add(DayEntry.Work(T.Sep(9), 3m, Codes.StateHoliday));
        Assert.AreEqual(0, T.Rules(T.Data(partly, calendar: calendar), "V-20").Count, "5 h worked + 3 h holiday");

        var wrong = T.FullMonth(calendar);
        wrong.Add(DayEntry.Work(T.Sep(9), 5m, Codes.HolidayWork));
        wrong.Add(DayEntry.Work(T.Sep(9), 2m, Codes.StateHoliday));
        Assert.AreEqual(Severity.Blocking, T.Rules(T.Data(wrong, calendar: calendar), "V-20").Single().Severity);
    }

    [TestMethod]
    public void V21_AFundBelowTheExpectedValue_ShouldWarn_AFundAboveIsFine()
    {
        var short1 = Without(Clean(), 1);
        var issues = TimesheetValidator.Validate(T.Data(short1));
        Assert.AreEqual(1, issues.Count(i => i.RuleId == "V-21"));
        Assert.AreEqual(1, issues.Count(i => i.RuleId == "V-04"), "the missing day is reported on its own too");

        var more = Clean();
        more.Add(DayEntry.Work(T.Sep(9), 4m, Codes.RegularNightWork));
        Assert.AreEqual(0, T.Rules(T.Data(more), "V-21").Count);
    }

    [TestMethod]
    public void V21_ShouldFollowTheEmploymentDates()
    {
        // Started on Tuesday 22 September: only 7 weekdays are expected
        var entries = T.WorkingDays(T.September).Where(d => d >= T.Sep(22)).Select(d => DayEntry.Work(d)).ToList();
        var data = T.Data(entries, employee: new EmployeeStatus(ActiveFrom: T.Sep(22)));
        Assert.AreEqual(0, TimesheetValidator.Validate(data).Count);
    }

    [TestMethod]
    public void Severities_ShouldBeConfigurable()
    {
        var entries = Without(Clean(), 1);
        var data = T.Data(entries);

        Assert.AreEqual(Severity.Warning, TimesheetValidator.Validate(data).First(i => i.RuleId == "V-04").Severity);
        var strict = new ValidationSettings().Set("V-04", Severity.Blocking);
        Assert.AreEqual(Severity.Blocking, TimesheetValidator.Validate(data, strict).First(i => i.RuleId == "V-04").Severity);
    }

    [TestMethod]
    public void V16_EmployeesMissingFromTheTimesheet_ShouldBeListed()
    {
        var missing = TimesheetValidator.MissingEmployees(new[] { "Z-001", "Z-002", "Z-003" }, new[] { "z-002" });
        CollectionAssert.AreEqual(new[] { "Z-001", "Z-003" }, missing.ToArray());
    }

    [TestMethod]
    public void AFinding_SaysWhatWasFound_NotHowToWordIt()
    {
        var entries = Clean();
        entries.Add(DayEntry.Absence(T.Sep(5), Codes.AnnualLeaveA));
        var issue = TimesheetValidator.Validate(T.Data(entries)).First(i => i.RuleId == "V-02");
        Assert.AreEqual("V-02", issue.Key);
        Assert.AreEqual(T.Sep(5), issue.Values["date"]);
        Assert.AreEqual(Codes.AnnualLeaveA, issue.Values["code"]);
    }

    [TestMethod]
    public void ARuleThatFindsTwoKindsOfThings_NamesWhichOne()
    {
        var unknown = TimesheetValidator.Validate(T.Data(Clean(), employee: new EmployeeStatus(IsKnown: false))).First(i => i.RuleId == "V-08");
        Assert.AreEqual("V-08.unknown", unknown.Key);
    }
}
