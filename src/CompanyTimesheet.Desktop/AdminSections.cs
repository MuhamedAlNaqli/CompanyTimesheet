using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Company.Api.Client;
using Company.Api.Contracts;

namespace CompanyTimesheet.Desktop;

/// <summary>One figure of the first screen of the administrator. Tone is "ok", "warn" or "info" and colours the card.</summary>
public sealed record StatCard(string Title, string Value, string Caption, string Tone)
{
    public bool IsWarning => Tone == "warn";
}

/// <summary>Something the administrator should look at, with the section that deals with it</summary>
public sealed record AttentionItem(string Text, string Section, bool IsWarning)
{
    public string ButtonText => Loc.T("Otvori");
}

/// <summary>A state holiday in the list of the year</summary>
public sealed class HolidayRow
{
    public HolidayRow(HolidayDto holiday)
    {
        Holiday = holiday;
        Date = holiday.Date;
        Name = holiday.Name;
        DateText = EmployeeFormat.Date(holiday.Date);
        WeekdayText = EmployeeFormat.Weekday(holiday.Date);
    }

    public HolidayDto Holiday { get; }

    public DateOnly Date { get; }

    public string Name { get; }

    public string DateText { get; }

    public string WeekdayText { get; }
}

/// <summary>An event of the audit log, written so that it can be read</summary>
public sealed class AuditRow
{
    public AuditRow(AuditEventDto item)
    {
        WhenText = item.AtUtc.ToLocalTime().ToString(Loc.IsEnglish ? "dd/MM/yyyy HH:mm" : "dd.MM.yyyy. HH:mm");
        Actor = item.Actor;
        ActionText = AuditActions.Text(item.Action);
        What = AuditActions.Entity(item.EntityType) + (!string.IsNullOrEmpty(item.EntityId) && item.EntityId != "*" ? " " + item.EntityId : "");
        Details = item.Details ?? "";
    }

    public string WhenText { get; }

    public string Actor { get; }

    public string ActionText { get; }

    public string What { get; }

    public string Details { get; }
}

/// <summary>What the actions of the audit log mean, in words; an action nobody has described is shown as it is</summary>
public static class AuditActions
{
    private static readonly Dictionary<string, string> Known = new()
    {
        ["AdminCreated"] = "Napravljen prvi administrator",
        ["EmployeeCreated"] = "Dodana osoba",
        ["EmployeeUpdated"] = "Izmijenjena osoba",
        ["EmployeeRenamed"] = "Promijenjeno ime osobe",
        ["EmployeesImported"] = "Uvezena lista radnika",
        ["AccountLinked"] = "Povezan račun",
        ["AccountUnlinked"] = "Razvezan račun",
        ["HolidayAdded"] = "Dodan državni praznik",
        ["HolidayRenamed"] = "Preimenovan državni praznik",
        ["HolidayRemoved"] = "Uklonjen državni praznik",
        ["ReligiousHolidaysSet"] = "Postavljeni vjerski praznici",
        ["CodeAdded"] = "Dodana šifra",
        ["CodeChanged"] = "Izmijenjena šifra",
        ["SignedIn"] = "Prijava",
        ["AccountLocked"] = "Račun zaključan",
        ["PasswordChanged"] = "Promijenjena lozinka",
        ["CredentialsSet"] = "Postavljeno korisničko ime",
        ["CredentialsRemoved"] = "Uklonjeno korisničko ime",
        ["DeputyAdded"] = "Postavljena zamjena",
        ["DeputyRemoved"] = "Uklonjena zamjena",
        ["EntrySet"] = "Upisan unos u šihtaricu",
        ["EntryCleared"] = "Obrisan unos iz šihtarice",
        ["StandardDaysFilled"] = "Popunjeni standardni dani",
        ["AbsenceRangeApplied"] = "Upisano odsustvo za period",
        ["OvertimeAdded"] = "Dodan prekovremeni rad",
        ["OvertimeChanged"] = "Izmijenjen prekovremeni rad",
        ["OvertimeDeleted"] = "Obrisan prekovremeni rad",
        ["Submitted"] = "Šihtarica predana na odobravanje",
        ["StepApproved"] = "Odobreno, ide dalje",
        ["Approved"] = "Šihtarica odobrena",
        ["Returned"] = "Šihtarica vraćena na ispravku",
        ["Answered"] = "Odgovor zaposlenika",
        ["Reassigned"] = "Korak proslijeđen drugoj osobi",
        ["SheetViewed"] = "Pregledana šihtarica",
        ["RevisionViewed"] = "Pregledana predana verzija",
        ["TeamReminderSent"] = "Poslan podsjetnik",
        ["TeamSheetPrinted"] = "Ispisane šihtarice tima",
        ["TeamSummaryPrinted"] = "Ispisan pregled za odobravanje",
        ["ApprovedSheetsPrinted"] = "Ispisane odobrene šihtarice"
    };

    private static readonly Dictionary<string, string> Entities = new()
    {
        ["MonthSheet"] = "Šihtarica",
        ["Employee"] = "Osoba",
        ["Credential"] = "Korisnički račun",
        ["Code"] = "Šifra",
        ["StateHoliday"] = "Državni praznik",
        ["Deputy"] = "Zamjena"
    };

    public static string Entity(string entityType) => Entities.TryGetValue(entityType, out var text) ? Loc.T(text) : entityType;

    public static string Text(string action) => Known.TryGetValue(action, out var text) ? Loc.T(text) : action;
}

// The sections of the administration (the side bar chooses one), and what each of them shows
public sealed partial class AdminViewModel
{
    public const string OverviewSection = "overview";
    public const string PeopleSection = "people";
    public const string CodesSection = "codes";
    public const string HolidaysSection = "holidays";
    public const string AuditSection = "audit";

    /// <summary>"overview", "people", "codes", "holidays" or "audit"</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview), nameof(ShowPeople), nameof(ShowCodes), nameof(ShowHolidays), nameof(ShowAudit),
        nameof(ShowList), nameof(ShowChart), nameof(SectionTitle), nameof(SectionHint))]
    private string _section = OverviewSection;

    public bool IsOverview => Section == OverviewSection;

    public bool ShowPeople => Section == PeopleSection;

    public bool ShowCodes => Section == CodesSection;

    public bool ShowHolidays => Section == HolidaysSection;

    public bool ShowAudit => Section == AuditSection;

    public string SectionTitle => Section switch
    {
        PeopleSection => Loc.T("Osobe"),
        CodesSection => Loc.T("Šifre"),
        HolidaysSection => Loc.T("Praznici"),
        AuditSection => Loc.T("Revizija"),
        _ => Loc.T("Pregled")
    };

    public string SectionHint => Section switch
    {
        PeopleSection => Loc.T("Zaposlenici, ko je kome nadređeni i kako se prijavljuju."),
        CodesSection => Loc.T("Šifre koje zaposlenici unose u šihtaricu: dodajte novu ili promijenite postojeću."),
        HolidaysSection => Loc.T("Državni praznici po godinama: od njih zavisi mjesečni fond sati."),
        AuditSection => Loc.T("Ko je i kada šta promijenio. Svaki zapis je vezan za prethodni, pa se naknadna izmjena primijeti."),
        _ => Loc.T("Šta se trenutno dešava i šta treba pogledati.")
    };

    public void LanguageChanged()
    {
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionHint));
    }

    // ---- Overview ------------------------------------------------------------------------------------------------------

    public ObservableCollection<StatCard> Cards { get; } = new();

    public ObservableCollection<AttentionItem> Attention { get; } = new();

    public ObservableCollection<AuditRow> RecentEvents { get; } = new();

    public bool HasRecentEvents => RecentEvents.Count > 0;

    private void BuildOverview(AdminOverviewDto overview)
    {
        Cards.Clear();
        Attention.Clear();
        RecentEvents.Clear();

        var p = overview.People;
        var c = overview.Codes;
        var h = overview.Holidays;
        string Month(OverviewMonthDto m) => EmployeeFormat.Month(m.Year, m.Month).TrimEnd('.');

        Cards.Add(new StatCard(Loc.T("Osobe"), $"{p.Active}", Loc.F("od {0} ukupno · neaktivnih {1} · administratora {2}", p.Total, p.Inactive, p.Administrators), "info"));
        Cards.Add(new StatCard(Loc.T("Bez korisničkog imena"), $"{p.WithoutUsername}", Loc.T("aktivnih osoba koje se još ne mogu prijaviti"), p.WithoutUsername > 0 ? "warn" : "ok"));
        Cards.Add(new StatCard(Loc.T("Zaključani računi"), $"{p.Locked}", Loc.T("zaključano zbog pogrešnih lozinki"), p.Locked > 0 ? "warn" : "ok"));
        Cards.Add(new StatCard(Loc.T("Šifre"), $"{c.Active}", Loc.F("od {0} ukupno · zelenih {1} · plavih {2} · narandžastih {3} · crvenih {4}", c.Total, c.Green, c.Blue, c.Orange, c.Red), "info"));
        Cards.Add(MonthCard(overview.ThisMonth, Loc.F("Šihtarice: {0}", Month(overview.ThisMonth)), false));
        Cards.Add(MonthCard(overview.PreviousMonth, Loc.F("Šihtarice: {0}", Month(overview.PreviousMonth)), true));
        Cards.Add(new StatCard(Loc.F("Državni praznici {0}", h.Year), $"{h.ThisYear}", Loc.F("upisano za {0} · za {1}: {2}", h.Year, h.Year + 1, h.NextYear), h.ThisYear == 0 ? "warn" : "ok"));
        Cards.Add(new StatCard(Loc.T("Revizija"), $"{overview.Audit.Events}", overview.Audit.LastUtc is { } last
            ? Loc.F("zapisa · zadnji {0}", last.ToLocalTime().ToString(Loc.IsEnglish ? "dd/MM/yyyy HH:mm" : "dd.MM.yyyy. HH:mm")) : Loc.T("zapisa"), "info"));

        // What needs a decision, most important first
        if (h.ThisYear == 0)
            Attention.Add(new AttentionItem(Loc.F("Nisu upisani državni praznici za {0}. Mjesečni fond sati neće biti tačan.", h.Year), HolidaysSection, true));
        else if (DateTime.Today.Month >= 10 && h.NextYear == 0)
            Attention.Add(new AttentionItem(Loc.F("Upišite državne praznike za {0}.", h.Year + 1), HolidaysSection, false));
        if (p.WithoutUsername > 0)
            Attention.Add(new AttentionItem(Loc.F("{0} aktivnih osoba nema korisničko ime pa se ne mogu prijaviti.", p.WithoutUsername), PeopleSection, true));
        if (p.Locked > 0)
            Attention.Add(new AttentionItem(Loc.F("Zaključanih računa: {0}. Postavite novu lozinku da se otključaju.", p.Locked), PeopleSection, true));
        if (p.WithoutSupervisor > 2)
            Attention.Add(new AttentionItem(Loc.F("{0} osoba nema nadređenog. Vrh hijerarhije obično ima jednu ili dvije osobe.", p.WithoutSupervisor), PeopleSection, false));
        var previous = overview.PreviousMonth;
        if (previous.Expected > 0 && previous.Approved < previous.Expected)
            Attention.Add(new AttentionItem(Loc.F("{0} šihtarica za {1} još nije odobreno (nije počelo: {2}, predano: {3}, vraćeno: {4}).",
                previous.Expected - previous.Approved, Month(previous), previous.NotStarted, previous.Submitted, previous.Returned), OverviewSection, false));
        if (Attention.Count == 0)
            Attention.Add(new AttentionItem(Loc.T("Sve je u redu: nema ničega što traži vašu pažnju."), OverviewSection, false));

        foreach (var item in overview.Audit.Recent) RecentEvents.Add(new AuditRow(item));
        OnPropertyChanged(nameof(HasRecentEvents));
    }

    private static StatCard MonthCard(OverviewMonthDto m, string title, bool previous)
    {
        var done = m.Expected > 0 && m.Approved == m.Expected;
        var tone = done ? "ok" : previous && m.Expected > 0 ? "warn" : "info";
        return new StatCard(title, $"{m.Approved}/{m.Expected}",
            Loc.F("odobreno · predano {0} · vraćeno {1} · u pripremi {2} · nije počelo {3}", m.Submitted, m.Returned, m.Draft, m.NotStarted), tone);
    }

    // ---- Holidays ------------------------------------------------------------------------------------------------------

    [ObservableProperty] private int _holidayYear = DateTime.Today.Year;

    public ObservableCollection<HolidayRow> Holidays { get; } = new();

    public bool HasNoHolidays => Holidays.Count == 0;

    /// <summary>The date picker never shows "not selected": it starts on today</summary>
    [ObservableProperty] private DateTimeOffset? _newHolidayDate = new DateTimeOffset(DateTime.Today);

    [ObservableProperty] private string _newHolidayName = "";

    [ObservableProperty] private string _holidaysSummary = "";

    // ---- Audit log -----------------------------------------------------------------------------------------------------

    public ObservableCollection<AuditRow> AuditRows { get; } = new();

    /// <summary>How many of the latest events the audit section lists</summary>
    public const int AuditTake = 150;

    /// <summary>Reads what the sections beyond the people and the codes show: the overview, the holidays of the year and the audit log</summary>
    public async Task LoadExtrasAsync(ICompanyAdminClient client)
    {
        BuildOverview(await client.GetOverviewAsync());
        await LoadHolidaysAsync(client);

        var events = await client.GetAuditAsync(AuditTake);
        AuditRows.Clear();
        foreach (var item in events) AuditRows.Add(new AuditRow(item));
    }

    public async Task LoadHolidaysAsync(ICompanyAdminClient client)
    {
        var list = await client.GetHolidaysAsync(HolidayYear);
        Holidays.Clear();
        foreach (var holiday in list.OrderBy(x => x.Date)) Holidays.Add(new HolidayRow(holiday));
        HolidaysSummary = list.Count == 0
            ? Loc.F("Za {0}. nisu upisani državni praznici. Dok ih ne upišete, mjesečni fond sati se računa kao da ih nema.", HolidayYear)
            : Loc.F("Državnih praznika za {0}: {1}.", HolidayYear, list.Count);
        OnPropertyChanged(nameof(HasNoHolidays));
    }
}
