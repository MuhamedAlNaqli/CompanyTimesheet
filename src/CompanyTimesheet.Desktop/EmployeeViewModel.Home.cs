using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Company.Api.Client;
using Company.Api.Contracts;
using Microsoft.Extensions.Logging;
using Timesheet.Domain;

namespace CompanyTimesheet.Desktop;

// The start screen of an employee: a few figures instead of the whole month at once
public partial class EmployeeViewModel
{
    /// <summary>The start screen is on screen (the first thing an employee sees after signing in)</summary>
    [ObservableProperty] private bool _inHome;

    public ObservableCollection<StatCard> HomeCards { get; } = new();

    public bool ShowHomeBody => IsConnected && InHome;

    partial void OnInHomeChanged(bool value) => RaiseModeChanged();

    [RelayCommand]
    private async Task ShowHomeAsync()
    {
        InApprovals = false;
        InTeam = false;
        InAdmin = false;
        InHistory = false;
        IsReviewing = false;
        _reviewCode = null;
        InHome = true;
        InfoText = "";
        await LoadHomeAsync();
    }

    /// <summary>Reads the figures; the start screen stays empty (and the rest of the program works) when they cannot be read</summary>
    private async Task LoadHomeAsync()
    {
        try
        {
            var overview = await _session.GetMyOverviewAsync();
            ShowHome(overview);
        }
        catch (Exception ex) when (ex is ApiException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "The start screen could not be filled");
        }
    }

    private void ShowHome(EmployeeOverviewDto overview)
    {
        HomeCards.Clear();
        var month = EmployeeFormat.Month(overview.Year, overview.Month);

        HomeCards.Add(new StatCard(Loc.F("Šihtarica za {0}", month), EmployeeFormat.Status(overview.Status),
            Loc.F("Mjesečni fond: {0} od {1}", EmployeeFormat.Hours(overview.FundMinutes), EmployeeFormat.Hours(overview.ExpectedFundMinutes)),
            overview.Status == SheetStatus.Returned ? "warn" : ""));
        HomeCards.Add(new StatCard(Loc.T("Prekovremeni rad ovaj mjesec"), EmployeeFormat.Hours(overview.MonthOvertimeMinutes), month, ""));
        HomeCards.Add(new StatCard(Loc.T("Prekovremeni rad ove godine"), EmployeeFormat.Hours(overview.YearOvertimeMinutes), overview.Year.ToString(), ""));
        HomeCards.Add(new StatCard(Loc.T("Radni sati ove godine"), EmployeeFormat.Hours(overview.YearWorkedMinutes), Loc.T("Redovan rad (šifra 1010)"), ""));
        HomeCards.Add(new StatCard(Loc.T("Predane šihtarice"), $"{overview.PastMonthsHandedIn} / {overview.PastMonths}",
            Loc.T("Prethodni mjeseci ove godine"), overview.PastMonthsHandedIn < overview.PastMonths ? "warn" : ""));
        if (IsApprover)
            HomeCards.Add(new StatCard(Loc.T("Čeka vaše odobrenje"), overview.PendingApprovals.ToString(),
                Loc.T("Šihtarice zaposlenika"), overview.PendingApprovals > 0 ? "warn" : ""));
    }
}
