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
            ShortName = "calc",
            CloudApp = CloudApps.Calc,
            Features = [AppFeature.Cloud],
            Name = "ThixCalc",
            Tagline = "Tabellenkalkulation für Zahlen, Listen und Auswertungen",
            Description = "Rechnen wie in Excel: Formeln mit 459 Funktionen unter deutschen Namen, Zahlenformate, Sortieren und Filtern, " +
                          "Drucken und Export als PDF. Excel-Mappen (.xlsx, .xlsm) und CSV-Dateien öffnet und speichert ThixCalc " +
                          "direkt, mit der MatCloud auch in ThixDrive.",
            Icon = "table",
            // ThixOffice publishes every app of the suite (ThixWrite, ThixCalc, ThixShow, ThixContacts, ThixMail) into the same rolling releases;
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
            ShortName = "calendar",
            CloudApp = CloudApps.Calendar,
            Features = [AppFeature.Cloud],
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
            ShortName = "clip",
            CloudApp = CloudApps.Clip,
            Features = [AppFeature.Cloud],
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
            ShortName = "contacts",
            CloudApp = CloudApps.Contacts,
            Features = [AppFeature.Cloud],
            Name = "ThixContacts",
            Tagline = "Adressbuch für Kontakte und Kontaktgruppen",
            Description = "Kontakte verwalten wie in Outlook: Personen mit Adressen, Telefonnummern und Bild, Gruppen, Kategorien und " +
                          "Favoriten, doppelte Kontakte finden und zusammenführen, Import und Export als vCard und CSV, Drucken und " +
                          "Export als PDF. Mit der MatCloud sind die Kontakte auf all deinen Computern und im Browser gleich.",
            Icon = "users",
            // ThixOffice publishes every app of the suite (ThixWrite, ThixCalc, ThixShow, ThixContacts, ThixMail) into the same rolling
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
            ShortName = "fred",
            CloudApp = CloudApps.Fred,
            Features = [AppFeature.Cloud],
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
            Id = "thixmail",
            ShortName = "mail",
            CloudApp = CloudApps.Mail,
            Features = [AppFeature.Cloud],
            Name = "ThixMail",
            Tagline = "E-Mail, Kalender und Personen in einem Fenster",
            Description = "Mailen wie in Outlook: Konten über IMAP und SMTP, offline lesen, schreiben und ordnen, Unterhaltungen, Suche und " +
                          "Einladungen zu Terminen. Mit der MatCloud kommen Kalender und Personen ins selbe Fenster; die Nachrichten bleiben " +
                          "auf dem Mailserver und deinem Computer. Gmail und Microsoft 365 brauchen vorerst ein App-Passwort.",
            Icon = "mail",
            // ThixOffice publishes every app of the suite (ThixWrite, ThixCalc, ThixShow, ThixContacts, ThixMail) into the same
            // rolling releases; the manifest and the package names tell them apart.
            Repository = "Real-TTX/ThixOffice",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixmail-update.json",
            WindowsExecutable = "ThixMail.exe",
            WindowsUninstallKey = "ThixMail",
            // ThixMailSetup.exe (the program itself, the same install code as the other ThixOffice apps) --update installs or
            // updates without a window: a fresh install takes the default choices (Start menu, "Öffnen mit" for .eml files, no
            // desktop shortcut, no default program taken); a running ThixMail ends (its messages are in its store and on the mail
            // server), it is not started again (that would take "--restart").
            WindowsQuietInstallArguments = "--update",
            MacBundleName = "ThixMail.app",
            MacBundleId = "info.schmoldt.thixmail",
            Screenshots =
            [
                new AppScreenshot("thixmail-main-newsletter-light.png", "Posteingang mit Lesebereich"),
                new AppScreenshot("thixmail-compose-light.png", "Eine Nachricht verfassen"),
                new AppScreenshot("thixmail-main-calendar-light.png", "Der Kalender im selben Fenster"),
                new AppScreenshot("thixmail-main-invitation-light.png", "Einladungen zusagen oder absagen"),
            ],
        },
        new ThixApp
        {
            Id = "thixnote",
            ShortName = "note",
            CloudApp = CloudApps.Note,
            Features = [AppFeature.Cloud],
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
            Id = "thixpdf",
            ShortName = "pdf",
            CloudApp = CloudApps.Pdf,
            Features = [AppFeature.Cloud],
            Name = "ThixPdf",
            Tagline = "PDFs ansehen, kommentieren, ausfüllen und unterschreiben",
            Description = "Alles für PDFs in einem Programm: lesen und durchsuchen, markieren und kommentieren, Formulare ausfüllen und " +
                          "selbst anlegen, von Hand oder digital mit Zertifikat unterschreiben, Seiten drehen, verschieben, zusammenführen " +
                          "und teilen, mit Kennwort schützen. Mit der MatCloud liegen deine PDFs in ThixDrive, Unterschriften und Stempel " +
                          "sind auf all deinen Computern gleich.",
            Icon = "file-text",
            Repository = "Real-TTX/ThixPdf",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixpdf-update.json",
            WindowsExecutable = "ThixPdf.exe",
            WindowsUninstallKey = "ThixPdf",
            // ThixPdfSetup.exe (the program itself, the install code of ThixOffice) --update installs or updates without a window: a
            // fresh install takes the default choices (Start menu, "Öffnen mit" for PDFs, no desktop shortcut, no default program
            // taken); a running ThixPdf keeps its unsaved changes as recovery files and ends, it is not started again (that would take
            // "--restart").
            WindowsQuietInstallArguments = "--update",
            MacBundleName = "ThixPdf.app",
            MacBundleId = "info.schmoldt.thixpdf",
            Screenshots =
            [
                new AppScreenshot("thixpdf-main-light.png", "PDFs lesen und durchsuchen"),
                new AppScreenshot("thixpdf-main-comment-light.png", "Markieren und kommentieren"),
                new AppScreenshot("thixpdf-main-form-light.png", "Formulare ausfüllen"),
                new AppScreenshot("thixpdf-main-sign-light.png", "Unterschreiben"),
                new AppScreenshot("thixpdf-main-pages-light.png", "Seiten ordnen"),
            ],
        },
        new ThixApp
        {
            Id = "thixshot",
            ShortName = "shot",
            Name = "ThixShot",
            Tagline = "Screenshots und Bildschirmvideos mit bearbeitbaren Zooms",
            Description = "Nimm den Bildschirm oder einen Ausschnitt auf. Screenshots kannst du mit Zeitverzögerung aufnehmen, " +
                          "zuschneiden und markieren. Im Videoeditor lassen sich Zooms nachträglich setzen, verschieben und anpassen. " +
                          "Die schwebende Aufnahmeleiste startet Screenshots und Videos direkt vom Desktop.",
            Icon = "monitor",
            Repository = "Real-TTX/ThixShot",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixshot-update.json",
            WindowsExecutable = "ThixShot.exe",
            WindowsUninstallKey = "ThixShot",
            WindowsMsiUpgradeCode = new Guid("B75BDAC5-9408-45CC-B1BE-0AE9049DD8C0"),
            MacBundleName = "ThixShot.app",
            MacBundleId = "de.thixapps.thixshot",
            ScreenshotFolder = "docs",
            Screenshots =
            [
                new AppScreenshot("aufnahmestudio.png", "Screenshots und Bildschirmvideos im Aufnahmestudio"),
                new AppScreenshot("videoeditor.png", "Zooms auf der Zeitleiste des Videoeditors bearbeiten"),
                new AppScreenshot("bereitschaft.png", "Die Aufnahmeleiste am Bildschirmrand"),
            ],
        },
        new ThixApp
        {
            Id = "thixshow",
            ShortName = "show",
            CloudApp = CloudApps.Show,
            Features = [AppFeature.Cloud],
            Name = "ThixShow",
            Tagline = "Präsentationen mit Folien, Designs und Bildschirmpräsentation",
            Description = "Präsentieren wie in PowerPoint: Folien mit Layouts und Designs, Formen, Bilder und Diagramme, " +
                          "Übergänge und Animationen, Bildschirmpräsentation mit Referentenansicht, Drucken und Export als PDF. " +
                          "PowerPoint-Präsentationen (.pptx, .pptm) öffnet und speichert ThixShow direkt, mit der MatCloud auch in ThixDrive.",
            Icon = "tv",
            // ThixOffice publishes every app of the suite (ThixWrite, ThixCalc, ThixShow, ThixContacts, ThixMail) into the same rolling releases; the
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
            Id = "thixspace",
            ShortName = "space",
            CloudApp = CloudApps.Space,
            Features = [AppFeature.Cloud],
            Name = "ThixSpace",
            Tagline = "Speicheranalyse mit interaktiver Karte",
            Description = "ThixSpace analysiert Laufwerke und SMB-Freigaben und zeigt auf einer interaktiven Karte, was wie viel Platz belegt. " +
                          "Dateien und Ordner öffnest oder löschst du direkt dort, und jede neue Analyse zeigt, was seit der letzten gewachsen " +
                          "oder geschrumpft ist. Mit der MatCloud sind Quellen und Analysen auf all deinen Computern gleich.",
            Icon = "hard-drive",
            Repository = "Real-TTX/ThixSpace",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixspace-update.json",
            WindowsExecutable = "ThixSpace.exe",
            WindowsUninstallKey = "ThixSpace",
            // ThixSpaceSetup.exe --update installs or updates without a window and starts ThixSpace in the tray.
            WindowsQuietInstallArguments = "--update",
            MacBundleName = "ThixSpace.app",
            MacBundleId = "info.schmoldt.thixspace",
            Screenshots =
            [
                new AppScreenshot("main-map-light.png", "Die Speicherkarte"),
                new AppScreenshot("main-sources-light.png", "Quellen"),
                new AppScreenshot("main-analyses-light.png", "Analysen und Veränderungen"),
            ],
        },
        new ThixApp
        {
            Id = "thixsync",
            ShortName = "sync",
            CloudApp = CloudApps.Sync,
            Features = [AppFeature.Cloud],
            Name = "ThixSync",
            Tagline = "Synchronisation und Backup für alle Speicher",
            Description = "ThixSync gleicht Ordner zwischen Computer, USB-Laufwerk, NAS, Server und Cloud ab – bidirektional, als Spiegel oder als " +
                          "Backup mit Versionen. Konflikte erkennt es, Änderungen zeigt es vorher, und viele kleine Dateien kopiert es parallel. " +
                          "SMB, SFTP, FTP, WebDAV, S3, Google Drive, OneDrive, Dropbox und mehr; mit der MatCloud sind Aufträge auf allen Computern gleich.",
            Icon = "refresh",
            Repository = "Real-TTX/ThixSync",
            Feed = FeedKind.ChannelManifest,
            ManifestFileName = "thixsync-update.json",
            WindowsExecutable = "ThixSync.exe",
            WindowsUninstallKey = "ThixSync",
            // ThixSyncSetup.exe --update installs or updates without a window and starts ThixSync in the tray.
            WindowsQuietInstallArguments = "--update",
            MacBundleName = "ThixSync.app",
            MacBundleId = "info.schmoldt.thixsync",
            Screenshots =
            [
                new AppScreenshot("main-jobs-light.png", "Synchronisationen"),
                new AppScreenshot("main-activity-light.png", "Aktivität mit Geschwindigkeit"),
                new AppScreenshot("main-connections-light.png", "Geräte und Cloud-Speicher"),
            ],
        },
        new ThixApp
        {
            Id = "thixtunnel",
            ShortName = "tunnel",
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
            ShortName = "write",
            CloudApp = CloudApps.Write,
            Features = [AppFeature.Cloud],
            Name = "ThixWrite",
            Tagline = "Textverarbeitung für Dokumente, Briefe und Berichte",
            Description = "Schreiben wie in Word: Menüband, Seiten wie auf Papier, Formatvorlagen, Aufzählungen, Suchen und Ersetzen und " +
                          "Export als PDF. Word-Dokumente (.docx) öffnet und speichert ThixWrite direkt – was es noch nicht anzeigt, " +
                          "bleibt beim Speichern erhalten.",
            Icon = "file-text",
            // ThixOffice publishes every app of the suite (ThixWrite, ThixCalc, ThixShow, ThixContacts, ThixMail) into the same rolling releases;
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
