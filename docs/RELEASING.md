# Releasing and updating the app

The app updates itself the way Daily Task Manager does: **NetSparkleUpdater** reads an *appcast* (an RSS file) from the latest GitHub
release, downloads the MSI it names and starts it.

```
tag v0.2.0 pushed ──> GitHub Actions (release.yml) ──> tests, publish, MSI
                                                    └──> release v0.2.0 with  CompanyTimesheet-v0.2.0.0-win-x64.msi  and  appcast.xml
installed app ──> https://github.com/MuhamedAlNaqli/CompanyTimesheet/releases/latest/download/appcast.xml
              ──> a newer version? ──> update window ──> MSI (/passive) replaces the installed copy
```

## Making a release

1. Make sure `dotnet build` and `dotnet test` pass on `master`.
2. Push a tag: `git tag v0.2.0` then `git push origin v0.2.0`. (A tag with a suffix, for example `v0.2.0-beta`, makes a pre-release; the
   `latest` release the app reads from is the newest one that is not a pre-release.)
3. The workflow `Build and Release` does the rest. The version of the tag becomes the version of the program (`0.2.0.0`) and of the MSI.
4. To try the workflow without a tag, run it by hand from the Actions tab: it builds the MSI and keeps it as an artifact for two weeks.

To build the MSI on your own PC (needs `dotnet tool install --global wix --version 4.0.5`):

```powershell
.\installer\build-installer.ps1 -Version 0.2.0.0      # installer\output\CompanyTimesheet-v0.2.0.0-win-x64.msi
```

## What the app does about updates

- Twenty seconds after the start and then once a day (`UpdateService:CheckInterval`) it reads the appcast. A newer version opens the update
  window of the library (in English; it offers the update once per version per run).
- The button with the cloud icon in the header checks at once and says "Imate najnoviju verziju" or what went wrong.
- The scheduled reminder task (every 30 minutes, see `API_IT_GUIDE.md`) also reads the appcast every six hours and, when the app is closed,
  shows a notification once per release (again after three days); clicking it opens the app, which offers the update.
- Settings (`appsettings.json`, overridable in `appsettings.local.json`): `UpdateService:AppCastUrl`, `AutoUpdatesEnabled`, `CheckInterval`.
  `COMPANYTIMESHEET_ALLOW_MULTIPLE=1` (debug builds) lets a development build run next to an installed copy.

## Good to know

- The installer is per machine (Program Files) and needs administrator rights; the update window starts it with the rights it needs.
  `appsettings.local.json` (the server address) is not part of the package and survives updates.
- Signatures are not verified yet: set `UpdateService:Ed25519PublicKey` (and sign the MSI with NetSparkle's `generate_appcast`/`netsparkle-generate-appcast`
  tool) before the app is used outside a closed group. Without a key the log says so at every start.
- The feed must be public: a private GitHub repository cannot serve `releases/latest/download/appcast.xml` to the app.
- Until the first release exists, the address answers 404 and the app only says that the update server could not be reached.
- An update installs a new version; it never changes the data on the company server.
