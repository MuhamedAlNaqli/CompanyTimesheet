using System;
using System.IO;

namespace CompanyTimesheet.Desktop;

/// <summary>Small choices of the employee window that are remembered on this PC, like the language</summary>
public static class EmployeePreferences
{
    /// <summary>Tests keep their choices in a folder of their own, not in the profile of whoever runs them</summary>
    public static string? FolderOverride { get; set; }

    private static string Folder => FolderOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CompanyTimesheet");

    private static string ViewPath => Path.Combine(Folder, "employee-view.txt");

    private static string Read(string file)
    {
        try
        {
            var path = Path.Combine(Folder, file);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    private static void Write(string file, string value)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, file), value);
        }
        catch
        {
            // The choice still applies for this session
        }
    }

    /// <summary>True when the employee chose the calendar instead of the list</summary>
    public static bool LoadCalendarView()
    {
        try
        {
            return File.Exists(ViewPath) && File.ReadAllText(ViewPath).Trim().Equals("calendar", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void SaveCalendarView(bool calendar)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ViewPath)!);
            File.WriteAllText(ViewPath, calendar ? "calendar" : "list");
        }
        catch
        {
            // The choice still applies for this session
        }
    }

    // The look of the program is kept on the server too (it follows the person); this copy is what the sign-in window uses before anybody has signed in

    /// <summary>"system", "light" or "dark"</summary>
    public static string LoadThemeMode() => Read("theme-mode.txt") is { Length: > 0 } mode ? mode : "light";

    public static void SaveThemeMode(string mode) => Write("theme-mode.txt", mode);

    public static string LoadPalette() => Read("palette.txt") is { Length: > 0 } palette ? palette : "teal";

    public static void SavePalette(string palette) => Write("palette.txt", palette);

    /// <summary>"bs" or "en"</summary>
    public static string LoadLanguage() => Read("language.txt") is "en" ? "en" : "bs";

    public static void SaveLanguage(string language) => Write("language.txt", language);

    /// <summary>The side bar is open (with the names of the buttons) unless the person folded it</summary>
    public static bool LoadSidebarExpanded() => !Read("sidebar.txt").Equals("collapsed", StringComparison.OrdinalIgnoreCase);

    public static void SaveSidebarExpanded(bool expanded) => Write("sidebar.txt", expanded ? "expanded" : "collapsed");
}
