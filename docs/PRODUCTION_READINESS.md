# Production readiness

What the product is made of, what was checked before the stakeholder test, what has to be decided or set up before it runs for the whole
company, and the limits to know about. Read it together with `API_IT_GUIDE.md` (how IT runs the server) and `RELEASING.md` (how a release is made).

## How it is built

```
CompanyTimesheet.Desktop  (Avalonia, MVVM)        one program for everybody; the role decides the window
        │  ICompanyApiClient / ICompanySession / ICompanyAdminClient (HTTP + JSON, Company.Api.Client)
        ▼
Company.Api  (ASP.NET Core minimal API, EF Core)  endpoints → services → CompanyDbContext
        │            │
        │            └─ Timesheet.Domain  the rules, pure .NET: calendar, codes, validation, overtime, chain, workflow, deadlines
        ▼
PostgreSQL (production) / SQLite (development, tests)
```

* **The rules live once**, in `Timesheet.Domain`. The server applies them on every save; the app only shows what the server answers (no rule is
  copied into the app). The legend of codes lives in the database (`code_definitions`) and is loaded into the domain at start and after every change.
* **Every change is audited** in the same transaction as the change. The audit log is append-only and hash-chained; *Revizija → Provjeri
  revizijski dnevnik* proves nothing was changed behind the application's back.
* **A sheet is saved with a version number**; two people (or two windows) changing one sheet at once cannot both win. The person is told to refresh.
* **Roles** follow from the data: administrator (a flag), approver (has people below, is a designated approver, or is a deputy), employee.
* **Language and look** (Bosnian or English, theme, palette) are kept per person on the account; findings of the rules and reminder mails follow the
  language. Printed sheets stay Bosnian: they are the company's form.

## Checked for the stakeholder test

| Area | What was checked | Result |
| --- | --- | --- |
| Sign-in | Hashed passwords, hashed session tokens, lock after 5 wrong passwords, forced change of the first password, protected "remember me" file | In order |
| Requests | Header required for every change, requests from other origins refused, 2 MB limit, all failures answered in one error form | In order |
| Concurrency | Sheet version token, one writer to the audit chain (one server process) | In order; see "Limits" |
| Errors in the app | A refusal of the server is explained in the language of the interface for every code the server uses (test); a failure nobody caught is logged and does not close the program | Fixed in this round |
| Window sizes | Smallest size (1000 × 560): the side bar folds to icons by itself below 1200 px wide, tables scroll instead of hiding their actions, the title cannot run over the month buttons, the side bar scrolls when the window is low | Fixed in this round |
| Administration | Holidays and the audit log had no screen; the first screen now shows what needs attention (no holidays for the year, people without a user name, locked accounts, timesheets not approved) | Added in this round |
| Monitoring | `GET /health` (the process answers) and `GET /health/ready` (the database answers, 503 otherwise) | Added in this round |
| Tests | Domain, API (incl. the migrations) and desktop view models; every text of the app has an English wording (test) | Passing |

## To do before the whole company uses it

1. **State holidays.** The server writes the fixed ones of the Federation (1 and 2 January, 1 March, 1 and 2 May, 25 November) for this and the next year
   (`Holidays:SeedFixed`, on by default; a year that already has holidays is left alone). Check the list once with the company, and use *Praznici* for what
   differs (a canton declares a day, a law changes). Without holidays the monthly fund of hours is wrong; the first screen warns. Religious holidays are not
   set per person: an employee enters the code 1025 on the day, which counts as a day of absence in the fund.
2. **Sign the updates.** The app checks for updates in the appcast; without an Ed25519 key the signatures are *not verified*
   (`UpdateService:Ed25519PublicKey` is empty). Generate a key pair, keep the private key as a secret of the release workflow, put the public key
   in `appsettings.json` of the app. Also add the code-signing certificate (`SIGNING_PFX_BASE64`, see `RELEASING.md`) so Windows does not warn.
   Both need a decision of the company; neither can be done from the code alone.
3. **HTTPS.** The server warns at start when it listens without it. Passwords and session tokens travel in every request.
4. **Database.** A dedicated PostgreSQL user, daily backups with a tested restore, and (if wanted) `Database:MigrateOnStart=false` with
   `Company.Api.dll --migrate` run once per version by an account that may change the schema.
5. **First administrator.** `Bootstrap:AdminUsername` and `Bootstrap:AdminPassword` once; the password must be changed at the first sign-in.
   Remove them from the configuration afterwards.
6. **Mail reminders** (optional): `Features:EmailReminders` with the `Smtp` section, and the e-mail address of each person.
7. **Forgotten passwords.** An employee asks for a new password at the sign-in ("Zaboravili ste lozinku?"); the administrator sees the request
   in the list of people and sets a temporary password (the key button), which is also mailed when the `Smtp` section is configured. If the
   **administrator** forgets theirs, run on the server `Company.Api.dll --reset-password <user name>`: it prints a temporary password that must be
   changed at the next sign-in. The e-mail address is required for every new person.
8. **Update order.** A release that adds a column (v0.1.5, v0.1.8, v0.1.12) needs the new server first: it applies its migration when it starts.

## Limits to know about

* **One server process.** The audit chain is serialised by a lock inside the process, and the catalogue of codes is cached in it. Two processes
  behind a load balancer would race on the chain and show a new code late. Run one instance (with a restart policy).
* **Sign-in is limited per account, not per address.** Anybody can lock an account by guessing five times; the administrator unlocks it by setting a
  new password. Rate limiting by address belongs on the reverse proxy, where the real client address is known.
* **Printed sheets and the messages of the CSV import are Bosnian only.**
* **The People and Audit lists are not virtualised.** They are fine for hundreds of rows; thousands of employees would need paging.
* **Windows only** (the desktop app, its installer and the update flow).
