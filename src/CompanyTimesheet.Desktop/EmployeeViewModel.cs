using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Company.Api.Client;
using Company.Api.Contracts;
using CompanyTimesheet.Desktop.Services;
using Microsoft.Extensions.Logging;
using Timesheet.Domain;

namespace CompanyTimesheet.Desktop;

/// <summary>The employee's month: the list of days, the overtime, the totals and the findings of the rules</summary>
public partial class EmployeeViewModel : ObservableObject
{
    private readonly ICompanyApiClient _client;
    private readonly ICompanySession _session;
    private readonly ICompanyAdminClient _admin;
    private readonly IEmployeeSignInStore _tokens;
    private readonly IEmployeeDialogs _dialogs;
    private readonly ISheetFileSaver _files;
    private readonly IEmployeeShell _shell;
    private readonly IUpdateService _updates;
    private readonly IServerSettings _server;
    private readonly ILogger<EmployeeViewModel> _logger;
    private SheetDto? _sheet;

    [ObservableProperty] private int _year = DateTime.Today.Year;
    [ObservableProperty] private int _month = DateTime.Today.Month;
    [ObservableProperty] private string _monthTitle = "";
    [ObservableProperty] private string _whoText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _errorText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isEditable;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _infoText = "";

    /// <summary>The person has people reporting to them</summary>
    [ObservableProperty] private bool _isApprover;

    /// <summary>The person administers the company's employees (the administrator tab)</summary>
    [ObservableProperty] private bool _isAdmin;

    /// <summary>The server gives the timesheet as an Excel or PDF file (switched off for now)</summary>
    [ObservableProperty] private bool _canExport;

    /// <summary>The month as a calendar instead of a list; remembered for the next time</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListView), nameof(ShowListPane), nameof(ShowCalendarPane))]
    private bool _isCalendarView = EmployeePreferences.LoadCalendarView();

    public bool IsListView => !IsCalendarView;

    // With no timesheet chosen in the approvals there is only the hint in the card, not the empty headings of a list
    public bool ShowListPane => IsListView && !ShowApprovalsHint;
    public bool ShowCalendarPane => IsCalendarView && !ShowApprovalsHint;

    partial void OnIsCalendarViewChanged(bool value) => EmployeePreferences.SaveCalendarView(value);

    [RelayCommand]
    private void ShowListView() => IsCalendarView = false;

    [RelayCommand]
    private void ShowCalendarView() => IsCalendarView = true;

    // Submission and approval
    [ObservableProperty] private bool _canSubmit;
    [ObservableProperty] private bool _canAct;
    [ObservableProperty] private string _reviewTitle = "";

    /// <summary>The "for approval" part is shown instead of the employee's own month</summary>
    [ObservableProperty] private bool _inApprovals;

    /// <summary>The people below the person are shown as a tree, with who has submitted the month</summary>
    [ObservableProperty] private bool _inTeam;

    /// <summary>The administrator's list of employees is shown</summary>
    [ObservableProperty] private bool _inAdmin;

    /// <summary>The history of the own month (versions, who did what) is shown in place of the days</summary>
    [ObservableProperty] private bool _inHistory;

    /// <summary>Someone else's timesheet is open (read only)</summary>
    [ObservableProperty] private bool _isReviewing;

    private string? _reviewCode;

    /// <summary>The month on screen: days, calendar, overtime, totals, findings, messages and history</summary>
    public SheetViewModel Sheet { get; } = new();

    /// <summary>The timesheets waiting for this person as an approver</summary>
    public PendingListViewModel PendingApprovals { get; } = new();

    /// <summary>The people below this person, as a tree</summary>
    public TeamViewModel Team { get; } = new();

    /// <summary>Who stands in for this person, and whom they stand in for</summary>
    public DeputiesViewModel Deputies { get; } = new();

    /// <summary>An older version of the timesheet (as it was submitted) is on screen, read only</summary>
    [ObservableProperty] private bool _isViewingRevision;

    [ObservableProperty] private string _revisionBanner = "";

    /// <summary>What is due for this person (submit, fix, approve)</summary>
    public RemindersViewModel Reminders { get; } = new();

    public bool HasInfo => InfoText.Length > 0;

    public bool ShowMineToolbar => IsConnected && InMine && !InHistory;

    public bool InMine => !InApprovals && !InTeam && !InAdmin && !InHome;

    /// <summary>The "My timesheet" tab itself (not its history)</summary>
    public bool InSheet => InMine && !InHistory;

    /// <summary>The month, its totals and findings are on screen (not the team or the administrator's list)</summary>
    public bool ShowSheetBody => !InTeam && !InAdmin && !InHome;

    /// <summary>The days, with the side column of totals and findings</summary>
    public bool ShowDaysBody => ShowSheetBody && !ShowHistoryBody;

    /// <summary>The history of the own month fills the window</summary>
    public bool ShowHistoryBody => InMine && InHistory && !IsReviewing;

    public bool ShowAdminToolbar => IsConnected && InAdmin;

    /// <summary>The deputy settings are in the list of approvals, not while a timesheet is open</summary>
    public bool ShowDeputies => InApprovals && !IsReviewing;

    /// <summary>The month chooses what is shown in the team and the lists for approval, not in the administrator's list</summary>

    public bool ShowTeamToolbar => IsConnected && InTeam;

    public bool ShowMonthNav => IsConnected && !IsReviewing && !InAdmin && !InHome;

    /// <summary>A person with nobody above them decides on their own timesheet from their own screen</summary>
    public bool CanApproveOwn => CanAct && !IsReviewing && InMine;

    public bool ShowApprovalsToolbar => IsConnected && InApprovals && !IsReviewing;

    public bool ShowReviewToolbar => IsConnected && IsReviewing;

    public bool ShowApprovalsHint => IsConnected && InApprovals && !IsReviewing;

    partial void OnInfoTextChanged(string value) => OnPropertyChanged(nameof(HasInfo));

    partial void OnInApprovalsChanged(bool value) { if (value) InHome = false; RaiseModeChanged(); }

    partial void OnInTeamChanged(bool value) { if (value) InHome = false; RaiseModeChanged(); }

    partial void OnInAdminChanged(bool value) { if (value) InHome = false; RaiseModeChanged(); }

    partial void OnInHistoryChanged(bool value) { if (value) InHome = false; RaiseModeChanged(); }

    partial void OnIsReviewingChanged(bool value) => RaiseModeChanged();

    partial void OnIsConnectedChanged(bool value) => RaiseModeChanged();

    partial void OnCanActChanged(bool value) => OnPropertyChanged(nameof(CanApproveOwn));

    private void RaiseModeChanged()
    {
        OnPropertyChanged(nameof(ShowMonthNav));
        OnPropertyChanged(nameof(CanApproveOwn));
        OnPropertyChanged(nameof(ShowApprovalsToolbar));
        OnPropertyChanged(nameof(ShowMineToolbar));
        OnPropertyChanged(nameof(InMine));
        OnPropertyChanged(nameof(InSheet));
        OnPropertyChanged(nameof(ShowSheetBody));
        OnPropertyChanged(nameof(ShowDaysBody));
        OnPropertyChanged(nameof(ShowHistoryBody));
        OnPropertyChanged(nameof(ShowAdminToolbar));
        OnPropertyChanged(nameof(ShowDeputies));
        OnPropertyChanged(nameof(ShowTeamToolbar));
        OnPropertyChanged(nameof(ShowReviewToolbar));
        OnPropertyChanged(nameof(ShowApprovalsHint));
        OnPropertyChanged(nameof(ShowListPane));
        OnPropertyChanged(nameof(ShowCalendarPane));
        OnPropertyChanged(nameof(ShowHomeBody));
        OnPropertyChanged(nameof(ShowSignInForm));
    }

    public bool HasError => ErrorText.Length > 0;

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    public EmployeeViewModel(ICompanyApiClient client, ICompanySession session, ICompanyAdminClient admin, IEmployeeSignInStore tokens, IEmployeeDialogs dialogs, ISheetFileSaver files, IEmployeeShell shell, IUpdateService updates, IServerSettings server, ILogger<EmployeeViewModel> logger)
    {
        _updates = updates;
        _server = server;
        ServerAddress = server.Address;
        _client = client;
        _session = session;
        _admin = admin;
        _tokens = tokens;
        _dialogs = dialogs;
        _files = files;
        _shell = shell;
        _logger = logger;
        MonthTitle = EmployeeFormat.Month(Year, Month);
        IsRestoring = tokens.Load() != null;
        InitAppearance();
    }

    // ---- Loading ---------------------------------------------------------------------------------------------------

    /// <summary>A sign-in saved by "remember me" is being tried: the sign-in window waits instead of showing its form for a moment</summary>
    [ObservableProperty] private bool _isRestoring;

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            await LoadCoreAsync();
        }
        finally
        {
            IsRestoring = false;
        }
    }

    /// <summary>Loads the month on screen; false when the server refused, was out of reach, or another call was still running</summary>
    private Task<bool> LoadCoreAsync()
    {
        IsViewingRevision = false;
        return LoadMonthAsync();
    }

    private Task<bool> LoadMonthAsync() =>
        RunAsync(async () =>
        {
            if (!IsConnected)
            {
                var me = await _client.GetMeAsync();
                WhoText = $"{me.Name} · {me.Code}";
                UserName = me.Name;
                UserCode = me.Code;
                IsApprover = me.IsApprover;
                CanExport = me.SheetExport;
                IsAdmin = me.IsAdmin;
                ApplyServerAppearance(me.ThemeMode, me.Palette, me.Language);
                await LoadTimeCodesAsync();

                // An employee starts on the start screen; the administrator has the administration
                if (!me.IsAdmin)
                {
                    InHome = true;
                    await LoadHomeAsync();
                }
            }

            // An administrator only administers: no timesheet is loaded for them, the administration is all there is
            if (IsAdmin)
            {
                InAdmin = true;
                await Admin.RefreshAsync(_admin);
                return null;
            }

            return await LoadCurrentAsync();
        }, connecting: true, refreshPending: true);

    private async Task<SheetDto?> LoadCurrentAsync() => _reviewCode == null
        ? await _client.GetSheetAsync(Year, Month)
        : await _client.GetReviewAsync(_reviewCode, Year, Month);

    /// <summary>Runs one call, shows the new state of the month, and turns a refusal into a message instead of an exception</summary>
    private async Task<bool> RunAsync(Func<Task<SheetDto?>> call, bool connecting = false, bool refreshPending = false)
    {
        if (IsBusy) return false;
        try
        {
            IsBusy = true;
            ErrorText = "";
            InfoText = "";
            if (await call() is { } sheet) Show(sheet);
            IsConnected = true;
            if (refreshPending && !IsAdmin) await RefreshPendingAsync();
            return true;
        }
        catch (ApiException ex) when (NeedsSignInAgain(ex))
        {
            ShowSignIn(IsConnected ? Loc.T("Sesija je istekla. Prijavite se ponovo.") : "");
            return false;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "The company server refused or could not be reached ({Code})", ex.Code);
            ErrorText = ex.Code switch
            {
                "not-registered" => Loc.T("Vaš račun nije povezan sa zaposlenikom. Obratite se administratoru."),
                "unauthorized" => Loc.T("Server nije prihvatio vašu Windows prijavu. Provjerite da ste prijavljeni firmskim računom i da ste u firmskoj mreži."),
                "unreachable" or "timeout" => Loc.T("Server firme nije dostupan. Provjerite mrežnu vezu (VPN)."),
                _ => ApiMessages.Describe(ex)
            };
            if (connecting) IsConnected = false;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Employee mode failed");
            ErrorText = Loc.T("Došlo je do greške: ") + ex.Message;
            if (connecting) IsConnected = false;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The reminders and the list of timesheets for approval. A failure here must not spoil the month that was just shown.</summary>
    private async Task RefreshPendingAsync()
    {
        try
        {
            await Reminders.RefreshAsync(_client);
            await PendingApprovals.RefreshAsync(_client);
        }
        catch (Exception ex)
        {
            // An answer that is not ours (a login page of a proxy, say) or a failure of the network: the month stays as it is
            _logger.LogWarning(ex, "The reminders and the list of timesheets for approval could not be read");
        }
    }

    private void ClearSheet()
    {
        _sheet = null;
        Sheet.Clear();
        StatusText = "";
        ReviewTitle = "";
        IsEditable = false;
        CanSubmit = false;
        CanAct = false;
    }

    private void Show(SheetDto sheet)
    {
        _sheet = sheet;
        Year = sheet.Year;
        Month = sheet.Month;
        MonthTitle = EmployeeFormat.Month(sheet.Year, sheet.Month);
        StatusText = EmployeeFormat.Status(sheet.Status);
        IsEditable = sheet.Editable;

        Sheet.Show(sheet, own: _reviewCode == null);

        var workflow = sheet.Workflow;
        CanSubmit = workflow?.CanSubmit == true;
        CanAct = workflow?.CanAct == true;
        ReviewTitle = _reviewCode != null && workflow != null ? $"{workflow.OwnerName} · {MonthTitle}" : "";
    }

    // ---- Month navigation ------------------------------------------------------------------------------------------

    [RelayCommand]
    private Task PreviousMonthAsync() => ChangeMonthAsync(-1);

    [RelayCommand]
    private Task NextMonthAsync() => ChangeMonthAsync(1);

    [RelayCommand]
    private Task ThisMonthAsync()
    {
        Year = DateTime.Today.Year;
        Month = DateTime.Today.Month;
        MonthTitle = EmployeeFormat.Month(Year, Month);
        return InTeam ? RefreshTeamAsync() : LoadAsync();
    }

    private async Task ChangeMonthAsync(int delta)
    {
        if (IsReviewing || IsBusy) return;
        var (previousYear, previousMonth) = (Year, Month);
        var next = new DateTime(Year, Month, 1).AddMonths(delta);
        Year = next.Year;
        Month = next.Month;

        // In the team the month chooses which month of the people below is shown
        if (InTeam)
        {
            MonthTitle = EmployeeFormat.Month(Year, Month);
            await RefreshTeamAsync();
            return;
        }

        // In the list of timesheets for approval the month only chooses the month of the team sheet
        if (InApprovals)
        {
            MonthTitle = EmployeeFormat.Month(Year, Month);
            return;
        }

        // If the month could not be loaded, the screen still shows the old one, so that is the one that stays selected
        if (!await LoadCoreAsync())
        {
            Year = previousYear;
            Month = previousMonth;
            MonthTitle = EmployeeFormat.Month(Year, Month);
        }
    }

    // ---- Editing ---------------------------------------------------------------------------------------------------

    /// <summary>A new entry: the form changes with the code (hours, absence over several days, or overtime)</summary>
    [RelayCommand]
    private Task AddEntryAsync() => OpenEntryAsync(null, null, null);

    /// <summary>A day of the list or of the calendar: its entry, or a new entry for that day when it is empty</summary>
    [RelayCommand]
    private Task EditDayAsync(EmployeeDayRow? row)
    {
        if (row == null || !IsEditable) return Task.CompletedTask;

        var entry = row.Day.Entries.FirstOrDefault(e => TimeCodes.Get(e.Code).IsBaseEntry) ?? row.Day.Entries.FirstOrDefault();
        return OpenEntryAsync(row.Date, entry, null);
    }

    private async Task OpenEntryAsync(DateOnly? date, EntryDto? entry, OvertimeDto? overtime)
    {
        if (!IsEditable || _sheet == null) return;

        var viewModel = new EntryViewModel(Year, Month, DayTypeResolver(), request => _client.PreviewAbsenceAsync(Year, Month, request), date, entry, overtime);
        var result = await _dialogs.EntryAsync(viewModel);
        if (result == null) return;

        if (result.Overtime != null)
            await RunAsync(() => result.OvertimeId is { } id
                ? _client.UpdateOvertimeAsync(Year, Month, id, result.Overtime)
                : _client.AddOvertimeAsync(Year, Month, result.Overtime));
        else if (result.Absence != null)
            await RunAsync(() => _client.ApplyAbsenceAsync(Year, Month, result.Absence));
        else
            await RunAsync(() => _client.SetEntryAsync(Year, Month, result.DayDate!.Value, result.Day!));
    }

    [RelayCommand]
    private async Task ClearDayAsync(EmployeeDayRow? row)
    {
        if (row == null || !IsEditable || !row.HasEntries) return;

        await RunAsync(async () =>
        {
            SheetDto? last = null;
            foreach (var entry in row.Day.Entries) last = await _client.ClearEntryAsync(Year, Month, row.Date, entry.Code);
            return last!;
        });
    }

    [RelayCommand]
    private Task EditOvertimeAsync(EmployeeOvertimeRow? row) => row == null ? Task.CompletedTask : OpenEntryAsync(null, null, row.Record);

    [RelayCommand]
    private async Task DeleteOvertimeAsync(EmployeeOvertimeRow? row)
    {
        if (row == null || !IsEditable) return;

        var confirmed = await _dialogs.ConfirmAsync(Loc.T("Obriši prekovremeni rad"), Loc.F("Obrisati unos {0}?", row.Title));
        if (confirmed) await RunAsync(() => _client.DeleteOvertimeAsync(Year, Month, row.Record.Id));
    }

    /// <summary>The type of a date for the live preview of overtime: from the month's calendar, else by weekday</summary>
    private Func<DateOnly, DayType> DayTypeResolver()
    {
        var types = _sheet!.Days.ToDictionary(d => d.Date, d => d.Type switch { DayKind.Weekend => DayType.Weekend, DayKind.Holiday => DayType.Holiday, _ => DayType.Working });
        return date => types.TryGetValue(date, out var type) ? type : (WorkCalendar.IsWeekend(date) ? DayType.Weekend : DayType.Working);
    }

    // ---- Printed sheet ---------------------------------------------------------------------------------------------

    /// <summary>Prints the month on screen (the own sheet, or the one opened for approval) in the layout of the current Excel sheet</summary>
    [RelayCommand]
    private Task PrintAsync(string? format) => SaveFileAsync(format, f => _reviewCode == null
        ? _client.GetPrintAsync(Year, Month, f)
        : _client.GetReviewPrintAsync(_reviewCode, Year, Month, f));

    /// <summary>The message for the approver: what there is to report for everybody below, and the overtime register</summary>
    [RelayCommand]
    private Task PrintSummaryAsync(string? format) => SaveFileAsync(format, f => _client.GetTeamSummaryAsync(Year, Month, f));

    /// <summary>The month of everybody who reports to the signed-in person, on one sheet, whatever the status of each</summary>
    [RelayCommand]
    private Task PrintTeamAsync(string? format) => SaveFileAsync(format, f => _client.GetTeamPrintAsync(Year, Month, f));

    private async Task SaveFileAsync(string? format, Func<string, Task<DownloadedFile>> download)
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            ErrorText = "";
            InfoText = "";
            var result = await _files.SaveAsync(format, download);
            if (result.Error != null) ErrorText = result.Error;
            else if (result.Path != null) InfoText = Loc.F("Sačuvano: {0}", result.Path);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Submission ------------------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (!CanSubmit) return;

        // One process: what the approver wrote is answered in the same step in which the timesheet is submitted again
        var answers = new List<(int Index, string Text)>();
        foreach (var message in Sheet.Messages.Where(m => m.CanAnswer))
        {
            var prompt = new TextPromptViewModel(Loc.T("Odgovor na poruku"), Loc.T("Vaš odgovor"), Loc.T("Odgovori i predaj"), $"{message.Header}: {message.Text}");
            var answer = await _dialogs.PromptAsync(prompt);
            if (answer == null) return;
            answers.Add((message.Message.Index, answer));
        }

        if (answers.Count == 0)
        {
            var confirmed = await _dialogs.ConfirmAsync(Loc.T("Predaj na odobravanje"),
                Loc.F("Nakon predaje je šihtarica za {0} zaključana dok je neko od odobravatelja ne vrati. Predati?", MonthTitle));
            if (!confirmed) return;
        }

        await RunAsync(async () =>
        {
            foreach (var (index, text) in answers) await _client.AnswerAsync(Year, Month, index, text);
            return await _client.SubmitAsync(Year, Month);
        }, refreshPending: true);
    }

    [RelayCommand]
    private async Task AnswerAsync(MessageRow? row)
    {
        if (row == null || !row.CanAnswer) return;

        var viewModel = new TextPromptViewModel(Loc.T("Odgovor na poruku"), Loc.T("Vaš odgovor"), Loc.T("Pošalji odgovor"), $"{row.Header}: {row.Text}");
        var answer = await _dialogs.PromptAsync(viewModel);
        if (answer != null) await RunAsync(() => _client.AnswerAsync(Year, Month, row.Message.Index, answer), refreshPending: true);
    }

    // ---- Approval --------------------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task ShowMineAsync()
    {
        InHome = false;
        InApprovals = false;
        InTeam = false;
        InAdmin = false;
        InHistory = false;
        IsReviewing = false;
        _reviewCode = null;
        await LoadAsync();
    }

    /// <summary>The versions that were submitted and who did what with the month on screen; the month buttons choose the month</summary>
    [RelayCommand]
    private async Task ShowHistoryAsync()
    {
        InApprovals = false;
        InTeam = false;
        InAdmin = false;
        InHistory = true;
        IsReviewing = false;
        _reviewCode = null;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ShowApprovalsAsync()
    {
        InTeam = false;
        InAdmin = false;
        InHistory = false;
        InApprovals = true;
        IsReviewing = false;
        IsViewingRevision = false;
        _reviewCode = null;
        ClearSheet();
        InfoText = "";
        await RefreshPendingAsync();
        await RunAdminAsync(() => Deputies.RefreshAsync(_client));
    }

    /// <summary>Chooses the deputy described by the form</summary>
    [RelayCommand]
    private async Task AddDeputyAsync()
    {
        var (request, problem) = Deputies.ToRequest();
        if (request == null)
        {
            ErrorText = problem ?? "";
            return;
        }

        await RunAdminAsync(async () =>
        {
            await _client.AddDeputyAsync(request);
            InfoText = Loc.T("Zamjena je postavljena.");
            await Deputies.RefreshAsync(_client);
        });
    }

    [RelayCommand]
    private async Task RemoveDeputyAsync(DeputyRow? row)
    {
        if (row == null || !row.CanRemove) return;

        await RunAdminAsync(async () =>
        {
            await _client.RemoveDeputyAsync(row.Id);
            InfoText = Loc.T("Zamjena je uklonjena.");
            await Deputies.RefreshAsync(_client);
        });
    }

    // ---- Older versions --------------------------------------------------------------------------------------------------

    /// <summary>A version of the month as it was submitted, read only (the own sheet, or the one opened for approval)</summary>
    [RelayCommand]
    private async Task OpenRevisionAsync(RevisionRow? row)
    {
        if (row == null) return;

        var opened = await RunAsync(() => _reviewCode == null
            ? _client.GetRevisionAsync(Year, Month, row.Number)
            : _client.GetReviewRevisionAsync(_reviewCode, Year, Month, row.Number));
        if (!opened) return;

        // Whatever the current state allows, an older version is only looked at
        IsEditable = false;
        CanSubmit = false;
        CanAct = false;
        IsViewingRevision = true;
        RevisionBanner = Loc.F("Prikazana je {0}, {1}. Ovo nije trenutno stanje šihtarice.", row.Title.ToLowerInvariant(), row.Detail);
    }

    [RelayCommand]
    private Task CloseRevisionAsync() => LoadCoreAsync();

    [RelayCommand]
    private async Task OpenPendingAsync(PendingRow? row)
    {
        if (row == null) return;

        _reviewCode = row.Item.EmployeeCode;
        Year = row.Item.Year;
        Month = row.Item.Month;
        MonthTitle = EmployeeFormat.Month(Year, Month);
        IsReviewing = true;
        if (!await LoadCoreAsync()) CloseReview();
    }

    [RelayCommand]
    private void CloseReview()
    {
        _reviewCode = null;
        IsReviewing = false;
        ClearSheet();
    }

    [RelayCommand]
    private async Task ApproveAsync()
    {
        var code = _reviewCode ?? _sheet?.Workflow?.OwnerCode;
        if (!CanAct || code == null) return;
        var what = _reviewCode == null ? Loc.F("svoju šihtaricu za {0}", MonthTitle) : Loc.F("šihtaricu: {0}", ReviewTitle);
        var confirmed = await _dialogs.ConfirmAsync(Loc.T("Odobri šihtaricu"), Loc.F("Odobriti {0}?", what));
        if (!confirmed) return;

        if (await RunAsync(() => _client.ApproveAsync(code, Year, Month), refreshPending: true)) InfoText = "Odobreno.";
    }

    [RelayCommand]
    private async Task ReturnAsync()
    {
        if (!CanAct || _reviewCode == null) return;
        var code = _reviewCode;

        var viewModel = new TextPromptViewModel(Loc.T("Vrati na ispravku"), Loc.T("Šta treba ispraviti? (obavezno)"), Loc.T("Vrati zaposleniku"));
        var message = await _dialogs.PromptAsync(viewModel);
        if (message == null) return;

        if (await RunAsync(() => _client.ReturnAsync(code, Year, Month, message), refreshPending: true)) InfoText = Loc.T("Šihtarica je vraćena zaposleniku.");
    }

    // ---- The team --------------------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task ShowTeamAsync()
    {
        InApprovals = false;
        InAdmin = false;
        InHistory = false;
        InTeam = true;
        Team.ShowSummary = false;
        IsReviewing = false;
        _reviewCode = null;
        ClearSheet();
        InfoText = "";
        await RefreshTeamAsync();
    }

    private Task RefreshTeamAsync() => RunTeamAsync(async () =>
    {
        await Team.RefreshAsync(_client, Year, Month);
        if (Team.ShowSummary) await Team.LoadSummaryAsync(_client, Year, Month);
    });

    /// <summary>The message for the approver: what there is to report for everybody below, as text to read and copy</summary>
    [RelayCommand]
    private async Task ShowSummaryAsync()
    {
        await RunTeamAsync(async () =>
        {
            await Team.LoadSummaryAsync(_client, Year, Month);
            Team.ShowSummary = true;
        });
    }

    [RelayCommand]
    private void ShowTree() => Team.ShowSummary = false;

    [RelayCommand]
    private async Task CopySummaryAsync()
    {
        if (Team.SummaryBody.Length == 0) return;
        await _shell.CopyAsync(Team.SummaryBody);
        InfoText = Loc.T("Kopirano. Zalijepite u mail.");
    }

    /// <summary>One person of the team: asks them, by name, to submit the month. Their computer shows a notification.</summary>
    [RelayCommand]
    private async Task RemindAsync(TeamRow? row)
    {
        if (row == null || !row.CanRemind) return;

        await RunTeamAsync(async () =>
        {
            try
            {
                await _client.RemindAsync(row.Code, Year, Month);
                row.MarkReminded(DateTime.UtcNow);
                InfoText = Loc.F("Podsjetnik je poslan: {0}.", row.Name);
            }
            catch (ApiException ex) when (ex.Code is "already-reminded" or "nothing-to-remind" or "not-in-team")
            {
                ErrorText = TeamViewModel.ReminderRefusal(ex, row.Name);
            }
        });
    }

    /// <summary>Everybody who has not submitted, except those who were reminded a moment ago</summary>
    [RelayCommand]
    private async Task RemindAllAsync()
    {
        await RunTeamAsync(async () =>
        {
            var sent = 0;
            foreach (var row in Team.Rows.Where(r => r.CanRemind).ToList())
            {
                try
                {
                    await _client.RemindAsync(row.Code, Year, Month);
                    row.MarkReminded(DateTime.UtcNow);
                    sent++;
                }
                catch (ApiException ex) when (ex.Code is "already-reminded" or "nothing-to-remind")
                {
                    // Reminded a moment ago, or nothing left to ask for: leave this one out
                }
            }

            InfoText = sent == 0 ? Loc.T("Svi koji nisu predali su već podsjećeni.") : Loc.F("Podsjetnik je poslan: {0}.", sent);
        });
    }

    /// <summary>Runs one action of the team tab with the same error handling as the month</summary>
    private async Task RunTeamAsync(Func<Task> action)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorText = "";
            InfoText = "";
            await action();
        }
        catch (ApiException ex) when (NeedsSignInAgain(ex))
        {
            ShowSignIn(Loc.T("Sesija je istekla. Prijavite se ponovo."));
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "The team could not be read or reminded ({Code})", ex.Code);
            ErrorText = ApiMessages.Describe(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The team tab failed");
            ErrorText = Loc.T("Došlo je do greške: ") + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- The administrator ---------------------------------------------------------------------------------------------

    /// <summary>The company's employees, their user names, and the import of the list</summary>
    public AdminViewModel Admin { get; } = new();

    [RelayCommand]
    private async Task ShowAdminAsync()
    {
        InApprovals = false;
        InTeam = false;
        InHistory = false;
        InAdmin = true;
        IsReviewing = false;
        _reviewCode = null;
        ClearSheet();
        InfoText = "";
        await RunAdminAsync(() => Admin.RefreshAsync(_admin));
    }

    /// <summary>A new person: the dialog asks for the data and, if wanted, the user name and first password; then the person is added</summary>
    [RelayCommand]
    private async Task AddEmployeeAsync()
    {
        var result = await _dialogs.AdminEditAsync(new AdminEmployeeViewModel(null, Admin.Employees));
        if (result?.Create == null) return;

        await RunAdminAsync(async () =>
        {
            var created = await _admin.CreateEmployeeAsync(result.Create);
            if (result.SetCredentials != null)
            {
                try
                {
                    await _admin.SetCredentialsAsync(created.Code, result.SetCredentials);
                }
                catch (ApiException ex)
                {
                    // The person exists now: show them, and say what is still missing
                    await Admin.RefreshAsync(_admin);
                    ErrorText = Loc.F("Osoba je dodana ({0}), ali korisničko ime nije postavljeno: {1} Otvorite osobu i pokušajte ponovo.", created.Code, AdminViewModel.Refusal(ex));
                    return;
                }
            }

            InfoText = result.SetCredentials == null
                ? Loc.F("Dodano: {0} ({1}). Korisničko ime još nije postavljeno.", created.Name, created.Code)
                : Loc.F("Dodano: {0} ({1}).", created.Name, created.Code);
            await Admin.RefreshAsync(_admin);
        });
    }

    [RelayCommand]
    private async Task EditEmployeeAsync(AdminRow? row)
    {
        if (row == null) return;

        var result = await _dialogs.AdminEditAsync(new AdminEmployeeViewModel(row, Admin.Employees));
        if (result == null) return;

        await RunAdminAsync(async () =>
        {
            if (result.RemoveCredentials)
            {
                await _admin.RemoveCredentialsAsync(row.Code);
                InfoText = Loc.F("Korisničko ime je uklonjeno: {0}.", row.Name);
            }
            else
            {
                await _admin.UpdateEmployeeAsync(row.Code, result.Update);
                if (result.SetCredentials != null) await _admin.SetCredentialsAsync(row.Code, result.SetCredentials);
                InfoText = Loc.F("Sačuvano: {0}.", row.Name);
            }

            await Admin.RefreshAsync(_admin);
        });
    }

    /// <summary>The list of employees from a CSV file: the preview first, then the import</summary>
    [RelayCommand]
    private async Task ImportEmployeesAsync()
    {
        var csv = await _dialogs.PickCsvAsync();
        if (csv == null) return;

        await RunAdminAsync(async () =>
        {
            var preview = await _admin.PreviewImportAsync(csv);
            var good = preview.Rows.Count(r => r.Errors.Count == 0);
            if (good == 0)
            {
                ErrorText = preview.Rows.Count == 0 ? Loc.T("U datoteci nema redova.") : Loc.F("Nijedan red nije ispravan ({0} grešaka). Provjerite datoteku.", preview.ErrorCount);
                return;
            }

            var question = Loc.F("Uvesti {0} od {1} redova? Redovi s greškom ({2}) se preskaču, upozorenja: {3}.", good, preview.Rows.Count, preview.Rows.Count - good, preview.WarningCount);
            if (!await _dialogs.ConfirmAsync(Loc.T("Uvoz liste radnika"), question)) return;

            var result = await _admin.ImportAsync(csv);
            InfoText = Loc.F("Dodano: {0}, ažurirano: {1}, preskočeno: {2}.", result.Created, result.Updated, result.Skipped);
            await Admin.RefreshAsync(_admin);
        });
    }

    [RelayCommand]
    private Task VerifyAuditAsync() => RunAdminAsync(async () =>
    {
        var result = await _admin.VerifyAuditAsync();
        if (result.IsIntact) InfoText = Loc.F("Revizijski dnevnik je u redu ({0} zapisa).", result.Checked);
        else ErrorText = Loc.F("Revizijski dnevnik je mijenjan mimo aplikacije. Prvi sporni zapis: {0}.", result.FirstBrokenId);
    });

    private async Task RunAdminAsync(Func<Task> action)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorText = "";
            InfoText = "";
            await action();
        }
        catch (ApiException ex) when (NeedsSignInAgain(ex))
        {
            ShowSignIn(Loc.T("Sesija je istekla. Prijavite se ponovo."));
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "The administrator's action was refused ({Code})", ex.Code);
            ErrorText = AdminViewModel.Refusal(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The administrator's tab failed");
            ErrorText = Loc.T("Došlo je do greške: ") + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Updates -----------------------------------------------------------------------------------------------------

    /// <summary>Asks whether a newer version exists; when it does, the update window of the library opens</summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorText = "";
            InfoText = "";
            var outcome = await _updates.CheckAndPromptAsync();
            switch (outcome.Status)
            {
                case UpdateCheckStatus.Failed:
                    ErrorText = Loc.T("Provjera ažuriranja nije uspjela: ") + outcome.Message;
                    break;
                case UpdateCheckStatus.UpToDate:
                    InfoText = outcome.Message;
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The check for updates failed");
            ErrorText = Loc.T("Provjera ažuriranja nije uspjela: ") + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Sign-in -----------------------------------------------------------------------------------------------------

    /// <summary>The server wants a user name and password (none yet, or the session ended)</summary>
    [ObservableProperty] private bool _needsSignIn;

    /// <summary>The administrator set the password: the person chooses their own before anything else</summary>
    [ObservableProperty] private bool _mustChangePassword;

    [ObservableProperty] private string _loginUsername = "";
    [ObservableProperty] private string _loginPassword = "";
    [ObservableProperty] private bool _loginRemember;
    [ObservableProperty] private string _loginError = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _newPasswordAgain = "";

    public bool ShowLoginForm => NeedsSignIn && !MustChangePassword;

    /// <summary>
    /// The form of the sign-in window: shown whenever nobody is signed in, the first password is not being chosen, and no saved sign-in
    /// is being tried. (Not tied to the server having said "unauthorized": with the server out of reach the form must still be there, to
    /// change the server's address.)
    /// </summary>
    public bool ShowSignInForm => !IsConnected && !MustChangePassword && !IsRestoring;

    partial void OnNeedsSignInChanged(bool value) => OnPropertyChanged(nameof(ShowLoginForm));

    partial void OnMustChangePasswordChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLoginForm));
        OnPropertyChanged(nameof(ShowSignInForm));
    }

    partial void OnIsRestoringChanged(bool value) => OnPropertyChanged(nameof(ShowSignInForm));

    private string? _pendingToken;

    /// <summary>The address of the company server, shown on the sign-in screen so a test can point the app at any server</summary>
    [ObservableProperty] private string _serverAddress = "";

    [ObservableProperty] private bool _showServerField;

    [RelayCommand]
    private void ToggleServerField() => ShowServerField = !ShowServerField;

    /// <summary>Uses the address typed on the sign-in screen; false (with the reason in LoginError) when it cannot be used</summary>
    private bool ApplyServerAddress()
    {
        var typed = ServerAddress.Trim();
        if (typed == _server.Address) return true;

        try
        {
            _session.UseServer(typed);
            _server.Save(typed);
            return true;
        }
        catch (ArgumentException)
        {
            LoginError = Loc.T("Adresa servera nije ispravna. Primjer: https://server.firma.ba ili http://localhost:5090");
        }
        catch (InvalidOperationException)
        {
            LoginError = Loc.T("Adresa mora počinjati sa https:// (http je dozvoljen samo za ovaj računar).");
        }

        return false;
    }

    private bool NeedsSignInAgain(ApiException ex) => _session.PasswordSignIn && ex.Code == "unauthorized";

    private void ShowSignIn(string message)
    {
        // Nothing of the person who was here stays on the screen: not their name, their reminders or their lists
        _reviewCode = null;
        ClearSheet();
        Reminders.Clear();
        PendingApprovals.Clear();
        Team.Clear();
        Admin.Clear();
        Deputies.Clear();
        WhoText = "";
        UserName = "";
        UserCode = "";
        IsApprover = false;
        IsAdmin = false;
        CanExport = false;
        InApprovals = false;
        InTeam = false;
        InAdmin = false;
        InHistory = false;
        InHome = false;
        HomeCards.Clear();
        IsReviewing = false;
        IsViewingRevision = false;
        IsConnected = false;
        NeedsSignIn = true;
        MustChangePassword = false;
        LoginError = message;
        ErrorText = "";
        InfoText = "";
    }

    /// <summary>Text under the sign-in form that is not an error (the request for a new password was sent)</summary>
    [ObservableProperty] private string _loginInfo = "";

    /// <summary>The person who forgot the password asks the administrator for a new one; the answer does not say whether the user name exists</summary>
    [RelayCommand]
    private async Task ForgotPasswordAsync()
    {
        if (IsBusy) return;
        LoginError = "";
        LoginInfo = "";
        if (LoginUsername.Trim().Length == 0) { LoginError = Loc.T("Upišite korisničko ime, pa zatražite novu lozinku."); return; }
        if (!ApplyServerAddress()) return;

        try
        {
            IsBusy = true;
            await _session.RequestPasswordResetAsync(LoginUsername.Trim());
            LoginInfo = Loc.T("Zahtjev je poslan. Administrator će vam postaviti privremenu lozinku.");
        }
        catch (ApiException ex)
        {
            LoginError = SignInRefusal(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The request for a new password failed");
            LoginError = Loc.T("Zahtjev nije poslan: ") + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsBusy) return;
        LoginError = "";
        LoginInfo = "";
        if (LoginUsername.Trim().Length == 0 || LoginPassword.Length == 0)
        {
            LoginError = Loc.T("Unesite korisničko ime i lozinku.");
            return;
        }

        if (!ApplyServerAddress()) return;

        var signedIn = false;
        try
        {
            IsBusy = true;
            var result = await _session.LoginAsync(LoginUsername.Trim(), LoginPassword, LoginRemember);
            _pendingToken = result.Token;
            if (result.MustChangePassword)
            {
                MustChangePassword = true;
                NewPassword = "";
                NewPasswordAgain = "";
            }
            else
            {
                CompleteSignIn();
                signedIn = true;
            }
        }
        catch (ApiException ex)
        {
            LoginError = SignInRefusal(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sign-in failed");
            LoginError = Loc.T("Prijava nije uspjela: ") + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (signedIn) await LoadAsync();
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (IsBusy) return;
        LoginError = "";
        if (NewPassword.Length < 10) { LoginError = Loc.T("Lozinka mora imati najmanje 10 znakova."); return; }
        if (NewPassword != NewPasswordAgain) { LoginError = Loc.T("Lozinke se ne podudaraju."); return; }

        var changed = false;
        try
        {
            IsBusy = true;
            await _session.ChangePasswordAsync(LoginPassword, NewPassword);
            CompleteSignIn();
            changed = true;
        }
        catch (ApiException ex)
        {
            LoginError = SignInRefusal(ex);
        }
        finally
        {
            IsBusy = false;
        }

        if (changed) await LoadAsync();
    }

    private void CompleteSignIn()
    {
        if (LoginRemember && _pendingToken != null) _tokens.Save(_pendingToken);
        else _tokens.Clear();

        _pendingToken = null;
        LoginPassword = "";
        NewPassword = "";
        NewPasswordAgain = "";
        LoginError = "";
        MustChangePassword = false;
        NeedsSignIn = false;
    }

    private static string SignInRefusal(ApiException ex) => ex.Code switch
    {
        "invalid-credentials" => Loc.T("Pogrešno korisničko ime ili lozinka."),
        "locked" => Loc.T("Račun je privremeno zaključan zbog previše pogrešnih lozinki. Pokušajte kasnije ili se obratite administratoru."),
        "weak-password" => Loc.T("Lozinka je preslaba: najmanje 10 znakova i različita od korisničkog imena."),
        "same-password" => Loc.T("Izaberite lozinku različitu od trenutne."),
        "wrong-password" => Loc.T("Trenutna lozinka nije tačna."),
        "unreachable" or "timeout" => Loc.T("Server nije dostupan. Provjerite adresu servera i mrežnu vezu (VPN)."),
        _ => ApiMessages.Describe(ex)
    };

    // ---- Sign-out ----------------------------------------------------------------------------------------------------

    /// <summary>Back to the sign-in screen. The next start shows it too, until the user signs in again with "remember me".</summary>
    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _session.LogoutAsync();
        _tokens.Clear();
        ShowSignIn("");
        LoginUsername = "";
        LoginPassword = "";
        LoginRemember = false;
    }
}
