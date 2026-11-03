using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using CompanyTimesheet.Desktop.Background;
using CompanyTimesheet.Desktop.Services;
using Company.Api.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace CompanyTimesheet.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // The Windows task scheduler starts this exe with --reminder-check; that run shows a notification
            // and exits without ever creating a window
            var reminderRun = ReminderRunner.IsReminderRun(args);
            if (!reminderRun && !AllowSecondInstance() && !SingleInstance.TryAcquire())
            {
                SingleInstance.ActivateExisting();
                return;
            }

            var host = CreateHostBuilder(args).Build();
            if (reminderRun)
            {
                Environment.ExitCode = ReminderRunner.Run(host.Services, args);
                Serilog.Log.CloseAndFlush();
                return;
            }

            App.ServiceProvider = host.Services;
            _ = Task.Run(() => InstallReminderTaskAsync(host.Services));
            // The update service is not needed to show the first window
            _ = Task.Run(() => InitializeUpdateServiceAsync(host.Services));
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Serilog.Log.Fatal(ex, "Unhandled exception in Main");
            throw;
        }
    }

    /// <summary>Debug builds only: lets a development build run next to an installed copy (set COMPANYTIMESHEET_ALLOW_MULTIPLE=1)</summary>
    private static bool AllowSecondInstance()
    {
#if DEBUG
        return Environment.GetEnvironmentVariable("COMPANYTIMESHEET_ALLOW_MULTIPLE") == "1";
#else
        return false;
#endif
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.Sources.Clear();
                config.SetBasePath(AppContext.BaseDirectory)
                      .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                      // The address of the company server, set by IT for this PC; not part of the program
                      .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true)
                      .AddEnvironmentVariables()
                      .AddCommandLine(args);
            })
            .UseSerilog((context, configuration) =>
            {
                // The install folder is read-only for the user, so logs live in the user profile
                configuration
                    .WriteTo.File(Path.Combine(ReminderState.FolderPath, "logs", "companytimesheet-.txt"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                    .MinimumLevel.Information();
            })
            .ConfigureServices((context, services) =>
            {
                // The company server: sign-in with the user name and password the administrator created (the session token is kept in
                // memory, and in a protected file with "remember me"). Windows sign-in is switched off unless Company:WindowsLogin is
                // true. Created on first use, so a missing address is shown in the window.
                services.AddSingleton<IEmployeeSignInStore, ProtectedSignInStore>();
                services.AddSingleton(sp =>
                {
                    var configuration = sp.GetRequiredService<IConfiguration>();
                    var address = configuration["Company:ApiUrl"];
                    if (string.IsNullOrWhiteSpace(address))
                        throw new InvalidOperationException("The company server address is not set. Ask IT for it and set Company:ApiUrl in appsettings.local.json.");

                    var windows = configuration.GetValue<bool>("Company:WindowsLogin");
                    var http = windows
                        ? CompanyApiClient.CreateWindowsHttpClient(new Uri(address))
                        : CompanyApiClient.CreatePlainHttpClient(new Uri(address));
                    var client = new CompanyApiClient(http, passwordSignIn: !windows);
                    client.UseToken(sp.GetRequiredService<IEmployeeSignInStore>().Load());
                    return client;
                });
                services.AddSingleton<ICompanyApiClient>(sp => sp.GetRequiredService<CompanyApiClient>());
                services.AddSingleton<ICompanySession>(sp => sp.GetRequiredService<CompanyApiClient>());
                services.AddSingleton<ICompanyAdminClient>(sp => sp.GetRequiredService<CompanyApiClient>());

                services.AddSingleton<DialogService>();
                services.AddSingleton<IPlatformService, PlatformService>();
                services.AddSingleton<IEmployeeDialogs, WindowEmployeeDialogs>();
                services.AddSingleton<ISheetFileSaver, SheetFileSaver>();
                services.AddSingleton<IEmployeeShell, EmployeeShell>();
                services.AddSingleton<ReminderTask>();
                services.AddSingleton<IUpdateService, UpdateService>();
                services.AddTransient<EmployeeViewModel>();
            });

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Software rendering keeps the graphics driver out of the process; this UI is light enough
            .With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } })
            .WithInterFont()
            .LogToTrace();

    private static async Task InitializeUpdateServiceAsync(IServiceProvider services)
    {
        var logger = services.GetService<ILogger<App>>();
        try
        {
            var updateService = services.GetRequiredService<IUpdateService>();
            await updateService.InitializeAsync();

            var interval = services.GetRequiredService<IConfiguration>()["UpdateService:CheckInterval"] ?? "24:00:00";
            updateService.StartAutomaticUpdateChecking(TimeSpan.TryParse(interval, out var parsed) ? parsed : TimeSpan.FromHours(24));
        }
        catch (Exception ex)
        {
            // Never fail the start because of the update service
            logger?.LogError(ex, "Failed to initialize the update service");
        }
    }

    /// <summary>
    /// The scheduled task asks the company server what is due and shows it, even when the app is closed. It is registered once per
    /// install location (an update that moves the program registers it again).
    /// </summary>
    private static async Task InstallReminderTaskAsync(IServiceProvider services)
    {
        try
        {
            await services.GetRequiredService<ReminderTask>().InstallAsync();
        }
        catch (Exception ex)
        {
            services.GetService<ILogger<App>>()?.LogWarning(ex, "Could not install the reminder task");
        }
    }
}
