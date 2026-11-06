using System.Text.RegularExpressions;
using Company.Api.Contracts;
using CompanyTimesheet.Desktop;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CompanyTimesheet.Desktop.Tests;

/// <summary>The English interface: every text has an English wording, and the language follows the person</summary>
[TestClass]
public class LocalizationTests
{
    [TestCleanup]
    public void BackToBosnian() => Loc.Initialize("bs");

    private static string SourceFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CompanyTimesheet.sln"))) dir = dir.Parent;
        Assert.IsNotNull(dir, "the solution folder was not found");
        return Path.Combine(dir!.FullName, "src", "CompanyTimesheet.Desktop");
    }

    [TestMethod]
    public void EveryTextOfTheWindowsAndOfTheCode_HasAnEnglishWording()
    {
        var missing = new List<string>();
        foreach (var file in Directory.GetFiles(SourceFolder(), "*.axaml"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"em:Tr '([^']*)'"))
                if (!Translations.English.ContainsKey(Regex.Unescape(m.Groups[1].Value))) missing.Add(Path.GetFileName(file) + ": " + m.Groups[1].Value);

        foreach (var file in Directory.GetFiles(SourceFolder(), "*.cs").Where(f => !f.EndsWith("Translations.cs")))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "Loc\\.[TF]\\(\"((?:[^\"\\\\\\n]|\\\\.)*)\""))
                if (!Translations.English.ContainsKey(Regex.Unescape(m.Groups[1].Value))) missing.Add(Path.GetFileName(file) + ": " + m.Groups[1].Value);

        Assert.AreEqual(0, missing.Count, "texts without an English wording:\n" + string.Join("\n", missing));
    }

    [TestMethod]
    public void AnEnglishWording_KeepsTheValuesOfTheBosnianOne()
    {
        // A pattern with {0}, {1}… must have the same placeholders in both languages, or a value would go missing
        foreach (var (bosnian, english) in Translations.English)
        {
            var a = Regex.Matches(bosnian, @"\{(\d+)").Select(m => m.Groups[1].Value).OrderBy(x => x).ToList();
            var b = Regex.Matches(english, @"\{(\d+)").Select(m => m.Groups[1].Value).OrderBy(x => x).ToList();
            CollectionAssert.AreEqual(a, b, $"the placeholders of «{bosnian}» differ from «{english}»");
            Assert.AreNotEqual("", english, bosnian);
        }
    }

    [TestMethod]
    public void ATextIsShownInTheLanguageOfTheInterface_AndAnUnknownOneStaysAsItIs()
    {
        Loc.Initialize("bs");
        Assert.AreEqual("Odjava", Loc.T("Odjava"));

        Loc.Language = "en";
        Assert.AreEqual("Sign out", Loc.T("Odjava"));
        Assert.AreEqual("Tekst koji niko nije preveo", Loc.T("Tekst koji niko nije preveo"));
        Assert.AreEqual("Saved: Amra", Loc.F("Sačuvano: {0}", "Amra"));
    }

    [TestMethod]
    public void ChangingTheLanguage_TellsTheWindows()
    {
        Loc.Initialize("bs");
        var told = 0;
        void Handler() => told++;
        Loc.Changed += Handler;
        var bound = new List<string?>();
        Loc.Instance.PropertyChanged += (_, e) => bound.Add(e.PropertyName);

        Loc.Language = "en";
        Loc.Language = "en";
        Loc.Changed -= Handler;

        Assert.AreEqual(1, told, "only a real change is announced");
        CollectionAssert.Contains(bound, "Version");
        Assert.AreEqual("Sign out", (string?)TrConverter.Instance.Convert(Loc.Instance.Version, typeof(string), "Odjava", System.Globalization.CultureInfo.InvariantCulture),
            "a text of a window reads its new wording when the version changes");
    }

    [TestMethod]
    public void MonthsDaysCodesAndStatuses_FollowTheLanguage()
    {
        Loc.Initialize("bs");
        Assert.AreEqual("Novembar 2026.", EmployeeFormat.Month(2026, 11));
        Assert.AreEqual("Redovan rad", EmployeeFormat.CodeName(1010));

        Loc.Language = "en";
        Assert.AreEqual("November 2026", EmployeeFormat.Month(2026, 11));
        Assert.AreEqual("Regular work", EmployeeFormat.CodeName(1010));
        Assert.AreEqual("Approved", EmployeeFormat.Status(SheetStatus.Approved));
        Assert.AreEqual("weekend", EmployeeFormat.DayType(DayKind.Weekend));
        Assert.AreEqual("Thursday", EmployeeFormat.Weekday(new DateOnly(2026, 11, 5)));
        Assert.AreEqual("05/11", EmployeeFormat.ShortDate(new DateOnly(2026, 11, 5)));
        Assert.AreEqual("2.5 h", EmployeeFormat.Hours(150));
    }

    [TestMethod]
    public void TheEnglishAndTheBosnianPalettesAndLanguagesAreOffered()
    {
        CollectionAssert.AreEqual(new[] { "bs", "en" }, Loc.Languages);
        Assert.IsTrue(Loc.IsKnown("en"));
        Assert.IsFalse(Loc.IsKnown("de"));
    }

    [TestMethod]
    public void TheRestartAfterAnUpdate_WaitsForTheProgramAndTheInstaller_ThenStartsTheProgram()
    {
        var script = CompanyTimesheet.Desktop.Services.UpdateRelauncher.Script(4321, @"C:\Program Files\SmartPoint\Company's\CompanyTimesheet.exe");

        StringAssert.Contains(script, "Wait-Process -Id 4321");
        StringAssert.Contains(script, "msiexec", "it waits while an installer is running");
        StringAssert.Contains(script, @"HKLM:\SOFTWARE\SmartPoint\CompanyTimesheet", "and until the installed version changes");
        StringAssert.Contains(script, "Company''s", "an apostrophe in the path is escaped");
        Assert.IsTrue(script.TrimEnd().EndsWith("Start-Process -FilePath $exe"));
    }
}
