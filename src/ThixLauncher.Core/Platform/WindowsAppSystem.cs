using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Platform;

/// <summary>
/// Windows: every Thix app installs itself per user (no administrator rights) and writes its entry under
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\&lt;app&gt; – that entry is how the launcher finds it (version,
/// folder, uninstaller). Installing runs the app's own setup: without a window when the app knows how
/// (<see cref="ThixApp.WindowsQuietInstallArguments"/>), otherwise the setup opens and the launcher waits for it. Removing
/// runs the uninstaller of the entry, the quiet one when there is one.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsAppSystem : IAppSystem
{
    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>A setup without a window that takes longer than this is stuck.</summary>
    private static readonly TimeSpan QuietTimeout = TimeSpan.FromMinutes(10);

    public bool IsSupported => true;

    public InstalledApp? Detect(ThixApp app)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey($@"{UninstallRoot}\{app.WindowsUninstallKey}");
            if (key is null)
            {
                continue;
            }

            var location = (key.GetValue("InstallLocation") as string)?.Trim().Trim('"');
            var executable = location is { Length: > 0 } ? Path.Combine(location, app.WindowsExecutable) : null;
            if (executable is null || !File.Exists(executable))
            {
                executable = InstallInfo.DisplayIconPath(key.GetValue("DisplayIcon") as string) is { } icon && File.Exists(icon) ? icon : null;
            }

            if (location is not { Length: > 0 } && executable is not null)
            {
                location = Path.GetDirectoryName(executable);
            }

            return new InstalledApp(
                key.GetValue("DisplayVersion") as string,
                location ?? "",
                executable,
                key.GetValue("UninstallString") as string,
                key.GetValue("QuietUninstallString") as string);
        }

        return null;
    }

    public async Task InstallAsync(ThixApp app, string packageFile, InstalledApp? installed, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!packageFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new AppSystemException($"Das Paket von {app.Name} ist kein Windows-Setup.");
        }

        var quiet = app.WindowsQuietInstallArguments;
        var startInfo = new ProcessStartInfo(packageFile)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(packageFile)!,
            CreateNoWindow = quiet is not null,
        };
        foreach (var argument in CommandLine.Arguments(quiet))
        {
            startInfo.ArgumentList.Add(argument);
        }

        progress.Report(quiet is null
            ? $"Das Setup von {app.Name} ist geöffnet – bitte dort abschließen"
            : installed is null ? $"{app.Name} wird installiert" : $"{app.Name} wird aktualisiert");
        var exitCode = await RunAsync(startInfo, quiet is null ? null : QuietTimeout, app, cancellationToken);
        if (exitCode != 0)
        {
            throw new AppSystemException($"Das Setup von {app.Name} endete mit dem Fehler {exitCode}.");
        }
    }

    public async Task UninstallAsync(ThixApp app, InstalledApp installed, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var isQuiet = installed.QuietUninstallCommand is { Length: > 0 };
        var command = CommandLine.Split(isQuiet ? installed.QuietUninstallCommand : installed.UninstallCommand)
                      ?? throw new AppSystemException($"{app.Name} hat kein Deinstallationsprogramm eingetragen – bitte unter „Installierte Apps“ entfernen.");
        var startInfo = new ProcessStartInfo(command.Program) { UseShellExecute = false, CreateNoWindow = isQuiet };
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        progress.Report(isQuiet ? $"{app.Name} wird entfernt" : $"Die Deinstallation von {app.Name} ist geöffnet – bitte dort bestätigen");
        var exitCode = await RunAsync(startInfo, isQuiet ? QuietTimeout : null, app, cancellationToken);
        if (exitCode != 0)
        {
            throw new AppSystemException($"Die Deinstallation von {app.Name} endete mit dem Fehler {exitCode}.");
        }
    }

    public void Launch(ThixApp app, InstalledApp installed)
    {
        var executable = installed.Executable ?? throw new AppSystemException($"Die Programmdatei von {app.Name} wurde nicht gefunden.");
        try
        {
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
            })?.Dispose();
        }
        catch (Win32Exception exception)
        {
            throw new AppSystemException($"{app.Name} konnte nicht gestartet werden: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Starts the program off the calling thread (Windows checks a fresh download inside CreateProcess, which can take a
    /// while) and waits for its end; null timeout = as long as the user needs.
    /// </summary>
    private static async Task<int> RunAsync(ProcessStartInfo startInfo, TimeSpan? timeout, ThixApp app, CancellationToken cancellationToken)
    {
        Process process;
        try
        {
            process = await Task.Run(() => Process.Start(startInfo), cancellationToken)
                      ?? throw new AppSystemException($"Das Programm von {app.Name} konnte nicht gestartet werden.");
        }
        catch (Win32Exception exception)
        {
            throw new AppSystemException($"Das Programm von {app.Name} konnte nicht gestartet werden: {exception.Message}", exception);
        }

        using (process)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout is { } span)
            {
                limit.CancelAfter(span);
            }

            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning($"{Path.GetFileName(startInfo.FileName)} of {app.Id} did not end within {timeout}");
                throw new AppSystemException($"Das Programm von {app.Name} reagiert nicht mehr.");
            }

            Log.Info($"{Path.GetFileName(startInfo.FileName)} of {app.Id} ended with {process.ExitCode}");
            return process.ExitCode;
        }
    }
}
