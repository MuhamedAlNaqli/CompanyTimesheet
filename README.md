# CompanyTimesheet

The company timesheet ("šihtarica"): employees enter the exceptions of their month, supervisors follow and approve, and the approver gets a
message in the format the teams send today. It is a product of its own, with nothing shared with any other application.

| Part | What it is |
| --- | --- |
| `src/Timesheet.Domain` | The rules, pure .NET: calendar, codes, validation, overtime, approval chain, workflow, deadlines, reminders |
| `src/Company.Api.Contracts` | The records and enums that travel between the server and the app |
| `src/Company.Api` | The server (ASP.NET Core, EF Core; PostgreSQL in production, SQLite for development and tests) |
| `src/Company.Api.Client` | The HTTP client of the app |
| `src/CompanyTimesheet.Desktop` | The Windows desktop app (Avalonia): sign-in, the month, the team, approvals, administration, reminders |
| `tests/` | Domain, API (with a few tests against a real PostgreSQL when `COMPANY_API_TEST_POSTGRES` is set) and desktop view model tests |
| `tools/lab/lab.ps1` | A local test bench: the server with sample people and the app, signed in with sample users |
| `installer/` | The WiX MSI and the appcast template; `build-installer.ps1` makes the MSI the in-app update installs |
| `.github/workflows/` | `build-and-test.yml` (every push) and `release.yml` (a pushed tag `v*` makes a GitHub release with the MSI and the appcast) |
| `Dockerfile`, `render.yaml` | A demo server for the internet (`docs/HOSTING_DEMO.md`) |
| `server-test/` | What goes into the test-server zip of a release: `start-test-server.cmd` (SQLite, one PC) and a sample employee list |
| `docs/` | `EMPLOYEE_TIMESHEET_FEASIBILITY.md` (analysis, decisions, results), `API_IT_GUIDE.md` (what IT needs to run the server), `RELEASING.md` (releases and the update of the app), `MANUAL_TESTING.md` (how to try a release on one PC, in Bosnian), `HOSTING_DEMO.md` (a demo server on the internet), `PRODUCTION_READINESS.md` (how it is built, what was checked, what to decide and set up before the whole company uses it) |

```
dotnet build
dotnet test
.\tools\lab\lab.ps1 -Role approver      # try it on this PC; stop with .\tools\lab\lab.ps1 -Stop
```

The app keeps its files in `%LOCALAPPDATA%\CompanyTimesheet`. The server address is set in `appsettings.local.json` next to the program
(`Company:ApiUrl`). The employee list of the company (`Lista radnika.csv`) is personal data and is not kept in the repository; the one test
that reads it takes its path from the environment variable `TIMESHEET_EMPLOYEE_LIST`.
