using System.Text.Json;
using Company.Api.Contracts;
using Company.Api.Data;
using Microsoft.EntityFrameworkCore;
using Timesheet.Domain;

namespace Company.Api.Services;

/// <summary>
/// Submitting a month and walking it up the approval chain. The status machine is the domain library's
/// <see cref="TimesheetWorkflow"/>; this service loads the state, applies the transition, keeps the history and the
/// submitted revision, and saves it all in one step. Only an explicit action of the right person moves a sheet on.
/// </summary>
public sealed class WorkflowService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly CompanyDbContext _db;
    private readonly TimesheetService _sheets;
    private readonly SheetDtoBuilder _builder;
    private readonly ChartService _chart;
    private readonly AuditService _audit;
    private readonly TimeProvider _time;
    private readonly DeputyService _deputies;

    public WorkflowService(CompanyDbContext db, TimesheetService sheets, SheetDtoBuilder builder, ChartService chart, AuditService audit, TimeProvider time, DeputyService deputies)
    {
        _deputies = deputies;
        _db = db;
        _sheets = sheets;
        _builder = builder;
        _chart = chart;
        _audit = audit;
        _time = time;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    private static bool Same(string a, string b) => SheetDtoBuilder.Same(a, b);

    // ---- The employee ----------------------------------------------------------------------------------------------

    public async Task<SheetDto> SubmitAsync(string employeeCode, int year, int month)
    {
        var sheet = await _sheets.LoadOrCreateAsync(employeeCode, year, month);
        var current = await _builder.BuildAsync(sheet);

        var outcome = await _chart.ChainOfAsync(employeeCode);
        if (outcome.Chain == null)
            throw new SheetException(409, "no-chain", "The administrator has not set who approves your timesheet yet.");

        var result = TimesheetWorkflow.Submit(ToState(sheet), employeeCode, outcome.Chain, current.Issues.Any(i => i.Severity == IssueSeverity.Blocking));
        Apply(sheet, result);

        // What was submitted is kept, so a later correction can be compared with it
        var frozen = await _builder.BuildAsync(sheet);
        _db.SheetRevisions.Add(new SheetRevision
        {
            SheetId = sheet.Id,
            Revision = sheet.Revision,
            SubmittedUtc = UtcNow,
            SchemaVersion = SnapshotSchema.Current,
            Json = JsonSerializer.Serialize(frozen with { Workflow = null }, Json)
        });

        await _sheets.SaveAsync();
        return await _builder.BuildAsync(sheet);
    }

    public async Task<SheetDto> AnswerAsync(string employeeCode, int year, int month, int index, string answer)
    {
        var text = Limits.Check(answer, Limits.Message, "The answer");
        var sheet = await _sheets.LoadOrCreateAsync(employeeCode, year, month);
        Apply(sheet, TimesheetWorkflow.Answer(ToState(sheet), employeeCode, index, text));
        await _sheets.SaveAsync();
        return await _builder.BuildAsync(sheet);
    }

    // ---- The approvers ---------------------------------------------------------------------------------------------

    /// <summary>The submitted timesheets that are waiting for this person right now (one indexed query on the current approver)</summary>
    public async Task<List<PendingApprovalDto>> PendingAsync(string approverCode)
    {
        // What waits for this person, and what waits for the approvers they stand in for today
        var covered = await _deputies.ApproversActedForAsync(approverCode);
        var waiting = await _db.Sheets.AsNoTracking().WithDetails()
            .Where(s => s.Status == TimesheetStatus.Submitted && s.CurrentApproverCode != null && (s.CurrentApproverCode == approverCode || (covered.Contains(s.CurrentApproverCode) && s.EmployeeCode != approverCode))).ToListAsync();
        var names = await _db.Employees.AsNoTracking().Where(e => covered.Contains(e.Code)).ToDictionaryAsync(e => e.Code, e => e.Name);

        var result = new List<PendingApprovalDto>();
        foreach (var sheet in waiting)
        {
            var dto = await _builder.BuildAsync(sheet, approverCode);
            var actingFor = sheet.CurrentApproverCode != approverCode ? names.GetValueOrDefault(sheet.CurrentApproverCode!) : null;
            var sent = sheet.Events.Where(e => e.Kind == WorkflowEventKind.Submitted && e.Revision == sheet.Revision).Select(e => e.AtUtc).DefaultIfEmpty(sheet.UpdatedUtc).Max();
            result.Add(new PendingApprovalDto(
                sheet.EmployeeCode, dto.Workflow!.OwnerName, sheet.Year, sheet.Month, sheet.Revision, sheet.Step,
                DateTime.SpecifyKind(sent, DateTimeKind.Utc),
                dto.Issues.Count(i => i.Severity == IssueSeverity.Blocking),
                dto.Issues.Count(i => i.Severity != IssueSeverity.Blocking),
                actingFor));
        }

        return result.OrderBy(p => p.SubmittedUtc).ThenBy(p => p.EmployeeName).ToList();
    }

    /// <summary>Someone else's sheet, read only. Opening it is written to the audit log.</summary>
    public async Task<SheetDto> ReviewAsync(string viewerCode, string ownerCode, int year, int month)
    {
        var sheet = await FindForReviewAsync(viewerCode, ownerCode, year, month);
        await AuditViewAsync(sheet, viewerCode, "SheetViewed");
        return await _builder.BuildAsync(sheet, viewerCode);
    }

    public async Task<SheetDto> ApproveAsync(string approverCode, string ownerCode, int year, int month)
    {
        var sheet = await FindForReviewAsync(approverCode, ownerCode, year, month);
        var covered = await _deputies.ApproversActedForAsync(approverCode);
        Apply(sheet, TimesheetWorkflow.Approve(ToState(sheet), approverCode, (_, current) => covered.Contains(current, StringComparer.OrdinalIgnoreCase)));
        await _sheets.SaveAsync();
        return await _builder.BuildAsync(sheet, approverCode);
    }

    public async Task<SheetDto> ReturnAsync(string approverCode, string ownerCode, int year, int month, string message)
    {
        var text = Limits.Check(message, Limits.Message, "The message");
        var sheet = await FindForReviewAsync(approverCode, ownerCode, year, month);
        var covered = await _deputies.ApproversActedForAsync(approverCode);
        Apply(sheet, TimesheetWorkflow.Return(ToState(sheet), approverCode, text, (_, current) => covered.Contains(current, StringComparer.OrdinalIgnoreCase)));
        await _sheets.SaveAsync();
        return await _builder.BuildAsync(sheet, approverCode);
    }

    /// <summary>The sheet exactly as it was submitted in the given revision</summary>
    public async Task<SheetDto> RevisionAsync(string viewerCode, string ownerCode, int year, int month, int revision)
    {
        var sheet = await FindForReviewAsync(viewerCode, ownerCode, year, month);
        var stored = await _db.SheetRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.SheetId == sheet.Id && r.Revision == revision)
                     ?? throw new SheetException(404, "not-found", "There is no such revision.");
        await AuditViewAsync(sheet, viewerCode, "RevisionViewed", $"revision {revision}");
        var dto = ReadRevision(stored);
        return dto with { Editable = false, Workflow = (await _builder.BuildAsync(sheet, viewerCode)).Workflow };
    }

    /// <summary>A stored revision is read by the layout it was written in; one from a newer version, or a damaged one, is refused clearly</summary>
    private static SheetDto ReadRevision(SheetRevision stored)
    {
        if (stored.SchemaVersion > SnapshotSchema.Current)
            throw new SheetException(409, "revision-too-new", "This revision was written by a newer version of the server.");

        try
        {
            return JsonSerializer.Deserialize<SheetDto>(stored.Json, Json) ?? throw new JsonException("empty");
        }
        catch (JsonException)
        {
            throw new SheetException(409, "revision-unreadable", "This revision cannot be read any more.");
        }
    }

    // ---- The administrator -----------------------------------------------------------------------------------------

    /// <summary>
    /// Hands the step a submitted timesheet is waiting at to somebody else, for example when the approver has left. The change
    /// is in the history of the sheet and in the audit log, with the reason.
    /// </summary>
    public async Task<SheetDto> ReassignAsync(string adminCode, string ownerCode, int year, int month, string toCode, string reason)
    {
        var why = Limits.Check(reason, Limits.Message, "The reason");
        if (why.Length == 0) throw new SheetException(400, "message-required", "Write why the step is handed to somebody else.");

        var sheet = await _sheets.LoadOrCreateAsync(ownerCode, year, month);
        if (sheet.Status != TimesheetStatus.Submitted || sheet.Step >= sheet.ChainCodes.Count)
            throw new SheetException(409, "wrong-status", "Only a submitted timesheet has a step that can be handed on.");

        var target = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Code == toCode && e.IsActive)
                     ?? throw new SheetException(404, "not-found", "The new approver is not an active employee.");
        if (Same(target.Code, ownerCode) && sheet.ChainCodes.Count > 1)
            throw new SheetException(409, "self-approval", "Nobody approves their own timesheet.");

        var chain = sheet.ChainCodes;
        var from = chain[sheet.Step];
        chain[sheet.Step] = target.Code;
        sheet.SetChain(chain);
        sheet.CurrentApproverCode = target.Code;

        sheet.Events.Add(new SheetEvent
        {
            AtUtc = UtcNow, Kind = WorkflowEventKind.Reassigned, ActorCode = adminCode, Revision = sheet.Revision, Step = sheet.Step, Text = $"{from} -> {target.Code}: {why}"
        });
        _sheets.Touch(sheet, nameof(WorkflowEventKind.Reassigned), $"step {sheet.Step}: {from} -> {target.Code}: {why}");
        await _sheets.SaveAsync();
        return await _builder.BuildAsync(sheet, adminCode);
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Who may look at somebody else's sheet: the people it was submitted to while it is in the chain, anybody who took part in
    /// it, and the supervisors the owner has today (once it was submitted at all). A person who has been moved away from the
    /// owner, and never took part, no longer sees it.
    /// </summary>
    internal async Task<MonthSheet> FindForReviewAsync(string viewerCode, string ownerCode, int year, int month)
    {
        var sheet = await _db.Sheets.WithDetails().FirstOrDefaultAsync(s => s.EmployeeCode == ownerCode && s.Year == year && s.Month == month);

        if (sheet == null || !await MayViewAsync(sheet, viewerCode))
            throw new SheetException(403, "forbidden", "You are not allowed to see this timesheet.");
        return sheet;
    }

    private async Task<bool> MayViewAsync(MonthSheet sheet, string viewer)
    {
        if (Same(viewer, sheet.EmployeeCode)) return true;
        if (sheet.Status == TimesheetStatus.Submitted && sheet.ChainCodes.Any(c => Same(c, viewer))) return true;
        if (sheet.Status == TimesheetStatus.Submitted && sheet.CurrentApproverCode != null && await _deputies.IsDeputyForAsync(viewer, sheet.CurrentApproverCode)) return true;
        if (sheet.Events.Any(e => Same(e.ActorCode, viewer) || (e.OnBehalfOf != null && Same(e.OnBehalfOf, viewer)))) return true;
        if (sheet.Revision == 0) return false;

        var today = await _chart.ChainOfAsync(sheet.EmployeeCode);
        return today.Chain?.Approvers.Any(c => Same(c, viewer)) == true;
    }

    /// <summary>Opening a colleague's sheet leaves a trace; the owner looking at their own does not</summary>
    internal async Task AuditViewAsync(MonthSheet sheet, string viewerCode, string action, string? details = null)
    {
        if (Same(viewerCode, sheet.EmployeeCode)) return;

        _audit.Record(action, "MonthSheet", $"{sheet.EmployeeCode}/{sheet.Year}-{sheet.Month:00}", $"viewed by {viewerCode}" + (details == null ? "" : ", " + details), viewerCode);
        await _sheets.SaveAsync();
    }

    private static TimesheetState ToState(MonthSheet sheet) => new(
        sheet.EmployeeCode,
        sheet.Status,
        sheet.Revision,
        sheet.ChainCodes,
        sheet.Step,
        sheet.Messages.OrderBy(m => m.Position).Select(m => new ReturnMessage(m.FromCode, m.Text, m.Revision, m.Answer)).ToList());

    /// <summary>Writes the new state back to the sheet and records the event, in the history and in the audit log</summary>
    private void Apply(MonthSheet sheet, Transition transition)
    {
        if (!transition.Ok) throw Refusal(transition.Error);

        var state = transition.State;
        var ev = transition.Event!;

        sheet.Status = state.Status;
        sheet.Revision = state.Revision;
        sheet.Step = state.Step;
        sheet.SetChain(state.ChainOrEmpty.ToList());
        sheet.CurrentApproverCode = state.CurrentApprover;

        // Messages: a return adds one, an answer fills one
        var messages = state.MessagesOrEmpty;
        for (var i = 0; i < messages.Count; i++)
        {
            var existing = sheet.Messages.FirstOrDefault(m => m.Position == i);
            if (existing == null)
            {
                sheet.Messages.Add(new SheetMessage
                {
                    Position = i, FromCode = messages[i].From, Text = messages[i].Text, Revision = messages[i].Revision, Answer = messages[i].Answer, CreatedUtc = UtcNow
                });
            }
            else
            {
                existing.Answer = messages[i].Answer;
            }
        }

        sheet.Events.Add(new SheetEvent
        {
            AtUtc = UtcNow, Kind = ev.Kind, ActorCode = ev.Actor, OnBehalfOf = ev.OnBehalfOf, Revision = ev.Revision, Step = ev.Step, Text = ev.Text
        });

        _sheets.Touch(sheet, ev.Kind.ToString(), $"revision {ev.Revision}, step {ev.Step}" + (ev.Text == null ? "" : ": " + ev.Text));
    }

    private static SheetException Refusal(WorkflowError error) => error switch
    {
        WorkflowError.NotAllowedInThisStatus => new(409, "wrong-status", "This is not possible in the current status of the timesheet."),
        WorkflowError.NotTheOwner => new(403, "forbidden", "Only the owner of the timesheet can do this."),
        WorkflowError.NotTheCurrentApprover => new(403, "not-your-turn", "It is not your turn to decide on this timesheet."),
        WorkflowError.SelfApproval => new(403, "self-approval", "Nobody approves their own timesheet."),
        WorkflowError.BlockingIssues => new(409, "blocking-issues", "Fix the blocking findings before submitting."),
        WorkflowError.UnansweredMessages => new(409, "unanswered-messages", "Answer every message of the approver before submitting again."),
        WorkflowError.MessageRequired => new(400, "message-required", "A text is required."),
        WorkflowError.EmptyChain => new(409, "no-chain", "The administrator has not set who approves your timesheet yet."),
        WorkflowError.UnknownMessage => new(404, "not-found", "There is no such message."),
        WorkflowError.AlreadyAnswered => new(409, "already-answered", "This message has been answered already."),
        _ => new(400, "invalid", "The request is not valid.")
    };
}
