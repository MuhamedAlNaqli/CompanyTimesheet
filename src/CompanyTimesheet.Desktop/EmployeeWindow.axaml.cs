using System;
using Avalonia;
using Avalonia.Controls;

namespace CompanyTimesheet.Desktop;

public partial class EmployeeWindow : Window
{
    public EmployeeWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            FitToScreen();
            if (DataContext is EmployeeViewModel viewModel) await viewModel.LoadAsync();
        };
    }

    /// <summary>
    /// A window taller than the screen (a laptop, or a scaled display) has its title bar above the top edge: the buttons to
    /// minimise, resize and close cannot be reached. The window is made to fit the working area and centred in it.
    /// </summary>
    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null) return;

        var scale = screen.Scaling;
        var area = screen.WorkingArea;
        var maxWidth = area.Width / scale - 16;
        var maxHeight = area.Height / scale - 48;

        MinWidth = Math.Min(MinWidth, maxWidth);
        MinHeight = Math.Min(MinHeight, maxHeight);
        Width = Math.Min(Width, maxWidth);
        Height = Math.Min(Height, maxHeight);

        var x = area.X + (int)((area.Width - Width * scale) / 2);
        var y = area.Y + (int)((area.Height - Height * scale) / 2);
        Position = new PixelPoint(x, Math.Max(area.Y, y));
    }
}
