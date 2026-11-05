namespace Timesheet.Domain;

/// <summary>Colour group of the legend; it decides what a code means for the monthly fund of hours</summary>
public enum CodeColour
{
    /// <summary>Regular hours and whole-day absences (in the fund)</summary>
    Green,

    /// <summary>State holiday hours not worked (in the fund)</summary>
    Blue,

    /// <summary>Regular work on a holiday and night shift work (in the fund)</summary>
    Orange,

    /// <summary>Overtime (never in the fund)</summary>
    Red
}

public enum CodeUnit
{
    /// <summary>The entry is a number of hours</summary>
    Hours,

    /// <summary>The entry is the code itself for a whole day; a day counts as the daily norm (8 h)</summary>
    Day
}

/// <summary>One category of the legend of the sheet</summary>
public sealed record TimeCode(int Code, CodeColour Colour, CodeUnit Unit, bool Active = true)
{
    public bool IsOvertime => Colour == CodeColour.Red;

    /// <summary>Green, blue and orange codes form the monthly fund; overtime does not</summary>
    public bool InMonthlyFund => !IsOvertime;

    /// <summary>A whole-day code other than regular work: leave, sick leave, business trip, religious holiday</summary>
    public bool IsAbsence => Unit == CodeUnit.Day;

    /// <summary>
    /// The entry that fills a working day: regular hours (1010) or an absence code. A day has at most one
    /// (the absence codes are typed into the regular-work row of the sheet).
    /// </summary>
    public bool IsBaseEntry => Code == Codes.RegularWork || IsAbsence;

    public override string ToString() => Code.ToString();
}

/// <summary>The numbers of the legend, so that rules read as words</summary>
public static class Codes
{
    public const int RegularWork = 1010;
    public const int PaidLeave = 1020;
    public const int UnpaidLeave = 1021;
    public const int AnnualLeaveA = 1022;
    public const int AnnualLeaveB = 1023;
    public const int StateHoliday = 1024;
    public const int ReligiousHoliday = 1025;
    public const int BusinessTripDomestic = 1027;
    public const int BusinessTripAbroad = 1028;
    public const int BusinessTripNoPerDiem = 1029;
    public const int SickUpTo42Days = 1030;
    public const int SickOver42Days = 1031;
    public const int MaternityLeave = 1040;
    public const int HolidayWork = 2010;
    public const int RegularNightWork = 2011;
    public const int HolidayNightWork = 2012;
    public const int Overtime = 2020;
    public const int NightOvertime = 2021;
    public const int HolidayOvertime = 2022;
    public const int WeekendOvertime = 2023;
    public const int NightWeekendOvertime = 2024;
    public const int NightHolidayOvertime = 2025;

    public static bool IsAnnualLeave(int code) => code is AnnualLeaveA or AnnualLeaveB;

    public static bool IsBusinessTrip(int code) => code is BusinessTripDomestic or BusinessTripAbroad or BusinessTripNoPerDiem;

    public static bool IsSickLeave(int code) => code is SickUpTo42Days or SickOver42Days;
}

/// <summary>
/// The catalogue of the codes of the legend. It starts as the 22 codes of the legend of the sheet (the analysis says 20); the server
/// keeps the codes in its database, where the administrator can add codes and change them, and hands the list to the programs
/// (<see cref="Configure"/>). A code that was switched off stays here, so that old sheets still read, but <see cref="TimeCode.Active"/> is false.
/// </summary>
public static class TimeCodes
{
    /// <summary>The codes every installation has; the database is seeded with them</summary>
    public static IReadOnlyList<TimeCode> Defaults { get; } = new[]
    {
        new TimeCode(Codes.RegularWork, CodeColour.Green, CodeUnit.Hours),
        new TimeCode(Codes.PaidLeave, CodeColour.Green, CodeUnit.Day),
        new TimeCode(Codes.UnpaidLeave, CodeColour.Green, CodeUnit.Day),
        new TimeCode(Codes.AnnualLeaveA, CodeColour.Green, CodeUnit.Day),
        new TimeCode(Codes.AnnualLeaveB, CodeColour.Green, CodeUnit.Day),
        new TimeCode(Codes.StateHoliday, CodeColour.Blue, CodeUnit.Hours),
        new TimeCode(Codes.ReligiousHoliday, CodeColour.Green, CodeUnit.Day),
        new TimeCode(Codes.BusinessTripDomestic, CodeColour.Green, CodeUnit.Day),
        new TimeCode(Codes.BusinessTripAbroad, CodeColour.Green, CodeUnit.Day),
        new TimeCode(Codes.BusinessTripNoPerDiem, CodeColour.Green, CodeUnit.Day),
        new TimeCode(Codes.SickUpTo42Days, CodeColour.Green, CodeUnit.Day),
        new TimeCode(Codes.SickOver42Days, CodeColour.Green, CodeUnit.Day),
        new TimeCode(Codes.MaternityLeave, CodeColour.Green, CodeUnit.Day),
        new TimeCode(Codes.HolidayWork, CodeColour.Orange, CodeUnit.Hours),
        new TimeCode(Codes.RegularNightWork, CodeColour.Orange, CodeUnit.Hours),
        new TimeCode(Codes.HolidayNightWork, CodeColour.Orange, CodeUnit.Hours),
        new TimeCode(Codes.Overtime, CodeColour.Red, CodeUnit.Hours),
        new TimeCode(Codes.NightOvertime, CodeColour.Red, CodeUnit.Hours),
        new TimeCode(Codes.HolidayOvertime, CodeColour.Red, CodeUnit.Hours),
        new TimeCode(Codes.WeekendOvertime, CodeColour.Red, CodeUnit.Hours),
        new TimeCode(Codes.NightWeekendOvertime, CodeColour.Red, CodeUnit.Hours),
        new TimeCode(Codes.NightHolidayOvertime, CodeColour.Red, CodeUnit.Hours),
    };

    /// <summary>The list and its index are swapped together, so a reader never sees one without the other</summary>
    private sealed record Catalogue(IReadOnlyList<TimeCode> All, Dictionary<int, TimeCode> ByCode);

    private static volatile Catalogue _current = Build(Defaults);

    private static Catalogue Build(IEnumerable<TimeCode> codes)
    {
        var list = codes.OrderBy(c => c.Code).ToList();
        return new Catalogue(list, list.ToDictionary(c => c.Code));
    }

    /// <summary>Every code, active or not, in the order of the numbers</summary>
    public static IReadOnlyList<TimeCode> All => _current.All;

    /// <summary>Replaces the catalogue with the one the server holds. An empty list is ignored: the programs always know some codes.</summary>
    public static void Configure(IEnumerable<TimeCode> codes)
    {
        var list = codes.ToList();
        if (list.Count == 0) return;
        _current = Build(list);
    }

    /// <summary>Back to the codes every installation has (for tests)</summary>
    public static void Reset() => _current = Build(Defaults);

    public static bool TryGet(int code, out TimeCode timeCode) => _current.ByCode.TryGetValue(code, out timeCode!);

    public static TimeCode Get(int code) =>
        _current.ByCode.TryGetValue(code, out var found) ? found : throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown time code");

    public static bool Exists(int code) => _current.ByCode.ContainsKey(code);
}
