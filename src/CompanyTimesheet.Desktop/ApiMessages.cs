using Company.Api.Client;

namespace CompanyTimesheet.Desktop;

/// <summary>
/// What a refusal of the server says to the person, in the language of the interface. The server answers with a code and an English
/// sentence for the log; this is the sentence for the screen. A code nobody described is shown with the server's own sentence.
/// </summary>
public static class ApiMessages
{
    public static string Describe(ApiException ex) => ex.Code switch
    {
        "changed" => Loc.T("Šihtaricu je u međuvremenu promijenio neko drugi ili drugi prozor. Osvježite prikaz i pokušajte ponovo."),
        "wrong-status" => Loc.T("Šihtarica trenutno nije u stanju u kojem se to može uraditi (možda je već predana ili odobrena). Osvježite prikaz."),
        "self-approval" => Loc.T("Šihtaricu ne može odobriti osoba čija je."),
        "no-chain" => Loc.T("Administrator još nije odredio ko odobrava vašu šihtaricu. Obratite se administratoru."),
        "message-required" => Loc.T("Upišite poruku."),
        "outside-month" => Loc.T("Datum nije u mjesecu koji je otvoren."),
        "outside-employment" => Loc.T("Datum je izvan perioda zaposlenja."),
        "invalid-hours" => Loc.T("Sati nisu ispravni: upišite između jednog minuta i 24 sata."),
        "invalid-interval" => Loc.T("Početak i kraj ne smiju biti isti."),
        "invalid-absence" => Loc.T("Odsustvo nije ispravno: provjerite šifru i period."),
        "invalid-month" => Loc.T("Mjesec nije ispravan."),
        "overtime-derived" => Loc.T("Prekovremeni rad se upisuje kao interval vremena, a ne kao šifra na danu."),
        "unknown-code" => Loc.T("Ta šifra ne postoji."),
        "inactive-code" => Loc.T("Ta šifra se više ne koristi. Izaberite drugu."),
        "revision-too-new" => Loc.T("Ta verzija šihtarice je napravljena novijom verzijom programa. Ažurirajte program."),
        "revision-unreadable" => Loc.T("Ta verzija šihtarice se više ne može pročitati."),
        "no-approved-sheets" => Loc.T("Za izabrani mjesec nema odobrenih šihtarica."),
        "no-credentials" => Loc.T("Ovaj račun nema korisničko ime i lozinku."),
        "password-change-required" => Loc.T("Prvo izaberite svoju lozinku."),
        "forbidden" => Loc.T("Nemate pravo na ovu radnju."),
        "forbidden-origin" or "missing-header" => Loc.T("Server je odbio zahtjev. Pokušajte ponovo; ako se ponavlja, ažurirajte program."),
        "not-found" => Loc.T("Ono što tražite nije pronađeno."),
        "too-long" => Loc.T("Tekst je predug."),
        "server-error" => Loc.T("Server nije mogao završiti zahtjev. Pokušajte ponovo; ako se ponavlja, javite administratoru."),
        _ => ex.Message
    };
}
