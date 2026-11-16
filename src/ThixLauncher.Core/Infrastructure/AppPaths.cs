namespace ThixLauncher.Core.Infrastructure;

/// <summary>
/// Where ThixLauncher keeps its files. Program files and user data are separate: updates and uninstalling never touch the
/// user data unless the user decides so.
///   Windows: %LocalAppData%\ThixLauncher (settings, downloads, updates, logs)
///   macOS:   ~/Library/Application Support/ThixLauncher
/// </summary>
public static class AppPaths
{
    /// <summary>Developers can point ThixLauncher to a separate folder (sample data, screenshots) with this variable.</summary>
    public const string DataDirectoryVariable = "THIXLAUNCHER_DATA_DIR";

    private static string? _dataDirectoryOverride = NullIfEmpty(Environment.GetEnvironmentVariable(DataDirectoryVariable));

    public static string DataDirectory => _dataDirectoryOverride ?? Path.Combine(UserFolder(), AppInfo.ProductName);

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>The packages of the apps while they are installed (emptied after every installation).</summary>
    public static string DownloadDirectory => Path.Combine(DataDirectory, "downloads");

    /// <summary>The new versions of ThixLauncher itself.</summary>
    public static string UpdateDirectory => Path.Combine(DataDirectory, "updates");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>True when a developer or a test pointed ThixLauncher to its own folder: it is then its own instance.</summary>
    public static bool HasOverride => _dataDirectoryOverride is not null;

    /// <summary>Tests use a temporary folder.</summary>
    internal static void UseDirectoryForTests(string? directory) => _dataDirectoryOverride = directory;

    private static string UserFolder() => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
