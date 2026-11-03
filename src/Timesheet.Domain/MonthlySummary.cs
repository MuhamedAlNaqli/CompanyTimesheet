namespace Timesheet.Domain;

/// <summary>
/// One cell of the month: regular hours or an hour-based code on a date (<see cref="Minutes"/>), or a whole-day
/// absence code (minutes are ignored; the day counts as the daily norm). Overtime is not entered here; it is derived
/// from the overtime intervals.
/// </summary>
public sealed record DayEntry(DateOnly Date, int Code, int Minutes = 0)
{
    public TimeCode TimeCode => TimeCodes.Get(Code);

    public static DayEntry Work(DateOnly date, decimal hours = 8m, int code = Codes.RegularWork) =>
        new(date, code, (int)Math.Round(hours * 60m));

    public static DayEntry Absence(DateOnly date, int code) => new(date, code);
}

/// <summary>Quantities per code for one employee and month; the application delivers quantities, finance applies rates (Q-04)</summary>
public sealed record MonthlySummary(
    IReadOnlyDictionary<int, int> MinutesByCode,
    IReadOnlyDictionary<int, int> DaysByCode,
    IReadOnlyDictionary<int, int> WorkingDaysByCode,
    int FundMinutes,
    int OvertimeMinutes)
{
    public decimal Hours(int code) => MinutesByCode.GetValueOrDefault(code) / 60m;

    public int Days(int code) => DaysByCode.GetValueOrDefault(code);

    public int WorkingDays(int code) => WorkingDaysByCode.GetValueOrDefault(code);

    public decimal FundHours => FundMinutes / 60m;

    public decimal OvertimeHours => OvertimeMinutes / 60m;
}

public static class MonthlySummaryCalculator
{
    /// <summary>
    /// Sums the month. The monthly fund is the green, blue and orange codes: regular hours, the hours of state holidays
    /// and of holiday or night regular work, and every whole-day absence at the daily norm. Overtime (red) is not in it.
    /// Absence codes are never counted as hours: the Excel sheet stored them in the hours row, so a plain sum
    /// of that row added the code numbers up as hours.
    /// </summary>
    public static MonthlySummary Compute(
        IEnumerable<DayEntry> entries,
        IEnumerable<OvertimeAllocation> overtime,
        WorkingTimeSettings? settings = null,
        WorkCalendar? calendar = null)
    {
        settings ??= WorkingTimeSettings.Default;
        var minutes = new Dictionary<int, int>();
        var days = new Dictionary<int, HashSet<DateOnly>>();
        var workingDays = new Dictionary<int, HashSet<DateOnly>>();
        var fund = 0;

        foreach (var entry in entries)
        {
            var code = entry.TimeCode;
            Add(days, code.Code, entry.Date);
            if (calendar != null && !WorkCalendar.IsWeekend(entry.Date) && !calendar.IsStateHoliday(entry.Date))
                Add(workingDays, code.Code, entry.Date);

            if (code.Unit == CodeUnit.Hours)
            {
                minutes[code.Code] = minutes.GetValueOrDefault(code.Code) + entry.Minutes;
                if (code.InMonthlyFund) fund += entry.Minutes;
            }
            else
            {
                fund += settings.DailyNormMinutes;
            }
        }

        var overtimeMinutes = 0;
        foreach (var allocation in overtime)
        {
            minutes[allocation.Code] = minutes.GetValueOrDefault(allocation.Code) + allocation.Minutes;
            Add(days, allocation.Code, allocation.Date);
            overtimeMinutes += allocation.Minutes;
        }

        return new MonthlySummary(
            minutes,
            days.ToDictionary(d => d.Key, d => d.Value.Count),
            workingDays.ToDictionary(d => d.Key, d => d.Value.Count),
            fund,
            overtimeMinutes);
    }

    /// <summary>What a full-time employee is expected to reach: weekdays (state holidays on weekdays included) times the daily norm</summary>
    public static int ExpectedFundMinutes(int year, int month, WorkingTimeSettings? settings = null, DateOnly? activeFrom = null, DateOnly? activeTo = null) =>
        WorkCalendar.WeekdayCount(year, month, activeFrom, activeTo) * (settings ?? WorkingTimeSettings.Default).DailyNormMinutes;

    private static void Add(Dictionary<int, HashSet<DateOnly>> map, int code, DateOnly date)
    {
        if (!map.TryGetValue(code, out var set)) map[code] = set = new HashSet<DateOnly>();
        set.Add(date);
    }
}
