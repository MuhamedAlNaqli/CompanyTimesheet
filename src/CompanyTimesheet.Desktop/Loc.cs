using System;
using System.ComponentModel;
using System.Globalization;

namespace CompanyTimesheet.Desktop;

/// <summary>
/// The language of the interface: Bosnian (the language the texts are written in) or English. A text is looked up by its Bosnian wording;
/// a text without an English version is shown in Bosnian, so a missing translation never breaks a screen. Windows follow a change of
/// language at once, because their texts are bound to <see cref="Version"/> (see <see cref="TrExtension"/>).
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public static readonly string[] Languages = { "bs", "en" };

    private static string _language = "bs";
    private static int _version;

    /// <summary>Counts the changes of the language; every text of a window is bound to it and reads its wording again when it changes</summary>
    public int Version => _version;

    /// <summary>"bs" or "en"</summary>
    public static string Language
    {
        get => _language;
        set
        {
            var next = value == "en" ? "en" : "bs";
            if (next == _language) return;
            _language = next;
            Apply(next);
            _version++;
            Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(nameof(Version)));
            Changed?.Invoke();
        }
    }

    /// <summary>Sets the language at start-up, without telling anybody (there is nobody yet)</summary>
    public static void Initialize(string? language)
    {
        _language = language == "en" ? "en" : "bs";
        Apply(_language);
    }

    public static bool IsEnglish => _language == "en";

    /// <summary>Raised after the language changed, for the texts that are built in code and have to be built again</summary>
    public static event Action? Changed;

    public static bool IsKnown(string? language) => language is "bs" or "en";

    /// <summary>The culture of the interface: it names the months of a date picker and writes the numbers</summary>
    public static CultureInfo Culture => _language == "en" ? English : EmployeeFormat.Bosnian;

    private static readonly CultureInfo English = new("en-GB");

    private static void Apply(string language)
    {
        var culture = language == "en" ? English : EmployeeFormat.Bosnian;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>The text in the language of the interface; <paramref name="bosnian"/> is the key and the Bosnian wording</summary>
    public static string T(string bosnian) =>
        _language == "en" && Translations.English.TryGetValue(bosnian, out var english) ? english : bosnian;

    /// <summary>A text with values: the Bosnian wording is the pattern, written with {0}, {1}…</summary>
    public static string F(string bosnian, params object?[] values) => string.Format(Culture, T(bosnian), values);

    public event PropertyChangedEventHandler? PropertyChanged;
}
