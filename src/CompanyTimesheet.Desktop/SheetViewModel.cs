using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Company.Api.Contracts;
using Timesheet.Domain;

namespace CompanyTimesheet.Desktop;

/// <summary>
/// What a month looks like on screen, made from what the server sent: the days (as a list and as a calendar), the overtime, the
/// totals, the findings of the rules, the messages of the approvers and the history. It does nothing; it only shows.
/// </summary>
public sealed partial class SheetViewModel : ObservableObject
{
    public ObservableCollection<EmployeeDayRow> Days { get; } = new();

    public ObservableCollection<CalendarCell> CalendarCells { get; } = new();

    public ObservableCollection<EmployeeOvertimeRow> Overtime { get; } = new();

    /// <summary>Overtime of the previous month that goes on after midnight into this one</summary>
    public ObservableCollection<string> Carried { get; } = new();

    public ObservableCollection<EmployeeIssueRow> Issues { get; } = new();

    public ObservableCollection<string> TotalLines { get; } = new();

    public ObservableCollection<MessageRow> Messages { get; } = new();

    public ObservableCollection<HistoryRow> History { get; } = new();

    /// <summary>The versions of the timesheet that were submitted, the newest first</summary>
    public ObservableCollection<RevisionRow> Revisions { get; } = new();

    public bool HasRevisions => Revisions.Count > 0;

    public bool HasHistory => History.Count > 0;

    /// <summary>A month is on screen (the weekday headings of the calendar make no sense without one)</summary>
    public bool HasDays => Days.Count > 0;

    [ObservableProperty] private string _fundText = "";
    [ObservableProperty] private string _overtimeText = "";
    [ObservableProperty] private bool _fundOk;
    [ObservableProperty] private string _workflowText = "";
    [ObservableProperty] private string _chainText = "";

    public System.Collections.Generic.IReadOnlyList<string> WeekdayHeaders => new[] { Loc.T("Pon"), Loc.T("Uto"), Loc.T("Sri"), Loc.T("Čet"), Loc.T("Pet"), Loc.T("Sub"), Loc.T("Ned") };

    /// <summary>The language changed: the headings of the calendar are written again</summary>
    public void LanguageChanged() => OnPropertyChanged(nameof(WeekdayHeaders));

    public bool HasMessages => Messages.Count > 0;

    public bool HasWorkflow => WorkflowText.Length > 0;

    public bool HasIssues => Issues.Count > 0;

    public bool NoIssues => Issues.Count == 0;

    public bool NoOvertime => Overtime.Count == 0 && Carried.Count == 0;

    partial void OnWorkflowTextChanged(string value) => OnPropertyChanged(nameof(HasWorkflow));

    public void Clear()
    {
        Days.Clear();
        CalendarCells.Clear();
        Overtime.Clear();
        Carried.Clear();
        Issues.Clear();
        TotalLines.Clear();
        Messages.Clear();
        History.Clear();
        Revisions.Clear();
        FundText = "";
        OvertimeText = "";
        WorkflowText = "";
        ChainText = "";
        RaiseLists();
    }

    /// <param name="own">The person's own month (they may answer the messages); false for somebody else's, which is only looked at</param>
    public void Show(SheetDto sheet, bool own)
    {
        Days.Clear();
        foreach (var day in sheet.Days) Days.Add(new EmployeeDayRow(day, sheet.Issues));
        BuildCalendar();

        Overtime.Clear();
        foreach (var record in sheet.Overtime) Overtime.Add(new EmployeeOvertimeRow(record));

        Carried.Clear();
        foreach (var carried in sheet.CarriedIn ?? Array.Empty<CarriedOvertimeDto>())
            Carried.Add(Loc.F("{0} {1:HH\\:mm}–{2:HH\\:mm} iz prethodnog mjeseca: {3} se računa u ovom mjesecu", EmployeeFormat.ShortDate(carried.Date), carried.Start, carried.End, EmployeeFormat.Hours(carried.MinutesInThisMonth)));

        Issues.Clear();
        foreach (var issue in sheet.Issues.OrderByDescending(i => i.Severity == IssueSeverity.Blocking)) Issues.Add(new EmployeeIssueRow(issue));

        var summary = sheet.Summary;
        FundOk = summary.FundMinutes >= summary.ExpectedFundMinutes;
        FundText = Loc.F("Mjesečni fond: {0} od {1}", EmployeeFormat.Hours(summary.FundMinutes), EmployeeFormat.Hours(summary.ExpectedFundMinutes));
        OvertimeText = Loc.F("Prekovremeni rad: {0}", EmployeeFormat.Hours(summary.OvertimeMinutes));

        TotalLines.Clear();
        foreach (var (code, minutes) in summary.MinutesByCode.OrderBy(p => p.Key))
            TotalLines.Add($"{code} {EmployeeFormat.CodeName(code)}: {EmployeeFormat.Hours(minutes)}");
        foreach (var (code, days) in summary.DaysByCode.Where(p => TimeCodes.Get(p.Key).IsAbsence).OrderBy(p => p.Key))
            TotalLines.Add(Loc.F("{0} {1}: {2} {3}", code, EmployeeFormat.CodeName(code), days, Loc.T(days == 1 ? "dan" : "dana")));

        ShowWorkflow(sheet, own);
        RaiseLists();
    }

    /// <summary>Monday first: empty squares up to the weekday of the first, then the days</summary>
    private void BuildCalendar()
    {
        CalendarCells.Clear();
        if (Days.Count == 0) return;

        var offset = ((int)Days[0].Date.DayOfWeek + 6) % 7;
        for (var i = 0; i < offset; i++) CalendarCells.Add(new CalendarCell(null));
        foreach (var day in Days) CalendarCells.Add(new CalendarCell(day));
    }

    private void ShowWorkflow(SheetDto sheet, bool own)
    {
        var workflow = sheet.Workflow;
        Messages.Clear();
        History.Clear();

        if (workflow == null)
        {
            WorkflowText = "";
            ChainText = "";
            return;
        }

        WorkflowText = sheet.Status switch
        {
            SheetStatus.Submitted => Loc.F("Čeka odobravanje: {0} (korak {1} od {2}).", workflow.CurrentApproverName, workflow.Step + 1, workflow.Chain.Count),
            SheetStatus.Returned => own
                ? Loc.T("Vraćeno na ispravku. Odgovorite na poruke, ispravite unos i predajte ponovo.")
                : Loc.T("Vraćeno zaposleniku na ispravku."),
            SheetStatus.Approved => Loc.T("Odobreno. Šihtarica je zaključana."),
            _ => own ? Loc.T("Još nije predano na odobravanje.") : ""
        };
        ChainText = workflow.Chain.Count == 0 ? "" : Loc.T("Lanac odobravanja: ") + string.Join(" → ", workflow.Chain.Select(c => c.Name));

        var canAnswer = own && sheet.Status == SheetStatus.Returned;
        foreach (var message in workflow.Messages) Messages.Add(new MessageRow(message, canAnswer));
        foreach (var item in workflow.History.Reverse()) History.Add(new HistoryRow(item));
        Revisions.Clear();
        foreach (var revision in workflow.Revisions.OrderByDescending(r => r.Revision)) Revisions.Add(new RevisionRow(revision));
    }

    private void RaiseLists()
    {
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(HasRevisions));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HasDays));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(NoIssues));
        OnPropertyChanged(nameof(NoOvertime));
    }
}
