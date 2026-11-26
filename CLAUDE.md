# CLAUDE.md

Guidance for working in this repository. Keep it in sync with the code: update it whenever architecture, interfaces or conventions change.

## What ThixLauncher is

The launcher of the Thix apps for **Windows and macOS** (desktop app, tray / menu bar): it installs the apps with one
click, keeps them up to date, starts them and removes them. The catalog has **ThixCalc**, **ThixCalendar**, **ThixClip**, **ThixContacts**, **ThixFred**, **ThixMail**, **ThixNote**, **ThixPdf**, **ThixShot**, **ThixShow**, **ThixSpace**, **ThixSync**, **ThixTunnel** and **ThixWrite**
(`AppCatalog`; ThixWrite, ThixCalc, ThixShow, ThixContacts and ThixMail come from ThixOffice, one repository with shared rolling releases); a new app is one entry there plus its logo in `src/ThixLauncher/Assets/apps/<id>.png`. Every app has a `ShortName` ("clip"): its page on the website is thixapps.de/apps/<short name>. The website (MatCMS plugin `thix-store`) reads **`docs/catalog.json`** = `WebCatalog.Json()` from main; `WebCatalogTests` keeps the file equal to the catalog (`THIXLAUNCHER_WRITE_CATALOG=1 dotnet test` writes it). The apps come
straight from their GitHub releases (public, no token), every package is checked by **SHA-256** before it runs.
With **MatCloud** (`../MatCloud`, the cloud of the Thix apps) the launcher signs in once and
hands its sign-in to every app it installs (`CloudService`, contract `POST /api/v1/devices/handoff` + `CloudHandoff`); the cloud never holds up
an installation. UI language German; code, comments and identifiers English. The sister projects are the reference: ThixClip
(`../ThixClip`, structure, setup, update) and MatDesign (`../MatDesign`, MatDesktop and the rules DESK, TRAY, SETUP).

Stack: **.NET 10**, C#, **Avalonia 11.3** with **MatDesktop 0.2.0** (copied from MatDesign). No database: the state of the
apps is read from the system and GitHub; settings are `settings.json`.

## Build / run / test

- `dotnet build ThixLauncher.slnx` – expected: 0 warnings, 0 errors.
- `dotnet test tests/ThixLauncher.Tests` – versions, the platforms' text rules, the feeds, install / update / remove / start
  against **GitHub in memory** (`SampleGitHub`), **a computer in memory** (`SampleAppSystem`) and **MatCloud in memory** (`SampleCloud`:
  sign-in, hand-over, unreachable, removed device), settings and policies,
  the self update, and the **UI rendered headless** (`UiTests`; `THIXLAUNCHER_SCREENSHOTS=<folder>` keeps the pictures).
  Tests never touch the network, the real apps, registry values, the Keychain, the real hand-over folder, a browser or LaunchAgents (`TestData`,
  `Ui.OpenExternalForTests`).
  `THIXLAUNCHER_LIVE_FEEDS=1` enables `LiveFeedTests` (reads the real feeds of every app, downloads no package; every picture of
  the catalog must be in its repository, every commit feed readable).
- Run: `dotnet run --project src/ThixLauncher` (with `THIXLAUNCHER_DATA_DIR=<folder>` an instance of its own). A development
  run works on the real computer: Installieren really installs the app.
- **Snapshots:** `dotnet run --project src/ThixLauncher -- --snapshot docs/images` draws every window headless from sample
  data (ThixClip 0.1.9 installed with 0.1.12 waiting, ThixFred 0.1.1 installed, ThixNote 1.1.1 offered, pinned clock 16.11.2026 14:32, version 1.0.0,
  ANNA-PC) in light and dark: `setup-*`, `main-{apps,list,store,installing,detail,detail-changes,settings,settings-cloud-code,settings-cloud,about,folded}` (the cloud tab against `SampleCloud`), `dialog-install-options` (ThixNote's), `dialog-uninstall`, `toast`,
  `tray-icon-{windows,macos}[-updates]`. The pictures of the store are the real screenshots of the apps when their
  repositories lie next to this one (`../ThixClip/docs/images` …), else plain sample pictures. A new window or page: add it to
  `Snapshot.cs` and look at the pictures.
- The Windows publish (win-x64) is a **trimmed** single file (`TrimMode=partial`, about 33 MB). No `dynamic`, no built-in
  COM (`Platform/Windows/ShellLink` uses `[GeneratedComInterface]`). `ThixLauncher.exe --self-test <report>` checks the
  published program (JSON, installer.json and the install options, feeds, download + SHA-256, install/start/remove in memory, the sign-in at MatCloud and the hand-over with the
  token under DPAPI, COM, every window); CI runs it.
- `scripts/package-macos.sh` builds `ThixLauncher.app` (Info.plist, icns, ad-hoc signature), the update zip and the dmg.
- Icons: `cd tools && npm install && node render-icons.js` renders `branding/*.svg` (installed Edge, headless) into
  `branding/png` and `src/ThixLauncher/Assets`.
- **MatCloud contract** (`src/ThixLauncher.Core/MatCloud/`: routes, DTOs, `MatCloudClient`, `CloudHandoff`) is a COPY from MatCloud
  (`src/MatCloud.Contract`): never edit it here. Copy it again with `node ../MatCloud/tools/contract-copy.js src/ThixLauncher.Core`;
  `UiTests.The_copy_of_the_MatCloud_contract_is_unchanged` holds the files to `matcloud.version`.
- **MatDesktop** (`src/ThixLauncher/MatDesktop/`) is a COPY from MatDesign (branch `feature/desktop-and-services`,
  `src/MatDesktop`): never edit it here. Change it in MatDesign and copy again with
  `node tools/matdesktop-copy.js <path>/ThixLauncher/src/ThixLauncher`; `UiTests.The_copy_of_MatDesktop_is_unchanged`
  holds the files to `matdesktop.version`. An icon the sprite lacks is added to MatDesign first.

## Map

```
src/ThixLauncher.Core   (no UI) models, business layer (static classes), infrastructure, platforms
  MatCloud/             COPY of MatCloud's contract (never edited here): CloudContract, CloudApps, the sign-in DTOs, DeviceHandoff*, MatCloudClient,
                        CloudHandoff (the hand-over file per app in %LOCALAPPDATA%ThixMatCloudhandoff)
  Models/               ThixApp (catalog entry: feed kind, package patterns, Windows uninstall key or MSI upgrade code, mac bundle,
                        Screenshots = AppScreenshot(file in ScreenshotFolder, caption)), AppChange (a commit),
                        AppVersion (release / nightly / local, build number compare), AppRelease, InstalledApp, AppStatus + AppState,
                        AppQuery (search, AppFilter, AppSort), AppSettings (theme, AppChannel, updates, AppsView tiles / list, Cloud = CloudSettings – only CloudService
                        writes it –, ConnectInstalledApps), UpdateManifest (+ "installer"), BusinessException; ThixApp.CloudApp = its id at MatCloud;
                        InstallerConfig (SETUP-08: InstallerSwitch, InstallerFileType, InstallChoices, SetupAction, SetupRequest,
                        AppSetupCommand = how a platform runs an app's package)
  Business/             AppCatalog (the apps), WebCatalog (docs/catalog.json for the website), StoreService (the store's talk with GitHub WITHOUT the API: PictureAsync – raw.githubusercontent.com
                        /<repo>/main/<ScreenshotFolder>/<file>, kept in <data>/store/<app>/, asked again once a day with the ETag, "-dark" first in
                        the dark theme, only PNG, safe names –, ChangesAsync – github.com/<repo>/commits/<main|dev>.atom, first line of the
                        whole message, 6 h in memory –, Featured = the first app not installed), AppService (state per app in memory, Query/Count, RefreshAsync, DetectInstalledAsync, InstallAsync = install or
                        update with download + SHA-256, UninstallAsync, LaunchAsync, UpdateAllAsync; one action per app; all checks; every
                        call to the system on a worker thread, the state read by the UI never asks the system – HasDetected after the
                        first detection –; DescribeActivity for the hang report),
                        CloudService (sign-in in the browser / with a password, CheckAsync in the background, HandOffAsync before the setup of a fresh
                        install: at most 8 s, never throws; ForgetHandoff after removing), SettingsService, AccessPolicy (LockConfiguration,
                        DisableAppChanges, DisableUpdates, DisableCloud), AutostartService
                        (HKCU Run / LaunchAgent), InstallService (Windows self-installing exe), UpdateService + MacAppBundle (own OTA),
                        InstallerOptions (SETUP-08 of MatDesign for the launcher itself: installer.json – embedded, and as "installer"
                        in its update manifest – offers desktop shortcut, autostart, automatic updates, no file types; the command line
                        --quiet / --update / --configure [--desktop-shortcut=yes|no --autostart=yes|no --automatic-updates=yes|no
                        --file-types=…] [--start], a wrong value = exit code 2; Current/Apply), AppInstallOptions (SETUP-08 for the
                        apps: the block "installer" of their manifests → what to ask (ForThisSystem) and the setup's arguments
                        (SetupFor: first install "--quiet" + every offered option [+ "--start" with autostart], macOS "--configure …";
                        updates and builds without the block keep WindowsQuietInstallArguments); AppService.InstallOptionsOf)
  Infrastructure/       ReleaseFeed (manifest of a rolling channel, GitHub release list, download with checksum; HandlerForTests),
                        SampleGitHub (GitHub in memory: tests, self test, snapshots; the store's pictures – Pictures, else SamplePicture
                        PNGs, with ETag/304 – and commit feeds – Commits –; apps of one repository – ThixOffice – told apart
                        by the manifest's and the package's file name; a test may give its own apps; Installers = the "installer" block per
                        app, the sample ThixNote has one), SampleCloud (MatCloud in memory), TokenStore (DPAPI / Keychain),
                        SettingsStore, AppPaths, AppInfo, AppClock, Log (+ Recent: the newest lines in memory), ProblemReports (crash and hang
                        reports; a hang dump is read from a snapshot of the process – PssCaptureSnapshot, the launcher stands still for
                        milliseconds –, at most one per run, the newest KeptDumps = 3 stay)
  Platform/             IAppSystem + AppSystem (the current one; UseForTests), WindowsAppSystem (uninstall entry, MSI products by their upgrade code, the app's setup with
                        the AppSetupCommand – quiet: stderr is read, exit code 2 = the setup refused the options –, its uninstaller),
                        MacAppSystem (bundle in /Applications or ~/Applications, ditto / hdiutil, quit, swap, then "--configure …"),
                        SampleAppSystem (a computer in memory; Setups records every AppSetupCommand, RefuseOptions), InstallInfo (DisplayIcon path, bundle version), CommandLine, Windows/ShellLink
src/ThixLauncher        (Avalonia app)
  Program / App         start-up: ProblemReports, self test, snapshot, setup mode (InstallerOptions.Parse: Windows --update, --quiet, --uninstall;
                        both --configure; wrong options = exit code 2), single
                        instance (MatSingleInstance), tray, AppCoordinator, UpdateCoordinator; ShowMainWindow, Notify, ExitApplication
  AppCoordinator        reads the apps at start / every 6 h / after a channel change; automatic app updates; announces new updates once
  UpdateCoordinator     the launcher's own updates (check, full auto update)
  AppActions            the actions of an app for every page and the tray: main button (Aktualisieren / Öffnen / Installieren),
                        second button, may it be removed, RunAsync (asks the install options before a first install, asks before removing)
  MainWindow            MatTitleBar (logo, word mark, the installed apps to start), MatNav (Bibliothek, Store, Updates (n) = the
                        library filtered, Einstellungen; foot: the cloud state → Einstellungen › Cloud, version = About, fold), page host; an app's page goes back to where it was
                        opened (store or library); keyboard (OnPreviewKeyDown)
  Pages/                AppsPage = the LIBRARY, the start page (the installed apps; title with the compact toolbar beside it – search /
                        filter "Alle installierten" | "Mit Update" / sort / "Nach Updates suchen" / "Alle aktualisieren (n)" while updates
                        wait (AppService.UpdateAllAsync, one run at a time, IsUpdatingAll/CanUpdateAll; also Ctrl/Cmd+U and the tray) /
                        tiles or list, under the title when narrow – and the state line; on "Updates" a waiting update of ThixLauncher
                        itself (button → Einstellungen › Updates); the apps as app tiles – the logo big, the texts centred – (default) or card rows, remembered via
                        SettingsService.Remember; the last tile AddAppsCard "Apps hinzufügen" opens the store; actions of the chosen app
                        below), StorePage (every app: one big – picture, logo, text, state, main action, "Mehr erfahren", ‹ n / 8 › –, then
                        all as tiles; a click on a tile opens its page, its button acts; search hides the big one), TileStyles.axaml (the
                        tile theme AppTile and the band styles, included by both), AppDetailPage (like a store page: header card with the
                        actions – Deinstallieren after a gap –, the gallery: one picture big with its caption, the others as thumbnails;
                        tabs Übersicht | Änderungen (StoreService.ChangesAsync, a row opens the commit) | Details), SettingsPage (tabs Allgemein | Apps | Cloud | Updates | Erweitert;
                        Cloud like ThixClip: the code in a card or user name and password; connected: the account, Abmelden, "Neue Apps gleich verbinden"), AboutPage
  Controls/             ONE code base per control: AppCard (one app as tile – cover picture with the logo on it – or row, same parts),
                        AddAppsCard, AppPicture (a picture of the store: loads when shown and on a theme change, the logo stands in),
                        AppLogo, StateBadge, ActionBar, IconLabel, InfoBar, FormErrors
  Tray/                 TrayIcon (TRAY-01 order; own items: start each installed app, Nach Updates suchen, Alle Apps aktualisieren; amber dot while
                        updates wait)
  Setup/                ThixLauncherSetup (MatSetupWindow: install, update, uninstall; the Options switches from installer.json)
  Dialogs/              ConfirmDialog, InstallOptionsDialog (SETUP-08: a switch per offered option, the app's defaults; Installieren /
                        Abbrechen), ProblemReportDialog
  SelfTest, Snapshot, Ui
  UiWatchdog            knocks on the UI thread every second (HangDetector: a hang = the oldest knock not run for 8 s, so a late
                        watchdog is no hang); the report says what the UI thread did (UiThreadProbe: CPU time over the hang, whether it
                        takes a WM_NULL – waiting in a message loop or not –, IsHungAppWindow), what ran (AppService.DescribeActivity),
                        the newest log lines, and the length of the hang without the pause of the dump
tests/ThixLauncher.Tests
```

## Conventions

- **Microsoft C# coding conventions**, file-scoped namespaces, `_camelCase` fields, human-readable names, early returns.
- **Business layer = static classes.** UI never touches `SettingsStore`, `ReleaseFeed`, the registry, files of other apps
  or processes; pages ask `AppService`. Validation **and permission checks** (AccessPolicy, state, checksum) live in the
  business layer. Errors are `BusinessException(message, field)`; the UI shows them at the `MatFormField` with that
  `FormErrors.Field` or in the page's `InfoBar`. `FeedException` / `AppSystemException` become `BusinessException` there.
- **Every package is checked** (SHA-256 from the manifest or GitHub's digest); a package without one is refused. File names
  from feeds pass `ReleaseFeed.IsSafeFileName`. Never log tokens; GitHub is used without one.
- **The apps own their installation**: on Windows the launcher runs the app's setup and uninstaller (per user, entry under
  HKCU\...\Uninstall\<app>) and never writes that entry itself; on macOS the bundle is the installation.
- **UI rules:** toolbar above lists (search, filter, sort), actions below, left-aligned, positive to negative, delete after
  a gap; buttons in one row; icons where they help (MatDesktop's sprite only); tabs for areas of one item; hide what does
  not apply (Deinstallieren only for installed apps, the second button only while an update waits); cards keep their
  distance; the about page opens from the version bottom left. Colours only from MatDesktop's theme (`{DynamicResource
  Mat.*}`, `control.Use(...)`), never a literal. Version text `Version x · channel` (SETUP-07). Icon-only buttons get
  `AutomationProperties.Name` and a tooltip. Several instances of a control on one page must work (no static state).
- **Avalonia:** a property bound with `control.Use(...)` is not overridden by setting it: rebuild the child instead (AppLogo).
  `ListBox` handles Enter itself: listen with `handledEventsToo`.
- **The UI thread never waits for the system:** registry, Windows Installer, files of other apps, processes, downloads and
  setups run on a worker (`Task.Run` in the business layer, so whoever calls it is safe); no lock is held across such a call.
  Windows Installer can wait while another installation runs, and creating, closing or starting a freshly downloaded program can
  wait for the virus scanner. `UiThreadTests` hold it with a system whose detection waits.
- **Platforms:** one code base; differences behind `OperatingSystem.IsWindows()/IsMacOS()` in one class each. macOS cannot
  be checked from the Windows machine: CI builds and tests it, the owner tries it on a Mac.
- Edit text files with the editor tools or Node/`[IO.File]` with UTF-8: Windows PowerShell 5.1 `Get-Content`/`Set-Content`
  double-encode umlauts.

## Versioning, branches, CI

- `Directory.Build.props` holds major/minor. CI sets `Channel`, `BuildNumber` (GitHub run number of the workflow),
  `BuildDate`: release `0.1.42-20261116` (main), nightly `nightly-43-20261116` (dev), local `local-20261116` (everything else).
- Branches: `main` = release, `dev` = nightly. **Commits are authored by Matthias Schmoldt only – no co-author trailers, no
  other names, no "generated with" lines.**
- `.github/workflows/desktop.yml` (every branch): Windows (test, publish win-x64 single file = `ThixLauncherSetup.exe`, self
  test), macOS (test, publish osx-arm64 + osx-x64, `package-macos.sh` → dmg/zip), publish (manifest
  `thixlauncher-update.json`, rolling releases `release-channel` / `nightly-channel`, versioned release `v<version>` on main).
