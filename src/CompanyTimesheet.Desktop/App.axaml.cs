using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyTimesheet.Desktop;

public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            RequestedThemeVariant = ThemeVariant.Light;
            desktop.MainWindow = CreateWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The only window of the app. A missing server address is shown in the window instead of stopping the app.</summary>
    private static Window CreateWindow()
    {
        if (ServiceProvider == null) throw new InvalidOperationException("ServiceProvider is not initialized");

        // The interface is Bosnian only (month names, date pickers and number formats)
        var bosnian = EmployeeFormat.Bosnian;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = bosnian;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = bosnian;
        System.Globalization.CultureInfo.CurrentCulture = bosnian;
        System.Globalization.CultureInfo.CurrentUICulture = bosnian;

        var window = new EmployeeWindow();
        try
        {
            window.DataContext = ServiceProvider.GetRequiredService<EmployeeViewModel>();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "The app could not start");
            window.Content = new TextBlock
            {
                Text = "Evidencija radnog vremena ne može se pokrenuti: " + (ex.InnerException?.Message ?? ex.Message),
                Margin = new Thickness(32),
                TextWrapping = global::Avalonia.Media.TextWrapping.Wrap
            };
        }

        return window;
    }
}
