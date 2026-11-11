using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Company.Api.Contracts;

namespace CompanyTimesheet.Desktop;

/// <summary>What a colour of the legend means, in words the administrator knows from the sheet</summary>
public static class CodeKinds
{
    public static string ColourText(string colour) => colour switch
    {
        "Green" => Loc.T("Zeleno: redovan rad i odsustva (u fondu sati)"),
        "Blue" => Loc.T("Plavo: državni praznik (u fondu sati)"),
        "Orange" => Loc.T("Narandžasto: rad praznikom i noćni rad (u fondu sati)"),
        "Red" => Loc.T("Crveno: prekovremeni rad (nije u fondu sati)"),
        _ => colour
    };

    public static string ColourShort(string colour) => colour switch
    {
        "Green" => Loc.T("Zeleno"),
        "Blue" => Loc.T("Plavo"),
        "Orange" => Loc.T("Narandžasto"),
        "Red" => Loc.T("Crveno"),
        _ => colour
    };

    public static string UnitText(string unit) => unit switch
    {
        "Hours" => Loc.T("Upisuju se sati"),
        "Day" => Loc.T("Upisuje se cijeli dan (računa se kao 8 h)"),
        _ => unit
    };

    public static IBrush ColourBrush(string colour) => new SolidColorBrush(Color.Parse(colour switch
    {
        "Green" => "#16A34A",
        "Blue" => "#2563EB",
        "Orange" => "#F59E0B",
        "Red" => "#DC2626",
        _ => "#94A3B8"
    }));
}

/// <summary>One code of the legend in the administrator's list</summary>
public sealed class CodeRow
{
    public CodeRow(CodeDefinitionDto definition)
    {
        Definition = definition;
        Code = definition.Code;
        Name = definition.NameBs;
        ColourShort = CodeKinds.ColourShort(definition.Colour);
        UnitShort = definition.Unit == "Day" ? Loc.T("Cijeli dan") : Loc.T("Sati");
        ColourBrush = CodeKinds.ColourBrush(definition.Colour);
        StatusText = definition.IsActive ? Loc.T("Aktivna") : Loc.T("Isključena");
        OriginText = definition.IsSystem ? Loc.T("Sistemska") : "Dodana";
        SearchKey = $"{definition.Code} {definition.NameBs} {definition.NameEn}".ToLowerInvariant();
    }

    public CodeDefinitionDto Definition { get; }

    public int Code { get; }

    public string Name { get; }

    public string ColourShort { get; }

    public string UnitShort { get; }

    public IBrush ColourBrush { get; }

    public string StatusText { get; }

    public string OriginText { get; }

    public bool IsActive => Definition.IsActive;

    /// <summary>A code of the program's own rules, or one that sheets use, stays; it can be switched off instead</summary>
    public bool CanDelete => !Definition.IsSystem && !Definition.InUse;

    public string SearchKey { get; }
}

/// <summary>A choice in a list of the code dialog: the value the server knows and the words the administrator reads</summary>
public sealed record KindOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The dialog in which the administrator adds a code of the legend (no definition) or changes one</summary>
public sealed partial class CodeEditViewModel : ObservableObject, IDialogResult<SaveCodeRequest?>
{
    private readonly CodeDefinitionDto? _definition;

    public CodeEditViewModel(CodeDefinitionDto? definition)
    {
        _definition = definition;
        IsNew = definition == null;
        Title = IsNew ? Loc.T("Nova šifra") : $"Šifra {definition!.Code}";

        // A new code cannot be overtime: the program works overtime out from the intervals. The overtime codes that exist can be renamed.
        var colours = new[] { "Green", "Blue", "Orange" }.Concat(definition?.Colour == "Red" ? new[] { "Red" } : Array.Empty<string>());
        Colours = colours.Select(c => new KindOption(c, CodeKinds.ColourText(c))).ToList();
        Units = new[] { "Hours", "Day" }.Select(u => new KindOption(u, CodeKinds.UnitText(u))).ToList();

        CodeText = definition?.Code.ToString() ?? "";
        NameBs = definition?.NameBs ?? "";
        NameEn = definition?.NameEn ?? "";
        IsActive = definition?.IsActive ?? true;
        Colour = Colours.First(c => c.Value == (definition?.Colour ?? "Green"));
        Unit = Units.First(u => u.Value == (definition?.Unit ?? "Hours"));

        CanChangeMeaning = IsNew || (!definition!.IsSystem && !definition.InUse);
        CanSwitchOff = IsNew || !definition!.IsSystem;
        Note = IsNew
            ? Loc.T("Šifra je broj od 1000 do 9999 koji još ne postoji. Boja i vrsta unosa određuju kako se šifra računa u fondu sati i ne mogu se mijenjati kad je neka šihtarica već upotrijebi.")
            : definition!.IsSystem
                ? Loc.T("Ovu šifru program koristi u svojim pravilima: može se preimenovati, ali ne i isključiti ni promijeniti joj značenje.")
                : definition.InUse
                    ? Loc.T("Šihtarice već koriste ovu šifru, pa joj se ne mogu mijenjati boja i vrsta unosa. Možete je preimenovati ili isključiti; stari unosi ostaju.")
                    : "";
    }

    public string Title { get; }

    public bool IsNew { get; }

    public bool IsExisting => !IsNew;

    public IReadOnlyList<KindOption> Colours { get; }

    public IReadOnlyList<KindOption> Units { get; }

    /// <summary>Colour and unit change only for a code that no sheet uses and that the program does not rely on</summary>
    public bool CanChangeMeaning { get; }

    public bool CanSwitchOff { get; }

    public string Note { get; }

    public bool HasNote => Note.Length > 0;

    [ObservableProperty] private string _codeText = "";
    [ObservableProperty] private string _nameBs = "";
    [ObservableProperty] private string _nameEn = "";
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private KindOption? _colour;
    [ObservableProperty] private KindOption? _unit;
    [ObservableProperty] private string _error = "";

    public SaveCodeRequest? Result { get; private set; }

    public event Action? RequestClose;

    [RelayCommand]
    private void Save()
    {
        int code;
        if (IsNew)
        {
            if (!int.TryParse(CodeText.Trim(), out code) || code < 1000 || code > 9999) { Error = Loc.T("Šifra je broj od 1000 do 9999."); return; }
        }
        else
        {
            code = _definition!.Code;
        }

        if (string.IsNullOrWhiteSpace(NameBs)) { Error = Loc.T("Naziv ne smije biti prazan."); return; }

        Result = new SaveCodeRequest(code, NameBs.Trim(), NameEn.Trim(), Colour!.Value, Unit!.Value, IsActive);
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
