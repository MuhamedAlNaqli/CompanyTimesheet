using Company.Api.Contracts;

namespace Company.Api.Client;

/// <summary>What the desktop app asks of the company server. The view models depend on this, not on HTTP.</summary>
public interface ICompanyApiClient
{
    Task<MeDto> GetMeAsync(CancellationToken ct = default);

    Task<AuthDiagnosticsDto> GetDiagnosticsAsync(CancellationToken ct = default);

    Task<List<TimeCodeDto>> GetTimeCodesAsync(CancellationToken ct = default);

    /// <summary>Saves the theme and the colour palette of the caller on the server, so they follow the person to every computer</summary>
    Task SetPreferencesAsync(PreferencesRequest request, CancellationToken ct = default);

    Task<SheetDto> GetSheetAsync(int year, int month, CancellationToken ct = default);

    Task<SheetDto> SetEntryAsync(int year, int month, DateOnly date, SetEntryRequest request, CancellationToken ct = default);

    Task<SheetDto> ClearEntryAsync(int year, int month, DateOnly date, int code, CancellationToken ct = default);

    Task<SheetDto> FillStandardDaysAsync(int year, int month, CancellationToken ct = default);

    Task<AbsencePreviewDto> PreviewAbsenceAsync(int year, int month, AbsenceRangeRequest request, CancellationToken ct = default);

    Task<SheetDto> ApplyAbsenceAsync(int year, int month, AbsenceRangeRequest request, CancellationToken ct = default);

    Task<SheetDto> AddOvertimeAsync(int year, int month, OvertimeRequest request, CancellationToken ct = default);

    Task<SheetDto> UpdateOvertimeAsync(int year, int month, int id, OvertimeRequest request, CancellationToken ct = default);

    Task<SheetDto> DeleteOvertimeAsync(int year, int month, int id, CancellationToken ct = default);

    Task<SheetDto> SubmitAsync(int year, int month, CancellationToken ct = default);

    Task<SheetDto> AnswerAsync(int year, int month, int index, string answer, CancellationToken ct = default);

    Task<SheetDto> GetRevisionAsync(int year, int month, int revision, CancellationToken ct = default);

    Task<List<PendingApprovalDto>> GetPendingAsync(CancellationToken ct = default);

    Task<SheetDto> GetReviewAsync(string code, int year, int month, CancellationToken ct = default);

    Task<SheetDto> GetReviewRevisionAsync(string code, int year, int month, int revision, CancellationToken ct = default);

    Task<SheetDto> ApproveAsync(string code, int year, int month, CancellationToken ct = default);

    Task<SheetDto> ReturnAsync(string code, int year, int month, string message, CancellationToken ct = default);

    Task<RemindersDto> GetRemindersAsync(CancellationToken ct = default);

    Task<DownloadedFile> GetPrintAsync(int year, int month, string format, CancellationToken ct = default);

    Task<DownloadedFile> GetReviewPrintAsync(string code, int year, int month, string format, CancellationToken ct = default);

    Task<DownloadedFile> GetTeamPrintAsync(int year, int month, string format, CancellationToken ct = default);

    /// <summary>The message for the approver: what there is to report for everybody below the caller, and the register of overtime</summary>
    Task<DownloadedFile> GetTeamSummaryAsync(int year, int month, string format, CancellationToken ct = default);

    /// <summary>The same message as text, for the screen and for copying into a mail</summary>
    Task<TeamSummaryDto> GetTeamSummaryTextAsync(int year, int month, CancellationToken ct = default);

    Task<TeamStatusDto> GetTeamStatusAsync(int year, int month, CancellationToken ct = default);

    /// <summary>Asks one person below the caller to submit the month; their computer shows a notification</summary>
    Task RemindAsync(string code, int year, int month, CancellationToken ct = default);

    /// <summary>Who stands in for the caller, and whose approvals the caller stands in for (periods running or to come)</summary>
    Task<DeputiesDto> GetDeputiesAsync(CancellationToken ct = default);

    /// <summary>The colleagues who can be chosen as a deputy</summary>
    Task<List<PersonDto>> GetDeputyCandidatesAsync(CancellationToken ct = default);

    Task<DeputyDto> AddDeputyAsync(AddDeputyRequest request, CancellationToken ct = default);

    Task RemoveDeputyAsync(int id, CancellationToken ct = default);

    Task<DownloadedFile> GetApprovedExportAsync(int year, int month, string format, CancellationToken ct = default);
}
