using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Company.Api.Contracts;
using Timesheet.Domain;

namespace CompanyTimesheet.Desktop;

/// <summary>A dialog view model: the result is set before closing; null means cancelled</summary>
public interface IDialogResult<T>
{
    event Action? RequestClose;

    T? Result { get; }
}

public static class EmployeeDialogs
{
    public static async Task<TResult?> ShowAsync<TWindow, TResult>(TWindow window, IDialogResult<TResult> viewModel) where TWindow : Window
    {
        var owner = (App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner == null) return default;

        viewModel.RequestClose += () => window.Close();
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        await window.ShowDialog(owner);
        return viewModel.Result;
    }
}

/// <summary>What the form of the entry asks for: hours, a range of days, or a start and end time</summary>
public enum EntryKind
{
    Hours,
    Absence,
    Overtime
}

/// <summary>A code the employee can pick; the form changes with it</summary>
public sealed record CodeOption(TimeCode Code)
{
    public string Label => $"{Code.Code} · {TimeCodeNames.Bs(Code.Code)}";

    public bool NeedsHours => Code.Unit == CodeUnit.Hours;

    public EntryKind Kind => Code.IsOvertime ? EntryKind.Overtime : Code.IsAbsence ? EntryKind.Absence : EntryKind.Hours;

    public override string ToString() => Label;
}

/// <summary>What the entry dialog produced: one of the three, depending on the code chosen</summary>
public sealed record EntryResult(SetEntryRequest? Day, DateOnly? DayDate, AbsenceRangeRequest? Absence, OvertimeRequest? Overtime, int? OvertimeId);

/// <summary>
/// One dialog for everything the employee writes into the month. The code decides the form: a code of hours asks for the
/// hours of a day, an absence code asks for the days it covers (and shows them before saving), an overtime code asks for the
/// date, the start and the end (and shows the total and the split into codes while typing, from the rules the server uses).
/// </summary>
public partial class EntryViewModel : ObservableObject, IDialogResult<EntryResult?>
{
    private readonly Func<DateOnly, DayType> _dayTypeOf;
    private readonly Func<AbsenceRangeRequest, Task<AbsencePreviewDto>> _preview;
    private readonly int _year;
    private readonly int _month;
    private readonly int? _overtimeId;

    public EntryViewModel(int year, int month, Func<DateOnly, DayType> dayTypeOf, Func<AbsenceRangeRequest, Task<AbsencePreviewDto>> preview,
        DateOnly? date = null, EntryDto? entry = null, OvertimeDto? overtime = null)
    {
        _year = year;
        _month = month;
        _dayTypeOf = dayTypeOf;
        _preview = preview;
        _overtimeId = overtime?.Id;

        // Changing an overtime record keeps it an overtime record; everything else may become anything
        Codes = TimeCodes.All.Where(c => overtime == null || c.IsOvertime).Select(c => new CodeOption(c)).ToList();
        CanChangeDate = entry == null;
        Title = overtime != null ? "Izmjena prekovremenog rada" : entry != null ? "Izmjena unosa" : "Novi unos";

        var day = overtime?.Date ?? date ?? (DateTime.Today.Year == year && DateTime.Today.Month == month
            ? DateOnly.FromDateTime(DateTime.Today) : new DateOnly(year, month, 1));
        var code = overtime != null ? (overtime.Allocations.FirstOrDefault()?.Code ?? Timesheet.Domain.Codes.Overtime) : entry?.Code ?? Timesheet.Domain.Codes.RegularWork;

        _selectedCode = Codes.FirstOrDefault(c => c.Code.Code == code) ?? Codes[0];
        _date = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue));
        _to = _date;
        _hoursText = entry is { Minutes: > 0 } ? (entry.Minutes / 60m).ToString("0.##", EmployeeFormat.Bosnian) : "8";
        _startText = overtime?.Start.ToString("HH\\:mm") ?? "17:00";
        _endText = overtime?.End.ToString("HH\\:mm") ?? "19:00";
        _description = overtime?.Description ?? "";
        Recalculate();
    }

    public string Title { get; }

    public IReadOnlyList<CodeOption> Codes { get; }

    /// <summary>The date of an existing entry stays; a new entry or an overtime record can move</summary>
    public bool CanChangeDate { get; }

    public DateTimeOffset MinDate => new(new DateTime(_year, _month, 1));

    public DateTimeOffset MaxDate => new(new DateTime(_year, _month, DateTime.DaysInMonth(_year, _month)));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Kind), nameof(IsHours), nameof(IsAbsence), nameof(IsOvertime), nameof(DateLabel), nameof(ShowHint), nameof(HintText))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private CodeOption? _selectedCode;

    [ObservableProperty] private DateTimeOffset? _date;
    [ObservableProperty] private DateTimeOffset? _to;
    [ObservableProperty] private string _hoursText;
    [ObservableProperty] private string _startText;
    [ObservableProperty] private string _endText;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _warningText = "";
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _error = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _previewed;

    public EntryKind Kind => SelectedCode?.Kind ?? EntryKind.Hours;

    public bool IsHours => Kind == EntryKind.Hours;

    public bool IsAbsence => Kind == EntryKind.Absence;

    public bool IsOvertime => Kind == EntryKind.Overtime;

    public string DateLabel => IsAbsence ? "Od" : "Datum";

    public bool ShowHint => Kind != EntryKind.Overtime || SelectedCode != null;

    public string HintText => Kind switch
    {
        EntryKind.Absence => "Šifra odsustva se upisuje za cijeli dan; jedan dan se računa kao 8 sati. Vikendi i praznici se preskaču. Prvo pogledajte dane, zatim sačuvajte.",
        EntryKind.Overtime => "Šifra prekovremenog rada se određuje prema datumu i vremenu (radni dan, vikend, praznik, noć). Ukupno se računa iz početka i kraja; rad poslije ponoći upisuje se na sljedeći datum.",
        _ => "Upišite sate za ovaj dan, na primjer 8, 7,5 ili 7:30."
    };

    partial void OnSelectedCodeChanged(CodeOption? value)
    {
        Invalidate();
        Recalculate();
    }

    partial void OnDateChanged(DateTimeOffset? value)
    {
        if (To == null || (value != null && To < value)) To = value;
        Invalidate();
        Recalculate();
    }

    partial void OnToChanged(DateTimeOffset? value) => Invalidate();

    partial void OnStartTextChanged(string value) => Recalculate();

    partial void OnEndTextChanged(string value) => Recalculate();

    private void Invalidate()
    {
        Previewed = false;
        PreviewText = "";
    }

    public EntryResult? Result { get; private set; }

    public event Action? RequestClose;

    /// <summary>The total and the split into codes while the employee types; the server checks again</summary>
    private void Recalculate()
    {
        SummaryText = "";
        WarningText = "";
        if (!IsOvertime || Date == null || !EmployeeFormat.TryParseTime(StartText, out var start) || !EmployeeFormat.TryParseTime(EndText, out var end)) return;

        var interval = new OvertimeInterval(DateOnly.FromDateTime(Date.Value.Date), start, end);
        if (interval.TotalMinutes == 0)
        {
            WarningText = "Početak i kraj ne smiju biti isti.";
            return;
        }

        var result = OvertimeClassifier.Classify(interval, _dayTypeOf);
        var parts = result.Allocations.Select(a => $"{a.Code} {TimeCodeNames.Bs(a.Code)}: {EmployeeFormat.Hours(a.Minutes)}" +
                                                    (a.Date != interval.Date ? $" (za {EmployeeFormat.ShortDate(a.Date)})" : ""));
        SummaryText = $"Ukupno {EmployeeFormat.Hours(result.TotalMinutes)}" + (result.Allocations.Count > 0 ? " — " + string.Join("; ", parts) : "");
        if (result.Conflicts.Count > 0)
            WarningText = "Prekovremeni rad se ne može upisati u redovno radno vrijeme (08:30–17:00) radnog dana ili praznika.";
    }

    private bool CanSave => !IsAbsence || Previewed;

    private AbsenceRangeRequest? BuildAbsence()
    {
        if (SelectedCode == null || Date == null || To == null) return null;
        return new AbsenceRangeRequest(SelectedCode.Code.Code, DateOnly.FromDateTime(Date.Value.Date), DateOnly.FromDateTime(To.Value.Date));
    }

    [RelayCommand]
    private async Task ShowPreviewAsync()
    {
        Error = "";
        var request = BuildAbsence();
        if (request == null) return;
        if (request.To < request.From)
        {
            Error = "Kraj ne može biti prije početka.";
            return;
        }

        if (!InMonth(request.From) || !InMonth(request.To))
        {
            Error = "Odsustvo se upisuje unutar mjeseca koji je otvoren.";
            return;
        }

        try
        {
            var preview = await _preview(request);
            if (preview.Days.Count == 0)
            {
                PreviewText = "U ovom periodu nema dana na koje se šifra upisuje (vikendi i praznici se preskaču).";
                Previewed = false;
                return;
            }

            var skipped = preview.Skipped.Count == 0
                ? ""
                : "\nPreskočeno: " + string.Join(", ", preview.Skipped.Select(s => $"{EmployeeFormat.ShortDate(s.Date)} ({BosnianTexts.SkippedBecause(s.Reason)})"));
            PreviewText = $"Šifra {request.Code} bit će upisana za {preview.Days.Count} {(preview.Days.Count == 1 ? "dan" : "dana")}: " +
                          string.Join(", ", preview.Days.Select(EmployeeFormat.ShortDate)) + skipped;
            Previewed = true;
        }
        catch (Company.Api.Client.ApiException ex)
        {
            Error = ex.Message;
        }
    }

    private bool InMonth(DateOnly date) => date.Year == _year && date.Month == _month;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        Error = "";
        if (SelectedCode == null || Date == null)
        {
            Error = "Odaberite šifru i datum.";
            return;
        }

        var date = DateOnly.FromDateTime(Date.Value.Date);
        if (!InMonth(date))
        {
            Error = "Datum mora biti u mjesecu koji je otvoren.";
            return;
        }

        switch (Kind)
        {
            case EntryKind.Absence:
                Result = new EntryResult(null, null, BuildAbsence(), null, null);
                break;

            case EntryKind.Overtime:
                if (!EmployeeFormat.TryParseTime(StartText, out var start) || !EmployeeFormat.TryParseTime(EndText, out var end))
                {
                    Error = "Upišite vrijeme kao 17:00.";
                    return;
                }

                if (new OvertimeInterval(date, start, end).TotalMinutes == 0)
                {
                    Error = "Početak i kraj ne smiju biti isti.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(Description))
                {
                    Error = "Kratak opis posla je obavezan: napišite šta je tačno rađeno.";
                    return;
                }

                Result = new EntryResult(null, null, null, new OvertimeRequest(date, start, end, Description.Trim()), _overtimeId);
                break;

            default:
                var minutes = 0;
                if (SelectedCode.NeedsHours && !EmployeeFormat.TryParseHours(HoursText, out minutes))
                {
                    Error = "Upišite sate, na primjer 8, 7,5 ili 7:30 (najviše 24).";
                    return;
                }

                Result = new EntryResult(new SetEntryRequest(SelectedCode.Code.Code, minutes), date, null, null, null);
                break;
        }

        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
