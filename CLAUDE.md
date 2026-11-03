# CompanyTimesheet

The company timesheet: a server (`src/Company.Api`), a domain library, a contracts library, an HTTP client and a Windows desktop app
(`src/CompanyTimesheet.Desktop`). It is a separate product; it shares nothing with any other application.

- Build with `dotnet build` and test with `dotnet test` (the API tests run against SQLite files; a few run against a real PostgreSQL when
  `COMPANY_API_TEST_POSTGRES` holds a connection string that may create databases).
- Try it on one PC with `.\tools\lab\lab.ps1 -Role approver|employee|admin|finance` (sign in as `amra`, `benjamin`, `emir`, `dino` or
  `admin`, password `Lab-password-1`); stop it with `-Stop`. Kill the processes you start when you are done.
- Test the interface through the real window for every feature (the lab above), and say what you saw.
- Documentation lives in `docs/`; the decisions and results of each step are in `docs/EMPLOYEE_TIMESHEET_FEASIBILITY.md`.
- The employee list of the company (`Lista radnika.csv`) is personal data: never commit it (`TIMESHEET_EMPLOYEE_LIST` points a test to it).
- Commit, push and release only when asked.
