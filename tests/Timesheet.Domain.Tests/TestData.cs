using Timesheet.Domain;

namespace Timesheet.Domain.Tests;

internal static class T
{
    public static DateOnly D(int year, int month, int day) => new(year, month, day);

    public static DateOnly Sep(int day) => new(2026, 9, day);

    public static TimeOnly At(int hour, int minute = 0) => new(hour, minute);

    /// <summary>September 2026 as in the sample sheet: 22 working days, no state holiday</summary>
    public static WorkCalendar September => new();

    public static Func<DateOnly, DayType> TypeOf(WorkCalendar calendar, IReadOnlyCollection<DateOnly>? religious = null) =>
        d => calendar.DayTypeOf(d, religious);

    public static IEnumerable<DateOnly> WorkingDays(WorkCalendar calendar, int year = 2026, int month = 9) =>
        WorkCalendar.DaysOf(year, month).Where(d => !WorkCalendar.IsWeekend(d) && !calendar.IsStateHoliday(d));

    /// <summary>A month without findings: 8 regular hours on every working day</summary>
    public static List<DayEntry> FullMonth(WorkCalendar calendar, int year = 2026, int month = 9) =>
        WorkingDays(calendar, year, month).Select(d => DayEntry.Work(d)).ToList();

    public static TimesheetData Data(
        List<DayEntry> entries,
        IReadOnlyList<OvertimeRecord>? overtime = null,
        WorkCalendar? calendar = null,
        EmployeeStatus? employee = null,
        IReadOnlyCollection<DateOnly>? religious = null,
        int year = 2026,
        int month = 9) =>
        new("Z-001", year, month, entries, overtime ?? Array.Empty<OvertimeRecord>(), calendar ?? September, religious, null, employee);

    public static OvertimeRecord Ot(DateOnly date, TimeOnly start, TimeOnly end, string description = "Rad na projektu") =>
        new(date, start, end, description);

    public static IReadOnlyList<ValidationIssue> Rules(TimesheetData data, params string[] ids) =>
        TimesheetValidator.Validate(data).Where(i => ids.Contains(i.RuleId)).ToList();
}
