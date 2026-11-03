# Employee timesheets (šihtarica): feasibility and proposed design

Status: **assessment for review; steps 1 to 6 (the domain library, the API skeleton, the employee mode with timesheet entry, submission with the approval workflow, the reminders, and the printed sheet) are implemented in code**, see sections 12 to 17. What remains of step 6 is the pilot and the go-live, which need the company (section 17).

Source: the analysis in `E:\Internal Projects\Timesheet` (`docs/timesheet-analysis`, 37 questions and answers,
the sample sheet, the filling instruction, the employee list). The architecture file (S5) there is empty, and the
original Excel workbook was not supplied (the stakeholders say it has no formulas, only display).

Decisions taken so far:

- The **consultant side stays a standalone app** with everything it has today and its own local database, so a
  consultant's data is theirs alone. It never talks to the company API.
- The **corporate data is stored in one place for all employees**, behind a **separate API** that holds the business
  logic. The desktop app is the front end for it, with an **Employee mode** that is separate from the consultant mode.
- Users sign in with **Windows authentication against the on-premises Active Directory**. **Microsoft Entra ID** is
  added as a second, switchable option in case it is needed.
- The system runs **inside the company network**.
- The desktop app must **remind** users to fill in and submit their timesheets.

## 1. Verdict

**It can be built in this solution, and the chosen architecture is the right one. It is also the simpler of the options
discussed: the consultant app does not change.** A desktop front end is the better
fit than a web application for one requirement in particular: reminders. A browser page cannot nudge anyone while it is
closed, but the desktop app already has the machinery (a scheduled Windows task that runs even when the app is closed,
and tray notifications). The API keeps rules, workflow, audit and authorisation in one place, which a desktop client
that talks to the database directly could never guarantee for 112 employees.

## 2. Architecture

```
  Desktop app (Avalonia)                                  company network (HTTPS, internal certificate)
   ├ Consultant mode ── local database (PostgreSQL on the user's PC), exactly as today, no network
   └ Employee mode  ────────────────────────────────────────────────► API (ASP.NET Core, Windows server)
        Windows sign-in (Kerberos) or Entra token                        Timesheet logic ─► corporate database
  Scheduled reminder task (runs as the user, no password)                Identity: AD / Entra, roles, audit
```

| Part | Content |
| --- | --- |
| **API** | One ASP.NET Core application for the corporate data: employees, calendar and holidays, time codes, month entries, overtime split and classification, validation, workflow, revisions, audit, reminders, identity. |
| **Domain library** | Pure code without I/O, shared by the API and the tests: calendar, overtime windows, monthly fund, validation rules, workflow state machine. Same style as `MissingDays` and `ReminderEvaluator` today. |
| **Desktop app** | Avalonia, as today. At the sign-in screen the user picks the mode. **Consultant mode** is the current app, unchanged. **Employee mode** signs in with the Windows account and works only against the API. The two modes share the design system and the update mechanism, never data or sign-in. |
| **Databases** | The consultant database stays on the consultant's PC. The corporate database is one central PostgreSQL database that only the API reaches. |

Because the consultant mode needs no server, a consultant who is not an employee (or who works outside the company
network) can use the app exactly as before. The employee mode needs the company network.

Splitting the two modes into two executables later is possible (they only share libraries), but one app with two modes
means one installer, one updater and one place for the design system, so I would start with one.

## 3. Windows authentication

**On-premises Active Directory (primary):**

1. The API runs on a domain-joined **Windows server** and uses `Negotiate` authentication (Kerberos, NTLM as
   fallback). The API can run on Kestrel or behind IIS.
2. The desktop app calls the API with the current Windows identity (`UseDefaultCredentials`). There is no password
   prompt, no stored secret, and no "remember me" for employees.
3. The API reads the account (for example `DOMAIN\amra.adzamija`) and maps it to an **employee** (code `Z-NNN`).
   An account that maps to nobody gets a clear "not registered" answer.
4. Roles (service head, legal reviewer, administrator) and the responsible-person relationships come from the
   application's own tables. AD groups can additionally mark who is an administrator or the lawyer.
5. The reminder task runs as the signed-in user, so it can ask the API silently as well.

**Microsoft Entra ID (second option, same code base):**

- The API accepts a bearer token from Entra (`Microsoft.Identity.Web`). The desktop app gets it with MSAL and the
  Windows account broker, which also signs in silently on a joined PC and honours Conditional Access (MFA).
- A configuration setting chooses `Windows`, `Entra` or `Both`. With `Both`, a request with a bearer token is
  validated as Entra, and any other request goes through Negotiate. Everything after authentication (mapping to an
  employee, roles) is identical.
- Entra needs an app registration (client id, tenant id) from the company's administrator. It stays off until needed.

**Safeguards that come with it:** HTTPS with the company's internal certificate (Kerberos tickets and data must not
travel in clear), a switchable **test sign-in** for development only (never enabled in production), and auditing of
who signed in and what they did.

**Testing:** this development PC is in a workgroup, so real Windows authentication cannot be tested here. A test
domain, a company test server or a small lab VM with a domain controller is needed early. The Entra path can be tried
with a work account on this PC after an app registration exists.

### 3b. Linking accounts to employees and the test environment

**Which AD attribute?** Nobody knows yet, so the design does not depend on one. Active Directory normally has these
fields on every user, any of which can be used:

| Attribute | Use |
| --- | --- |
| `sAMAccountName`, `userPrincipalName`, `mail` | The sign-in name. Always present. |
| `displayName` | Usually "first last", comparable to the employee list's name |
| `manager` | The user's supervisor, a ready-made source for the approval chain **if HR keeps it filled** |
| `employeeID`, `employeeNumber` | Could hold the code `Z-NNN`, if the company fills it |
| `department`, `title` | Optional context |

How the link is made, in order of preference:

1. If `employeeID` or `employeeNumber` holds the code, map automatically.
2. Otherwise **match by name** (ignoring case and diacritics) with a preview, and let the administrator fix the
   leftovers on screen. 112 people take minutes, not days.
3. Always possible: the administrator imports a small CSV (code, sign-in name).

To find out what the company has, someone with domain access can run this on any domain PC with the AD tools, for one
or two sample users, and send the output (no passwords are involved):

```powershell
Get-ADUser -Identity <signinname> -Properties displayName, mail, manager, employeeID, employeeNumber, department, title |
  Select-Object SamAccountName, UserPrincipalName, DisplayName, Mail, Manager, EmployeeID, EmployeeNumber, Department, Title
```

**Server and test environment:** the API needs a domain-joined Windows server (any supported Windows Server, or a
Windows PC for a pilot) with HTTPS from the company's internal certificate authority. For testing, either the company
provides a test server and two test accounts, or a lab is built on a PC with Hyper-V: a Windows Server evaluation VM
as domain controller plus a joined client VM. I can write the lab setup script if the company cannot provide one.

## 4. Reminders

| Reminder | Who | When | How |
| --- | --- | --- | --- |
| Fill in and submit the timesheet | Employee | Before the 25th (moved to the first working day when the 25th is a weekend), and when it is overdue | Tray notification from the scheduled task, plus a banner in the app |
| Returned with a message | Employee | When the service head or lawyer returns it | Notification and an item in the work queue |
| Items waiting for approval | Service head, lawyer | When something is submitted, and daily while overdue; all approvals before the next month starts | Notification and work queue |
| Missing hours (existing consultant reminder) | Consultant mode | As today | Unchanged, answered by the local database |

How it works: the **API decides** what is due (one rule set for the banner, the task and any email), and the desktop
task only asks "what is due for me?" and shows it. The existing `ReminderRunner`, `TrayNotifier` and the scheduled
task are reused; in Employee mode the source of truth is the API, in Consultant mode it stays the local database. If the API
cannot be reached (laptop outside the network), the task shows nothing rather than a wrong reminder, and it keeps the
last known deadline so a due date is still honoured. Email reminders from the API are an optional addition for people
who rarely open the app.

## 5. Separate employee and consultant views

| Level | Consultant mode | Employee mode |
| --- | --- | --- |
| Screens | Daily tasks, calendar, reports, clients, documents (as today) | My month, absences, overtime, my submissions; approvals for service heads; legal review for the lawyer |
| Sign-in | Local account, Google, Microsoft (as today) | Windows account only |
| Data | Local database, private to the consultant | Corporate database, behind the API |
| Network | None needed | Company network |

The mode is chosen on the sign-in screen and remembered. Nothing the consultant stores is ever sent to the API.

## 6. Approval chain (decided)

Every person has **one supervisor** ("reports to"). A timesheet climbs that chain: the employee's team lead approves
first, then the team lead's supervisor, and so on. The length of the chain is whatever the organisation chart is; the
application does not fix it at two or three levels. Example from the stakeholders: employee, team lead, lawyer,
finance.

Rules (the defaults below fill what was not said; each can be changed without redesign):

| Rule | Default |
| --- | --- |
| Data | `Employee.ReportsTo` (the supervisor, maintained by the administrator, or read from the AD `manager` attribute if IT keeps it filled). Cycles are rejected. |
| Steps for one timesheet | The supervisors above the employee, in order, up to and including the first one marked **final approver** (the lawyer). If nobody is marked, up to the top of the chain. |
| Whose own timesheet needs whom | Everyone's goes up *their own* chain. The lawyer's chain is finance, so the lawyer's timesheet is approved by finance. |
| A person at the top with no supervisor | Needs a **designated approver** set by the administrator. Nobody approves their own timesheet. |
| Return | Any step can return it, to the **employee**, with a message. After the correction it **starts again at the first step** (confirmed earlier in the analysis). |
| Frozen chain | The chain in force when the timesheet is submitted is stored with that submission, so a later change in the organisation chart does not alter a running approval. |
| Absence of a supervisor | A **deputy** can be named per supervisor for a period; the audit log shows who acted and for whom. |
| Visibility | A supervisor sees the timesheets of the people below them in the chain and nobody else's. Health-related codes are visible to the chain, the lawyer and finance (Q-25). |
| Finance | Not an approval step for employees' timesheets (it receives approved data later, Phase 2); it only approves what sits in its own chain, such as the lawyer's timesheet. |

This replaces the fixed "employee, service head, lawyer" path of the analysis (`HEAD` and `LEG` become positions in the
chain, with the lawyer flagged as final approver) and is the "chained flow" the stakeholders described in Q-11.

## 7. What carries over and what is new

| Area | Needed by the analysis | Reuse from today |
| --- | --- | --- |
| Rules: overtime split at 06:00, 08:30, 17:00, 22:00 and midnight, classification into codes 2020 to 2025, monthly fund, absence ranges that skip weekends | CALC-01 to 07, BR-17 to 23 | Pure code with tests, new, small. The sample sheet and the instruction's examples are the fixtures. |
| Validation: 21 rules with ids and severities | V-01 to V-21 | New, pure code |
| Status machine, revisions with hash, audit | T1 to T12 | New |
| Month grid, absence by range, overtime register, review screens | Requirements section 5 | New screens in the existing design system; the week view patterns help |
| Reminders | By the 25th, approvals before next month | Reuse the task and tray notifier (section 4) |
| Printed sheet in the Excel layout | Q-33: required, landscape A4, colour rows, 9 to 10 rows per employee | QuestPDF and ClosedXML are in the solution; medium effort |
| Bosnian interface | Q-32: Bosnian first | Existing catalogue and language switch |

## 8. Proposed solution layout

```
src/DailyTaskManager.Core            existing
src/DailyTaskManager.Infrastructure  existing (consultant data access, exports)
src/DailyTaskManager.Avalonia        existing desktop app; gains Employee mode and an API client
src/Timesheet.Domain                 new: calendar, overtime, fund, validation, workflow (no I/O)
src/Company.Api                      new: ASP.NET Core API, identity (AD / Entra), timesheet logic and data
src/Company.Api.Contracts            new: the request and response types shared by the API and the desktop app
tests/Timesheet.Domain.Tests         new: fixtures from the sample sheet and the instruction
tests/Company.Api.Tests              new: authorisation and workflow tests
```

## 9. Gaps and contradictions found in the analysis

1. **Approval chain.** Settled in section 6: a chain of any length through each person's supervisor. Still to
   confirm: the default for the person at the top, and that finance approves only its own chain. Q-10 says "the
   administrator approves": with the chain, that is simply the next supervisor.
2. **Extra master data.** Q-13 says only code and name are kept, but the workflow needs the supervisor and
   Windows sign-in needs the account. See section 3b for how to link them without typing 112 mappings.
3. **Holidays per employee.** Q-14: state and religious holidays are marked per employee and nobody maintains
   them today. The analysis models one global calendar. This needs a yearly holiday list with an administrator and a
   per-employee assignment for religious holidays. Q-03: holiday codes apply even when the holiday is on a weekend.
4. **Overtime on absence days (Q-35).** Allowed on annual leave, not on sick leave, not on business trips.
5. **Hours in "units of an hour" (Q-17)** while the sample has 2.5 h. Store minutes; make the granularity a setting.
6. **Shift workers are excluded for now (Q-34).** Codes 2011 and 2012 can stay hidden. Q-16: employees add rows only
   for codes they use, so the grid shows the used rows and an "add row" action.
7. **The printed sheet (Q-33) is required** but the analysis schedules it late (M6). It belongs in the first release.
8. **Deadline:** the 25th, "moved to the first working day" on a weekend (Q-21); the direction is ambiguous.
9. **Employee list:** people are missing and will be added later, so add-employee must exist from the start. One
   entry has stray spaces in the code and the sample sheet has `Z- 255`.
10. **The legal reviewer checks nothing beyond the instruction (Q-06)**, so no legal-limit rules are needed.
11. **Finance is out of the first release (Q-04, Q-05 open).** Approved data only needs a read-only view or simple export.
12. **The sample sheet carries the client company's name and logo**, so this product serves the client's company. Their IT
    hosts the API, owns the employee data and decides how consultants' data is treated (section 2).

## 10. Suggested order of work

| Step | Content | Depends on |
| --- | --- | --- |
| 1 (done) | `Timesheet.Domain` with tests: calendar, overtime split and classification, fund, absence ranges, validation rules. | Nothing external |
| 2 (done) | `Company.Api` skeleton: Windows sign-in with a test mode, the Entra option behind a setting, user profiles and roles, employee import, time-code catalogue, holiday administration, audit base. | Test domain (section 3) |
| 3 (done) | Employee mode in the desktop app (mode choice at sign-in, Windows sign-in): month list, absence by range, overtime register, validation report. | 1, 2 |
| 4 | Workflow: submit, return with message, approval up the chain (section 6), locking, frozen revisions, history. | Section 6 defaults |
| 5 | Reminders from the API: rules, the scheduled task asking the API, notifications. | 2, 4 |
| 6 | Printed sheet in the Excel layout, pilot with a parallel run, go-live. | 1 to 5 |

Phase 2 as in the analysis: finance hand-over, reports, legacy import.

## 11. Decisions needed

1. Confirm the defaults of section 6: the person at the top gets a designated approver; finance approves only its own
   chain; deputies are wanted.
2. A Windows server in the company network for the API, and a test domain or test server (section 3b).
3. Go-ahead to start with step 1 (domain library with tests), which needs none of the answers above.

## 12. Step 1 result: `Timesheet.Domain`

Implemented in `src/Timesheet.Domain` (pure .NET 8 library, no dependencies, no I/O) with 96 tests in
`tests/Timesheet.Domain.Tests`. The tests run in the existing "Build and Test" workflow.

| Part | What it does |
| --- | --- |
| `TimeCodes` | The catalogue of the legend with unit, colour and fund membership |
| `WorkCalendar`, `WorkingTimeSettings` | Generated days, state holidays, per-employee religious holidays, Bosnian weekday letters, next working day; the working-time rules as data |
| `OvertimeClassifier` | Splits an interval at 06:00, 08:30, 17:00, 22:00 and midnight and gives each part its code (2020 to 2025) by the type of its date |
| `AbsenceRanges` | Expands "code, from, to" into days and reports the days it leaves out |
| `MonthlySummaryCalculator` | Hours and days per code, the monthly fund, overtime total, expected fund |
| `TimesheetValidator` | The checks V-01 to V-21 with configurable severities and Bosnian messages |
| `ApprovalChainBuilder`, `TimesheetWorkflow` | The chain of any length, and submit, approve, return, answer with the rules of section 6; every transition returns an audit event |
| `Deadlines`, `ReminderRules` | The 25th (next working day if not working), approval before the next month, who is due a reminder |
| `EmployeeListImporter`, `EmployeeCode` | The employee list preview: bad codes, duplicates, trimmed names, existing codes with another name |

**Acceptance tests that pass:** the six overtime records of the sample sheet give exactly the allocations of the
sample; the examples of the instruction (Friday 17:00 to 21:00; Saturday 21:00 to 02:00 with the Sunday part; a
partly worked holiday) are reproduced; the eleven sample employees give the derived summary (1,480 regular hours,
35 annual leave days, 8.0 h of 2020, 1.5 h of 2021, 5.0 h of 2023) and a monthly fund of 176 h each; entering
annual leave 01.09 to 15.09 gives exactly 11 days; the real employee list imports as 112 rows, 1 trimmed name and
0 errors (checked with a local path, the list is not in the repository).

**Findings while building it:**

1. The legend has **22 codes**, not 20 as the analysis says.
2. **A weekend has no regular window.** Weekend overtime from 06:00 to 22:00 is one code (2023), also between 08:30
   and 17:00. The sample proves it (Saturday 08:30 to 11:00 is 2023 = 2.5). Overtime inside 08:30 to 17:00 is only
   an error on a working day or a holiday.
3. Rules changed by the stakeholder answers: V-17 is blocking (Q-08); V-21 warns only when the fund is **lower** than
   expected (Q-36); overtime is allowed on annual leave, **blocked on sick leave (V-19b)** and a warning on the other
   absences (V-19c, undecided); V-15 means "start equals end" because overtime past midnight is allowed (Q-22).
4. The sample sheet entered as it is would be rejected for employee Z-251, whose maternity leave sits on weekends.

**Assumptions to confirm** (each is one setting or one line of code):

- Hours are kept in minutes; the step is half an hour (V-12 warns). Q-17 says "in units of an hour", but the sample
  has 2.5 h.
- Range absences skip the employee's religious holidays (they have code 1025); state holidays use 1024 and 2010.
- When the 25th is not a working day the deadline moves **later**; reminders start three days before the deadline.
- A holiday on a weekend uses holiday codes, also for a religious holiday (Q-03).

## 13. Step 2 result: `Company.Api`

Implemented in `src/Company.Api` (ASP.NET Core, .NET 8) and `src/Company.Api.Contracts` (the request and response
types for the desktop app), with 25 tests in `tests/Company.Api.Tests`. The guide for the company's IT is
[API_IT_GUIDE.md](API_IT_GUIDE.md).

| Part | What it does |
| --- | --- |
| Sign-in modes | `Windows` (Negotiate: Kerberos, NTLM fallback), `Entra` (bearer token), `Both` (a bearer token goes to Entra, anything else to Windows) and `Test` (development only; the server refuses it elsewhere). A mode that cannot work, such as Entra without a tenant, stops the server at start-up with a clear message. |
| Identity mapping | The caller's names (Windows name, user principal name, Entra name, e-mail) are compared, ignoring case, with the accounts the administrator linked to employees. Roles follow from the data: administrator, approver (has people reporting to them), final approver (the lawyer). |
| Not registered | A signed-in account that is linked to nobody gets `403` with the code `not-registered`, which the desktop app can show as it is. |
| Employees | Import with a preview (the importer of the domain library), edit, supervisor, final-approver flag, designated approver, active dates. A change that would create a cycle or name an unknown person is refused and saves nothing. |
| Accounts | Link and unlink; an account belongs to one employee. A name-matching preview proposes links between directory users and employees, ignoring accents and the order of names. |
| Calendar | State holidays maintained by the administrator, religious holidays per employee, the days of a month for the caller. The 22 time codes come from the domain library. |
| Audit | Append-only: every administrative change is recorded in the same save, and the data layer refuses to change or delete an audit row. |
| First start | `Bootstrap:AdminAccounts` creates the first administrator while no employee exists. |
| Diagnostics | `/api/auth/diagnostics` shows what the server sees of the caller, for testing Windows or Entra sign-in. |

**Verified with a real handshake:** the API was started on Kestrel in `Windows` mode on this PC and called with
`curl --negotiate`. An anonymous call got `401` with `WWW-Authenticate: Negotiate`; with the Windows identity the
server saw `<computer>\<user>` (NTLM, as expected on a workgroup PC) and mapped it to the bootstrap administrator.
Kerberos on the company domain uses the same code; only the service principal name and the domain are needed. The
in-process test server cannot do that handshake, so the automated tests use the `Test` mode and check that the
`Windows`, `Entra` and `Both` modes start with the right schemes.

**Still open:** database migrations (the tables are created automatically for now), a test domain for Kerberos
and Entra, and everything from steps 3 to 6.

## 14. Step 3 result: employee mode and timesheet entry

**API** (`Company.Api`, 44 more tests than step 2): each employee's own month, created as a draft on first use.

| Endpoint (under `/api/timesheet/{year}/{month}`) | What it does |
| --- | --- |
| `GET` | The month: days with type and holiday, entries, overtime with its split into codes, totals, the expected monthly fund, and the findings of all rules |
| `PUT days/{date}`, `DELETE days/{date}/{code}` | Set or clear a day's entry. A day has one base entry (regular hours or an absence); a whole-day code carries no minutes. Overtime codes are refused: overtime is an interval. |
| `POST fill-standard-days` | 8 regular hours on every working day without an entry; holidays, the employee's religious holidays and days before employment are skipped |
| `POST absences/preview`, `POST absences` | The days a range covers and the days it leaves out, then the change |
| `POST/PUT/DELETE overtime` | Date, start, end, description; the total and the split are computed; past midnight is allowed |

Changes are accepted and the findings come back with the month ("prevent by showing", blocking findings stop the
submission later). A submitted or approved timesheet answers `409 locked`; a returned one is editable again. Every
change is audited with the employee as actor. The overtime part after midnight on the last day of a month belongs to
the next month and is left out of this month's totals. The typed client `Company.Api.Client` carries all of this to
the desktop app, with the server's error codes as `ApiException`.

**Desktop app** (`Employee/`): the sign-in screen has "Employee sign-in (company account)". It opens **employee mode**: a
Bosnian-only window with the month list (weekend and holiday stripes, entries, overtime, a count of findings per day),
the totals and expected fund, the overtime records and the findings of the rules with their ids. Dialogs: a day's entry,
an absence over several days (shows the days before saving) and overtime (shows the total and the split into codes
while typing, using the domain library). The mode is remembered in `%LOCALAPPDATA%\DailyTaskManager\mode.txt`; in
employee mode the app starts no local database and no consultant reminder, and "Consultant mode" switches back. The
server address is `Company:ApiUrl` in `appsettings.local.json`; plain `http` is only accepted for this computer.
A server that cannot be reached, an account that is not linked, or a refused sign-in is explained in the window.

**Verified in the real app:** the API running on Kestrel with Windows sign-in, the desktop app in employee mode signed
in with this PC's Windows account (NTLM): month load, "fill standard days" (the test holiday on a weekday was skipped
and the missing-fund warning appeared), an absence with preview, an overtime record on a Saturday split into 2023 and
2024, and the Bosnian date picker. 11 logic tests for the formats and dialogs run with the UI test project.

**Not in this step:** submitting, the approval workflow, other people's timesheets, reminders (steps 4 and 5), an
administrator screen (the administrator uses the API), and the printed sheet (step 6).

## 15. Step 4 result: submission and the approval workflow

**API.** The status machine is the domain library's `TimesheetWorkflow`; `WorkflowService` loads the state, applies the
transition and saves the new state, the history entry, the audit row and (on submission) a copy of the sheet in one step.

| Endpoint | Who | What it does |
| --- | --- | --- |
| `POST /api/timesheet/{y}/{m}/submit` | the owner | Freezes the month and hands it to the first approver. Refused (409) while blocking findings remain (`blocking-issues`), while a returned message is unanswered (`unanswered-messages`), or when the chart gives no approver (`no-chain`). The chain in force is stored with the revision. |
| `POST /api/timesheet/{y}/{m}/messages/{i}/answer` | the owner | Answers one return message; only while the sheet is returned |
| `GET /api/timesheet/{y}/{m}/revisions/{n}` | the owner | The sheet exactly as it was submitted in revision n |
| `GET /api/approvals` | an approver | The submitted timesheets waiting for the caller right now (oldest first, with counts of findings) |
| `GET /api/approvals/{code}/{y}/{m}` | owner or anyone in the sheet's chain | Someone else's sheet, read only (`editable` is always false for them); `workflow.canAct` says whether it is the caller's turn |
| `POST /api/approvals/{code}/{y}/{m}/approve` | the current approver | Moves to the next step, or sets the sheet to Approved after the last one |
| `POST /api/approvals/{code}/{y}/{m}/return` | the current approver | Returns it to the employee; a message is mandatory |
| `GET /api/approvals/{code}/{y}/{m}/revisions/{n}` | owner or chain | A submitted revision, for comparing a correction with what was approved earlier |

Rules, all enforced on the server:

- Only the person whose turn it is can approve or return (`not-your-turn` otherwise); nobody approves their own sheet
  (`self-approval`); anyone outside the chain gets `forbidden` and cannot even read the sheet.
- A return always goes to the employee; the resubmission is a new revision and starts again at the first step.
- A submitted or approved sheet cannot be edited (`locked`); a returned one can.
- Each sheet row has a version number that changes with every save, so two people acting at the same moment cannot both
  win (`changed`, "open it again").
- Submissions, step approvals, returns and answers are in the sheet's history (visible to the employee and the chain) and in
  the append-only audit log.
- Deputies (`isDeputyFor`) are supported by the domain library but there is no table or screen for them yet.

**Desktop app.** Two tabs in employee mode. "Moja šihtarica" gets the button "Predaj na odobravanje" (enabled only when
nothing blocks the submission), a status text with whose turn it is, the chain, the approvers' messages with an
"Odgovori" button, and the history. "Za odobravanje (n)" lists the timesheets waiting for the signed-in person (the count is
on the tab); "Otvori" shows one read only with "Odobri" and "Vrati na ispravku…" (a message is required). The editing
buttons are hidden while a sheet cannot be edited.

**Verified in the real app** (API on Kestrel with Windows sign-in, a second instance with the test sign-in to act as the
other employees, one SQLite file for both): as the approver, the tab showed "(1)", the sheet opened read only, an empty
return message was refused, the return with a message reached the employee, the answer and the resubmission (revision 2)
went back to the same approver, whose approval moved it to the second step; as the employee, the submitted sheet was locked,
a returned sheet showed the message with "Odgovori", the submit button stayed disabled until the answer was written, and the
submission handed the sheet to the first approver. The test servers and data were removed afterwards.

**Not in this step:** reminders (step 5), the printed sheet and finance's view of approved sheets (step 6), an administrator
screen, a screen for the stored revisions (the API is ready), notification of the next approver. The confirmation dialog still
shows the buttons in English ("Yes/No"); it is the shared dialog of the consultant app.

## 16. Step 5 result: reminders from the API

**API.** `GET /api/reminders` answers "what is due for me today?" for the signed-in person. The rules are the domain library's
`ReminderRules` and `Deadlines`; the server only gathers the facts, so the banner in the app and the scheduled task never
disagree. The answer says what is due (kind, month, count, deadline), not how to word it.

| Kind | Who and when |
| --- | --- |
| `SubmitSoon` | The month is still a draft and the deadline (the 25th, or the next working day when the 25th is a weekend or a holiday) is three days away or less |
| `SubmitOverdue` | The deadline has passed and the month is still a draft; applies to the current and the previous month |
| `FixReturned` | A sheet was returned and waits for the employee (any month) |
| `ApproveWaiting` | Sheets wait for this person as an approver; the count is in the answer |
| `ApproveOverdue` | The oldest waiting sheet belongs to a month that is over |

Months before `Reminders:StartFrom` (for example `2026-09`, the go-live month) are ignored, as are months outside a person's
employment and inactive employees. Without that setting, the first day after go-live would remind everybody about last month.

**Desktop app.**

- **In the window:** a yellow banner above the toolbar lists what is due (most urgent first); it refreshes whenever the month is loaded.
- **Scheduled task:** employee mode installs the same Windows task as the consultant reminder (every 30 minutes and at sign-in, runs as the user, no password, no resident process). The run asks the API and shows one notification; clicking it opens the app.
- **How often:** something new is announced at once (for example a timesheet that has just arrived for approval); what the user was already told about repeats at most twice a day, two hours apart.
- **Offline:** when the server cannot be reached (a laptop outside the network), the last answer is reused only for the submission deadline, which depends on the date alone, so a due date is still honoured. Nothing else is guessed.

**Verified:** 7 API tests with a fixed server clock (three days before the deadline and not earlier, overdue, a deadline that
moves from Sunday 25 October to Monday the 26th, approval overdue after month end, returned sheets, the start month,
employment dates) and 4 logic tests for the wording and the rate limit. In the real app, with the API on Windows sign-in:
the banner showed "Šihtarica kasni" for October with the deadline 26.10.2026., the task run (`--reminder-check`) wrote its
notification and its state, and with the server stopped the run still announced the overdue month from the saved deadline.
The reminder state file and the mode file were restored afterwards.

**Not in this step:** email reminders (optional, section 4), a refresh of the banner while the window stays open (it
refreshes on loading a month and on "Osvježi"), a screen for the administrator, and the printed sheet (step 6). The
notification text is Bosnian only.

## 17. Step 6 result: the printed sheet, and what the pilot needs

**The printed sheet** is the current Excel sheet reproduced from the data, as an Excel file (opens and prints in the old
layout) and as a PDF. It follows the sample of September 2026: landscape A4, the legend of the 22 codes in three colour
groups, a header with weekday initials and day numbers, then a block of nine rows per employee (1010, 1024, 2010, 2020 to
2025) with the code and the name merged over the block. Row colours come from the legend group (green, blue, orange, pink),
weekend and holiday columns are dark red in every row. As on the current sheet, a whole-day absence is written into the
regular-work row as its code (for example 1022), regular days as hours, overtime in its own row as decimal hours. Rows for
2011 and 2012 appear only for employees who have such hours. A second sheet (page) is the overtime register of the sample:
employee, date, start and end, total hours, short description. Four employees go on a page, so a block is never split.

| Who | Endpoint | What |
| --- | --- | --- |
| The owner | `GET /api/timesheet/{y}/{m}/print/{xlsx or pdf}` | Their own month in any status; the status is printed on it ("nije odobreno" until approved) |
| An approver of the chain | `GET /api/approvals/{code}/{y}/{m}/print/{xlsx or pdf}` | The sheet they decide on |
| Finance, administrator | `GET /api/export/{y}/{m}/{xlsx or pdf}` | Every **approved** timesheet of the month on one sheet; 404 until something is approved; each export is written to the audit log |

Finance is a new flag on the employee (`isFinance`, set by the administrator like the other roles). Finance sees only the
approved sheets and approves nothing, which keeps the decision that finance receives approved data only.

**Desktop app.** "Ispis Excel" and "Ispis PDF" on the own sheet and on a sheet opened for approval, and for finance
and the administrator "Odobrene (Excel)" and "Odobrene (PDF)" for the month on screen; each asks where to save the file.

**Verified:** 8 API tests (layout of the Excel file cell by cell: legend colours, the nine rows, absence code in the 1010
row, weekend column red, overtime hours on the Saturday, the register; the PDF; page breaks between whole blocks; access
rules; finance), and in the real app a save through the native dialog of both formats for the own sheet and the finance export,
with approved sheets of several employees, rendered and compared with the sample. Two things the check found: the PDF library's built-in font has
no č, so the PDF uses Arial (every Windows server has it; the file is about 1 MB because the font is embedded), and the
toolbar now wraps when finance has more buttons.

**Assumptions to confirm with the company:** state-holiday columns are dark red like weekends (the sample has no holiday
column); there are no totals, as on the current sheet; there is no logo (the sample carries the company logo, which is not
reproduced here); the nine-row block is kept, with the rows for 2011 and 2012 added only when used (the analysis says "9 to 10
rows").

### What the pilot and the go-live need (not code)

1. **A server:** a Windows machine in the company network, .NET 8 runtime, PostgreSQL, an HTTPS certificate, a service account and its service principal name (`docs/API_IT_GUIDE.md`). Without them only the test sign-in and a development database can be shown.
2. **Real sign-in check:** `curl --negotiate -u : https://host/api/auth/diagnostics` for two or three users, and the `Get-ADUser` output (to decide how accounts are linked to employees).
3. ~~Database migrations~~ **Done:** PostgreSQL is now created and updated by migrations (see below).
4. **First data:** the employee list (import with preview), the links of accounts, "reports to" for everyone, the lawyer as final approver, the finance flag, the state holidays of the year, `Reminders:StartFrom` set to the first month in the app.
5. **Parallel run:** one month on paper or in Excel and in the app side by side, compare the printed sheet of the app with the Excel sheet row by row, and let finance calculate from the exported sheet.
6. **Open questions of section 10** that change behaviour: the approval-chain defaults, deputies, half-hour granularity, the rule for overtime on other absence days.

### Database migrations

Production (PostgreSQL) is created and updated by EF Core migrations: the API applies pending ones at start-up, so installing
a new version updates the database. The first migration, `InitialCreate`, was applied to an empty PostgreSQL 17 database
and produced the 11 tables plus the history table and the first administrator; the test database was dropped afterwards.
Development and tests use a SQLite file, which is simply created.

When an entity changes, add a migration from the repository root (the tool needs no database):

```
dotnet ef migrations add WhatChanged --project src/Company.Api --output-dir Data/Migrations
```

A test (`MigrationTests`) fails when the model and the migrations differ, so a forgotten migration cannot reach the build
server. A PostgreSQL database that was created by the earlier start-up code (without migrations) must be dropped and created
again; nothing was in production yet.

### Small things closed in the same pass

- The confirmation dialog of employee mode now has the buttons "Da" and "Ne" (it was "Yes/No").
- The reminder banner refreshes after a submission or an answer, not only when a month is loaded.

### Still open (by choice, not forgotten)

- **Deputies:** the domain library supports them; there is no table, endpoint or screen yet. Needed when an approver is away.
- **Administrator screen:** the administrator works through the API (`docs/API_IT_GUIDE.md`); a screen in the app is a convenience for a later release.
- **Screen for stored revisions:** the API returns each submitted revision; the app does not show them yet.
- **Email reminders:** optional.
- **Real Kerberos and Entra sign-in:** only testable with the company's domain or tenant; NTLM Windows sign-in was verified.

### Trying it on one PC

`tools/lab/lab.ps1` starts the API with a temporary database, sample people and timesheets, and the desktop app in employee mode
signed in with your own Windows account: `-Role employee`, `-Role approver` or `-Role finance` chooses who you are, `-Stop`
removes everything. The script itself acts as the other people through a second copy of the API that accepts a header.

## 18. Changes after the first trial of the employee window

| Remark | What was done |
| --- | --- |
| No minimise, resize or close | The window was 860 points high and centred, so on a screen with a working area of 816 points (a laptop, or a scaled display) its title bar was above the top edge. It is now 760 high, and on opening it is fitted to the working area of the screen and centred in it. |
| Calendar instead of the list | A toggle (list or calendar) next to "Osvježi"; the choice is remembered. The calendar has the weeks Monday to Sunday, weekends and holidays tinted, the entries and overtime of each day, a badge for findings, today outlined; a click on a day opens it. |
| Finance part | Removed from the app. The API still has it (`/api/export`, the finance flag) but nothing shows it. |
| Supervisor's summary | "Ispis" has "Šihtarica tima" (Excel, PDF): the month of everybody who reports to the person (and of those for whom they are the designated approver), whatever the status; the status is printed under each name; an employee who has not opened the month appears as an empty draft. Available under "Za odobravanje" as well, for the month chosen there. API: `GET /api/team/{y}/{m}/print/{xlsx or pdf}`. |
| No supervisor | A person with nobody above them has a chain of themselves alone: they submit, and approve their own timesheet (button "Odobri" in their own screen). Everybody else still cannot approve their own. The "no supervisor assigned" error is gone. |
| Start screen | The sign-in screen is always shown, unless "remember me" was ticked. Employee sign-in is remembered only with that box ticked; a consultant sign-in always resets the mode. |
| Consultant button | Removed from the employee window. |
| Sign-out | "Odjava" in the employee window returns to the sign-in screen (and the next start shows it too). |
| Icons, fewer buttons, export dropdown | Every button has an icon. Toolbar: "Novi unos", "Popuni standardne dane", "Predaj na odobravanje" on the left; "Ispis" (a dropdown like the consultant's export), refresh and the two view buttons on the right. |
| One form for all codes | "Odsustvo" and "Prekovremeni rad" buttons are gone. "Novi unos" (and a click on a day, or the pencil of a row) opens one dialog; the code decides the form: hours for a day, or a range of days with a preview for an absence code, or date, start, end, description and the live split into codes for an overtime code. |

Checked in the real app: sign-in screen to the employee window (title bar and its buttons on a screen of 1536 x 816 points), the
calendar, the three forms, a person with nobody above them filling, submitting and approving their own month, the print menu with
the team entries, sign-out back to the sign-in screen. API: 3 new tests (own approval, a person with a supervisor still cannot,
the team sheet), domain: 1 new test and 2 changed rules.

## 19. Code review of the employee side and what was done about it

A reviewer who specialises in C#, .NET and architecture went through the whole employee side (domain library, API, client,
desktop employee mode, reminders, printed sheet, tests, the lab script) and a part of the earlier consultant work. Every finding
was checked against the code before it was acted on; one turned out to be the intended design.

| Id | Finding | What was done |
| --- | --- | --- |
| F-01 | Overtime across midnight on the last day of a month was counted in no month after the first | The part before midnight stays in the month where the work began; the part after midnight is added to the next month. The next month shows it ("iz prethodnog mjeseca"), the record in the first month says how much of it belongs to the next. Works across New Year, with the holiday type of the new year. |
| F-02 | A web page could submit or approve in the name of the employee (Windows sign-in is sent automatically by the browser) | Every request that changes data needs the header `X-Requested-With: CompanyTimesheet` (the app sends it, a page of another site cannot add it) and a request that names another origin is refused. |
| F-03 | In Entra mode the account was found by e-mail, `preferred_username` or display name, which a user can change | An Entra caller is known by the immutable object id (`entra:tenant/object`, shown by `/api/auth/diagnostics`) and the user principal name only. Names that lead to two different employees sign nobody in. |
| F-04 | Long texts passed on SQLite and failed with a server error on PostgreSQL; no limit on the size of a request | Lengths are checked (description 500, messages and answers 2000, names 150) with a clear answer; a request may be 2 MB. |
| F-05 | Reading all submitted sheets of the company for every "waiting for me" and every reminder poll, with four joined collections | One indexed column (`CurrentApproverCode`) answers "waiting for me"; the collections are read in separate queries; read-only paths do not track; the calendar of a year is read once per request; the lookup of who an account is kept for 30 seconds (emptied by every change of the administrator). |
| F-06 | Two requests at once gave a server error | One save routine turns a conflict into "open it again"; two first openings of the same new month both succeed; an unexpected error is logged and answered in the normal form. |
| F-07 | A sheet waiting for somebody who left could never move on | A person with people or sheets waiting cannot be deactivated; the administrator can hand a waiting step to somebody else (`POST /api/admin/sheets/{code}/{y}/{m}/reassign`, with a reason, in the history and the audit log). |
| F-08 | The audit log is append-only only in this program | `Database:MigrateOnStart=false` and `Company.Api.dll --migrate` let the service account have no right to change the schema; `docs/API_IT_GUIDE.md` says how to give it insert and select only on the audit table. A hash chain was not added. |
| F-09 | Opening a colleague's sheet left no trace | Viewing, printing and opening a revision of somebody else's sheet are audited. |
| F-10 | A former supervisor kept access for good | Access: the owner; the people the sheet was submitted to while it is in the chain; anybody who took part in it; the supervisors the owner has today (once it was submitted). A draft is never seen. |
| F-11 | A failed month change left the selected month different from the one on screen; "Odobreno." after a call that never happened | The month changes only when it loaded; messages appear only when the server did it; the standard-days question names the month. |
| F-12 | Everything ran on SQLite only | `PostgresTests` run the migrations and the workflow on a real PostgreSQL (set `COMPANY_API_TEST_POSTGRES`); moments leave the server as UTC. They include an upgrade of a database of the first release. |
| F-13 | A garbled answer could end the app | Every command shows a message instead; an unknown day type is read as a working day. |
| F-14 | The reminder task could skip its fallback, wrote its state non-atomically, and notified at night | Any failure uses the last known deadlines; files are replaced in one step; no notification before 07:00, after 20:00 or on Saturday and Sunday. |
| F-15 | Services depended on a type in an endpoint file and on each other | `SheetDtoBuilder` is the one place that turns a sheet into what is shown; `Reference` and `SheetException` have files of their own; the endpoints share one way to answer; time comes from `TimeProvider`. |
| F-16 | An answer could be written over | Refused (`already-answered`). |
| F-17 | Only the last two months were reminded about | Every month from the start month (or six back) to this one. |
| F-18 | Employment dates could not be cleared; the last administrator could be removed; entries before the employment were accepted | Clear flags; the last administrator is protected; nothing is entered outside the employment. |
| F-19 | "A top person and their designated approver can approve each other" | **Not a defect.** Somebody has to approve the top person, and that is normally someone who reports to them (the lawyer, finance); what is forbidden is approving one's own month. The rule is now stated in the code and in a test. |
| F-20 | A Test server and a plain-HTTP server on the network | A Test server refuses to listen on anything but this computer; a plain-HTTP address that is not local is logged as a warning at start. |
| F-21 | The PDF font depended on the server; renders could pile up | The font is chosen by what the computer has (Arial, Liberation Sans, DejaVu Sans); two renders at a time; the app waits two minutes for a file. Note: QuestPDF's Community licence is free only below a revenue limit; the company has to check that it qualifies. |
| F-22 | The editor of clients could show the contracts of another client | Late answers for an earlier selection are dropped. |
| F-23 | A regenerated document replaced one that was corrected by hand; a folder test by prefix | The new one gets " (2)"; a path is inside a folder only if it really is. |
| F-24 | The lab script lost the user's mode choice and used fixed months | It restores both; months follow today; it waits for the servers instead of sleeping. |
| F-25 | Daylight saving time and a holiday on a weekend in the overtime rules | Not changed: hours are counted on the clock, as the sheet does, and the overtime rules follow the instruction (S6). To be confirmed with the company (Q-03) if night work on the two nights of the time change occurs. |

The restructurings that were first left for later were done afterwards; see section 20.

## 20. The restructurings of the review

| What | Before | Now |
| --- | --- | --- |
| Status, kinds of history, severity, day type, kind of reminder, reason for skipping | Strings compared with literals in the app, the service and the tests | Enums in the contracts (`SheetStatus`, `HistoryKind`, `IssueSeverity`, `DayKind`, `ReminderType`, `SkippedBecause`). They travel as their names, so the traffic and the stored revisions stay readable. `DtoMapping` is the one place that maps the domain's enums to them; a test fails if a member has no counterpart. The history kind is an enum in the domain and in the database too (stored as its name). |
| Bosnian text in the domain library | Messages of the rules, names of the codes, weekday letters, wording of the import | The domain says what was found: a rule key (`V-08.inactive`) and the values (dates, codes, minutes); the import says a kind. The wording is in `ValidationTexts` and `ImportTexts` (API), the names of the codes in `TimeCodeNames` and the shared texts (months, statuses, weekday letters, kinds of history) in `BosnianTexts` (contracts). Numbers are written the same on every server. A test fails if the domain gets text for people again. |
| Approval chain | Employee codes in one text separated by commas, 400 characters at most | A table `sheet_chain_steps` (sheet, position, employee) with a foreign key to the employee; `month_sheets` has a foreign key to the employee too. The upgrade moves the chains of sheets that were already submitted. |
| Version counted by hand | Every method had to remember `Touch` | The database context counts the version of a sheet once per save when anything of it changes (entries, overtime, messages, events, chain). A test changes a sheet without any service and gets the conflict. PostgreSQL's own row version (`xmin`) was not used: the tests run on SQLite, which has none, and one rule for both is worth more than the saving. |
| Stored revisions | The layout of the data transfer object was the layout of the archive | Every revision has a `SchemaVersion`; one from a newer server, or a damaged one, is refused with a clear message (`revision-too-new`, `revision-unreadable`). Raise `SnapshotSchema.Current` when the layout changes in a way an old revision cannot be read. |
| Audit log | Append-only only inside the program | Also a chain: each event carries the hash of the one before and its own. `GET /api/admin/audit/verify` walks the log and names the first event that was changed, removed or inserted. Events written before the chain existed stay as they are. It proves a change, it does not prevent it (database rights, see the IT guide). A unique index on the previous hash stops a second server from forking the chain. |
| Services | `TimesheetService` read, edited, built the answer and held the loading | `SheetDtoBuilder` builds the answer; `SheetQueries.WithDetails()` is the one way to read a sheet with everything; `EmployeeAdminService` holds the rules of changing an employee; `EndpointResults` is the one way to answer. |
| Endpoints | The sheet of the employee was in two files, exports in the approvals file | `TimesheetEndpoints` (the own month, from reading to printing), `ApprovalEndpoints` (what waits for the approver), `ExportEndpoints` (team and finance), reminders with `MeEndpoints`. |
| Employee view model (800 lines, with windows, files and the App inside) | One class | The client has an interface (`ICompanyApiClient`); the dialogs, the saving of files and the leaving of the window are services behind interfaces; the month on screen is `SheetViewModel`; the reminders and the waiting list are small view models; the rows are in their own file. The view model is now about 480 lines and has no window in it. 13 tests run it against a server of a few lines. |

Still open: the printed sheet styles each cell separately (fine for the size of the company; apply styles to ranges if a sheet of many hundreds of employees is ever printed), and two servers writing the same audit log would block each other's events through the unique index (the design is one server).

## 21. Supervisors: reminders, the team tree, the summary; finance and legal switched off

Asked for after the first trial with the colleagues' own monthly message to the legal department.

| Request | What was done |
| --- | --- |
| The supervisor sees exactly who has not submitted | A tab "Moj tim" in the employee window: everybody below the supervisor, with the status of the chosen month (not submitted, submitted and who it waits for, returned, approved). `GET /api/team/{y}/{m}/status`. |
| A reminder to each person that appears as a notification on their computer | `POST /api/team/{y}/{m}/remind/{code}` and a button for each person (and "Podsjeti sve koji nisu predali"). It is stored (`team_nudges`, migration `TeamNudges`) and comes back in that person's `/api/reminders` as `Nudged` with the name of who asked; the banner and the scheduled task (every 30 minutes, working days 07:00–20:00) show it, as a new notification each time (the moment it was sent is part of the signature). It lasts seven days and goes as soon as the month is submitted. Rules: only people below the caller; not for a month that has not begun or one the person was not employed in; once in four hours per person, whoever asks. Audited (`TeamReminderSent`). |
| The supervisor sees the people below as a tree | The same tab: indented by level, a person right before their own reports (`TeamService.TreeOfAsync`; the designated approver of the top person is never walked into twice). |
| Finance and legal out for now, behind a feature flag | `Features:FinanceAndLegal` (default off). Off: the lawyer's "final approver" flag is ignored when chains are built (the chain runs to the top, so nothing waits for a person who never opens the app), the finance and final-approver roles are not granted, `/api/me` reports both as false and `/api/export` is not served. The data stays; switching it on restores everything. The tests run with it on, and one test file with it off. |
| The printout the supervisors send to legal today | "Pregled za odobravanje" (Excel or PDF, from Ispis in the team tab): for each person one statement as in the message ("Nema prekovremenih sati niti izostanaka.", "Prekovremeni sati prikazani u tabeli ispod.", then absences and business trips as periods such as "Službeno putovanje: 21.10.-23.10."; leave across a weekend is one period), grouped by the team lead they report to, and the register of overtime in the five columns of the message (employee, date, start and end, hours with a comma, short description). It covers everybody below, whatever the status; a sheet that is not approved is marked. `AbsencePeriods` (domain) joins the days; `TeamSummary` draws it. |

Left out on purpose: groups named like departments ("CRM", "DEV") because the chart has no department; the groups are named after the team lead. A department field on the employee would give the same headings.

## 22. Entry by exception, and the message as the output

The first version of section 21 made the output right (the message to the legal department) but left the entry as it was: the employee still had to fill the whole month. The change after the discussion with the team:

| Before | Now |
| --- | --- |
| The employee fills every working day ("Popuni standardne dane") and then enters the exceptions | Every working day of an editable month is a standard day (8 hours) from the moment the month is opened, for every employee. There is no fill button. The employee enters only what differs: absence (code and period), overtime, work on a holiday. Taking an absence away gives the standard day back. A month with no exceptions can be submitted as it is. Weekends, state holidays and the person's own religious holidays stay empty; days outside the employment are not filled. |
| A part day could be entered | Not offered: a person works the whole day or has an absence code. |
| The output was a printed sheet (Excel, PDF) | The output is the message for the approver: "Pregled za odobravanje" in the team tab, shown as text with a "Kopiraj" button (the text goes to the clipboard and stays there, so it can be pasted into a mail). Everybody below the supervisor is listed, also those with nothing to report ("Nema prekovremenih sati niti izostanaka."), as the team writes it today; absences and business trips are periods with the name of the code; the register of overtime is tab separated, so it pastes into a table. Sheets that are not approved yet are named above the text, not inside it. `GET /api/team/{y}/{m}/summary`. |
| Excel and PDF everywhere | Behind `Features:SheetExport` (default off): the own sheet, the approver's copy, the team sheet and the summary as a file. Off, the routes answer 404 and the menus are not shown; `/api/me` reports the setting. |

A business trip is a period of absence with its own code (domestic, abroad, without per diem), written as "Službeno putovanje: 21.10.-23.10.". The mail of the team names the occasion too ("Network"); the entry has no free text for it. A description on an absence period would add it.

## 23. Sign-in by user name and password, administrator screen, deputies, versions, e-mail

Asked for after the discussion of what is needed before the company's own pilot. Windows sign-in is built but switched off; people sign in with a user name and password that the administrator creates.

| Part | What it is |
| --- | --- |
| **Sign-in** | `Authentication:Mode` is now `Password` by default. `POST /api/auth/login` (user name, password, remember) gives a session token; every request carries it as `Authorization: Bearer`. The token is random and only its SHA-256 is stored (`auth_sessions`); a session lasts 12 hours, or 30 days with "remember me". The password is a salted PBKDF2 hash (SHA-256, 600 000 rounds; `employee_credentials`). Five wrong passwords lock the account for 15 minutes (the administrator setting a new password lifts it); the answer is the same for a wrong name and a wrong password. The administrator sets a first password; the person must choose their own before the server serves anything else (`password-change-required`). A user name is also linked to the employee as the account `local:username`, so roles and the chart work as for any other sign-in. Deactivating an employee ends their access at once. |
| **Windows / Entra switched off** | `Authentication:Mode` Windows, Entra or Both refuse to start unless `Features:WindowsLogin` is true. The desktop app reads `Company:WindowsLogin` for the same (then it uses the Windows account and shows no sign-in form). |
| **The app** | In employee mode the first thing without a session is the sign-in form (user name, password, "Zapamti me"), then, when the administrator set the password, the form for choosing one's own. With "remember me" the token is kept in a file protected for the Windows user (`employee-signin.dat`) so the reminder task works and the next start goes straight to the window; without it nothing is kept and the 12-hour session ends with the app. "Odjava" ends the session on the server and forgets the token. An ended session brings the form back. |
| **Administrator tab** | Visible to administrators: the list of employees with supervisor, user name and flags, a search, "Uvezi listu radnika" (CSV: preview with the number of good and bad rows, then import), "Provjeri revizijski dnevnik", and a dialog per employee: name, e-mail, active, administrator, supervisor, designated approver, employment dates, user name, new password (with "must choose their own at first sign-in") and removal of the user name. The refusals of the server are explained in Bosnian (circle in the chart, last administrator, taken user name, weak password). The first administrator is created from `Bootstrap:AdminUsername` and `Bootstrap:AdminPassword` (and must change the password at once). |
| **Deputies** | An approver (or the administrator for them) chooses a deputy for a period (`approver_deputies`; at most a year, no overlaps, nobody is their own deputy). During the period the deputy sees what waits for the approver in their own list ("kao zamjena za …"), can open it, approve and return it. The history says who decided and for whom, and the message of a return is written in the name of the approver. A deputy never decides on their own timesheet. The approvals tab is shown to a deputy too. `/api/deputies`, `/api/admin/deputies`, `/api/admin/employees/{code}/deputies`. |
| **Older versions** | The stored versions of a submitted sheet ("Predate verzije") are listed beside the history, for the employee and for the approver; "Otvori" shows that version read only, with a bar "Prikazana je verzija N…" and a way back. Approve, return and submit are off while it is shown. Reading it is audited as before (`RevisionViewed`). |
| **E-mail reminders** | Behind `Features:EmailReminders` (off). An employee has an optional address (set in the administrator dialog). When it is on, the server sends one mail a day (working days, from `Reminders:EmailHour`, default 8, until 20:00) to everybody who has an address and something due, with the same items as the banner and the notification, including a supervisor's reminder. Settings `Smtp:Host`, `Port`, `UseSsl`, `User`, `Password`, `From`, `FromName`. A failing address is tried again in the next round (every ten minutes) and does not stop the others; `reminder_mails` keeps one row per person and day. |

Migrations added: `PasswordLogin`, `Deputies`, `EmailReminders` (applied and tested on PostgreSQL).

Not done on purpose: a screen for the audit log itself, a password reset by e-mail (the administrator sets a new one), two-factor sign-in.

## 24. A product of its own

Until section 23 the desktop app of the employees lived inside the consultant's Daily Task Manager as a "mode" with its own entry button. That was
dropped: the two are now completely separate applications, in separate repositories, sharing no code, no project, no data folder and no
settings, and they do not talk to each other.

| | Daily Task Manager (consultant) | CompanyTimesheet (this repository) |
| --- | --- | --- |
| Data | Its own PostgreSQL database on the consultant's PC | The company server (`Company.Api`) |
| Sign-in | Its own account and "remember me" | User name and password created by the company administrator |
| Files of the user | `%LOCALAPPDATA%\DailyTaskManager` | `%LOCALAPPDATA%\CompanyTimesheet` |
| Scheduled task | `DailyTaskManager\Reminder` | `CompanyTimesheet\Reminder` |
| Updates | In-app update from its GitHub releases | In-app update from this repository's GitHub releases, the same mechanism (`docs/RELEASING.md`) |

What was done: the server, the domain library, the contracts and the HTTP client moved as they were. The window code moved to
`src/CompanyTimesheet.Desktop` with its own start-up, theme, dialogs, file pickers, reminder task and notification code (copied, not
referenced). Signing out now only brings back the sign-in form of the same app. The tests of the window moved with it
(`tests/CompanyTimesheet.Desktop.Tests`). Nothing of the employee side is left in the consultant's repository: its sign-in screen, mode
file, reminder branch, projects, tests and documents were removed.

