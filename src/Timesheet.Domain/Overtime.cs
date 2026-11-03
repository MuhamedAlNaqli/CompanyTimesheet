namespace Timesheet.Domain;

/// <summary>
/// One overtime occurrence as the employee enters it: a date, a start and an end. An end that is not after the start
/// means the work continues past midnight (S6 allows it). The total is always computed, never typed.
/// </summary>
public sealed record OvertimeInterval(DateOnly Date, TimeOnly Start, TimeOnly End)
{
    public const int MinutesPerDay = 24 * 60;

    public bool CrossesMidnight => End <= Start;

    /// <summary>Zero when start and end are equal (that is not a valid interval)</summary>
    public int TotalMinutes
    {
        get
        {
            var start = Start.Hour * 60 + Start.Minute;
            var end = End.Hour * 60 + End.Minute;
            if (start == end) return 0;
            return end > start ? end - start : MinutesPerDay - start + end;
        }
    }

    /// <summary>The interval on a single time line in minutes from midnight of <see cref="Date"/> (the end may exceed 1440)</summary>
    public (int Start, int End) Absolute
    {
        get
        {
            var start = Start.Hour * 60 + Start.Minute;
            return (start, start + TotalMinutes);
        }
    }
}

/// <summary>Minutes of an overtime interval that belong to one date and one overtime code</summary>
public sealed record OvertimeAllocation(DateOnly Date, int Code, int Minutes)
{
    public decimal Hours => Minutes / 60m;
}

/// <summary>Minutes of an overtime interval that fall inside the regular working window, where overtime is not allowed (V-03)</summary>
public sealed record RegularWindowConflict(DateOnly Date, int Minutes);

public sealed record OvertimeClassification(
    int TotalMinutes,
    IReadOnlyList<OvertimeAllocation> Allocations,
    IReadOnlyList<RegularWindowConflict> Conflicts)
{
    public bool IsValid => TotalMinutes > 0 && Conflicts.Count == 0;
}

/// <summary>
/// Splits an overtime interval at the boundaries of the instruction (06:00, 08:30, 17:00, 22:00 and midnight) and gives
/// every part the code of its date and time window (S6):
/// <list type="bullet">
/// <item>working day: 2020 between 06:00 and 08:30 and between 17:00 and 22:00, 2021 at night</item>
/// <item>holiday: 2022 in the same windows, 2025 at night</item>
/// <item>weekend: 2023 from 06:00 to 22:00, 2024 at night</item>
/// </list>
/// Hours after midnight belong to the next date and are classified by that date's type.
/// </summary>
public static class OvertimeClassifier
{
    private enum Window { Night, Early, Regular, Evening }

    public static OvertimeClassification Classify(
        OvertimeInterval interval,
        Func<DateOnly, DayType> dayTypeOf,
        WorkingTimeSettings? settings = null)
    {
        settings ??= WorkingTimeSettings.Default;
        var total = interval.TotalMinutes;
        if (total == 0) return new OvertimeClassification(0, Array.Empty<OvertimeAllocation>(), Array.Empty<RegularWindowConflict>());

        var nightEnd = Minutes(settings.NightEnd);
        var regularStart = Minutes(settings.RegularStart);
        var regularEnd = Minutes(settings.RegularEnd);
        var nightStart = Minutes(settings.NightStart);

        // The windows of one calendar day, in order
        var windows = new (int From, int To, Window Kind)[]
        {
            (0, nightEnd, Window.Night),
            (nightEnd, regularStart, Window.Early),
            (regularStart, regularEnd, Window.Regular),
            (regularEnd, nightStart, Window.Evening),
            (nightStart, OvertimeInterval.MinutesPerDay, Window.Night)
        };

        var (start, end) = interval.Absolute;
        var allocations = new Dictionary<(DateOnly, int), int>();
        var conflicts = new Dictionary<DateOnly, int>();

        for (var dayOffset = 0; dayOffset <= 1; dayOffset++)
        {
            var date = interval.Date.AddDays(dayOffset);
            var dayFrom = dayOffset * OvertimeInterval.MinutesPerDay;
            var dayType = dayTypeOf(date);

            foreach (var (from, to, kind) in windows)
            {
                var overlap = Math.Min(end, dayFrom + to) - Math.Max(start, dayFrom + from);
                if (overlap <= 0) continue;

                if (kind == Window.Regular && dayType != DayType.Weekend)
                {
                    // Work inside 08:30 to 17:00 on a working day or a holiday is regular time, not overtime. On a weekend there is no
                    // regular time, so it is overtime 2023.
                    conflicts[date] = conflicts.GetValueOrDefault(date) + overlap;
                    continue;
                }

                var code = CodeFor(dayType, kind == Window.Night);
                allocations[(date, code)] = allocations.GetValueOrDefault((date, code)) + overlap;
            }
        }

        return new OvertimeClassification(
            total,
            allocations.Select(a => new OvertimeAllocation(a.Key.Item1, a.Key.Item2, a.Value)).OrderBy(a => a.Date).ThenBy(a => a.Code).ToList(),
            conflicts.Select(c => new RegularWindowConflict(c.Key, c.Value)).OrderBy(c => c.Date).ToList());
    }

    public static int CodeFor(DayType dayType, bool night) => dayType switch
    {
        DayType.Working => night ? Codes.NightOvertime : Codes.Overtime,
        DayType.Holiday => night ? Codes.NightHolidayOvertime : Codes.HolidayOvertime,
        _ => night ? Codes.NightWeekendOvertime : Codes.WeekendOvertime
    };

    private static int Minutes(TimeOnly time) => time.Hour * 60 + time.Minute;
}
