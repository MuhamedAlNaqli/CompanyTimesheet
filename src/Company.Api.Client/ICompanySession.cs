using Company.Api.Contracts;

namespace Company.Api.Client;

/// <summary>Who is signed in to the company server: the session token of a user name and password, and the calls around it</summary>
public interface ICompanySession
{
    /// <summary>The session token that goes with every request; null means no sign-in (Windows sign-in needs none)</summary>
    void UseToken(string? token);

    bool HasToken { get; }

    /// <summary>The language the server writes its messages in (the findings of the rules): "bs" or "en"</summary>
    void UseLanguage(string language);

    /// <summary>Points the client at another server; throws when the text is not an address, or plain http is used for another computer</summary>
    void UseServer(string address);

    /// <summary>The server is used with a user name and password (not with the Windows account)</summary>
    bool PasswordSignIn { get; }

    Task<LoginResultDto> LoginAsync(string username, string password, bool remember, CancellationToken ct = default);

    /// <summary>Ends the session on the server. A server that cannot be reached does not stop the sign-out.</summary>
    Task LogoutAsync(CancellationToken ct = default);

    /// <summary>The figures of the start screen</summary>
    Task<EmployeeOverviewDto> GetMyOverviewAsync(CancellationToken ct = default);

    Task ChangePasswordAsync(string current, string newPassword, CancellationToken ct = default);

    /// <summary>Asks the administrator for a new password; the answer is the same whether the user name exists or not</summary>
    Task RequestPasswordResetAsync(string username, CancellationToken ct = default);
}

/// <summary>What the administrator does: the people, their user names and the import of the list of employees</summary>
public interface ICompanyAdminClient
{
    Task<List<EmployeeDto>> GetEmployeesAsync(CancellationToken ct = default);

    Task<EmployeeDto> CreateEmployeeAsync(CreateEmployeeRequest request, CancellationToken ct = default);

    Task<EmployeeDto> UpdateEmployeeAsync(string code, UpdateEmployeeRequest request, CancellationToken ct = default);

    Task<ImportPreviewDto> PreviewImportAsync(string csv, CancellationToken ct = default);

    Task<ImportResultDto> ImportAsync(string csv, CancellationToken ct = default);

    Task<List<CredentialDto>> GetCredentialsAsync(CancellationToken ct = default);

    Task SetCredentialsAsync(string code, SetCredentialsRequest request, CancellationToken ct = default);

    Task RemoveCredentialsAsync(string code, CancellationToken ct = default);

    /// <summary>A new temporary password for the employee; it is also mailed to them when the server can send mail</summary>
    Task<ResetResultDto> ResetPasswordAsync(string code, CancellationToken ct = default);

    Task<AuditVerificationDto> VerifyAuditAsync(CancellationToken ct = default);

    /// <summary>The figures of the first screen of the administrator</summary>
    Task<AdminOverviewDto> GetOverviewAsync(CancellationToken ct = default);

    Task<List<HolidayDto>> GetHolidaysAsync(int year, CancellationToken ct = default);


    /// <summary>Adds a state holiday, or renames the one on that date</summary>
    Task<HolidayDto> SaveHolidayAsync(HolidayDto holiday, CancellationToken ct = default);

    Task DeleteHolidayAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>The latest events of the audit log, newest first</summary>
    Task<List<AuditEventDto>> GetAuditAsync(int take, CancellationToken ct = default);

    /// <summary>The codes of the legend, with the ones that were switched off</summary>
    Task<List<CodeDefinitionDto>> GetCodesAsync(CancellationToken ct = default);

    Task<CodeDefinitionDto> AddCodeAsync(SaveCodeRequest request, CancellationToken ct = default);

    Task<CodeDefinitionDto> UpdateCodeAsync(int code, SaveCodeRequest request, CancellationToken ct = default);

    Task DeleteCodeAsync(int code, CancellationToken ct = default);
}
