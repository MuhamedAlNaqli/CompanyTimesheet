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

/// <summary>A palette on the side bar: its colour, and whether it is the one in use</summary>
public sealed partial class PaletteOption : ObservableObject
{
    public PaletteOption(PaletteInfo info, bool selected, Action<string>? select = null)
    {
        SelectCommand = new RelayCommand(() => select?.Invoke(info.Key));
        Key = info.Key;
        Name = info.Name;
        Swatch = new SolidColorBrush(Color.Parse(info.Swatch));
        _isSelected = selected;
    }

    public string Key { get; }

    public string Name { get; }

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
    [NotifyPropertyChangedFor(nameof(CurrentPaletteName), nameof(CurrentPaletteSwatch))]
    private string _palette = EmployeePreferences.LoadPalette();

    /// <summary>The side bar shows the names of its buttons; folded, only the icons</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    private bool _sidebarExpanded = EmployeePreferences.LoadSidebarExpanded();

    public double SidebarWidth => SidebarExpanded ? 224 : 60;

    public bool IsModeSystem => ThemeMode == "system";

    public bool IsModeLight => ThemeMode == "light";

    public bool IsModeDark => ThemeMode == "dark";

    /// <summary>The button of the side bar names the theme it switches to: dark while the look is light, light otherwise</summary>
    public string ThemeToggleText => IsModeDark ? "Svijetla tema" : "Tamna tema";

    public string CurrentPaletteName => ThemeManager.Palettes.FirstOrDefault(p => p.Key == Palette)?.Name ?? "";

    public IBrush CurrentPaletteSwatch => new SolidColorBrush(Color.Parse(ThemeManager.Palettes.FirstOrDefault(p => p.Key == Palette)?.Swatch ?? "#0F766E"));

    public IReadOnlyList<PaletteOption> PaletteOptions { get; private set; } = Array.Empty<PaletteOption>();

    private void InitAppearance()
    {
        if (!ThemeManager.IsKnownMode(ThemeMode)) ThemeMode = "light";
        if (!ThemeManager.IsKnownPalette(Palette)) Palette = ThemeManager.DefaultPalette;
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
    private void ApplyServerAppearance(string? mode, string? palette)
    {
        _applyingServerAppearance = true;
        try
        {
            if (ThemeManager.IsKnownMode(mode) && mode != ThemeMode) ThemeMode = mode!;
            if (ThemeManager.IsKnownPalette(palette) && palette != Palette) Palette = palette!;
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
            InfoText = $"Dodana šifra {added.Code} · {added.NameBs}. Zaposlenici je vide pri sljedećem otvaranju programa.";
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
            InfoText = $"Sačuvana šifra {row.Code}.";
            await Admin.RefreshAsync(_admin);
            await LoadTimeCodesAsync();
        });
    }
}
