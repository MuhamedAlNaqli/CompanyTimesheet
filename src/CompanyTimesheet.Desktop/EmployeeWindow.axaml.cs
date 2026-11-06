using Avalonia.Controls;

namespace CompanyTimesheet.Desktop;

/// <summary>The window of a person with a timesheet. The sign-in has a window of its own; this one opens after it.</summary>
public partial class EmployeeWindow : Window
{
    public EmployeeWindow()
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
