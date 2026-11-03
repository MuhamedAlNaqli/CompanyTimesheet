@echo off
rem Test server of the company timesheet on THIS computer: a SQLite file, no PostgreSQL, address http://localhost:5090
rem The first administrator is  admin  with the first password below; the app asks for a new one at the first sign-in.
setlocal
set "DATA=%LOCALAPPDATA%\CompanyTimesheetTestServer"
if not exist "%DATA%" mkdir "%DATA%"

set "Database__Provider=sqlite"
set "ConnectionStrings__Default=Data Source=%DATA%\test.db"
set "Authentication__Mode=Password"
set "Bootstrap__AdminUsername=admin"
set "Bootstrap__AdminPassword=Admin-first-password-1"
set "ASPNETCORE_URLS=http://localhost:5090"

rem Reminders are counted from this month, so a new test database does not start with months of overdue timesheets
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM"') do set "Reminders__StartFrom=%%i"

echo.
echo  Test server:  http://localhost:5090
echo  Database:     %DATA%\test.db   (delete the file to start from nothing)
echo  Administrator: admin  /  Admin-first-password-1   (you choose your own password at the first sign-in)
echo.
echo  Leave this window open while you test. Close it (or press Ctrl+C) to stop the server.
echo.
"%~dp0Company.Api.exe"
