using Avalonia.Controls;

namespace CompanyTimesheet.Desktop;

/// <summary>The window of an administrator: only the administration, no timesheet. It opens after the sign-in window.</summary>
public partial class AdminWindow : Window
{
    public AdminWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            WindowSizing.FitToScreen(this);
            Report();
        };
        SizeChanged += (_, _) => Report();
    }

    /// <summary>Tells the view model how wide the window is, so that the side bar can fold in a narrow one</summary>
    private void Report()
    {
        if (DataContext is EmployeeViewModel viewModel) viewModel.SetWindowWidth(Bounds.Width);
    }
}
