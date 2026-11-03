using Company.Api.Contracts;
using Timesheet.Domain;

namespace Company.Api.Services;

/// <summary>
/// The domain's enums and the contract's enums have the same names on purpose (the contract does not depend on the domain);
/// this is the one place that maps between them. A test makes sure that no member is left without its counterpart.
/// </summary>
public static class DtoMapping
{
    public static SheetStatus ToDto(this TimesheetStatus status) => Enum.Parse<SheetStatus>(status.ToString());

    public static DayKind ToDto(this DayType type) => Enum.Parse<DayKind>(type.ToString());

    public static IssueSeverity ToDto(this Severity severity) => Enum.Parse<IssueSeverity>(severity.ToString());

    public static HistoryKind ToDto(this WorkflowEventKind kind) => Enum.Parse<HistoryKind>(kind.ToString());

    public static ReminderType ToDto(this ReminderKind kind) => Enum.Parse<ReminderType>(kind.ToString());

    public static SkippedBecause ToDto(this SkipReason reason) => Enum.Parse<SkippedBecause>(reason.ToString());
}
