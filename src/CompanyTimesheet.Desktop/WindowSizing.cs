using System;
using Avalonia;
using Avalonia.Controls;

namespace CompanyTimesheet.Desktop;

internal static class WindowSizing
{
    /// <summary>
    /// A window taller than the screen (a laptop, or a scaled display) has its title bar above the top edge: the buttons to
    /// minimise, resize and close cannot be reached. The window is made to fit the working area and centred in it.
    /// </summary>
    public static void FitToScreen(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen == null) return;

        var scale = screen.Scaling;
        var area = screen.WorkingArea;
        var maxWidth = area.Width / scale - 16;
        var maxHeight = area.Height / scale - 48;

        window.MinWidth = Math.Min(window.MinWidth, maxWidth);
        window.MinHeight = Math.Min(window.MinHeight, maxHeight);
        window.Width = Math.Min(window.Width, maxWidth);
        window.Height = Math.Min(window.Height, maxHeight);

        var x = area.X + (int)((area.Width - window.Width * scale) / 2);
        var y = area.Y + (int)((area.Height - window.Height * scale) / 2);
        window.Position = new PixelPoint(x, Math.Max(area.Y, y));
    }
}
