using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyTimesheet.Desktop.Background;

/// <summary>
/// The headless process of the reminder (<c>--reminder-check</c>): started by the Windows task scheduler, it asks the company server what
/// is due, shows at most one notification and exits, so nothing stays in memory between the checks.
/// </summary>
internal static class ReminderRunner
{
    public const string CheckArgument = "--reminder-check";

    /// <summary>Diagnostics: shows a notification as the reminder would look</summary>
    public const string TestArgument = "--reminder-test";

    /// <summary>Diagnostics: shows the "update available" notification as it would look</summary>
    public const string UpdateTestArgument = "--update-test";

    private const string DefaultAppCastUrl = "https://github.com/MuhamedAlNaqli/CompanyTimesheet/releases/latest/download/appcast.xml";

    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(30);

    public static bool IsReminderRun(string[] args) => Array.Exists(args, a => a == CheckArgument || a == TestArgument || a == UpdateTestArgument);

    public static int Run(IServiceProvider services, string[] args)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ReminderRunner");
        try
        {
            // This run has no window and no sign-in: the language is the one this computer used last
            Loc.Initialize(EmployeePreferences.LoadLanguage());
            var exePath = Environment.ProcessPath ?? "";
            if (Array.Exists(args, a => a == TestArgument))
            {
                ShowAndOpenIfClicked(logger, "Predajte šihtaricu", "Ovako izgleda vaš podsjetnik. Kliknite ga da otvorite Evidenciju radnog vremena.", exePath);
                return 0;
            }

            if (Array.Exists(args, a => a == UpdateTestArgument))
            {
                ShowAndOpenIfClicked(logger, "Dostupna je nova verzija", "Ovako izgleda obavijest o novoj verziji. Kliknite je da otvorite Evidenciju radnog vremena i instalirate je.", exePath);
                return 0;
            }

            return RunCheckAsync(services, logger, exePath).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The reminder failed");
            return 1;
        }
    }

    private static async Task<int> RunCheckAsync(IServiceProvider services, ILogger logger, string exePath)
    {
        var state = ReminderState.Load();
        var now = DateTime.Now;

        if (state.UpdateCheckDue(now)) await CheckForUpdateAsync(services, logger, exePath, state);

        if (EmployeeReminders.IsQuiet(now)) return 0;

        var items = await EmployeeReminders.DueAsync(services, logger, DateOnly.FromDateTime(now));
        if (!EmployeeReminders.ShouldNotify(state, items, now)) return 0;

        state.Record(now);
        state.EmployeeSignature = EmployeeReminders.Signature(items);
        state.Save();

        var (title, message) = EmployeeReminders.Compose(items);
        logger.LogInformation("Showing the reminder: {Message}", message);
        ShowAndOpenIfClicked(logger, title, message, exePath);
        return 0;
    }

    /// <summary>
    /// Tells the user about a new version, once per release (and again after a few days). It stays quiet while the app is
    /// open, because the app then offers the update itself.
    /// </summary>
    private static async Task CheckForUpdateAsync(IServiceProvider services, ILogger logger, string exePath, ReminderState state)
    {
        var now = DateTime.Now;
        try
        {
            var current = typeof(ReminderRunner).Assembly.GetName().Version ?? new Version(0, 0);
            var feed = services.GetRequiredService<IConfiguration>()["UpdateService:AppCastUrl"] is { Length: > 0 } configured ? configured : DefaultAppCastUrl;

            var update = await BackgroundUpdateChecker.FindNewerAsync(feed, current, TimeSpan.FromSeconds(15));
            state.LastUpdateCheck = now;
            state.Save();

            if (update == null)
            {
                logger.LogInformation("Update check: version {Current} is up to date", current);
                return;
            }

            if (!state.ShouldAnnounce(update.DisplayVersion, now)) return;
            if (IsAppRunning(exePath))
            {
                logger.LogInformation("Update {Version} is available; the app is open and will offer it itself", update.DisplayVersion);
                return;
            }

            state.RecordAnnounced(update.DisplayVersion, now);
            state.Save();
            logger.LogInformation("Announcing update {Version}", update.DisplayVersion);
            ShowAndOpenIfClicked(logger, "Dostupna je nova verzija", $"Evidencija radnog vremena {update.DisplayVersion} je spremna. Kliknite da otvorite aplikaciju i instalirate je.", exePath);
        }
        catch (Exception ex)
        {
            // Offline or the feed is down: try again at the next scheduled run
            logger.LogInformation("Update check skipped: {Reason}", ex.Message);
        }
    }

    private static bool IsAppRunning(string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return false;

        var name = Path.GetFileNameWithoutExtension(exePath);
        using var self = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                if (process.Id != self.Id) return true;
            }
        }

        return false;
    }

    private static void ShowAndOpenIfClicked(ILogger logger, string title, string message, string exePath)
    {
        var clicked = TrayNotifier.Show(title, message, exePath, NotificationTimeout);
        if (TrayNotifier.LastFailure != null) logger.LogWarning("Notification problem: {Problem}", TrayNotifier.LastFailure);
        else logger.LogInformation("Notification closed (clicked: {Clicked})", clicked);
        if (!clicked || string.IsNullOrEmpty(exePath)) return;

        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
    }
}
