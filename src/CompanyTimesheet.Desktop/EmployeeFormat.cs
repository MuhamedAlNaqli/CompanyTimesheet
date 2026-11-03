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

    public static string Month(int year, int month) => BosnianTexts.Month(year, month);

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy.", CultureInfo.InvariantCulture);

    public static string ShortDate(DateOnly date) => date.ToString("dd.MM.", CultureInfo.InvariantCulture);

    public static string Weekday(DateOnly date) => WeekdayNames[(int)date.DayOfWeek];

    /// <summary>"8 h", "2,5 h", "0,5 h"</summary>
    public static string Hours(int minutes) => (minutes / 60m).ToString("0.##", Bosnian) + " h";

    public static string Status(SheetStatus status) => BosnianTexts.Status(status);

    /// <summary>The title and the text of one due item, in the words of the employee</summary>
    public static (string Title, string Message) Reminder(ReminderDto item)
    {
        var month = Month(item.Year, item.Month);
        var deadline = Date(item.Deadline);
        return item.Kind switch
        {
            ReminderType.SubmitSoon => ("Predajte šihtaricu", $"Šihtarica za {month} se predaje do {deadline}"),
            ReminderType.SubmitOverdue => ("Šihtarica kasni", $"Šihtarica za {month} je trebala biti predana do {deadline}"),
            ReminderType.FixReturned => ("Šihtarica je vraćena na ispravku", $"Šihtarica za {month} je vraćena. Pročitajte poruku, ispravite unos i predajte je ponovo."),
            ReminderType.Nudged => ($"Podsjetnik: {item.From}", $"{item.From} vas podsjeća da predate šihtaricu za {month}. Rok je {deadline}"),
            ReminderType.ApproveWaiting => ("Šihtarice čekaju odobrenje", $"Broj šihtarica koje čekaju vaše odobrenje: {item.Count}. Rok je {deadline}"),
            ReminderType.ApproveOverdue => ("Odobravanje kasni", $"Broj šihtarica koje čekaju vaše odobrenje: {item.Count}. Rok je bio {deadline}"),
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

    public static string HistoryKind(HistoryKind kind) => BosnianTexts.History(kind);

    public static string DayType(DayKind type) => BosnianTexts.DayKind(type);

    public static string Entry(EntryDto entry)
    {
        var code = TimeCodes.Get(entry.Code);
        return code.Unit == CodeUnit.Hours ? $"{code.Code} {TimeCodeNames.Bs(code.Code)}: {Hours(entry.Minutes)}" : $"{code.Code} {TimeCodeNames.Bs(code.Code)}";
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
