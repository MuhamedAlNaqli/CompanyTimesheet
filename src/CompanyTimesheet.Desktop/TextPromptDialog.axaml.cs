using System;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CompanyTimesheet.Desktop;

public partial class TextPromptDialog : Window
{
    public TextPromptDialog()
    {
        InitializeComponent();
    }
}

/// <summary>Asks for one text, for example the reason a timesheet is returned or the answer to it</summary>
public partial class TextPromptViewModel : ObservableObject, IDialogResult<string?>
{
    public TextPromptViewModel(string title, string label, string confirmText, string? quote = null)
    {
        Title = title;
        Label = label;
        ConfirmText = confirmText;
        Quote = quote ?? "";
    }

    public string Title { get; }

    public string Label { get; }

    public string ConfirmText { get; }

    /// <summary>The text being answered, shown above the input</summary>
    public string Quote { get; }

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _error = "";

    public string? Result { get; private set; }

    public event Action? RequestClose;

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            Error = "Upišite tekst.";
            return;
        }

        Result = Text.Trim();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();
}
