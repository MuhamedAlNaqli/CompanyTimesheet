# Company API: guide for the company's IT

This guide is for whoever runs the server of the employee timesheet API (`src/Company.Api`). It covers what is needed,
how to configure sign-in, and how to test it before any employee uses it. Status: the sign-in, employee administration,
holidays and audit are implemented; month entry, submission, approval, reminders and the printed sheet work.

## 1. What is needed

| Item | Notes |
| --- | --- |
| A Windows server in the company network | Domain-joined for Kerberos. A Windows PC is enough for a pilot. |
| .NET 8 runtime (ASP.NET Core) | Or publish the API self-contained. |
| PostgreSQL 14 or newer | One database, for example `CompanyTimesheet`, reachable only from the API. Daily backups with a tested restore. |
| An HTTPS certificate from the company's certificate authority | Kerberos tickets and timesheet data must not travel in clear text. |
| A service account | Runs the API as a Windows service and, for Kerberos, owns the service principal name (section 3). |

## 2. Settings (`appsettings.json` or environment variables)

| Setting | Meaning |
| --- | --- |
| `ConnectionStrings:Default` | PostgreSQL connection string |
| `Database:Provider` | `postgres` (default) or `sqlite` (development only) |
| `Authentication:Mode` | `Password` (user name and password created by the administrator, the default), `Windows`, `Entra` or `Both` (these three need `Features:WindowsLogin` = true), or `Test` (development only, refused elsewhere) |
| `Authentication:Entra:TenantId`, `ClientId`, `Audience` | Needed for `Entra` and `Both`; the server refuses to start without them |
| `Bootstrap:AdminUsername`, `Bootstrap:AdminPassword` | With the password sign-in: the user name and first password of the first administrator (at least 10 characters). It must be changed at the first sign-in. Used once, while no employee exists. |
| `Bootstrap:AdminAccounts` | Sign-in names of the first administrators, for example `["CORP\\jane.admin"]`. Used once, while no employee exists. |
| `Bootstrap:AdminCode` | Employee code given to that first administrator (default `Z-000`) |
| `Features:WindowsLogin` | `false` by default. Needed for Authentication:Mode Windows, Entra or Both. The desktop app has the same setting `Company:WindowsLogin`. |
| `Features:EmailReminders` | `false` by default. On: one reminder mail a day to employees who have an address. Needs the `Smtp:*` settings; `Reminders:EmailHour` (default 8) is the first hour of the day for mails. |
| `Smtp:Host`, `Smtp:Port`, `Smtp:UseSsl`, `Smtp:User`, `Smtp:Password`, `Smtp:From`, `Smtp:FromName` | The mail server for the reminder mails |
| `Features:SheetExport` | `false` by default. Off: the timesheet is not offered as an Excel or PDF file (own sheet, approver's copy, team sheet, summary file); the summary for the approver is read and copied as text. |
| `Features:FinanceAndLegal` | `false` by default. Off: the legal department does not end the approval chain (it runs to the top of the chart), the legal and finance roles are ignored and `/api/export` is not served. Switch on when finance and legal start to use the app. |
| `Reminders:StartFrom` | The first month that is reminded about, as `yyyy-MM` (for example the go-live month). Earlier months are ignored. Without it, last month counts as overdue on the first day. |
| `Kestrel:Endpoints:Https:Url` / certificate | The HTTPS address and certificate of the server |

Environment variables use double underscores, for example `Authentication__Mode=Windows`.

## 3. Windows (Kerberos) sign-in (switched off by default: needs `Features:WindowsLogin`)

1. Run the API as a Windows service under the service account (`UseWindowsService` is built in).
2. Register the service principal name so that clients get Kerberos instead of the older NTLM fallback:
   `setspn -S HTTP/timesheet.company.local CORP\svc-timesheet` (use the real host name and account).
3. Clients call the API by that host name over HTTPS. The desktop app sends the user's Windows identity automatically.
4. Users in the domain need no password prompt. A caller without a valid ticket receives `401` with `WWW-Authenticate: Negotiate`.

## 4. Testing the sign-in before go-live

From a domain PC with `curl` (Windows 10 or newer has it), signed in as a normal user:

```powershell
curl.exe --negotiate -u : https://timesheet.company.local/api/auth/diagnostics
```

The answer shows what the server sees: `identityName` (for example `CORP\amra.adzamija`), `authenticationType`
(`Kerberos` is what we want; `NTLM` works but means the service principal name is missing), the names the account is
known by (`candidates`) and the `employeeCode` it is linked to (empty until the administrator links it). Please send us
this output for two or three test users.

To see which Active Directory attributes the company fills in, run this for one or two users (no passwords involved):

```powershell
Get-ADUser -Identity <signinname> -Properties displayName, mail, manager, employeeID, employeeNumber, department, title |
  Select-Object SamAccountName, UserPrincipalName, DisplayName, Mail, Manager, EmployeeID, EmployeeNumber, Department, Title
```

## 4b. Sign-in with a user name and password (the default)

The administrator creates a user name and a first password for each employee (in the app: Administracija, "Uredi", or through
`PUT /api/admin/employees/{code}/credentials`). The person chooses their own password at the first sign-in. Passwords are kept as salted
hashes and cannot be read back; five wrong passwords lock the account for 15 minutes. A session lasts 12 hours (30 days with "remember
me" in the app). Put the server behind HTTPS: passwords and session tokens travel in every request. The first administrator comes from
`Bootstrap:AdminUsername` and `Bootstrap:AdminPassword`.

## 5. Microsoft Entra ID (optional, "just in case")

Only if the company wants it, now or later:

1. In the Entra admin centre, register an application for the API. Note the tenant id and the application (client) id.
2. Expose the API (an application id URI), and register the desktop app as a client that may request it.
3. Set `Authentication:Mode` to `Entra` (Entra only) or `Both` (Windows and Entra) and fill `Authentication:Entra`.
4. The desktop app then signs in with the Windows account broker, which also honours Conditional Access (for example MFA).

With `Both`, a request with a bearer token is validated as Entra and every other request goes through Windows.

The database is created and updated by the API itself: at start-up it applies the migrations that are missing, so the
service account needs the right to create tables in that database (and nothing else). Back up before installing a new version.

### Database accounts and the audit log

The log is append-only inside the program, but a database account with wider rights could change it. Use two accounts:

- a **migration account** (owner of the tables) that runs `dotnet Company.Api.dll --migrate` once per installed version;
- the **service account** of the API, which has `SELECT, INSERT, UPDATE, DELETE` on the tables and only `SELECT, INSERT` on
  `audit_events`, and `Database:MigrateOnStart` set to `false`.

If one account is used (a pilot), leave `Database:MigrateOnStart` at its default `true`; the API then updates the database itself.

The audit log is also a chain of hashes: every event holds the hash of the one before it. Once a month (or after any doubt) the
administrator calls `GET /api/admin/audit/verify`. `isIntact` must be `true`; if not, `firstBrokenId` is the first event that was
changed, removed or inserted behind the program's back. Keep the answer with the monthly records.

### What the server asks of every request

- Requests that change data must carry the header `X-Requested-With: CompanyTimesheet` (the desktop app does). A tool such as
  `curl` has to add it. Reading needs nothing.
- A request that names another origin (a web page of another site) is refused.
- A request is at most 2 MB; the longest text fields are limited (description 500, messages 2000, names 150).
- Behind a reverse proxy, set `AllowedHosts` in `appsettings.json` to the real host name instead of `*`.
- In Entra mode the administrator links the account written as `entra:<tenant id>/<object id>` (shown by `/api/auth/diagnostics`)
  or the user principal name, not the e-mail address.
- A timesheet waiting for somebody who left: `POST /api/admin/sheets/{code}/{year}/{month}/reassign` with
  `{ "toCode": "Z-017", "reason": "..." }`. A person who still has people or waiting timesheets cannot be deactivated.

## 6. First start

1. Start the API with `Bootstrap:AdminAccounts` set. The first administrator is created and is recorded in the audit log.
2. The administrator signs in (the desktop app or `curl` as above) and imports the employee list (a preview shows
   errors and warnings first), then links accounts: by an attribute if the company has one, by a name-matching
   preview, or from a small CSV.
3. The administrator sets "reports to" for everyone, flags the lawyer as final approver, flags the people of finance (they receive the approved timesheets of everybody as an Excel or PDF sheet), and gives the person at the top of
   the chart a designated approver. The server refuses a chart where someone would be their own supervisor.
4. The administrator enters the state holidays of the year; religious holidays are marked per employee.

## 7. What the server records

Every change to master data (employees, accounts, chart, holidays) is written to an append-only audit log in the same
save: who, when, what. The application cannot change or delete audit rows. The log is available to administrators at
`/api/admin/audit`.

## 7b. The desktop app

On each employee PC the app needs one setting, in `appsettings.local.json` next to the program (or the environment
variable `Company__ApiUrl`):

```json
{ "Company": { "ApiUrl": "https://timesheet.company.local" } }
```

The employee signs in with the user name and password the administrator created (with "remember me" the next start goes straight to the window). The app keeps its files in `%LOCALAPPDATA%\CompanyTimesheet`. It also registers a Windows scheduled task for the signed-in user (no administrator rights, no password) that asks the server every 30 minutes what is due and shows a notification; it uses the remembered sign-in, so without "remember me" it stays silent. Plain `http://`
is refused except for the local computer.

A supervisor (anybody with people reporting to them) has a third tab, "Moj tim": everybody below them as a tree with the status of the
month, a "Podsjeti" button for each person who has not submitted (and one for all of them) and the printouts. A reminder is written to
the table `team_nudges` and reaches the person on their next check (the scheduled task runs every 30 minutes, on working days from 07:00
to 20:00; the open app shows it at once). The same person is reminded at most once in four hours, whoever asks, and a reminder goes away
when the month is submitted. The summary for approval (`/api/team/{year}/{month}/summary/{xlsx|pdf}`) lists, for everybody below, what
there is to report (overtime, absences and business trips as periods) and the register of overtime.

## 8. Not yet implemented

Everything of the timesheet works (entries, validation, submission, approval, reminders, printed sheet, database
migrations). Not built: deputies for approvers, an administrator screen in the app (use the API), a screen for stored
revisions, email reminders; see `docs/EMPLOYEE_TIMESHEET_FEASIBILITY.md` section 17.
