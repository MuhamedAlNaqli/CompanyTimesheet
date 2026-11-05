using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace CompanyTimesheet.Desktop;

/// <summary>A colour palette the person can choose; <see cref="Swatch"/> is the colour shown on its button</summary>
public sealed record PaletteInfo(string Key, string Name, string Swatch);

/// <summary>
/// The look of the program: a theme (light, dark, or the one of Windows) and a colour palette. The palette replaces the colours of both
/// themes in the application resources, so every window follows at once. The default palette is the one in App.axaml; the Logosoft
/// palette follows the red and grey of logosoft.ba.
/// </summary>
public static class ThemeManager
{
    // Neutral colours (backgrounds, borders, text) in the order of NeutralKeys
    private static readonly string[] NeutralKeys =
    {
        "AppBackground", "AppSurface", "AppSurfaceAlt", "AppSidebar", "AppInputBg", "AppInputText", "AppBorder", "AppBorderStrong",
        "AppDivider", "AppHover", "AppPressed", "AppText", "AppTextStrong", "AppTextMuted", "AppTextFaint"
    };

    // Colours of the brand in the order of AccentKeys: text accent, its hover, soft background, fill, fill on hover, badge background,
    // badge text, soft border, and the light tone Material controls use
    private static readonly string[] AccentKeys =
    {
        "AppAccent", "AppAccentHover", "AppAccentSoft", "AppAccentFill", "AppAccentFillHover", "AppBadgeBg", "AppBadgeText",
        "AppAccentSoftBorder", "MaterialPrimaryLightBrush"
    };

    private static readonly string[] SlateLight = { "F3F5F7", "FFFFFF", "F8FAFB", "FFFFFF", "FFFFFF", "0F172A", "E4E8EC", "CBD2D9", "EEF1F4", "F1F4F6", "E6EAEE", "334155", "0F172A", "64748B", "94A3B8" };
    private static readonly string[] SlateDark = { "0C1114", "151B20", "12181C", "10161A", "11171B", "E8EEF1", "232C33", "34414A", "1C252B", "1D272D", "26323A", "CBD5DB", "F1F5F7", "8FA0AB", "6B7C87" };
    private static readonly string[] GreyLight = { "F2F2F2", "FFFFFF", "F7F7F7", "FFFFFF", "FFFFFF", "1F1F1F", "E3E3E3", "CCCCCC", "EDEDED", "F0F0F0", "E4E4E4", "333333", "111111", "666666", "999999" };
    private static readonly string[] GreyDark = { "141414", "1E1E1E", "191919", "171717", "181818", "EDEDED", "2C2C2C", "3D3D3D", "242424", "262626", "303030", "CFCFCF", "F5F5F5", "9A9A9A", "707070" };

    private sealed record Palette(string Key, string Name, string Swatch, string[] LightNeutral, string[] DarkNeutral, string[] LightAccent, string[] DarkAccent);

    private static readonly Palette[] All =
    {
        new("teal", "Zelena", "#0F766E", SlateLight, SlateDark,
            new[] { "0F766E", "0B5F58", "E2F4F1", "0F766E", "0B5F58", "D9F3EF", "0B5D56", "99D9D1", "14A398" },
            new[] { "2DD4BF", "5EEAD4", "10312E", "0D8077", "0F9488", "123A36", "7EE7DA", "1F6F67", "5EEAD4" }),
        // The red of logosoft.ba is E30613; the text accent on white is the darker C00510 so that it stays readable
        new("logosoft", "Logosoft", "#E30613", GreyLight, GreyDark,
            new[] { "C00510", "9E040D", "FDE8EA", "E30613", "C00510", "FBD5D8", "8F0410", "F4A1A7", "F04452" },
            new[] { "FF5C66", "FF8A91", "3A1417", "C00510", "E30613", "441A1E", "FFADB2", "7A2A30", "FF8A91" }),
        new("blue", "Plava", "#1D4ED8", SlateLight, SlateDark,
            new[] { "1D4ED8", "1E40AF", "E3ECFD", "1D4ED8", "1E40AF", "DBE7FD", "1E3A8A", "9DBBF5", "3B82F6" },
            new[] { "60A5FA", "93C5FD", "12243F", "2563EB", "3B82F6", "152C4F", "93C5FD", "1E4A8A", "93C5FD" }),
        new("graphite", "Grafit", "#374151", GreyLight, GreyDark,
            new[] { "374151", "1F2937", "E5E7EB", "374151", "1F2937", "E5E7EB", "1F2937", "9CA3AF", "6B7280" },
            new[] { "D1D5DB", "F3F4F6", "262A30", "4B5563", "6B7280", "2A2F36", "E5E7EB", "4B5563", "9CA3AF" })
    };

    public static IReadOnlyList<PaletteInfo> Palettes { get; } = All.Select(p => new PaletteInfo(p.Key, p.Name, p.Swatch)).ToList();

    public static string DefaultPalette => All[0].Key;

    public static bool IsKnownPalette(string? key) => All.Any(p => p.Key == key);

    public static bool IsKnownMode(string? mode) => mode is "system" or "light" or "dark";

    /// <summary>Applies the theme and the palette to the whole program. Without a running application (a test) nothing happens.</summary>
    public static void Apply(string? mode, string? paletteKey)
    {
        var app = Application.Current;
        if (app == null) return;

        app.RequestedThemeVariant = mode switch
        {
            "dark" => ThemeVariant.Dark,
            "system" => ThemeVariant.Default,
            _ => ThemeVariant.Light
        };

        var palette = All.FirstOrDefault(p => p.Key == paletteKey) ?? All[0];
        Fill(app, ThemeVariant.Light, palette.LightNeutral, palette.LightAccent);
        Fill(app, ThemeVariant.Dark, palette.DarkNeutral, palette.DarkAccent);
    }

    private static void Fill(Application app, ThemeVariant variant, string[] neutral, string[] accent)
    {
        if (!app.Resources.ThemeDictionaries.TryGetValue(variant, out var provider) || provider is not ResourceDictionary dictionary) return;

        for (var i = 0; i < NeutralKeys.Length; i++) Set(dictionary, NeutralKeys[i], neutral[i]);
        for (var i = 0; i < AccentKeys.Length; i++) Set(dictionary, AccentKeys[i], accent[i]);

        // Material's own controls (check boxes, progress, focus) follow the brand colour through these keys
        var fill = accent[3];
        var fillHover = accent[4];
        var light = accent[8];
        foreach (var key in new[] { "MaterialPrimaryMidBrush", "PrimaryHueMidBrush" }) Set(dictionary, key, fill);
        foreach (var key in new[] { "MaterialPrimaryLightBrush", "PrimaryHueLightBrush" }) Set(dictionary, key, light);
        foreach (var key in new[] { "MaterialPrimaryDarkBrush", "PrimaryHueDarkBrush" }) Set(dictionary, key, fillHover);
    }

    // A new brush replaces the old one, so everything that uses the colour is told to redraw
    private static void Set(ResourceDictionary dictionary, string key, string hex) =>
        dictionary[key] = new SolidColorBrush(Color.Parse("#FF" + hex));
}
