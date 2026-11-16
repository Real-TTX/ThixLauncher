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
            Id = "thixclip",
            Name = "ThixClip",
            Tagline = "Zwischenablage-Verlauf",
            Description = "ThixClip merkt sich, was du kopierst – Texte, Links und Bilder samt erkanntem Text – und holt es mit einem " +
                          "Tastenkürzel zurück. Mit ThixClip.Cloud ist der Verlauf auf all deinen Computern gleich.",
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
            Id = "thixnote",
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
    ];

    /// <summary>The app with this id, or null.</summary>
    public static ThixApp? Find(string appId) =>
        Apps.FirstOrDefault(app => string.Equals(app.Id, appId, StringComparison.OrdinalIgnoreCase));
}
