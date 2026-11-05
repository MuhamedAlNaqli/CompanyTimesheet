using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using Company.Api.Client;
using CompanyTimesheet.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace CompanyTimesheet.Desktop;

// What the employee view model needs from the world around it, as small interfaces: the dialogs, the saving of files and the change of
// window. The program supplies windows and the file system; a test supplies answers.

/// <summary>The questions the employee window asks the person in front of it</summary>
public interface IEmployeeDialogs
{
    /// <summary>A yes or no question</summary>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>The dialog for a new or changed entry; null when it was cancelled</summary>
    Task<EntryResult?> EntryAsync(EntryViewModel viewModel);

    /// <summary>A text (the reason of a return, an answer); null when it was cancelled</summary>
    Task<string?> PromptAsync(TextPromptViewModel viewModel);

    /// <summary>The dialog in which the administrator changes an employee; null when it was cancelled</summary>
    Task<AdminEditResult?> AdminEditAsync(AdminEmployeeViewModel viewModel);

    /// <summary>The dialog in which the administrator adds or changes a code of the legend; null when it was cancelled</summary>
    Task<Company.Api.Contracts.SaveCodeRequest?> CodeEditAsync(CodeEditViewModel viewModel);

    /// <summary>The person chooses a CSV file; its text, or null when they cancelled</summary>
    Task<string?> PickCsvAsync();
}

public sealed class WindowEmployeeDialogs : IEmployeeDialogs
{
    private readonly DialogService _dialogs;
    private readonly IPlatformService _platform;

    public WindowEmployeeDialogs(DialogService dialogs, IPlatformService platform)
    {
        _dialogs = dialogs;
        _platform = platform;
    }

    public Task<AdminEditResult?> AdminEditAsync(AdminEmployeeViewModel viewModel) =>
        EmployeeDialogs.ShowAsync<AdminEmployeeDialog, AdminEditResult?>(new AdminEmployeeDialog { DataContext = viewModel }, viewModel);

    public Task<Company.Api.Contracts.SaveCodeRequest?> CodeEditAsync(CodeEditViewModel viewModel) =>
        EmployeeDialogs.ShowAsync<CodeDialog, Company.Api.Contracts.SaveCodeRequest?>(new CodeDialog { DataContext = viewModel }, viewModel);

    public async Task<string?> PickCsvAsync()
    {
        var path = await _platform.ShowOpenFileDialogAsync("Izaberite listu radnika (CSV)", new Dictionary<string, List<string>> { ["CSV"] = new() { "csv", "txt" } });
        return path == null ? null : await File.ReadAllTextAsync(path);
    }

    public Task<bool> ConfirmAsync(string title, string message) => _dialogs.ShowConfirmDialogAsync(title, message);

    public Task<EntryResult?> EntryAsync(EntryViewModel viewModel) =>
        EmployeeDialogs.ShowAsync<EntryDialog, EntryResult?>(new EntryDialog { DataContext = viewModel }, viewModel);

    public Task<string?> PromptAsync(TextPromptViewModel viewModel) =>
        EmployeeDialogs.ShowAsync<TextPromptDialog, string?>(new TextPromptDialog { DataContext = viewModel }, viewModel);
}

/// <summary>What came of asking for a file and saving it: where it went, or what went wrong. Neither when the person cancelled.</summary>
public sealed record FileSaveResult(string? Path, string? Error)
{
    public static FileSaveResult Cancelled { get; } = new(null, null);
}

/// <summary>Gets a printed sheet from the server and saves it where the person says</summary>
public interface ISheetFileSaver
{
    Task<FileSaveResult> SaveAsync(string? format, Func<string, Task<DownloadedFile>> download);
}

public sealed class SheetFileSaver : ISheetFileSaver
{
    private readonly IPlatformService _platform;
    private readonly ILogger<SheetFileSaver> _logger;

    public SheetFileSaver(IPlatformService platform, ILogger<SheetFileSaver> logger)
    {
        _platform = platform;
        _logger = logger;
    }

    public async Task<FileSaveResult> SaveAsync(string? format, Func<string, Task<DownloadedFile>> download)
    {
        format = string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase) ? "pdf" : "xlsx";

        try
        {
            var file = await download(format);

            var types = format == "pdf"
                ? new Dictionary<string, List<string>> { ["PDF"] = new() { "pdf" } }
                : new Dictionary<string, List<string>> { ["Excel"] = new() { "xlsx" } };
            var path = await _platform.ShowSaveFileDialogAsync("Sačuvaj šihtaricu", file.FileName, types);
            if (path == null) return FileSaveResult.Cancelled;

            await File.WriteAllBytesAsync(path, file.Bytes);
            return new FileSaveResult(path, null);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "The printed sheet was refused ({Code})", ex.Code);
            return new FileSaveResult(null, ex.Code == "no-team" ? "Nikome nisu dodijeljeni vaši nadzor ni odobravanje." : ex.Message);
        }
        catch (Exception ex)
        {
            // A garbled answer, a full disk, a file that is open elsewhere: all the same to the person
            _logger.LogWarning(ex, "The printed sheet could not be produced or saved");
            return new FileSaveResult(null, "Datoteka nije sačuvana: " + ex.Message);
        }
    }
}

/// <summary>Where the session token of a remembered sign-in is kept between runs (the reminder task needs it too)</summary>
public interface IEmployeeSignInStore
{
    string? Load();

    void Save(string token);

    void Clear();
}

/// <summary>The token in a file of the user's profile, protected so that only this Windows user can read it</summary>
public sealed class ProtectedSignInStore : IEmployeeSignInStore
{
    private static string FilePath => Path.Combine(Background.ReminderState.FolderPath, "employee-signin.dat");

    public string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var bytes = File.ReadAllBytes(FilePath);
            if (OperatingSystem.IsWindows()) bytes = System.Security.Cryptography.ProtectedData.Unprotect(bytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            // A damaged or foreign file is the same as no sign-in
            return null;
        }
    }

    public void Save(string token)
    {
        Directory.CreateDirectory(Background.ReminderState.FolderPath);
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        if (OperatingSystem.IsWindows()) bytes = System.Security.Cryptography.ProtectedData.Protect(bytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, bytes);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch (IOException)
        {
            // Left behind, it holds a token the server has already ended
        }
    }
}

/// <summary>What the window needs from the computer around it</summary>
public interface IEmployeeShell
{

    /// <summary>Puts a text on the clipboard</summary>
    Task CopyAsync(string text);
}

public sealed class EmployeeShell : IEmployeeShell
{
    public async Task CopyAsync(string text)
    {
        if (App.Current is App app && app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);

            // Hands the text to Windows itself, so it stays on the clipboard (and other programs can read it) whatever the app does next
            await clipboard.FlushAsync();
        }
    }
}

/// <summary>What is due for the person, from the server</summary>
public sealed class RemindersViewModel : ObservableObject
{
    public ObservableCollection<ReminderRow> Items { get; } = new();

    public bool HasAny => Items.Count > 0;

    public void Clear()
    {
        Items.Clear();
        OnPropertyChanged(nameof(HasAny));
    }

    public async Task RefreshAsync(ICompanyApiClient client)
    {
        var due = await client.GetRemindersAsync();
        Items.Clear();
        foreach (var item in due.Items.OrderBy(i => EmployeeFormat.ReminderOrder(i.Kind))) Items.Add(new ReminderRow(item));
        OnPropertyChanged(nameof(HasAny));
    }
}

/// <summary>The timesheets that wait for the person as an approver</summary>
public sealed class PendingListViewModel : ObservableObject
{
    private int _count;

    public ObservableCollection<PendingRow> Items { get; } = new();

    public int Count
    {
        get => _count;
        private set
        {
            if (SetProperty(ref _count, value)) OnPropertyChanged(nameof(TabText));
        }
    }

    public bool IsEmpty => Items.Count == 0;

    public string TabText => Count > 0 ? $"Za odobravanje ({Count})" : "Za odobravanje";

    public void Clear()
    {
        Items.Clear();
        Count = 0;
        OnPropertyChanged(nameof(IsEmpty));
    }

    public async Task RefreshAsync(ICompanyApiClient client)
    {
        var items = await client.GetPendingAsync();
        Items.Clear();
        foreach (var item in items) Items.Add(new PendingRow(item));
        Count = items.Count;
        OnPropertyChanged(nameof(IsEmpty));
    }
}
