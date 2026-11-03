using System.Globalization;
using ClosedXML.Excel;
using Company.Api.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Timesheet.Domain;

namespace Company.Api.Printing;

/// <summary>One employee's month as it goes on the printed sheet</summary>
/// <summary>A note (for example the status) is printed under the name; the sheet of one employee needs none</summary>
public sealed record PrintedEmployee(string Code, string Name, SheetDto Sheet, string? Note = null);

/// <summary>One row of an employee's block: a code and a value for each day of the month (null leaves the cell empty)</summary>
public sealed record PrintedRow(TimeCode Code, decimal?[] Days);

/// <summary>
/// The printed timesheet in the layout of the current Excel sheet (the "šihtarica"): the legend, a block of rows per
/// employee with one column per day, the colour of each row from its legend group, weekend and holiday columns in dark
/// red, and the register of overtime. The whole-day absence codes are written into the regular-work row, as on the
/// current sheet; everything else goes in the row of its own code.
/// </summary>
public static class LegacySheet
{
    public const string WeekendColour = "#C00000";

    /// <summary>
    /// The built-in font of the PDF library has no č and Č. Arial is on every Windows server and has all letters of the language;
    /// the others are what a Linux server usually has, so the sheet reads the same wherever it is drawn.
    /// </summary>
    internal static readonly string PdfFont = ChooseFont();

    /// <summary>The first of these that the computer has; without any, the library's own font (which lacks č and Č)</summary>
    private static string ChooseFont()
    {
        try
        {
            var fonts = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "/usr/share/fonts", "/usr/local/share/fonts" };
            foreach (var (family, file) in new[] { ("Arial", "arial.ttf"), ("Liberation Sans", "LiberationSans-Regular.ttf"), ("DejaVu Sans", "DejaVuSans.ttf") })
            {
                foreach (var folder in fonts.Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f)))
                {
                    if (Directory.EnumerateFiles(folder, file, SearchOption.AllDirectories).Any()) return family;
                }
            }
        }
        catch (Exception)
        {
            // An unreadable fonts folder only means the library's own font is used
        }

        return "Lato";
    }

    private static readonly int[] StandardRows =
    {
        Codes.RegularWork, Codes.StateHoliday, Codes.HolidayWork, Codes.Overtime, Codes.NightOvertime,
        Codes.HolidayOvertime, Codes.WeekendOvertime, Codes.NightWeekendOvertime, Codes.NightHolidayOvertime
    };

    public static string Colour(CodeColour colour) => colour switch
    {
        CodeColour.Green => "#92D050",
        CodeColour.Blue => "#95B3D7",
        CodeColour.Orange => "#FABF8F",
        _ => "#E6B8B7"
    };

    public static string MonthTitle(int year, int month) => BosnianTexts.Month(year, month);

    public static string StatusText(SheetStatus status) => BosnianTexts.Status(status);

    /// <summary>The rows of one employee's block, in the order of the current sheet; the night-work rows only when they are used</summary>
    public static IReadOnlyList<PrintedRow> Rows(SheetDto sheet)
    {
        var count = sheet.Days.Count;
        var values = new Dictionary<int, decimal?[]>();
        foreach (var code in StandardRows) values[code] = new decimal?[count];

        decimal?[] RowOf(int code)
        {
            if (!values.TryGetValue(code, out var row)) values[code] = row = new decimal?[count];
            return row;
        }

        for (var i = 0; i < count; i++)
        {
            foreach (var entry in sheet.Days[i].Entries)
            {
                var code = TimeCodes.Get(entry.Code);
                if (code.IsAbsence) RowOf(Codes.RegularWork)[i] = code.Code;
                else RowOf(code.Code)[i] = (RowOf(code.Code)[i] ?? 0) + entry.Minutes / 60m;
            }

            foreach (var allocation in sheet.Days[i].OvertimeAllocations)
                RowOf(allocation.Code)[i] = (RowOf(allocation.Code)[i] ?? 0) + allocation.Minutes / 60m;
        }

        var order = StandardRows.ToList();
        foreach (var night in new[] { Codes.RegularNightWork, Codes.HolidayNightWork })
        {
            if (values.TryGetValue(night, out var row) && row.Any(v => v != null))
                order.Insert(order.IndexOf(Codes.HolidayWork) + 1 + (night == Codes.HolidayNightWork && order.Contains(Codes.RegularNightWork) ? 1 : 0), night);
        }

        return order.Select(code => new PrintedRow(TimeCodes.Get(code), values[code])).ToList();
    }

    public static string Format(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool IsOff(DayDto day) => day.Type != DayKind.Working;

    // ---- Excel ---------------------------------------------------------------------------------------------------------

    public static byte[] ToExcel(int year, int month, IReadOnlyList<PrintedEmployee> employees, string? statusLine = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Šihtarica");
        var days = DateTime.DaysInMonth(year, month);
        const int fixedColumns = 4;
        var lastColumn = fixedColumns + days;

        sheet.Column(1).Width = 8;
        sheet.Column(2).Width = 22;
        sheet.Column(3).Width = 7;
        sheet.Column(4).Width = 30;
        for (var d = 1; d <= days; d++) sheet.Column(fixedColumns + d).Width = 4.6;

        var row = 1;
        sheet.Cell(row, 1).Value = $"Evidencija radnog vremena – {MonthTitle(year, month)}";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 14;
        row++;
        if (!string.IsNullOrWhiteSpace(statusLine))
        {
            sheet.Cell(row, 1).Value = statusLine;
            row++;
        }

        row++;
        sheet.Cell(row, 1).Value = "LEGENDA:";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        // The legend in three columns; the blocks span the fixed columns and then two halves of the day columns
        var legend = TimeCodes.All.ToList();
        var perColumn = (int)Math.Ceiling(legend.Count / 3.0);
        var spans = new[] { (1, fixedColumns), (fixedColumns + 1, fixedColumns + days / 2), (fixedColumns + days / 2 + 1, lastColumn) };
        for (var i = 0; i < legend.Count; i++)
        {
            var (from, to) = spans[i / perColumn];
            var range = sheet.Range(row + i % perColumn, from, row + i % perColumn, to);
            range.Merge();
            range.Style.Fill.BackgroundColor = XLColor.FromHtml(Colour(legend[i].Colour));
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            sheet.Cell(row + i % perColumn, from).Value = $"{legend[i].Code}  {TimeCodeNames.Bs(legend[i].Code)}";
        }

        row += perColumn + 1;

        var firstHeader = row;
        var firstBlockRow = 0;
        var blocks = 0;
        var employeesList = employees.ToList();
        var template = employeesList.Count > 0 ? employeesList[0].Sheet.Days : Array.Empty<DayDto>();

        // Header: month, weekday initials, day numbers
        sheet.Range(row, fixedColumns + 1, row, lastColumn).Merge().Value = MonthTitle(year, month);
        sheet.Range(row, fixedColumns + 1, row, lastColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Range(row, fixedColumns + 1, row, lastColumn).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        row++;
        for (var d = 1; d <= days; d++)
        {
            var date = new DateOnly(year, month, d);
            var info = template.Count >= d ? template[d - 1] : null;
            sheet.Cell(row, fixedColumns + d).Value = info?.Weekday ?? BosnianTexts.WeekdayInitial(date);
            sheet.Cell(row + 1, fixedColumns + d).Value = d;
            foreach (var r in new[] { row, row + 1 })
            {
                var cell = sheet.Cell(r, fixedColumns + d);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                if (info != null ? IsOff(info) : WorkCalendar.IsWeekend(date)) cell.Style.Fill.BackgroundColor = XLColor.FromHtml(WeekendColour);
            }
        }

        row++;
        foreach (var (text, column) in new[] { ("Šifra", 1), ("Ime i prezime", 2), ("Vrsta", 3), ("Opis", 4) })
        {
            sheet.Cell(row, column).Value = text;
            sheet.Cell(row, column).Style.Font.Bold = true;
            sheet.Cell(row, column).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        var lastHeader = row;
        row++;

        foreach (var employee in employeesList)
        {
            if (firstBlockRow == 0) firstBlockRow = row;
            var rows = Rows(employee.Sheet);
            var first = row;

            for (var r = 0; r < rows.Count; r++)
            {
                var printed = rows[r];
                sheet.Cell(row, 3).Value = printed.Code.Code;
                sheet.Cell(row, 4).Value = TimeCodeNames.Bs(printed.Code.Code);
                sheet.Range(row, 3, row, 4).Style.Fill.BackgroundColor = XLColor.FromHtml(Colour(printed.Code.Colour));
                sheet.Cell(row, 3).Style.Font.Bold = true;
                sheet.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                for (var d = 1; d <= days; d++)
                {
                    var cell = sheet.Cell(row, fixedColumns + d);
                    var info = employee.Sheet.Days.Count >= d ? employee.Sheet.Days[d - 1] : null;
                    var off = info != null && IsOff(info);
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml(off ? WeekendColour : Colour(printed.Code.Colour));
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    if (printed.Days[d - 1] is { } value)
                    {
                        cell.Value = (double)value;
                        cell.Style.NumberFormat.Format = "0.##";
                    }
                }

                sheet.Range(row, 3, row, lastColumn).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                sheet.Range(row, 3, row, lastColumn).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                row++;
            }

            var code = sheet.Range(first, 1, row - 1, 1);
            code.Merge().Value = employee.Code;
            code.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            code.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            code.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            var name = sheet.Range(first, 2, row - 1, 2);
            name.Merge().Value = employee.Note == null ? employee.Name : employee.Name + Environment.NewLine + employee.Note;
            name.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            name.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            name.Style.Alignment.WrapText = true;
            name.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            // A block is never split between two pages: four employees to a page, the header repeats
            blocks++;
            if (blocks % 4 == 0 && blocks < employeesList.Count) sheet.PageSetup.AddHorizontalPageBreak(row - 1);
        }

        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.Margins.Left = sheet.PageSetup.Margins.Right = 0.4;
        sheet.PageSetup.Margins.Top = sheet.PageSetup.Margins.Bottom = 0.5;
        sheet.PageSetup.SetRowsToRepeatAtTop(firstHeader, lastHeader);
        sheet.SheetView.FreezeRows(lastHeader);
        sheet.SheetView.FreezeColumns(fixedColumns);

        AddOvertimeSheet(workbook, year, month, employeesList);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void AddOvertimeSheet(XLWorkbook workbook, int year, int month, IReadOnlyList<PrintedEmployee> employees)
    {
        var sheet = workbook.AddWorksheet("Prekovremeni rad");
        var headers = new[] { "Uposlenik", "Datum", "Početak i završetak rada", "Ukupno prekovremenih sati", "Kratak opis posla" };
        var widths = new[] { 28, 14, 24, 26, 60 };
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Column(c + 1).Width = widths[c];
            sheet.Cell(1, c + 1).Value = headers[c];
            sheet.Cell(1, c + 1).Style.Font.Bold = true;
            sheet.Cell(1, c + 1).Style.Alignment.WrapText = true;
        }

        var row = 2;
        foreach (var record in OvertimeLines(employees))
        {
            sheet.Cell(row, 1).Value = record.Name;
            sheet.Cell(row, 2).Value = record.Date.ToString("dd.MM.yyyy.", CultureInfo.InvariantCulture);
            sheet.Cell(row, 3).Value = $"{record.Start:HH\\:mm}-{record.End:HH\\:mm}";
            sheet.Cell(row, 4).Value = (double)(record.Minutes / 60m);
            sheet.Cell(row, 4).Style.NumberFormat.Format = "0.##";
            sheet.Cell(row, 5).Value = record.Description;
            sheet.Cell(row, 5).Style.Alignment.WrapText = true;
            row++;
        }

        sheet.Range(1, 1, Math.Max(row - 1, 1), headers.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        sheet.Range(1, 1, Math.Max(row - 1, 1), headers.Length).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.FitToPages(1, 0);
    }

    private sealed record OvertimeLine(string Name, DateOnly Date, TimeOnly Start, TimeOnly End, int Minutes, string Description);

    /// <summary>The overtime of the month, by employee and date, as in the register on the current sheet</summary>
    private static IEnumerable<OvertimeLine> OvertimeLines(IReadOnlyList<PrintedEmployee> employees) =>
        employees.SelectMany(e => e.Sheet.Overtime.Select(o => new OvertimeLine(e.Name, o.Date, o.Start, o.End, o.TotalMinutes, o.Description)))
            .OrderBy(l => l.Name, StringComparer.CurrentCulture).ThenBy(l => l.Date).ThenBy(l => l.Start);

    // ---- PDF -----------------------------------------------------------------------------------------------------------

    public static byte[] ToPdf(int year, int month, IReadOnlyList<PrintedEmployee> employees, string? statusLine = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var days = DateTime.DaysInMonth(year, month);
        var legend = TimeCodes.All.ToList();
        var perColumn = (int)Math.Ceiling(legend.Count / 3.0);

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(18);
                page.DefaultTextStyle(t => t.FontSize(6.5f).FontFamily(PdfFont));
                page.Content().Column(column =>
                {
                    column.Spacing(6);
                    column.Item().Text($"Evidencija radnog vremena – {MonthTitle(year, month)}").FontSize(13).Bold();
                    if (!string.IsNullOrWhiteSpace(statusLine)) column.Item().Text(statusLine).FontSize(8);

                    column.Item().Text("LEGENDA:").Bold();
                    column.Item().Row(row =>
                    {
                        for (var c = 0; c < 3; c++)
                        {
                            row.RelativeItem().Border(0.5f).Column(legendColumn =>
                            {
                                foreach (var code in legend.Skip(c * perColumn).Take(perColumn))
                                    legendColumn.Item().Background(Colour(code.Colour)).PaddingHorizontal(3).Text($"{code.Code}  {TimeCodeNames.Bs(code.Code)}");
                            });
                        }
                    });

                    foreach (var employee in employees) column.Item().ShowEntire().Element(e => EmployeeTable(e, employee, year, month, days));
                });
                page.Footer().AlignRight().Text(t =>
                {
                    t.Span("Strana ").FontSize(7);
                    t.CurrentPageNumber().FontSize(7);
                });
            });

            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily(PdfFont));
                page.Content().Column(column =>
                {
                    column.Spacing(6);
                    column.Item().Text($"Prekovremeni rad – {MonthTitle(year, month)}").FontSize(13).Bold();
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1.5f);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2.5f);
                            c.RelativeColumn(6);
                        });

                        foreach (var header in new[] { "Uposlenik", "Datum", "Početak i završetak rada", "Ukupno prekovremenih sati", "Kratak opis posla" })
                            table.Cell().Border(0.8f).Padding(3).Text(header).Bold();

                        foreach (var line in OvertimeLines(employees))
                        {
                            table.Cell().Border(0.5f).Padding(3).Text(line.Name);
                            table.Cell().Border(0.5f).Padding(3).Text(line.Date.ToString("dd.MM.yyyy.", CultureInfo.InvariantCulture));
                            table.Cell().Border(0.5f).Padding(3).Text($"{line.Start:HH\\:mm}-{line.End:HH\\:mm}");
                            table.Cell().Border(0.5f).Padding(3).AlignRight().Text(Format(line.Minutes / 60m));
                            table.Cell().Border(0.5f).Padding(3).Text(line.Description);
                        }
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void EmployeeTable(IContainer container, PrintedEmployee employee, int year, int month, int days)
    {
        var rows = Rows(employee.Sheet);
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(32);
                c.ConstantColumn(78);
                c.ConstantColumn(26);
                c.ConstantColumn(96);
                for (var d = 0; d < days; d++) c.RelativeColumn();
            });

            // Header: weekday initials and day numbers
            for (var line = 0; line < 2; line++)
            {
                for (var fixedColumn = 1; fixedColumn <= 4; fixedColumn++)
                    table.Cell().Row((uint)(line + 1)).Column((uint)fixedColumn).Border(0.3f).Text(line == 1 ? new[] { "Šifra", "Ime i prezime", "Vrsta", "Opis" }[fixedColumn - 1] : "").Bold();

                for (var d = 1; d <= days; d++)
                {
                    var date = new DateOnly(year, month, d);
                    var info = employee.Sheet.Days.Count >= d ? employee.Sheet.Days[d - 1] : null;
                    var off = info != null ? IsOff(info) : WorkCalendar.IsWeekend(date);
                    IContainer cell = table.Cell().Row((uint)(line + 1)).Column((uint)(4 + d));
                    if (off) cell = cell.Background(WeekendColour);
                    cell.Border(0.3f).AlignCenter().Text(line == 0 ? info?.Weekday ?? BosnianTexts.WeekdayInitial(date) : d.ToString(CultureInfo.InvariantCulture));
                }
            }

            for (var r = 0; r < rows.Count; r++)
            {
                var rowNumber = (uint)(r + 3);
                var printed = rows[r];
                var colour = Colour(printed.Code.Colour);
                table.Cell().Row(rowNumber).Column(3).Background(colour).Border(0.3f).AlignCenter().Text(printed.Code.Code.ToString(CultureInfo.InvariantCulture)).Bold();
                table.Cell().Row(rowNumber).Column(4).Background(colour).Border(0.3f).PaddingLeft(2).Text(TimeCodeNames.Bs(printed.Code.Code));

                for (var d = 1; d <= days; d++)
                {
                    var info = employee.Sheet.Days.Count >= d ? employee.Sheet.Days[d - 1] : null;
                    var off = info != null && IsOff(info);
                    var cell = table.Cell().Row(rowNumber).Column((uint)(4 + d)).Background(off ? WeekendColour : colour).Border(0.3f).AlignCenter();
                    cell.Text(printed.Days[d - 1] is { } value ? Format(value) : "");
                }
            }

            table.Cell().Row(3).Column(1).RowSpan((uint)rows.Count).Border(0.3f).AlignMiddle().AlignCenter().Text(employee.Code);
            table.Cell().Row(3).Column(2).RowSpan((uint)rows.Count).Border(0.3f).AlignMiddle().AlignCenter().Column(name =>
            {
                name.Item().AlignCenter().Text(employee.Name);
                if (employee.Note != null) name.Item().AlignCenter().Text(employee.Note).FontSize(5.5f).Italic();
            });
        });
    }
}
