using System.Globalization;
using System.Text;
using Company.Api.Contracts;

namespace Company.Api.Texts;

/// <summary>The words of a reminder mail (the same as the banner and the notification of the desktop app say)</summary>
public static class ReminderTexts
{
    private static bool English(string? language) => language == "en";

    public static string Subject(IReadOnlyList<ReminderDto> items, string? language = null) =>
        items.Count == 1 ? Title(items[0], language) : English(language) ? $"Working time record: {items.Count} notices" : $"Evidencija radnog vremena: {items.Count} obavijesti";

    public static string Body(string name, IReadOnlyList<ReminderDto> items, string? language = null)
    {
        if (English(language))
        {
            var en = new StringBuilder();
            en.AppendLine($"Dear {name},");
            en.AppendLine();
            foreach (var item in items.OrderBy(Order)) en.AppendLine("• " + Line(item, language));
            en.AppendLine();
            en.AppendLine("You fill in your timesheet in the Working time record application.");
            return en.ToString();
        }

        var text = new StringBuilder();
        text.AppendLine($"Poštovani/a {name},");
        text.AppendLine();
        foreach (var item in items.OrderBy(Order)) text.AppendLine("• " + Line(item));
        text.AppendLine();
        text.AppendLine("Šihtaricu popunjavate u aplikaciji Evidencija radnog vremena.");
        return text.ToString();
    }

    public static string Title(ReminderDto item, string? language = null) => English(language) ? item.Kind switch
    {
        ReminderType.SubmitSoon => "Submit your timesheet",
        ReminderType.SubmitOverdue => "Your timesheet is late",
        ReminderType.FixReturned => "Your timesheet was returned for correction",
        ReminderType.ApproveWaiting => "Timesheets are waiting for approval",
        ReminderType.ApproveOverdue => "Approval is late",
        ReminderType.Nudged => $"Reminder: {item.From}",
        _ => item.Kind.ToString()
    } : item.Kind switch
    {
        ReminderType.SubmitSoon => "Predajte šihtaricu",
        ReminderType.SubmitOverdue => "Šihtarica kasni",
        ReminderType.FixReturned => "Šihtarica je vraćena na ispravku",
        ReminderType.ApproveWaiting => "Šihtarice čekaju odobrenje",
        ReminderType.ApproveOverdue => "Odobravanje kasni",
        ReminderType.Nudged => $"Podsjetnik: {item.From}",
        _ => item.Kind.ToString()
    };

    public static string Line(ReminderDto item, string? language = null)
    {
        if (English(language))
        {
            var monthEn = $"{BosnianTexts.MonthEnglish(item.Month)} {item.Year}";
            var deadlineEn = item.Deadline.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            return item.Kind switch
            {
                ReminderType.SubmitSoon => $"The timesheet for {monthEn} is due by {deadlineEn}",
                ReminderType.SubmitOverdue => $"The timesheet for {monthEn} should have been submitted by {deadlineEn}",
                ReminderType.FixReturned => $"The timesheet for {monthEn} was returned. Read the message, correct the entry and submit it again.",
                ReminderType.ApproveWaiting => $"Timesheets waiting for your approval: {item.Count}. The deadline is {deadlineEn}",
                ReminderType.ApproveOverdue => $"Timesheets waiting for your approval: {item.Count}. The deadline was {deadlineEn}",
                ReminderType.Nudged => $"{item.From} reminds you to submit the timesheet for {monthEn}. The deadline is {deadlineEn}",
                _ => monthEn
            };
        }

        var month = BosnianTexts.Month(item.Year, item.Month);
        var deadline = item.Deadline.ToString("dd.MM.yyyy.", CultureInfo.InvariantCulture);
        return item.Kind switch
        {
            ReminderType.SubmitSoon => $"Šihtarica za {month} se predaje do {deadline}",
            ReminderType.SubmitOverdue => $"Šihtarica za {month} je trebala biti predana do {deadline}",
            ReminderType.FixReturned => $"Šihtarica za {month} je vraćena. Pročitajte poruku, ispravite unos i predajte je ponovo.",
            ReminderType.ApproveWaiting => $"Broj šihtarica koje čekaju vaše odobrenje: {item.Count}. Rok je {deadline}",
            ReminderType.ApproveOverdue => $"Broj šihtarica koje čekaju vaše odobrenje: {item.Count}. Rok je bio {deadline}",
            ReminderType.Nudged => $"{item.From} vas podsjeća da predate šihtaricu za {month}. Rok je {deadline}",
            _ => month
        };
    }

    private static int Order(ReminderDto item) => item.Kind switch
    {
        ReminderType.FixReturned => 0,
        ReminderType.Nudged => 1,
        ReminderType.SubmitOverdue => 1,
        ReminderType.ApproveOverdue => 2,
        ReminderType.SubmitSoon => 3,
        ReminderType.ApproveWaiting => 4,
        _ => 9
    };
}
