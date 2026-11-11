namespace Company.Api.Contracts;

/// <summary>
/// Every request that changes something carries this header. A browser cannot add a custom header to a request sent from
/// another site without the server's consent, so a web page the employee visits cannot act with their Windows sign-in.
/// </summary>
public static class ApiHeaders
{
    public const string RequestedWith = "X-Requested-With";
    public const string RequestedWithValue = "CompanyTimesheet";
}

/// <summary>Who the caller is, after sign-in</summary>
public sealed record MeDto(
    string Code,
    string Name,
    string Account,
    bool IsAdmin,
    bool IsApprover,
    bool IsFinalApprover,
    string? SupervisorCode,
    IReadOnlyList<string> ApprovalChain,
    string AuthenticationMode,
    bool IsFinance = false,
    bool SheetExport = false,
    string? ThemeMode = null,
    string? Palette = null,
    string? Language = null);

public sealed record EmployeeDto(
    string Code,
    string Name,
    bool IsActive,
    string? SupervisorCode,
    bool IsFinalApprover,
    string? DesignatedApproverCode,
    bool IsAdmin,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    IReadOnlyList<string> Accounts,
    bool IsFinance = false,
    string? Email = null);

/// <summary>Changes to one employee made by the administrator. Null leaves a value as it is; an empty string clears a code.</summary>
public sealed record UpdateEmployeeRequest(
    string? Name,
    bool? IsActive,
    string? SupervisorCode,
    bool? IsFinalApprover,
    string? DesignatedApproverCode,
    bool? IsAdmin,
    DateOnly? ActiveFrom,
    DateOnly? ActiveTo,
    bool? IsFinance = null,
    bool? ClearActiveFrom = null,
    bool? ClearActiveTo = null,
    string? Email = null);

/// <summary>Sign-in with the user name and password the administrator created. Remember keeps the session for 30 days instead of 12 hours.</summary>
public sealed record LoginRequest(string Username, string Password, bool Remember = false);

public sealed record LoginResultDto(string Token, DateTime ExpiresUtc, bool MustChangePassword);

public sealed record ChangePasswordRequest(string Current, string New);

/// <summary>A new person. An empty code is chosen by the server (the next free Z-nnn).</summary>
public sealed record CreateEmployeeRequest(
    string? Code,
    string Name,
    string? SupervisorCode = null,
    string? Email = null,
    bool IsAdmin = false,
    DateOnly? ActiveFrom = null,
    DateOnly? ActiveTo = null);

/// <summary>The administrator creates or replaces an employee's user name and password. MustChange: the employee chooses their own at the first sign-in.</summary>
public sealed record SetCredentialsRequest(string Username, string Password, bool MustChange = true);

public sealed record CredentialDto(string EmployeeCode, string Username, bool MustChangePassword, DateTime? LockedUntilUtc, DateTime? ResetRequestedUtc = null);

/// <summary>The figures of the start screen of an employee: this month, the year so far, and what waits to be approved</summary>
public sealed record EmployeeOverviewDto(
    int Year,
    int Month,
    SheetStatus Status,
    int FundMinutes,
    int ExpectedFundMinutes,
    int MonthOvertimeMinutes,
    int YearWorkedMinutes,
    int YearOvertimeMinutes,
    int PastMonthsHandedIn,
    int PastMonths,
    int PendingApprovals);

/// <summary>The employee asks for a new password; only the user name is needed</summary>
public sealed record ResetRequestDto(string Username);

/// <summary>A temporary password the administrator set for an employee, and whether it was also sent to the employee's e-mail</summary>
public sealed record ResetResultDto(string TemporaryPassword, bool EmailSent, string? Email);

public sealed record CsvRequest(string Csv);

public sealed record ImportRowDto(int Line, string Code, string Name, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public sealed record ImportPreviewDto(IReadOnlyList<ImportRowDto> Rows, int ErrorCount, int WarningCount);

public sealed record ImportResultDto(int Created, int Updated, int Skipped);

public sealed record AccountRequest(string Account);

public sealed record DirectoryUserDto(string Account, string DisplayName);

public sealed record AccountMatchDto(string Account, string DisplayName, string? EmployeeCode, string Status);

/// <summary>A state holiday; <see cref="IsBuiltIn"/> is true for one the server wrote itself (not changed since), which the administrator cannot edit</summary>
public sealed record HolidayDto(DateOnly Date, string Name, bool IsBuiltIn = false);

/// <summary>What the administrator sees first (see AdminOverviewService)</summary>
public sealed record AdminOverviewDto(
    OverviewPeopleDto People,
    OverviewCodesDto Codes,
    OverviewMonthDto ThisMonth,
    OverviewMonthDto PreviousMonth,
    OverviewHolidaysDto Holidays,
    OverviewAuditDto Audit);

/// <param name="WithoutUsername">Active people who cannot sign in yet</param>
/// <param name="Locked">Accounts locked after wrong passwords</param>
/// <param name="WithoutSupervisor">Active people at the top of the chart (nobody above them)</param>
public sealed record OverviewPeopleDto(int Total, int Active, int Inactive, int WithoutUsername, int Locked, int Administrators, int WithoutSupervisor);

public sealed record OverviewCodesDto(int Total, int Active, int SwitchedOff, int Green, int Blue, int Orange, int Red);

/// <param name="Expected">People who have a timesheet in the month</param>
/// <param name="NotStarted">Expected people with no timesheet yet</param>
public sealed record OverviewMonthDto(int Year, int Month, int Expected, int NotStarted, int Draft, int Submitted, int Returned, int Approved);

/// <param name="ThisYear">State holidays entered for the current year (none means the fund of hours is wrong)</param>
public sealed record OverviewHolidaysDto(int Year, int ThisYear, int NextYear);

public sealed record OverviewAuditDto(long Events, DateTime? LastUtc, IReadOnlyList<AuditEventDto> Recent);

public sealed record TimeCodeDto(int Code, string NameBs, string NameEn, string Colour, string Unit, bool InMonthlyFund, bool IsOvertime, bool IsActive = true);

/// <summary>A code of the legend as the administrator sees it. A system code is one the rules of the program rely on (it can be renamed, not removed).</summary>
public sealed record CodeDefinitionDto(int Code, string NameBs, string NameEn, string Colour, string Unit, bool IsActive, bool IsSystem, bool InUse);

/// <summary>A new code, or the changes to one. Colour and unit only change while no sheet uses the code.</summary>
public sealed record SaveCodeRequest(int Code, string NameBs, string NameEn, string Colour, string Unit, bool IsActive);

/// <summary>How the screen of one person looks: the theme and the colour palette. Null leaves the choice as it was.</summary>
public sealed record PreferencesRequest(string? ThemeMode, string? Palette, string? Language = null);

/// <summary>The choices a person has for the look of the program, so that the server and the program agree</summary>
public static class UiPreferences
{
    public static readonly string[] ThemeModes = { "system", "light", "dark" };

    public static readonly string[] Palettes = { "teal", "logosoft", "blue", "graphite" };

    /// <summary>Bosnian and English</summary>
    public static readonly string[] Languages = { "bs", "en" };
}

public sealed record CalendarDayDto(DateOnly Date, string Weekday, DayKind Type, string? HolidayName);

/// <summary>The result of checking the chain of hashes of the audit log</summary>
public sealed record AuditVerificationDto(bool IsIntact, long Checked, long BeforeTheChain, long? FirstBrokenId, string? Problem);

public sealed record AuditEventDto(long Id, DateTime AtUtc, string Actor, string Action, string EntityType, string EntityId, string? Details);

/// <summary>For the administrator and IT: what the server sees of the caller, to test Windows or Entra sign-in</summary>
public sealed record AuthDiagnosticsDto(
    string Mode,
    string? IdentityName,
    string? AuthenticationType,
    bool IsAuthenticated,
    IReadOnlyList<string> Candidates,
    string? EmployeeCode,
    IReadOnlyList<ClaimDto> Claims);

public sealed record ClaimDto(string Type, string Value);

/// <summary>An error the desktop app can show as it is</summary>
public sealed record ApiErrorDto(string Code, string Message);

// ---- Timesheet capture ------------------------------------------------------------------------------------------------

public sealed record EntryDto(int Code, int Minutes);

public sealed record AllocationDto(DateOnly Date, int Code, int Minutes);

public sealed record DayDto(
    DateOnly Date,
    string Weekday,
    DayKind Type,
    string? HolidayName,
    IReadOnlyList<EntryDto> Entries,
    IReadOnlyList<AllocationDto> OvertimeAllocations);

public sealed record OvertimeDto(
    int Id,
    DateOnly Date,
    TimeOnly Start,
    TimeOnly End,
    string Description,
    int TotalMinutes,
    IReadOnlyList<AllocationDto> Allocations,
    bool HasRegularWindowConflict,
    int MinutesInNextMonth = 0);

/// <summary>Overtime of the previous month that goes on after midnight into this one: the part that belongs here</summary>
public sealed record CarriedOvertimeDto(DateOnly Date, TimeOnly Start, TimeOnly End, string Description, int MinutesInThisMonth);

public sealed record SummaryDto(
    IReadOnlyDictionary<int, int> MinutesByCode,
    IReadOnlyDictionary<int, int> DaysByCode,
    int FundMinutes,
    int ExpectedFundMinutes,
    int OvertimeMinutes);

public sealed record IssueDto(string RuleId, IssueSeverity Severity, DateOnly? Date, string Message);

/// <summary>One employee's month as the employee sees it while filling it in</summary>
public sealed record SheetDto(
    int Year,
    int Month,
    SheetStatus Status,
    int Revision,
    bool Editable,
    IReadOnlyList<DayDto> Days,
    IReadOnlyList<OvertimeDto> Overtime,
    SummaryDto Summary,
    IReadOnlyList<IssueDto> Issues,
    WorkflowDto? Workflow = null,
    IReadOnlyList<CarriedOvertimeDto>? CarriedIn = null);

// ---- Submission and approval ------------------------------------------------------------------------------------------

public sealed record ChainStepDto(string Code, string Name);

public sealed record MessageDto(int Index, string From, string FromName, string Text, int Revision, string? Answer);

public sealed record HistoryDto(DateTime AtUtc, HistoryKind Kind, string Actor, string ActorName, string? OnBehalfOf, int Revision, int Step, string? Text);

public sealed record RevisionDto(int Revision, DateTime SubmittedUtc);

/// <summary>Where a sheet stands in the approval chain, and what the caller may do with it</summary>
public sealed record WorkflowDto(
    string OwnerCode,
    string OwnerName,
    IReadOnlyList<ChainStepDto> Chain,
    int Step,
    string? CurrentApprover,
    string? CurrentApproverName,
    IReadOnlyList<MessageDto> Messages,
    IReadOnlyList<HistoryDto> History,
    IReadOnlyList<RevisionDto> Revisions,
    bool CanSubmit,
    bool CanAct);

public sealed record ReturnRequest(string Message);

public sealed record AnswerRequest(string Answer);

/// <summary>The administrator hands the step a timesheet is waiting at to somebody else (for example when the approver left)</summary>
public sealed record ReassignRequest(string ToCode, string Reason);

/// <summary>One thing that is due for the caller. Kind is a ReminderKind name; the app words it.</summary>
public sealed record ReminderDto(ReminderType Kind, int Year, int Month, int Count, DateOnly Deadline, string? From = null, DateTime? SentUtc = null);

// ---- The people who report to the caller -----------------------------------------------------------------------------------

/// <summary>One person below the caller. The list is in the order of a tree read from the top: a person comes right before their own reports.</summary>
public sealed record TeamMemberDto(
    string Code,
    string Name,
    string? ParentCode,
    int Depth,
    SheetStatus Status,
    string? WaitingForName,
    bool CanRemind,
    DateTime? RemindedUtc);

public sealed record ReminderSentDto(DateTime SentUtc);

// ---- The summary for the approver ---------------------------------------------------------------------------------------------

/// <summary>What there is to say of one person for the month. Note is for the screen only (the sheet is not approved yet); it is not part of the text.</summary>
public sealed record SummaryPersonDto(string Name, IReadOnlyList<string> Lines, string? Note);

public sealed record SummaryGroupDto(string? Title, IReadOnlyList<SummaryPersonDto> People);

public sealed record SummaryOvertimeDto(string Name, DateOnly Date, string Interval, string Hours, string Description);

/// <summary>The message that goes to the approver: the groups, the register of overtime, and all of it as text to copy</summary>
public sealed record TeamSummaryDto(int Year, int Month, IReadOnlyList<SummaryGroupDto> Groups, IReadOnlyList<SummaryOvertimeDto> Overtime, string Text);

public sealed record TeamStatusDto(int Year, int Month, DateOnly Deadline, IReadOnlyList<TeamMemberDto> Members);

public sealed record RemindersDto(DateOnly Today, IReadOnlyList<ReminderDto> Items);

/// <summary>A submitted timesheet waiting for the caller</summary>
public sealed record PendingApprovalDto(
    string EmployeeCode,
    string EmployeeName,
    int Year,
    int Month,
    int Revision,
    int Step,
    DateTime SubmittedUtc,
    int BlockingCount,
    int WarningCount,
    string? ActingForName = null);

// ---- Deputies ---------------------------------------------------------------------------------------------------------------

public sealed record PersonDto(string Code, string Name);

public sealed record DeputyDto(int Id, string ApproverCode, string ApproverName, string DeputyCode, string DeputyName, DateOnly From, DateOnly To);

public sealed record AddDeputyRequest(string DeputyCode, DateOnly From, DateOnly To);

/// <summary>Mine: who stands in for me. ForOthers: whose approvals I stand in for.</summary>
public sealed record DeputiesDto(IReadOnlyList<DeputyDto> Mine, IReadOnlyList<DeputyDto> ForOthers);

/// <summary>Sets the day's entry. A whole-day absence code needs no minutes; an hours code needs them.</summary>
public sealed record SetEntryRequest(int Code, int Minutes);

public sealed record AbsenceRangeRequest(int Code, DateOnly From, DateOnly To);

public sealed record SkippedDayDto(DateOnly Date, SkippedBecause Reason);

public sealed record AbsencePreviewDto(int Code, IReadOnlyList<DateOnly> Days, IReadOnlyList<SkippedDayDto> Skipped);

public sealed record OvertimeRequest(DateOnly Date, TimeOnly Start, TimeOnly End, string Description);
