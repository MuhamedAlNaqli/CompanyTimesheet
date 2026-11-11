using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Company.Api.Client;
using Company.Api.Contracts;

namespace CompanyTimesheet.Desktop;

// The sections of the administration: what the side bar opens, and the actions of the holidays
public partial class EmployeeViewModel
{
    /// <summary>The side bar of the administrator opens a section: the overview, the people, the codes, the holidays or the audit log</summary>
    [RelayCommand]
    private async Task OpenAdminSectionAsync(string? section)
    {
        if (section is not (AdminViewModel.OverviewSection or AdminViewModel.PeopleSection or AdminViewModel.CodesSection
            or AdminViewModel.HolidaysSection or AdminViewModel.AuditSection)) return;

        Admin.Section = section;
        InfoText = "";
        ErrorText = "";

        // The people and the codes were read when the administration opened; the others show figures that move, so they are read again
        if (section is AdminViewModel.OverviewSection or AdminViewModel.AuditSection or AdminViewModel.HolidaysSection)
            await RunAdminAsync(() => Admin.LoadExtrasAsync(_admin));
    }

    /// <summary>The administrator who also approves: the window of the timesheets of the people below, with the list of what waits for a decision</summary>
    [RelayCommand]
    private async Task OpenTeamWorkAsync()
    {
        if (!CanSwitchWork) return;
        AdminShowsTeam = true;
        InAdmin = false;
        await ShowApprovalsAsync();
    }

    /// <summary>Back to the administration</summary>
    [RelayCommand]
    private async Task OpenAdminWorkAsync()
    {
        InApprovals = false;
        InTeam = false;
        IsReviewing = false;
        _reviewCode = null;
        AdminShowsTeam = false;
        InAdmin = true;
        ClearSheet();
        await OpenAdminSectionAsync(AdminViewModel.OverviewSection);
    }

    /// <summary>A new temporary password for a person who forgot theirs; the person changes it at the next sign-in</summary>
    [RelayCommand]
    private async Task ResetPasswordAsync(AdminRow? row)
    {
        if (row == null || !row.HasUsername) return;
        if (!await _dialogs.ConfirmAsync(Loc.T("Nova lozinka"), Loc.F("Postaviti novu privremenu lozinku za {0}? Stara lozinka prestaje važiti, a osoba se odjavljuje.", row.Name))) return;

        await RunAdminAsync(async () =>
        {
            var result = await _admin.ResetPasswordAsync(row.Code);
            await Admin.RefreshAsync(_admin);
            InfoText = result.EmailSent
                ? Loc.F("Privremena lozinka za {0}: {1}. Poslana je i na {2}; osoba je mijenja pri prvoj prijavi.", row.Name, result.TemporaryPassword, result.Email ?? "")
                : Loc.F("Privremena lozinka za {0}: {1}. Prenesite je osobi; ona je mijenja pri prvoj prijavi.", row.Name, result.TemporaryPassword);
        });
    }

    [RelayCommand]
    private Task ChangeHolidayYearAsync(string? delta) => RunAdminAsync(async () =>
    {
        if (!int.TryParse(delta, out var step)) return;
        var year = Admin.HolidayYear + step;
        if (year < 2000 || year > 2100) return;

        Admin.HolidayYear = year;
        await Admin.LoadHolidaysAsync(_admin);
    });

    [RelayCommand]
    private Task AddHolidayAsync() => RunAdminAsync(async () =>
    {
        var name = Admin.NewHolidayName.Trim();
        if (Admin.NewHolidayDate is not { } date) { ErrorText = Loc.T("Izaberite datum praznika."); return; }
        if (name.Length == 0) { ErrorText = Loc.T("Upišite naziv praznika."); return; }

        var holiday = new HolidayDto(DateOnly.FromDateTime(date.Date), name);
        var editing = Admin.EditingHolidayDate;

        // A holiday moved to another day: the old day goes first, so a failed save does not leave both
        await _admin.SaveHolidayAsync(holiday);
        if (editing is { } old && old != holiday.Date) await _admin.DeleteHolidayAsync(old);

        // The list shows the year of the holiday that was added
        Admin.HolidayYear = holiday.Date.Year;
        Admin.NewHolidayName = "";
        Admin.EditingHolidayDate = null;
        await Admin.LoadExtrasAsync(_admin);
        InfoText = editing != null
            ? Loc.F("Sačuvan praznik: {0} ({1}).", name, EmployeeFormat.Date(holiday.Date))
            : Loc.F("Dodan praznik: {0} ({1}).", name, EmployeeFormat.Date(holiday.Date));
    });

    /// <summary>An added holiday goes back into the form above the list; saving it replaces the old one</summary>
    [RelayCommand]
    private void EditHoliday(HolidayRow? row)
    {
        if (row == null || row.IsBuiltIn) return;

        Admin.EditingHolidayDate = row.Date;
        Admin.NewHolidayDate = new DateTimeOffset(row.Date.ToDateTime(TimeOnly.MinValue));
        Admin.NewHolidayName = row.Name;
        InfoText = Loc.F("Mijenjate praznik {0}. Promijenite datum ili naziv iznad liste i sačuvajte.", row.Name);
        ErrorText = "";
    }

    [RelayCommand]
    private void CancelHolidayEdit()
    {
        Admin.EditingHolidayDate = null;
        Admin.NewHolidayName = "";
        InfoText = "";
    }

    [RelayCommand]
    private async Task DeleteCodeAsync(CodeRow? row)
    {
        if (row == null || !row.CanDelete) return;
        if (!await _dialogs.ConfirmAsync(Loc.T("Obriši šifru"), Loc.F("Obrisati šifru {0} · {1}? Ovo se ne može poništiti.", row.Code, row.Name))) return;

        await RunAdminAsync(async () =>
        {
            await _admin.DeleteCodeAsync(row.Code);
            await Admin.RefreshAsync(_admin);
            await LoadTimeCodesAsync();
            InfoText = Loc.F("Obrisana šifra {0}.", row.Code);
        });
    }

    [RelayCommand]
    private async Task RemoveHolidayAsync(HolidayRow? row)
    {
        if (row == null) return;
        if (!await _dialogs.ConfirmAsync(Loc.T("Ukloni praznik"), Loc.F("Ukloniti praznik {0} ({1})? Mjesečni fond sati se računa bez njega.", row.Name, row.DateText))) return;

        await RunAdminAsync(async () =>
        {
            await _admin.DeleteHolidayAsync(row.Date);
            if (Admin.EditingHolidayDate == row.Date) Admin.EditingHolidayDate = null;
            await Admin.LoadExtrasAsync(_admin);
            InfoText = Loc.F("Uklonjen praznik: {0}.", row.Name);
        });
    }
}
