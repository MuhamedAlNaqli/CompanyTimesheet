# Ručno testiranje

Najkraći put do probe na jednom računaru: **tri koraka**, bez podešavanja.

## 1. Priprema

1. Na stranici *Releases* skinite dvije datoteke: `CompanyTimesheet-v….msi` (aplikacija) i `CompanyTimesheet-Server-v….zip` (probni server).
2. Raspakujte zip bilo gdje i dvaput kliknite **`start-test-server.cmd`**. Prozor ostaje otvoren dok testirate. Server odmah ima šest probnih osoba i dva predata mjeseca.
3. Instalirajte MSI i pokrenite **Evidencija radnog vremena**. Adresa servera je već `http://localhost:5090` (mijenja se u aplikaciji, dugme *Server* na prijavnom ekranu).

**Probne osobe** (lozinka za sve: `Demo-password-1`): `lejla` (radnica), `haris` i `maja` (radnici), `tarik` (njihov nadređeni), `selma` (iznad Tarika),
`nermin` (radnik kod Selme). Administrator: `admin` / `Admin-first-password-1` (traži izbor vlastite lozinke).

> **Upozorenje Windowsa pri instalaciji.** Izdanja za sada nisu potpisana, pa Windows (SmartScreen) može prikazati "Windows protected your PC".
> Desni klik na preuzeti MSI i zip → *Properties* → označiti *Unblock* → *OK*, ili u upozorenju *More info → Run anyway*.

## 2. Scenariji

Redom su; svaki se oslanja na prethodni. Uz svaki piše šta treba vidjeti.

### A. Supervizor (Tarik)
- Prijava `tarik`. Tab **Za odobravanje (2)**: Lejla i Maja su predale prošli mjesec.
- *Otvori* Lejlinu šihtaricu: vidi se prekovremeni (19:30–22:00, "Podrška pri obračunu") i godišnji odmor od 15. do 17. *Vrati na ispravku…* (poruka je obavezna) ili *Odobri*.
- Tab **Moj tim**: ko je predao tekući mjesec (Nije predano / Predano / Odobreno). *Podsjeti* uz osobu; drugi pokušaj: "Podsjetnik … je već poslan".
- **Pregled za odobravanje** → *Kopiraj* → zalijepite u Notepad. Svako ima red; odsustva su periodi; prekovremeni su tabela s tabulatorima.

### B. Radnik (Lejla)
- Odjava, prijava `lejla`. Tekući mjesec već ima **8 sati za svaki radni dan** (nema "Popuni"). Vikendi i praznici su prazni.
- Prekidač **lista / kalendar** gore desno (pamti se).
- *Novi unos*: šifra odsustva + period od–do (vikendi se preskaču); šifra prekovremenog + vrijeme od–do + opis (opis je obavezan); službeno putovanje kao period.
- Dan se mijenja klikom na njega; odsustvo se skida brisanjem i dan se vraća na standardan.
- *Predaj na odobravanje* → Da/Ne. Mjesec se zaključava; piše ko je na redu.

### C. Povratak i verzije
- Ako je Tarik vratio Lejlinu šihtaricu: kao Lejla pročitajte poruku, *Odgovori*, ispravite, *Predaj* ponovo.
- U desnom panelu **Predate verzije** → *Otvori*: starija verzija samo za čitanje, uz traku za povratak.
- Tarik odobrava, zatim Selma (drugi korak lanca). Poslije posljednjeg koraka status je **Odobreno**.

### D. Zamjena
- Kao Tarik: *Za odobravanje* → **Zamjena dok ste odsutni**: izaberite Harisa i period → *Postavi zamjenu*.
- Kao **Haris**: u *Za odobravanje* se vide šihtarice "kao zamjena za Tarik". Haris smije odobriti ili vratiti; u historiji piše ko je odlučio i za koga. Svoju šihtaricu ne smije odobriti.

### E. Administracija (admin)
- Prijava `admin` otvara **samo administraciju**: administrator nema šihtaricu, ni podsjetnike, ni tabove za odobravanje. Prijavni prozor je zaseban; poslije prijave zamjenjuje ga prozor uloge, a *Odjava* vraća prijavni prozor.
- *Nova osoba*: ime, (šifra: prazno = sljedeća slobodna Z-nnn), e-pošta, nadređeni, korisničko ime i prva lozinka. Nova osoba se odmah vidi u listi i u stablu, i može se prijaviti (pri prvoj prijavi bira svoju lozinku).
- Tabela ima zaglavlja kolona (Šifra, Ime i prezime, Nadređeni, Korisničko ime i uloga). Dugme **Hijerarhija** pokazuje stablo: na vrhu osobe bez nadređenog, ispod njih podređeni; strelica ispod osobe otvara ili zatvara njene podređene, klik na osobu je uređuje.
- *Administracija*: lista sa pretragom. *Uredi*: ime, e-pošta, nadređeni, uloge, datumi zaposlenja, korisničko ime i nova lozinka (opcija "mora izabrati svoju pri prvoj prijavi").
- *Uvezi listu radnika*: izaberite `sample-employees.csv` iz foldera servera (Z-001…Z-006 već postoje; vidi se pregled "ažurirano").
- Pokušaj da Selma dobije za nadređenog Lejlu mora biti odbijen (krug u hijerarhiji). *Provjeri revizijski dnevnik* → "u redu".

### F. Prijava i sigurnost
- Pogrešna lozinka: "Pogrešno korisničko ime ili lozinka." Pet zaredom zaključava račun 15 minuta.
- *Odjava* ostavlja prazan prijavni ekran. Uz "Zapamti me" sljedeće pokretanje prijavljuje samo.
- Dugme *Server* na prijavnom ekranu: druga adresa (npr. server na internetu, `docs/HOSTING_DEMO.md`); `http://` je dozvoljen samo za ovaj računar.

### G. Podsjetnici
- Pri prijavi označite **Zapamti me** (bez toga zakazani zadatak nema prijavu i šuti).
- Izgled odmah: `"C:\Program Files\SmartPoint\CompanyTimesheet\CompanyTimesheet.exe" --reminder-test`.
- Pravo: Tarik *Podsjeti* osobu koja nije predala, zatvorite aplikaciju; u roku od 30 minuta (radnim danom 07:00–20:00) stiže obavijest.
  Zadatak: *Task Scheduler → CompanyTimesheet → Reminder*.

### H. Ažuriranja
- Dugme s ikonom oblaka u zaglavlju: "Imate najnoviju verziju (…)" ili prozor za nadogradnju kad postoji novije izdanje.

## 3. Šta prijaviti

Za svaku grešku: korak, šta ste očekivali, šta se desilo (snimak ekrana) i datoteka loga iz `%LOCALAPPDATA%\CompanyTimesheet\logs`.
Prijave: https://github.com/MuhamedAlNaqli/CompanyTimesheet/issues.

Isključeno namjerno: izvoz u Excel/PDF, uloge finansija i pravnog, prijava Windows nalogom, mailovi podsjetnika (zastavice).

## 4. Čišćenje

1. Zatvorite prozor servera i obrišite `%LOCALAPPDATA%\CompanyTimesheetTestServer` (baza) i `%LOCALAPPDATA%\CompanyTimesheet` (postavke i log).
2. *Settings → Apps*: deinstalirajte **Evidencija radnog vremena** (uklanja i zakazani zadatak).
