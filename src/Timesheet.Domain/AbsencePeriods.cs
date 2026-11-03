namespace Timesheet.Domain;

/// <summary>A run of days off with one code: from the first to the last working day it covers</summary>
public sealed record AbsencePeriod(int Code, DateOnly From, DateOnly To)
{
    public bool IsSingleDay => From == To;
}

/// <summary>
/// Joins the days of an absence into the periods people write in a message ("business trip 21.10.-23.10."). Days are joined when
/// every day between them is a day off anyway (a weekend or a holiday), so leave from Friday to Monday is one period.
/// </summary>
public static class AbsencePeriods
{
    /// <param name="days">Every day of the month with the base code entered on it (the absence), and whether it is a working day</param>
    /// <param name="isWorkingDay">Whether a date is a working day, for the days between two entries</param>
    public static IReadOnlyList<AbsencePeriod> Build(IEnumerable<(DateOnly Date, int Code)> days, Func<DateOnly, bool> isWorkingDay)
    {
        var result = new List<AbsencePeriod>();
        foreach (var (date, code) in days.OrderBy(d => d.Date))
        {
            if (result.Count > 0 && result[^1].Code == code && JoinsWith(result[^1].To, date, isWorkingDay))
                result[^1] = result[^1] with { To = date };
            else
                result.Add(new AbsencePeriod(code, date, date));
        }

        return result;
    }

    private static bool JoinsWith(DateOnly last, DateOnly next, Func<DateOnly, bool> isWorkingDay)
    {
        for (var between = last.AddDays(1); between < next; between = between.AddDays(1))
        {
            if (isWorkingDay(between)) return false;
        }

        return true;
    }
}
