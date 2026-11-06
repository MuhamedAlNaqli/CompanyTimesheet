namespace Company.Api.Texts;

/// <summary>Which language the texts of an answer are written in: English when the program asks for it (Accept-Language), Bosnian otherwise</summary>
public static class RequestLanguage
{
    public static string Of(HttpContext? context) => From(context?.Request.Headers.AcceptLanguage.ToString());

    public static string From(string? header) =>
        header != null && header.TrimStart().StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "bs";
}
