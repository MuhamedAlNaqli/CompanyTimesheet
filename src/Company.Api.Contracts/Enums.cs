using System.Text.Json.Serialization;

namespace Company.Api.Contracts;

// These travel as their names ("Submitted"), so a person reading the traffic or the stored revisions can read them, and the
// order of the members may change without changing what was stored.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SheetStatus
{
    /// <summary>The employee is filling in the month</summary>
    Draft,

    /// <summary>Frozen and waiting at one step of the approval chain</summary>
    Submitted,

    /// <summary>Returned to the employee with a message</summary>
    Returned,

    /// <summary>Every step of the chain approved; locked</summary>
    Approved
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DayKind
{
    Working,
    Weekend,
    Holiday
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IssueSeverity
{
    /// <summary>Stops the submission</summary>
    Blocking,

    Warning,

    Info
}

/// <summary>What happened to a sheet, as the history shows it</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HistoryKind
{
    Submitted,
    StepApproved,
    Approved,
    Returned,
    Answered,

    /// <summary>The administrator handed the waiting step to somebody else</summary>
    Reassigned
}

/// <summary>What is due for a person today</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReminderType
{
    SubmitSoon,
    SubmitOverdue,
    FixReturned,
    ApproveWaiting,
    ApproveOverdue,

    /// <summary>The supervisor asked the person, by name, to submit the timesheet (not a rule of the domain)</summary>
    Nudged
}

/// <summary>Why a day was left out of an absence</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SkippedBecause
{
    Weekend,
    StateHoliday,
    ReligiousHoliday
}

/// <summary>
/// The texts of the interface that several programs show the same way (the desktop app, the printed sheet): months, statuses, kinds
/// of history. Bosnian is the language of the employee side. They are kept here, in one place, and not in the domain library.
/// </summary>
public static class BosnianTexts
{
    public static readonly string[] MonthNames =
    {
        "Januar", "Februar", "Mart", "April", "Maj", "Juni", "Juli", "August", "Septembar", "Oktobar", "Novembar", "Decembar"
    };

    /// <summary>"Septembar 2026."</summary>
    public static string Month(int year, int month) => $"{MonthNames[month - 1]} {year}.";

    /// <summary>The weekday letter in the header of the sheet: P U S Č P S N (Ponedjeljak, Utorak, Srijeda, Četvrtak, Petak, Subota, Nedjelja)</summary>
    public static string WeekdayInitial(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "P",
        DayOfWeek.Tuesday => "U",
        DayOfWeek.Wednesday => "S",
        DayOfWeek.Thursday => "Č",
        DayOfWeek.Friday => "P",
        DayOfWeek.Saturday => "S",
        _ => "N"
    };

    public static string Status(SheetStatus status) => status switch
    {
        SheetStatus.Draft => "U pripremi",
        SheetStatus.Submitted => "Predano na odobravanje",
        SheetStatus.Returned => "Vraćeno na ispravku",
        SheetStatus.Approved => "Odobreno",
        _ => status.ToString()
    };

    public static string History(HistoryKind kind) => kind switch
    {
        HistoryKind.Submitted => "Predano na odobravanje",
        HistoryKind.StepApproved => "Odobreno, ide dalje",
        HistoryKind.Approved => "Odobreno",
        HistoryKind.Returned => "Vraćeno na ispravku",
        HistoryKind.Answered => "Odgovor zaposlenika",
        HistoryKind.Reassigned => "Korak proslijeđen drugoj osobi",
        _ => kind.ToString()
    };

    public static string DayKind(DayKind kind) => kind switch
    {
        Contracts.DayKind.Weekend => "vikend",
        Contracts.DayKind.Holiday => "praznik",
        _ => "radni dan"
    };

    public static string SkippedBecause(SkippedBecause reason) => reason switch
    {
        Contracts.SkippedBecause.Weekend => "vikend",
        Contracts.SkippedBecause.StateHoliday => "državni praznik",
        Contracts.SkippedBecause.ReligiousHoliday => "vjerski praznik",
        _ => reason.ToString()
    };
}
