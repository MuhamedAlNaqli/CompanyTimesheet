using Company.Api.Client;
using Company.Api.Contracts;
using CompanyTimesheet.Desktop;
using CompanyTimesheet.Desktop.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using Timesheet.Domain;

namespace CompanyTimesheet.Desktop.Tests;

/// <summary>The employee view model against a server that is a few lines of code: no window, no network, no file dialog</summary>
[TestClass]
public class EmployeeViewModelTests
{
    // ---- the doubles ---------------------------------------------------------------------------------------------------------

    private sealed class FakeClient : ICompanyApiClient
    {
        public List<string> Calls { get; } = new();
        public Func<int, int, Task<SheetDto>> GetSheet { get; set; } = (y, m) => Task.FromResult(Sheets.Make(y, m));
        public Func<string, int, int, Task<SheetDto>> GetReview { get; set; } = (c, y, m) => Task.FromResult(Sheets.Make(y, m, SheetStatus.Submitted, editable: false, owner: c, canAct: true));
        public List<PendingApprovalDto> Pending { get; set; } = new();
        public List<ReminderDto> Reminders { get; set; } = new();
        public bool IsApprover { get; set; }
        public bool SheetExport { get; set; }
        public bool IsAdmin { get; set; }

        private Task<T> Record<T>(string call, T value) { Calls.Add(call); return Task.FromResult(value); }

        public string? ServerTheme { get; set; }
        public string? ServerPalette { get; set; }
        public List<TimeCodeDto> ServerCodes { get; set; } = new();
        public List<PreferencesRequest> Preferences { get; } = new();

        public Task<MeDto> GetMeAsync(CancellationToken ct = default) =>
            Record("me", new MeDto("Z-001", "Amra Adžamija", @"corp\amra", IsAdmin, IsApprover, false, "Z-002", new[] { "Z-002" }, "Test", false, SheetExport, ServerTheme, ServerPalette));

        public Task SetPreferencesAsync(PreferencesRequest request, CancellationToken ct = default) { Preferences.Add(request); return Task.CompletedTask; }

        public Task<SheetDto> GetSheetAsync(int year, int month, CancellationToken ct = default) { Calls.Add($"sheet {year}-{month}"); return GetSheet(year, month); }
        public Task<SheetDto> GetReviewAsync(string code, int year, int month, CancellationToken ct = default) { Calls.Add($"review {code} {year}-{month}"); return GetReview(code, year, month); }
        public Task<List<PendingApprovalDto>> GetPendingAsync(CancellationToken ct = default) => Record("pending", Pending);
        public Task<RemindersDto> GetRemindersAsync(CancellationToken ct = default) => Record("reminders", new RemindersDto(new DateOnly(2026, 9, 10), Reminders));
        public Task<SheetDto> SubmitAsync(int year, int month, CancellationToken ct = default) =>
            Record($"submit {year}-{month}", Sheets.Make(year, month, SheetStatus.Submitted, editable: false));
        public Task<SheetDto> ApproveAsync(string code, int year, int month, CancellationToken ct = default) =>
            Record($"approve {code} {year}-{month}", Sheets.Make(year, month, SheetStatus.Approved, editable: false, owner: code));
        public Task<SheetDto> ReturnAsync(string code, int year, int month, string message, CancellationToken ct = default) =>
            Record($"return {code} {year}-{month}: {message}", Sheets.Make(year, month, SheetStatus.Returned, editable: false, owner: code));
        public Task<SheetDto> SetEntryAsync(int year, int month, DateOnly date, SetEntryRequest request, CancellationToken ct = default) =>
            Record($"entry {date:yyyy-MM-dd} {request.Code} {request.Minutes}", Sheets.Make(year, month));
        public Task<SheetDto> FillStandardDaysAsync(int year, int month, CancellationToken ct = default) => Record($"fill {year}-{month}", Sheets.Make(year, month));
        public Task<SheetDto> AddOvertimeAsync(int year, int month, OvertimeRequest request, CancellationToken ct = default) => Record($"overtime add {request.Date:yyyy-MM-dd}", Sheets.Make(year, month));
        public Task<SheetDto> UpdateOvertimeAsync(int year, int month, int id, OvertimeRequest request, CancellationToken ct = default) => Record($"overtime update {id}", Sheets.Make(year, month));
        public Task<SheetDto> ApplyAbsenceAsync(int year, int month, AbsenceRangeRequest request, CancellationToken ct = default) => Record($"absence {request.Code}", Sheets.Make(year, month));
        public Task<SheetDto> AnswerAsync(int year, int month, int index, string answer, CancellationToken ct = default) => Record($"answer {index}: {answer}", Sheets.Make(year, month));

        public Task<AuthDiagnosticsDto> GetDiagnosticsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<TimeCodeDto>> GetTimeCodesAsync(CancellationToken ct = default) => Task.FromResult(ServerCodes);
        public Task<SheetDto> ClearEntryAsync(int year, int month, DateOnly date, int code, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AbsencePreviewDto> PreviewAbsenceAsync(int year, int month, AbsenceRangeRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SheetDto> DeleteOvertimeAsync(int year, int month, int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SheetDto> GetRevisionAsync(int year, int month, int revision, CancellationToken ct = default) =>
            Record($"revision {revision} {year}-{month}", Sheets.Make(year, month, SheetStatus.Submitted, editable: false, canSubmit: true));
        public Task<SheetDto> GetReviewRevisionAsync(string code, int year, int month, int revision, CancellationToken ct = default) =>
            Record($"review-revision {code} {revision} {year}-{month}", Sheets.Make(year, month, SheetStatus.Submitted, editable: false, owner: code, canAct: true));

        public DeputiesDto Deputies { get; set; } = new(new List<DeputyDto>(), new List<DeputyDto>());
        public string? DeputyRefusal { get; set; }
        public Task<DeputiesDto> GetDeputiesAsync(CancellationToken ct = default) => Record("deputies", Deputies);
        public Task<List<PersonDto>> GetDeputyCandidatesAsync(CancellationToken ct = default) =>
            Record("candidates", new List<PersonDto> { new("Z-004", "Damir Dizdarević"), new("Z-005", "Dario Kovačević") });
        public Task<DeputyDto> AddDeputyAsync(AddDeputyRequest request, CancellationToken ct = default)
        {
            Calls.Add($"add-deputy {request.DeputyCode} {request.From:yyyy-MM-dd}..{request.To:yyyy-MM-dd}");
            if (DeputyRefusal != null) throw new ApiException(HttpStatusCode.Conflict, DeputyRefusal, DeputyRefusal);
            return Task.FromResult(new DeputyDto(1, "Z-001", "Amra", request.DeputyCode, "Damir Dizdarević", request.From, request.To));
        }
        public Task RemoveDeputyAsync(int id, CancellationToken ct = default) { Calls.Add($"remove-deputy {id}"); return Task.CompletedTask; }
        public Task<DownloadedFile> GetPrintAsync(int year, int month, string format, CancellationToken ct = default) => Record($"print {format}", new DownloadedFile("a." + format, new byte[] { 1 }));
        public Task<DownloadedFile> GetReviewPrintAsync(string code, int year, int month, string format, CancellationToken ct = default) => Record($"print {code} {format}", new DownloadedFile("a." + format, new byte[] { 1 }));
        public Task<DownloadedFile> GetTeamPrintAsync(int year, int month, string format, CancellationToken ct = default) => Record($"team {format}", new DownloadedFile("t." + format, new byte[] { 1 }));
        public Task<DownloadedFile> GetApprovedExportAsync(int year, int month, string format, CancellationToken ct = default) => throw new NotSupportedException();

        public List<TeamMemberDto> Team { get; set; } = new();
        public Dictionary<string, string> RemindRefusals { get; } = new();
        public string SummaryText { get; set; } = "Šihtarice za Septembar 2026";
        public Task<TeamSummaryDto> GetTeamSummaryTextAsync(int year, int month, CancellationToken ct = default) =>
            Record($"summary-text {year}-{month}", new TeamSummaryDto(year, month,
                new[] { new SummaryGroupDto(null, new[] { new SummaryPersonDto("Amra Adžamija", new[] { "Nema prekovremenih sati niti izostanaka." }, "Šihtarica: Nacrt (nije odobreno)") }) },
                Array.Empty<SummaryOvertimeDto>(), SummaryText));
        public Task<DownloadedFile> GetTeamSummaryAsync(int year, int month, string format, CancellationToken ct = default) => Record($"summary {format}", new DownloadedFile("s." + format, new byte[] { 1 }));

        public Task<TeamStatusDto> GetTeamStatusAsync(int year, int month, CancellationToken ct = default)
        {
            Calls.Add($"team-status {year}-{month}");
            return Team.Count == 0
                ? throw new ApiException(System.Net.HttpStatusCode.NotFound, "no-team", "Nobody reports to you.")
                : Task.FromResult(new TeamStatusDto(year, month, new DateOnly(year, month, 25), Team));
        }

        public Task RemindAsync(string code, int year, int month, CancellationToken ct = default)
        {
            Calls.Add($"remind {code} {year}-{month}");
            if (RemindRefusals.TryGetValue(code, out var refusal)) throw new ApiException(System.Net.HttpStatusCode.Conflict, refusal, refusal);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDialogs : IEmployeeDialogs
    {
        public bool Answer { get; set; } = true;
        public string? Prompted { get; set; }
        public EntryResult? Entry { get; set; }
        public List<string> Asked { get; } = new();

        public AdminEditResult? AdminEdit { get; set; }
        public string? Csv { get; set; }
        public Task<AdminEditResult?> AdminEditAsync(AdminEmployeeViewModel viewModel) { Asked.Add("admin-edit " + viewModel.Title); return Task.FromResult(AdminEdit); }
        public Task<string?> PickCsvAsync() { Asked.Add("pick-csv"); return Task.FromResult(Csv); }

        public SaveCodeRequest? CodeEdit { get; set; }
        public Task<SaveCodeRequest?> CodeEditAsync(CodeEditViewModel viewModel) { Asked.Add("code-edit " + viewModel.Title); return Task.FromResult(CodeEdit); }

        public Task<bool> ConfirmAsync(string title, string message) { Asked.Add(title + ": " + message); return Task.FromResult(Answer); }
        public Task<EntryResult?> EntryAsync(EntryViewModel viewModel) { Asked.Add("entry"); return Task.FromResult(Entry); }
        public Task<string?> PromptAsync(TextPromptViewModel viewModel) { Asked.Add("prompt"); return Task.FromResult(Prompted); }
    }

    private sealed class FakeSaver : ISheetFileSaver
    {
        public FileSaveResult Result { get; set; } = new("C:\\out\\a.xlsx", null);
        public Task<FileSaveResult> SaveAsync(string? format, Func<string, Task<DownloadedFile>> download) => Task.FromResult(Result);
    }

    private sealed class FakeSession : ICompanySession
    {
        public bool PasswordSignIn { get; set; }
        public bool HasToken { get; private set; }
        public List<string> Servers { get; } = new();
        public Exception? RefuseServer { get; set; }

        public void UseServer(string address)
        {
            if (RefuseServer != null) throw RefuseServer;
            Servers.Add(address);
        }
        public bool MustChange { get; set; }
        public string? RefuseWith { get; set; }
        public List<string> Calls { get; } = new();
        public void UseToken(string? token) => HasToken = token != null;

        public Task<LoginResultDto> LoginAsync(string username, string password, bool remember, CancellationToken ct = default)
        {
            Calls.Add($"login {username} {remember}");
            if (RefuseWith != null) throw new ApiException(HttpStatusCode.Unauthorized, RefuseWith, RefuseWith);
            HasToken = true;
            return Task.FromResult(new LoginResultDto("token-1", DateTime.UtcNow.AddHours(12), MustChange));
        }

        public Task LogoutAsync(CancellationToken ct = default) { Calls.Add("logout"); HasToken = false; return Task.CompletedTask; }

        public Task ChangePasswordAsync(string current, string newPassword, CancellationToken ct = default)
        {
            Calls.Add($"change {current} -> {newPassword}");
            if (RefuseWith != null) throw new ApiException(HttpStatusCode.BadRequest, RefuseWith, RefuseWith);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAdmin : ICompanyAdminClient
    {
        public List<string> Calls { get; } = new();
        public List<EmployeeDto> Employees { get; set; } = new();
        public List<CredentialDto> Credentials { get; set; } = new();
        public ImportPreviewDto Preview { get; set; } = new(new List<ImportRowDto>(), 0, 0);
        public string? RefuseWith { get; set; }

        private void Maybe() { if (RefuseWith != null) throw new ApiException(HttpStatusCode.Conflict, RefuseWith, RefuseWith); }

        public Task<List<EmployeeDto>> GetEmployeesAsync(CancellationToken ct = default) { Calls.Add("employees"); return Task.FromResult(Employees); }
        public Task<List<CredentialDto>> GetCredentialsAsync(CancellationToken ct = default) { Calls.Add("credentials"); return Task.FromResult(Credentials); }
        public string? RefuseCredentialsWith { get; set; }
        public Task<EmployeeDto> CreateEmployeeAsync(CreateEmployeeRequest request, CancellationToken ct = default)
        {
            Calls.Add($"create {request.Code} {request.Name} supervisor={request.SupervisorCode}");
            Maybe();
            var created = Person(string.IsNullOrEmpty(request.Code) ? "Z-007" : request.Code, request.Name, request.SupervisorCode);
            Employees.Add(created);
            return Task.FromResult(created);
        }
        public Task<EmployeeDto> UpdateEmployeeAsync(string code, UpdateEmployeeRequest request, CancellationToken ct = default)
        {
            Calls.Add($"update {code} {request.Name} supervisor={request.SupervisorCode}");
            Maybe();
            return Task.FromResult(Employees.First(e => e.Code == code));
        }
        public Task<ImportPreviewDto> PreviewImportAsync(string csv, CancellationToken ct = default) { Calls.Add("preview"); return Task.FromResult(Preview); }
        public Task<ImportResultDto> ImportAsync(string csv, CancellationToken ct = default) { Calls.Add("import"); return Task.FromResult(new ImportResultDto(2, 1, 0)); }
        public Task SetCredentialsAsync(string code, SetCredentialsRequest request, CancellationToken ct = default) {
            Calls.Add($"set {code} {request.Username} {request.MustChange}");
            Maybe();
            if (RefuseCredentialsWith != null) throw new ApiException(HttpStatusCode.Conflict, RefuseCredentialsWith, RefuseCredentialsWith);
            return Task.CompletedTask;
        }
        public Task RemoveCredentialsAsync(string code, CancellationToken ct = default) { Calls.Add($"remove {code}"); Maybe(); return Task.CompletedTask; }
        public List<CodeDefinitionDto> Codes { get; set; } = new();
        public Task<List<CodeDefinitionDto>> GetCodesAsync(CancellationToken ct = default) { Calls.Add("codes"); return Task.FromResult(Codes); }
        public Task<CodeDefinitionDto> AddCodeAsync(SaveCodeRequest request, CancellationToken ct = default)
        {
            Calls.Add($"add-code {request.Code} {request.NameBs} {request.Colour} {request.Unit}");
            Maybe();
            var added = new CodeDefinitionDto(request.Code, request.NameBs, request.NameEn, request.Colour, request.Unit, request.IsActive, false, false);
            Codes.Add(added);
            return Task.FromResult(added);
        }
        public Task<CodeDefinitionDto> UpdateCodeAsync(int code, SaveCodeRequest request, CancellationToken ct = default)
        {
            Calls.Add($"update-code {code} {request.NameBs} active={request.IsActive}");
            Maybe();
            return Task.FromResult(Codes.First(c => c.Code == code));
        }
        public bool AuditIntact { get; set; } = true;
        public Task<AuditVerificationDto> VerifyAuditAsync(CancellationToken ct = default) =>
            Task.FromResult(new AuditVerificationDto(AuditIntact, 120, 0, AuditIntact ? null : 77, null));
    }

    private sealed class FakeServer : IServerSettings
    {
        public string Address { get; set; } = "http://localhost:5090";
        public void Save(string address) => Address = address;
    }

    private sealed class FakeUpdates : IUpdateService
    {
        public UpdateCheckOutcome Outcome { get; set; } = UpdateCheckOutcome.UpToDate("0.1.0.0");
        public int Checks { get; private set; }
        public Task InitializeAsync() => Task.CompletedTask;
        public Task<UpdateCheckOutcome> CheckAndPromptAsync() { Checks++; return Task.FromResult(Outcome); }
        public void StartAutomaticUpdateChecking(TimeSpan interval) { }
        public void StopAutomaticUpdateChecking() { }
        public string GetCurrentVersion() => "0.1.0.0";
        public bool AutoUpdatesEnabled { get; set; } = true;
    }

    private sealed class FakeTokens : IEmployeeSignInStore
    {
        public string? Token { get; set; }
        public string? Load() => Token;
        public void Save(string token) => Token = token;
        public void Clear() => Token = null;
    }

    private sealed class FakeShell : IEmployeeShell
    {
        public List<string> Copied { get; } = new();
        public Task CopyAsync(string text) { Copied.Add(text); return Task.CompletedTask; }
    }

    private static class Sheets
    {
        public static SheetDto Make(int year, int month, SheetStatus status = SheetStatus.Draft, bool editable = true, string owner = "Z-001", bool canAct = false, bool canSubmit = false)
        {
            var days = Enumerable.Range(1, DateTime.DaysInMonth(year, month)).Select(d =>
            {
                var date = new DateOnly(year, month, d);
                var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                return new DayDto(date, "P", weekend ? DayKind.Weekend : DayKind.Working, null, new List<EntryDto>(), new List<AllocationDto>());
            }).ToList();

            var workflow = new WorkflowDto(owner, "Amra Adžamija", new[] { new ChainStepDto("Z-002", "Benjamin Bašić") }, 0, status == SheetStatus.Submitted ? "Z-002" : null,
                status == SheetStatus.Submitted ? "Benjamin Bašić" : null, new List<MessageDto>(), new List<HistoryDto>(), new List<RevisionDto>(), canSubmit, canAct);
            return new SheetDto(year, month, status, 0, editable, days, new List<OvertimeDto>(),
                new SummaryDto(new Dictionary<int, int>(), new Dictionary<int, int>(), 0, 176 * 60, 0), new List<IssueDto>(), workflow, new List<CarriedOvertimeDto>());
        }
    }

    private static (EmployeeViewModel Vm, FakeClient Client, FakeDialogs Dialogs, FakeSaver Saver, FakeShell Shell) Create() => CreateWith(out _, out _);

    private static (EmployeeViewModel Vm, FakeClient Client, FakeDialogs Dialogs, FakeSaver Saver, FakeShell Shell) CreateWith(out FakeSession session, out FakeTokens tokens) =>
        CreateWith(out session, out tokens, out _);

    private static (EmployeeViewModel Vm, FakeClient Client, FakeDialogs Dialogs, FakeSaver Saver, FakeShell Shell) CreateWith(out FakeSession session, out FakeTokens tokens, out FakeAdmin admin) =>
        CreateWith(out session, out tokens, out admin, out _);

    private static (EmployeeViewModel Vm, FakeClient Client, FakeDialogs Dialogs, FakeSaver Saver, FakeShell Shell) CreateWith(out FakeSession session, out FakeTokens tokens, out FakeAdmin admin, out FakeUpdates updates)
    {
        return CreateWith(out session, out tokens, out admin, out updates, out _);
    }

    private static (EmployeeViewModel Vm, FakeClient Client, FakeDialogs Dialogs, FakeSaver Saver, FakeShell Shell) CreateWith(out FakeSession session, out FakeTokens tokens, out FakeAdmin admin, out FakeUpdates updates, out FakeServer server)
    {
        updates = new FakeUpdates();
        server = new FakeServer();
        admin = new FakeAdmin();
        var client = new FakeClient();
        var dialogs = new FakeDialogs();
        var saver = new FakeSaver();
        var shell = new FakeShell();
        session = new FakeSession();
        tokens = new FakeTokens();
        var vm = new EmployeeViewModel(client, session, admin, tokens, dialogs, saver, shell, updates, server, NullLogger<EmployeeViewModel>.Instance) { Year = 2026, Month = 9 };
        return (vm, client, dialogs, saver, shell);
    }

    // ---- loading ----------------------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task LoadingShowsTheMonth_TheReminders_AndWhatWaitsForApproval()
    {
        var (vm, client, _, _, _) = Create();
        client.IsApprover = true;
        client.Pending.Add(new PendingApprovalDto("Z-004", "Dario Kovačević", 2026, 9, 1, 0, DateTime.UtcNow, 0, 1));
        client.Reminders.Add(new ReminderDto(ReminderType.SubmitOverdue, 2026, 8, 1, new DateOnly(2026, 8, 25)));

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsConnected);
        Assert.AreEqual(30, vm.Sheet.Days.Count);
        Assert.AreEqual(1 + 30, vm.Sheet.CalendarCells.Count, "September 2026 starts on a Tuesday: one empty square, then the days");
        Assert.IsTrue(vm.IsApprover);
        Assert.IsTrue(vm.Reminders.HasAny);
        Assert.AreEqual("Za odobravanje (1)", vm.PendingApprovals.TabText);
        Assert.AreEqual("", vm.ErrorText);
    }

    [TestMethod]
    public async Task AServerThatCannotBeReached_IsExplainedInTheWindow()
    {
        var (vm, client, _, _, _) = Create();
        client.GetSheet = (_, _) => throw new ApiException(HttpStatusCode.ServiceUnavailable, "unreachable", "no network");

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsConnected);
        StringAssert.Contains(vm.ErrorText, "nije dostupan");
    }

    // ---- only one call at a time, and the month on screen is the month acted on -----------------------------------------------------

    [TestMethod]
    public async Task ACallThatIsStillRunning_IsNotOverlapped()
    {
        var (vm, client, _, _, _) = Create();
        await vm.LoadCommand.ExecuteAsync(null);

        var release = new TaskCompletionSource<SheetDto>();
        client.GetSheet = (_, _) => release.Task;
        var first = vm.LoadCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsBusy);

        var callsBefore = client.Calls.Count;
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.NextMonthCommand.ExecuteAsync(null);
        Assert.AreEqual(callsBefore, client.Calls.Count, "nothing was sent while the first call runs");
        Assert.AreEqual(9, vm.Month, "and the month did not move");

        release.SetResult(Sheets.Make(2026, 9));
        await first;
        Assert.IsFalse(vm.IsBusy);
    }

    [TestMethod]
    public async Task AMonthThatCouldNotBeLoaded_IsNotTheMonthThatIsActedOn()
    {
        var (vm, client, dialogs, _, _) = Create();
        await vm.LoadCommand.ExecuteAsync(null);

        client.GetSheet = (y, m) => m == 10 ? throw new ApiException(HttpStatusCode.ServiceUnavailable, "unreachable", "no network") : Task.FromResult(Sheets.Make(y, m));
        await vm.NextMonthCommand.ExecuteAsync(null);

        Assert.AreEqual(9, vm.Month, "the screen still shows September");
        Assert.AreEqual("Septembar 2026.", vm.MonthTitle);

    }

    // ---- submitting and deciding ------------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task Submitting_AsksFirst_AndDoesNothingWhenTheAnswerIsNo()
    {
        var (vm, client, dialogs, _, _) = Create();
        client.GetSheet = (y, m) => Task.FromResult(Sheets.Make(y, m, canSubmit: true));
        await vm.LoadCommand.ExecuteAsync(null);

        dialogs.Answer = false;
        await vm.SubmitCommand.ExecuteAsync(null);
        Assert.IsFalse(client.Calls.Any(c => c.StartsWith("submit")));

        dialogs.Answer = true;
        await vm.SubmitCommand.ExecuteAsync(null);
        CollectionAssert.Contains(client.Calls, "submit 2026-9");
        Assert.AreEqual("Predano na odobravanje", vm.StatusText);
    }

    [TestMethod]
    public async Task AReturnNeedsAMessage_AndNothingIsSentWhenItIsCancelled()
    {
        var (vm, client, dialogs, _, _) = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.OpenPendingCommand.Execute(new PendingRow(new PendingApprovalDto("Z-004", "Dino", 2026, 9, 1, 0, DateTime.UtcNow, 0, 0)));
        await Task.Delay(50);
        Assert.IsTrue(vm.IsReviewing);
        Assert.IsTrue(vm.CanAct);

        dialogs.Prompted = null;
        await vm.ReturnCommand.ExecuteAsync(null);
        Assert.IsFalse(client.Calls.Any(c => c.StartsWith("return")));

        dialogs.Prompted = "Ispravi sate";
        await vm.ReturnCommand.ExecuteAsync(null);
        CollectionAssert.Contains(client.Calls, "return Z-004 2026-9: Ispravi sate");
        Assert.AreEqual("Šihtarica je vraćena zaposleniku.", vm.InfoText);
    }

    [TestMethod]
    public async Task ApprovingWhileAnotherCallRuns_IsNotReportedAsDone()
    {
        var (vm, client, dialogs, _, _) = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        vm.OpenPendingCommand.Execute(new PendingRow(new PendingApprovalDto("Z-004", "Dino", 2026, 9, 1, 0, DateTime.UtcNow, 0, 0)));
        await Task.Delay(50);

        var release = new TaskCompletionSource<SheetDto>();
        client.GetReview = (_, _, _) => release.Task;
        var running = vm.LoadCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsBusy);

        await vm.ApproveCommand.ExecuteAsync(null);
        Assert.IsFalse(client.Calls.Any(c => c.StartsWith("approve")), "the server was not asked");
        Assert.AreEqual("", vm.InfoText, "so nothing is said to have happened");

        release.SetResult(Sheets.Make(2026, 9, SheetStatus.Submitted, editable: false, owner: "Z-004", canAct: true));
        await running;
    }

    [TestMethod]
    public async Task APersonWithNobodyAbove_ApprovesTheirOwnMonthFromTheirOwnScreen()
    {
        var (vm, client, dialogs, _, _) = Create();
        client.GetSheet = (y, m) => Task.FromResult(Sheets.Make(y, m, SheetStatus.Submitted, editable: false, canAct: true));
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.CanApproveOwn);
        await vm.ApproveCommand.ExecuteAsync(null);

        CollectionAssert.Contains(client.Calls, "approve Z-001 2026-9");
        Assert.AreEqual("Odobreno.", vm.InfoText);
        StringAssert.Contains(dialogs.Asked.Last(), "svoju šihtaricu");
    }

    // ---- entries ----------------------------------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task WhatTheEntryDialogReturns_IsSentToTheServer_AndACancelledDialogSendsNothing()
    {
        var (vm, client, dialogs, _, _) = Create();
        await vm.LoadCommand.ExecuteAsync(null);

        dialogs.Entry = null;
        await vm.AddEntryCommand.ExecuteAsync(null);
        Assert.IsFalse(client.Calls.Any(c => c.StartsWith("entry")));

        dialogs.Entry = new EntryResult(new SetEntryRequest(1010, 450), new DateOnly(2026, 9, 7), null, null, null);
        await vm.AddEntryCommand.ExecuteAsync(null);
        CollectionAssert.Contains(client.Calls, "entry 2026-09-07 1010 450");

        dialogs.Entry = new EntryResult(null, null, null, new OvertimeRequest(new DateOnly(2026, 9, 5), new TimeOnly(8, 30), new TimeOnly(11, 0), "x"), 42);
        await vm.AddEntryCommand.ExecuteAsync(null);
        CollectionAssert.Contains(client.Calls, "overtime update 42");
    }

    [TestMethod]
    public async Task ALockedMonth_CannotBeEdited()
    {
        var (vm, client, dialogs, _, _) = Create();
        client.GetSheet = (y, m) => Task.FromResult(Sheets.Make(y, m, SheetStatus.Submitted, editable: false));
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.AddEntryCommand.ExecuteAsync(null);

        Assert.AreEqual(0, dialogs.Asked.Count, "no dialog is opened");
        Assert.IsFalse(vm.IsEditable);
    }

    // ---- printing and leaving --------------------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task APrintout_ReportsWhereItWentOrWhatWentWrong_AndNothingWhenCancelled()
    {
        var (vm, _, _, saver, _) = Create();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.PrintCommand.ExecuteAsync("xlsx");
        Assert.AreEqual("Sačuvano: C:\\out\\a.xlsx", vm.InfoText);

        saver.Result = new FileSaveResult(null, "Datoteka nije sačuvana: disk je pun");
        await vm.PrintCommand.ExecuteAsync("pdf");
        Assert.AreEqual("Datoteka nije sačuvana: disk je pun", vm.ErrorText);
        Assert.AreEqual("", vm.InfoText);

        saver.Result = FileSaveResult.Cancelled;
        await vm.PrintTeamCommand.ExecuteAsync("pdf");
        Assert.AreEqual("", vm.ErrorText);
        Assert.AreEqual("", vm.InfoText);
        Assert.IsFalse(vm.IsBusy);
    }

    // ---- the team -----------------------------------------------------------------------------------------------------------------

    private static TeamMemberDto Member(string code, string name, SheetStatus status, int depth = 0, string? parent = null, bool canRemind = true, string? waiting = null) =>
        new(code, name, parent, depth, status, waiting, canRemind, null);

    [TestMethod]
    public async Task TheTeamTab_ShowsTheTreeWithWhoHasNotSubmitted()
    {
        var (vm, client, _, _, _) = Create();
        client.IsApprover = true;
        client.Team = new()
        {
            Member("Z-002", "Benjamin Bašić", SheetStatus.Submitted, canRemind: false, waiting: "Emir Ferhatbegović"),
            Member("Z-001", "Amra Adžamija", SheetStatus.Draft, depth: 1, parent: "Z-002"),
            Member("Z-004", "Damir Dizdarević", SheetStatus.Approved, depth: 1, parent: "Z-002", canRemind: false)
        };
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.ShowTeamCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.InTeam);
        Assert.IsFalse(vm.InMine);
        Assert.IsTrue(vm.ShowTeamToolbar);
        Assert.IsFalse(vm.ShowMineToolbar, "the toolbar of the own month is replaced");
        CollectionAssert.AreEqual(new[] { "Benjamin Bašić", "Amra Adžamija", "Damir Dizdarević" }, vm.Team.Rows.Select(r => r.Name).ToArray());
        Assert.AreEqual("Predano, čeka: Emir Ferhatbegović", vm.Team.Rows[0].StatusText);
        Assert.AreEqual("Nije predano", vm.Team.Rows[1].StatusText);
        Assert.IsTrue(vm.Team.Rows[1].CanRemind);
        Assert.IsFalse(vm.Team.Rows[2].CanRemind, "approved: nothing to remind");
        Assert.AreEqual(30, vm.Team.Rows[1].Indent.Left, "one step of the tree for the level below");
        StringAssert.StartsWith(vm.Team.SummaryText, "Nisu predali: 1 od 3");
        Assert.IsTrue(vm.Team.HasPeopleToRemind);

        await vm.ShowMineCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.InTeam);
        Assert.IsTrue(vm.ShowMineToolbar);
    }

    [TestMethod]
    public async Task ChangingTheMonthInTheTeamTab_ReadsTheTeamOfThatMonth_NotTheOwnSheet()
    {
        var (vm, client, _, _, _) = Create();
        client.IsApprover = true;
        client.Team = new() { Member("Z-001", "Amra Adžamija", SheetStatus.Draft) };
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ShowTeamCommand.ExecuteAsync(null);
        client.Calls.Clear();

        await vm.NextMonthCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "team-status 2026-10" }, client.Calls);
        Assert.AreEqual(10, vm.Month);
    }

    [TestMethod]
    public async Task Reminding_AsksTheServerForThatPerson_AndSaysSo()
    {
        var (vm, client, _, _, _) = Create();
        client.IsApprover = true;
        client.Team = new() { Member("Z-001", "Amra Adžamija", SheetStatus.Draft), Member("Z-004", "Damir Dizdarević", SheetStatus.Draft) };
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ShowTeamCommand.ExecuteAsync(null);

        await vm.RemindCommand.ExecuteAsync(vm.Team.Rows[0]);

        CollectionAssert.Contains(client.Calls, "remind Z-001 2026-9");
        Assert.AreEqual("Podsjetnik je poslan: Amra Adžamija.", vm.InfoText);
        Assert.IsTrue(vm.Team.Rows[0].HasReminder, "the row shows that it was sent");
        Assert.IsFalse(vm.Team.Rows[1].HasReminder);
    }

    [TestMethod]
    public async Task AReminderThatWasRefused_IsExplainedInBosnian()
    {
        var (vm, client, _, _, _) = Create();
        client.IsApprover = true;
        client.Team = new() { Member("Z-001", "Amra Adžamija", SheetStatus.Draft) };
        client.RemindRefusals["Z-001"] = "already-reminded";
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ShowTeamCommand.ExecuteAsync(null);

        await vm.RemindCommand.ExecuteAsync(vm.Team.Rows[0]);

        Assert.AreEqual("Podsjetnik za Amra Adžamija je već poslan. Pokušajte kasnije.", vm.ErrorText);
        Assert.IsFalse(vm.Team.Rows[0].HasReminder);
        Assert.IsFalse(vm.IsBusy);
    }

    [TestMethod]
    public async Task RemindingEverybody_SkipsThoseWhoCannotBeReminded_AndThoseAlreadyReminded()
    {
        var (vm, client, _, _, _) = Create();
        client.IsApprover = true;
        client.Team = new()
        {
            Member("Z-001", "Amra Adžamija", SheetStatus.Draft),
            Member("Z-002", "Benjamin Bašić", SheetStatus.Approved, canRemind: false),
            Member("Z-004", "Damir Dizdarević", SheetStatus.Returned),
            Member("Z-005", "Dario Kovačević", SheetStatus.Draft)
        };
        client.RemindRefusals["Z-004"] = "already-reminded";
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ShowTeamCommand.ExecuteAsync(null);

        await vm.RemindAllCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "remind Z-001 2026-9", "remind Z-004 2026-9", "remind Z-005 2026-9" }, client.Calls.Where(c => c.StartsWith("remind ")).ToArray());
        Assert.AreEqual("Podsjetnik je poslan: 2.", vm.InfoText);
        Assert.AreEqual("", vm.ErrorText);
    }

    [TestMethod]
    public async Task ANobodyBelow_GivesAnEmptyTreeNotAnError()
    {
        var (vm, client, _, _, _) = Create();
        client.IsApprover = true;
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.ShowTeamCommand.ExecuteAsync(null);

        Assert.AreEqual("", vm.ErrorText);
        Assert.IsTrue(vm.Team.IsEmpty);
        Assert.IsFalse(vm.Team.HasPeopleToRemind);
    }

    [TestMethod]
    public async Task TheSummaryMessage_IsShownInsteadOfTheTree_AndCopiedAsItIs()
    {
        var (vm, client, _, _, shell) = Create();
        client.IsApprover = true;
        client.Team = new() { Member("Z-001", "Amra Adžamija", SheetStatus.Draft) };
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ShowTeamCommand.ExecuteAsync(null);

        await vm.ShowSummaryCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.Team.ShowSummary);
        Assert.IsFalse(vm.Team.ShowTree);
        Assert.AreEqual("Šihtarice za Septembar 2026", vm.Team.SummaryBody);
        Assert.AreEqual("Još nisu odobrene šihtarice: Amra Adžamija.", vm.Team.SummaryNote, "the supervisor sees it before sending");

        await vm.CopySummaryCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { "Šihtarice za Septembar 2026" }, shell.Copied);
        Assert.AreEqual("Kopirano. Zalijepite u mail.", vm.InfoText);

        client.Calls.Clear();
        await vm.NextMonthCommand.ExecuteAsync(null);
        CollectionAssert.Contains(client.Calls, "summary-text 2026-10", "the message follows the month");

        vm.ShowTreeCommand.Execute(null);
        Assert.IsTrue(vm.Team.ShowTree);
    }

    [TestMethod]
    public async Task TheFileExport_IsOfferedOnlyWhenTheServerHasItSwitchedOn()
    {
        var (vm, client, _, _, _) = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.CanExport, "the default of the product");

        var (on, onClient, _, _, _) = Create();
        onClient.SheetExport = true;
        await on.LoadCommand.ExecuteAsync(null);
        Assert.IsTrue(on.CanExport);
    }

    [TestMethod]
    public async Task TheSummary_IsSavedLikeAnyPrintout()
    {
        var (vm, client, _, saver, _) = Create();
        client.IsApprover = true;
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.PrintSummaryCommand.ExecuteAsync("xlsx");

        Assert.AreEqual("Sačuvano: C:\\out\\a.xlsx", vm.InfoText);
    }

    [TestMethod]
    public void ANudgeIsWordedWithTheNameOfWhoAsked()
    {
        var item = new ReminderDto(ReminderType.Nudged, 2026, 9, 1, new DateOnly(2026, 10, 5), "Benjamin Bašić", DateTime.UtcNow);
        var (title, message) = EmployeeFormat.Reminder(item);
        StringAssert.Contains(title, "Benjamin Bašić");
        StringAssert.Contains(message, "Septembar 2026.");
        StringAssert.Contains(message, "05.10.2026.");

        var first = CompanyTimesheet.Desktop.Background.EmployeeReminders.Signature(new[] { item });
        var later = CompanyTimesheet.Desktop.Background.EmployeeReminders.Signature(new[] { item with { SentUtc = item.SentUtc!.Value.AddHours(5) } });
        Assert.AreNotEqual(first, later, "a new request is a new notification, even for the same month");
    }

    // ---- signing in with a user name and password -----------------------------------------------------------------------------------

    [TestMethod]
    public async Task WithoutASession_TheSignInFormIsShown_AndSigningInLoadsTheMonth()
    {
        var (vm, client, _, _, shell) = CreateWith(out var session, out var tokens);
        session.PasswordSignIn = true;
        client.GetSheet = (y, m) => session.HasToken ? Task.FromResult(Sheets.Make(y, m)) : throw new ApiException(HttpStatusCode.Unauthorized, "unauthorized", "no session");

        await vm.LoadCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.NeedsSignIn);
        Assert.IsTrue(vm.ShowLoginForm);
        Assert.IsFalse(vm.IsConnected);
        Assert.AreEqual("", vm.ErrorText, "no error: the first thing is the form");

        vm.LoginUsername = "amra";
        vm.LoginPassword = "Start-password-1";
        vm.LoginRemember = true;
        await vm.SignInCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.NeedsSignIn);
        Assert.IsTrue(vm.IsConnected, "the month is loaded after the sign-in");
        Assert.AreEqual("token-1", tokens.Token, "remember me keeps the token");
        Assert.AreEqual("", vm.LoginPassword, "the password does not stay in memory longer than needed");
    }

    [TestMethod]
    public async Task WithoutRememberMe_NothingIsKept()
    {
        var (vm, client, _, _, shell) = CreateWith(out var session, out var tokens);
        session.PasswordSignIn = true;
        tokens.Token = "an old one";
        vm.LoginUsername = "amra";
        vm.LoginPassword = "Start-password-1";

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.IsNull(tokens.Token);
    }

    [TestMethod]
    public async Task AWrongPassword_IsExplainedInBosnian_AndALockToo()
    {
        var (vm, _, _, _, _) = CreateWith(out var session, out _);
        session.PasswordSignIn = true;
        vm.LoginUsername = "amra";
        vm.LoginPassword = "wrong";

        session.RefuseWith = "invalid-credentials";
        await vm.SignInCommand.ExecuteAsync(null);
        Assert.AreEqual("Pogrešno korisničko ime ili lozinka.", vm.LoginError);

        session.RefuseWith = "locked";
        await vm.SignInCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.LoginError, "zaključan");

        vm.LoginUsername = "";
        await vm.SignInCommand.ExecuteAsync(null);
        Assert.AreEqual("Unesite korisničko ime i lozinku.", vm.LoginError);
    }

    [TestMethod]
    public async Task APasswordTheAdministratorSet_MustBeChangedBeforeTheMonthOpens()
    {
        var (vm, client, _, _, shell) = CreateWith(out var session, out var tokens);
        session.PasswordSignIn = true;
        session.MustChange = true;
        vm.LoginUsername = "amra";
        vm.LoginPassword = "Start-password-1";
        vm.LoginRemember = true;

        await vm.SignInCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.MustChangePassword);
        Assert.IsFalse(vm.ShowLoginForm);
        Assert.IsFalse(vm.IsConnected);
        Assert.IsNull(tokens.Token, "nothing is kept before the password is changed");

        vm.NewPassword = "short";
        vm.NewPasswordAgain = "short";
        await vm.ChangePasswordCommand.ExecuteAsync(null);
        Assert.AreEqual("Lozinka mora imati najmanje 10 znakova.", vm.LoginError);

        vm.NewPassword = "My-own-password-1";
        vm.NewPasswordAgain = "different-one-123";
        await vm.ChangePasswordCommand.ExecuteAsync(null);
        Assert.AreEqual("Lozinke se ne podudaraju.", vm.LoginError);

        vm.NewPasswordAgain = "My-own-password-1";
        await vm.ChangePasswordCommand.ExecuteAsync(null);
        CollectionAssert.Contains(session.Calls, "change Start-password-1 -> My-own-password-1");
        Assert.IsFalse(vm.NeedsSignIn);
        Assert.IsTrue(vm.IsConnected);
        Assert.AreEqual("token-1", tokens.Token);
    }

    [TestMethod]
    public async Task ASessionThatEnds_BringsTheSignInFormBack_WithAnExplanation()
    {
        var (vm, client, _, _, _) = CreateWith(out var session, out _);
        session.PasswordSignIn = true;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsConnected);

        client.GetSheet = (_, _) => throw new ApiException(HttpStatusCode.Unauthorized, "unauthorized", "ended");
        await vm.NextMonthCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.NeedsSignIn);
        Assert.IsFalse(vm.IsConnected);
        Assert.AreEqual("Sesija je istekla. Prijavite se ponovo.", vm.LoginError);
    }

    [TestMethod]
    public async Task WithWindowsSignIn_ARefusalIsAnErrorNotAForm()
    {
        var (vm, client, _, _, _) = CreateWith(out var session, out _);
        session.PasswordSignIn = false;
        client.GetSheet = (_, _) => throw new ApiException(HttpStatusCode.Unauthorized, "unauthorized", "no");

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.NeedsSignIn);
        Assert.IsTrue(vm.HasError);
    }

    // ---- the administrator -------------------------------------------------------------------------------------------------------------------

    private static EmployeeDto Person(string code, string name, string? supervisor = null, bool admin = false) =>
        new(code, name, true, supervisor, false, null, admin, null, null, new List<string>());

    [TestMethod]
    public async Task TheAdministratorTab_ListsEverybody_WithSupervisorAndUserName_AndSearches()
    {
        var (vm, client, _, _, _) = CreateWith(out _, out _, out var admin);
        client.IsAdmin = true;
        admin.Employees = new() { Person("Z-000", "Administrator", admin: true), Person("Z-001", "Amra Adžamija", "Z-002"), Person("Z-002", "Benjamin Bašić") };
        admin.Credentials = new() { new CredentialDto("Z-001", "amra", true, null), new CredentialDto("Z-000", "admin", false, null) };
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsAdmin);

        await vm.ShowAdminCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.InAdmin);
        Assert.IsFalse(vm.ShowSheetBody);
        Assert.IsFalse(vm.ShowMonthNav, "the list has no month");
        Assert.AreEqual(3, vm.Admin.Rows.Count);
        var amra = vm.Admin.Rows.Single(r => r.Code == "Z-001");
        Assert.AreEqual("Benjamin Bašić", amra.SupervisorText);
        Assert.AreEqual("amra (mora promijeniti lozinku)", amra.UsernameText);
        Assert.AreEqual("bez korisničkog imena", vm.Admin.Rows.Single(r => r.Code == "Z-002").UsernameText);
        StringAssert.Contains(vm.Admin.SummaryText, "Aktivnih bez korisničkog imena: 1");

        vm.Admin.Filter = "benj";
        CollectionAssert.AreEqual(new[] { "Z-002" }, vm.Admin.Rows.Select(r => r.Code).ToArray());
        vm.Admin.Filter = "AMRA";
        CollectionAssert.AreEqual(new[] { "Z-001" }, vm.Admin.Rows.Select(r => r.Code).ToArray(), "also by user name, without regard to case");
    }

    [TestMethod]
    public async Task ChangingAnEmployee_SendsTheChanges_AndTheNewPassword_ThenReloads()
    {
        var (vm, client, dialogs, _, _) = CreateWith(out _, out _, out var admin);
        client.IsAdmin = true;
        admin.Employees = new() { Person("Z-001", "Amra Adžamija"), Person("Z-002", "Benjamin Bašić") };
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ShowAdminCommand.ExecuteAsync(null);
        admin.Calls.Clear();

        dialogs.AdminEdit = new AdminEditResult(
            new UpdateEmployeeRequest("Amra A.", true, "Z-002", null, "", false, null, null),
            new SetCredentialsRequest("amra", "Start-password-1", true), RemoveCredentials: false);
        await vm.EditEmployeeCommand.ExecuteAsync(vm.Admin.Rows[0]);

        CollectionAssert.AreEqual(new[] { "update Z-001 Amra A. supervisor=Z-002", "set Z-001 amra True", "employees", "credentials", "codes" }, admin.Calls);
        Assert.AreEqual("Sačuvano: Amra Adžamija.", vm.InfoText);
    }

    [TestMethod]
    public async Task ARefusedChange_IsExplainedInBosnian_AndANoDialogChangesNothing()
    {
        var (vm, client, dialogs, _, _) = CreateWith(out _, out _, out var admin);
        client.IsAdmin = true;
        admin.Employees = new() { Person("Z-001", "Amra Adžamija") };
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ShowAdminCommand.ExecuteAsync(null);
        admin.Calls.Clear();

        dialogs.AdminEdit = null;
        await vm.EditEmployeeCommand.ExecuteAsync(vm.Admin.Rows[0]);
        CollectionAssert.AreEqual(new string[0], admin.Calls);

        dialogs.AdminEdit = new AdminEditResult(new UpdateEmployeeRequest("Amra", true, "Z-009", null, "", false, null, null), null, false);
        admin.RefuseWith = "chart-cycle";
        await vm.EditEmployeeCommand.ExecuteAsync(vm.Admin.Rows[0]);
        StringAssert.Contains(vm.ErrorText, "krug u hijerarhiji");
        Assert.IsFalse(vm.IsBusy);

        admin.RefuseWith = "last-admin";
        dialogs.AdminEdit = new AdminEditResult(new UpdateEmployeeRequest(null, null, null, null, null, null, null, null), null, RemoveCredentials: true);
        await vm.EditEmployeeCommand.ExecuteAsync(vm.Admin.Rows[0]);
        StringAssert.Contains(vm.ErrorText, "posljednji administrator");
    }

    [TestMethod]
    public async Task TheImport_ShowsWhatWillHappenFirst_AndSkipsBadRows()
    {
        var (vm, client, dialogs, _, _) = CreateWith(out _, out _, out var admin);
        client.IsAdmin = true;
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ShowAdminCommand.ExecuteAsync(null);

        dialogs.Csv = null;
        await vm.ImportEmployeesCommand.ExecuteAsync(null);
        Assert.IsFalse(admin.Calls.Contains("preview"), "a cancelled file picker does nothing");

        dialogs.Csv = "Br.,Ime i prezime\nZ-001,Amra";
        admin.Preview = new ImportPreviewDto(new List<ImportRowDto>
        {
            new(2, "Z-001", "Amra", new List<string>(), new List<string>()),
            new(3, "", "Bez šifre", new List<string> { "greška" }, new List<string>())
        }, 1, 0);
        await vm.ImportEmployeesCommand.ExecuteAsync(null);

        StringAssert.Contains(dialogs.Asked.Last(), "Uvesti 1 od 2 redova");
        CollectionAssert.Contains(admin.Calls, "import");
        Assert.AreEqual("Dodano: 2, ažurirano: 1, preskočeno: 0.", vm.InfoText);

        admin.Calls.Clear();
        dialogs.Answer = false;
        await vm.ImportEmployeesCommand.ExecuteAsync(null);
        Assert.IsFalse(admin.Calls.Contains("import"), "no means no");
    }

    [TestMethod]
    public async Task TheAuditCheck_SaysWhetherTheLogIsIntact()
    {
        var (vm, client, _, _, _) = CreateWith(out _, out _, out var admin);
        client.IsAdmin = true;
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ShowAdminCommand.ExecuteAsync(null);

        await vm.VerifyAuditCommand.ExecuteAsync(null);
        Assert.AreEqual("Revizijski dnevnik je u redu (120 zapisa).", vm.InfoText);

        admin.AuditIntact = false;
        await vm.VerifyAuditCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.ErrorText, "77");
    }

    [TestMethod]
    public async Task AnAdministrator_GetsOnlyTheAdministration_AndNoTimesheetIsLoadedForThem()
    {
        var (vm, client, _, _, _) = CreateWith(out _, out _, out var admin);
        client.IsAdmin = true;
        admin.Employees = new() { Person("Z-000", "Administrator", admin: true), Person("Z-001", "Amra Adžamija", "Z-002"), Person("Z-002", "Benjamin Bašić") };

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsConnected);
        Assert.IsTrue(vm.IsAdmin);
        Assert.IsTrue(vm.InAdmin, "the administration is all there is");
        Assert.AreEqual(3, vm.Admin.Rows.Count);
        Assert.IsFalse(client.Calls.Any(c => c.StartsWith("sheet") || c == "pending" || c == "reminders"), "nothing of a timesheet is asked for: " + string.Join(", ", client.Calls));
        Assert.IsFalse(vm.Reminders.HasAny);
    }

    [TestMethod]
    public async Task AddingAPerson_CreatesThem_ThenGivesTheUserName_AndReloads()
    {
        var (vm, client, dialogs, _, _) = CreateWith(out _, out _, out var admin);
        client.IsAdmin = true;
        admin.Employees = new() { Person("Z-002", "Benjamin Bašić") };
        await vm.LoadCommand.ExecuteAsync(null);
        admin.Calls.Clear();

        dialogs.AdminEdit = new AdminEditResult(new UpdateEmployeeRequest(null, null, null, null, null, null, null, null),
            new SetCredentialsRequest("nova", "Start-password-1", true), RemoveCredentials: false, new CreateEmployeeRequest("", "Nova Osoba", "Z-002"));
        await vm.AddEmployeeCommand.ExecuteAsync(null);

        StringAssert.Contains(dialogs.Asked.Last(), "Nova osoba");
        CollectionAssert.AreEqual(new[] { "create  Nova Osoba supervisor=Z-002", "set Z-007 nova True", "employees", "credentials", "codes" }, admin.Calls);
        Assert.AreEqual("Dodano: Nova Osoba (Z-007).", vm.InfoText);
        Assert.IsTrue(vm.Admin.Rows.Any(r => r.Code == "Z-007"));
    }

    [TestMethod]
    public async Task AddingAPerson_WhoseUserNameIsRefused_StillShowsThem_AndSaysWhatIsMissing()
    {
        var (vm, client, dialogs, _, _) = CreateWith(out _, out _, out var admin);
        client.IsAdmin = true;
        await vm.LoadCommand.ExecuteAsync(null);

        admin.RefuseCredentialsWith = "username-taken";
        dialogs.AdminEdit = new AdminEditResult(new UpdateEmployeeRequest(null, null, null, null, null, null, null, null),
            new SetCredentialsRequest("amra", "Start-password-1", true), RemoveCredentials: false, new CreateEmployeeRequest("", "Nova Osoba"));
        await vm.AddEmployeeCommand.ExecuteAsync(null);

        StringAssert.Contains(vm.ErrorText, "Z-007");
        StringAssert.Contains(vm.ErrorText, "već ima to korisničko ime");
        Assert.IsTrue(vm.Admin.Rows.Any(r => r.Code == "Z-007"), "the person exists now, so the list shows them");
        Assert.IsFalse(vm.IsBusy);
    }

    [TestMethod]
    public async Task AddingAPerson_ThatTheServerRefuses_IsExplained_AndACancelledDialogAddsNothing()
    {
        var (vm, client, dialogs, _, _) = CreateWith(out _, out _, out var admin);
        client.IsAdmin = true;
        await vm.LoadCommand.ExecuteAsync(null);
        admin.Calls.Clear();

        dialogs.AdminEdit = null;
        await vm.AddEmployeeCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new string[0], admin.Calls);

        admin.RefuseWith = "code-taken";
        dialogs.AdminEdit = new AdminEditResult(new UpdateEmployeeRequest(null, null, null, null, null, null, null, null), null, false, new CreateEmployeeRequest("Z-001", "Dupli"));
        await vm.AddEmployeeCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.ErrorText, "šifra već pripada");
    }

    [TestMethod]
    public void TheDialogOfANewPerson_AsksForTheNameFirst_AndBuildsTheRequest()
    {
        var people = new List<EmployeeDto> { Person("Z-002", "Benjamin Bašić") };
        var dialog = new AdminEmployeeViewModel(null, people);

        Assert.IsTrue(dialog.IsNew);
        Assert.IsFalse(dialog.IsExisting);
        Assert.AreEqual("Nova osoba", dialog.Title);

        dialog.SaveCommand.Execute(null);
        Assert.IsNull(dialog.Result, "without a name nothing is saved");
        StringAssert.Contains(dialog.Error, "Ime");

        dialog.Name = "Nova Osoba";
        dialog.Username = "nova";
        dialog.SaveCommand.Execute(null);
        StringAssert.Contains(dialog.Error, "prvu lozinku", "a new user name needs its first password");

        dialog.NewPassword = "Start-password-1";
        dialog.Supervisor = dialog.People.First(p => p.Code == "Z-002");
        dialog.Email = "nova@firma.ba";
        dialog.SaveCommand.Execute(null);

        Assert.IsNotNull(dialog.Result?.Create);
        Assert.AreEqual("Nova Osoba", dialog.Result!.Create!.Name);
        Assert.AreEqual("Z-002", dialog.Result.Create.SupervisorCode);
        Assert.AreEqual("nova@firma.ba", dialog.Result.Create.Email);
        Assert.AreEqual("nova", dialog.Result.SetCredentials!.Username);
    }

    [TestMethod]
    public async Task TheChartOfTheHierarchy_PutsTheBossOnTop_AndTheirPeopleBelow()
    {
        var (vm, client, _, _, _) = CreateWith(out _, out _, out var admin);
        client.IsAdmin = true;
        admin.Employees = new()
        {
            Person("Z-000", "Administrator", admin: true),
            Person("Z-003", "Selma Mujkić"),
            Person("Z-002", "Tarik Begić", "Z-003"),
            Person("Z-006", "Nermin Čolić", "Z-003"),
            Person("Z-001", "Lejla Hadžić", "Z-002"),
            Person("Z-004", "Haris Delić", "Z-002"),
            Person("Z-005", "Maja Perić", "Z-002"),
        };
        await vm.LoadCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "Administrator", "Selma Mujkić" }, vm.Admin.Chart.Select(n => n.Name).ToArray(), "everybody with nobody above is a top");
        var selma = vm.Admin.Chart.Single(n => n.Code == "Z-003");
        CollectionAssert.AreEqual(new[] { "Nermin Čolić", "Tarik Begić" }, selma.Children.Select(n => n.Name).ToArray());
        Assert.AreEqual(5, selma.Descendants);
        Assert.AreEqual("Z-003 · podređenih: 5", selma.Caption);

        var tarik = selma.Children.Single(n => n.Code == "Z-002");
        Assert.AreEqual(3, tarik.Children.Count);
        Assert.AreEqual(0, tarik.Children[0].Descendants);
        Assert.AreEqual("Z-004", tarik.Children[0].Caption, "by name: Haris, Lejla, Maja");

        // The lines: the first box of a row has no line to its left, the last none to its right
        Assert.IsTrue(selma.Children[0].IsFirst);
        Assert.IsFalse(selma.Children[0].IsLast);
        Assert.IsTrue(selma.Children[1].IsLast);
        Assert.IsTrue(tarik.HasParent);
        Assert.IsFalse(selma.HasParent);
        Assert.IsTrue(tarik.ShowChildren, "a small company is shown open");
    }

    [TestMethod]
    public async Task ABoxClosedInTheChart_StaysClosed_AfterARefresh_AndAnUnknownSupervisorMakesATop()
    {
        var (vm, client, _, _, _) = CreateWith(out _, out _, out var admin);
        client.IsAdmin = true;
        admin.Employees = new() { Person("Z-002", "Tarik Begić"), Person("Z-001", "Lejla Hadžić", "Z-002"), Person("Z-009", "Sama Samić", "Z-404") };
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.AreEqual(2, vm.Admin.Chart.Count, "a supervisor who is not in the list leaves the person at the top");

        vm.Admin.Chart.Single(n => n.Code == "Z-002").ToggleCommand.Execute(null);
        Assert.IsFalse(vm.Admin.Chart.Single(n => n.Code == "Z-002").ShowChildren);

        await vm.ShowAdminCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.Admin.Chart.Single(n => n.Code == "Z-002").ShowChildren, "the choice survives the refresh");
    }

    [TestMethod]
    public async Task TheTableAndTheChart_AreTwoViewsOfTheSamePeople_AndTheTableIsTheDefault()
    {
        var (vm, client, _, _, _) = CreateWith(out _, out _, out var admin);
        client.IsAdmin = true;
        admin.Employees = new() { Person("Z-001", "Amra Adžamija") };
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.Admin.ShowList);
        vm.Admin.ShowChartViewCommand.Execute(null);
        Assert.IsTrue(vm.Admin.ShowChart);
        Assert.IsFalse(vm.Admin.ShowList);
        vm.Admin.ShowListViewCommand.Execute(null);
        Assert.IsTrue(vm.Admin.ShowList);
    }

    [TestMethod]
    public async Task TheSignInForm_WaitsWhileASavedSignInIsTried_AndGoesAwayOnceSignedIn()
    {
        var withToken = new EmployeeViewModel(new FakeClient(), new FakeSession(), new FakeAdmin(), new FakeTokens { Token = "saved" }, new FakeDialogs(), new FakeSaver(),
            new FakeShell(), new FakeUpdates(), new FakeServer(), NullLogger<EmployeeViewModel>.Instance);
        Assert.IsTrue(withToken.IsRestoring);
        Assert.IsFalse(withToken.ShowSignInForm, "no flash of the form while the saved sign-in is being tried");

        await withToken.LoadCommand.ExecuteAsync(null);
        Assert.IsFalse(withToken.IsRestoring);
        Assert.IsFalse(withToken.ShowSignInForm, "signed in: the form has gone");

        var (fresh, _, _, _, _) = Create();
        Assert.IsFalse(fresh.IsRestoring);
        Assert.IsTrue(fresh.ShowSignInForm, "nothing saved: the form is there at once, even before the server has answered");
    }

    // ---- deputies ---------------------------------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task TheDeputiesPanel_ListsBothDirections_AndChoosingADeputySendsThePeriod()
    {
        var (vm, client, _, _, _) = Create();
        client.IsApprover = true;
        client.Deputies = new DeputiesDto(
            new List<DeputyDto> { new(5, "Z-001", "Amra", "Z-004", "Damir Dizdarević", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 15)) },
            new List<DeputyDto> { new(6, "Z-002", "Benjamin Bašić", "Z-001", "Amra", new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 25)) });
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.ShowApprovalsCommand.ExecuteAsync(null);

        Assert.AreEqual("Zamjenjuje vas: Damir Dizdarević", vm.Deputies.Mine.Single().Title);
        Assert.AreEqual("01.10.2026. – 15.10.2026.", vm.Deputies.Mine.Single().Period);
        Assert.IsTrue(vm.Deputies.Mine.Single().CanRemove);
        Assert.AreEqual("Zamjenjujete: Benjamin Bašić", vm.Deputies.ForOthers.Single().Title);
        Assert.IsFalse(vm.Deputies.ForOthers.Single().CanRemove, "a deputy cannot cancel the period");
        CollectionAssert.AreEqual(new[] { "Damir Dizdarević", "Dario Kovačević" }, vm.Deputies.Candidates.Select(c => c.Label).ToArray());

        vm.Deputies.SelectedCandidate = vm.Deputies.Candidates[0];
        vm.Deputies.From = new DateTime(2026, 11, 2);
        vm.Deputies.To = new DateTime(2026, 11, 9);
        await vm.AddDeputyCommand.ExecuteAsync(null);
        CollectionAssert.Contains(client.Calls, "add-deputy Z-004 2026-11-02..2026-11-09");
        Assert.AreEqual("Zamjena je postavljena.", vm.InfoText);

        await vm.RemoveDeputyCommand.ExecuteAsync(vm.Deputies.Mine.Single());
        CollectionAssert.Contains(client.Calls, "remove-deputy 5");
        client.Calls.Clear();
        await vm.RemoveDeputyCommand.ExecuteAsync(vm.Deputies.ForOthers.Single());
        Assert.AreEqual(0, client.Calls.Count(c => c.StartsWith("remove-deputy")), "not removable");
    }

    [TestMethod]
    public async Task ADeputyForm_ThatIsIncomplete_ExplainsWhat_AndAServerRefusalIsInBosnian()
    {
        var (vm, client, _, _, _) = Create();
        client.IsApprover = true;
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ShowApprovalsCommand.ExecuteAsync(null);

        await vm.AddDeputyCommand.ExecuteAsync(null);
        Assert.AreEqual("Izaberite zamjenu.", vm.ErrorText);

        vm.Deputies.SelectedCandidate = vm.Deputies.Candidates[0];
        vm.Deputies.From = new DateTime(2026, 11, 9);
        vm.Deputies.To = new DateTime(2026, 11, 2);
        await vm.AddDeputyCommand.ExecuteAsync(null);
        Assert.AreEqual("Kraj perioda je prije početka.", vm.ErrorText);

        vm.Deputies.To = new DateTime(2026, 11, 12);
        client.DeputyRefusal = "overlap";
        await vm.AddDeputyCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.ErrorText, "dio ovog perioda");
    }

    [TestMethod]
    public async Task AWaitingTimesheetOfAnAbsentApprover_SaysItIsAsADeputy()
    {
        var row = new PendingRow(new PendingApprovalDto("Z-001", "Amra Adžamija", 2026, 9, 1, 0, DateTime.UtcNow, 0, 0, "Benjamin Bašić"));
        StringAssert.Contains(row.Detail, "kao zamjena za Benjamin Bašić");
        Assert.IsFalse(new PendingRow(new PendingApprovalDto("Z-001", "Amra", 2026, 9, 1, 0, DateTime.UtcNow, 0, 0)).Detail.Contains("zamjena"));
        await Task.CompletedTask;
    }

    // ---- older versions ---------------------------------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task AnOlderVersion_IsShownReadOnly_WithABannerBackToTheCurrentState()
    {
        var (vm, client, _, _, _) = Create();
        client.GetSheet = (y, m) => Task.FromResult(Sheets.Make(y, m, SheetStatus.Returned, editable: true, canSubmit: true) with
        {
            Workflow = Sheets.Make(y, m, SheetStatus.Returned, canSubmit: true).Workflow! with
            {
                Revisions = new List<RevisionDto> { new(1, new DateTime(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc)), new(2, new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc)) }
            }
        });
        await vm.LoadCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { 2, 1 }, vm.Sheet.Revisions.Select(r => r.Number).ToArray(), "the newest first");
        Assert.IsTrue(vm.CanSubmit);

        await vm.OpenRevisionCommand.ExecuteAsync(vm.Sheet.Revisions.Last());

        CollectionAssert.Contains(client.Calls, "revision 1 2026-9");
        Assert.IsTrue(vm.IsViewingRevision);
        Assert.IsFalse(vm.IsEditable);
        Assert.IsFalse(vm.CanSubmit, "an older version is only looked at");
        StringAssert.Contains(vm.RevisionBanner, "verzija 1");

        await vm.CloseRevisionCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.IsViewingRevision);
        Assert.IsTrue(vm.CanSubmit, "back to the current state");
    }

    [TestMethod]
    public async Task ANeighbouringMonth_LeavesTheOlderVersion()
    {
        var (vm, client, _, _, _) = Create();
        client.GetSheet = (y, m) => Task.FromResult(Sheets.Make(y, m) with
        {
            Workflow = Sheets.Make(y, m).Workflow! with { Revisions = new List<RevisionDto> { new(1, DateTime.UtcNow) } }
        });
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.OpenRevisionCommand.ExecuteAsync(vm.Sheet.Revisions.Single());
        Assert.IsTrue(vm.IsViewingRevision);

        await vm.NextMonthCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsViewingRevision);
    }

    [TestMethod]
    public async Task AnOlderVersionOfSomebodyElsesSheet_IsOpenedThroughTheReview()
    {
        var (vm, client, _, _, _) = Create();
        client.IsApprover = true;
        client.Pending.Add(new PendingApprovalDto("Z-004", "Dario Kovačević", 2026, 9, 1, 0, DateTime.UtcNow, 0, 0));
        client.GetReview = (c, y, m) => Task.FromResult(Sheets.Make(y, m, SheetStatus.Submitted, editable: false, owner: c, canAct: true) with
        {
            Workflow = Sheets.Make(y, m, SheetStatus.Submitted, owner: c, canAct: true).Workflow! with { Revisions = new List<RevisionDto> { new(1, DateTime.UtcNow) } }
        });
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.OpenPendingCommand.ExecuteAsync(vm.PendingApprovals.Items.Single());

        await vm.OpenRevisionCommand.ExecuteAsync(vm.Sheet.Revisions.Single());

        CollectionAssert.Contains(client.Calls, "review-revision Z-004 1 2026-9");
        Assert.IsFalse(vm.CanAct, "no decision on an old version");
    }

    [TestMethod]
    public async Task SigningOut_EndsTheSession_ForgetsTheToken_AndShowsTheSignInFormAgain()
    {
        var (vm, _, _, _, _) = CreateWith(out var session, out var tokens);
        session.PasswordSignIn = true;
        tokens.Token = "remembered";
        vm.LoginUsername = "amra";
        vm.LoginRemember = true;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsConnected);

        await vm.LogoutCommand.ExecuteAsync(null);

        CollectionAssert.Contains(session.Calls, "logout");
        Assert.IsNull(tokens.Token);
        Assert.IsTrue(vm.NeedsSignIn, "there is no other screen to go back to: the form of this app");
        Assert.IsFalse(vm.IsConnected);
        Assert.AreEqual("", vm.LoginUsername, "the next person starts with an empty form");
        Assert.IsFalse(vm.LoginRemember);
    }

    [TestMethod]
    public async Task TheServerAddressTypedOnTheSignInScreen_IsUsedAndRemembered()
    {
        var (vm, _, _, _, _) = CreateWith(out var session, out _, out _, out _, out var server);
        session.PasswordSignIn = true;
        Assert.AreEqual("http://localhost:5090", vm.ServerAddress, "a test on one PC needs no setting");

        vm.ServerAddress = "https://demo.example.com/";
        vm.LoginUsername = "amra";
        vm.LoginPassword = "Start-password-1";
        await vm.SignInCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "https://demo.example.com/" }, session.Servers);
        Assert.AreEqual("https://demo.example.com/", server.Address);
        Assert.IsFalse(vm.NeedsSignIn);
    }

    [TestMethod]
    public async Task AnUnusableServerAddress_IsExplained_AndNobodyIsSignedIn()
    {
        var (vm, _, _, _, _) = CreateWith(out var session, out _, out _, out _, out var server);
        session.PasswordSignIn = true;
        vm.LoginUsername = "amra";
        vm.LoginPassword = "Start-password-1";

        vm.ServerAddress = "ovo nije adresa";
        session.RefuseServer = new ArgumentException("bad");
        await vm.SignInCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.LoginError, "nije ispravna");

        vm.ServerAddress = "http://neki-drugi-racunar:5090";
        session.RefuseServer = new InvalidOperationException("http");
        await vm.SignInCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.LoginError, "https://");

        Assert.AreEqual("http://localhost:5090", server.Address, "a refused address is not remembered");
        Assert.IsFalse(session.Calls.Any(c => c.StartsWith("login")), "no sign-in is tried against an address that cannot be used");
    }

    [TestMethod]
    public async Task CheckingForUpdates_SaysWhetherThereIsNothingNew_OrWhatWentWrong()
    {
        var (vm, _, _, _, _) = CreateWith(out _, out _, out _, out var updates);

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.AreEqual(1, updates.Checks);
        StringAssert.Contains(vm.InfoText, "najnoviju verziju");
        Assert.AreEqual("", vm.ErrorText);

        updates.Outcome = UpdateCheckOutcome.Failed("server nije dostupan");
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.AreEqual("Provjera ažuriranja nije uspjela: server nije dostupan", vm.ErrorText);
        Assert.IsFalse(vm.IsBusy);

        updates.Outcome = UpdateCheckOutcome.Available("0.2.0");
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.AreEqual("", vm.ErrorText, "an available version is offered in the update window, not as an error");
        Assert.AreEqual("", vm.InfoText);
    }

    [TestMethod]
    public async Task AfterSigningOut_NothingOfThePreviousPersonIsLeftOnTheScreen()
    {
        var (vm, client, _, _, _) = CreateWith(out var session, out _, out var admin);
        session.PasswordSignIn = true;
        client.IsApprover = true;
        client.Pending.Add(new PendingApprovalDto("Z-004", "Dario Kovačević", 2026, 9, 1, 0, DateTime.UtcNow, 0, 1));
        client.Reminders.Add(new ReminderDto(ReminderType.SubmitOverdue, 2026, 8, 1, new DateOnly(2026, 8, 25)));
        client.Team = new() { Member("Z-004", "Dario Kovačević", SheetStatus.Draft) };
        admin.Employees = new() { Person("Z-001", "Amra Adžamija") };
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ShowAdminCommand.ExecuteAsync(null);
        Assert.AreEqual(1, vm.Admin.Rows.Count);
        vm.IsAdmin = true;
        await vm.ShowTeamCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.Reminders.HasAny);
        Assert.AreNotEqual("", vm.WhoText);

        await vm.LogoutCommand.ExecuteAsync(null);

        Assert.AreEqual("", vm.WhoText);
        Assert.IsFalse(vm.Reminders.HasAny);
        Assert.AreEqual(0, vm.PendingApprovals.Items.Count);
        Assert.AreEqual(0, vm.Team.Rows.Count);
        Assert.AreEqual(0, vm.Sheet.Days.Count);
        Assert.IsFalse(vm.IsApprover);
        Assert.IsFalse(vm.IsAdmin);
        Assert.IsFalse(vm.InTeam);
        Assert.AreEqual("", vm.StatusText);
    }

    // ---- history tab, approvals only for approvers ------------------------------------------------------------------------------

    [TestMethod]
    public async Task HistoryTab_FillsTheWindowInPlaceOfTheDays_AndTheOtherTabsLeaveIt()
    {
        var (vm, client, _, _, _) = Create();
        client.IsApprover = true;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.ShowDaysBody);
        Assert.IsFalse(vm.ShowHistoryBody);
        Assert.IsTrue(vm.InSheet);

        await vm.ShowHistoryCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.InHistory);
        Assert.IsTrue(vm.ShowHistoryBody);
        Assert.IsFalse(vm.ShowDaysBody, "the days and the side column give way to the history");
        Assert.IsFalse(vm.ShowMineToolbar, "no entry buttons in the history");
        Assert.IsFalse(vm.InSheet);
        Assert.IsTrue(vm.ShowMonthNav, "the month can still be chosen");

        await vm.ShowApprovalsCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.InHistory);
        Assert.IsFalse(vm.ShowHistoryBody);

        await vm.ShowHistoryCommand.ExecuteAsync(null);
        await vm.ShowMineCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.InHistory);
        Assert.IsTrue(vm.ShowDaysBody);
        Assert.IsTrue(vm.ShowMineToolbar);
    }

    [TestMethod]
    public async Task APersonWithNobodyBelowThem_IsNotAnApprover_SoThereIsNoApprovalsTab()
    {
        var (vm, client, _, _, _) = Create();
        client.IsApprover = false;

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsApprover, "the approvals tab is bound to this");
        Assert.IsTrue(vm.ShowDaysBody);
    }

    // ---- look of the program: theme, palette, side bar ------------------------------------------------------------------------------

    [TestInitialize]
    public void KeepChoicesOutOfTheProfile()
    {
        EmployeePreferences.FolderOverride = Path.Combine(Path.GetTempPath(), "CompanyTimesheetTests-" + Guid.NewGuid().ToString("N"));
        TimeCodes.Reset();
        TimeCodeNames.Reset();
    }

    [TestCleanup]
    public void ForgetTheChoices()
    {
        TimeCodes.Reset();
        TimeCodeNames.Reset();
        try { if (EmployeePreferences.FolderOverride != null) Directory.Delete(EmployeePreferences.FolderOverride, true); } catch (IOException) { }
        EmployeePreferences.FolderOverride = null;
    }

    [TestMethod]
    public void TheLookStartsLightWithTheDefaultPalette_AndEveryPaletteIsOffered()
    {
        var (vm, _, _, _, _) = Create();

        Assert.AreEqual("light", vm.ThemeMode);
        Assert.IsTrue(vm.IsModeLight);
        Assert.AreEqual("teal", vm.Palette);
        CollectionAssert.AreEqual(new[] { "teal", "logosoft", "blue", "graphite" }, vm.PaletteOptions.Select(p => p.Key).ToArray());
        Assert.IsTrue(vm.PaletteOptions.Single(p => p.Key == "teal").IsSelected);
    }

    [TestMethod]
    public async Task ChoosingATheme_IsKeptOnThisComputer_AndSentToTheServer()
    {
        var (vm, client, _, _, _) = Create();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SetThemeModeCommand.Execute("dark");
        vm.SetPaletteCommand.Execute("logosoft");
        await Task.Yield();

        Assert.IsTrue(vm.IsModeDark);
        Assert.IsFalse(vm.IsModeLight);
        Assert.IsTrue(vm.PaletteOptions.Single(p => p.Key == "logosoft").IsSelected);
        Assert.IsFalse(vm.PaletteOptions.Single(p => p.Key == "teal").IsSelected);
        Assert.AreEqual("dark", EmployeePreferences.LoadThemeMode());
        Assert.AreEqual("logosoft", EmployeePreferences.LoadPalette());
        Assert.AreEqual(2, client.Preferences.Count);
        Assert.AreEqual("dark", client.Preferences[0].ThemeMode);
        Assert.AreEqual("logosoft", client.Preferences[1].Palette);
    }

    [TestMethod]
    public void ANameThatIsNotAThemeOrPalette_ChangesNothing()
    {
        var (vm, _, _, _, _) = Create();

        vm.SetThemeModeCommand.Execute("neon");
        vm.SetPaletteCommand.Execute("pink");

        Assert.AreEqual("light", vm.ThemeMode);
        Assert.AreEqual("teal", vm.Palette);
    }

    [TestMethod]
    public async Task TheChoiceSavedOnTheAccount_WinsOverTheOneOfThisComputer_AndIsNotSentBack()
    {
        var (vm, client, _, _, _) = Create();
        client.ServerTheme = "dark";
        client.ServerPalette = "graphite";

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.AreEqual("dark", vm.ThemeMode);
        Assert.AreEqual("graphite", vm.Palette);
        Assert.AreEqual(0, client.Preferences.Count, "what the server told us is not saved on the server again");
    }

    [TestMethod]
    public void TheSideBarCanBeFolded_AndRemembersIt()
    {
        var (vm, _, _, _, _) = Create();
        Assert.IsTrue(vm.SidebarExpanded);
        var open = vm.SidebarWidth;

        vm.ToggleSidebarCommand.Execute(null);

        Assert.IsFalse(vm.SidebarExpanded);
        Assert.IsTrue(vm.SidebarWidth < open);
        Assert.IsFalse(EmployeePreferences.LoadSidebarExpanded());
    }

    // ---- codes of the legend ---------------------------------------------------------------------------------------------------------

    [TestMethod]
    public async Task TheCodesOfTheServer_ReplaceTheKnownOnes_SoANewCodeCanBeEntered()
    {
        var (vm, client, _, _, _) = Create();
        client.ServerCodes = TimeCodes.Defaults
            .Select(c => new TimeCodeDto(c.Code, TimeCodeNames.Bs(c.Code), TimeCodeNames.En(c.Code), c.Colour.ToString(), c.Unit.ToString(), c.InMonthlyFund, c.IsOvertime))
            .Append(new TimeCodeDto(1050, "Edukacija", "Training", "Green", "Day", true, false))
            .ToList();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.IsTrue(TimeCodes.Exists(1050));
        Assert.IsTrue(TimeCodes.Get(1050).IsAbsence);
        Assert.AreEqual("Edukacija", TimeCodeNames.Bs(1050));
    }

    [TestMethod]
    public async Task ACodeThatWasSwitchedOff_IsNotOffered_ExceptOnTheEntryThatHasIt()
    {
        var (vm, client, _, _, _) = Create();
        client.ServerCodes = TimeCodes.Defaults
            .Select(c => new TimeCodeDto(c.Code, TimeCodeNames.Bs(c.Code), TimeCodeNames.En(c.Code), c.Colour.ToString(), c.Unit.ToString(), c.InMonthlyFund, c.IsOvertime, c.Code != 1021))
            .ToList();
        await vm.LoadCommand.ExecuteAsync(null);

        var fresh = new EntryViewModel(2026, 9, _ => DayType.Working, _ => Task.FromResult(new AbsencePreviewDto(0, new List<DateOnly>(), new List<SkippedDayDto>())));
        Assert.IsFalse(fresh.Codes.Any(c => c.Code.Code == 1021), "a new entry cannot use a switched-off code");
        Assert.IsTrue(fresh.Codes.Any(c => c.Code.Code == 1020));
    }

    [TestMethod]
    public async Task TheAdministratorAddsACode_AndTheListIsShownAgain()
    {
        var (vm, client, dialogs, _, _) = Create();
        client.IsAdmin = true;
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.ShowAdminCommand.ExecuteAsync(null);
        var admin = (FakeAdmin)typeof(EmployeeViewModel).GetField("_admin", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vm)!;
        admin.Calls.Clear();

        dialogs.CodeEdit = new SaveCodeRequest(1050, "Edukacija", "", "Green", "Day", true);
        await vm.AddCodeCommand.ExecuteAsync(null);

        CollectionAssert.Contains(admin.Calls, "add-code 1050 Edukacija Green Day");
        Assert.AreEqual(1, vm.Admin.Codes.Count);
        Assert.AreEqual("Edukacija", vm.Admin.Codes[0].Name);
        StringAssert.Contains(vm.InfoText, "1050");

        dialogs.CodeEdit = null;
        admin.Calls.Clear();
        await vm.EditCodeCommand.ExecuteAsync(vm.Admin.Codes[0]);
        Assert.AreEqual(0, admin.Calls.Count, "cancelling the dialog changes nothing");
    }

    [TestMethod]
    public void TheCodeDialog_RefusesANumberOutsideTheRange_AndAnEmptyName()
    {
        var dialog = new CodeEditViewModel(null) { CodeText = "12", NameBs = "x" };
        dialog.SaveCommand.Execute(null);
        Assert.IsNull(dialog.Result);
        StringAssert.Contains(dialog.Error, "1000");

        dialog.CodeText = "1050";
        dialog.NameBs = " ";
        dialog.SaveCommand.Execute(null);
        Assert.IsNull(dialog.Result);

        dialog.NameBs = "Edukacija";
        dialog.SaveCommand.Execute(null);
        Assert.AreEqual(1050, dialog.Result!.Code);
        Assert.IsFalse(dialog.Colours.Any(c => c.Value == "Red"), "a new code cannot be overtime");
    }

    [TestMethod]
    public void ASystemCode_CanOnlyBeRenamed_AndACodeInUseKeepsItsMeaning()
    {
        var system = new CodeEditViewModel(new CodeDefinitionDto(1010, "Redovan rad", "Regular work", "Green", "Hours", true, true, true));
        Assert.IsFalse(system.CanChangeMeaning);
        Assert.IsFalse(system.CanSwitchOff);

        var used = new CodeEditViewModel(new CodeDefinitionDto(1050, "Edukacija", "Training", "Green", "Day", true, false, true));
        Assert.IsFalse(used.CanChangeMeaning);
        Assert.IsTrue(used.CanSwitchOff);

        var unused = new CodeEditViewModel(new CodeDefinitionDto(1051, "Novo", "New", "Green", "Day", true, false, false));
        Assert.IsTrue(unused.CanChangeMeaning);
    }
}
