using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Timesheet.Domain.Tests;

/// <summary>
/// The September 2026 sample (the sheet S1 and the overtime register S2) as the acceptance test of the calculations:
/// the derived monthly summary of data-model.md section 6.2 must come out exactly.
/// </summary>
[TestClass]
public class SampleSheetFixtureTests
{
    private sealed record Sample(string Code, int WorkDays, int AnnualDays, OvertimeRecord[] Overtime, int Maternity = 0);

    private static readonly Sample[] Employees =
    {
        new("Z-127", 11, 11, Array.Empty<OvertimeRecord>()),
        new("Z-230", 21, 1, Array.Empty<OvertimeRecord>()),
        new("Z-232", 13, 9, Array.Empty<OvertimeRecord>()),
        new("Z-092", 21, 1, new[] { T.Ot(T.Sep(5), T.At(8, 30), T.At(11)), T.Ot(T.Sep(19), T.At(8, 30), T.At(11)) }),
        new("Z-245", 22, 0, new[]
        {
            T.Ot(T.Sep(1), T.At(19), T.At(21)), T.Ot(T.Sep(2), T.At(19), T.At(22)), T.Ot(T.Sep(7), T.At(20), T.At(22, 30))
        }),
        new("Z-247", 22, 0, new[] { T.Ot(T.Sep(3), T.At(21), T.At(23)) }),
        new("Z-188", 15, 7, Array.Empty<OvertimeRecord>()),
        new("Z-251", 0, 0, Array.Empty<OvertimeRecord>(), Maternity: 22),
        new("Z-255", 16, 6, Array.Empty<OvertimeRecord>()),
        new("Z-257", 22, 0, Array.Empty<OvertimeRecord>()),
        new("Z-269", 22, 0, Array.Empty<OvertimeRecord>()),
    };

    private static (TimesheetData Data, MonthlySummary Summary) Build(Sample sample)
    {
        var days = T.WorkingDays(T.September).ToList();
        var entries = new List<DayEntry>();
        entries.AddRange(days.Take(sample.AnnualDays).Select(d => DayEntry.Absence(d, Codes.AnnualLeaveA)));
        entries.AddRange(days.Skip(sample.AnnualDays).Take(sample.WorkDays).Select(d => DayEntry.Work(d)));
        entries.AddRange(days.Take(sample.Maternity).Select(d => DayEntry.Absence(d, Codes.MaternityLeave)));

        var data = T.Data(entries, sample.Overtime) with { EmployeeCode = sample.Code };
        var allocations = sample.Overtime
            .SelectMany(o => OvertimeClassifier.Classify(o.Interval, T.TypeOf(T.September)).Allocations).ToList();
        return (data, MonthlySummaryCalculator.Compute(entries, allocations, null, T.September));
    }

    [TestMethod]
    public void EveryEmployee_ShouldReachTheMonthlyFundOf176Hours()
    {
        foreach (var sample in Employees)
        {
            var (_, summary) = Build(sample);
            Assert.AreEqual(176m, summary.FundHours, sample.Code);
        }
    }

    [TestMethod]
    public void SheetTotals_ShouldMatchTheDerivedSummary()
    {
        var summaries = Employees.Select(e => Build(e).Summary).ToList();

        Assert.AreEqual(1480m, summaries.Sum(s => s.Hours(Codes.RegularWork)), "1010 hours");
        Assert.AreEqual(185, summaries.Sum(s => s.Days(Codes.RegularWork)), "1010 days");
        Assert.AreEqual(35, summaries.Sum(s => s.Days(Codes.AnnualLeaveA)), "1022 days");
        Assert.AreEqual(8.0m, summaries.Sum(s => s.Hours(Codes.Overtime)), "2020 hours");
        Assert.AreEqual(1.5m, summaries.Sum(s => s.Hours(Codes.NightOvertime)), "2021 hours");
        Assert.AreEqual(5.0m, summaries.Sum(s => s.Hours(Codes.WeekendOvertime)), "2023 hours");
    }

    [TestMethod]
    public void PerEmployee_ShouldMatchTheTableOfTheAnalysis()
    {
        var belmin = Build(Employees.Single(e => e.Code == "Z-245")).Summary;
        Assert.AreEqual(176m, belmin.Hours(Codes.RegularWork));
        Assert.AreEqual(7.0m, belmin.Hours(Codes.Overtime));
        Assert.AreEqual(0.5m, belmin.Hours(Codes.NightOvertime));

        var emrah = Build(Employees.Single(e => e.Code == "Z-247")).Summary;
        Assert.AreEqual(1.0m, emrah.Hours(Codes.Overtime));
        Assert.AreEqual(1.0m, emrah.Hours(Codes.NightOvertime));

        var dino = Build(Employees.Single(e => e.Code == "Z-127")).Summary;
        Assert.AreEqual(88m, dino.Hours(Codes.RegularWork));
        Assert.AreEqual(11, dino.Days(Codes.AnnualLeaveA));
    }

    [TestMethod]
    public void TheSampleEnteredAsItIs_ShouldHaveNoBlockingFindings()
    {
        foreach (var sample in Employees)
        {
            var issues = TimesheetValidator.Validate(Build(sample).Data);
            var blocking = issues.Where(i => i.Severity == Severity.Blocking).ToList();
            Assert.AreEqual(0, blocking.Count, $"{sample.Code}: {string.Join(" | ", blocking.Select(b => b.Key + " " + string.Join(", ", b.Values.Select(v => v.Key + "=" + v.Value))))}");
        }
    }

    [TestMethod]
    public void MaternityLeaveOnWeekends_ShouldBeReportedAndPushTheFundTo240()
    {
        // The sample of Z-251 has 1040 on all 30 days. The stakeholders confirmed (Q-08) that weekends are not marked.
        var entries = WorkCalendar.DaysOf(2026, 9).Select(d => DayEntry.Absence(d, Codes.MaternityLeave)).ToList();
        var summary = MonthlySummaryCalculator.Compute(entries, Array.Empty<OvertimeAllocation>(), null, T.September);
        var issues = TimesheetValidator.Validate(T.Data(entries));

        Assert.AreEqual(240m, summary.FundHours);
        Assert.AreEqual(8, issues.Count(i => i.RuleId == "V-17" && i.Severity == Severity.Blocking), "one finding per weekend day");
        Assert.AreEqual(0, issues.Count(i => i.RuleId == "V-21"), "a fund above 176 h is not an error (Q-36)");
    }
}
