using System.Text;
using System.Text.RegularExpressions;

namespace Timesheet.Domain;

public static class EmployeeCode
{
    private static readonly Regex Pattern = new(@"^Z-\d{3}$", RegexOptions.Compiled);

    /// <summary>"Z-255" is valid. "Z- 255" (the typo in the sample sheet) and "z255" are not; surrounding spaces are removed.</summary>
    public static bool TryParse(string? text, out string code)
    {
        code = (text ?? "").Trim();
        return Pattern.IsMatch(code);
    }
}

public enum ImportSeverity
{
    Warning,
    Error
}

/// <summary>What was found in a line of the list; the wording is for the program that shows it</summary>
public enum ImportIssueKind
{
    /// <summary>The code is not of the form Z-NNN (Value: the text as written)</summary>
    BadCode,

    MissingName,

    /// <summary>Extra spaces in the name were removed</summary>
    ExtraSpaces,

    /// <summary>The code appears twice (Value: the line of the first appearance)</summary>
    DuplicateCode,

    /// <summary>The code exists already, with another name (Value: that name)</summary>
    NameDiffers
}

public sealed record ImportIssue(ImportSeverity Severity, ImportIssueKind Kind, string Code = "", string Value = "");

public sealed record EmployeeImportRow(int Line, string Code, string Name, IReadOnlyList<ImportIssue> Issues)
{
    public bool HasErrors => Issues.Any(i => i.Severity == ImportSeverity.Error);
}

public sealed record EmployeeImportPreview(IReadOnlyList<EmployeeImportRow> Rows)
{
    /// <summary>The rows that can be imported</summary>
    public IEnumerable<EmployeeImportRow> Valid => Rows.Where(r => !r.HasErrors);

    public int ErrorCount => Rows.Count(r => r.HasErrors);

    public int WarningCount => Rows.Count(r => r.Issues.Any(i => i.Severity == ImportSeverity.Warning));
}

/// <summary>
/// Reads the employee list (columns "Br.,Ime i prezime", UTF-8 with or without a byte order mark) and reports what an
/// import would do, before anything is saved: malformed codes, duplicates, names cleaned of stray spaces, and codes that already
/// exist with another name.
/// </summary>
public static class EmployeeListImporter
{
    public static EmployeeImportPreview Preview(string csv, IReadOnlyDictionary<string, string>? existing = null)
    {
        var rows = new List<EmployeeImportRow>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;
        var headerSkipped = false;

        foreach (var line in csv.TrimStart('﻿').Split('\n').Select(l => l.TrimEnd('\r')))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = SplitLine(line);
            if (!headerSkipped)
            {
                headerSkipped = true;
                if (cells.Count > 0 && cells[0].Trim().StartsWith("Br", StringComparison.OrdinalIgnoreCase)) continue;
            }

            var issues = new List<ImportIssue>();
            var rawCode = cells.Count > 0 ? cells[0] : "";
            var rawName = cells.Count > 1 ? cells[1] : "";

            var codeOk = EmployeeCode.TryParse(rawCode, out var code);
            if (!codeOk)
                issues.Add(new ImportIssue(ImportSeverity.Error, ImportIssueKind.BadCode, Value: rawCode.Trim()));

            var name = Regex.Replace(rawName.Trim(), @"\s+", " ");
            if (name.Length == 0)
                issues.Add(new ImportIssue(ImportSeverity.Error, ImportIssueKind.MissingName));
            else if (name != rawName)
                issues.Add(new ImportIssue(ImportSeverity.Warning, ImportIssueKind.ExtraSpaces));

            if (codeOk)
            {
                if (seen.TryGetValue(code, out var firstLine))
                    issues.Add(new ImportIssue(ImportSeverity.Error, ImportIssueKind.DuplicateCode, code, firstLine.ToString()));
                else
                    seen[code] = lineNumber;

                if (existing != null && existing.TryGetValue(code, out var existingName) && !string.Equals(existingName, name, StringComparison.Ordinal))
                    issues.Add(new ImportIssue(ImportSeverity.Warning, ImportIssueKind.NameDiffers, code, existingName));
            }

            rows.Add(new EmployeeImportRow(lineNumber, code, name, issues));
        }

        return new EmployeeImportPreview(rows);
    }

    /// <summary>Splits one line at commas, honouring double quotes</summary>
    private static List<string> SplitLine(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        cells.Add(current.ToString());
        return cells;
    }
}
