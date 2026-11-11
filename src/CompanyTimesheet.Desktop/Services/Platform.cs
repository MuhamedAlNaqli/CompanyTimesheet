using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Models;
using Microsoft.Extensions.Logging;

namespace CompanyTimesheet.Desktop.Services;

/// <summary>The file pickers of the window</summary>
public interface IPlatformService
{
    Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, Dictionary<string, List<string>> fileTypes);

    Task<string?> ShowOpenFileDialogAsync(string title, Dictionary<string, List<string>> fileTypes);
}

public sealed class PlatformService : IPlatformService
{
    private readonly ILogger<PlatformService> _logger;

    public PlatformService(ILogger<PlatformService> logger) => _logger = logger;

    public async Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, Dictionary<string, List<string>> fileTypes)
    {
        try
        {
            var storage = GetTopLevel()?.StorageProvider;
            if (storage == null) return null;

            var result = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = defaultFileName,
                FileTypeChoices = Convert(fileTypes)
            });
            return result?.Path.LocalPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The save dialog failed");
            return null;
        }
    }

    public async Task<string?> ShowOpenFileDialogAsync(string title, Dictionary<string, List<string>> fileTypes)
    {
        try
        {
            var storage = GetTopLevel()?.StorageProvider;
            if (storage == null) return null;

            var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = Convert(fileTypes) });
            return result.FirstOrDefault()?.Path.LocalPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The open dialog failed");
            return null;
        }
    }

    private static TopLevel? GetTopLevel() =>
        App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null
            ? TopLevel.GetTopLevel(desktop.MainWindow)
            : null;

    private static List<FilePickerFileType> Convert(Dictionary<string, List<string>> fileTypes) =>
        fileTypes.Select(t => new FilePickerFileType(t.Key) { Patterns = t.Value.Select(x => x.Contains('*') ? x : "*." + x.TrimStart('.')).ToArray() }).ToList();
}

/// <summary>The yes / no question of the window, in Bosnian (the standard buttons of the library are English)</summary>
public sealed class DialogService
{
    private readonly ILogger<DialogService> _logger;

    public DialogService(ILogger<DialogService> logger) => _logger = logger;

    public async Task<bool> ShowConfirmDialogAsync(string title, string message)
    {
        try
        {
            var owner = (App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner == null) return false;

            var box = MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams
            {
                ContentTitle = title,
                ContentMessage = message,
                Icon = MsBox.Avalonia.Enums.Icon.Question,
                ButtonDefinitions = new[] { new ButtonDefinition { Name = "Da", IsDefault = true }, new ButtonDefinition { Name = "Ne", IsCancel = true } },
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            });
            return await box.ShowWindowDialogAsync(owner) == "Da";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The confirmation dialog failed");
            return false;
        }
    }
}
