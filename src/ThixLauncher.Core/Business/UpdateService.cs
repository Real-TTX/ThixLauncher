using System.Diagnostics;
using System.Runtime.InteropServices;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Business;

public sealed record UpdateCheckResult(bool IsUpdateAvailable, string Message, UpdateManifest? Manifest = null);

/// <summary>
/// Over-the-air updates of ThixLauncher itself from GitHub releases. Each channel has one rolling release whose files every
/// build replaces: "release-channel" (branch main) and "nightly-channel" (branch dev), with the manifest
/// thixlauncher-update.json. A build only looks at its own channel; local builds never update themselves.
///
/// Windows: the package is the new ThixLauncher.exe itself, started with "--update" (InstallService): it ends the running
/// ThixLauncher, replaces the program file and starts it again in the tray.
/// macOS: the package is the zipped ThixLauncher.app; a small script replaces the bundle once ThixLauncher has ended and
/// opens it again (<see cref="MacAppBundle"/>). The user data stays as it is on both.
/// </summary>
public static class UpdateService
{
    public const string ManifestFileName = "thixlauncher-update.json";

    /// <summary>The Windows program started by an update installs itself silently (InstallService).</summary>
    public const string WindowsUpdateArgument = "--update";

    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    public static DateTimeOffset? LastCheck { get; private set; }

    public static UpdateCheckResult? LastResult { get; private set; }

    /// <summary>The package name of this platform: win-x64, osx-arm64 or osx-x64 (the apps use the same keys).</summary>
    public static string PlatformKey { get; } =
        (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" +
        (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64");

    public static string ReleasePageUrl(string channel) => $"https://github.com/{AppInfo.Repository}/releases/tag/{channel}-channel";

    /// <summary>A Mac app only updates itself as an app bundle (not when started with "dotnet run").</summary>
    public static bool CanUpdate => !AppInfo.IsLocalBuild && !AccessPolicy.AreUpdatesDisabled &&
                                    (OperatingSystem.IsWindows() || (OperatingSystem.IsMacOS() && MacAppBundle.Current is not null));

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        AccessPolicy.EnsureUpdatesAllowed();
        if (AppInfo.IsLocalBuild)
        {
            return Remember(new UpdateCheckResult(false, "Lokaler Build – keine automatischen Updates."));
        }

        try
        {
            var manifest = await ReleaseFeed.ReadManifestAsync(AppInfo.Repository, AppInfo.Channel, ManifestFileName, cancellationToken);
            if (manifest is null)
            {
                return Remember(new UpdateCheckResult(false, "Im Kanal gibt es noch keine Version."));
            }

            if (!IsNewer(manifest, AppInfo.Channel, AppInfo.BuildNumber))
            {
                return Remember(new UpdateCheckResult(false, "ThixLauncher ist aktuell."));
            }

            return Remember(manifest.PackageFor(PlatformKey) is null
                ? new UpdateCheckResult(false, $"Version {manifest.Version} gibt es noch nicht für dieses System ({PlatformKey}).")
                : new UpdateCheckResult(true, $"Version {manifest.Version} ist verfügbar.", manifest));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Warning($"Update check failed: {exception.Message}");
            return Remember(new UpdateCheckResult(false, "Update-Prüfung fehlgeschlagen – keine Verbindung zu GitHub?"));
        }
    }

    /// <summary>Same channel and a higher build number (build numbers only grow).</summary>
    public static bool IsNewer(UpdateManifest manifest, string channel, int buildNumber) =>
        string.Equals(manifest.Channel, channel, StringComparison.OrdinalIgnoreCase) && manifest.BuildNumber > buildNumber;

    /// <summary>
    /// Downloads the new version, checks its SHA-256 and starts its installation. The caller ends the application right
    /// afterwards (Windows: the update would end it anyway; macOS: the bundle is replaced once it has ended).
    /// </summary>
    public static async Task DownloadAndStartInstallerAsync(UpdateManifest manifest, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default, bool showWindowAfter = false)
    {
        AccessPolicy.EnsureUpdatesAllowed();
        var package = manifest.PackageFor(PlatformKey);
        if (package is null || !ReleaseFeed.IsSafeFileName(package.File) || package.Sha256.Length != 64)
        {
            throw new BusinessException("Die Update-Information ist ungültig.");
        }

        var packagePath = Path.Combine(AppPaths.UpdateDirectory, package.File);
        var url = $"https://github.com/{AppInfo.Repository}/releases/download/{manifest.Channel}-channel/{package.File}";
        try
        {
            // on a worker thread: creating and closing a freshly written program can wait for the virus scanner
            await Task.Run(() => ReleaseFeed.DownloadAsync(url, packagePath, package.Sha256, progress, cancellationToken), CancellationToken.None);
        }
        catch (FeedException exception)
        {
            throw new BusinessException(exception.Message);
        }

        Log.Info($"Starting update to {manifest.Version}");
        // Before the new program starts: on Windows it ends this one at once.
        if (showWindowAfter)
        {
            ShowWindowAfterUpdate();
        }

        if (OperatingSystem.IsMacOS())
        {
            // unpacking takes a moment: not on the UI thread
            await Task.Run(() =>
            {
                if (OperatingSystem.IsMacOS())
                {
                    MacAppBundle.StartReplacement(packagePath);
                }
            }, cancellationToken);
            return;
        }

        // Windows checks a freshly downloaded program inside CreateProcess, which can take seconds: off the UI thread.
        await Task.Run(() => Process.Start(new ProcessStartInfo(packagePath, WindowsUpdateArgument)
        {
            UseShellExecute = false,
            WorkingDirectory = AppPaths.UpdateDirectory,
        })?.Dispose(), cancellationToken);
    }

    /// <summary>
    /// The update was asked for in the window (or the window was open): the ThixLauncher the update starts again shows its window instead
    /// of waiting in the tray. A marker in the update folder carries this over the restart (the restart itself always says "--tray").
    /// Only ThixLauncher's own update: how it starts the apps it installs is not touched.
    /// </summary>
    public static void ShowWindowAfterUpdate()
    {
        Directory.CreateDirectory(AppPaths.UpdateDirectory);
        File.WriteAllText(ShowMarker, DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>True once after an update that should show the window (a marker older than 15 minutes is stale); the marker goes.</summary>
    public static bool TakeShowWindowAfterUpdate()
    {
        if (!File.Exists(ShowMarker))
        {
            return false;
        }

        try
        {
            var written = File.GetLastWriteTimeUtc(ShowMarker);
            File.Delete(ShowMarker);
            return DateTime.UtcNow - written < TimeSpan.FromMinutes(15);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ShowMarker => Path.Combine(AppPaths.UpdateDirectory, "show-window-after-update");

    private static UpdateCheckResult Remember(UpdateCheckResult result)
    {
        LastCheck = AppClock.Now;
        LastResult = result;
        return result;
    }
}
