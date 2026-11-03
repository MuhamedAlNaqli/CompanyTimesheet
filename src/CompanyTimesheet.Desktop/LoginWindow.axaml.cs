using System;
using Avalonia.Controls;

namespace CompanyTimesheet.Desktop;

/// <summary>
/// The first window of the app and the only thing shown until somebody has signed in; then the window of their role takes its place
/// (see <see cref="App"/>). It shares the view model with the windows that follow it.
/// </summary>
public partial class LoginWindow : Window
{
    /// <summary>Raised once, when the window is first on screen: the moment to try a sign-in that "remember me" saved</summary>
    public event Action? FirstShown;

    public LoginWindow()
    {
        InitializeComponent();
        Opened += (_, _) => FirstShown?.Invoke();
    }
}
