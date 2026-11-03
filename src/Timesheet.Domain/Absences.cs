namespace Timesheet.Domain;

public enum SkipReason
{
    Weekend,
    StateHoliday,
    ReligiousHoliday
}

public sealed record SkippedDay(DateOnly Date, SkipReason Reason);

/// <summary>The days a range of one absence code covers, and the days it leaves out (shown to the employee before saving)</summary>
public sealed record AbsenceExpansion(int Code, IReadOnlyList<DateOnly> Days, IReadOnlyList<SkippedDay> Skipped);

public static class AbsenceRanges
{
    /// <summary>
    /// Expands "code + from + to" into the days that receive the code. Weekends carry no absence entries (Q-08), and a
    /// state holiday uses 1024 and 2010 instead of a code in the regular-work row (S6). Religious holidays of the
    /// employee are left out too, because they have their own code 1025; that code itself is entered on those days.
    /// </summary>
    public static AbsenceExpansion Expand(
        int code,
        DateOnly from,
        DateOnly to,
        WorkCalendar calendar,
        IReadOnlyCollection<DateOnly>? religiousHolidays = null)
    {
        if (!TimeCodes.TryGet(code, out var timeCode) || !timeCode.IsAbsence)
            throw new ArgumentException($"{code} is not a whole-day absence code.", nameof(code));
        if (to < from) throw new ArgumentException("The range ends before it starts.", nameof(to));
        if (to.DayNumber - from.DayNumber > 366) throw new ArgumentException("A range can span at most a year.", nameof(to));

        var days = new List<DateOnly>();
        var skipped = new List<SkippedDay>();
        var forReligiousHoliday = code == Codes.ReligiousHoliday;

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (calendar.IsStateHoliday(date))
            {
                skipped.Add(new SkippedDay(date, SkipReason.StateHoliday));
            }
            else if (WorkCalendar.IsWeekend(date))
            {
                skipped.Add(new SkippedDay(date, SkipReason.Weekend));
            }
            else if (!forReligiousHoliday && religiousHolidays != null && religiousHolidays.Contains(date))
            {
                skipped.Add(new SkippedDay(date, SkipReason.ReligiousHoliday));
            }
            else
            {
                days.Add(date);
            }
        }

        return new AbsenceExpansion(code, days, skipped);
    }
}
