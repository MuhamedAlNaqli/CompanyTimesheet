using System.Globalization;
using System.Text;
using Company.Api.Contracts;

namespace Company.Api.Texts;

/// <summary>The words of a reminder mail (the same as the banner and the notification of the desktop app say)</summary>
public static class ReminderTexts
{
    public static string Subject(IReadOnlyList<ReminderDto> items) =>
        items.Count == 1 ? Title(items[0]) : $"Evidencija radnog vremena: {items.Count} obavijesti";

    public static string Body(string name, IReadOnlyList<ReminderDto> items)
    {
        var text = new StringBuilder();
        text.AppendLine($"Poštovani/a {name},");
        text.AppendLine();
        foreach (var item in items.OrderBy(Order)) text.AppendLine("• " + Line(item));
        text.AppendLine();
        text.AppendLine("Šihtaricu popunjavate u aplikaciji Evidencija radnog vremena.");
        return text.ToString();
    }

    public static string Title(ReminderDto item) => item.Kind switch
    {
        ReminderType.SubmitSoon => "Predajte šihtaricu",
        ReminderType.SubmitOverdue => "Šihtarica kasni",
        ReminderType.FixReturned => "Šihtarica je vraćena na ispravku",
        ReminderType.ApproveWaiting => "Šihtarice čekaju odobrenje",
        ReminderType.ApproveOverdue => "Odobravanje kasni",
        ReminderType.Nudged => $"Podsjetnik: {item.From}",
        _ => item.Kind.ToString()
    };

    public static string Line(ReminderDto item)
    {
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
