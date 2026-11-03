namespace Timesheet.Domain;

public enum ReminderKind
{
    /// <summary>The employee has not submitted yet and the deadline is near</summary>
    SubmitSoon,

    SubmitOverdue,

    /// <summary>The timesheet came back with a message and waits for the employee</summary>
    FixReturned,

    /// <summary>Timesheets wait for the approver</summary>
    ApproveWaiting,

    ApproveOverdue
}

public static class Deadlines
{
    /// <summary>
    /// Employees submit by the 25th. When that day is a weekend or a state holiday the deadline moves to the first working
    /// day after it (Q-21; the instruction does not say whether earlier or later, so later is assumed).
    /// </summary>
    public static DateOnly Submission(int year, int month, WorkCalendar calendar) =>
        calendar.NextWorkingDay(new DateOnly(year, month, 25));

    /// <summary>Everything must be approved before the next month starts</summary>
    public static DateOnly Approval(int year, int month) => new(year, month, DateTime.DaysInMonth(year, month));
}

/// <summary>When someone should be reminded. The API decides; the desktop app only asks what is due and shows it.</summary>
public static class ReminderRules
{
    public static ReminderKind? ForEmployee(DateOnly today, TimesheetStatus status, DateOnly submissionDeadline, int daysBefore = 3)
    {
        switch (status)
        {
            case TimesheetStatus.Returned:
                return ReminderKind.FixReturned;
            case TimesheetStatus.Draft:
                if (today > submissionDeadline) return ReminderKind.SubmitOverdue;
                return today >= submissionDeadline.AddDays(-daysBefore) ? ReminderKind.SubmitSoon : null;
            default:
                return null;
        }
    }

    public static ReminderKind? ForApprover(DateOnly today, DateOnly approvalDeadline, int waitingCount)
    {
        if (waitingCount <= 0) return null;
        return today > approvalDeadline ? ReminderKind.ApproveOverdue : ReminderKind.ApproveWaiting;
    }
}
