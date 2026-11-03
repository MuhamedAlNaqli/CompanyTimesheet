using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Services;

/// <summary>A refusal the desktop app can show as it is</summary>
public sealed class SheetException : Exception
{
    public SheetException(int status, string code, string message) : base(message)
    {
        Status = status;
        Code = code;
    }

    public int Status { get; }

    public string Code { get; }
}

/// <summary>The sizes the database columns allow; a longer text is refused with a clear message instead of failing in the database</summary>
public static class Limits
{
    public const int OvertimeDescription = 500;
    public const int Message = 2000;
    public const int Name = 150;
    public const int Code = 10;

    public static string Check(string? text, int max, string what)
    {
        var value = text?.Trim() ?? "";
        if (value.Length > max)
            throw new SheetException(400, "too-long", $"{what} can have at most {max} characters; this one has {value.Length}.");
        return value;
    }
}

/// <summary>Holidays and other facts every sheet needs</summary>
public static class Reference
{
    public static async Task<WorkCalendar> CalendarAsync(CompanyDbContext db, int year)
    {
        var holidays = await db.StateHolidays.AsNoTracking()
            .Where(h => h.Date.Year == year || h.Date.Year == year + 1 || h.Date.Year == year - 1).ToListAsync();
        return new WorkCalendar(holidays.Select(h => new Holiday(h.Date, h.Name)));
    }
}
