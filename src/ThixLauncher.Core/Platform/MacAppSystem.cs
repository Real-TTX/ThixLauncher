using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Platform;

/// <summary>
/// macOS: a Thix app is an app bundle (ThixClip.app) in /Applications or ~/Applications; the launcher finds it there and
/// reads its version from Info.plist. Installing unpacks the package (a zip of the bundle with ditto, or a disk image with
/// hdiutil), ends a running copy, swaps the bundles (the old one comes back if that fails) and removes the quarantine flag.
/// Removing ends the app and deletes the bundle and its LaunchAgent (autostart); the user data stays.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacAppSystem : IAppSystem
{
    private static readonly string SystemApplications = "/Applications";

    private static string UserApplications => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications");

    public bool IsSupported => true;

    public InstalledApp? Detect(ThixApp app)
    {
        foreach (var folder in new[] { SystemApplications, UserApplications })
        {
            var bundle = Path.Combine(folder, app.MacBundleName);
            if (!Directory.Exists(bundle))
            {
                continue;
            }

            var info = ReadInfo(Path.Combine(bundle, "Contents", "Info.plist"));
            var version = InstallInfo.BundleVersion(app, info);
            return new InstalledApp(version, bundle, bundle);
        }

        return null;
    }

    public async Task InstallAsync(ThixApp app, string packageFile, InstalledApp? installed, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var work = Path.Combine(Path.GetDirectoryName(packageFile)!, "unpacked");
        DeleteFolder(work);
        Directory.CreateDirectory(work);

        progress.Report("Paket entpacken");
        await Task.Run(() => Unpack(packageFile, work), cancellationToken);
        var newBundle = Directory.GetDirectories(work, "*.app").OrderByDescending(path => Path.GetFileName(path) == app.MacBundleName).FirstOrDefault()
                        ?? throw new AppSystemException($"Das Paket enthält keine {app.MacBundleName}.");

        var target = installed?.Location is { Length: > 0 } location ? location : Path.Combine(TargetFolder(), app.MacBundleName);
        var wasRunning = await QuitAsync(app, target, progress);

        progress.Report(installed is null ? $"{app.Name} nach {Path.GetDirectoryName(target)} kopieren" : $"{app.Name} ersetzen");
        await Task.Run(() => Replace(target, newBundle), cancellationToken);
        DeleteFolder(work);

        if (wasRunning)
        {
            Run("/usr/bin/open", target);
        }
    }

    public async Task UninstallAsync(ThixApp app, InstalledApp installed, IProgress<string> progress, CancellationToken cancellationToken)
    {
        await QuitAsync(app, installed.Location, progress);
        progress.Report($"{app.MacBundleName} löschen");
        await Task.Run(() => Directory.Delete(installed.Location, recursive: true), cancellationToken);

        if (app.MacBundleId is { Length: > 0 } bundleId)
        {
            var launchAgent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", bundleId + ".plist");
            if (File.Exists(launchAgent))
            {
                File.Delete(launchAgent);
                progress.Report("Autostart entfernt");
            }
        }
    }

    public void Launch(ThixApp app, InstalledApp installed) => Run("/usr/bin/open", installed.Location);

    /// <summary>/Applications when this user may write there (an administrator), otherwise ~/Applications.</summary>
    private static string TargetFolder()
    {
        try
        {
            var probe = Path.Combine(SystemApplications, $".thixlauncher-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return SystemApplications;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Directory.CreateDirectory(UserApplications);
            return UserApplications;
        }
    }

    private static void Unpack(string packageFile, string work)
    {
        if (packageFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            Run("/usr/bin/ditto", "-x", "-k", packageFile, work);
            return;
        }

        var mountPoint = Path.Combine(work, ".mount");
        Directory.CreateDirectory(mountPoint);
        Run("/usr/bin/hdiutil", "attach", "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mountPoint, packageFile);
        try
        {
            foreach (var bundle in Directory.GetDirectories(mountPoint, "*.app"))
            {
                Run("/usr/bin/ditto", bundle, Path.Combine(work, Path.GetFileName(bundle)));
            }
        }
        finally
        {
            Run("/usr/bin/hdiutil", "detach", mountPoint, "-force");
        }
    }

    /// <summary>The old bundle goes aside, the new one takes its place; when that fails, the old one comes back.</summary>
    private static void Replace(string target, string newBundle)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var old = target + ".old";
        DeleteFolder(old);
        var hadOld = Directory.Exists(target);
        if (hadOld)
        {
            Directory.Move(target, old);
        }

        try
        {
            Run("/usr/bin/ditto", newBundle, target);
        }
        catch
        {
            DeleteFolder(target);
            if (hadOld)
            {
                Directory.Move(old, target);
            }

            throw;
        }

        DeleteFolder(old);
        TryRun("/usr/bin/xattr", "-dr", "com.apple.quarantine", target);
    }

    /// <summary>Asks a running app to quit (like ⌘Q), waits up to 15 s, then ends it. True when it was running.</summary>
    private static async Task<bool> QuitAsync(ThixApp app, string bundle, IProgress<string> progress)
    {
        var executables = Path.Combine(bundle, "Contents", "MacOS") + "/";
        if (!TryRun("/usr/bin/pgrep", "-f", executables))
        {
            return false;
        }

        progress.Report($"{app.Name} beenden");
        if (app.MacBundleId is { Length: > 0 } bundleId)
        {
            TryRun("/usr/bin/osascript", "-e", $"if application id \"{bundleId}\" is running then tell application id \"{bundleId}\" to quit");
        }

        for (var waited = 0; waited < 30 && TryRun("/usr/bin/pgrep", "-f", executables); waited++)
        {
            await Task.Delay(500);
        }

        TryRun("/usr/bin/pkill", "-f", executables);
        return true;
    }

    /// <summary>The keys of Info.plist (a binary plist too: plutil turns it into JSON).</summary>
    private static Dictionary<string, string> ReadInfo(string plist)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(plist))
        {
            return values;
        }

        try
        {
            var json = Run("/usr/bin/plutil", "-convert", "json", "-o", "-", plist);
            using var document = JsonDocument.Parse(json);
            foreach (var property in document.RootElement.EnumerateObject().Where(property => property.Value.ValueKind == JsonValueKind.String))
            {
                values[property.Name] = property.Value.GetString() ?? "";
            }
        }
        catch (Exception exception) when (exception is AppSystemException or JsonException)
        {
            Log.Warning($"Could not read {plist}: {exception.Message}");
        }

        return values;
    }

    private static string Run(string program, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new AppSystemException($"{program} konnte nicht gestartet werden.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new AppSystemException($"{Path.GetFileName(program)} meldet: {errors.Trim()}");
        }

        return output.Result;
    }

    private static bool TryRun(string program, params string[] arguments)
    {
        try
        {
            Run(program, arguments);
            return true;
        }
        catch (Exception exception) when (exception is AppSystemException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void DeleteFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
