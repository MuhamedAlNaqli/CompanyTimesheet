# Ručno testiranje (verzija 0.1.0)

Ovako se aplikacija isprobava na jednom računaru, bez firminog servera. Treba vam dvoje: **aplikacija** (MSI) i **probni server**
(zip) iz iste verzije na stranici *Releases*. Server pamti podatke u jednoj SQLite datoteci na vašem računaru; ništa ne ide na internet.

## 1. Priprema (jednom)

1. **Skinite** s https://github.com/MuhamedAlNaqli/CompanyTimesheet/releases/latest dvije datoteke:
   `CompanyTimesheet-v0.1.0.0-win-x64.msi` i `CompanyTimesheet-Server-v0.1.0-win-x64.zip`.
2. **Server:** raspakujte zip u neki folder (npr. `C:\CompanyTimesheetServer`) i dvaput kliknite `start-test-server.cmd`. Prozor ostaje otvoren
   dok testirate (zatvaranje prozora zaustavlja server). Provjera: u pregledniku otvorite http://localhost:5090/health, treba pisati `{"status":"ok"}`.
   Baza je `%LOCALAPPDATA%\CompanyTimesheetTestServer\test.db`; brisanjem te datoteke počinjete ispočetka.
3. **Aplikacija:** pokrenite MSI (traži administratorska prava). Windows može upozoriti ("Windows protected your PC" / SmartScreen) jer
   program još nije potpisan: *More info → Run anyway*.
4. **Adresa servera.** Aplikacija mora znati gdje je server. U PowerShellu (običan korisnik, bez administratora):

   ```powershell
   setx Company__ApiUrl http://localhost:5090
   ```

   Zatim **zatvorite i ponovo otvorite** aplikaciju (varijabla se čita pri pokretanju). (Alternativa: datoteka
   `C:\Program Files\SmartPoint\CompanyTimesheet\appsettings.local.json` sa sadržajem `{ "Company": { "ApiUrl": "http://localhost:5090" } }`.)
5. Pokrenite **Evidencija radnog vremena** iz Start menija.

## 2. Scenariji

Redom su; svaki se oslanja na prethodne. Uz svaki je napisano šta treba vidjeti.

### A. Prva prijava administratora
- Prijava: korisničko ime `admin`, lozinka `Admin-first-password-1`.
- **Očekivano:** odmah se traži izbor vlastite lozinke (najmanje 10 znakova). Kratka ili različita ponovljena lozinka daje poruku.
  Nakon izbora ulazite u prozor; vidite tabove *Moja šihtarica*, *Za odobravanje* i **Administracija**.

### B. Administracija
1. *Administracija → Uvezi listu radnika* i izaberite `sample-employees.csv` iz foldera servera. **Očekivano:** pitanje "Uvesti 6 od 6 redova?",
   nakon potvrde poruka "Dodano: 6…" i šest osoba na listi (uz administratora).
2. *Uredi* uz **Lejla Hadžić**: upišite korisničko ime `lejla` i lozinku `Privremena-1234` (prva lozinka; osoba je mijenja pri prvoj prijavi).
   Isto za ostale: `tarik`, `selma`, `haris`, `maja`, `nermin` (svi lozinka `Privremena-1234`).
3. Hijerarhija (polje *Nadređeni* u dijalogu): Lejla → Tarik, Haris → Tarik, Maja → Tarik, Tarik → Selma, Nermin → Selma. Selma nema nadređenog.
   Pokušaj da Selma dobije za nadređenog Lejlu mora biti odbijen porukom o krugu u hijerarhiji.
4. *Provjeri revizijski dnevnik* → "Revizijski dnevnik je u redu (N zapisa)."
5. Pretraga: upišite dio imena ili korisničkog imena u polje za traženje; lista se sužava.

### C. Radnik: unos mjeseca (Lejla)
- *Odjava*, pa prijava `lejla` / `Privremena-1234`. **Očekivano:** izbor vlastite lozinke, zatim prozor tekućeg mjeseca.
- Svi **radni dani već imaju 8 sati** (nema dugmeta "Popuni"). Vikendi i državni praznici su prazni. Na desnoj strani: zbir mjeseca (fond 100%).
- Prebacite **listu / kalendar** (dvije ikone gore desno); izbor se pamti.
- *Novi unos*: izaberite šifru **odsustva** (npr. godišnji odmor), period od–do → prikaže se koji dani se uzimaju (vikendi se preskaču) → sačuvajte.
  Dani dobijaju tu šifru; zbir ostaje 100% fonda.
- *Novi unos* sa šifrom **prekovremenog**: datum, od–do (npr. 19:30–22:00), kratak opis → u desnom panelu se vidi razdioba sati po vrsti prekovremenog.
  Opis je obavezan. Prekovremeni preko ponoći: dio poslije ponoći računa se u sljedeći mjesec.
- Dan mijenjate klikom na njega (olovka); odsustvo skidate brisanjem, pa se dan vraća na standardan.
- *Predaj na odobravanje* → potvrda (Da/Ne). **Očekivano:** mjesec se zaključava, status "Predano", piše ko je na redu (Tarik).

### D. Supervizor (Tarik)
- Prijava `tarik` / `Privremena-1234` (izbor lozinke). Tab **Za odobravanje (1)**.
- *Otvori* Lejlinu šihtaricu: vidite njene dane, prekovremeni i provjere. *Vrati na ispravku…* sa porukom ILI *Odobri*.
- Tab **Moj tim**: svi koji se javljaju Tariku sa statusom (Nije predano / Predano / Odobreno). *Podsjeti* uz osobu koja nije predala (ili
  *Podsjeti sve koji nisu predali*). Drugi pokušaj istog dana: "Podsjetnik … je već poslan".
- **Pregled za odobravanje** u tabu *Moj tim*: poruka kao ona koju timovi šalju (ime, "Nema prekovremenih sati niti izostanaka." ili
  "Prekovremeni sati prikazani u tabeli ispod.", odsustva/putovanja kao periodi, tabela prekovremenih). *Kopiraj* pa zalijepite u Notepad/Word:
  tabela se lijepi kao tabulatori.

### E. Povratak na ispravku i verzije
- Ako je Tarik vratio šihtaricu: kao Lejla vidite poruku, *Odgovori*, ispravite unos, *Predaj* ponovo.
- U desnom panelu **Predate verzije**: *Otvori* prikazuje starije stanje samo za čitanje (traka "Prikazana je verzija N…", dugme za povratak).
- Tarik odobrava; zatim Selma (sljedeći korak lanca) odobrava u tabu *Za odobravanje*. Poslije posljednjeg koraka status je **Odobreno**.

### F. Zamjena
- Kao Tarik: *Za odobravanje* → kartica **Zamjena dok ste odsutni**: izaberite Harisa, period (npr. od danas do sedam dana) → *Postavi zamjenu*.
- Neka Lejla ili Maja preda mjesec. Kao **Haris**: tab *Za odobravanje* sada prikazuje te šihtarice sa "kao zamjena za Tarik …". Haris smije odobriti ili
  vratiti; u historiji piše ko je odlučio i za koga. Haris ne smije odobriti **svoju** šihtaricu.

### G. Podsjetnici i notifikacije
- Pri prijavi označite **Zapamti me na ovom računaru** (bez toga zakazani zadatak nema prijavu i šuti).
- Odmah probajte izgled: `"C:\Program Files\SmartPoint\CompanyTimesheet\CompanyTimesheet.exe" --reminder-test` (pojavi se Windows obavijest).
- Pravo: kao Tarik *Podsjeti* osobu koja nije predala; zatvorite aplikaciju. U roku od 30 minuta (radnim danom 07:00–20:00) stiže obavijest
  "Podsjetnik: Tarik …". Zadatak je u *Task Scheduler → CompanyTimesheet → Reminder*. Klik na obavijest otvara aplikaciju.

### H. Prijava i sigurnost
- Pogrešna lozinka: "Pogrešno korisničko ime ili lozinka." Pet pogrešnih zaredom: račun se zaključa 15 minuta (admin ga otključava novom lozinkom).
- *Odjava* vraća formu za prijavu; na ekranu ne ostaje ništa od prethodne osobe. Uz "Zapamti me" sljedeće pokretanje prijavljuje samo.
- Admin: *Uredi → Ukloni korisničko ime* ili *Neaktivan* — ta osoba se više ne može prijaviti.

### I. Ažuriranja
- Dugme s ikonom oblaka u zaglavlju: "Imate najnoviju verziju (0.1.0.0)." (ako je to najnovije izdanje). Kad izađe novije izdanje, otvara se prozor
  za nadogradnju (na engleskom) i nudi instalaciju.

## 3. Šta prijaviti

Za svaku grešku napišite: korak, šta ste očekivali, šta se desilo (snimak ekrana), i datoteku loga iz `%LOCALAPPDATA%\CompanyTimesheet\logs`.
Prijave idu na https://github.com/MuhamedAlNaqli/CompanyTimesheet/issues.

Poznato i namjerno: izvoz u Excel/PDF i uloge finansija/pravnog su isključeni (zastavice); prijava Windows nalogom je isključena; mailovi
podsjetnika su isključeni.

## 4. Čišćenje poslije testa

1. Zatvorite prozor servera.
2. Obrišite `%LOCALAPPDATA%\CompanyTimesheetTestServer` (baza) i `%LOCALAPPDATA%\CompanyTimesheet` (postavke i log aplikacije).
3. *Settings → Apps*: deinstalirajte **Evidencija radnog vremena** (uklanja i zakazani zadatak).
4. Uklonite adresu servera: `reg delete HKCU\Environment /v Company__ApiUrl /f`.
