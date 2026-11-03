using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Services;

/// <summary>
/// An employee's own month: entries, absence ranges and overtime, with everything derived (overtime split, totals, the fund
/// and the findings of the rules) computed by the domain library. The employee edits only while the sheet is a draft or
/// returned; every change is audited.
/// </summary>
public sealed class TimesheetService
{
    private readonly CompanyDbContext _db;
    private readonly AuditService _audit;
    private readonly SheetDtoBuilder _builder;
    private readonly TimeProvider _time;

    public TimesheetService(CompanyDbContext db, AuditService audit, SheetDtoBuilder builder, TimeProvider time)
    {
        _db = db;
        _audit = audit;
        _builder = builder;
        _time = time;
    }

    // ---- Reading ---------------------------------------------------------------------------------------------------

    public async Task<SheetDto> GetAsync(string employeeCode, int year, int month)
    {
        CheckMonth(year, month);
        var sheet = await LoadOrCreateAsync(employeeCode, year, month);
        return await _builder.BuildAsync(sheet);
    }

    // ---- Entries ---------------------------------------------------------------------------------------------------

    public async Task<SheetDto> SetEntryAsync(string employeeCode, int year, int month, DateOnly date, SetEntryRequest request)
    {
        var sheet = await EditableAsync(employeeCode, year, month);
        CheckDate(date, year, month);

        if (!TimeCodes.TryGet(request.Code, out var code)) throw new SheetException(400, "unknown-code", $"The code {request.Code} does not exist.");
        if (code.IsOvertime) throw new SheetException(400, "overtime-derived", "Overtime is entered as an interval, not as a code on a day.");
        await CheckEmploymentAsync(employeeCode, year, date);

        var minutes = request.Minutes;
        if (code.Unit == CodeUnit.Hours)
        {
            if (minutes <= 0 || minutes > OvertimeInterval.MinutesPerDay)
                throw new SheetException(400, "invalid-hours", "Enter between 1 minute and 24 hours.");
        }
        else
        {
            minutes = 0;
        }

        // A day has one base entry (regular hours or an absence); other hour codes are replaced per code
        var existing = sheet.Entries.Where(e => e.Date == date && (code.IsBaseEntry ? TimeCodes.Get(e.Code).IsBaseEntry : e.Code == code.Code)).ToList();
        foreach (var old in existing) _db.SheetEntries.Remove(old);
        sheet.Entries.RemoveAll(existing.Contains);
        sheet.Entries.Add(new SheetEntry { Date = date, Code = code.Code, Minutes = minutes });

        Touch(sheet, "EntrySet", $"{date:yyyy-MM-dd} {code.Code} {minutes}");
        await SaveAsync();
        return await _builder.BuildAsync(sheet);
    }

    public async Task<SheetDto> ClearEntryAsync(string employeeCode, int year, int month, DateOnly date, int code)
    {
        var sheet = await EditableAsync(employeeCode, year, month);
        var entry = sheet.Entries.FirstOrDefault(e => e.Date == date && e.Code == code);
        if (entry == null) throw new SheetException(404, "not-found", "There is no such entry.");

        _db.SheetEntries.Remove(entry);
        sheet.Entries.Remove(entry);
        Touch(sheet, "EntryCleared", $"{date:yyyy-MM-dd} {code}");
        await SaveAsync();

        // Taking away an absence gives the standard day back
        await AddStandardDaysAsync(sheet);
        return await _builder.BuildAsync(sheet);
    }

    /// <summary>Regular hours (the daily norm) on every working day that has no base entry. Opening an editable month does the same.</summary>
    public async Task<SheetDto> FillStandardDaysAsync(string employeeCode, int year, int month)
    {
        var sheet = await EditableAsync(employeeCode, year, month);
        return await _builder.BuildAsync(sheet);
    }

    /// <summary>
    /// The default of the month: everybody worked the standard day on every working day, so those days are filled in when the month is
    /// opened. The employee then enters only the exceptions (an absence, overtime, work on a holiday). Returns whether anything was added.
    /// </summary>
    private async Task<bool> AddStandardDaysAsync(MonthSheet sheet)
    {
        var context = await _builder.ContextAsync(sheet.EmployeeCode, sheet.Year);
        var (year, month) = (sheet.Year, sheet.Month);
        var added = 0;

        foreach (var date in WorkCalendar.DaysOf(year, month))
        {
            if (!context.Employee.IsActiveOn(date)) continue;
            if (context.Calendar.Resolve(date, context.Religious).Type != DayType.Working) continue;
            if (sheet.Entries.Any(e => e.Date == date && TimeCodes.Get(e.Code).IsBaseEntry)) continue;

            sheet.Entries.Add(new SheetEntry { Date = date, Code = Codes.RegularWork, Minutes = WorkingTimeSettings.Default.DailyNormMinutes });
            added++;
        }

        if (added == 0) return false;

        Touch(sheet, "StandardDaysFilled", $"{year}-{month:00}: {added} days");
        await SaveAsync();
        return true;
    }

    // ---- Absence ranges ----------------------------------------------------------------------------------------------

    public async Task<AbsencePreviewDto> PreviewAbsenceAsync(string employeeCode, int year, int month, AbsenceRangeRequest request)
    {
        CheckMonth(year, month);
        var expansion = await ExpandAsync(employeeCode, year, month, request);
        return new AbsencePreviewDto(
            request.Code,
            expansion.Days,
            expansion.Skipped.Select(s => new SkippedDayDto(s.Date, s.Reason.ToDto())).ToList());
    }

    public async Task<SheetDto> ApplyAbsenceAsync(string employeeCode, int year, int month, AbsenceRangeRequest request)
    {
        var sheet = await EditableAsync(employeeCode, year, month);
        var expansion = await ExpandAsync(employeeCode, year, month, request);

        foreach (var date in expansion.Days)
        {
            var existing = sheet.Entries.Where(e => e.Date == date && TimeCodes.Get(e.Code).IsBaseEntry).ToList();
            foreach (var old in existing) _db.SheetEntries.Remove(old);
            sheet.Entries.RemoveAll(existing.Contains);
            sheet.Entries.Add(new SheetEntry { Date = date, Code = request.Code, Minutes = 0 });
        }

        Touch(sheet, "AbsenceRangeApplied", $"{request.Code} {request.From:yyyy-MM-dd}..{request.To:yyyy-MM-dd}: {expansion.Days.Count} days");
        await SaveAsync();
        return await _builder.BuildAsync(sheet);
    }

    private async Task<AbsenceExpansion> ExpandAsync(string employeeCode, int year, int month, AbsenceRangeRequest request)
    {
        // The range must lie inside the month being filled in
        CheckDate(request.From, year, month);
        CheckDate(request.To, year, month);

        var context = await _builder.ContextAsync(employeeCode, year);
        try
        {
            return AbsenceRanges.Expand(request.Code, request.From, request.To, context.Calendar, context.Religious);
        }
        catch (ArgumentException ex)
        {
            throw new SheetException(400, "invalid-absence", ex.Message);
        }
    }

    // ---- Overtime --------------------------------------------------------------------------------------------------

    public async Task<SheetDto> AddOvertimeAsync(string employeeCode, int year, int month, OvertimeRequest request)
    {
        var sheet = await EditableAsync(employeeCode, year, month);
        Check(request, year, month);
        await CheckEmploymentAsync(employeeCode, year, request.Date);

        sheet.Overtime.Add(new SheetOvertime { Date = request.Date, Start = request.Start, End = request.End, Description = Limits.Check(request.Description, Limits.OvertimeDescription, "The description") });
        Touch(sheet, "OvertimeAdded", $"{request.Date:yyyy-MM-dd} {request.Start:HH\\:mm}-{request.End:HH\\:mm}");
        await SaveAsync();
        return await _builder.BuildAsync(sheet);
    }

    public async Task<SheetDto> UpdateOvertimeAsync(string employeeCode, int year, int month, int id, OvertimeRequest request)
    {
        var sheet = await EditableAsync(employeeCode, year, month);
        var record = sheet.Overtime.FirstOrDefault(o => o.Id == id) ?? throw new SheetException(404, "not-found", "There is no such overtime record.");
        Check(request, year, month);

        record.Date = request.Date;
        record.Start = request.Start;
        record.End = request.End;
        await CheckEmploymentAsync(employeeCode, year, request.Date);
        record.Description = Limits.Check(request.Description, Limits.OvertimeDescription, "The description");
        Touch(sheet, "OvertimeChanged", $"{id}: {request.Date:yyyy-MM-dd} {request.Start:HH\\:mm}-{request.End:HH\\:mm}");
        await SaveAsync();
        return await _builder.BuildAsync(sheet);
    }

    public async Task<SheetDto> DeleteOvertimeAsync(string employeeCode, int year, int month, int id)
    {
        var sheet = await EditableAsync(employeeCode, year, month);
        var record = sheet.Overtime.FirstOrDefault(o => o.Id == id) ?? throw new SheetException(404, "not-found", "There is no such overtime record.");

        _db.SheetOvertime.Remove(record);
        sheet.Overtime.Remove(record);
        Touch(sheet, "OvertimeDeleted", $"{id}: {record.Date:yyyy-MM-dd}");
        await SaveAsync();
        return await _builder.BuildAsync(sheet);
    }

    /// <summary>Nothing is entered for a day before the employment began or after it ended</summary>
    private async Task CheckEmploymentAsync(string employeeCode, int year, DateOnly date)
    {
        var context = await _builder.ContextAsync(employeeCode, year);
        if (!context.Employee.IsActiveOn(date))
            throw new SheetException(400, "outside-employment", "This date is outside your employment.");
    }

    private static void Check(OvertimeRequest request, int year, int month)
    {
        CheckDate(request.Date, year, month);
        if (new OvertimeInterval(request.Date, request.Start, request.End).TotalMinutes == 0)
            throw new SheetException(400, "invalid-interval", "The start and the end must differ.");
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------------

    private static bool IsEditable(MonthSheet sheet) => SheetDtoBuilder.IsEditable(sheet);

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Saves, and turns what a busy server can run into into a message the app can show: someone changed the sheet a moment
    /// ago (the version token), or the same row was created twice.
    /// </summary>
    internal async Task SaveAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new SheetException(409, "changed", "The timesheet was changed a moment ago. Open it again.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new SheetException(409, "changed", "The same thing was entered twice at once. Open the timesheet again.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // SQLite: 19 (constraint); PostgreSQL: SQLSTATE 23505. Anything else is not ours to hide.
        var text = ex.InnerException?.Message ?? ex.Message;
        return text.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase) || text.Contains("23505", StringComparison.Ordinal)
               || text.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }

    internal async Task<MonthSheet> LoadOrCreateAsync(string employeeCode, int year, int month)
    {
        async Task<MonthSheet?> Load() => await _db.Sheets.WithDetails().FirstOrDefaultAsync(s => s.EmployeeCode == employeeCode && s.Year == year && s.Month == month);

        var sheet = await Load() ?? await Create();
        if (!IsEditable(sheet)) return sheet;

        try
        {
            await AddStandardDaysAsync(sheet);
            return sheet;
        }
        catch (SheetException ex) when (ex.Code == "changed")
        {
            // A second request filled the same month first: take its sheet
            _db.ChangeTracker.Clear();
            return await Load() ?? throw ex;
        }

        async Task<MonthSheet> Create()
        {
            var created = new MonthSheet { EmployeeCode = employeeCode, Year = year, Month = month, CreatedUtc = UtcNow, UpdatedUtc = UtcNow };
            _db.Sheets.Add(created);
            try
            {
                await _db.SaveChangesAsync();
                return created;
            }
            catch (DbUpdateException)
            {
                // A second request opened the same new month first: take its sheet
                _db.Entry(created).State = EntityState.Detached;
                return await Load() ?? throw new SheetException(409, "changed", "The timesheet could not be opened. Try again.");
            }
        }
    }

    private async Task<MonthSheet> EditableAsync(string employeeCode, int year, int month)
    {
        CheckMonth(year, month);
        var sheet = await LoadOrCreateAsync(employeeCode, year, month);
        if (!IsEditable(sheet))
            throw new SheetException(409, "locked", "The timesheet is submitted or approved and cannot be changed.");
        return sheet;
    }

    internal void Touch(MonthSheet sheet, string action, string details)
    {
        sheet.UpdatedUtc = UtcNow;
        _audit.Record(action, "MonthSheet", $"{sheet.EmployeeCode}/{sheet.Year}-{sheet.Month:00}", details);
    }

    private static void CheckMonth(int year, int month)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            throw new SheetException(400, "invalid-month", "Choose a month between 2000 and 2100.");
    }

    private static void CheckDate(DateOnly date, int year, int month)
    {
        if (date.Year != year || date.Month != month)
            throw new SheetException(400, "outside-month", "The date is not in this month.");
    }
}
