using Avalonia.Controls;

namespace CompanyTimesheet.Desktop;

/// <summary>The window of a person with a timesheet. The sign-in has a window of its own; this one opens after it.</summary>
public partial class EmployeeWindow : Window
{
    public EmployeeWindow()
    {
        InitializeComponent();
        Opened += (_, _) => WindowSizing.FitToScreen(this);
    }
}
