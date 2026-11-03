using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Timesheet.Domain.Tests;

[TestClass]
public class CodesAndCalendarTests
{
    [TestMethod]
    public void Catalogue_ShouldHoldTheTwentyTwoCodesOfTheLegend()
    {
        // The analysis speaks of 20 codes; the legend of the sample sheet lists 22
        Assert.AreEqual(22, TimeCodes.All.Count);
        Assert.AreEqual(22, TimeCodes.All.Select(c => c.Code).Distinct().Count());
        Assert.IsFalse(TimeCodes.Exists(1026), "1026 is skipped in the numbering");
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => TimeCodes.Get(9999));
    }

    [TestMethod]
    public void Codes_ShouldKnowTheirUnitAndWhetherTheyBelongToTheFund()
    {
        Assert.AreEqual(CodeUnit.Hours, TimeCodes.Get(Codes.RegularWork).Unit);
        Assert.AreEqual(CodeUnit.Hours, TimeCodes.Get(Codes.StateHoliday).Unit, "1024 is entered in hours not worked");
        Assert.AreEqual(CodeUnit.Day, TimeCodes.Get(Codes.AnnualLeaveA).Unit);
        Assert.AreEqual(CodeUnit.Day, TimeCodes.Get(Codes.MaternityLeave).Unit);

        // S6: green, blue and orange form the fund; red (overtime) does not
        foreach (var code in new[] { 1010, 1020, 1022, 1024, 1025, 1040, 2010, 2011, 2012 })
            Assert.IsTrue(TimeCodes.Get(code).InMonthlyFund, code.ToString());
        foreach (var code in new[] { 2020, 2021, 2022, 2023, 2024, 2025 })
        {
            Assert.IsTrue(TimeCodes.Get(code).IsOvertime, code.ToString());
            Assert.IsFalse(TimeCodes.Get(code).InMonthlyFund, code.ToString());
        }
    }

    [TestMethod]
    public void BaseEntry_ShouldBeRegularWorkOrAWholeDayAbsence()
    {
        Assert.IsTrue(TimeCodes.Get(1010).IsBaseEntry);
        Assert.IsTrue(TimeCodes.Get(1022).IsBaseEntry);
        Assert.IsFalse(TimeCodes.Get(1024).IsBaseEntry, "1024 has its own row");
        Assert.IsFalse(TimeCodes.Get(2010).IsBaseEntry);
        Assert.IsFalse(TimeCodes.Get(2020).IsBaseEntry);
    }

    [TestMethod]
    public void September2026_ShouldHave22WorkingDaysAnd8WeekendDays()
    {
        var days = WorkCalendar.DaysOf(2026, 9).ToList();
        Assert.AreEqual(30, days.Count);
        Assert.AreEqual(22, WorkCalendar.WeekdayCount(2026, 9));
        Assert.AreEqual(22, T.September.WorkingDayCount(2026, 9));
        Assert.AreEqual(8, days.Count(WorkCalendar.IsWeekend));
    }

    [TestMethod]
    public void TheSampleSheetsSunday27September_IsASunday()
    {
        // The sample sheet labelled its Sunday 27 September "Thursday"; the weekday comes from the date, never from a typed letter
        Assert.AreEqual(DayOfWeek.Tuesday, T.Sep(1).DayOfWeek);
        Assert.AreEqual(DayOfWeek.Sunday, T.Sep(27).DayOfWeek);
    }

    [TestMethod]
    public void Holiday_ShouldWinOverWeekendSoHolidayCodesApply()
    {
        var calendar = new WorkCalendar(new[] { new Holiday(T.Sep(5), "Praznik na subotu"), new Holiday(T.Sep(9), "Praznik") });

        Assert.AreEqual(DayType.Holiday, calendar.DayTypeOf(T.Sep(5)), "Q-03: a holiday on a weekend uses holiday codes");
        Assert.AreEqual(DayType.Holiday, calendar.DayTypeOf(T.Sep(9)));
        Assert.AreEqual(DayType.Weekend, calendar.DayTypeOf(T.Sep(6)));
        Assert.AreEqual(DayType.Working, calendar.DayTypeOf(T.Sep(8)));
        Assert.AreEqual(HolidayKind.State, calendar.Resolve(T.Sep(9)).HolidayKind);
        Assert.AreEqual(21, calendar.WorkingDayCount(2026, 9), "A holiday on a weekday is not a working day");
        Assert.AreEqual(22, WorkCalendar.WeekdayCount(2026, 9), "but it is a weekday for the monthly fund");
    }

    [TestMethod]
    public void ReligiousHoliday_ShouldBePerEmployee()
    {
        var calendar = T.September;
        var mine = new[] { T.Sep(16) };

        Assert.AreEqual(DayType.Holiday, calendar.DayTypeOf(T.Sep(16), mine));
        Assert.AreEqual(HolidayKind.Religious, calendar.Resolve(T.Sep(16), mine).HolidayKind);
        Assert.AreEqual(DayType.Working, calendar.DayTypeOf(T.Sep(16)), "another employee works that day");
    }

    [TestMethod]
    public void NextWorkingDay_ShouldSkipWeekendsAndHolidays()
    {
        var calendar = new WorkCalendar(new[] { new Holiday(T.D(2026, 10, 26), "Praznik") });
        Assert.AreEqual(T.D(2026, 10, 27), calendar.NextWorkingDay(T.D(2026, 10, 25)), "25.10.2026 is a Sunday and Monday is a holiday");
        Assert.AreEqual(T.Sep(25), T.September.NextWorkingDay(T.Sep(25)));
    }

    [TestMethod]
    public void ExpectedFund_ShouldCountWeekdaysTimesTheNorm_AndFollowEmploymentDates()
    {
        Assert.AreEqual(176 * 60, MonthlySummaryCalculator.ExpectedFundMinutes(2026, 9));
        Assert.AreEqual(7 * 8 * 60, MonthlySummaryCalculator.ExpectedFundMinutes(2026, 9, null, T.Sep(22), null), "starts on Tuesday 22nd: 22 to 30 has 7 weekdays");
    }
}
