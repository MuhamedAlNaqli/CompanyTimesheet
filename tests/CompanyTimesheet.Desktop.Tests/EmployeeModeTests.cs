using System.IO;
using Company.Api.Contracts;
using CompanyTimesheet.Desktop.Background;
using CompanyTimesheet.Desktop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace CompanyTimesheet.Desktop.Tests;

/// <summary>The logic of employee mode that needs no window: formats, parsing and the dialogs' view models</summary>
[TestClass]
public class EmployeeModeTests
{
    [DataTestMethod]
    [DataRow("8", 480)]
    [DataRow("7,5", 450)]
    [DataRow("7.5", 450)]
    [DataRow("7:30", 450)]
    [DataRow("7h30", 450)]
    [DataRow("7h", 420)]
    [DataRow(" 24 ", 1440)]
    [DataRow("0,5", 30)]
    public void Hours_ShouldBeReadTheWayAnEmployeeWritesThem(string text, int expected)
    {
        Assert.IsTrue(EmployeeFormat.TryParseHours(text, out var minutes), text);
        Assert.AreEqual(expected, minutes);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("abc")]
    [DataRow("0")]
    [DataRow("25")]
    [DataRow("7:75")]
    [DataRow("-3")]
    public void InvalidHours_ShouldBeRejected(string text) => Assert.IsFalse(EmployeeFormat.TryParseHours(text, out _), text);

    [DataTestMethod]
    [DataRow("17:00", 17, 0)]
    [DataRow("8:30", 8, 30)]
    [DataRow("17", 17, 0)]
    [DataRow("20.15", 20, 15)]
    public void Times_ShouldBeRead(string text, int hour, int minute)
    {
        Assert.IsTrue(EmployeeFormat.TryParseTime(text, out var time), text);
        Assert.AreEqual(new TimeOnly(hour, minute), time);
    }

    [TestMethod]
    public void Formats_ShouldBeBosnian()
    {
        Assert.AreEqual("Septembar 2026.", EmployeeFormat.Month(2026, 9));
        Assert.AreEqual("2,5 h", EmployeeFormat.Hours(150));
        Assert.AreEqual("8 h", EmployeeFormat.Hours(480));
        Assert.AreEqual("utorak", EmployeeFormat.Weekday(new DateOnly(2026, 9, 1)));
        Assert.AreEqual("Vraćeno na ispravku", EmployeeFormat.Status(SheetStatus.Returned));
        Assert.AreEqual("1022 Godišnji odmor A", EmployeeFormat.Entry(new EntryDto(1022, 0)));
        Assert.AreEqual("1010 Redovan rad: 8 h", EmployeeFormat.Entry(new EntryDto(1010, 480)));
    }

    // ---- The entry dialog: one form that changes with the code ----------------------------------------------------------------

    private static EntryViewModel NewEntry(Func<AbsenceRangeRequest, Task<AbsencePreviewDto>>? preview = null, DateOnly? date = null, EntryDto? entry = null, OvertimeDto? overtime = null)
    {
        Func<DateOnly, DayType> days = d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? DayType.Weekend : DayType.Working;
        return new EntryViewModel(2026, 9, days, preview ?? (_ => throw new InvalidOperationException("must not be called")), date ?? new DateOnly(2026, 9, 7), entry, overtime);
    }

    [TestMethod]
    public void TheFormShouldChangeWithTheCode()
    {
        var vm = NewEntry();
        Assert.IsTrue(vm.Codes.Any(c => c.Code.IsOvertime), "overtime is a code like any other");
        Assert.AreEqual(1010, vm.SelectedCode!.Code.Code);
        Assert.IsTrue(vm.IsHours && !vm.IsAbsence && !vm.IsOvertime);

        vm.SelectedCode = vm.Codes.Single(c => c.Code.Code == Codes.AnnualLeaveA);
        Assert.IsTrue(vm.IsAbsence && !vm.IsHours && !vm.IsOvertime);
        Assert.AreEqual("Od", vm.DateLabel);

        vm.SelectedCode = vm.Codes.Single(c => c.Code.Code == Codes.WeekendOvertime);
        Assert.IsTrue(vm.IsOvertime && !vm.IsHours && !vm.IsAbsence);
        Assert.AreEqual("Datum", vm.DateLabel);
    }

    [TestMethod]
    public void AnHoursEntry_ShouldRejectBadHoursAndKeepTheDialogOpen()
    {
        var vm = NewEntry();
        var closed = false;
        vm.RequestClose += () => closed = true;

        vm.HoursText = "abc";
        vm.SaveCommand.Execute(null);
        Assert.IsFalse(closed);
        Assert.IsNull(vm.Result);
        StringAssert.Contains(vm.Error, "sate");

        vm.HoursText = "7,5";
        vm.SaveCommand.Execute(null);
        Assert.IsTrue(closed);
        Assert.AreEqual(new SetEntryRequest(1010, 450), vm.Result!.Day);
        Assert.AreEqual(new DateOnly(2026, 9, 7), vm.Result.DayDate);
    }

    [TestMethod]
    public void EditingAnEntry_ShouldStartFromItAndKeepItsDate()
    {
        var vm = NewEntry(entry: new EntryDto(1010, 360));
        Assert.AreEqual(1010, vm.SelectedCode!.Code.Code);
        Assert.AreEqual("6", vm.HoursText);
        Assert.IsFalse(vm.CanChangeDate);
        Assert.IsTrue(NewEntry().CanChangeDate);
    }

    [TestMethod]
    public void AnAbsenceCode_ShouldShowTheDaysBeforeItCanBeSaved()
    {
        var calls = 0;
        var vm = NewEntry(request =>
        {
            calls++;
            return Task.FromResult(new AbsencePreviewDto(request.Code, new[] { new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 8) },
                new[] { new SkippedDayDto(new DateOnly(2026, 9, 12), SkippedBecause.Weekend) }));
        });
        vm.SelectedCode = vm.Codes.Single(c => c.Code.Code == Codes.AnnualLeaveA);
        vm.To = new DateTimeOffset(new DateTime(2026, 9, 13));
        Assert.IsFalse(vm.SaveCommand.CanExecute(null), "nothing can be saved before the days were shown");

        vm.ShowPreviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.AreEqual(1, calls);
        StringAssert.Contains(vm.PreviewText, "2 dana");
        StringAssert.Contains(vm.PreviewText, "vikend");
        Assert.IsTrue(vm.SaveCommand.CanExecute(null));

        vm.To = new DateTimeOffset(new DateTime(2026, 9, 18));
        Assert.IsFalse(vm.SaveCommand.CanExecute(null), "changing the range asks for a new preview");

        vm.ShowPreviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        vm.SaveCommand.Execute(null);
        Assert.AreEqual(1022, vm.Result!.Absence!.Code);
        Assert.AreEqual(new DateOnly(2026, 9, 18), vm.Result.Absence.To);
        Assert.AreEqual(new DateOnly(2026, 9, 7), vm.Result.Absence.From);
    }

    [TestMethod]
    public void AnAbsenceThatEndsBeforeItStarts_ShouldBeRefused()
    {
        var vm = NewEntry();
        vm.SelectedCode = vm.Codes.Single(c => c.Code.Code == Codes.AnnualLeaveA);
        vm.Date = new DateTimeOffset(new DateTime(2026, 9, 10));
        vm.To = new DateTimeOffset(new DateTime(2026, 9, 5));
        vm.ShowPreviewCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        StringAssert.Contains(vm.Error, "Kraj");
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

    [TestMethod]
    public void AnOvertimeCode_ShouldPreviewTheTotalAndTheSplitWhileTyping()
    {
        var vm = NewEntry(date: new DateOnly(2026, 9, 7));
        vm.SelectedCode = vm.Codes.Single(c => c.Code.Code == Codes.Overtime);
        vm.StartText = "20:00";
        vm.EndText = "22:30";

        StringAssert.Contains(vm.SummaryText, "2,5 h");
        StringAssert.Contains(vm.SummaryText, "2020");
        StringAssert.Contains(vm.SummaryText, "2021");
        Assert.AreEqual("", vm.WarningText);

        vm.StartText = "10:00";
        vm.EndText = "12:00";
        Assert.IsTrue(vm.WarningText.Contains("redovno radno vrijeme"), "inside 08:30 to 17:00 on a working day");
    }

    [TestMethod]
    public void AnOvertimeEntry_ShouldNeedADescriptionAndDifferentTimes()
    {
        var vm = NewEntry(date: new DateOnly(2026, 9, 7));
        vm.SelectedCode = vm.Codes.Single(c => c.Code.Code == Codes.Overtime);
        vm.StartText = "18:00";
        vm.EndText = "20:00";

        vm.SaveCommand.Execute(null);
        Assert.IsNull(vm.Result);
        StringAssert.Contains(vm.Error, "opis");

        vm.Description = "Ispravka greške";
        vm.StartText = "20:00";
        vm.SaveCommand.Execute(null);
        StringAssert.Contains(vm.Error, "isti");
        Assert.IsNull(vm.Result);

        vm.StartText = "18:00";
        vm.SaveCommand.Execute(null);
        Assert.AreEqual(new OvertimeRequest(new DateOnly(2026, 9, 7), new TimeOnly(18, 0), new TimeOnly(20, 0), "Ispravka greške"), vm.Result!.Overtime);
        Assert.IsNull(vm.Result.OvertimeId);
    }

    [TestMethod]
    public void EditingOvertime_ShouldStayOvertimeAndKeepItsId()
    {
        var record = new OvertimeDto(7, new DateOnly(2026, 9, 5), new TimeOnly(8, 30), new TimeOnly(11, 0), "Provisioning", 150,
            new[] { new AllocationDto(new DateOnly(2026, 9, 5), 2023, 150) }, false);
        var vm = NewEntry(overtime: record);

        Assert.IsTrue(vm.IsOvertime);
        Assert.IsTrue(vm.Codes.All(c => c.Code.IsOvertime), "an overtime record cannot become an hours entry");
        Assert.AreEqual(2023, vm.SelectedCode!.Code.Code);
        Assert.AreEqual("08:30", vm.StartText);

        vm.SaveCommand.Execute(null);
        Assert.AreEqual(7, vm.Result!.OvertimeId);
    }

    [TestMethod]
    public void ADateOutsideTheMonth_ShouldBeRefused()
    {
        var vm = NewEntry();
        vm.Date = new DateTimeOffset(new DateTime(2026, 10, 2));
        vm.SaveCommand.Execute(null);
        Assert.IsNull(vm.Result);
        StringAssert.Contains(vm.Error, "mjesecu");
    }

    [TestMethod]
    public void DayRows_ShouldShowEntriesOvertimeAndFindings()
    {
        var day = new DayDto(new DateOnly(2026, 9, 5), "S", DayKind.Weekend, null,
            new[] { new EntryDto(1022, 0) }, new[] { new AllocationDto(new DateOnly(2026, 9, 5), 2023, 150) });
        var row = new EmployeeDayRow(day, new[] { new IssueDto("V-02", IssueSeverity.Blocking, new DateOnly(2026, 9, 5), "Godišnji odmor na vikend") });

        Assert.AreEqual("05.09.", row.DateText);
        Assert.AreEqual("vikend", row.TypeText);
        StringAssert.StartsWith(row.EntriesText, "1022 Godišnji odmor A");
        StringAssert.Contains(row.OvertimeText, "2023");
        StringAssert.Contains(row.OvertimeText, "2,5 h");
        Assert.AreEqual(1, row.IssueCount);
        Assert.IsTrue(row.HasBlocking);
        StringAssert.Contains(row.IssueTooltip, "V-02");
        Assert.IsFalse(row.IsWorkingDay);
    }

    // ---- Submission and approval ----------------------------------------------------------------------------------------

    [TestMethod]
    public void PendingRow_ShouldNameThePersonTheMonthAndWhatIsWrong()
    {
        var row = new PendingRow(new PendingApprovalDto("Z-001", "Amra Adžamija", 2026, 9, 1, 0, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc), 0, 2));
        Assert.AreEqual("Amra Adžamija", row.Title);
        StringAssert.Contains(row.Detail, "Septembar 2026.");
        Assert.AreEqual("2 upozorenja", row.Warning);
        Assert.IsTrue(row.HasWarning);
        Assert.IsFalse(new PendingRow(new PendingApprovalDto("Z-001", "A", 2026, 9, 1, 0, DateTime.UtcNow, 0, 0)).HasWarning);
    }

    [TestMethod]
    public void MessageRow_ShouldOfferAnAnswerOnlyWhileItIsOpenAndTheSheetIsReturned()
    {
        var open = new MessageDto(0, "Z-002", "Benjamin Bašić", "Ispravi sate", 1, null);
        Assert.IsTrue(new MessageRow(open, canAnswer: true).CanAnswer);
        Assert.IsFalse(new MessageRow(open, canAnswer: false).CanAnswer);

        var answered = new MessageRow(open with { Answer = "Gotovo" }, canAnswer: true);
        Assert.IsFalse(answered.CanAnswer);
        Assert.AreEqual("Odgovor: Gotovo", answered.AnswerText);
        StringAssert.Contains(answered.Header, "Benjamin");
    }

    [TestMethod]
    public void HistoryRow_ShouldShowWhoActedAndForWhom()
    {
        var row = new HistoryRow(new HistoryDto(DateTime.UtcNow, HistoryKind.Returned, "Z-003", "Emir Ferhatbegović", "Z-002", 1, 1, "Prekovremeni"));
        StringAssert.Contains(row.Title, "Vraćeno na ispravku");
        StringAssert.Contains(row.Detail, "Emir Ferhatbegović");
        StringAssert.Contains(row.Detail, "u ime Z-002");
        StringAssert.Contains(row.Detail, "Prekovremeni");
    }

    [TestMethod]
    public void TextPrompt_ShouldNeedATextAndTrimIt()
    {
        var vm = new TextPromptViewModel("Vrati", "Šta treba ispraviti?", "Vrati");
        vm.SaveCommand.Execute(null);
        Assert.IsNull(vm.Result);
        Assert.AreNotEqual("", vm.Error);

        vm.Text = "  Ispravi sate  ";
        vm.SaveCommand.Execute(null);
        Assert.AreEqual("Ispravi sate", vm.Result);
    }

    // ---- Reminders -------------------------------------------------------------------------------------------------------

    private static ReminderDto Due(ReminderType kind, int month = 9, int count = 1, int deadlineDay = 25) =>
        new(kind, 2026, month, count, new DateOnly(2026, month, deadlineDay));

    [TestMethod]
    public void Reminders_ShouldBeWordedForTheEmployee()
    {
        var (title, message) = EmployeeFormat.Reminder(Due(ReminderType.SubmitSoon));
        Assert.AreEqual("Predajte šihtaricu", title);
        StringAssert.Contains(message, "Septembar 2026.");
        StringAssert.Contains(message, "25.09.2026.");

        StringAssert.Contains(EmployeeFormat.Reminder(Due(ReminderType.ApproveWaiting, count: 3)).Message, "3");
        Assert.IsTrue(EmployeeFormat.ReminderOrder(ReminderType.FixReturned) < EmployeeFormat.ReminderOrder(ReminderType.SubmitSoon), "what blocks others comes first");
    }

    [TestMethod]
    public void TheNotification_ShouldNameTheMostUrgentAndMentionTheRest()
    {
        var items = new[] { Due(ReminderType.ApproveWaiting, count: 2), Due(ReminderType.SubmitOverdue) };
        var (title, message) = EmployeeReminders.Compose(items);
        Assert.AreEqual("Šihtarica kasni", title);
        StringAssert.Contains(message, "predana");
        StringAssert.Contains(message, "čekaju vaše odobrenje");
    }

    [TestMethod]
    public void ANewItemIsAnnouncedAtOnce_ButTheSameOneOnlyNowAndThen()
    {
        var items = new[] { Due(ReminderType.SubmitSoon) };
        var now = new DateTime(2026, 9, 23, 10, 0, 0);
        var state = new ReminderState();
        Assert.IsTrue(EmployeeReminders.ShouldNotify(state, items, now));

        state.Record(now);
        state.EmployeeSignature = EmployeeReminders.Signature(items);
        Assert.IsFalse(EmployeeReminders.ShouldNotify(state, items, now.AddMinutes(30)), "told a moment ago");
        Assert.IsTrue(EmployeeReminders.ShouldNotify(state, items, now.AddHours(3)), "a repeat after the gap");

        var more = new[] { Due(ReminderType.SubmitSoon), Due(ReminderType.ApproveWaiting) };
        Assert.IsTrue(EmployeeReminders.ShouldNotify(state, more, now.AddMinutes(30)), "something new arrived");
        Assert.IsFalse(EmployeeReminders.ShouldNotify(state, Array.Empty<ReminderDto>(), now), "nothing is due");
    }

    [TestMethod]
    public void WithoutTheServer_ASubmissionDeadlineIsStillHonoured_AndNothingElseIsGuessed()
    {
        var last = new RemindersDto(new DateOnly(2026, 9, 10), new[] { Due(ReminderType.SubmitSoon), Due(ReminderType.ApproveWaiting, count: 2), Due(ReminderType.FixReturned) });

        Assert.AreEqual(0, EmployeeReminders.Offline(last, new DateOnly(2026, 9, 15)).Count, "not due yet");
        Assert.AreEqual(ReminderType.SubmitSoon, EmployeeReminders.Offline(last, new DateOnly(2026, 9, 23)).Single().Kind);
        Assert.AreEqual(ReminderType.SubmitOverdue, EmployeeReminders.Offline(last, new DateOnly(2026, 9, 27)).Single().Kind);
        Assert.AreEqual(0, EmployeeReminders.Offline(null, new DateOnly(2026, 9, 27)).Count);
    }

    // ---- Findings of the code review ----------------------------------------------------------------------------------------

    [TestMethod]
    public void TheReminderIsQuietAtNightAndOnDaysOff()
    {
        Assert.IsTrue(EmployeeReminders.IsQuiet(new DateTime(2026, 11, 2, 6, 59, 0)), "Monday, before seven");
        Assert.IsFalse(EmployeeReminders.IsQuiet(new DateTime(2026, 11, 2, 7, 0, 0)));
        Assert.IsFalse(EmployeeReminders.IsQuiet(new DateTime(2026, 11, 2, 19, 59, 0)));
        Assert.IsTrue(EmployeeReminders.IsQuiet(new DateTime(2026, 11, 2, 20, 0, 0)));
        Assert.IsTrue(EmployeeReminders.IsQuiet(new DateTime(2026, 11, 7, 10, 0, 0)), "Saturday");
        Assert.IsTrue(EmployeeReminders.IsQuiet(new DateTime(2026, 11, 8, 10, 0, 0)), "Sunday");
    }

    [TestMethod]
    public void AFileIsReplacedInOneStep_AndNoTemporaryFileIsLeft()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dtm-atomic-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "state.json");
            ReminderState.WriteAtomically(path, "first");
            ReminderState.WriteAtomically(path, "second");
            Assert.AreEqual("second", File.ReadAllText(path));
            CollectionAssert.AreEqual(new[] { path }, Directory.GetFiles(folder));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
