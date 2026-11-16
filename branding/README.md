# ThixLauncher – Logo

Die Apps als Raster aus vier Kacheln auf der blauen Kachel der Thix-Familie; die vierte Kachel ist dunkel und trägt ein
Startzeichen (installieren und starten). Akzentfarbe **#2F6FED**, dunkles Blau **#163E9E**, Verlauf der Kachel
**#5A9CFF → #1E5BE6** – wie ThixClip und ThixCalendar.

| Datei | Detailstufe | Einsatz |
|---|---|---|
| `logo-icon.svg` | voll (ab 48 px): vier Kacheln mit Verlauf, Startzeichen | App-Icon (Windows .ico ab 48 px, macOS .icns), Fenster, Über-Seite |
| `logo-small.svg` | reduziert (16–32 px): randlose Kachel, vier dicke Kacheln, fettes Startzeichen | Favicon, kleine .ico-Größen, Taskleiste |
| `logo-tray.svg` | einfarbig, ohne Kachel | macOS-Menüleiste (Template-Bild, das System färbt es) |
| `logo-full.svg` | Icon + Wortmarke „Thix**Launcher**“ | README, Kopfzeilen |

`png/` enthält alle gerenderten Größen. Neu erzeugen (nach jeder Änderung an einer SVG):

```
cd tools && npm install && node render-icons.js
```

Das Skript rendert mit dem installierten Microsoft Edge (headless) und schreibt zusätzlich `src/ThixLauncher/Assets/*`
(thixlauncher.ico, Logos, Tray-Vorlage für macOS). Die macOS-Varianten `png/thixlauncher-mac-*.png` halten Apples Rand um
die Kachel ein; `scripts/package-macos.sh` baut daraus das `.icns`. Den Punkt des Zustands im Tray-Symbol zeichnet
MatDesktop zur Laufzeit (grün: läuft, amber: Updates verfügbar).

Die Logos der Apps im Katalog (`src/ThixLauncher/Assets/apps/<id>.png`, 256 px) stammen aus deren Repositories
(ThixClip `branding/png/thixclip-256.png`, ThixNote `assets/icons/thixnote-256.png`).
