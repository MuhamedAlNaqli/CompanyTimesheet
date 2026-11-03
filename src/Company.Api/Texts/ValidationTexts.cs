using System.Globalization;
using Company.Api.Contracts;
using Timesheet.Domain;

namespace Company.Api.Texts;

/// <summary>
/// The wording of the findings of the rules, in Bosnian. The rules (in the domain library) say what was found and with which values;
/// this is the one place that says it in words. Numbers are written the same on every server: a comma for the decimals.
/// </summary>
public static class ValidationTexts
{
    public static string Message(ValidationIssue issue)
    {
        string D(string name) => Date((DateOnly)issue.Values[name]);
        string T(string name) => ((TimeOnly)issue.Values[name]).ToString("HH':'mm", CultureInfo.InvariantCulture);
        string H(string name) => Hours((int)issue.Values[name]);
        object V(string name) => issue.Values[name];

        return issue.Key switch
        {
            "V-08.unknown" => $"Zaposlenik {V("employee")} nije u evidenciji zaposlenika.",
            "V-08.inactive" => $"Zaposlenik {V("employee")} nije aktivan u ovom mjesecu.",
            "V-02" => $"Godišnji odmor ({V("code")}) ne smije biti upisan za neradni dan {D("date")}.",
            "V-17" => $"Odsustvo ({V("code")}) se ne upisuje za vikend {D("date")}.",
            "V-06" => $"Redovan rad ne može biti upisan za vikend {D("date")}; rad vikendom je prekovremeni rad.",
            "V-07" => $"Državni praznik {D("date")}: koristite šifre 1024 i 2010, ne {V("code")}.",
            "V-14" => $"Redovni sati {D("date")} ({H("minutes")}) razlikuju se od dnevne norme ({H("normMinutes")}).",
            "V-12.hours" => $"Sati {D("date")} nisu višekratnik od {V("step")} min.",
            "V-12.overtime" => $"Prekovremeni rad {D("date")}: vrijeme nije višekratnik od {V("step")} min.",
            "V-05" => $"Više od jednog unosa u redu Redovan rad za {D("date")}: {V("codes")}.",
            "V-04" => $"Za radni dan {D("date")} nema unosa.",
            "V-20" => $"Državni praznik {D("date")}: 1024 ({H("holidayMinutes")}) + 2010 ({H("workedMinutes")}) mora biti {H("normMinutes")}.",
            "V-15" => $"Prekovremeni rad {D("date")}: početak i kraj su isti.",
            "V-01" => $"Prekovremeni rad {D("date")}: ukupno {H("declaredMinutes")} ne odgovara intervalu ({H("intervalMinutes")}).",
            "V-03" => $"Prekovremeni rad {D("date")} zalazi u redovno radno vrijeme ({T("regularStart")}–{T("regularEnd")}) dana {D("conflictDate")}.",
            "V-13" => $"Prekovremeni rad {D("date")}: kratak opis posla je obavezan.",
            "V-18" => $"Prekovremeni rad {D("date")}: kategorije su ručno izmijenjene.",
            "V-19" => $"Na dan službenog puta ({V("code")}) {D("date")} ne može se upisati prekovremeni rad.",
            "V-19b" => $"Na bolovanju ({V("code")}) {D("date")} ne smije se raditi.",
            "V-19c" => $"Prekovremeni rad na dan odsustva ({V("code")}) {D("date")} nije potvrđen kao dozvoljen.",
            "V-10" => $"Prekovremeni rad {D("date")} {T("start")}–{T("end")} je unesen dvaput.",
            "V-09" => $"Prekovremeni rad {D("date")} {T("start")}–{T("end")} preklapa se s unosom {D("otherDate")} {T("otherStart")}–{T("otherEnd")}.",
            "V-11" => $"Za {D("date")} je upisano više od 24 sata ({H("minutes")}).",
            "V-21" => $"Mjesečni fond sati ({H("fundMinutes")}) manji je od očekivanog ({H("expectedMinutes")}).",
            _ => issue.Key
        };
    }

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy.", CultureInfo.InvariantCulture);

    /// <summary>"8 h", "2,5 h"</summary>
    public static string Hours(int minutes) => (minutes / 60m).ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',') + " h";
}
