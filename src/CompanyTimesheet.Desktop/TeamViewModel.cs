using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Company.Api.Client;
using Company.Api.Contracts;

namespace CompanyTimesheet.Desktop;

/// <summary>One person of the team tree: where they stand in the month, and whether they can be reminded</summary>
public sealed class TeamRow : ObservableObject
{
    private DateTime? _remindedUtc;

    public TeamRow(TeamMemberDto member)
    {
        Member = member;
        Name = member.Name;
        Indent = new Thickness(member.Depth * 30, 0, 0, 0);
        IsBelow = member.Depth > 0;
        _remindedUtc = member.RemindedUtc;

        (StatusText, StatusBrush) = member.Status switch
        {
            SheetStatus.Approved => ("Odobreno", Brush.Parse("#15803D")),
            SheetStatus.Submitted => (member.WaitingForName is { Length: > 0 } ? $"Predano, čeka: {member.WaitingForName}" : "Predano", Brush.Parse("#1D4ED8")),
            SheetStatus.Returned => ("Vraćeno na ispravku", Brush.Parse("#B45309")),
            _ => ("Nije predano", Brush.Parse("#B91C1C"))
        };
    }

    public TeamMemberDto Member { get; }

    public string Name { get; }

    public string Code => Member.Code;

    /// <summary>Space to the left, one step for each level of the tree</summary>
    public Thickness Indent { get; }

    /// <summary>Shows the branch arrow in front of a person who reports to somebody below the signed-in one</summary>
    public bool IsBelow { get; }

    public string StatusText { get; }

    public IBrush StatusBrush { get; }

    /// <summary>Not submitted: a reminder can be sent</summary>
    public bool CanRemind => Member.CanRemind;

    public bool HasReminder => _remindedUtc != null;

    public string ReminderText => _remindedUtc is { } at ? $"Podsjetnik poslan {at.ToLocalTime():dd.MM. u HH:mm}" : "";

    public void MarkReminded(DateTime sentUtc)
    {
        _remindedUtc = sentUtc;
        OnPropertyChanged(nameof(HasReminder));
        OnPropertyChanged(nameof(ReminderText));
    }
}

/// <summary>The people below the signed-in person as a tree, with the status of the month for each of them</summary>
public sealed partial class TeamViewModel : ObservableObject
{
    public ObservableCollection<TeamRow> Rows { get; } = new();

    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private bool _hasPeopleToRemind;

    /// <summary>The message for the approver is shown instead of the tree</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTree))]
    private bool _showSummary;

    /// <summary>The message as it goes to the approver: copied as it is</summary>
    [ObservableProperty] private string _summaryBody = "";

    /// <summary>Sheets in the message that are not approved yet, so the supervisor knows before sending</summary>
    [ObservableProperty] private string _summaryNote = "";

    public bool ShowTree => !ShowSummary;

    public bool HasSummaryNote => SummaryNote.Length > 0;

    partial void OnSummaryNoteChanged(string value) => OnPropertyChanged(nameof(HasSummaryNote));

    public async Task LoadSummaryAsync(ICompanyApiClient client, int year, int month)
    {
        var summary = await client.GetTeamSummaryTextAsync(year, month);
        SummaryBody = summary.Text;

        var open = summary.Groups.SelectMany(g => g.People).Where(p => p.Note != null).Select(p => p.Name).ToList();
        SummaryNote = open.Count == 0 ? "" : $"Još nisu odobrene šihtarice: {string.Join(", ", open)}.";
    }

    public bool IsEmpty => Rows.Count == 0;

    public void Clear()
    {
        Rows.Clear();
        SummaryText = "";
        SummaryBody = "";
        SummaryNote = "";
        HasPeopleToRemind = false;
        ShowSummary = false;
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Loads who has submitted the month. A person with nobody below them gets an empty tree, not an error.</summary>
    public async Task RefreshAsync(ICompanyApiClient client, int year, int month)
    {
        TeamStatusDto status;
        try
        {
            status = await client.GetTeamStatusAsync(year, month);
        }
        catch (ApiException ex) when (ex.Code == "no-team")
        {
            Rows.Clear();
            SummaryText = "";
            HasPeopleToRemind = false;
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        Rows.Clear();
        foreach (var member in status.Members) Rows.Add(new TeamRow(member));

        var open = status.Members.Count(m => m.Status is SheetStatus.Draft or SheetStatus.Returned);
        SummaryText = open == 0
            ? $"Sve šihtarice ({status.Members.Count}) su predane."
            : $"Nisu predali: {open} od {status.Members.Count}. Rok za predaju je {EmployeeFormat.Date(status.Deadline)}";
        HasPeopleToRemind = status.Members.Any(m => m.CanRemind);
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>The text for a refusal to remind, in the words of the person using the app</summary>
    public static string ReminderRefusal(ApiException ex, string name) => ex.Code switch
    {
        "already-reminded" => $"Podsjetnik za {name} je već poslan. Pokušajte kasnije.",
        "nothing-to-remind" => $"Za {name} nema šta podsjećati: šihtarica je predana ili mjesec još nije počeo.",
        "not-in-team" => $"{name} se ne javlja vama.",
        _ => ex.Message
    };
}
