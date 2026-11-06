using Company.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Company.Api.Services;

/// <summary>
/// The state holidays that fall on the same date every year, for the Federation of Bosnia and Herzegovina. The server writes them for the
/// current and the next year so that nobody has to type them; the administrator can change, remove or add holidays (a canton declares one,
/// a law changes). A year that already has holidays is left alone, so what the administrator decided is never written over.
/// Religious holidays are not here: they differ from person to person, and an employee uses the code 1025 on the day.
/// </summary>
public static class HolidayCatalog
{
    /// <summary>Month, day and name of the fixed holidays</summary>
    public static readonly (int Month, int Day, string Name)[] Fixed =
    {
        (1, 1, "Nova godina"),
        (1, 2, "Drugi dan Nove godine"),
        (3, 1, "Dan nezavisnosti Bosne i Hercegovine"),
        (5, 1, "Praznik rada"),
        (5, 2, "Drugi dan Praznika rada"),
        (11, 25, "Dan državnosti Bosne i Hercegovine")
    };

    /// <summary>Writes the fixed holidays of every year in the list that has no holiday yet; returns how many were written</summary>
    public static async Task<int> SeedAsync(CompanyDbContext db, params int[] years)
    {
        var written = 0;
        foreach (var year in years.Distinct())
        {
            if (await db.StateHolidays.AnyAsync(h => h.Date.Year == year)) continue;

            foreach (var (month, day, name) in Fixed)
                db.StateHolidays.Add(new StateHoliday { Date = new DateOnly(year, month, day), Name = name });

            db.AuditEvents.Add(new AuditEvent
            {
                AtUtc = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc),
                Actor = "system",
                Action = "HolidaysSeeded",
                EntityType = "StateHoliday",
                EntityId = year.ToString(),
                Details = $"{Fixed.Length} fixed state holidays"
            });
            written += Fixed.Length;
        }

        if (written > 0) await db.SaveChangesAsync();
        return written;
    }
}
