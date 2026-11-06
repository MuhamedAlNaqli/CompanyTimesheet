using System;
using System.Globalization;
using Company.Api.Contracts;
using Timesheet.Domain;

namespace CompanyTimesheet.Desktop;

/// <summary>Bosnian wording and number formats of the employee mode (the interface of this mode is Bosnian only)</summary>
public static class EmployeeFormat
{
    public static readonly CultureInfo Bosnian = CreateCulture();

    private static CultureInfo CreateCulture()
    {
        try { return new CultureInfo("bs-Latn-BA"); } catch (CultureNotFoundException) { return new CultureInfo("hr-HR"); }
    }

    public static readonly string[] WeekdayNames =
        { "nedjelja", "ponedjeljak", "utorak", "srijeda", "četvrtak", "petak", "subota" };

    public static string Month(int year, int month) =>
        Loc.IsEnglish ? $"{Loc.T(BosnianTexts.MonthNames[month - 1])} {year}" : BosnianTexts.Month(year, month);

    /// <summary>The name of a code in the language of the interface</summary>
    public static string CodeName(int code) => Loc.IsEnglish ? TimeCodeNames.En(code) : TimeCodeNames.Bs(code);

    public static string Skipped(SkippedBecause reason) => Loc.T(BosnianTexts.SkippedBecause(reason));

    public static string Date(DateOnly date) => date.ToString(Loc.IsEnglish ? "dd/MM/yyyy" : "dd.MM.yyyy.", CultureInfo.InvariantCulture);

    public static string ShortDate(DateOnly date) => date.ToString(Loc.IsEnglish ? "dd/MM" : "dd.MM.", CultureInfo.InvariantCulture);

    public static string Weekday(DateOnly date) => Loc.T(WeekdayNames[(int)date.DayOfWeek]);

    /// <summary>"8 h", "2,5 h", "0,5 h"</summary>
    public static string Hours(int minutes) => (minutes / 60m).ToString("0.##", Loc.Culture) + " h";

    public static string Status(SheetStatus status) => Loc.T(BosnianTexts.Status(status));

    /// <summary>The title and the text of one due item, in the words of the employee</summary>
    public static (string Title, string Message) Reminder(ReminderDto item)
    {
        var month = Month(item.Year, item.Month);
        var deadline = Date(item.Deadline);
        return item.Kind switch
        {
            ReminderType.SubmitSoon => (Loc.T("Predajte šihtaricu"), $"Šihtarica za {month} se predaje do {deadline}"),
            ReminderType.SubmitOverdue => (Loc.T("Šihtarica kasni"), Loc.F("Šihtarica za {0} je trebala biti predana do {1}", month, deadline)),
            ReminderType.FixReturned => (Loc.T("Šihtarica je vraćena na ispravku"), Loc.F("Šihtarica za {0} je vraćena. Pročitajte poruku, ispravite unos i predajte je ponovo.", month)),
            ReminderType.Nudged => (Loc.F("Podsjetnik: {0}", item.From), Loc.F("{0} vas podsjeća da predate šihtaricu za {1}. Rok je {2}", item.From, month, deadline)),
            ReminderType.ApproveWaiting => (Loc.T("Šihtarice čekaju odobrenje"), Loc.F("Broj šihtarica koje čekaju vaše odobrenje: {0}. Rok je {1}", item.Count, deadline)),
            ReminderType.ApproveOverdue => (Loc.T("Odobravanje kasni"), Loc.F("Broj šihtarica koje čekaju vaše odobrenje: {0}. Rok je bio {1}", item.Count, deadline)),
            _ => (item.Kind.ToString(), month)
        };
    }

    /// <summary>The most urgent first: what blocks others, then what is late, then what is coming</summary>
    public static int ReminderOrder(ReminderType kind) => kind switch
    {
        ReminderType.FixReturned => 0,
        ReminderType.Nudged => 1,
        ReminderType.SubmitOverdue => 1,
        ReminderType.ApproveOverdue => 2,
        ReminderType.SubmitSoon => 3,
        ReminderType.ApproveWaiting => 4,
        _ => 9
    };

    public static string HistoryKind(HistoryKind kind) => Loc.T(BosnianTexts.History(kind));

    public static string DayType(DayKind type) => Loc.T(BosnianTexts.DayKind(type));

    public static string Entry(EntryDto entry)
    {
        var code = TimeCodes.Get(entry.Code);
        return code.Unit == CodeUnit.Hours ? $"{code.Code} {CodeName(code.Code)}: {Hours(entry.Minutes)}" : $"{code.Code} {CodeName(code.Code)}";
    }

    /// <summary>
    /// Reads hours as the employee writes them: "8", "7,5", "7.5", "7:30" or "7h30". Returns false for anything else,
    /// zero or more than 24 hours.
    /// </summary>
    public static bool TryParseHours(string? text, out int minutes)
    {
        minutes = 0;
        var value = (text ?? "").Trim().ToLowerInvariant().Replace(" ", "");
        if (value.Length == 0) return false;

        var separator = value.IndexOfAny(new[] { ':', 'h' });
        if (separator > 0)
        {
            var hoursPart = value.Substring(0, separator);
            var minutesPart = value.Substring(separator + 1).TrimEnd('m');
            if (!int.TryParse(hoursPart, NumberStyles.None, CultureInfo.InvariantCulture, out var h)) return false;
            var m = 0;
            if (minutesPart.Length > 0 && !int.TryParse(minutesPart, NumberStyles.None, CultureInfo.InvariantCulture, out m)) return false;
            if (m >= 60) return false;
            minutes = h * 60 + m;
        }
        else
        {
            if (!decimal.TryParse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var hours)) return false;
            minutes = (int)Math.Round(hours * 60m);
        }

        return minutes > 0 && minutes <= 24 * 60;
    }

    public static bool TryParseTime(string? text, out TimeOnly time)
    {
        time = default;
        var value = (text ?? "").Trim().Replace('.', ':').Replace(',', ':');
        if (value.Length == 0) return false;
        if (!value.Contains(':')) value += ":00";
        return TimeOnly.TryParseExact(value, new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }
}
