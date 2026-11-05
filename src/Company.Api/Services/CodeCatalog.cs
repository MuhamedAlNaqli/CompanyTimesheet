using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Services;

/// <summary>
/// Keeps the codes of the legend in the database and hands them to the rules of the sheet. The 22 codes of the legend are written in once
/// (as system codes); the administrator adds more and changes them, and every change is loaded into <see cref="TimeCodes"/> at once.
/// </summary>
public static class CodeCatalog
{
    /// <summary>The lowest and highest number a code can have</summary>
    public const int FirstCode = 1000;

    public const int LastCode = 9999;

    /// <summary>Writes the codes of the legend that are missing, then loads the catalogue</summary>
    public static async Task SeedAndLoadAsync(CompanyDbContext db)
    {
        var existing = await db.Codes.Select(c => c.Code).ToListAsync();
        foreach (var code in TimeCodes.Defaults.Where(d => !existing.Contains(d.Code)))
        {
            db.Codes.Add(new CodeDefinition
            {
                Code = code.Code,
                NameBs = TimeCodeNames.Defaults[code.Code].Bs,
                NameEn = TimeCodeNames.Defaults[code.Code].En,
                Colour = code.Colour.ToString(),
                Unit = code.Unit.ToString(),
                IsActive = true,
                IsSystem = true
            });
        }

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
        await LoadAsync(db);
    }

    /// <summary>Makes the rules of the sheet use the codes of the database</summary>
    public static async Task LoadAsync(CompanyDbContext db)
    {
        var rows = await db.Codes.AsNoTracking().OrderBy(c => c.Code).ToListAsync();
        TimeCodes.Configure(rows.Select(ToTimeCode));
        TimeCodeNames.Configure(rows.Select(r => (r.Code, r.NameBs, r.NameEn)));
    }

    public static TimeCode ToTimeCode(CodeDefinition row) => new(
        row.Code,
        Enum.TryParse<CodeColour>(row.Colour, out var colour) ? colour : CodeColour.Green,
        Enum.TryParse<CodeUnit>(row.Unit, out var unit) ? unit : CodeUnit.Hours,
        row.IsActive);
}
