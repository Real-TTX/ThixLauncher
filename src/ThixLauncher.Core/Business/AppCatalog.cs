using MatCloud.Contract;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Business;

/// <summary>
/// The Thix apps the launcher knows. A new app is one entry here (and its logo in the app's Assets/apps folder): where it
/// publishes its builds, how its setup runs on Windows and how its bundle is called on macOS.
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
            // ThixOffice publishes every app of the suite (ThixWrite, ThixCalc, ThixShow) into the same rolling releases;
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
            // ThixOffice publishes every app of the suite (ThixWrite, ThixCalc, ThixShow) into the same rolling releases; the
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
            // ThixOffice publishes every app of the suite (ThixWrite, ThixCalc, ThixShow) into the same rolling releases;
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
        },
    ];

    /// <summary>The app with this id, or null.</summary>
    public static ThixApp? Find(string appId) =>
        Apps.FirstOrDefault(app => string.Equals(app.Id, appId, StringComparison.OrdinalIgnoreCase));
}
