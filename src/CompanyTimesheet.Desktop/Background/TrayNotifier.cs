using System;
using System.Runtime.InteropServices;

namespace CompanyTimesheet.Desktop.Background;

/// <summary>
/// Shows a Windows notification without any UI toolkit: a short-lived hidden window owns a notification-area
/// icon whose balloon Windows 10/11 presents as a toast. Used by the headless reminder process.
/// </summary>
internal static class TrayNotifier
{
    private const uint WM_TIMER = 0x0113;
    private const uint WM_APP_TRAY = 0x8001;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    private const uint NIIF_USER = 0x4;
    private const int NIN_BALLOONHIDE = 0x403, NIN_BALLOONTIMEOUT = 0x404, NIN_BALLOONUSERCLICK = 0x405;
    private const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x80;

    private static bool _clicked;

    /// <summary>What went wrong the last time <see cref="Show"/> could not display a notification</summary>
    public static string? LastFailure { get; private set; }
    private static WndProc? _wndProc;

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Shows the notification and waits until it is clicked, dismissed or the timeout passes</summary>
    /// <returns>True when the user clicked the notification</returns>
    public static bool Show(string title, string message, string iconSourcePath, TimeSpan timeout)
    {
        if (!OperatingSystem.IsWindows()) return false;

        _clicked = false;
        LastFailure = null;
        _wndProc = HandleMessage;
        var instance = GetModuleHandle(null);
        var className = "CompanyTimesheet.ReminderWindow." + Guid.NewGuid().ToString("N");

        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = className
        };
        if (RegisterClassEx(ref windowClass) == 0)
        {
            LastFailure = "RegisterClassEx failed: " + Marshal.GetLastWin32Error();
            return false;
        }

        // A hidden top-level window (not message-only): Explorer refuses balloons for message-only owners
        var window = CreateWindowEx(WS_EX_TOOLWINDOW, className, "", WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (window == IntPtr.Zero)
        {
            LastFailure = "CreateWindowEx failed: " + Marshal.GetLastWin32Error();
            return false;
        }

        var icon = IntPtr.Zero;
        try
        {
            var small = new IntPtr[1];
            if (ExtractIconEx(iconSourcePath, 0, null, small, 1) > 0) icon = small[0];

            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = window,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_APP_TRAY,
                hIcon = icon,
                szTip = "Evidencija radnog vremena"
            };
            if (!Shell_NotifyIcon(NIM_ADD, ref data))
            {
                LastFailure = "Shell_NotifyIcon(add) failed: " + Marshal.GetLastWin32Error();
                return false;
            }

            data.uFlags = NIF_INFO;
            data.szInfoTitle = Truncate(title, 63);
            data.szInfo = Truncate(message, 255);
            data.dwInfoFlags = NIIF_USER;
            data.hBalloonIcon = icon;
            if (!Shell_NotifyIcon(NIM_MODIFY, ref data)) LastFailure = "Shell_NotifyIcon(modify) failed: " + Marshal.GetLastWin32Error();

            SetTimer(window, 1, (uint)timeout.TotalMilliseconds, IntPtr.Zero);
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            Shell_NotifyIcon(NIM_DELETE, ref data);
            return _clicked;
        }
        finally
        {
            DestroyWindow(window);
            UnregisterClass(className, instance);
            if (icon != IntPtr.Zero) DestroyIcon(icon);
        }
    }

    private static IntPtr HandleMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TIMER)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        if (msg == WM_APP_TRAY)
        {
            var notification = (int)((long)lParam & 0xFFFF);
            if (notification == NIN_BALLOONUSERCLICK) _clicked = true;
            if (notification is NIN_BALLOONUSERCLICK or NIN_BALLOONTIMEOUT or NIN_BALLOONHIDE) PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value.Substring(0, length - 1) + "…";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint elapseMs, IntPtr timerProc);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
