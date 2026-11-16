namespace ThixLauncher.Core.Models;

/// <summary>Where the builds of an app are published (both on GitHub releases).</summary>
public enum FeedKind
{
    /// <summary>
    /// One rolling release per channel ("release-channel", "nightly-channel") with a JSON manifest that names the package
    /// of every platform and its SHA-256 (ThixClip, ThixLauncher itself).
    /// </summary>
    ChannelManifest,

    /// <summary>
    /// One release per build: "v&lt;version&gt;" for release, a pre-release "nightly-…" for nightly (ThixNote). The package of a
    /// platform is found by the pattern of its file name; GitHub's digest of the file is its SHA-256.
    /// </summary>
    GitHubReleases,
}

/// <summary>
/// An app of the Thix family the launcher installs, updates, removes and starts (the catalog: <see cref="Business.AppCatalog"/>).
/// Everything the launcher needs to know about an app is data here; the platforms do the rest the same way for every app.
/// </summary>
public sealed record ThixApp
{
    /// <summary>The key of the app ("thixclip"); used in settings, logs and the download folder.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>One line: what it is ("Zwischenablage-Verlauf").</summary>
    public required string Tagline { get; init; }

    /// <summary>A few sentences for the detail page.</summary>
    public required string Description { get; init; }

    /// <summary>The icon of MatDesign's sprite that stands for the app where its logo is too big (filters, tray).</summary>
    public required string Icon { get; init; }

    /// <summary>owner/name of the GitHub repository that publishes the builds.</summary>
    public required string Repository { get; init; }

    public required FeedKind Feed { get; init; }

    /// <summary>The manifest of <see cref="FeedKind.ChannelManifest"/> ("thixclip-update.json").</summary>
    public string? ManifestFileName { get; init; }

    /// <summary>
    /// <see cref="FeedKind.GitHubReleases"/>: per platform key ("win-x64", "osx-arm64", "osx-x64") a regular expression for the
    /// file name of the package. A platform without a pattern has no package.
    /// </summary>
    public IReadOnlyDictionary<string, string> PackagePatterns { get; init; } = new Dictionary<string, string>();

    /// <summary>Windows: the program file in the install folder ("ThixClip.exe").</summary>
    public required string WindowsExecutable { get; init; }

    /// <summary>Windows: the name of the entry under ...\CurrentVersion\Uninstall (the app writes it, per user).</summary>
    public required string WindowsUninstallKey { get; init; }

    /// <summary>
    /// Windows: the arguments with which the app's setup installs or updates without a window ("--update"); null = the setup
    /// of the app opens and asks (the launcher waits until it is closed).
    /// </summary>
    public string? WindowsQuietInstallArguments { get; init; }

    /// <summary>macOS: the app bundle ("ThixClip.app") in /Applications or ~/Applications.</summary>
    public required string MacBundleName { get; init; }

    /// <summary>macOS: the bundle identifier; its LaunchAgent (autostart) goes with the app when it is removed.</summary>
    public string? MacBundleId { get; init; }

    /// <summary>The page of the project.</summary>
    public string ProjectUrl => $"https://github.com/{Repository}";

    /// <summary>True when the app installs and updates without asking on this system.</summary>
    public bool InstallsQuietly => !OperatingSystem.IsWindows() || WindowsQuietInstallArguments is not null;
}
