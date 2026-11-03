# Probni server na internetu

**GitHub sam ne može pokretati server.** Repozitorij čuva kod i izdanja; Pages služi samo statične stranice, a Actions radi poslove koji se završe. Server koji stalno
radi mora živjeti na hostingu. Ovaj repozitorij je spreman za besplatni demo na **Render**-u: `Dockerfile` pravi sliku servera, a `render.yaml` je "Blueprint".

Demo koristi SQLite datoteku u kontejneru: **podaci se gube pri svakom novom pokretanju** (to je namjera; pri svakom startu ponovo se prave probne osobe i dva predata mjeseca).
Besplatni servis se uspava poslije oko 15 minuta bez saobraćaja i budi se minutu-dvije pri prvom zahtjevu.

## Postavljanje (jednom, oko 5 minuta)

1. Na https://render.com napravite račun (može prijava preko GitHuba). To radite vi; ja ne mogu otvarati račune.
2. *New → Blueprint*, izaberite repozitorij `MuhamedAlNaqli/CompanyTimesheet` (ovlastite Render da ga čita) i potvrdite *Apply*.
3. Sačekajte da servis bude *Live* (prvi build traje nekoliko minuta). Adresa je oblika `https://companytimesheet-demo-xxxx.onrender.com`.
   Provjera: `…/health` mora vratiti `{"status":"ok"}`.
4. U servisu, tab *Environment*, pročitajte vrijednosti koje je Render sam izmislio: `Demo__Password` (lozinka probnih osoba) i `Bootstrap__AdminPassword` (prva lozinka administratora).

## Povezivanje aplikacije

U aplikaciji, na prijavnom ekranu: dugme **Server**, upišite adresu servisa, pa prijava `lejla` (ili `tarik`, `admin`…) sa lozinkom iz koraka 4.
Adresa se pamti. Scenariji su isti kao u `MANUAL_TESTING.md`.

## Šta znati

- To je javna adresa s izmišljenim osobama. Ne unosite prave podatke. Lozinke generiše Render, ali svako ko ih zna može se prijaviti.
- Ništa od ovoga nije put za firmu: firma pokreće server na svojoj infrastrukturi uz PostgreSQL (`API_IT_GUIDE.md`).
- Drugi hostinzi (Azure App Service, Fly.io, Railway…) rade po istom principu: slika iz `Dockerfile`-a, varijable okruženja kao u `render.yaml`.
