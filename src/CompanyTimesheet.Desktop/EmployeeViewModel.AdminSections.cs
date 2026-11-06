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
        await _admin.SaveHolidayAsync(holiday);

        // The list shows the year of the holiday that was added
        Admin.HolidayYear = holiday.Date.Year;
        Admin.NewHolidayName = "";
        await Admin.LoadExtrasAsync(_admin);
        InfoText = Loc.F("Dodan praznik: {0} ({1}).", name, EmployeeFormat.Date(holiday.Date));
    });

    [RelayCommand]
    private async Task RemoveHolidayAsync(HolidayRow? row)
    {
        if (row == null) return;
        if (!await _dialogs.ConfirmAsync(Loc.T("Ukloni praznik"), Loc.F("Ukloniti praznik {0} ({1})? Mjesečni fond sati se računa bez njega.", row.Name, row.DateText))) return;

        await RunAdminAsync(async () =>
        {
            await _admin.DeleteHolidayAsync(row.Date);
            await Admin.LoadExtrasAsync(_admin);
            InfoText = Loc.F("Uklonjen praznik: {0}.", row.Name);
        });
    }
}
