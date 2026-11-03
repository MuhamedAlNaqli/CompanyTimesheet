using System.Net.Http.Json;
using Company.Api.Contracts;
using Company.Api.Services;
using Company.Api.Texts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Timesheet.Domain;

namespace Company.Api.Tests;

/// <summary>The enums of the contracts and the texts that moved out of the domain library</summary>
[TestClass]
public class TextsAndEnumsTests
{
    [TestMethod]
    public void EveryValueOfTheDomainHasItsCounterpartInTheContracts()
    {
        foreach (var v in Enum.GetValues<TimesheetStatus>()) Assert.AreEqual(v.ToString(), v.ToDto().ToString());
        foreach (var v in Enum.GetValues<DayType>()) Assert.AreEqual(v.ToString(), v.ToDto().ToString());
        foreach (var v in Enum.GetValues<Severity>()) Assert.AreEqual(v.ToString(), v.ToDto().ToString());
        foreach (var v in Enum.GetValues<WorkflowEventKind>()) Assert.AreEqual(v.ToString(), v.ToDto().ToString());
        foreach (var v in Enum.GetValues<ReminderKind>()) Assert.AreEqual(v.ToString(), v.ToDto().ToString());
        foreach (var v in Enum.GetValues<SkipReason>()) Assert.AreEqual(v.ToString(), v.ToDto().ToString());
    }

    [TestMethod]
    public async Task EnumsTravelAsTheirNames()
    {
        var factory = new ApiFactory();
        using (factory)
        {
            await factory.ImportAsync(ApiFactory.SmallList);
            await factory.LinkAsync("Z-001", @"corp\amra");
            await factory.As(@"corp\amra").PutAsJsonAsync("/api/timesheet/2026/9/days/2026-09-01", new SetEntryRequest(Codes.RegularWork, 240));
            var json = await factory.As(@"corp\amra").GetStringAsync("/api/timesheet/2026/9");
            StringAssert.Contains(json, "\"status\":\"Draft\"");
            StringAssert.Contains(json, "\"type\":\"Working\"");
            StringAssert.Contains(json, "\"severity\":\"Warning\"");
        }
    }

    [TestMethod]
    public void EveryCodeHasANameInBothLanguages_AndTheLegendOrderIsKept()
    {
        foreach (var code in TimeCodes.All)
        {
            Assert.IsTrue(TimeCodeNames.Bs(code.Code).Length > 0 && TimeCodeNames.Bs(code.Code) != code.Code.ToString(), $"{code.Code} has no Bosnian name");
            Assert.IsTrue(TimeCodeNames.En(code.Code).Length > 0 && TimeCodeNames.En(code.Code) != code.Code.ToString(), $"{code.Code} has no English name");
        }

        Assert.AreEqual("Redovan rad", TimeCodeNames.Bs(Codes.RegularWork));
        Assert.AreEqual(TimeCodes.All.Count, TimeCodeNames.Known.Count());
    }

    [TestMethod]
    public void TheWeekdayLettersOfTheSheetAreBosnian()
    {
        // The sample sheet starts on Tuesday 1 September: U S Č P S N P. Its Sunday 27 September was labelled Thursday there.
        var week = Enumerable.Range(1, 7).Select(d => BosnianTexts.WeekdayInitial(new DateOnly(2026, 9, d))).ToArray();
        CollectionAssert.AreEqual(new[] { "U", "S", "Č", "P", "S", "N", "P" }, week);
        Assert.AreEqual("N", BosnianTexts.WeekdayInitial(new DateOnly(2026, 9, 27)));
    }

    [TestMethod]
    public void EveryRuleHasAWording_WithTheDatesAndHoursWrittenTheSameOnEveryServer()
    {
        var all = new List<ValidationIssue>
        {
            Issue("V-08", "unknown", ("employee", "Z-001")), Issue("V-08", "inactive", ("employee", "Z-001")),
            Issue("V-02", null, ("code", 1022), ("date", D(5))), Issue("V-17", null, ("code", 1020), ("date", D(5))),
            Issue("V-06", null, ("date", D(5))), Issue("V-07", null, ("date", D(9)), ("code", 1010)),
            Issue("V-14", null, ("date", D(7)), ("minutes", 450), ("normMinutes", 480)),
            Issue("V-12", "hours", ("date", D(7)), ("step", 30)), Issue("V-12", "overtime", ("date", D(7)), ("step", 30)),
            Issue("V-05", null, ("date", D(7)), ("codes", "1010, 1022")), Issue("V-04", null, ("date", D(7))),
            Issue("V-20", null, ("date", D(9)), ("holidayMinutes", 300), ("workedMinutes", 120), ("normMinutes", 480)),
            Issue("V-15", null, ("date", D(7))),
            Issue("V-01", null, ("date", D(7)), ("declaredMinutes", 120), ("intervalMinutes", 150)),
            Issue("V-03", null, ("date", D(7)), ("conflictDate", D(7)), ("regularStart", new TimeOnly(8, 30)), ("regularEnd", new TimeOnly(17, 0))),
            Issue("V-13", null, ("date", D(7))), Issue("V-18", null, ("date", D(7))),
            Issue("V-19", null, ("code", 1027), ("date", D(7))), Issue("V-19b", null, ("code", 1030), ("date", D(7))), Issue("V-19c", null, ("code", 1020), ("date", D(7))),
            Issue("V-10", null, ("date", D(7)), ("start", new TimeOnly(18, 0)), ("end", new TimeOnly(20, 0))),
            Issue("V-09", null, ("date", D(7)), ("start", new TimeOnly(18, 0)), ("end", new TimeOnly(20, 0)), ("otherDate", D(7)), ("otherStart", new TimeOnly(19, 0)), ("otherEnd", new TimeOnly(21, 0))),
            Issue("V-11", null, ("date", D(7)), ("minutes", 1500)),
            Issue("V-21", null, ("fundMinutes", 450 * 20), ("expectedMinutes", 480 * 20))
        };

        foreach (var issue in all)
        {
            var text = ValidationTexts.Message(issue);
            Assert.AreNotEqual(issue.Key, text, $"{issue.Key} has no wording");
            Assert.IsFalse(text.Contains('{'), text);
        }

        StringAssert.Contains(ValidationTexts.Message(all.Single(i => i.Key == "V-14")), "(7,5 h)");
        StringAssert.Contains(ValidationTexts.Message(all.Single(i => i.Key == "V-02")), "05.09.2026.");
        StringAssert.Contains(ValidationTexts.Message(all.Single(i => i.Key == "V-03")), "08:30–17:00");
    }

    [TestMethod]
    public void EveryRuleOfTheValidatorIsWordedHere()
    {
        // The rule keys the validator can produce are the keys of the table above; a new rule without a wording fails this
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Timesheet.Domain", "Validation.cs"));
        var keys = System.Text.RegularExpressions.Regex.Matches(source, @"Add(?:Variant)?\(""(V-\d+b?c?)""(?:, ""(\w+)"")?")
            .Select(m => m.Groups[2].Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}" : m.Groups[1].Value).Distinct().ToList();
        Assert.IsTrue(keys.Count >= 21, string.Join(",", keys));

        foreach (var key in keys)
        {
            var issue = new ValidationIssue(key.Split('.')[0], Severity.Warning, null, new Dictionary<string, object>(), key.Contains('.') ? key.Split('.')[1] : null);
            // With no values the wording cannot be built, but it must at least exist: only a missing key answers with the key itself
            try
            {
                Assert.AreNotEqual(key, ValidationTexts.Message(issue));
            }
            catch (KeyNotFoundException)
            {
                // The wording was found and asked for a value: it exists
            }
        }
    }

    private static DateOnly D(int day) => new(2026, 9, day);

    private static ValidationIssue Issue(string rule, string? variant, params (string, object)[] values) =>
        new(rule, Severity.Warning, null, values.ToDictionary(v => v.Item1, v => v.Item2), variant);

    [TestMethod]
    public void TheDomainLibraryHoldsNoWordsForPeople()
    {
        // The rules say what was found; the wording belongs to the programs that show it (see ValidationTexts, ImportTexts, BosnianTexts)
        var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Timesheet.Domain"));
        foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var code = string.Join(Environment.NewLine, File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("///") && !l.TrimStart().StartsWith("//")));
            Assert.IsFalse(code.Any(c => "čćšžđČĆŠŽĐ".Contains(c)), $"{Path.GetFileName(file)} has text in Bosnian");
        }
    }

    [TestMethod]
    public void EveryKindOfImportFindingHasAWording()
    {
        foreach (var kind in Enum.GetValues<ImportIssueKind>())
        {
            var text = ImportTexts.Message(new ImportIssue(ImportSeverity.Warning, kind, "Z-001", "x"));
            Assert.AreNotEqual(kind.ToString(), text);
        }

        StringAssert.Contains(ImportTexts.Message(new ImportIssue(ImportSeverity.Error, ImportIssueKind.BadCode, Value: "A1")), "\"A1\"");
    }
}
