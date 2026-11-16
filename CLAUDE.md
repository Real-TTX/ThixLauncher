# CLAUDE.md

Guidance for working in this repository. Keep it in sync with the code: update it whenever architecture, interfaces or conventions change.

## What ThixLauncher is

The launcher of the Thix apps for **Windows and macOS** (desktop app, tray / menu bar): it installs the apps with one
click, keeps them up to date, starts them and removes them. The catalog starts with **ThixClip** and **ThixNote**
(`AppCatalog`); a new app is one entry there plus its logo in `src/ThixLauncher/Assets/apps/<id>.png`. The apps come
straight from their GitHub releases (public, no token), every package is checked by **SHA-256** before it runs.
UI language German; code, comments and identifiers English. The sister projects are the reference: ThixClip
(`../ThixClip`, structure, setup, update) and MatDesign (`../MatDesign`, MatDesktop and the rules DESK, TRAY, SETUP).

Stack: **.NET 10**, C#, **Avalonia 11.3** with **MatDesktop 0.2.0** (copied from MatDesign). No database: the state of the
apps is read from the system and GitHub; settings are `settings.json`.

## Build / run / test

- `dotnet build ThixLauncher.slnx` – expected: 0 warnings, 0 errors.
- `dotnet test tests/ThixLauncher.Tests` – versions, the platforms' text rules, the feeds, install / update / remove / start
  against **GitHub in memory** (`SampleGitHub`) and **a computer in memory** (`SampleAppSystem`), settings and policies,
  the self update, and the **UI rendered headless** (`UiTests`; `THIXLAUNCHER_SCREENSHOTS=<folder>` keeps the pictures).
  Tests never touch the network, the real apps, registry values or LaunchAgents (`TestData`).
  `THIXLAUNCHER_LIVE_FEEDS=1` enables `LiveFeedTests` (reads the real feeds of every app, downloads nothing).
- Run: `dotnet run --project src/ThixLauncher` (with `THIXLAUNCHER_DATA_DIR=<folder>` an instance of its own). A development
  run works on the real computer: Installieren really installs the app.
- **Snapshots:** `dotnet run --project src/ThixLauncher -- --snapshot docs/images` draws every window headless from sample
  data (ThixClip 0.1.9 installed with 0.1.12 waiting, ThixNote 1.1.1 offered, pinned clock 16.11.2026 14:32, version 1.0.0,
  ANNA-PC) in light and dark: `setup-*`, `main-{apps,installing,detail,settings,about,folded}`, `dialog-uninstall`, `toast`,
  `tray-icon-{windows,macos}[-updates]`. A new window or page: add it to `Snapshot.cs` and look at the pictures.
- The Windows publish (win-x64) is a **trimmed** single file (`TrimMode=partial`, about 33 MB). No `dynamic`, no built-in
  COM (`Platform/Windows/ShellLink` uses `[GeneratedComInterface]`). `ThixLauncher.exe --self-test <report>` checks the
  published program (JSON, feeds, download + SHA-256, install/start/remove in memory, COM, every window); CI runs it.
- `scripts/package-macos.sh` builds `ThixLauncher.app` (Info.plist, icns, ad-hoc signature), the update zip and the dmg.
- Icons: `cd tools && npm install && node render-icons.js` renders `branding/*.svg` (installed Edge, headless) into
  `branding/png` and `src/ThixLauncher/Assets`.
- **MatDesktop** (`src/ThixLauncher/MatDesktop/`) is a COPY from MatDesign (branch `feature/desktop-and-services`,
  `src/MatDesktop`): never edit it here. Change it in MatDesign and copy again with
  `node tools/matdesktop-copy.js <path>/ThixLauncher/src/ThixLauncher`; `UiTests.The_copy_of_MatDesktop_is_unchanged`
  holds the files to `matdesktop.version`. An icon the sprite lacks is added to MatDesign first.

## Map

```
src/ThixLauncher.Core   (no UI) models, business layer (static classes), infrastructure, platforms
  Models/               ThixApp (catalog entry: feed kind, package patterns, Windows uninstall key + quiet arguments, mac bundle),
                        AppVersion (release / nightly / local, build number compare), AppRelease, InstalledApp, AppStatus + AppState,
                        AppQuery (search, AppFilter, AppSort), AppSettings (theme, AppChannel, updates), UpdateManifest, BusinessException
  Business/             AppCatalog (the apps), AppService (state per app in memory, Query/Count, RefreshAsync, InstallAsync = install or
                        update with download + SHA-256, UninstallAsync, Launch, UpdateAllAsync; one action per app; all checks),
                        SettingsService, AccessPolicy (LockConfiguration, DisableAppChanges, DisableUpdates), AutostartService
                        (HKCU Run / LaunchAgent), InstallService (Windows self-installing exe), UpdateService + MacAppBundle (own OTA)
  Infrastructure/       ReleaseFeed (manifest of a rolling channel, GitHub release list, download with checksum; HandlerForTests),
                        SampleGitHub (GitHub in memory: tests, self test, snapshots), SettingsStore, AppPaths, AppInfo, AppClock, Log, ProblemReports
  Platform/             IAppSystem + AppSystem (the current one; UseForTests), WindowsAppSystem (uninstall entry, the app's setup,
                        its uninstaller), MacAppSystem (bundle in /Applications or ~/Applications, ditto / hdiutil, quit, swap),
                        SampleAppSystem (a computer in memory), InstallInfo (DisplayIcon path, bundle version), CommandLine, Windows/ShellLink
src/ThixLauncher        (Avalonia app)
  Program / App         start-up: ProblemReports, self test, snapshot, Windows setup mode (--update, --quiet, --uninstall), single
                        instance (MatSingleInstance), tray, AppCoordinator, UpdateCoordinator; ShowMainWindow, Notify, ExitApplication
  AppCoordinator        reads the apps at start / every 6 h / after a channel change; automatic app updates; announces new updates once
  UpdateCoordinator     the launcher's own updates (check, full auto update)
  AppActions            the actions of an app for every page and the tray: main button (Aktualisieren / Öffnen / Installieren),
                        second button, may it be removed, RunAsync (asks before removing)
  MainWindow            MatTitleBar (logo, word mark, the installed apps to start), MatNav (Apps, Installiert, Updates (n),
                        Einstellungen, version = About, fold), page host; keyboard (OnPreviewKeyDown)
  Pages/                AppsPage (list page: title + state line, toolbar search / filter / sort / "Nach Updates suchen", the
                        apps as card rows, actions of the chosen app below), AppDetailPage (header card, tabs Übersicht | Details,
                        actions with Deinstallieren after a gap), SettingsPage (tabs Allgemein | Apps | Updates | Erweitert), AboutPage
  Controls/             ONE code base per control: AppRow, AppLogo, StateBadge, ActionBar, IconLabel, InfoBar, FormErrors
  Tray/                 TrayIcon (TRAY-01 order; own items: start each installed app, Nach Updates suchen, update; amber dot while
                        updates wait)
  Setup/                ThixLauncherSetup (MatSetupWindow: install, update, uninstall)
  Dialogs/              ConfirmDialog, ProblemReportDialog
  SelfTest, Snapshot, UiWatchdog, Ui
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
