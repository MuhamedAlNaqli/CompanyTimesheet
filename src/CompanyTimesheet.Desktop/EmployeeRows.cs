using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Company.Api.Contracts;
using Timesheet.Domain;

namespace CompanyTimesheet.Desktop;

/// <summary>One day of the month as a row of the list</summary>
public sealed class EmployeeDayRow
{
    public EmployeeDayRow(DayDto day, IReadOnlyList<IssueDto> issues)
    {
        Day = day;
        Date = day.Date;
        DateText = EmployeeFormat.ShortDate(day.Date);
        WeekdayText = EmployeeFormat.Weekday(day.Date);
        TypeText = day.HolidayName is { Length: > 0 } ? $"{EmployeeFormat.DayType(day.Type)}: {day.HolidayName}" : EmployeeFormat.DayType(day.Type);
        Stripe = day.Type switch
        {
            DayKind.Weekend => Brush.Parse("#C00000"),
            DayKind.Holiday => Brush.Parse("#EA580C"),
            _ => Brushes.Transparent
        };
        IsWorkingDay = day.Type == DayKind.Working;
        CalendarEntriesText = string.Join("\n", day.Entries.Select(e => TimeCodes.Get(e.Code).Unit == CodeUnit.Hours
            ? (e.Code == Timesheet.Domain.Codes.RegularWork ? EmployeeFormat.Hours(e.Minutes) : $"{e.Code} · {EmployeeFormat.Hours(e.Minutes)}")
            : $"{e.Code} {EmployeeFormat.CodeName(e.Code)}"));
        var overtimeMinutes = day.OvertimeAllocations.Sum(a => a.Minutes);
        CalendarOvertimeText = overtimeMinutes == 0 ? "" : Loc.T("Prek. ") + EmployeeFormat.Hours(overtimeMinutes);
        OvertimeText = string.Join("\n", day.OvertimeAllocations.Select(a => $"{a.Code} {EmployeeFormat.CodeName(a.Code)}: {EmployeeFormat.Hours(a.Minutes)}"));
        // The regular entries and the overtime are one list: every code of the day, one under the other
        EntriesText = string.Join("\n", day.Entries.Select(EmployeeFormat.Entry).Concat(OvertimeText.Length == 0 ? Array.Empty<string>() : OvertimeText.Split('\n')));
        HasEntries = day.Entries.Count > 0 || day.OvertimeAllocations.Count > 0;
        var mine = issues.Where(i => i.Date == day.Date).ToList();
        IssueCount = mine.Count;
        HasBlocking = mine.Any(i => i.Severity == IssueSeverity.Blocking);
        IssueTooltip = string.Join("\n", mine.Select(i => $"{i.RuleId}: {i.Message}"));
    }

    public DayDto Day { get; }
    public DateOnly Date { get; }

    // The cell of the calendar view
    public string DayNumber => Date.Day.ToString();
    // The colours of the cell come from the theme (see the "cal-cell" styles), so the calendar reads in the light and in the dark theme
    public bool IsWeekendCell => Day.Type == DayKind.Weekend;
    public bool IsHolidayCell => Day.Type == DayKind.Holiday;
    public bool IsTodayCell => Date == DateOnly.FromDateTime(DateTime.Today);
    public string HolidayText => Day.HolidayName ?? "";
    public string CalendarEntriesText { get; private set; } = "";
    public string CalendarOvertimeText { get; private set; } = "";
    public string DateText { get; }
    public string WeekdayText { get; }
    public string TypeText { get; }
    public IBrush Stripe { get; }
    public bool IsWorkingDay { get; }
    public string EntriesText { get; }
    public string OvertimeText { get; }
    public int IssueCount { get; }
    public bool HasIssues => IssueCount > 0;
    public bool HasBlocking { get; }
    public string IssueTooltip { get; }
    public bool HasEntries { get; }
}

/// <summary>One square of the calendar: a day of the month, or an empty square before the first</summary>
public sealed class CalendarCell
{
    public CalendarCell(EmployeeDayRow? row) => Row = row;

    public EmployeeDayRow? Row { get; }

    public bool HasDay => Row != null;
}

public sealed class EmployeeOvertimeRow
{
    public EmployeeOvertimeRow(OvertimeDto record)
    {
        Record = record;
        Title = $"{EmployeeFormat.ShortDate(record.Date)} {record.Start:HH\\:mm}–{record.End:HH\\:mm} ({EmployeeFormat.Hours(record.TotalMinutes)})";
        Detail = string.Join(", ", record.Allocations.Select(a => $"{a.Code}: {EmployeeFormat.Hours(a.Minutes)}"))
                 + (record.MinutesInNextMonth > 0 ? Loc.F(" ({0} se računa u sljedećem mjesecu)", EmployeeFormat.Hours(record.MinutesInNextMonth)) : "");
        Description = record.Description;
        HasConflict = record.HasRegularWindowConflict;
    }

    public OvertimeDto Record { get; }
    public string Title { get; }
    public string Detail { get; }
    public string Description { get; }
    public bool HasConflict { get; }
}

public sealed class EmployeeIssueRow
{
    public EmployeeIssueRow(IssueDto issue)
    {
        Issue = issue;
        Title = issue.RuleId + (issue.Severity == IssueSeverity.Blocking ? Loc.T(" · zaustavlja predaju") : " · upozorenje");
        Message = issue.Message;
        IsBlocking = issue.Severity == IssueSeverity.Blocking;
    }

    public IssueDto Issue { get; }
    public string Title { get; }
    public string Message { get; }
    public bool IsBlocking { get; }
}

/// <summary>A submitted timesheet waiting for the signed-in approver</summary>
public sealed class PendingRow
{
    public PendingRow(PendingApprovalDto item)
    {
        Item = item;
        Title = item.EmployeeName;
        Detail = Loc.F("{0} · predano {1:dd.MM.yyyy. HH:mm}", EmployeeFormat.Month(item.Year, item.Month), item.SubmittedUtc.ToLocalTime())
                 + (item.ActingForName is { Length: > 0 } acting ? Loc.F(" · kao zamjena za {0}", acting) : "");
        Warning = item.BlockingCount > 0
            ? Loc.F("{0} primjedbi koje zaustavljaju predaju", item.BlockingCount)
            : item.WarningCount > 0 ? Loc.F("{0} upozorenja", item.WarningCount) : "";
    }

    public PendingApprovalDto Item { get; }
    public string Title { get; }
    public string Detail { get; }
    public string Warning { get; }
    public bool HasWarning => Warning.Length > 0;
}

/// <summary>A period in which somebody stands in for an approver</summary>
public sealed class DeputyRow
{
    public DeputyRow(DeputyDto deputy, bool forOthers)
    {
        Deputy = deputy;
        Id = deputy.Id;
        Title = forOthers ? Loc.F("Zamjenjujete: {0}", deputy.ApproverName) : Loc.F("Zamjenjuje vas: {0}", deputy.DeputyName);
        Period = $"{EmployeeFormat.Date(deputy.From)} – {EmployeeFormat.Date(deputy.To)}";
        CanRemove = !forOthers;
    }

    public DeputyDto Deputy { get; }
    public int Id { get; }
    public string Title { get; }
    public string Period { get; }

    /// <summary>The approver removes their own deputy; a deputy cannot remove a period</summary>
    public bool CanRemove { get; }
}

/// <summary>A version of the timesheet as it was submitted</summary>
public sealed class RevisionRow
{
    public RevisionRow(RevisionDto revision)
    {
        Revision = revision;
        Number = revision.Revision;
        Title = Loc.F("Verzija {0}", revision.Revision);
        Detail = Loc.F("predano {0:dd.MM.yyyy. HH:mm}", revision.SubmittedUtc.ToLocalTime());
    }

    public RevisionDto Revision { get; }
    public int Number { get; }
    public string Title { get; }
    public string Detail { get; }
}

/// <summary>A message with which a timesheet was returned, and the answer to it</summary>
public sealed class MessageRow
{
    public MessageRow(MessageDto message, bool canAnswer)
    {
        Message = message;
        Header = Loc.F("{0} (predaja {1})", message.FromName, message.Revision);
        Text = message.Text;
        AnswerText = string.IsNullOrWhiteSpace(message.Answer) ? "" : Loc.T("Odgovor: ") + message.Answer;
        CanAnswer = canAnswer && string.IsNullOrWhiteSpace(message.Answer);
    }

    public MessageDto Message { get; }
    public string Header { get; }
    public string Text { get; }
    public string AnswerText { get; }
    public bool HasAnswer => AnswerText.Length > 0;
    public bool CanAnswer { get; }
}

public sealed class HistoryRow
{
    public HistoryRow(HistoryDto item)
    {
        var onBehalf = item.OnBehalfOf == null ? "" : Loc.F(" (u ime {0})", item.OnBehalfOf);
        Title = $"{item.AtUtc.ToLocalTime().ToString(Loc.IsEnglish ? "dd/MM/yyyy HH:mm" : "dd.MM.yyyy. HH:mm")} · {EmployeeFormat.HistoryKind(item.Kind)}";
        Detail = item.ActorName + onBehalf + (string.IsNullOrWhiteSpace(item.Text) ? "" : ": " + item.Text);
    }

    public string Title { get; }
    public string Detail { get; }
}

public sealed class ReminderRow
{
    public ReminderRow(ReminderDto item)
    {
        Item = item;
        (Title, Message) = EmployeeFormat.Reminder(item);
        IsLate = item.Kind is ReminderType.SubmitOverdue or ReminderType.ApproveOverdue or ReminderType.FixReturned;
    }

    public ReminderDto Item { get; }
    public string Title { get; }
    public string Message { get; }
    public bool IsLate { get; }
}
