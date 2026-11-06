using System.Net;
using System.Net.Http.Json;
using Company.Api.Contracts;

namespace Company.Api.Client;

/// <summary>An answer of the API that the desktop app shows as it is. <see cref="Code"/> is the stable code (for example "locked").</summary>
public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode status, string code, string message) : base(message)
    {
        Status = status;
        Code = code;
    }

    public HttpStatusCode Status { get; }

    public string Code { get; }
}

/// <summary>A file the server produced (the printed sheet)</summary>
public sealed record DownloadedFile(string FileName, byte[] Bytes);

/// <summary>
/// The company API for the desktop app. It carries no state: sign-in is whatever the <see cref="HttpClient"/> does (for
/// Windows sign-in, the default credentials of the user).
/// </summary>
public sealed class CompanyApiClient : ICompanyApiClient, ICompanySession, ICompanyAdminClient
{
    private volatile HttpClient _http;
    private volatile string? _token;

    public CompanyApiClient(HttpClient http, bool passwordSignIn = false)
    {
        _http = http;
        PasswordSignIn = passwordSignIn;
    }

    public bool PasswordSignIn { get; }

    /// <summary>Points the client at another server (the address typed on the sign-in screen). The session of the old one ends.</summary>
    /// <exception cref="ArgumentException">The text is not an address</exception>
    /// <exception cref="InvalidOperationException">Plain http is only for this computer</exception>
    public void UseServer(string address)
    {
        if (!Uri.TryCreate((address ?? "").Trim().TrimEnd('/'), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("The server address must be like https://server.example.com", nameof(address));

        _http = PasswordSignIn ? CreatePlainHttpClient(uri) : CreateWindowsHttpClient(uri);
        _token = null;
    }

    /// <summary>
    /// An HTTP client that signs in with the Windows account of the user (Kerberos on a domain, NTLM otherwise).
    /// Plain HTTP is only accepted for the local machine; anywhere else the address must be HTTPS.
    /// </summary>
    public static HttpClient CreateWindowsHttpClient(Uri baseAddress)
    {
        EnsureSecure(baseAddress);
        var handler = new HttpClientHandler { UseDefaultCredentials = true, AllowAutoRedirect = false };
        return new HttpClient(handler) { BaseAddress = baseAddress, Timeout = TimeSpan.FromMinutes(2) };
    }

    /// <summary>An HTTP client for sign-in with a user name and password: the session token carries the identity, no Windows credentials are sent</summary>
    public static HttpClient CreatePlainHttpClient(Uri baseAddress)
    {
        EnsureSecure(baseAddress);
        return new HttpClient(new HttpClientHandler { UseDefaultCredentials = false, AllowAutoRedirect = false }) { BaseAddress = baseAddress, Timeout = TimeSpan.FromMinutes(2) };
    }

    public static void EnsureSecure(Uri address)
    {
        if (address.Scheme == Uri.UriSchemeHttps) return;
        if (address.Scheme == Uri.UriSchemeHttp && address.IsLoopback) return;
        throw new InvalidOperationException("The company server address must start with https:// (plain http is only allowed for this computer).");
    }

    // ---- Sign-in by user name and password ------------------------------------------------------------------------------

    public bool HasToken => _token != null;

    private volatile string _language = "bs";

    public void UseLanguage(string language) => _language = language == "en" ? "en" : "bs";

    public void UseToken(string? token) => _token = string.IsNullOrWhiteSpace(token) ? null : token;

    public async Task<LoginResultDto> LoginAsync(string username, string password, bool remember, CancellationToken ct = default)
    {
        UseToken(null);
        var result = await SendAsync<LoginResultDto>(HttpMethod.Post, "/api/auth/login", new LoginRequest(username, password, remember), ct);
        UseToken(result.Token);
        return result;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            if (_token != null) await SendAsync<object>(HttpMethod.Post, "/api/auth/logout", null, ct);
        }
        catch (Exception)
        {
            // The sign-out must not depend on the network: the token is dropped here whatever the server says
        }
        finally
        {
            UseToken(null);
        }
    }

    public Task ChangePasswordAsync(string current, string newPassword, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Post, "/api/auth/change-password", new ChangePasswordRequest(current, newPassword), ct);

    // ---- The administrator -------------------------------------------------------------------------------------------------

    public Task<List<EmployeeDto>> GetEmployeesAsync(CancellationToken ct = default) => GetAsync<List<EmployeeDto>>("/api/admin/employees", ct);

    public Task<EmployeeDto> CreateEmployeeAsync(CreateEmployeeRequest request, CancellationToken ct = default) =>
        SendAsync<EmployeeDto>(HttpMethod.Post, "/api/admin/employees", request, ct);

    public Task<EmployeeDto> UpdateEmployeeAsync(string code, UpdateEmployeeRequest request, CancellationToken ct = default) =>
        SendAsync<EmployeeDto>(HttpMethod.Put, $"/api/admin/employees/{Uri.EscapeDataString(code)}", request, ct);

    public Task<ImportPreviewDto> PreviewImportAsync(string csv, CancellationToken ct = default) =>
        SendAsync<ImportPreviewDto>(HttpMethod.Post, "/api/admin/employees/import/preview", new CsvRequest(csv), ct);

    public Task<ImportResultDto> ImportAsync(string csv, CancellationToken ct = default) =>
        SendAsync<ImportResultDto>(HttpMethod.Post, "/api/admin/employees/import", new CsvRequest(csv), ct);

    public Task<List<CredentialDto>> GetCredentialsAsync(CancellationToken ct = default) => GetAsync<List<CredentialDto>>("/api/admin/credentials", ct);

    public Task SetCredentialsAsync(string code, SetCredentialsRequest request, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Put, $"/api/admin/employees/{Uri.EscapeDataString(code)}/credentials", request, ct);

    public Task RemoveCredentialsAsync(string code, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Delete, $"/api/admin/employees/{Uri.EscapeDataString(code)}/credentials", null, ct);

    public Task<AuditVerificationDto> VerifyAuditAsync(CancellationToken ct = default) => GetAsync<AuditVerificationDto>("/api/admin/audit/verify", ct);

    public Task<AdminOverviewDto> GetOverviewAsync(CancellationToken ct = default) => GetAsync<AdminOverviewDto>("/api/admin/overview", ct);

    public Task<List<HolidayDto>> GetHolidaysAsync(int year, CancellationToken ct = default) => GetAsync<List<HolidayDto>>($"/api/admin/holidays/{year}", ct);

    public Task<List<HolidayDto>> GetReligiousHolidaysAsync(string code, CancellationToken ct = default) =>
        GetAsync<List<HolidayDto>>($"/api/admin/employees/{Uri.EscapeDataString(code)}/religious-holidays", ct);

    public Task SetReligiousHolidaysAsync(string code, IReadOnlyList<HolidayDto> holidays, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Put, $"/api/admin/employees/{Uri.EscapeDataString(code)}/religious-holidays", holidays, ct);

    public Task<HolidayDto> SaveHolidayAsync(HolidayDto holiday, CancellationToken ct = default) =>
        SendAsync<HolidayDto>(HttpMethod.Post, "/api/admin/holidays", holiday, ct);

    public Task DeleteHolidayAsync(DateOnly date, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Delete, $"/api/admin/holidays/{date:yyyy-MM-dd}", null, ct);

    public Task<List<AuditEventDto>> GetAuditAsync(int take, CancellationToken ct = default) => GetAsync<List<AuditEventDto>>($"/api/admin/audit?take={take}", ct);

    public Task<List<CodeDefinitionDto>> GetCodesAsync(CancellationToken ct = default) => GetAsync<List<CodeDefinitionDto>>("/api/admin/codes", ct);

    public Task<CodeDefinitionDto> AddCodeAsync(SaveCodeRequest request, CancellationToken ct = default) =>
        SendAsync<CodeDefinitionDto>(HttpMethod.Post, "/api/admin/codes", request, ct);

    public Task<CodeDefinitionDto> UpdateCodeAsync(int code, SaveCodeRequest request, CancellationToken ct = default) =>
        SendAsync<CodeDefinitionDto>(HttpMethod.Put, $"/api/admin/codes/{code}", request, ct);

    // ---- Identity and reference data ------------------------------------------------------------------------------------

    public Task<MeDto> GetMeAsync(CancellationToken ct = default) => GetAsync<MeDto>("/api/me", ct);

    public Task<AuthDiagnosticsDto> GetDiagnosticsAsync(CancellationToken ct = default) => GetAsync<AuthDiagnosticsDto>("/api/auth/diagnostics", ct);

    public Task<List<TimeCodeDto>> GetTimeCodesAsync(CancellationToken ct = default) => GetAsync<List<TimeCodeDto>>("/api/timecodes", ct);

    public Task SetPreferencesAsync(PreferencesRequest request, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Put, "/api/me/preferences", request, ct);

    // ---- My timesheet -----------------------------------------------------------------------------------------------------

    private static string Sheet(int year, int month) => $"/api/timesheet/{year}/{month}";

    public Task<SheetDto> GetSheetAsync(int year, int month, CancellationToken ct = default) => GetAsync<SheetDto>(Sheet(year, month), ct);

    public Task<SheetDto> SetEntryAsync(int year, int month, DateOnly date, SetEntryRequest request, CancellationToken ct = default) =>
        SendAsync<SheetDto>(HttpMethod.Put, $"{Sheet(year, month)}/days/{date:yyyy-MM-dd}", request, ct);

    public Task<SheetDto> ClearEntryAsync(int year, int month, DateOnly date, int code, CancellationToken ct = default) =>
        SendAsync<SheetDto>(HttpMethod.Delete, $"{Sheet(year, month)}/days/{date:yyyy-MM-dd}/{code}", null, ct);

    public Task<SheetDto> FillStandardDaysAsync(int year, int month, CancellationToken ct = default) =>
        SendAsync<SheetDto>(HttpMethod.Post, $"{Sheet(year, month)}/fill-standard-days", null, ct);

    public Task<AbsencePreviewDto> PreviewAbsenceAsync(int year, int month, AbsenceRangeRequest request, CancellationToken ct = default) =>
        SendAsync<AbsencePreviewDto>(HttpMethod.Post, $"{Sheet(year, month)}/absences/preview", request, ct);

    public Task<SheetDto> ApplyAbsenceAsync(int year, int month, AbsenceRangeRequest request, CancellationToken ct = default) =>
        SendAsync<SheetDto>(HttpMethod.Post, $"{Sheet(year, month)}/absences", request, ct);

    public Task<SheetDto> AddOvertimeAsync(int year, int month, OvertimeRequest request, CancellationToken ct = default) =>
        SendAsync<SheetDto>(HttpMethod.Post, $"{Sheet(year, month)}/overtime", request, ct);

    public Task<SheetDto> UpdateOvertimeAsync(int year, int month, int id, OvertimeRequest request, CancellationToken ct = default) =>
        SendAsync<SheetDto>(HttpMethod.Put, $"{Sheet(year, month)}/overtime/{id}", request, ct);

    public Task<SheetDto> DeleteOvertimeAsync(int year, int month, int id, CancellationToken ct = default) =>
        SendAsync<SheetDto>(HttpMethod.Delete, $"{Sheet(year, month)}/overtime/{id}", null, ct);

    // ---- Submission and approval ----------------------------------------------------------------------------------------

    public Task<SheetDto> SubmitAsync(int year, int month, CancellationToken ct = default) =>
        SendAsync<SheetDto>(HttpMethod.Post, $"{Sheet(year, month)}/submit", null, ct);

    public Task<SheetDto> AnswerAsync(int year, int month, int index, string answer, CancellationToken ct = default) =>
        SendAsync<SheetDto>(HttpMethod.Post, $"{Sheet(year, month)}/messages/{index}/answer", new AnswerRequest(answer), ct);

    public Task<SheetDto> GetRevisionAsync(int year, int month, int revision, CancellationToken ct = default) =>
        GetAsync<SheetDto>($"{Sheet(year, month)}/revisions/{revision}", ct);

    /// <summary>The timesheets that wait for the signed-in person</summary>
    public Task<List<PendingApprovalDto>> GetPendingAsync(CancellationToken ct = default) => GetAsync<List<PendingApprovalDto>>("/api/approvals", ct);

    private static string Review(string code, int year, int month) => $"/api/approvals/{Uri.EscapeDataString(code)}/{year}/{month}";

    public Task<SheetDto> GetReviewAsync(string code, int year, int month, CancellationToken ct = default) => GetAsync<SheetDto>(Review(code, year, month), ct);

    public Task<SheetDto> GetReviewRevisionAsync(string code, int year, int month, int revision, CancellationToken ct = default) =>
        GetAsync<SheetDto>($"{Review(code, year, month)}/revisions/{revision}", ct);

    public Task<SheetDto> ApproveAsync(string code, int year, int month, CancellationToken ct = default) =>
        SendAsync<SheetDto>(HttpMethod.Post, $"{Review(code, year, month)}/approve", null, ct);

    public Task<SheetDto> ReturnAsync(string code, int year, int month, string message, CancellationToken ct = default) =>
        SendAsync<SheetDto>(HttpMethod.Post, $"{Review(code, year, month)}/return", new ReturnRequest(message), ct);

    /// <summary>What is due for the signed-in person today (submit, fix a returned sheet, approve)</summary>
    public Task<RemindersDto> GetRemindersAsync(CancellationToken ct = default) => GetAsync<RemindersDto>("/api/reminders", ct);

    // ---- Printed sheet -----------------------------------------------------------------------------------------------------

    /// <summary>The caller's own month in the layout of the current sheet; format is "xlsx" or "pdf"</summary>
    public Task<DownloadedFile> GetPrintAsync(int year, int month, string format, CancellationToken ct = default) =>
        DownloadAsync($"{Sheet(year, month)}/print/{format}", ct);

    public Task<DownloadedFile> GetReviewPrintAsync(string code, int year, int month, string format, CancellationToken ct = default) =>
        DownloadAsync($"{Review(code, year, month)}/print/{format}", ct);

    /// <summary>The month of everybody the caller supervises on one printed sheet</summary>
    public Task<DownloadedFile> GetTeamPrintAsync(int year, int month, string format, CancellationToken ct = default) =>
        DownloadAsync($"/api/team/{year}/{month}/print/{format}", ct);

    public Task<DownloadedFile> GetTeamSummaryAsync(int year, int month, string format, CancellationToken ct = default) =>
        DownloadAsync($"/api/team/{year}/{month}/summary/{format}", ct);

    public Task<DeputiesDto> GetDeputiesAsync(CancellationToken ct = default) => GetAsync<DeputiesDto>("/api/deputies", ct);

    public Task<List<PersonDto>> GetDeputyCandidatesAsync(CancellationToken ct = default) => GetAsync<List<PersonDto>>("/api/deputies/candidates", ct);

    public Task<DeputyDto> AddDeputyAsync(AddDeputyRequest request, CancellationToken ct = default) =>
        SendAsync<DeputyDto>(HttpMethod.Post, "/api/deputies", request, ct);

    public Task RemoveDeputyAsync(int id, CancellationToken ct = default) =>
        SendAsync<object>(HttpMethod.Delete, $"/api/deputies/{id}", null, ct);

    public Task<TeamSummaryDto> GetTeamSummaryTextAsync(int year, int month, CancellationToken ct = default) =>
        GetAsync<TeamSummaryDto>($"/api/team/{year}/{month}/summary", ct);

    public Task<TeamStatusDto> GetTeamStatusAsync(int year, int month, CancellationToken ct = default) =>
        GetAsync<TeamStatusDto>($"/api/team/{year}/{month}/status", ct);

    public Task RemindAsync(string code, int year, int month, CancellationToken ct = default) =>
        SendAsync<ReminderSentDto>(HttpMethod.Post, $"/api/team/{year}/{month}/remind/{Uri.EscapeDataString(code)}", null, ct);

    /// <summary>For finance and the administrator: every approved timesheet of the month on one printed sheet</summary>
    public Task<DownloadedFile> GetApprovedExportAsync(int year, int month, string format, CancellationToken ct = default) =>
        DownloadAsync($"/api/export/{year}/{month}/{format}", ct);

    // ---- Plumbing --------------------------------------------------------------------------------------------------------

    private Task<T> GetAsync<T>(string url, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, url, null, ct);

    private Task<DownloadedFile> DownloadAsync(string url, CancellationToken ct) =>
        SendCoreAsync(HttpMethod.Get, url, null, async response =>
        {
            var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName ?? "sihtarica";
            return new DownloadedFile(name.Trim('"'), await response.Content.ReadAsByteArrayAsync(ct));
        }, ct);

    private Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct) =>
        SendCoreAsync(method, url, body, async response =>
            await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new ApiException(response.StatusCode, "empty", "The server sent an empty answer."), ct);

    private async Task<T> SendCoreAsync<T>(HttpMethod method, string url, object? body, Func<HttpResponseMessage, Task<T>> read, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation(ApiHeaders.RequestedWith, ApiHeaders.RequestedWithValue);
        request.Headers.TryAddWithoutValidation("Accept-Language", _language);
        if (_token is { } token) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body != null) request.Content = JsonContent.Create(body, body.GetType());

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(HttpStatusCode.ServiceUnavailable, "unreachable", "The company server cannot be reached. Check the network connection. " + ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(HttpStatusCode.RequestTimeout, "timeout", "The company server did not answer in time.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode) return await read(response);

            throw await ToExceptionAsync(response, ct);
        }
    }

    private static async Task<ApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        ApiErrorDto? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiErrorDto>(cancellationToken: ct);
        }
        catch (Exception)
        {
            // Not our error body (for example a proxy page): fall back to the status
        }

        if (error != null && !string.IsNullOrEmpty(error.Code)) return new ApiException(response.StatusCode, error.Code, error.Message);

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new ApiException(response.StatusCode, "unauthorized", "The server did not accept your sign-in."),
            HttpStatusCode.Forbidden => new ApiException(response.StatusCode, "forbidden", "You are not allowed to do this."),
            HttpStatusCode.NotFound => new ApiException(response.StatusCode, "not-found", "The server does not know this request."),
            _ => new ApiException(response.StatusCode, "server-error", $"The server answered {(int)response.StatusCode}.")
        };
    }
}
