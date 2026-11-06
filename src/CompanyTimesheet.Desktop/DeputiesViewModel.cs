using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Company.Api.Client;
using Company.Api.Contracts;

namespace CompanyTimesheet.Desktop;

/// <summary>Who stands in for the person while they are away, and whom they stand in for; with the form to choose a deputy</summary>
public sealed partial class DeputiesViewModel : ObservableObject
{
    public ObservableCollection<DeputyRow> Mine { get; } = new();

    public ObservableCollection<DeputyRow> ForOthers { get; } = new();

    public IReadOnlyList<PersonOption> Candidates { get; private set; } = Array.Empty<PersonOption>();

    [ObservableProperty] private PersonOption? _selectedCandidate;
    [ObservableProperty] private DateTime? _from = DateTime.Today;
    [ObservableProperty] private DateTime? _to = DateTime.Today.AddDays(7);

    public bool HasMine => Mine.Count > 0;

    public bool HasForOthers => ForOthers.Count > 0;

    public void Clear()
    {
        Mine.Clear();
        ForOthers.Clear();
        Candidates = Array.Empty<PersonOption>();
        SelectedCandidate = null;
        OnPropertyChanged(nameof(Candidates));
        OnPropertyChanged(nameof(HasMine));
        OnPropertyChanged(nameof(HasForOthers));
    }

    public async Task RefreshAsync(ICompanyApiClient client)
    {
        var deputies = await client.GetDeputiesAsync();
        if (Candidates.Count == 0) Candidates = (await client.GetDeputyCandidatesAsync()).Select(c => new PersonOption(c.Code, c.Name)).ToList();
        OnPropertyChanged(nameof(Candidates));

        Mine.Clear();
        foreach (var d in deputies.Mine) Mine.Add(new DeputyRow(d, forOthers: false));
        ForOthers.Clear();
        foreach (var d in deputies.ForOthers) ForOthers.Add(new DeputyRow(d, forOthers: true));
        OnPropertyChanged(nameof(HasMine));
        OnPropertyChanged(nameof(HasForOthers));
    }

    /// <summary>The request the form describes, or the reason it cannot be sent</summary>
    public (AddDeputyRequest? Request, string? Problem) ToRequest()
    {
        if (SelectedCandidate == null) return (null, Loc.T("Izaberite zamjenu."));
        if (From == null || To == null) return (null, Loc.T("Izaberite period zamjene."));
        if (To < From) return (null, Loc.T("Kraj perioda je prije početka."));
        return (new AddDeputyRequest(SelectedCandidate.Code, DateOnly.FromDateTime(From.Value), DateOnly.FromDateTime(To.Value)), null);
    }
}
