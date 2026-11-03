using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Timesheet.Domain.Tests;

[TestClass]
public class OvertimeTests
{
    private static OvertimeClassification Classify(DateOnly date, TimeOnly start, TimeOnly end, WorkCalendar? calendar = null, IReadOnlyCollection<DateOnly>? religious = null)
    {
        calendar ??= T.September;
        return OvertimeClassifier.Classify(new OvertimeInterval(date, start, end), T.TypeOf(calendar, religious));
    }

    private static string Show(OvertimeClassification c) =>
        string.Join("; ", c.Allocations.Select(a => $"{a.Date:dd.MM} {a.Code}={a.Hours:0.##}"));

    // ---- The six overtime records of the sample sheet (S1 and S2) ------------------------------------------------------

    [DataTestMethod]
    [DataRow(1, 19, 0, 21, 0, "01.09 2020=2")]
    [DataRow(2, 19, 0, 22, 0, "02.09 2020=3")]
    [DataRow(7, 20, 0, 22, 30, "07.09 2020=2; 07.09 2021=0.5")]
    [DataRow(3, 21, 0, 23, 0, "03.09 2020=1; 03.09 2021=1")]
    [DataRow(5, 8, 30, 11, 0, "05.09 2023=2.5")]
    [DataRow(19, 8, 30, 11, 0, "19.09 2023=2.5")]
    public void SampleSheetRecords_ShouldReproduceTheAllocationsOfS1(int day, int sh, int sm, int eh, int em, string expected)
    {
        Assert.AreEqual(expected, Show(Classify(T.Sep(day), T.At(sh, sm), T.At(eh, em))));
    }

    // ---- The examples of the instruction (S6) ---------------------------------------------------------------------------

    [TestMethod]
    public void WorkingDayEvening_ShouldBeOvertime2020()
    {
        // Friday 04.09.2020, 17:00 to 21:00 -> 2020 = 4
        var result = Classify(T.D(2020, 9, 4), T.At(17), T.At(21), new WorkCalendar());
        Assert.AreEqual("04.09 2020=4", Show(result));
        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void WeekendOvertimeAcrossMidnight_ShouldBeSplitAndTheRestGoToTheNextDate()
    {
        // Saturday 05.09.2020, 21:00 to 02:00 -> Saturday 2023 = 1 and 2024 = 2; Sunday 06.09 2024 = 2
        var result = Classify(T.D(2020, 9, 5), T.At(21), T.At(2), new WorkCalendar());
        Assert.AreEqual("05.09 2023=1; 05.09 2024=2; 06.09 2024=2", Show(result));
        Assert.AreEqual(5 * 60, result.TotalMinutes);
        Assert.IsTrue(result.Allocations.Sum(a => a.Minutes) == result.TotalMinutes, "the parts add up to the total");
    }

    [TestMethod]
    public void FridayNightIntoSaturday_ShouldUseTheTypeOfEachDate()
    {
        var result = Classify(T.Sep(4), T.At(23), T.At(1));
        Assert.AreEqual("04.09 2021=1; 05.09 2024=1", Show(result), "Friday night is working-day night, Saturday after midnight is weekend night");
    }

    [TestMethod]
    public void EarlyMorningOvertime_ShouldUseThe0600To0830Window()
    {
        Assert.AreEqual("01.09 2020=2.5", Show(Classify(T.Sep(1), T.At(6), T.At(8, 30))));
        Assert.AreEqual("01.09 2020=1; 01.09 2021=1", Show(Classify(T.Sep(1), T.At(5), T.At(7))), "05:00 to 06:00 is still night");
    }

    // ---- Holidays and weekends -------------------------------------------------------------------------------------------

    [TestMethod]
    public void Holiday_ShouldUseTheHolidayCodes2022And2025()
    {
        var calendar = new WorkCalendar(new[] { new Holiday(T.Sep(9), "Praznik") });
        Assert.AreEqual("09.09 2022=2; 09.09 2025=1", Show(Classify(T.Sep(9), T.At(20), T.At(23), calendar)));
    }

    [TestMethod]
    public void HolidayOnAWeekend_ShouldUseHolidayCodesNotWeekendCodes()
    {
        var calendar = new WorkCalendar(new[] { new Holiday(T.Sep(5), "Praznik na subotu") });
        Assert.AreEqual("05.09 2022=2", Show(Classify(T.Sep(5), T.At(18), T.At(20), calendar)), "Q-03");
    }

    [TestMethod]
    public void ReligiousHolidayOfTheEmployee_ShouldUseHolidayCodes()
    {
        var mine = new[] { T.Sep(8) };
        Assert.AreEqual("08.09 2022=2", Show(Classify(T.Sep(8), T.At(18), T.At(20), religious: mine)));
        Assert.AreEqual("08.09 2020=2", Show(Classify(T.Sep(8), T.At(18), T.At(20))), "for a colleague it is a working day");
    }

    [TestMethod]
    public void WeekendDay_ShouldBeOneCode2023FromSixToTen()
    {
        Assert.AreEqual("06.09 2023=16", Show(Classify(T.Sep(6), T.At(6), T.At(22))), "Sunday 06:00 to 22:00, including the hours of the regular window");
    }

    // ---- Invalid intervals -----------------------------------------------------------------------------------------------

    [TestMethod]
    public void OvertimeInsideTheRegularWindow_ShouldBeReportedAsAConflictNotAllocated()
    {
        var result = Classify(T.Sep(7), T.At(10), T.At(12));
        Assert.AreEqual(2 * 60, result.TotalMinutes, "the total is computed: 10:00 to 12:00 is 2 h, not 1 h");
        Assert.AreEqual(0, result.Allocations.Count);
        Assert.AreEqual(120, result.Conflicts.Single().Minutes);
        Assert.IsFalse(result.IsValid);
    }

    [TestMethod]
    public void OvertimeThatReachesIntoTheRegularWindow_ShouldSplitIntoAllocationAndConflict()
    {
        var result = Classify(T.Sep(7), T.At(7), T.At(9));
        Assert.AreEqual("07.09 2020=1.5", Show(result));
        Assert.AreEqual(30, result.Conflicts.Single().Minutes);
    }

    [TestMethod]
    public void EqualStartAndEnd_ShouldHaveNoDuration()
    {
        var interval = new OvertimeInterval(T.Sep(1), T.At(10), T.At(10));
        Assert.AreEqual(0, interval.TotalMinutes);
        Assert.AreEqual(0, OvertimeClassifier.Classify(interval, T.TypeOf(T.September)).Allocations.Count);
    }

    [TestMethod]
    public void Total_ShouldAlwaysBeEndMinusStart()
    {
        Assert.AreEqual(150, new OvertimeInterval(T.Sep(1), T.At(20), T.At(22, 30)).TotalMinutes);
        Assert.AreEqual(300, new OvertimeInterval(T.Sep(5), T.At(21), T.At(2)).TotalMinutes);
        Assert.IsTrue(new OvertimeInterval(T.Sep(5), T.At(21), T.At(2)).CrossesMidnight);
    }
}
