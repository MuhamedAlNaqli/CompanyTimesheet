namespace Timesheet.Domain;

public enum TimesheetStatus
{
    /// <summary>The employee is filling in the month</summary>
    Draft,

    /// <summary>Frozen and waiting at one step of the approval chain</summary>
    Submitted,

    /// <summary>Returned to the employee with a message</summary>
    Returned,

    /// <summary>Every step of the chain approved; locked</summary>
    Approved
}

public sealed record ReturnMessage(string From, string Text, int Revision, string? Answer = null)
{
    public bool IsAnswered => !string.IsNullOrWhiteSpace(Answer);
}

/// <summary>The state of one employee timesheet (one employee, one month)</summary>
public sealed record TimesheetState(
    string OwnerCode,
    TimesheetStatus Status = TimesheetStatus.Draft,
    int Revision = 0,
    IReadOnlyList<string>? Chain = null,
    int Step = 0,
    IReadOnlyList<ReturnMessage>? Messages = null)
{
    public IReadOnlyList<string> ChainOrEmpty => Chain ?? Array.Empty<string>();

    public IReadOnlyList<ReturnMessage> MessagesOrEmpty => Messages ?? Array.Empty<ReturnMessage>();

    /// <summary>Whose turn it is, while the timesheet is submitted</summary>
    public string? CurrentApprover => Status == TimesheetStatus.Submitted && Step < ChainOrEmpty.Count ? ChainOrEmpty[Step] : null;

    public bool IsEditableByOwner => Status is TimesheetStatus.Draft or TimesheetStatus.Returned;
}

public enum WorkflowError
{
    None,
    NotAllowedInThisStatus,
    NotTheOwner,
    NotTheCurrentApprover,

    /// <summary>Nobody approves their own timesheet</summary>
    SelfApproval,

    BlockingIssues,
    UnansweredMessages,
    MessageRequired,
    EmptyChain,
    UnknownMessage,

    /// <summary>The message already has an answer; what was answered stays as it was</summary>
    AlreadyAnswered
}

/// <summary>What can happen to a timesheet</summary>
public enum WorkflowEventKind
{
    Submitted,
    StepApproved,
    Approved,
    Returned,
    Answered,

    /// <summary>The administrator handed the waiting step to somebody else (not a transition of the state machine)</summary>
    Reassigned
}

/// <summary>What happened, for the audit log and the notifications</summary>
public sealed record WorkflowEvent(WorkflowEventKind Kind, string Actor, string? OnBehalfOf, int Revision, int Step, string? Text);

public sealed record Transition(TimesheetState State, WorkflowError Error, WorkflowEvent? Event)
{
    public bool Ok => Error == WorkflowError.None;
}

/// <summary>
/// The status machine of one timesheet, as pure functions. Humans decide: a timesheet is only ever approved by an explicit
/// <see cref="Approve"/> of the right person. Each submission is a revision; a return always goes to the employee
/// and the resubmission starts again at the first step of the chain.
/// </summary>
public static class TimesheetWorkflow
{
    private static Transition Fail(TimesheetState state, WorkflowError error) => new(state, error, null);

    /// <summary>The employee submits (or resubmits) their month. The chain in force now is frozen with the revision.</summary>
    public static Transition Submit(TimesheetState state, string actor, ApprovalChain chain, bool hasBlockingIssues)
    {
        if (!state.IsEditableByOwner) return Fail(state, WorkflowError.NotAllowedInThisStatus);
        if (!Same(actor, state.OwnerCode)) return Fail(state, WorkflowError.NotTheOwner);
        if (chain.Approvers.Count == 0) return Fail(state, WorkflowError.EmptyChain);
        // Nobody approves their own timesheet, except a person with nobody above them: their chain is themselves alone
        if (chain.Approvers.Any(a => Same(a, state.OwnerCode)) && chain.Approvers.Count > 1) return Fail(state, WorkflowError.SelfApproval);
        if (hasBlockingIssues) return Fail(state, WorkflowError.BlockingIssues);
        if (state.MessagesOrEmpty.Any(m => !m.IsAnswered)) return Fail(state, WorkflowError.UnansweredMessages);

        var next = state with { Status = TimesheetStatus.Submitted, Revision = state.Revision + 1, Chain = chain.Approvers.ToList(), Step = 0 };
        return new Transition(next, WorkflowError.None, new WorkflowEvent(WorkflowEventKind.Submitted, actor, null, next.Revision, 0, null));
    }

    /// <summary>
    /// The current approver approves; the timesheet moves to the next step, or becomes approved after the last one.
    /// A deputy may act for the current approver (<paramref name="isDeputyFor"/>); the event records both.
    /// </summary>
    public static Transition Approve(TimesheetState state, string actor, Func<string, string, bool>? isDeputyFor = null)
    {
        var check = CheckApprover(state, actor, isDeputyFor, out var onBehalfOf);
        if (check != WorkflowError.None) return Fail(state, check);

        var step = state.Step;
        var last = step >= state.ChainOrEmpty.Count - 1;
        var next = last ? state with { Status = TimesheetStatus.Approved } : state with { Step = step + 1 };
        return new Transition(next, WorkflowError.None, new WorkflowEvent(last ? WorkflowEventKind.Approved : WorkflowEventKind.StepApproved, actor, onBehalfOf, state.Revision, step, null));
    }

    /// <summary>The current approver returns the timesheet to the employee. The message is mandatory.</summary>
    public static Transition Return(TimesheetState state, string actor, string message, Func<string, string, bool>? isDeputyFor = null)
    {
        var check = CheckApprover(state, actor, isDeputyFor, out var onBehalfOf);
        if (check != WorkflowError.None) return Fail(state, check);
        if (string.IsNullOrWhiteSpace(message)) return Fail(state, WorkflowError.MessageRequired);

        var messages = state.MessagesOrEmpty.Append(new ReturnMessage(onBehalfOf ?? actor, message.Trim(), state.Revision)).ToList();
        var next = state with { Status = TimesheetStatus.Returned, Messages = messages, Step = 0 };
        return new Transition(next, WorkflowError.None, new WorkflowEvent(WorkflowEventKind.Returned, actor, onBehalfOf, state.Revision, state.Step, message.Trim()));
    }

    /// <summary>The employee answers one return message; resubmission needs every message answered.</summary>
    public static Transition Answer(TimesheetState state, string actor, int messageIndex, string answer)
    {
        if (state.Status != TimesheetStatus.Returned) return Fail(state, WorkflowError.NotAllowedInThisStatus);
        if (!Same(actor, state.OwnerCode)) return Fail(state, WorkflowError.NotTheOwner);
        var messages = state.MessagesOrEmpty.ToList();
        if (messageIndex < 0 || messageIndex >= messages.Count) return Fail(state, WorkflowError.UnknownMessage);
        if (messages[messageIndex].IsAnswered) return Fail(state, WorkflowError.AlreadyAnswered);
        if (string.IsNullOrWhiteSpace(answer)) return Fail(state, WorkflowError.MessageRequired);

        messages[messageIndex] = messages[messageIndex] with { Answer = answer.Trim() };
        return new Transition(state with { Messages = messages }, WorkflowError.None, new WorkflowEvent(WorkflowEventKind.Answered, actor, null, state.Revision, state.Step, answer.Trim()));
    }

    private static WorkflowError CheckApprover(TimesheetState state, string actor, Func<string, string, bool>? isDeputyFor, out string? onBehalfOf)
    {
        onBehalfOf = null;
        if (state.Status != TimesheetStatus.Submitted) return WorkflowError.NotAllowedInThisStatus;
        if (Same(actor, state.OwnerCode) && !IsSelfApproved(state)) return WorkflowError.SelfApproval;

        var current = state.CurrentApprover;
        if (current == null) return WorkflowError.NotAllowedInThisStatus;
        if (Same(actor, current)) return WorkflowError.None;

        if (isDeputyFor != null && isDeputyFor(actor, current))
        {
            onBehalfOf = current;
            return WorkflowError.None;
        }

        return WorkflowError.NotTheCurrentApprover;
    }

    /// <summary>The chain is the owner alone: there is nobody above, so the owner approves their own timesheet</summary>
    public static bool IsSelfApproved(TimesheetState state) =>
        state.ChainOrEmpty.Count == 1 && Same(state.ChainOrEmpty[0], state.OwnerCode);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
