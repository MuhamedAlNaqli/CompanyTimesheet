using Avalonia.Controls;

namespace CompanyTimesheet.Desktop;

/// <summary>The window of an administrator: only the administration, no timesheet. It opens after the sign-in window.</summary>
public partial class AdminWindow : Window
{
    public AdminWindow()
    {
        InitializeComponent();
        Opened += (_, _) => WindowSizing.FitToScreen(this);
    }
}
