namespace Company.Api;

/// <summary>
/// Parts of the product that are built but not switched on. They are off until the configuration says otherwise
/// (<c>Features:FinanceAndLegal</c>), so a company that does not use them sees nothing of them.
/// </summary>
/// <param name="FinanceAndLegal">
/// The legal department as the last approver of every chain, the finance role and the export of everything approved.
/// Off: the chain runs to the top of the chart, the roles are ignored and the export is not served.
/// </param>
/// <param name="SheetExport">
/// The timesheet as an Excel or PDF file (the own month, the approver's copy, the team, the summary). Off: the summary for the
/// approver is read and copied as text only.
/// </param>
/// <param name="EmailReminders">
/// The reminders also go out as one mail a day to the people who have an address (needs the Smtp settings). Off: only the
/// notification and the banner of the desktop app remind.
/// </param>
public sealed record CompanyFeatures(bool FinanceAndLegal, bool SheetExport = false, bool EmailReminders = false)
{
    public static CompanyFeatures From(IConfiguration configuration) =>
        new(configuration.GetValue<bool>("Features:FinanceAndLegal"), configuration.GetValue<bool>("Features:SheetExport"), configuration.GetValue<bool>("Features:EmailReminders"));
}
