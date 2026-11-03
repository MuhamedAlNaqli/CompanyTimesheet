using System;
using System.Threading.Tasks;

namespace CompanyTimesheet.Desktop.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

/// <summary>What a check for a new version found, with a sentence the person can read</summary>
public sealed class UpdateCheckOutcome
{
    public UpdateCheckStatus Status { get; init; }

    /// <summary>Latest version found on the update feed (when known)</summary>
    public string? Version { get; init; }

    public string Message { get; init; } = string.Empty;

    public static UpdateCheckOutcome UpToDate(string currentVersion, string? extra = null) => new()
    {
        Status = UpdateCheckStatus.UpToDate,
        Version = currentVersion,
        Message = $"Imate najnoviju verziju ({currentVersion})." + (extra == null ? string.Empty : " " + extra)
    };

    public static UpdateCheckOutcome Available(string version) => new()
    {
        Status = UpdateCheckStatus.UpdateAvailable,
        Version = version,
        Message = $"Dostupna je verzija {version}."
    };

    public static UpdateCheckOutcome Failed(string message) => new() { Status = UpdateCheckStatus.Failed, Message = message };
}

public interface IUpdateService
{
    Task InitializeAsync();

    /// <summary>Checks for an update, reports the result and, when a newer version exists, shows the update window</summary>
    Task<UpdateCheckOutcome> CheckAndPromptAsync();

    void StartAutomaticUpdateChecking(TimeSpan interval);

    void StopAutomaticUpdateChecking();

    string GetCurrentVersion();

    bool AutoUpdatesEnabled { get; set; }
}
