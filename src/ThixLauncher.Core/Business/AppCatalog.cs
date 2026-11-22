using MatCloud.Contract;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Business;

/// <summary>
/// The Thix apps the launcher knows. A new app is one entry here (and its logo in the app's Assets/apps folder): where it
/// publishes its builds, how its setup runs on Windows, how its bundle is called on macOS and which pictures of its repository
/// (docs/images, the light ones; the store tries "-dark" in the dark theme) the store shows.
/// </summary>
public static class AppCatalog
{
    public static IReadOnlyList<ThixApp> Apps { get; } =
    [
        new ThixApp
        {
            Id = "thixcalc",
            CloudApp = CloudApps.Calc,
            Name = "ThixCalc",
            Tagline = "Tabellenkalkulation für Zahlen, Listen und Auswertungen",
            Description = "Rechnen wie in Excel: Formeln mit 459 Funktionen unter deutschen Namen, Zahlenformate, Sortieren und Filtern, " +
                          "Drucken und Export als PDF. Excel-Mappen (.xlsx, .xlsm) und CSV-Dateien öffnet und speichert ThixCalc " +
                          "direkt, mit der MatCloud auch in ThixDrive.",
            Icon = "table",
            // ThixOffice publishes every app of the suite (ThixWrite, ThixCalc, ThixShow, ThixContacts) into the same rolling releases;
            // the manifest and the package names tell them apart.
            Repository = "Real-TTX/ThixOffice",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixcalc-update.json",
            WindowsExecutable = "ThixCalc.exe",
            WindowsUninstallKey = "ThixCalc",
            // ThixCalcSetup.exe (the program itself, the same install code as ThixWrite) --update installs or updates without a
            // window: a fresh install takes the default choices (Start menu, "Öffnen mit" for its document types, no desktop
            // shortcut, no default program taken); a running ThixCalc keeps its unsaved changes as recovery files and ends, it is
            // not started again (that would take "--restart").
            WindowsQuietInstallArguments = "--update",
            MacBundleName = "ThixCalc.app",
            MacBundleId = "info.schmoldt.thixcalc",
            Screenshots =
            [
                new AppScreenshot("thixcalc-main-light.png", "Tabellen mit Formeln und Zahlenformaten"),
                new AppScreenshot("thixcalc-main-chart-light.png", "Diagramme aus den Daten der Tabelle"),
                new AppScreenshot("thixcalc-main-formula-light.png", "Formeln mit deutschen Funktionsnamen"),
                new AppScreenshot("thixcalc-dialog-format-cells-light.png", "Zellen formatieren wie gewohnt"),
            ],
        },
        new ThixApp
        {
            Id = "thixcalendar",
            CloudApp = CloudApps.Calendar,
            Name = "ThixCalendar",
            Tagline = "Alle Kalender, keine verpasste Erinnerung",
            Description = "Microsoft 365, Google Kalender, ICS-Abos und eigene Kalender in einem Fenster – mit Erinnerungen, die stehen " +
                          "bleiben, bis du sie beantwortest, und Online-Besprechungen mit einem Klick. Mit der MatCloud sind Konten, " +
                          "eigene Kalender und erledigte Erinnerungen auf all deinen Computern gleich.",
            Icon = "calendar",
            Repository = "Real-TTX/ThixCalendar",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixcalendar-update.json",
            WindowsExecutable = "ThixCalendar.exe",
            WindowsUninstallKey = "ThixCalendar",
            // ThixCalendarSetup.exe --update installs or updates without a window and starts ThixCalendar in the tray.
            WindowsQuietInstallArguments = "--update",
            MacBundleName = "ThixCalendar.app",
            MacBundleId = "info.schmoldt.thixcalendar",
            Screenshots =
            [
                new AppScreenshot("desktop-week-light.png", "Alle Kalender in einer Woche"),
                new AppScreenshot("desktop-month-light.png", "Der Monat auf einen Blick"),
                new AppScreenshot("desktop-reminder-light.png", "Erinnerungen, die stehen bleiben"),
                new AppScreenshot("desktop-editor-light.png", "Termine anlegen und bearbeiten"),
            ],
        },
        new ThixApp
        {
            Id = "thixclip",
            CloudApp = CloudApps.Clip,
            Name = "ThixClip",
            Tagline = "Zwischenablage-Verlauf",
            Description = "ThixClip merkt sich, was du kopierst – Texte, Links und Bilder samt erkanntem Text – und holt es mit einem " +
                          "Tastenkürzel zurück. Mit der MatCloud ist der Verlauf auf all deinen Computern gleich.",
            Icon = "clipboard",
            Repository = "Real-TTX/ThixClip",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixclip-update.json",
            WindowsExecutable = "ThixClip.exe",
            WindowsUninstallKey = "ThixClip",
            // ThixClipSetup.exe --update installs or updates without a window and starts ThixClip in the tray.
            WindowsQuietInstallArguments = "--update",
            MacBundleName = "ThixClip.app",
            MacBundleId = "info.schmoldt.thixclip",
            Screenshots =
            [
                new AppScreenshot("main-history-light.png", "Der Verlauf: Texte, Links und Bilder"),
                new AppScreenshot("dialog-viewer-light.png", "Ein Eintrag mit erkanntem Text"),
                new AppScreenshot("main-settings-light.png", "Tastenkürzel und Einstellungen"),
                new AppScreenshot("main-cloud-light.png", "Mit der MatCloud auf allen Computern"),
            ],
        },
        new ThixApp
        {
            Id = "thixcontacts",
            CloudApp = CloudApps.Contacts,
            Name = "ThixContacts",
            Tagline = "Adressbuch für Kontakte und Kontaktgruppen",
            Description = "Kontakte verwalten wie in Outlook: Personen mit Adressen, Telefonnummern und Bild, Gruppen, Kategorien und " +
                          "Favoriten, doppelte Kontakte finden und zusammenführen, Import und Export als vCard und CSV, Drucken und " +
                          "Export als PDF. Mit der MatCloud sind die Kontakte auf all deinen Computern und im Browser gleich.",
            Icon = "users",
            // ThixOffice publishes every app of the suite (ThixWrite, ThixCalc, ThixShow, ThixContacts) into the same rolling
            // releases; the manifest and the package names tell them apart.
            Repository = "Real-TTX/ThixOffice",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixcontacts-update.json",
            WindowsExecutable = "ThixContacts.exe",
            WindowsUninstallKey = "ThixContacts",
            // ThixContactsSetup.exe (the program itself, the same install code as the other ThixOffice apps) --update installs or
            // updates without a window: a fresh install takes the default choices (Start menu, "Öffnen mit" for vCard files, no
            // desktop shortcut, no default program taken); a running ThixContacts ends (its contacts are in its store), it is not
            // started again (that would take "--restart").
            WindowsQuietInstallArguments = "--update",
            MacBundleName = "ThixContacts.app",
            MacBundleId = "info.schmoldt.thixcontacts",
            Screenshots =
            [
                new AppScreenshot("thixcontacts-main-light.png", "Alle Kontakte mit Suche und Visitenkarte"),
                new AppScreenshot("thixcontacts-dialog-edit-light.png", "Einen Kontakt bearbeiten"),
                new AppScreenshot("thixcontacts-dialog-merge-light.png", "Doppelte Kontakte zusammenführen"),
                new AppScreenshot("thixcontacts-dialog-import-mapping-light.png", "Import aus CSV mit Spaltenzuordnung"),
            ],
        },
        new ThixApp
        {
            Id = "thixfred",
            CloudApp = CloudApps.Fred,
            Name = "ThixFred",
            Tagline = "Launcher für Programme und Befehle",
            Description = "Ein Tastenkürzel, ein Suchfeld: Programme starten und beenden, rechnen, Befehle im Terminal ausführen, im Web " +
                          "suchen – dazu Themes, Plugins mit eigenem Store und deine eigenen Skripte.",
            Icon = "rocket",
            Repository = "Real-TTX/ThixFred",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixfred-update.json",
            WindowsExecutable = "ThixFred.exe",
            WindowsUninstallKey = "ThixFred",
            // ThixFredSetup.exe --update installs or updates without a window and starts ThixFred in the tray.
            WindowsQuietInstallArguments = "--update",
            MacBundleName = "ThixFred.app",
            MacBundleId = "info.schmoldt.thixfred",
            Screenshots =
            [
                new AppScreenshot("main-themes-light.png", "Themes für den Launcher"),
                new AppScreenshot("launcher-search-light.png", "Ein Suchfeld für Programme und Befehle"),
                new AppScreenshot("launcher-calc-light.png", "Rechnen direkt im Launcher"),
                new AppScreenshot("launcher-terminal-light.png", "Befehle im eingebauten Terminal"),
                new AppScreenshot("main-plugins-light.png", "Plugins und eigene Skripte"),
            ],
        },
        new ThixApp
        {
            Id = "thixnote",
            CloudApp = CloudApps.Note,
            Name = "ThixNote",
            Tagline = "Notizen als Dateien",
            Description = "Eine bewusst einfache Notiz-App: Markdown, Text und kennwortgeschützte Notizen als normale Dateien, mit Tabs " +
                          "wie in einem Editor. Breit eine kleine Notizbibliothek, schmal ein Editor wie Notepad.",
            Icon = "file-text",
            Repository = "Real-TTX/ThixNote",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixnote-update.json",
            WindowsExecutable = "ThixNote.exe",
            WindowsUninstallKey = "ThixNote",
            // ThixNoteSetup.exe --update installs or updates without a window; a running ThixNote saves its notes and ends,
            // it is not started again (that would take "--restart").
            WindowsQuietInstallArguments = "--update",
            MacBundleName = "ThixNote.app",
            MacBundleId = "info.schmoldt.thixnote",
            Screenshots =
            [
                new AppScreenshot("main-notes-light.png", "Notizen als Dateien"),
                new AppScreenshot("main-preview-light.png", "Markdown mit Vorschau"),
                new AppScreenshot("main-compact-light.png", "Kompakt neben der Arbeit"),
                new AppScreenshot("main-settings-cloud-light.png", "Mit der MatCloud auf allen Computern"),
            ],
        },
        new ThixApp
        {
            Id = "thixshow",
            CloudApp = CloudApps.Show,
            Name = "ThixShow",
            Tagline = "Präsentationen mit Folien, Designs und Bildschirmpräsentation",
            Description = "Präsentieren wie in PowerPoint: Folien mit Layouts und Designs, Formen, Bilder und Diagramme, " +
                          "Übergänge und Animationen, Bildschirmpräsentation mit Referentenansicht, Drucken und Export als PDF. " +
                          "PowerPoint-Präsentationen (.pptx, .pptm) öffnet und speichert ThixShow direkt, mit der MatCloud auch in ThixDrive.",
            Icon = "tv",
            // ThixOffice publishes every app of the suite (ThixWrite, ThixCalc, ThixShow, ThixContacts) into the same rolling releases; the
            // manifest and the package names tell them apart.
            Repository = "Real-TTX/ThixOffice",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixshow-update.json",
            WindowsExecutable = "ThixShow.exe",
            WindowsUninstallKey = "ThixShow",
            // ThixShowSetup.exe (the program itself, the same install code as ThixWrite and ThixCalc) --update installs or updates
            // without a window: a fresh install takes the default choices (Start menu, "Öffnen mit" for its document types, no
            // desktop shortcut, no default program taken); a running ThixShow keeps its unsaved changes as recovery files and ends,
            // it is not started again (that would take "--restart").
            WindowsQuietInstallArguments = "--update",
            MacBundleName = "ThixShow.app",
            MacBundleId = "info.schmoldt.thixshow",
            Screenshots =
            [
                new AppScreenshot("thixshow-main-light.png", "Folien gestalten"),
                new AppScreenshot("thixshow-main-design-light.png", "Designs für die ganze Präsentation"),
                new AppScreenshot("thixshow-main-animations-light.png", "Animationen und Übergänge"),
                new AppScreenshot("thixshow-main-sorter-light.png", "Alle Folien im Überblick"),
            ],
        },
        new ThixApp
        {
            Id = "thixtunnel",
            Name = "ThixTunnel",
            Tagline = "SSH-Tunnel",
            Description = "SSH-Profile, mehrere Hops und lokale Portweiterleitungen: ThixTunnel baut Tunnel mit dem OpenSSH des Systems " +
                          "auf (ssh -J … -L …), verbindet auf Wunsch automatisch und nach Abbrüchen erneut – im Infobereich bzw. in der Menüleiste.",
            Icon = "route",
            Repository = "Real-TTX/ThixTunnel",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixtunnel-update.json",
            WindowsExecutable = "ThixTunnel.exe",
            WindowsUninstallKey = "ThixTunnel",
            // ThixTunnelSetup.exe --update installs or updates without a window; a running ThixTunnel remembers its active
            // tunnels and ends, the installed one starts in the tray and connects them again.
            WindowsQuietInstallArguments = "--update",
            MacBundleName = "ThixTunnel.app",
            MacBundleId = "info.schmoldt.thixtunnel",
            Screenshots =
            [
                new AppScreenshot("main-connections-light.png", "Verbindungen mit einem Klick"),
                new AppScreenshot("main-profiles-light.png", "Server als Profile"),
                new AppScreenshot("main-routes-light.png", "Weiterleitungen je Profil"),
                new AppScreenshot("main-profile-edit-light.png", "Ein Profil bearbeiten"),
            ],
        },
        new ThixApp
        {
            Id = "thixwrite",
            CloudApp = CloudApps.Write,
            Name = "ThixWrite",
            Tagline = "Textverarbeitung für Dokumente, Briefe und Berichte",
            Description = "Schreiben wie in Word: Menüband, Seiten wie auf Papier, Formatvorlagen, Aufzählungen, Suchen und Ersetzen und " +
                          "Export als PDF. Word-Dokumente (.docx) öffnet und speichert ThixWrite direkt – was es noch nicht anzeigt, " +
                          "bleibt beim Speichern erhalten.",
            Icon = "file-text",
            // ThixOffice publishes every app of the suite (ThixWrite, ThixCalc, ThixShow, ThixContacts) into the same rolling releases;
            // the manifest and the package names tell them apart.
            Repository = "Real-TTX/ThixOffice",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixwrite-update.json",
            WindowsExecutable = "ThixWrite.exe",
            WindowsUninstallKey = "ThixWrite",
            // ThixWriteSetup.exe (the program itself) --update installs or updates without a window: a fresh install takes the
            // default choices (Start menu, "Öffnen mit" for its document types, no desktop shortcut, no default program taken);
            // a running ThixWrite keeps its unsaved changes as recovery files and ends, it is not started again (that would
            // take "--restart").
            WindowsQuietInstallArguments = "--update",
            MacBundleName = "ThixWrite.app",
            MacBundleId = "info.schmoldt.thixwrite",
            Screenshots =
            [
                new AppScreenshot("thixwrite-main-light.png", "Texte schreiben wie in Word"),
                new AppScreenshot("thixwrite-main-table-light.png", "Tabellen im Dokument"),
                new AppScreenshot("thixwrite-main-comments-light.png", "Kommentare und Überarbeitung"),
                new AppScreenshot("thixwrite-main-spelling-light.png", "Rechtschreibprüfung"),
                new AppScreenshot("thixwrite-backstage-print-light.png", "Drucken und als PDF exportieren"),
            ],
        },
    ];

    /// <summary>The app with this id, or null.</summary>
    public static ThixApp? Find(string appId) =>
        Apps.FirstOrDefault(app => string.Equals(app.Id, appId, StringComparison.OrdinalIgnoreCase));
}
