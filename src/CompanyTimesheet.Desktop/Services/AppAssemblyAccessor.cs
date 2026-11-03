using System.Reflection;
using NetSparkleUpdater.Interfaces;

namespace CompanyTimesheet.Desktop.Services;

/// <summary>
/// Tells the updater which version is installed. The default accessor reads the informational version
/// ("1.0.0+&lt;git commit&gt;"), which never matches the versions published in the appcast and makes
/// every feed entry look newer than the installed app. We use the plain assembly version instead.
/// </summary>
public class AppAssemblyAccessor : IAssemblyAccessor
{
    private readonly Assembly _assembly = typeof(AppAssemblyAccessor).Assembly;

    public string? AssemblyTitle => _assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? "Evidencija radnog vremena";

    public string? AssemblyVersion => _assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    public string? AssemblyDescription => _assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;

    public string? AssemblyProduct => _assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "CompanyTimesheet";

    public string? AssemblyCopyright => _assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

    public string? AssemblyCompany => _assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "Smart Point";
}
