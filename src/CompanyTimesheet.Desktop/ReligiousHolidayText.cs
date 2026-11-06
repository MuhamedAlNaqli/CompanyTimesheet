using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Company.Api.Contracts;

namespace CompanyTimesheet.Desktop;

/// <summary>
/// The religious holidays of a person as the administrator writes them: one to a line, a date and a name ("06.03.2027. Ramazanski bajram").
/// A date is written dd.MM.yyyy (the dots at the end are optional) or yyyy-MM-dd; the name may be left out.
/// </summary>
public static class ReligiousHolidayText
{
    private static readonly string[] DateFormats = { "dd.MM.yyyy.", "dd.MM.yyyy", "d.M.yyyy.", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" };

    public static string Format(IReadOnlyList<HolidayDto>? holidays)
    {
        if (holidays == null || holidays.Count == 0) return "";

        var text = new StringBuilder();
        foreach (var holiday in holidays.OrderBy(h => h.Date))
        {
            if (text.Length > 0) text.Append('\n');
            text.Append(holiday.Date.ToString("dd.MM.yyyy.", CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(holiday.Name)) text.Append(' ').Append(holiday.Name.Trim());
        }

        return text.ToString();
    }

    /// <summary>Reads the lines; false with the sentence for the screen when a line is not a date</summary>
    public static bool TryParse(string? text, out IReadOnlyList<HolidayDto> holidays, out string problem)
    {
        var list = new List<HolidayDto>();
        holidays = list;
        problem = "";

        foreach (var raw in (text ?? "").Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;

            var cut = line.IndexOf(' ');
            var datePart = cut < 0 ? line : line[..cut];
            var name = cut < 0 ? "" : line[(cut + 1)..].Trim();
            if (!DateOnly.TryParseExact(datePart, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                problem = Loc.F("Red „{0}“ ne počinje datumom. Upišite datum kao 06.03.2027., pa naziv.", line);
                return false;
            }

            if (list.Any(h => h.Date == date)) continue;
            list.Add(new HolidayDto(date, name));
        }

        return true;
    }
}
