<p align="center"><img src="branding/png/thixlauncher-full.png" alt="ThixLauncher" width="460"></p>

# ThixLauncher

Der Launcher für die Thix-Apps unter **Windows und macOS**: installieren mit einem Klick, aktuell halten, starten – und
wieder entfernen. Dabei sind **ThixCalendar** (alle Kalender, keine verpasste Erinnerung), **ThixClip** (Zwischenablage-Verlauf), **ThixFred** (Launcher für Programme und Befehle), **ThixNote** (Notizen als Dateien) und **ThixTunnel** (SSH-Tunnel).
Läuft im Infobereich bzw. in der Menüleiste, ohne Administratorrechte.

![ThixLauncher mit ThixClip (Update verfügbar) und ThixNote](docs/images/main-apps-light.png)

## Screenshots

| Eine App installiert sich | Seite einer App |
|---|---|
| ![ThixNote wird installiert](docs/images/main-installing-light.png) | ![ThixClip mit Update](docs/images/main-detail-dark.png) |

| Einstellungen › Apps | Setup (Windows) |
|---|---|
| ![Kanal und automatische Updates](docs/images/main-settings-light.png) | ![Willkommen im Setup](docs/images/setup-welcome-light.png) |

| Einstellungen › Cloud: angemeldet | Anmeldung im Browser |
|---|---|
| ![Mit der MatCloud verbunden](docs/images/main-settings-cloud-light.png) | ![Der Code wartet auf die Bestätigung](docs/images/main-settings-cloud-code-dark.png) |

## Installation

Fertige Builds gibt es unter [Releases](https://github.com/Real-TTX/ThixLauncher/releases):

- **Windows:** `ThixLauncherSetup.exe` – das Programm ist sein eigenes Setup und installiert sich pro Benutzer nach
  `%LocalAppData%\Programs\ThixLauncher` (ohne Administratorrechte). Ohne Fenster: `ThixLauncherSetup.exe --quiet`.
  Entfernen über „Installierte Apps“ oder `ThixLauncher.exe --uninstall [--quiet]`.
- **macOS:** `ThixLauncher-macOS-arm64.dmg` (Apple Silicon) bzw. `-x64.dmg` (Intel) öffnen und ThixLauncher in
  „Programme“ ziehen.

Nightlies aus dem Branch `dev` stehen im Pre-Release `nightly-channel`.

## So funktioniert es

- **Die Apps** stehen im Katalog (`AppCatalog`): wo sie ihre Builds veröffentlichen und wie ihr Setup läuft. ThixLauncher
  liest bei jedem Start und alle 6 Stunden, was installiert ist und was der gewählte Kanal anbietet (Release oder
  Nightly) – direkt aus den GitHub-Releases der Apps:
  - **ThixCalendar**, **ThixClip**, **ThixFred**, **ThixNote** und **ThixTunnel:** das Manifest (`thixcalendar-update.json`, `thixclip-update.json`, `thixfred-update.json`, `thixnote-update.json`, `thixtunnel-update.json`) im Release
    `release-channel` / `nightly-channel`. Eine App, die nur einzelne Releases veröffentlicht, liest ThixLauncher auch
    (`FeedKind.GitHubReleases`: `v…` für Release, Pre-Release `nightly-…` für Nightly, das Paket über seinen Dateinamen).
- **Versionen:** Ein Release ist neuer, wenn Major.Minor größer ist, sonst die Build-Nummer. So zählt ThixNote 1.1.1
  (neuer Workflow ab Build 1) als neuer als 1.0.5.
- **Sicher:** Jedes Paket wird vor der Installation per **SHA-256** geprüft (aus dem Manifest bzw. GitHubs Digest). Ein
  Paket ohne Prüfsumme wird nie installiert.
- **Installieren / Aktualisieren:** unter Windows läuft das Setup der App selbst, ohne Rückfrage
  (`--update`). Ein Setup, das nachfragt, öffnet sich, und ThixLauncher wartet, bis es geschlossen ist. Unter macOS
  ersetzt ThixLauncher das App-Bundle in `/Applications` (bzw. `~/Applications`) und beendet eine laufende App vorher.
- **Entfernen:** mit dem Deinstallationsprogramm der App (das stille, wenn es eines gibt). Die Daten der App bleiben.
- **Starten:** aus der Liste, über die Symbole oben in der Titelleiste oder aus dem Menü des Tray-Symbols.
- **Updates von ThixLauncher selbst (OTA):** aus dem eigenen Kanal (`thixlauncher-update.json`), optional als
  **Full-Auto-Update** ohne Rückfrage. Lokale Builds aktualisieren sich nicht.
- **MatCloud:** Unter Einstellungen › Cloud meldet sich ThixLauncher einmal bei der MatCloud an (im Browser mit Code oder mit
  Benutzername und Passwort). Jede App, die er danach **neu installiert**, ist gleich mit demselben Konto verbunden: vor ihrem Setup holt
  ThixLauncher bei der MatCloud eine Anmeldung für die App (`POST /api/v1/devices/handoff`) und legt sie für ihren ersten Start ab
  (`%LocalAppData%\Thix\MatCloud\handoff`, gilt einmal und einen Tag). Ist die MatCloud nicht erreichbar, wird die App trotzdem
  installiert und meldet sich später selbst an – die Cloud hält nie eine Installation auf. Abschaltbar mit „Neue Apps gleich verbinden“.
- **Autostart** bei der Anmeldung (Windows: `HKCU\…\Run`, macOS: LaunchAgent) – unsichtbar im Tray.

### Tastenkürzel (macOS: ⌘ statt Strg)

| Kürzel | Aktion |
|---|---|
| Strg+1 / 2 / 3 | Alle Apps / Installiert / Updates |
| Strg+F | Apps durchsuchen |
| Strg+R oder F5 | Nach Updates suchen |
| Enter | Details der gewählten App |
| Esc | Zurück zur Liste / Fenster ausblenden |
| Strg+, | Einstellungen |
| Strg+B | Menü ein- und ausklappen |

### Richtlinien für Administratoren

Windows: `HKLM\SOFTWARE\Policies\ThixLauncher` (DWORD = 1), macOS: Konfigurationsprofil `info.schmoldt.thixlauncher`.

| Richtlinie | Wirkung |
|---|---|
| `LockConfiguration` | Die Einstellungen sind schreibgeschützt (Apps starten geht weiter). |
| `DisableAppChanges` | Keine App wird über den Launcher installiert, aktualisiert oder entfernt. |
| `DisableUpdates` | ThixLauncher sucht und installiert keine eigenen Updates. |
| `DisableCloud` | ThixLauncher meldet sich nicht bei der MatCloud an und gibt keine Anmeldung an Apps weiter. |

## Aufbau

| Projekt | Inhalt |
|---|---|
| `src/ThixLauncher.Core` | Modelle, Business-Layer (nur statische Klassen: `AppService`, `AppCatalog`, `SettingsService`, `CloudService`, `UpdateService`, `InstallService`, `AccessPolicy` …), Release-Feeds (`ReleaseFeed`), der Vertrag der MatCloud (`MatCloud/`, Kopie aus MatCloud) und die Plattformen (`WindowsAppSystem`, `MacAppSystem`) |
| `src/ThixLauncher` | Avalonia-UI mit **MatDesktop** (Kopie aus MatDesign): Hauptfenster, Seiten, Tray, Setup, Selbsttest, Snapshots |
| `tests/ThixLauncher.Tests` | Business-Layer gegen GitHub und einen Computer im Speicher, UI headless gerendert |
| `branding` | Logo in vier Detailstufen (Icon, klein, Tray, Wortmarke) und alle gerenderten Größen |

Berechtigungen und Regeln prüft immer der Business-Layer (`AppService`, `SettingsService`), egal was die Oberfläche zeigt.

## Entwickeln

```
dotnet build ThixLauncher.slnx
dotnet test tests/ThixLauncher.Tests
dotnet run --project src/ThixLauncher                      # mit THIXLAUNCHER_DATA_DIR=<Ordner> als eigene Instanz
dotnet run --project src/ThixLauncher -- --snapshot docs/images   # alle Fenster als PNG, hell und dunkel
THIXLAUNCHER_LIVE_FEEDS=1 dotnet test tests/ThixLauncher.Tests --filter LiveFeedTests   # die echten Feeds lesen
cd tools && npm install && node render-icons.js            # Logo neu rendern
```

## Versionierung

| Kanal | Branch | Format | Beispiel |
|---|---|---|---|
| Release | `main` | `<major>.<minor>.<build>-<builddate>` | `0.1.42-20261116` |
| Nightly | `dev` | `nightly-<build>-<builddate>` | `nightly-43-20261116` |
| Local | alle anderen / lokal | `local-<builddate>` | `local-20261116` |

Major/Minor stehen in `Directory.Build.props`, die Build-Nummer ist die Laufnummer des Workflows
`.github/workflows/desktop.yml` (baut jeden Branch; `main` und `dev` veröffentlichen).

© Matthias Schmoldt
