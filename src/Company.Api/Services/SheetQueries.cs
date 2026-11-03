using Company.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Company.Api.Services;

/// <summary>How a sheet is read with everything that belongs to it: one definition, so no query forgets a part</summary>
public static class SheetQueries
{
    /// <summary>Entries, overtime, messages, events and the chain, each read in a query of its own (they would multiply in one)</summary>
    public static IQueryable<MonthSheet> WithDetails(this IQueryable<MonthSheet> sheets) =>
        sheets.Include(s => s.Entries).Include(s => s.Overtime).Include(s => s.Messages).Include(s => s.Events).Include(s => s.ChainSteps).AsSplitQuery();
}
