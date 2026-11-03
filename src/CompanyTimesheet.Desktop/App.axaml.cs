using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyTimesheet.Desktop;

public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; set; }

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private EmployeeViewModel? _viewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            RequestedThemeVariant = ThemeVariant.Light;
            _desktop = desktop;
            desktop.MainWindow = CreateFirstWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The sign-in window is the only one on screen until somebody has signed in. Then the window of their role takes its place
    /// (the administration for an administrator, the timesheet for everybody else), and after a sign-out the sign-in window returns.
    /// A missing server address is shown in the window instead of stopping the app.
    /// </summary>
    private Window CreateFirstWindow()
    {
        if (ServiceProvider == null) throw new InvalidOperationException("ServiceProvider is not initialized");

        // The interface is Bosnian only (month names, date pickers and number formats)
        var bosnian = EmployeeFormat.Bosnian;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = bosnian;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = bosnian;
        System.Globalization.CultureInfo.CurrentCulture = bosnian;
        System.Globalization.CultureInfo.CurrentUICulture = bosnian;

        try
        {
            _viewModel = ServiceProvider.GetRequiredService<EmployeeViewModel>();
            _viewModel.PropertyChanged += OnViewModelChanged;

            // A sign-in saved by "remember me" is tried as soon as the window is up
            var login = CreateLoginWindow();
            login.FirstShown += async () => await _viewModel.LoadAsync();
            return login;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "The app could not start");
            return new Window
            {
                Title = "Evidencija radnog vremena",
                Width = 520,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new TextBlock
                {
                    Text = "Evidencija radnog vremena ne može se pokrenuti: " + (ex.InnerException?.Message ?? ex.Message),
                    Margin = new Thickness(32),
                    TextWrapping = global::Avalonia.Media.TextWrapping.Wrap
                }
            };
        }
    }

    private LoginWindow CreateLoginWindow() => new() { DataContext = _viewModel };

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EmployeeViewModel.IsConnected)) Dispatcher.UIThread.Post(SwitchWindow);
    }

    /// <summary>Signed in: the window of the role replaces the sign-in window. Signed out (or the session ended): the other way round.</summary>
    private void SwitchWindow()
    {
        if (_desktop?.MainWindow is not { } current || _viewModel == null) return;

        Window next;
        if (_viewModel.IsConnected)
        {
            if (current is not LoginWindow) return;
            next = _viewModel.IsAdmin
                ? new AdminWindow { DataContext = _viewModel }
                : new EmployeeWindow { DataContext = _viewModel };
        }
        else
        {
            if (current is LoginWindow) return;
            next = CreateLoginWindow();
        }

        // The new window is the main one before the old one closes, so closing the old one does not end the app
        _desktop.MainWindow = next;
        next.Show();
        current.Close();
    }
}
