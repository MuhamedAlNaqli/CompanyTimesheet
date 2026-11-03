using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CompanyTimesheet.Desktop.Services;

/// <summary>
/// Makes sure only one copy of the app runs per Windows session; a second launch (a shortcut, a clicked
/// reminder notification) brings the running window to the front instead of opening another one
/// </summary>
internal static class SingleInstance
{
    private const int SW_RESTORE = 9;
    private static Mutex? _mutex;

    /// <summary>True when this process is the first instance and may continue starting</summary>
    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, @"Local\CompanyTimesheet.SingleInstance", out var created);
        return created;
    }

    public static void ActivateExisting()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var self = Process.GetCurrentProcess();
        foreach (var other in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(self.ProcessName)))
        {
            using (other)
            {
                if (other.Id == self.Id || other.MainWindowHandle == IntPtr.Zero) continue;
                ShowWindow(other.MainWindowHandle, SW_RESTORE);
                SetForegroundWindow(other.MainWindowHandle);
                return;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
