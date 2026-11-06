using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CompanyTimesheet.Desktop.Services;

/// <summary>
/// Starts the program again once an update has been installed. The installer (an MSI for all users) runs after this program has closed,
/// and NetSparkle's own relaunch would not wait for it to finish, so a small hidden helper does the waiting: it lets this process
/// exit, waits until the installer is done (the version in the registry changes, or no installer is running any more, for instance when the
/// person declined the administrator prompt) and then starts the program as the person who was using it.
/// </summary>
public static class UpdateRelauncher
{
    private const string RegistryKey = @"HKLM:\SOFTWARE\SmartPoint\CompanyTimesheet";

    public static void Start(ILogger logger)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe) || !OperatingSystem.IsWindows()) return;

            var script = Script(Environment.ProcessId, exe);
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            logger.LogInformation("The program will start again when the update is installed");
        }
        catch (Exception ex)
        {
            // The update itself must not fail because the restart could not be arranged: the person starts the program by hand
            logger.LogWarning(ex, "The restart after the update could not be arranged");
        }
    }

    /// <summary>The text of the helper; public so that a test can check what it waits for</summary>
    public static string Script(int processId, string exe) => $@"
$exe = '{exe.Replace("'", "''")}'
$key = '{RegistryKey}'
$old = (Get-ItemProperty $key -ErrorAction SilentlyContinue).Version
try {{ Wait-Process -Id {processId} -Timeout 120 -ErrorAction Stop }} catch {{ }}
$idle = 0
$deadline = (Get-Date).AddMinutes(6)
Start-Sleep -Seconds 8
while ((Get-Date) -lt $deadline) {{
    $now = (Get-ItemProperty $key -ErrorAction SilentlyContinue).Version
    if ($now -and $now -ne $old) {{ break }}
    if (Get-Process -Name msiexec -ErrorAction SilentlyContinue) {{ $idle = 0 }} else {{ $idle++ }}
    if ($idle -ge 8) {{ break }}
    Start-Sleep -Seconds 2
}}
Start-Sleep -Seconds 3
Start-Process -FilePath $exe
";
}
