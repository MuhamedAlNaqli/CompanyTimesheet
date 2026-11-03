using Timesheet.Domain;

namespace Company.Api.Texts;

/// <summary>The wording of what the import of the employee list found, in Bosnian</summary>
public static class ImportTexts
{
    public static string Message(ImportIssue issue) => issue.Kind switch
    {
        ImportIssueKind.BadCode => $"Šifra \"{issue.Value}\" nije u obliku Z-NNN.",
        ImportIssueKind.MissingName => "Ime i prezime nedostaje.",
        ImportIssueKind.ExtraSpaces => "Suvišni razmaci u imenu su uklonjeni.",
        ImportIssueKind.DuplicateCode => $"Šifra {issue.Code} se već pojavljuje u redu {issue.Value}.",
        ImportIssueKind.NameDiffers => $"Šifra {issue.Code} već postoji s imenom \"{issue.Value}\".",
        _ => issue.Kind.ToString()
    };
}
