namespace Timesheet.Domain;

public enum Severity
{
    /// <summary>Submission is impossible while it is violated</summary>
    Blocking,

    /// <summary>Submission is allowed with an acknowledgement comment that the reviewers see</summary>
    Warning,

    Info
}

/// <summary>
/// One finding of a rule. <see cref="RuleId"/> links it to the source of the rule (V-01 to V-21). The finding says what was found
/// (<see cref="Values"/>: the date, the code, the minutes ...), not how to word it: wording is for the program that shows it, in
/// the language of the people who read it.
/// </summary>
public sealed record ValidationIssue(string RuleId, Severity Severity, DateOnly? Date, IReadOnlyDictionary<string, object> Values, string? Variant = null)
{
    /// <summary>The rule and, for a rule that finds two kinds of things, which of them ("V-08.inactive")</summary>
    public string Key => Variant == null ? RuleId : RuleId + "." + Variant;
}

/// <summary>An overtime record as entered: when, and why (the description justifies the overtime, Q-23)</summary>
public sealed record OvertimeRecord(
    DateOnly Date,
    TimeOnly Start,
    TimeOnly End,
    string Description,
    int? DeclaredTotalMinutes = null,
    bool CategoriesOverridden = false)
{
    public OvertimeInterval Interval => new(Date, Start, End);
}

/// <summary>What the master data says about the employee for the month</summary>
public sealed record EmployeeStatus(bool IsKnown = true, DateOnly? ActiveFrom = null, DateOnly? ActiveTo = null)
{
    public bool IsActiveOn(DateOnly date) =>
        IsKnown && (!ActiveFrom.HasValue || date >= ActiveFrom.Value) && (!ActiveTo.HasValue || date <= ActiveTo.Value);
}

/// <summary>Everything the rules need about one employee's month</summary>
public sealed record TimesheetData(
    string EmployeeCode,
    int Year,
    int Month,
    IReadOnlyList<DayEntry> Entries,
    IReadOnlyList<OvertimeRecord> Overtime,
    WorkCalendar Calendar,
    IReadOnlyCollection<DateOnly>? ReligiousHolidays = null,
    WorkingTimeSettings? Settings = null,
    EmployeeStatus? Employee = null);

/// <summary>The configurable severity of every rule (NFR-08: rules are data, not code)</summary>
public sealed class ValidationSettings
{
    public static IReadOnlyDictionary<string, Severity> Defaults { get; } = new Dictionary<string, Severity>
    {
        ["V-01"] = Severity.Blocking,
        ["V-02"] = Severity.Blocking,
        ["V-03"] = Severity.Blocking,
        ["V-04"] = Severity.Warning,
        ["V-05"] = Severity.Blocking,
        ["V-06"] = Severity.Blocking,
        ["V-07"] = Severity.Blocking,
        ["V-08"] = Severity.Blocking,
        ["V-09"] = Severity.Blocking,
        ["V-10"] = Severity.Blocking,
        ["V-11"] = Severity.Blocking,
        ["V-12"] = Severity.Warning,
        ["V-13"] = Severity.Blocking,
        ["V-14"] = Severity.Warning,
        ["V-15"] = Severity.Blocking,
        ["V-17"] = Severity.Blocking,
        ["V-18"] = Severity.Warning,
        ["V-19"] = Severity.Blocking,
        ["V-19b"] = Severity.Blocking,
        ["V-19c"] = Severity.Warning,
        ["V-20"] = Severity.Blocking,
        ["V-21"] = Severity.Warning
    };

    private readonly Dictionary<string, Severity> _overrides = new();

    public ValidationSettings Set(string ruleId, Severity severity)
    {
        _overrides[ruleId] = severity;
        return this;
    }

    public Severity For(string ruleId) =>
        _overrides.TryGetValue(ruleId, out var overridden) ? overridden : Defaults.GetValueOrDefault(ruleId, Severity.Blocking);
}

/// <summary>
/// The automated checks of the analysis (V-01 to V-21) as plain code. The employee sees the result while entering and at
/// submission. Passing the checks never approves anything; a person always decides.
/// </summary>
public static class TimesheetValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(TimesheetData data, ValidationSettings? settings = null)
    {
        settings ??= new ValidationSettings();
        var wt = data.Settings ?? WorkingTimeSettings.Default;
        var employee = data.Employee ?? new EmployeeStatus();
        var religious = data.ReligiousHolidays;
        var issues = new List<ValidationIssue>();

        void Add(string rule, DateOnly? date, params (string Name, object Value)[] values) =>
            issues.Add(new ValidationIssue(rule, settings.For(rule), date, values.ToDictionary(v => v.Name, v => v.Value)));
        void AddVariant(string rule, string variant, DateOnly? date, params (string Name, object Value)[] values) =>
            issues.Add(new ValidationIssue(rule, settings.For(rule), date, values.ToDictionary(v => v.Name, v => v.Value), variant));

        var first = new DateOnly(data.Year, data.Month, 1);
        var last = new DateOnly(data.Year, data.Month, DateTime.DaysInMonth(data.Year, data.Month));
        DayInfo Day(DateOnly date) => data.Calendar.Resolve(date, religious);
        var baseEntries = data.Entries.Where(e => e.TimeCode.IsBaseEntry).ToList();
        var baseByDate = baseEntries.GroupBy(e => e.Date).ToDictionary(g => g.Key, g => g.ToList());

        // V-08: unknown or inactive employee
        if (!employee.IsKnown)
            AddVariant("V-08", "unknown", null, ("employee", data.EmployeeCode));
        else if ((employee.ActiveFrom.HasValue && employee.ActiveFrom.Value > last) || (employee.ActiveTo.HasValue && employee.ActiveTo.Value < first))
            AddVariant("V-08", "inactive", null, ("employee", data.EmployeeCode));

        foreach (var entry in data.Entries)
        {
            var info = Day(entry.Date);
            var code = entry.TimeCode;

            // V-02: annual leave on a weekend. V-17: any other absence on a weekend (1040 included)
            if (WorkCalendar.IsWeekend(entry.Date) && code.IsAbsence)
            {
                if (Codes.IsAnnualLeave(code.Code))
                    Add("V-02", entry.Date, ("code", code.Code), ("date", entry.Date));
                else
                    Add("V-17", entry.Date, ("code", code.Code), ("date", entry.Date));
            }

            // V-06: regular work on a weekend
            if (code.Code == Codes.RegularWork && WorkCalendar.IsWeekend(entry.Date))
                Add("V-06", entry.Date, ("date", entry.Date));

            // V-07: anything in the regular-work row on a state holiday
            if (code.IsBaseEntry && info.HolidayKind == HolidayKind.State)
                Add("V-07", entry.Date, ("date", entry.Date), ("code", code.Code));

            // V-14: regular hours differ from the daily norm
            if (code.Code == Codes.RegularWork && info.Type == DayType.Working && entry.Minutes != wt.DailyNormMinutes)
                Add("V-14", entry.Date, ("date", entry.Date), ("minutes", entry.Minutes), ("normMinutes", wt.DailyNormMinutes));

            // V-12: granularity
            if (code.Unit == CodeUnit.Hours && wt.GranularityMinutes > 0 && entry.Minutes % wt.GranularityMinutes != 0)
                AddVariant("V-12", "hours", entry.Date, ("date", entry.Date), ("step", wt.GranularityMinutes));
        }

        // V-05: more than one base entry on a day
        foreach (var (date, list) in baseByDate.Where(p => p.Value.Count > 1))
            Add("V-05", date, ("date", date), ("codes", string.Join(", ", list.Select(e => e.Code))));

        // V-04: a working day without a base entry (state holidays use 1024/2010, so they are not expected here)
        foreach (var date in WorkCalendar.DaysOf(data.Year, data.Month))
        {
            if (!employee.IsActiveOn(date) || WorkCalendar.IsWeekend(date)) continue;
            var info = Day(date);
            if (info.HolidayKind == HolidayKind.State) continue;
            if (!baseByDate.ContainsKey(date))
                Add("V-04", date, ("date", date));
        }

        // V-20: state holiday on a weekday: 1024 + 2010 must equal the daily norm
        foreach (var date in WorkCalendar.DaysOf(data.Year, data.Month))
        {
            if (WorkCalendar.IsWeekend(date) || !data.Calendar.IsStateHoliday(date)) continue;
            var holiday = data.Entries.Where(e => e.Date == date && e.Code == Codes.StateHoliday).Sum(e => e.Minutes);
            var worked = data.Entries.Where(e => e.Date == date && e.Code == Codes.HolidayWork).Sum(e => e.Minutes);
            if ((holiday > 0 || worked > 0) && holiday + worked != wt.DailyNormMinutes)
                Add("V-20", date, ("date", date), ("holidayMinutes", holiday), ("workedMinutes", worked), ("normMinutes", wt.DailyNormMinutes));
        }

        // Overtime
        var intervals = data.Overtime.Select(o => (Record: o, Interval: o.Interval)).ToList();
        foreach (var (record, interval) in intervals)
        {
            var classification = OvertimeClassifier.Classify(interval, d => Day(d).Type, wt);

            // V-15: start and end must differ (an end before the start means past midnight, which is allowed)
            if (interval.TotalMinutes == 0)
            {
                Add("V-15", record.Date, ("date", record.Date));
                continue;
            }

            // V-01: the total must match the interval (matters for imported or legacy data)
            if (record.DeclaredTotalMinutes.HasValue && record.DeclaredTotalMinutes.Value != interval.TotalMinutes)
                Add("V-01", record.Date, ("date", record.Date), ("declaredMinutes", record.DeclaredTotalMinutes.Value), ("intervalMinutes", interval.TotalMinutes));

            // V-03: overtime inside the regular window
            foreach (var conflict in classification.Conflicts)
                Add("V-03", conflict.Date, ("date", record.Date), ("conflictDate", conflict.Date), ("regularStart", wt.RegularStart), ("regularEnd", wt.RegularEnd));

            // V-13: the description justifies the overtime
            if (string.IsNullOrWhiteSpace(record.Description))
                Add("V-13", record.Date, ("date", record.Date));

            // V-12 for the interval ends
            if (wt.GranularityMinutes > 0 && (interval.Absolute.Start % wt.GranularityMinutes != 0 || interval.Absolute.End % wt.GranularityMinutes != 0))
                AddVariant("V-12", "overtime", record.Date, ("date", record.Date), ("step", wt.GranularityMinutes));

            // V-18: categories changed by hand
            if (record.CategoriesOverridden)
                Add("V-18", record.Date, ("date", record.Date));

            // V-19 / V-19b / V-19c: overtime on a day with an absence
            foreach (var date in classification.Allocations.Select(a => a.Date).Concat(classification.Conflicts.Select(c => c.Date)).Distinct())
            {
                if (!baseByDate.TryGetValue(date, out var onDay)) continue;
                foreach (var absence in onDay.Where(e => e.TimeCode.IsAbsence))
                {
                    if (Codes.IsBusinessTrip(absence.Code))
                        Add("V-19", date, ("code", absence.Code), ("date", date));
                    else if (Codes.IsSickLeave(absence.Code))
                        Add("V-19b", date, ("code", absence.Code), ("date", date));
                    else if (!Codes.IsAnnualLeave(absence.Code))
                        Add("V-19c", date, ("code", absence.Code), ("date", date));
                }
            }
        }

        // V-09 and V-10: overlapping and duplicated overtime
        for (var i = 0; i < intervals.Count; i++)
        {
            for (var j = i + 1; j < intervals.Count; j++)
            {
                var a = intervals[i].Interval;
                var b = intervals[j].Interval;
                if (a.TotalMinutes == 0 || b.TotalMinutes == 0) continue;

                if (a.Date == b.Date && a.Start == b.Start && a.End == b.End)
                {
                    Add("V-10", a.Date, ("date", a.Date), ("start", a.Start), ("end", a.End));
                    continue;
                }

                var offsetB = (b.Date.DayNumber - a.Date.DayNumber) * OvertimeInterval.MinutesPerDay;
                var (aStart, aEnd) = a.Absolute;
                var (bStart, bEnd) = (b.Absolute.Start + offsetB, b.Absolute.End + offsetB);
                if (aStart < bEnd && bStart < aEnd)
                    Add("V-09", a.Date, ("date", a.Date), ("start", a.Start), ("end", a.End), ("otherDate", b.Date), ("otherStart", b.Start), ("otherEnd", b.End));
            }
        }

        // V-11: more than 24 hours on one date (hours entries plus the overtime parts that fall on the date)
        var classified = data.Overtime
            .Where(o => o.Interval.TotalMinutes > 0)
            .Select(o => OvertimeClassifier.Classify(o.Interval, d => Day(d).Type, wt))
            .ToList();
        var allocations = classified.SelectMany(c => c.Allocations).ToList();
        var perDate = data.Entries.Where(e => e.TimeCode.Unit == CodeUnit.Hours).Select(e => (e.Date, e.Minutes))
            .Concat(allocations.Select(a => (a.Date, a.Minutes)))
            .Concat(classified.SelectMany(c => c.Conflicts).Select(c => (c.Date, c.Minutes)))
            .GroupBy(x => x.Date)
            .Select(g => (Date: g.Key, Minutes: g.Sum(x => x.Minutes)));
        foreach (var (date, minutes) in perDate.Where(p => p.Minutes > OvertimeInterval.MinutesPerDay))
            Add("V-11", date, ("date", date), ("minutes", minutes));

        // V-21: the monthly fund is lower than expected (a higher fund is fine, Q-36)
        var summary = MonthlySummaryCalculator.Compute(data.Entries, allocations, wt, data.Calendar);
        var expected = MonthlySummaryCalculator.ExpectedFundMinutes(data.Year, data.Month, wt, employee.ActiveFrom, employee.ActiveTo);
        if (employee.IsKnown && summary.FundMinutes < expected)
            Add("V-21", null, ("fundMinutes", summary.FundMinutes), ("expectedMinutes", expected));

        return issues.OrderBy(i => i.Date ?? DateOnly.MinValue).ThenBy(i => i.RuleId, StringComparer.Ordinal).ToList();
    }

    /// <summary>V-16: employees in scope who are missing from the timesheet of the month</summary>
    public static IReadOnlyList<string> MissingEmployees(IEnumerable<string> expected, IEnumerable<string> present)
    {
        var have = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);
        return expected.Where(e => !have.Contains(e)).ToList();
    }
}
