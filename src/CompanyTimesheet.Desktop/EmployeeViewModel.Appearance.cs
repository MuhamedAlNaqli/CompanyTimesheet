using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Company.Api.Client;
using Company.Api.Contracts;
using Microsoft.Extensions.Logging;
using Timesheet.Domain;

namespace CompanyTimesheet.Desktop;

/// <summary>A language on the side bar</summary>
public sealed partial class LanguageOption : ObservableObject
{
    public LanguageOption(string key, string name, bool selected, Action<string> select)
    {
        Key = key;
        Name = name;
        _isSelected = selected;
        SelectCommand = new RelayCommand(() => select(key));
    }

    public string Key { get; }

    public string Name { get; }

    public IRelayCommand SelectCommand { get; }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>A palette on the side bar: its colour, and whether it is the one in use</summary>
public sealed partial class PaletteOption : ObservableObject
{
    public PaletteOption(PaletteInfo info, bool selected, Action<string>? select = null)
    {
        SelectCommand = new RelayCommand(() => select?.Invoke(info.Key));
        Key = info.Key;
        _name = info.Name;
        Swatch = new SolidColorBrush(Color.Parse(info.Swatch));
        _isSelected = selected;
    }

    private readonly string _name;

    public string Key { get; }

    /// <summary>The name in the language of the interface</summary>
    public string Name => Loc.T(_name);

    public void LanguageChanged() => OnPropertyChanged(nameof(Name));

    public IRelayCommand SelectCommand { get; }

    public IBrush Swatch { get; }

    [ObservableProperty] private bool _isSelected;
}

// The look of the program (theme and palette), the side bar, and the codes of the legend as the employee window handles them
public partial class EmployeeViewModel
{
    private bool _applyingServerAppearance;

    /// <summary>"system", "light" or "dark"</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModeSystem), nameof(IsModeLight), nameof(IsModeDark), nameof(ThemeToggleText))]
    private string _themeMode = EmployeePreferences.LoadThemeMode();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPaletteName), nameof(CurrentPaletteLabel), nameof(CurrentPaletteSwatch))]
    private string _palette = EmployeePreferences.LoadPalette();

    /// <summary>The side bar shows the names of its buttons; folded, only the icons</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth), nameof(SidebarShowsText))]
    private bool _sidebarExpanded = EmployeePreferences.LoadSidebarExpanded();

    /// <summary>Who is signed in, shown at the bottom of the side bar</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UserInitials))]
    private string _userName = "";

    [ObservableProperty] private string _userCode = "";

    /// <summary>The first letters of the first and the last name, for the round badge of the person</summary>
    public string UserInitials
    {
        get
        {
            var parts = UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? "" : parts.Length == 1 ? parts[0][..1].ToUpperInvariant() : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
        }
    }

    /// <summary>The window is narrow: the side bar shows only its icons, whatever the person chose, so the work area keeps its room</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth), nameof(SidebarShowsText))]
    private bool _isCompact;

    /// <summary>Below this width of the window (in device-independent pixels) the side bar folds by itself</summary>
    public const double CompactBelow = 1200;

    public void SetWindowWidth(double width) => IsCompact = width < CompactBelow;

    public bool SidebarShowsText => SidebarExpanded && !IsCompact;

    public double SidebarWidth => SidebarShowsText ? 224 : 60;

    public bool IsModeSystem => ThemeMode == "system";

    public bool IsModeLight => ThemeMode == "light";

    public bool IsModeDark => ThemeMode == "dark";

    /// <summary>The button of the side bar names the theme it switches to: dark while the look is light, light otherwise</summary>
    public string ThemeToggleText => IsModeDark ? Loc.T("Svijetla tema") : Loc.T("Tamna tema");

    public string CurrentPaletteName => Loc.T(ThemeManager.Palettes.FirstOrDefault(p => p.Key == Palette)?.Name ?? "");

    public string CurrentPaletteLabel => Loc.F("Boje: {0}", CurrentPaletteName);

    public IBrush CurrentPaletteSwatch => new SolidColorBrush(Color.Parse(ThemeManager.Palettes.FirstOrDefault(p => p.Key == Palette)?.Swatch ?? "#0F766E"));

    /// <summary>"bs" or "en"</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentLanguageName))]
    private string _language = Loc.Language;

    public string CurrentLanguageName => Loc.IsEnglish ? "English" : "Bosanski";

    public IReadOnlyList<LanguageOption> LanguageOptions { get; private set; } = Array.Empty<LanguageOption>();

    public IReadOnlyList<PaletteOption> PaletteOptions { get; private set; } = Array.Empty<PaletteOption>();

    private void InitAppearance()
    {
        if (!ThemeManager.IsKnownMode(ThemeMode)) ThemeMode = "light";
        if (!ThemeManager.IsKnownPalette(Palette)) Palette = ThemeManager.DefaultPalette;
        LanguageOptions = new[]
        {
            new LanguageOption("bs", "Bosanski", Language == "bs", key => Language = key),
            new LanguageOption("en", "English", Language == "en", key => Language = key)
        };
        Loc.Changed += OnLanguageApplied;
        _session.UseLanguage(Language);
        PaletteOptions = ThemeManager.Palettes.Select(p => new PaletteOption(p, p.Key == Palette, key => Palette = key)).ToList();
    }

    [RelayCommand]
    private void ToggleSidebar() => SidebarExpanded = !SidebarExpanded;

    /// <summary>One button: from the light theme to the dark one and back (from the theme of Windows it goes to dark)</summary>
    [RelayCommand]
    private void ToggleTheme() => ThemeMode = IsModeDark ? "light" : "dark";

    /// <summary>Follow the theme of Windows, or go back to light</summary>
    [RelayCommand]
    private void ToggleSystemTheme() => ThemeMode = IsModeSystem ? "light" : "system";

    [RelayCommand]
    private void SetThemeMode(string? mode)
    {
        if (ThemeManager.IsKnownMode(mode)) ThemeMode = mode!;
    }

    [RelayCommand]
    private void SetPalette(string? key)
    {
        if (ThemeManager.IsKnownPalette(key)) Palette = key!;
    }

    partial void OnLanguageChanged(string value)
    {
        foreach (var option in LanguageOptions) option.IsSelected = option.Key == value;
        Loc.Language = value;
        _session.UseLanguage(value);
        if (_applyingServerAppearance) return;
        EmployeePreferences.SaveLanguage(value);
        _ = SavePreferencesAsync(new PreferencesRequest(null, null, value));
    }

    /// <summary>The texts written in code are written again: the ones the window shows now, and the month or the list on screen</summary>
    private void OnLanguageApplied()
    {
        foreach (var option in PaletteOptions) option.LanguageChanged();
        OnPropertyChanged(nameof(ThemeToggleText));
        OnPropertyChanged(nameof(CurrentPaletteName));
        OnPropertyChanged(nameof(CurrentPaletteLabel));
        OnPropertyChanged(nameof(CurrentLanguageName));
        Sheet.LanguageChanged();
        Admin.LanguageChanged();
        if (IsConnected && !IsBusy) _ = (InAdmin ? ShowAdminCommand.ExecuteAsync(null) : LoadCommand.ExecuteAsync(null));
    }

    partial void OnSidebarExpandedChanged(bool value) => EmployeePreferences.SaveSidebarExpanded(value);

    partial void OnThemeModeChanged(string value)
    {
        ThemeManager.Apply(value, Palette);
        if (_applyingServerAppearance) return;
        EmployeePreferences.SaveThemeMode(value);
        _ = SavePreferencesAsync(new PreferencesRequest(value, null));
    }

    partial void OnPaletteChanged(string value)
    {
        foreach (var option in PaletteOptions) option.IsSelected = option.Key == value;
        ThemeManager.Apply(ThemeMode, value);
        if (_applyingServerAppearance) return;
        EmployeePreferences.SavePalette(value);
        _ = SavePreferencesAsync(new PreferencesRequest(null, value));
    }

    /// <summary>The choice follows the person to other computers; failing to save it must never disturb their work</summary>
    private async Task SavePreferencesAsync(PreferencesRequest request)
    {
        try
        {
            if (IsConnected) await _client.SetPreferencesAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "The look of the program could not be saved on the server");
        }
    }

    /// <summary>What the server holds for the person wins over what this computer remembered</summary>
    private void ApplyServerAppearance(string? mode, string? palette, string? language = null)
    {
        _applyingServerAppearance = true;
        try
        {
            if (ThemeManager.IsKnownMode(mode) && mode != ThemeMode) ThemeMode = mode!;
            if (ThemeManager.IsKnownPalette(palette) && palette != Palette) Palette = palette!;
            if (Loc.IsKnown(language) && language != Language) Language = language!;
            EmployeePreferences.SaveLanguage(Language);
            EmployeePreferences.SaveThemeMode(ThemeMode);
            EmployeePreferences.SavePalette(Palette);
        }
        finally
        {
            _applyingServerAppearance = false;
        }
    }

    // ---- Codes of the legend -------------------------------------------------------------------------------------------

    /// <summary>The codes the administrator keeps are the ones the rules and the entry form use; without an answer the known codes stay</summary>
    private async Task LoadTimeCodesAsync()
    {
        try
        {
            var codes = await _client.GetTimeCodesAsync();
            if (codes == null || codes.Count == 0) return;

            TimeCodes.Configure(codes.Select(c => new TimeCode(
                c.Code,
                Enum.TryParse<CodeColour>(c.Colour, out var colour) ? colour : CodeColour.Green,
                Enum.TryParse<CodeUnit>(c.Unit, out var unit) ? unit : CodeUnit.Hours,
                c.IsActive)));
            TimeCodeNames.Configure(codes.Select(c => (c.Code, c.NameBs, c.NameEn)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The codes of the legend could not be read; the known ones stay");
        }
    }

    /// <summary>A new code: the dialog asks for it, the server adds it, and the list is shown again</summary>
    [RelayCommand]
    private async Task AddCodeAsync()
    {
        var result = await _dialogs.CodeEditAsync(new CodeEditViewModel(null));
        if (result == null) return;

        await RunAdminAsync(async () =>
        {
            var added = await _admin.AddCodeAsync(result);
            InfoText = Loc.F("Dodana šifra {0} · {1}. Zaposlenici je vide pri sljedećem otvaranju programa.", added.Code, added.NameBs);
            await Admin.RefreshAsync(_admin);
            await LoadTimeCodesAsync();
        });
    }

    [RelayCommand]
    private async Task EditCodeAsync(CodeRow? row)
    {
        if (row == null) return;

        var result = await _dialogs.CodeEditAsync(new CodeEditViewModel(row.Definition));
        if (result == null) return;

        await RunAdminAsync(async () =>
        {
            await _admin.UpdateCodeAsync(row.Code, result);
            InfoText = Loc.F("Sačuvana šifra {0}.", row.Code);
            await Admin.RefreshAsync(_admin);
            await LoadTimeCodesAsync();
        });
    }
}
