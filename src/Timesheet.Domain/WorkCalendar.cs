namespace Timesheet.Domain;

public enum DayType
{
    Working,
    Weekend,

    /// <summary>A state holiday, or a religious holiday of the employee. Holiday codes apply even when it falls on a weekend (Q-03).</summary>
    Holiday
}

public enum HolidayKind
{
    None,
    State,
    Religious
}

public readonly record struct DayInfo(DateOnly Date, DayType Type, HolidayKind HolidayKind, string? HolidayName);

public sealed record Holiday(DateOnly Date, string Name);

/// <summary>
/// The calendar of a company: weekends and state holidays. Religious holidays are marked per employee (Q-14), so they
/// are passed in when a day is resolved. The days are computed, never typed (the Excel sheet labelled a Sunday as Thursday).
/// </summary>
public sealed class WorkCalendar
{
    private readonly Dictionary<DateOnly, string> _stateHolidays;

    public WorkCalendar(IEnumerable<Holiday>? stateHolidays = null)
    {
        _stateHolidays = (stateHolidays ?? Array.Empty<Holiday>()).GroupBy(h => h.Date).ToDictionary(g => g.Key, g => g.First().Name);
    }

    public static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public bool IsStateHoliday(DateOnly date) => _stateHolidays.ContainsKey(date);

    public DayInfo Resolve(DateOnly date, IReadOnlyCollection<DateOnly>? religiousHolidays = null)
    {
        if (_stateHolidays.TryGetValue(date, out var name)) return new DayInfo(date, DayType.Holiday, HolidayKind.State, name);
        if (religiousHolidays != null && religiousHolidays.Contains(date)) return new DayInfo(date, DayType.Holiday, HolidayKind.Religious, null);
        return new DayInfo(date, IsWeekend(date) ? DayType.Weekend : DayType.Working, HolidayKind.None, null);
    }

    public DayType DayTypeOf(DateOnly date, IReadOnlyCollection<DateOnly>? religiousHolidays = null) => Resolve(date, religiousHolidays).Type;

    /// <summary>Monday to Friday, state holidays included: the days the monthly fund is counted on</summary>
    public static int WeekdayCount(int year, int month, DateOnly? from = null, DateOnly? to = null)
    {
        var count = 0;
        foreach (var date in DaysOf(year, month))
        {
            if (from.HasValue && date < from.Value) continue;
            if (to.HasValue && date > to.Value) continue;
            if (!IsWeekend(date)) count++;
        }

        return count;
    }

    /// <summary>Weekdays that are not state holidays</summary>
    public int WorkingDayCount(int year, int month) => DaysOf(year, month).Count(d => !IsWeekend(d) && !IsStateHoliday(d));

    public static IEnumerable<DateOnly> DaysOf(int year, int month)
    {
        var days = DateTime.DaysInMonth(year, month);
        for (var day = 1; day <= days; day++) yield return new DateOnly(year, month, day);
    }

    /// <summary>The first working day on or after the date (used when a deadline falls on a weekend or a state holiday)</summary>
    public DateOnly NextWorkingDay(DateOnly date)
    {
        while (IsWeekend(date) || IsStateHoliday(date)) date = date.AddDays(1);
        return date;
    }
}

/// <summary>The rules about time that the instruction (S6) fixes; they are data so that the company can change them</summary>
public sealed record WorkingTimeSettings(
    TimeOnly RegularStart,
    TimeOnly RegularEnd,
    TimeOnly NightStart,
    TimeOnly NightEnd,
    int DailyNormMinutes,
    int GranularityMinutes)
{
    /// <summary>Regular 08:30 to 17:00 (8 h without the break), night 22:00 to 06:00, norm 8 h, half-hour steps</summary>
    public static WorkingTimeSettings Default { get; } = new(
        new TimeOnly(8, 30), new TimeOnly(17, 0), new TimeOnly(22, 0), new TimeOnly(6, 0), 8 * 60, 30);
}
