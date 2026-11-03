using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Timesheet.Domain.Tests;

[TestClass]
public class AbsencePeriodTests
{
    private static DateOnly D(int day) => new(2026, 10, day);

    // October 2026: the 1st is a Thursday, so 3-4, 10-11, 17-18, 24-25 and 31 are weekends
    private static bool Working(DateOnly date) => !WorkCalendar.IsWeekend(date);

    [TestMethod]
    public void ConsecutiveDaysWithOneCode_ShouldBeOnePeriod()
    {
        var periods = AbsencePeriods.Build(new[] { (D(21), Codes.BusinessTripDomestic), (D(22), Codes.BusinessTripDomestic), (D(23), Codes.BusinessTripDomestic) }, Working);
        var period = periods.Single();
        Assert.AreEqual((D(21), D(23)), (period.From, period.To));
        Assert.IsFalse(period.IsSingleDay);
    }

    [TestMethod]
    public void AWeekendBetweenTwoDays_ShouldNotBreakThePeriod()
    {
        var periods = AbsencePeriods.Build(new[] { (D(9), Codes.AnnualLeaveA), (D(12), Codes.AnnualLeaveA) }, Working);
        Assert.AreEqual(1, periods.Count, "Friday and Monday");
        Assert.AreEqual((D(9), D(12)), (periods[0].From, periods[0].To));
    }

    [TestMethod]
    public void AWorkingDayBetween_OrAnotherCode_ShouldStartANewPeriod()
    {
        var gap = AbsencePeriods.Build(new[] { (D(5), Codes.SickUpTo42Days), (D(7), Codes.SickUpTo42Days) }, Working);
        Assert.AreEqual(2, gap.Count, "Tuesday the 6th was worked");

        var other = AbsencePeriods.Build(new[] { (D(5), Codes.SickUpTo42Days), (D(6), Codes.AnnualLeaveA) }, Working);
        Assert.AreEqual(2, other.Count);
        Assert.IsTrue(other[0].IsSingleDay);
    }

    [TestMethod]
    public void Days_ShouldBeJoinedWhateverTheOrderTheyArriveIn_AndNothingGivesNothing()
    {
        var periods = AbsencePeriods.Build(new[] { (D(2), Codes.MaternityLeave), (D(1), Codes.MaternityLeave) }, Working);
        Assert.AreEqual((D(1), D(2)), (periods.Single().From, periods.Single().To));
        Assert.AreEqual(0, AbsencePeriods.Build(Array.Empty<(DateOnly, int)>(), Working).Count);
    }
}
