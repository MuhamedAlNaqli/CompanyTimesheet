namespace Company.Api.Data;

/// <summary>A person of the organisation: master data, place in the chart and the roles that follow from it</summary>
public class Employee
{
    public int Id { get; set; }

    /// <summary>Z-NNN, unique</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public bool IsActive { get; set; } = true;

    /// <summary>The person this employee reports to; the timesheet climbs this chain</summary>
    public string? SupervisorCode { get; set; }

    /// <summary>The lawyer: the approval chain of employees below stops here</summary>
    public bool IsFinalApprover { get; set; }

    /// <summary>Approver for the person at the top of the chart, who has no supervisor</summary>
    public string? DesignatedApproverCode { get; set; }

    public bool IsAdmin { get; set; }

    /// <summary>Finance: receives the approved timesheets of everyone for the calculation (reads only)</summary>
    public bool IsFinance { get; set; }

    public DateOnly? ActiveFrom { get; set; }

    public DateOnly? ActiveTo { get; set; }

    /// <summary>For the reminder mails (Features:EmailReminders); optional</summary>
    public string? Email { get; set; }

    public List<EmployeeAccount> Accounts { get; set; } = new();
}

/// <summary>The reminder mail an employee got on a day: at most one a day, and this is how the server knows</summary>
public class ReminderMail
{
    public int Id { get; set; }

    public string EmployeeCode { get; set; } = "";

    public DateOnly Day { get; set; }

    public DateTime SentUtc { get; set; }

    /// <summary>How many items the mail listed</summary>
    public int Items { get; set; }
}

/// <summary>A sign-in name (DOMAIN\user, user@domain or an Entra name) linked to an employee</summary>
public class EmployeeAccount
{
    public int Id { get; set; }

    public string EmployeeCode { get; set; } = "";

    /// <summary>Lower case, unique</summary>
    public string Account { get; set; } = "";
}

public class StateHoliday
{
    public int Id { get; set; }

    public DateOnly Date { get; set; }

    public string Name { get; set; } = "";
}

/// <summary>A religious holiday marked for one employee (Q-14: holidays are per employee)</summary>
public class ReligiousHoliday
{
    public int Id { get; set; }

    public string EmployeeCode { get; set; } = "";

    public DateOnly Date { get; set; }

    public string Name { get; set; } = "";
}

/// <summary>An entry of the append-only audit log. Nothing in the application updates or deletes these rows.</summary>
public class AuditEvent
{
    public long Id { get; set; }

    public DateTime AtUtc { get; set; }

    public string Actor { get; set; } = "";

    public string Action { get; set; } = "";

    public string EntityType { get; set; } = "";

    public string EntityId { get; set; } = "";

    public string? Details { get; set; }

    /// <summary>Hash of the event before this one; with <see cref="Hash"/> it links the log into a chain that shows any change afterwards</summary>
    public string? PreviousHash { get; set; }

    /// <summary>SHA-256 of the previous hash and the fields of this event (null only for events written before the chain existed)</summary>
    public string? Hash { get; set; }
}

/// <summary>The layout of the stored revision of a sheet. Raise it when the layout of <c>SheetDto</c> changes in a way an old revision cannot be read.</summary>
public static class SnapshotSchema
{
    public const int Current = 1;
}

/// <summary>One employee's timesheet for one month</summary>
public class MonthSheet
{
    public int Id { get; set; }

    public string EmployeeCode { get; set; } = "";

    public int Year { get; set; }

    public int Month { get; set; }

    public Timesheet.Domain.TimesheetStatus Status { get; set; } = Timesheet.Domain.TimesheetStatus.Draft;

    public int Revision { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    /// <summary>Changes with every save of the sheet, so two people acting at the same moment cannot both win</summary>
    public int Version { get; set; }

    /// <summary>Position in <see cref="ChainSteps"/> of the person whose turn it is</summary>
    public int Step { get; set; }

    /// <summary>The person whose turn it is while the sheet is submitted, otherwise null; makes "waiting for me" one indexed query</summary>
    public string? CurrentApproverCode { get; set; }

    public List<SheetEntry> Entries { get; set; } = new();

    public List<SheetOvertime> Overtime { get; set; } = new();

    public List<SheetMessage> Messages { get; set; } = new();

    public List<SheetEvent> Events { get; set; } = new();

    /// <summary>The approvers frozen at the last submission, in the order they decide</summary>
    public List<SheetChainStep> ChainSteps { get; set; } = new();

    /// <summary>The employee codes of the approvers, in order</summary>
    public List<string> ChainCodes => ChainSteps.OrderBy(s => s.Position).Select(s => s.EmployeeCode).ToList();

    /// <summary>
    /// Makes the frozen chain this one. Steps are changed where they are and only added or removed at the end, so that the unique
    /// order of the steps is never broken in the middle of a save.
    /// </summary>
    public void SetChain(IReadOnlyList<string> codes)
    {
        var steps = ChainSteps.OrderBy(s => s.Position).ToList();
        for (var i = 0; i < codes.Count; i++)
        {
            if (i < steps.Count) steps[i].EmployeeCode = codes[i];
            else ChainSteps.Add(new SheetChainStep { Position = i, EmployeeCode = codes[i] });
        }

        foreach (var surplus in steps.Skip(codes.Count)) ChainSteps.Remove(surplus);
    }
}

/// <summary>One approver of the chain a sheet was submitted to</summary>
public class SheetChainStep
{
    public int Id { get; set; }

    public int SheetId { get; set; }

    /// <summary>0 for the first approver</summary>
    public int Position { get; set; }

    public string EmployeeCode { get; set; } = "";
}

/// <summary>A message with which an approver returned the sheet, and the employee's answer</summary>
public class SheetMessage
{
    public int Id { get; set; }

    public int SheetId { get; set; }

    /// <summary>Order of the messages of the sheet, starting at 0</summary>
    public int Position { get; set; }

    public string FromCode { get; set; } = "";

    public string Text { get; set; } = "";

    public int Revision { get; set; }

    public string? Answer { get; set; }

    public DateTime CreatedUtc { get; set; }
}

/// <summary>What happened to a sheet (submitted, approved, returned, answered), shown as its history</summary>
public class SheetEvent
{
    public int Id { get; set; }

    public int SheetId { get; set; }

    public DateTime AtUtc { get; set; }

    public Timesheet.Domain.WorkflowEventKind Kind { get; set; }

    public string ActorCode { get; set; } = "";

    public string? OnBehalfOf { get; set; }

    public int Revision { get; set; }

    public int Step { get; set; }

    public string? Text { get; set; }
}

/// <summary>An approver is away for a period; the deputy decides on what waits for them</summary>
public class ApproverDeputy
{
    public int Id { get; set; }

    public string ApproverCode { get; set; } = "";

    public string DeputyCode { get; set; } = "";

    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }
}

/// <summary>The user name and password the administrator created for an employee (sign-in without Windows)</summary>
public class EmployeeCredential
{
    public int Id { get; set; }

    public string EmployeeCode { get; set; } = "";

    /// <summary>Lower case; also linked to the employee as the account "local:username"</summary>
    public string Username { get; set; } = "";

    /// <summary>Salted hash, never the password (see Passwords)</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>The administrator set the password: the employee chooses their own at the first sign-in</summary>
    public bool MustChange { get; set; }

    public int FailedCount { get; set; }

    public DateTime? LockedUntilUtc { get; set; }

    public DateTime ChangedUtc { get; set; }
}

/// <summary>A sign-in that is still valid: only the hash of the token is stored</summary>
public class AuthSession
{
    public int Id { get; set; }

    public string TokenHash { get; set; } = "";

    public string EmployeeCode { get; set; } = "";

    public DateTime CreatedUtc { get; set; }

    public DateTime ExpiresUtc { get; set; }
}

/// <summary>A supervisor asked a person to submit the timesheet of a month; the person's computer shows it as a notification</summary>
public class TeamNudge
{
    public int Id { get; set; }

    public string EmployeeCode { get; set; } = "";

    public string FromCode { get; set; } = "";

    public int Year { get; set; }

    public int Month { get; set; }

    public DateTime CreatedUtc { get; set; }
}

/// <summary>What the sheet looked like when it was submitted, so a later correction can be compared with it</summary>
public class SheetRevision
{
    public int Id { get; set; }

    public int SheetId { get; set; }

    public int Revision { get; set; }

    public DateTime SubmittedUtc { get; set; }

    /// <summary>The version of the layout of <see cref="Json"/>; a stored revision is read by the layout it was written in</summary>
    public int SchemaVersion { get; set; } = SnapshotSchema.Current;

    public string Json { get; set; } = "";
}

/// <summary>A regular-hours, absence or hour-based code on a date. Overtime is not stored here; it is derived from the intervals.</summary>
public class SheetEntry
{
    public int Id { get; set; }

    public int SheetId { get; set; }

    public DateOnly Date { get; set; }

    public int Code { get; set; }

    /// <summary>Zero for whole-day absence codes</summary>
    public int Minutes { get; set; }
}

public class SheetOvertime
{
    public int Id { get; set; }

    public int SheetId { get; set; }

    public DateOnly Date { get; set; }

    public TimeOnly Start { get; set; }

    public TimeOnly End { get; set; }

    public string Description { get; set; } = "";
}
