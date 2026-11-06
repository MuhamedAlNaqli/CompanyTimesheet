using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using NetSparkleUpdater;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.Avalonia;
using NetSparkleUpdater.Configurations;
using NetSparkleUpdater.Enums;

namespace CompanyTimesheet.Desktop.Services;

/// <summary>
/// Updates of the app through NetSparkleUpdater: the appcast is a file of the latest GitHub release, the installer is its MSI
/// </summary>
public sealed class UpdateService : IUpdateService, IDisposable
{
    private const string DefaultAppCastUrl =
        "https://github.com/MuhamedAlNaqli/CompanyTimesheet/releases/latest/download/appcast.xml";

    private static readonly TimeSpan StartupCheckDelay = TimeSpan.FromSeconds(20);

    private readonly ILogger<UpdateService> _logger;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private SparkleUpdater? _sparkleUpdater;
    private System.Timers.Timer? _updateTimer;
    private string _appCastUrl = DefaultAppCastUrl;
    private string? _lastPromptedVersion;
    private bool _disposed = false;

    public bool AutoUpdatesEnabled { get; set; } = true;


    public UpdateService(ILogger<UpdateService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing UpdateService...");

            _appCastUrl = _configuration["UpdateService:AppCastUrl"] is { Length: > 0 } configured
                ? configured
                : DefaultAppCastUrl;
            AutoUpdatesEnabled = _configuration.GetValue("UpdateService:AutoUpdatesEnabled", true);

            _logger.LogInformation("AppCast URL: {AppCastUrl}", _appCastUrl);

            // Verify update signatures whenever a public key is configured
            var publicKey = _configuration["UpdateService:Ed25519PublicKey"];
            Ed25519Checker checker;
            if (!string.IsNullOrWhiteSpace(publicKey))
            {
                checker = new Ed25519Checker(SecurityMode.Strict, publicKey);
            }
            else
            {
                _logger.LogWarning("UpdateService:Ed25519PublicKey is not configured - update signatures are NOT verified");
                checker = new Ed25519Checker(SecurityMode.Unsafe);
            }

            _sparkleUpdater = new SparkleUpdater(_appCastUrl, checker)
            {
                UIFactory = new UIFactory(),
                // Report the real assembly version as the installed version (see AppAssemblyAccessor)
                Configuration = new DefaultConfiguration(new AppAssemblyAccessor()),
                // GitHub's download redirect has no file name, so without this the installer is saved without
                // an extension and the install step cannot start it (the app just closes)
                TmpDownloadFileNameWithExtension = "CompanyTimesheet-update.msi"
            };

            // The program closes for the installer; it must come back by itself afterwards
            _sparkleUpdater.PreparingToExit += (_, _) => UpdateRelauncher.Start(_logger);

            _logger.LogInformation("UpdateService initialized successfully (current version {Version})", GetCurrentVersion());
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize UpdateService");
            throw;
        }
    }

    /// <summary>
    /// Checks for an update, reports the result and, when one exists, shows the update window
    /// </summary>
    public Task<UpdateCheckOutcome> CheckAndPromptAsync() => RunCheckAsync(promptIfAvailable: true);

    public void StartAutomaticUpdateChecking(TimeSpan interval)
    {
        try
        {
            if (!AutoUpdatesEnabled)
            {
                _logger.LogInformation("Automatic updates disabled");
                return;
            }

            _logger.LogInformation("Starting automatic update checking with interval: {Interval}", interval);

            _updateTimer?.Stop();
            _updateTimer?.Dispose();

            _updateTimer = new System.Timers.Timer(interval.TotalMilliseconds);
            _updateTimer.Elapsed += async (sender, e) => await SafeAutomaticCheckAsync();
            _updateTimer.AutoReset = true;
            _updateTimer.Start();

            // Check shortly after startup instead of waiting a full interval for the first check
            _ = Task.Run(async () =>
            {
                await Task.Delay(StartupCheckDelay);
                await SafeAutomaticCheckAsync();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start automatic update checking");
        }
    }

    public void StopAutomaticUpdateChecking()
    {
        try
        {
            _logger.LogInformation("Stopping automatic update checking");
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
            _updateTimer = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping automatic update checking");
        }
    }

    public string GetCurrentVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? "0.0.0.0";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current version");
            return "0.0.0.0";
        }
    }

    private async Task SafeAutomaticCheckAsync()
    {
        if (!AutoUpdatesEnabled || _disposed) return;

        try
        {
            var outcome = await RunCheckAsync(promptIfAvailable: true, automatic: true);
            _logger.LogInformation("Automatic update check: {Status} {Message}", outcome.Status, outcome.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during automatic update check");
        }
    }

    private async Task<UpdateCheckOutcome> RunCheckAsync(bool promptIfAvailable, bool automatic = false)
    {
        if (_sparkleUpdater == null)
        {
            return Complete(UpdateCheckOutcome.Failed("The update service is not initialized."));
        }

        if (!await _checkLock.WaitAsync(0))
        {
            return UpdateCheckOutcome.Failed("An update check is already running.");
        }

        try
        {
            _logger.LogInformation("Checking for updates (current {Version})...", GetCurrentVersion());

            // Make a failure to reach the update feed explicit instead of a silent "could not determine"
            var feedError = await ProbeFeedAsync();
            if (feedError != null)
            {
                _logger.LogWarning("Update feed unreachable: {Error}", feedError);
                return Complete(UpdateCheckOutcome.Failed(feedError));
            }

            var info = await _sparkleUpdater.CheckForUpdatesQuietly();

            switch (info.Status)
            {
                case UpdateStatus.UpdateAvailable:
                {
                    var latest = info.Updates?.FirstOrDefault();
                    var version = latest?.ShortVersion ?? latest?.Version ?? "unknown";

                    // Automatic checks only bother the user once per version per session
                    var shouldPrompt = promptIfAvailable && (!automatic || _lastPromptedVersion != version);
                    if (shouldPrompt && info.Updates != null)
                    {
                        _lastPromptedVersion = version;
                        await Dispatcher.UIThread.InvokeAsync(() => _sparkleUpdater.ShowUpdateNeededUI(info.Updates, false));
                    }

                    return Complete(UpdateCheckOutcome.Available(version));
                }

                case UpdateStatus.UserSkipped:
                    return Complete(UpdateCheckOutcome.UpToDate(GetCurrentVersion(), "A newer version exists but was skipped earlier."));

                case UpdateStatus.CouldNotDetermine:
                    return Complete(UpdateCheckOutcome.Failed("Could not read the update information from the update feed."));

                default:
                    return Complete(UpdateCheckOutcome.UpToDate(GetCurrentVersion()));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for updates");
            return Complete(UpdateCheckOutcome.Failed($"Update check failed: {ex.Message}"));
        }
        finally
        {
            _checkLock.Release();
        }
    }

    /// <summary>
    /// Returns an error message when the appcast cannot be downloaded, otherwise null
    /// </summary>
    private async Task<string?> ProbeFeedAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CompanyTimesheet-Updater");
            using var response = await client.GetAsync(_appCastUrl, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode) return null;

            return $"The update server answered {(int)response.StatusCode} {response.ReasonPhrase} for {_appCastUrl}. " +
                   "The update feed must be publicly reachable (a private GitHub repository cannot serve updates).";
        }
        catch (Exception ex)
        {
            return $"Could not reach the update server ({ex.Message}). Check your internet connection.";
        }
    }

    private static UpdateCheckOutcome Complete(UpdateCheckOutcome outcome) => outcome;

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _logger.LogInformation("Disposing UpdateService");
            _disposed = true;
            StopAutomaticUpdateChecking();
            _sparkleUpdater?.Dispose();
            _sparkleUpdater = null;
            _checkLock.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing UpdateService");
        }
    }
}
