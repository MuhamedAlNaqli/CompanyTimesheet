namespace Timesheet.Domain;

/// <summary>
/// A person in the organisation chart. Everyone has at most one supervisor; a timesheet climbs that chain.
/// The lawyer is flagged as final approver, so a chain stops there and finance only receives approved data.
/// </summary>
public sealed record Person(
    string Code,
    string? SupervisorCode,
    bool IsFinalApprover = false,
    string? DesignatedApproverCode = null);

public enum ChainProblem
{
    None,

    /// <summary>The employee, or someone on the way up, is not in the chart</summary>
    UnknownPerson,

    /// <summary>The chart loops back on itself</summary>
    Cycle,

    /// <summary>Not produced any more: a person with nobody above approves their own timesheet</summary>
    NoApprover,

    TooDeep
}

public sealed record ApprovalChain(IReadOnlyList<string> Approvers);

public sealed record ChainOutcome(ApprovalChain? Chain, ChainProblem Problem)
{
    public bool IsValid => Chain != null;
}

public static class ApprovalChainBuilder
{
    /// <summary>
    /// The people who approve a timesheet, in order: the employee's supervisor, that person's supervisor, and so on, up to and
    /// including the first one flagged as final approver, or to the top of the chart. The person at the top of the chart
    /// has no supervisor, so their designated approver is used instead; without one they approve their own timesheet.
    /// </summary>
    public static ChainOutcome Build(string employeeCode, Func<string, Person?> lookup, int maxDepth = 25)
    {
        var employee = lookup(employeeCode);
        if (employee == null) return new ChainOutcome(null, ChainProblem.UnknownPerson);

        var approvers = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { employee.Code };
        var currentCode = employee.SupervisorCode;

        while (!string.IsNullOrWhiteSpace(currentCode))
        {
            if (!seen.Add(currentCode)) return new ChainOutcome(null, ChainProblem.Cycle);
            if (approvers.Count >= maxDepth) return new ChainOutcome(null, ChainProblem.TooDeep);

            var person = lookup(currentCode);
            if (person == null) return new ChainOutcome(null, ChainProblem.UnknownPerson);

            approvers.Add(person.Code);
            if (person.IsFinalApprover) break;
            currentCode = person.SupervisorCode;
        }

        if (approvers.Count == 0)
        {
            var designated = employee.DesignatedApproverCode;
            if (string.IsNullOrWhiteSpace(designated) || designated.Equals(employee.Code, StringComparison.OrdinalIgnoreCase))
            {
                // Nobody above: the person submits and approves their own timesheet (the chain is the person alone)
                approvers.Add(employee.Code);
            }
            else
            {
                if (lookup(designated) == null) return new ChainOutcome(null, ChainProblem.UnknownPerson);
                approvers.Add(designated);
            }
        }

        return new ChainOutcome(new ApprovalChain(approvers), ChainProblem.None);
    }

    /// <summary>
    /// Whether the chart can be saved: every supervisor exists and nobody (directly or indirectly) is their own
    /// supervisor. The designated approver of the person at the top is deliberately not part of this check: somebody has to
    /// approve the top person, and that is usually somebody who reports to them (the lawyer or finance). Used when the
    /// administrator changes a "reports to".
    /// </summary>
    public static ChainProblem CheckChart(IEnumerable<Person> people)
    {
        var map = people.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var person in map.Values)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { person.Code };
            var next = person.SupervisorCode;
            while (!string.IsNullOrWhiteSpace(next))
            {
                if (!map.TryGetValue(next, out var supervisor)) return ChainProblem.UnknownPerson;
                if (!seen.Add(supervisor.Code)) return ChainProblem.Cycle;
                next = supervisor.SupervisorCode;
            }
        }

        return ChainProblem.None;
    }
}
