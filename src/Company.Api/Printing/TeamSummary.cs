using System.Globalization;
using ClosedXML.Excel;
using Company.Api.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Timesheet.Domain;

namespace Company.Api.Printing;

public sealed record SummaryPerson(string Name, SheetDto Sheet);

/// <summary>A team lead's group, as the message to the approver lists them. The title is the lead's name, or null when there is one group.</summary>
public sealed record SummaryGroup(string? Title, IReadOnlyList<SummaryPerson> People);

/// <summary>
/// The monthly summary a supervisor sends on for approval: for each person one statement (nothing to report, overtime in the
/// table below, or the days of absence and business trips as periods), then the register of overtime. It is the message the
/// supervisors wrote by hand, made from the sheets.
/// </summary>
public static class TeamSummary
{
    public const string OvertimeNote = "Prekovremeni sati prikazani u tabeli ispod.";
    public const string NothingToReport = "Nema prekovremenih sati niti izostanaka.";

    private static readonly string[] OvertimeHeaders = { "Uposlenik", "Datum", "Početak i završetak rada", "Ukupno prekovremenih sati", "Kratak opis posla" };

    /// <summary>What to say of one person: the sentences, in the order of the message</summary>
    public static IReadOnlyList<string> Statements(SheetDto sheet)
    {
        var kind = sheet.Days.ToDictionary(d => d.Date, d => d.Type);
        var absences = sheet.Days
            .Select(d => (d.Date, Code: d.Entries.Select(e => e.Code).FirstOrDefault(c => TimeCodes.TryGet(c, out var t) && t.IsAbsence)))
            .Where(d => d.Code != 0).ToList();
        var ranges = AbsencePeriods.Build(absences, date => kind.GetValueOrDefault(date) == DayKind.Working);

        var hasOvertime = sheet.Overtime.Count > 0 || sheet.CarriedIn?.Count > 0;
        var lines = new List<string>();
        if (hasOvertime) lines.Add(OvertimeNote);
        else if (!ranges.Any(r => !Codes.IsBusinessTrip(r.Code))) lines.Add(NothingToReport);

        foreach (var range in ranges) lines.Add($"{TimeCodeNames.Bs(range.Code)}: {Period(range)}");
        return lines;
    }

    /// <summary>"21.10." for a day, "21.10.-23.10." for a period</summary>
    public static string Period(AbsencePeriod range) =>
        range.IsSingleDay ? Short(range.From) : $"{Short(range.From)}-{Short(range.To)}";

    private static string Short(DateOnly date) => date.ToString("dd.MM.", CultureInfo.InvariantCulture);

    /// <summary>The rows of the overtime register: what was entered, and the part of last month's overtime that ends after midnight in this one</summary>
    public static IReadOnlyList<OvertimeRow> OvertimeRows(IReadOnlyList<SummaryGroup> groups) =>
        groups.SelectMany(g => g.People).SelectMany(p =>
        {
            var own = p.Sheet.Overtime.OrderBy(o => o.Date).ThenBy(o => o.Start)
                .Select(o => new OvertimeRow(p.Name, o.Date, $"{o.Start:HH\\:mm} - {o.End:HH\\:mm}", o.TotalMinutes - o.MinutesInNextMonth, o.Description));
            var carried = (p.Sheet.CarriedIn ?? Array.Empty<CarriedOvertimeDto>()).OrderBy(c => c.Date)
                .Select(c => new OvertimeRow(p.Name, c.Date, $"{c.Start:HH\\:mm} - {c.End:HH\\:mm}", c.MinutesInThisMonth, c.Description + " (nastavak prethodnog mjeseca)"));
            return own.Concat(carried);
        }).ToList();

    /// <summary>Hours with a comma, as they are written in the message: 2, 2,5, 7</summary>
    public static string Hours(int minutes) => LegacySheet.Format(minutes / 60m).Replace('.', ',');

    private static string Date(DateOnly date) => date.ToString("dd.MM.yyyy.", CultureInfo.InvariantCulture);

    private static string Note(SheetDto sheet) =>
        sheet.Status == SheetStatus.Approved ? "" : $"Šihtarica: {LegacySheet.StatusText(sheet.Status)} (nije odobreno)";

    // ---- Text and the data of the screen ---------------------------------------------------------------------------------------

    public static TeamSummaryDto ToDto(int year, int month, IReadOnlyList<SummaryGroup> groups) => new(
        year, month,
        groups.Select(g => new SummaryGroupDto(g.Title,
            g.People.Select(p => new SummaryPersonDto(p.Name, Statements(p.Sheet), Note(p.Sheet) is { Length: > 0 } note ? note : null)).ToList())).ToList(),
        OvertimeRows(groups).Select(r => new SummaryOvertimeDto(r.Name, r.Date, r.Interval, Hours(r.Minutes), r.Description)).ToList(),
        ToText(year, month, groups));

    /// <summary>The message as plain text, to paste into a mail; the register of overtime is separated by tabs so it pastes into a table</summary>
    public static string ToText(int year, int month, IReadOnlyList<SummaryGroup> groups)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"Šihtarice za {BosnianTexts.Month(year, month).TrimEnd('.')}");

        foreach (var group in groups)
        {
            text.AppendLine();
            if (group.Title != null) text.AppendLine(group.Title);
            foreach (var person in group.People)
            {
                text.AppendLine(person.Name);
                foreach (var line in Statements(person.Sheet)) text.AppendLine(line);
                text.AppendLine();
            }
        }

        text.AppendLine("Prekovremeni sati:");
        text.AppendLine(string.Join('\t', OvertimeHeaders));
        foreach (var row in OvertimeRows(groups))
            text.AppendLine(string.Join('\t', row.Name, Date(row.Date), row.Interval, Hours(row.Minutes), row.Description));
        return text.ToString().TrimEnd();
    }

    // ---- Excel -----------------------------------------------------------------------------------------------------------

    public static byte[] ToExcel(int year, int month, IReadOnlyList<SummaryGroup> groups)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Pregled");
        var row = 1;

        sheet.Cell(row, 1).Value = $"Šihtarice za {BosnianTexts.Month(year, month).TrimEnd('.')}";
        sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(14);
        row += 2;

        foreach (var group in groups)
        {
            if (group.Title != null)
            {
                sheet.Cell(row, 1).Value = group.Title;
                sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(12);
                row++;
            }

            foreach (var person in group.People)
            {
                sheet.Cell(row, 1).Value = person.Name;
                sheet.Cell(row, 1).Style.Font.SetBold();
                row++;
                foreach (var line in Statements(person.Sheet).Append(Note(person.Sheet)).Where(l => l.Length > 0))
                {
                    sheet.Cell(row, 1).Value = line;
                    row++;
                }

                row++;
            }
        }

        sheet.Cell(row, 1).Value = "Prekovremeni sati:";
        sheet.Cell(row, 1).Style.Font.SetBold();
        row++;
        for (var i = 0; i < OvertimeHeaders.Length; i++)
        {
            var cell = sheet.Cell(row, i + 1);
            cell.Value = OvertimeHeaders[i];
            cell.Style.Font.SetBold();
            cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        row++;
        foreach (var line in OvertimeRows(groups))
        {
            sheet.Cell(row, 1).Value = line.Name;
            sheet.Cell(row, 2).Value = Date(line.Date);
            sheet.Cell(row, 3).Value = line.Interval;
            sheet.Cell(row, 4).Value = Hours(line.Minutes);
            sheet.Cell(row, 4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            sheet.Cell(row, 5).Value = line.Description;
            sheet.Range(row, 1, row, 5).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            row++;
        }

        sheet.Column(1).Width = 34;
        sheet.Column(2).Width = 14;
        sheet.Column(3).Width = 26;
        sheet.Column(4).Width = 26;
        sheet.Column(5).Width = 60;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ---- PDF -------------------------------------------------------------------------------------------------------------

    public static byte[] ToPdf(int year, int month, IReadOnlyList<SummaryGroup> groups)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var overtime = OvertimeRows(groups);

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily(LegacySheet.PdfFont));
                page.Content().Column(column =>
                {
                    column.Spacing(4);
                    column.Item().Text($"Šihtarice za {BosnianTexts.Month(year, month).TrimEnd('.')}").FontSize(14).Bold();
                    column.Item().PaddingBottom(6).Text("");

                    foreach (var group in groups)
                    {
                        if (group.Title != null) column.Item().PaddingTop(6).Text(group.Title).FontSize(12).Bold();

                        foreach (var person in group.People)
                        {
                            column.Item().ShowEntire().Column(block =>
                            {
                                block.Item().PaddingTop(4).Text(person.Name).Bold();
                                foreach (var line in Statements(person.Sheet)) block.Item().Text(line);
                                var note = Note(person.Sheet);
                                if (note.Length > 0) block.Item().Text(note).Italic().FontColor(Colors.Grey.Darken1);
                            });
                        }
                    }

                    column.Item().PaddingTop(14).Text("Prekovremeni sati:").Bold();
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2.2f);
                            c.RelativeColumn(2);
                            c.RelativeColumn(5);
                        });

                        foreach (var header in OvertimeHeaders) table.Cell().Border(0.8f).Padding(3).Text(header).Bold().FontSize(9);

                        foreach (var line in overtime)
                        {
                            table.Cell().Border(0.5f).Padding(3).Text(line.Name).FontSize(9);
                            table.Cell().Border(0.5f).Padding(3).Text(Date(line.Date)).FontSize(9);
                            table.Cell().Border(0.5f).Padding(3).Text(line.Interval).FontSize(9);
                            table.Cell().Border(0.5f).Padding(3).AlignRight().Text(Hours(line.Minutes)).FontSize(9);
                            table.Cell().Border(0.5f).Padding(3).Text(line.Description).FontSize(9);
                        }
                    });
                });
                page.Footer().AlignRight().Text(t =>
                {
                    t.Span("Strana ").FontSize(8);
                    t.CurrentPageNumber().FontSize(8);
                });
            });
        }).GeneratePdf();
    }
}

public sealed record OvertimeRow(string Name, DateOnly Date, string Interval, int Minutes, string Description);
