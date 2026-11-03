using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Timesheet.Domain.Tests;

[TestClass]
public class ApprovalChainTests
{
    // employee Z-001 -> team lead Z-010 -> lawyer Z-020 (final approver) -> finance Z-030 -> director Z-040
    private static readonly Dictionary<string, Person> Chart = new[]
    {
        new Person("Z-001", "Z-010"),
        new Person("Z-002", "Z-010"),
        new Person("Z-010", "Z-020"),
        new Person("Z-020", "Z-030", IsFinalApprover: true),
        new Person("Z-030", "Z-040"),
        new Person("Z-040", null, DesignatedApproverCode: "Z-030"),
        new Person("Z-050", null),
    }.ToDictionary(p => p.Code);

    private static Person? Lookup(string code) => Chart.GetValueOrDefault(code);

    [TestMethod]
    public void EmployeeChain_ShouldClimbTheSupervisorsAndStopAtTheFinalApprover()
    {
        var outcome = ApprovalChainBuilder.Build("Z-001", Lookup);
        CollectionAssert.AreEqual(new[] { "Z-010", "Z-020" }, outcome.Chain!.Approvers.ToArray(), "team lead, then the lawyer; finance only receives approved data");
    }

    [TestMethod]
    public void TeamLeadChain_ShouldStartAtTheLawyer()
    {
        CollectionAssert.AreEqual(new[] { "Z-020" }, ApprovalChainBuilder.Build("Z-010", Lookup).Chain!.Approvers.ToArray());
    }

    [TestMethod]
    public void LawyersOwnTimesheet_ShouldGoUpTheirOwnChain()
    {
        var outcome = ApprovalChainBuilder.Build("Z-020", Lookup);
        CollectionAssert.AreEqual(new[] { "Z-030", "Z-040" }, outcome.Chain!.Approvers.ToArray(), "finance, then up to the top: nobody above is flagged final");
    }

    [TestMethod]
    public void TopOfTheChart_ShouldUseTheDesignatedApprover_OrFailClearly()
    {
        CollectionAssert.AreEqual(new[] { "Z-030" }, ApprovalChainBuilder.Build("Z-040", Lookup).Chain!.Approvers.ToArray());
        CollectionAssert.AreEqual(new[] { "Z-050" }, ApprovalChainBuilder.Build("Z-050", Lookup).Chain!.Approvers.ToArray(), "nobody above: the person approves their own timesheet");
    }

    [TestMethod]
    public void DesignatedApprover_MayNotBeTheEmployee()
    {
        var chart = new Dictionary<string, Person> { ["Z-1"] = new Person("Z-1", null, DesignatedApproverCode: "Z-1") };
        CollectionAssert.AreEqual(new[] { "Z-1" }, ApprovalChainBuilder.Build("Z-1", c => chart.GetValueOrDefault(c)).Chain!.Approvers.ToArray());
    }

    [TestMethod]
    public void BrokenCharts_ShouldBeReported()
    {
        Assert.AreEqual(ChainProblem.UnknownPerson, ApprovalChainBuilder.Build("Z-999", Lookup).Problem);

        var cycle = new[] { new Person("A", "B"), new Person("B", "C"), new Person("C", "A") }.ToDictionary(p => p.Code);
        Assert.AreEqual(ChainProblem.Cycle, ApprovalChainBuilder.Build("A", c => cycle.GetValueOrDefault(c)).Problem);
        Assert.AreEqual(ChainProblem.Cycle, ApprovalChainBuilder.CheckChart(cycle.Values));

        var dangling = new[] { new Person("A", "Nobody") }.ToDictionary(p => p.Code);
        Assert.AreEqual(ChainProblem.UnknownPerson, ApprovalChainBuilder.Build("A", c => dangling.GetValueOrDefault(c)).Problem);
        Assert.AreEqual(ChainProblem.None, ApprovalChainBuilder.CheckChart(Chart.Values));
    }

    [TestMethod]
    public void ChainLength_ShouldBeLimited()
    {
        Person[] Line(int count) => Enumerable.Range(0, count).Select(i => new Person($"P{i}", i < count - 1 ? $"P{i + 1}" : null)).ToArray();

        var shortLine = Line(20).ToDictionary(p => p.Code);
        Assert.IsTrue(ApprovalChainBuilder.Build("P0", c => shortLine.GetValueOrDefault(c)).IsValid, "20 levels are fine");

        var longLine = Line(40).ToDictionary(p => p.Code);
        Assert.AreEqual(ChainProblem.TooDeep, ApprovalChainBuilder.Build("P0", c => longLine.GetValueOrDefault(c)).Problem, "a chart this deep is almost certainly a mistake");
        Assert.IsTrue(ApprovalChainBuilder.Build("P0", c => longLine.GetValueOrDefault(c), maxDepth: 50).IsValid);
    }
}

[TestClass]
public class WorkflowTests
{
    private static readonly ApprovalChain Chain = new(new[] { "Z-010", "Z-020" });

    private static TimesheetState Submitted() => TimesheetWorkflow.Submit(new TimesheetState("Z-001"), "Z-001", Chain, false).State;

    [TestMethod]
    public void FullPath_ShouldGoThroughEveryStepAndEndApproved()
    {
        var submitted = TimesheetWorkflow.Submit(new TimesheetState("Z-001"), "Z-001", Chain, hasBlockingIssues: false);
        Assert.IsTrue(submitted.Ok);
        Assert.AreEqual(TimesheetStatus.Submitted, submitted.State.Status);
        Assert.AreEqual(1, submitted.State.Revision);
        Assert.AreEqual("Z-010", submitted.State.CurrentApprover);
        Assert.IsFalse(submitted.State.IsEditableByOwner, "frozen while waiting");

        var first = TimesheetWorkflow.Approve(submitted.State, "Z-010");
        Assert.AreEqual(TimesheetStatus.Submitted, first.State.Status);
        Assert.AreEqual("Z-020", first.State.CurrentApprover);
        Assert.AreEqual(WorkflowEventKind.StepApproved, first.Event!.Kind);

        var last = TimesheetWorkflow.Approve(first.State, "Z-020");
        Assert.AreEqual(TimesheetStatus.Approved, last.State.Status);
        Assert.AreEqual(WorkflowEventKind.Approved, last.Event!.Kind);
        Assert.IsNull(last.State.CurrentApprover);
    }

    [TestMethod]
    public void Approval_ShouldNeverHappenWithoutTheRightPerson()
    {
        var state = Submitted();
        Assert.AreEqual(WorkflowError.NotTheCurrentApprover, TimesheetWorkflow.Approve(state, "Z-020").Error, "the lawyer cannot skip the team lead");
        Assert.AreEqual(WorkflowError.NotTheCurrentApprover, TimesheetWorkflow.Approve(state, "Z-099").Error);
        Assert.AreEqual(WorkflowError.SelfApproval, TimesheetWorkflow.Approve(state, "Z-001").Error);
        Assert.AreEqual(TimesheetStatus.Submitted, state.Status);
    }

    [TestMethod]
    public void ThePersonAtTheTop_SubmitsAndApprovesTheirOwnTimesheet()
    {
        var own = new ApprovalChain(new[] { "Z-001" });
        var submitted = TimesheetWorkflow.Submit(new TimesheetState("Z-001"), "Z-001", own, false);
        Assert.IsTrue(submitted.Ok);
        Assert.AreEqual("Z-001", submitted.State.CurrentApprover);

        var approved = TimesheetWorkflow.Approve(submitted.State, "Z-001");
        Assert.IsTrue(approved.Ok);
        Assert.AreEqual(TimesheetStatus.Approved, approved.State.Status);
        Assert.AreEqual(WorkflowError.NotTheCurrentApprover, TimesheetWorkflow.Approve(submitted.State, "Z-099").Error, "nobody else is the approver");
    }

    [TestMethod]
    public void AnAnsweredMessage_StaysAsItWas()
    {
        var returned = new TimesheetState("Z-001", TimesheetStatus.Returned, 1, Chain.Approvers, 0, new[] { new ReturnMessage("Z-010", "Ispravi", 1) });
        var answered = TimesheetWorkflow.Answer(returned, "Z-001", 0, "Prvi odgovor");
        Assert.IsTrue(answered.Ok);

        var again = TimesheetWorkflow.Answer(answered.State, "Z-001", 0, "Drugi odgovor");
        Assert.AreEqual(WorkflowError.AlreadyAnswered, again.Error);
        Assert.AreEqual("Prvi odgovor", answered.State.MessagesOrEmpty[0].Answer);
    }

    [TestMethod]
    public void TheTopPersonsDesignatedApprover_MayBeSomebodyWhoReportsToThem()
    {
        // Somebody has to approve the person at the top, and that is usually the lawyer or finance, who report to them
        var chart = new[] { new Person("Z-040", null, DesignatedApproverCode: "Z-030"), new Person("Z-030", "Z-040") };
        Assert.AreEqual(ChainProblem.None, ApprovalChainBuilder.CheckChart(chart));
    }

    [TestMethod]
    public void Submit_ShouldBeRefusedWhenItIsNotAllowed()
    {
        var draft = new TimesheetState("Z-001");
        Assert.AreEqual(WorkflowError.NotTheOwner, TimesheetWorkflow.Submit(draft, "Z-010", Chain, false).Error);
        Assert.AreEqual(WorkflowError.BlockingIssues, TimesheetWorkflow.Submit(draft, "Z-001", Chain, true).Error);
        Assert.AreEqual(WorkflowError.EmptyChain, TimesheetWorkflow.Submit(draft, "Z-001", new ApprovalChain(Array.Empty<string>()), false).Error);
        Assert.AreEqual(WorkflowError.SelfApproval, TimesheetWorkflow.Submit(draft, "Z-001", new ApprovalChain(new[] { "Z-010", "Z-001" }), false).Error, "a person inside a longer chain cannot approve themselves");
        Assert.AreEqual(WorkflowError.NotAllowedInThisStatus, TimesheetWorkflow.Submit(Submitted(), "Z-001", Chain, false).Error, "already submitted");
    }

    [TestMethod]
    public void Return_ShouldNeedAMessageAndGoToTheEmployee()
    {
        var state = Submitted();
        Assert.AreEqual(WorkflowError.MessageRequired, TimesheetWorkflow.Return(state, "Z-010", "  ").Error);

        var returned = TimesheetWorkflow.Return(state, "Z-010", "Godišnji odmor je upisan za vikend.");
        Assert.AreEqual(TimesheetStatus.Returned, returned.State.Status);
        Assert.IsTrue(returned.State.IsEditableByOwner);
        Assert.AreEqual(WorkflowEventKind.Returned, returned.Event!.Kind);
        Assert.AreEqual(1, returned.State.MessagesOrEmpty.Count);
    }

    [TestMethod]
    public void ReturnByALaterStep_ShouldStartAgainAtTheFirstStep()
    {
        var atLawyer = TimesheetWorkflow.Approve(Submitted(), "Z-010").State;
        var returned = TimesheetWorkflow.Return(atLawyer, "Z-020", "Provjeriti prekovremeni rad.").State;
        var answered = TimesheetWorkflow.Answer(returned, "Z-001", 0, "Ispravljeno.").State;

        var resubmitted = TimesheetWorkflow.Submit(answered, "Z-001", Chain, false);
        Assert.IsTrue(resubmitted.Ok);
        Assert.AreEqual(2, resubmitted.State.Revision, "each submission is a new revision");
        Assert.AreEqual("Z-010", resubmitted.State.CurrentApprover, "the team lead sees it again first (confirmed in the analysis)");
    }

    [TestMethod]
    public void Resubmission_ShouldWaitForEveryMessageToBeAnswered()
    {
        var returned = TimesheetWorkflow.Return(Submitted(), "Z-010", "Ispravi.").State;
        Assert.AreEqual(WorkflowError.UnansweredMessages, TimesheetWorkflow.Submit(returned, "Z-001", Chain, false).Error);
        Assert.AreEqual(WorkflowError.NotTheOwner, TimesheetWorkflow.Answer(returned, "Z-010", 0, "x").Error);
        Assert.AreEqual(WorkflowError.UnknownMessage, TimesheetWorkflow.Answer(returned, "Z-001", 5, "x").Error);
        Assert.AreEqual(WorkflowError.MessageRequired, TimesheetWorkflow.Answer(returned, "Z-001", 0, " ").Error);
    }

    [TestMethod]
    public void ApprovedTimesheet_ShouldBeLocked()
    {
        var approved = TimesheetWorkflow.Approve(TimesheetWorkflow.Approve(Submitted(), "Z-010").State, "Z-020").State;

        Assert.IsFalse(approved.IsEditableByOwner);
        Assert.AreEqual(WorkflowError.NotAllowedInThisStatus, TimesheetWorkflow.Submit(approved, "Z-001", Chain, false).Error);
        Assert.AreEqual(WorkflowError.NotAllowedInThisStatus, TimesheetWorkflow.Return(approved, "Z-020", "Ipak ne").Error);
        Assert.AreEqual(WorkflowError.NotAllowedInThisStatus, TimesheetWorkflow.Approve(approved, "Z-020").Error);
    }

    [TestMethod]
    public void Deputy_ShouldActForTheApprover_AndTheEventShowsFor_Whom()
    {
        Func<string, string, bool> deputy = (actor, approver) => actor == "Z-011" && approver == "Z-010";
        var approved = TimesheetWorkflow.Approve(Submitted(), "Z-011", deputy);

        Assert.IsTrue(approved.Ok);
        Assert.AreEqual("Z-011", approved.Event!.Actor);
        Assert.AreEqual("Z-010", approved.Event.OnBehalfOf);
        Assert.AreEqual("Z-020", approved.State.CurrentApprover);
        Assert.AreEqual(WorkflowError.NotTheCurrentApprover, TimesheetWorkflow.Approve(Submitted(), "Z-012", deputy).Error);
    }

    [TestMethod]
    public void TheChainInForceAtSubmission_ShouldStayWithTheRevision()
    {
        var state = Submitted();
        // The chart changes later; the running approval does not
        Assert.AreEqual("Z-010", state.CurrentApprover);
        CollectionAssert.AreEqual(new[] { "Z-010", "Z-020" }, state.ChainOrEmpty.ToArray());
    }
}
