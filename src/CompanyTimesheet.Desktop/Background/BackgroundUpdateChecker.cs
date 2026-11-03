using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace CompanyTimesheet.Desktop.Background;

/// <summary>A release listed in the update feed</summary>
public sealed record AvailableUpdate(Version Version, string DisplayVersion);

/// <summary>
/// A light-weight look at the update feed for the headless background process. It only answers "is there a newer
/// version?"; downloading and installing stay with the update window inside the app.
/// </summary>
public static class BackgroundUpdateChecker
{
    private static readonly XNamespace Sparkle = "http://www.andymatuschak.org/xml-namespaces/sparkle";

    /// <summary>The newest Windows release in an appcast, or null when it lists none</summary>
    public static AvailableUpdate? ParseLatest(string appcastXml)
    {
        var document = XDocument.Parse(appcastXml);
        return document.Descendants("item")
            .Select(item => new
            {
                Os = (string?)item.Element(Sparkle + "os") ?? (string?)item.Element("enclosure")?.Attribute(Sparkle + "os"),
                Raw = (string?)item.Element(Sparkle + "version") ?? (string?)item.Element("enclosure")?.Attribute(Sparkle + "version"),
                Short = (string?)item.Element(Sparkle + "shortVersionString") ?? (string?)item.Element("enclosure")?.Attribute(Sparkle + "shortVersionString")
            })
            .Where(i => i.Os == null || i.Os.Equals("windows", StringComparison.OrdinalIgnoreCase))
            .Select(i => Version.TryParse(i.Raw, out var version) ? new AvailableUpdate(version, i.Short ?? version.ToString(3)) : null)
            .Where(u => u != null)
            .OrderByDescending(u => u!.Version)
            .FirstOrDefault();
    }

    /// <summary>The update that is newer than <paramref name="current"/>, or null when the app is up to date or the feed cannot be read</summary>
    public static async Task<AvailableUpdate?> FindNewerAsync(string feedUrl, Version current, TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CompanyTimesheet-BackgroundUpdater");
        var latest = ParseLatest(await client.GetStringAsync(feedUrl));
        return latest != null && latest.Version > current ? latest : null;
    }
}
