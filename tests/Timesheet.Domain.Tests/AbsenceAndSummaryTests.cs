using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Timesheet.Domain.Tests;

[TestClass]
public class AbsenceAndSummaryTests
{
    // ---- Absence ranges --------------------------------------------------------------------------------------------------

    [TestMethod]
    public void AnnualLeaveRange_ShouldSkipWeekends()
    {
        // Entering 1022 from 01.09 to 15.09 creates exactly 11 days (1-4, 7-11, 14-15) and nothing on 5, 6, 12, 13
        var result = AbsenceRanges.Expand(Codes.AnnualLeaveA, T.Sep(1), T.Sep(15), T.September);

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 7, 8, 9, 10, 11, 14, 15 }, result.Days.Select(d => d.Day).ToArray());
        CollectionAssert.AreEqual(new[] { 5, 6, 12, 13 }, result.Skipped.Select(d => d.Date.Day).ToArray());
        Assert.IsTrue(result.Skipped.All(s => s.Reason == SkipReason.Weekend));
    }

    [TestMethod]
    public void Range_ShouldLeaveOutStateHolidaysAndTheEmployeesReligiousHolidays()
    {
        var calendar = new WorkCalendar(new[] { new Holiday(T.Sep(9), "Praznik") });
        var result = AbsenceRanges.Expand(Codes.AnnualLeaveA, T.Sep(8), T.Sep(11), calendar, new[] { T.Sep(10) });

        CollectionAssert.AreEqual(new[] { 8, 11 }, result.Days.Select(d => d.Day).ToArray());
        Assert.AreEqual(SkipReason.StateHoliday, result.Skipped.Single(s => s.Date.Day == 9).Reason);
        Assert.AreEqual(SkipReason.ReligiousHoliday, result.Skipped.Single(s => s.Date.Day == 10).Reason);
    }

    [TestMethod]
    public void ReligiousHolidayCode_ShouldBeEnteredOnTheReligiousHolidayItself()
    {
        var result = AbsenceRanges.Expand(Codes.ReligiousHoliday, T.Sep(10), T.Sep(10), T.September, new[] { T.Sep(10) });
        Assert.AreEqual(1, result.Days.Count);
    }

    [TestMethod]
    public void Range_ShouldRejectCodesThatAreNotWholeDayAbsencesAndBackwardsRanges()
    {
        Assert.ThrowsException<ArgumentException>(() => AbsenceRanges.Expand(Codes.RegularWork, T.Sep(1), T.Sep(2), T.September));
        Assert.ThrowsException<ArgumentException>(() => AbsenceRanges.Expand(Codes.Overtime, T.Sep(1), T.Sep(2), T.September));
        Assert.ThrowsException<ArgumentException>(() => AbsenceRanges.Expand(9999, T.Sep(1), T.Sep(2), T.September));
        Assert.ThrowsException<ArgumentException>(() => AbsenceRanges.Expand(Codes.AnnualLeaveA, T.Sep(5), T.Sep(1), T.September));
    }

    [TestMethod]
    public void TwoDaysPaidLeaveAndFiveDaysAnnualLeave_ShouldMatchTheExampleOfTheInstruction()
    {
        var days = T.WorkingDays(T.September).ToList();
        var entries = new List<DayEntry>();
        entries.AddRange(days.Take(2).Select(d => DayEntry.Absence(d, Codes.PaidLeave)));
        entries.AddRange(days.Skip(2).Take(5).Select(d => DayEntry.Absence(d, Codes.AnnualLeaveA)));
        entries.AddRange(days.Skip(7).Select(d => DayEntry.Work(d)));

        var summary = MonthlySummaryCalculator.Compute(entries, Array.Empty<OvertimeAllocation>());
        Assert.AreEqual(2, summary.Days(Codes.PaidLeave));
        Assert.AreEqual(5, summary.Days(Codes.AnnualLeaveA));
        Assert.AreEqual(15 * 8m, summary.Hours(Codes.RegularWork));
        Assert.AreEqual(176m, summary.FundHours, "an absence day counts as 8 h");
    }

    // ---- Monthly summary: absence codes are never hours -----------------------------------------------------------------

    [TestMethod]
    public void AbsenceCodes_ShouldNeverBeCountedAsHours()
    {
        // In the Excel sheet the code 1022 is typed into the hours row: a plain sum gives 88 + 11 x 1022 = 11,330 "hours"
        var days = T.WorkingDays(T.September).ToList();
        var entries = days.Take(11).Select(d => DayEntry.Absence(d, Codes.AnnualLeaveA))
            .Concat(days.Skip(11).Select(d => DayEntry.Work(d))).ToList();

        var summary = MonthlySummaryCalculator.Compute(entries, Array.Empty<OvertimeAllocation>());
        Assert.AreEqual(88m, summary.Hours(Codes.RegularWork));
        Assert.AreEqual(11, summary.Days(Codes.AnnualLeaveA));
        Assert.AreEqual(0m, summary.Hours(Codes.AnnualLeaveA));
    }

    [TestMethod]
    public void Fund_ShouldIncludeHolidayHoursAndExcludeOvertime()
    {
        var calendar = new WorkCalendar(new[] { new Holiday(T.Sep(9), "Praznik") });
        var entries = T.WorkingDays(calendar).Select(d => DayEntry.Work(d)).ToList();
        entries.Add(DayEntry.Work(T.Sep(9), 3m, Codes.StateHoliday));
        entries.Add(DayEntry.Work(T.Sep(9), 5m, Codes.HolidayWork));
        var overtime = new[] { new OvertimeAllocation(T.Sep(1), Codes.Overtime, 180) };

        var summary = MonthlySummaryCalculator.Compute(entries, overtime, null, calendar);
        Assert.AreEqual(21 * 8m + 3m + 5m, summary.FundHours, "holiday hours 3 + 5 replace the regular 8; overtime is outside the fund");
        Assert.AreEqual(3m, summary.OvertimeHours);
        Assert.AreEqual(3m, summary.Hours(Codes.Overtime));
        Assert.AreEqual(MonthlySummaryCalculator.ExpectedFundMinutes(2026, 9, null) / 60m, summary.FundHours);
    }

    [TestMethod]
    public void WorkingDaysOfACode_ShouldExcludeWeekends()
    {
        // 1040 on all 30 calendar days: 22 working days and 30 calendar days (Z-251 in the sample)
        var entries = T.September.WorkingDays().Concat(WorkCalendar.DaysOf(2026, 9).Where(WorkCalendar.IsWeekend)).OrderBy(d => d)
            .Select(d => DayEntry.Absence(d, Codes.MaternityLeave)).ToList();

        var summary = MonthlySummaryCalculator.Compute(entries, Array.Empty<OvertimeAllocation>(), null, T.September);
        Assert.AreEqual(30, summary.Days(Codes.MaternityLeave));
        Assert.AreEqual(22, summary.WorkingDays(Codes.MaternityLeave));
        Assert.AreEqual(240m, summary.FundHours, "weekend entries push the fund to 240 h: the likely error of the sample (Q-08)");
    }
}

internal static class CalendarExtensions
{
    public static IEnumerable<DateOnly> WorkingDays(this WorkCalendar calendar) => T.WorkingDays(calendar);
}
