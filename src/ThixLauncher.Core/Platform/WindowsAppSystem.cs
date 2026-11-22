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
/// folder, uninstaller). Installing runs the app's own setup with the arguments the business layer chose
/// (<see cref="AppSetupCommand"/>: "--quiet" with the install options of SETUP-08, or <see cref="ThixApp.WindowsQuietInstallArguments"/>):
/// without a window when it is quiet – a refusal (exit code 2) comes back with what the setup wrote on stderr –, otherwise
/// the setup opens and the launcher waits for it. Removing
/// runs the uninstaller of the entry, the quiet one when there is one.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsAppSystem : IAppSystem
{
    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>A setup without a window that takes longer than this is stuck.</summary>
    private static readonly TimeSpan QuietTimeout = TimeSpan.FromMinutes(10);

    /// <summary>A setup of the contract (SETUP-08) refuses its arguments with this exit code and says why on stderr.</summary>
    private const int SetupRefusedExitCode = 2;

    public bool IsSupported => true;

    public InstalledApp? Detect(ThixApp app)
    {
        if (app.WindowsMsiUpgradeCode is { } family)
        {
            return WindowsInstaller.Detect(app, family);
        }

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

    public async Task InstallAsync(ThixApp app, string packageFile, InstalledApp? installed, AppSetupCommand setup, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (app.WindowsMsiUpgradeCode is { } family)
        {
            WindowsInstaller.VerifyPackage(app, packageFile, family);
            progress.Report(installed is null ? $"{app.Name} wird installiert" : $"{app.Name} wird aktualisiert");
            await RunMsiAsync(app, "/i", Path.GetFullPath(packageFile), progress, cancellationToken);
            return;
        }

        if (!packageFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new AppSystemException($"Das Paket von {app.Name} ist kein Windows-Setup.");
        }

        var quiet = setup.WindowsQuiet;
        var startInfo = new ProcessStartInfo(packageFile)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(packageFile)!,
            CreateNoWindow = quiet,
            // a quiet setup says on stderr why it refuses its arguments (exit code 2, SETUP-08)
            RedirectStandardError = quiet,
        };
        foreach (var argument in setup.WindowsArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        progress.Report(!quiet
            ? $"Das Setup von {app.Name} ist geöffnet – bitte dort abschließen"
            : installed is null ? $"{app.Name} wird installiert" : $"{app.Name} wird aktualisiert");
        Log.Info($"{Path.GetFileName(packageFile)} {string.Join(' ', setup.WindowsArguments)}");
        var (exitCode, errors) = await RunAsync(startInfo, quiet ? QuietTimeout : null, app, cancellationToken);
        if (exitCode == SetupRefusedExitCode && errors.Length > 0)
        {
            throw new AppSystemException($"Das Setup von {app.Name} lehnt die Optionen ab: {errors}");
        }

        if (exitCode != 0)
        {
            throw new AppSystemException(errors.Length > 0
                ? $"Das Setup von {app.Name} endete mit dem Fehler {exitCode}: {errors}"
                : $"Das Setup von {app.Name} endete mit dem Fehler {exitCode}.");
        }
    }

    public async Task UninstallAsync(ThixApp app, InstalledApp installed, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (app.WindowsMsiUpgradeCode is { } family)
        {
            // Resolve the family again: a major upgrade can replace its ProductCode while the store is open.
            if (WindowsInstaller.FindProduct(family) is { } product)
            {
                progress.Report($"{app.Name} wird entfernt");
                await RunMsiAsync(app, "/x", product, progress, cancellationToken);
            }
            return;
        }

        var isQuiet = installed.QuietUninstallCommand is { Length: > 0 };
        var command = CommandLine.Split(isQuiet ? installed.QuietUninstallCommand : installed.UninstallCommand)
                      ?? throw new AppSystemException($"{app.Name} hat kein Deinstallationsprogramm eingetragen – bitte unter „Installierte Apps“ entfernen.");
        var startInfo = new ProcessStartInfo(command.Program) { UseShellExecute = false, CreateNoWindow = isQuiet };
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        progress.Report(isQuiet ? $"{app.Name} wird entfernt" : $"Die Deinstallation von {app.Name} ist geöffnet – bitte dort bestätigen");
        var (exitCode, _) = await RunAsync(startInfo, isQuiet ? QuietTimeout : null, app, cancellationToken);
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

    private static async Task RunMsiAsync(ThixApp app, string action, string target, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var startInfo = WindowsInstaller.Command(action, target);
        var (exitCode, _) = await RunAsync(startInfo, QuietTimeout, app, cancellationToken);
        if (!WindowsInstaller.Succeeded(exitCode))
        {
            throw new AppSystemException($"Windows Installer konnte den Vorgang für {app.Name} nicht abschließen (Fehler {exitCode}).");
        }

        if (exitCode != 0)
        {
            progress.Report("Windows Installer meldet einen erforderlichen Neustart.");
        }
    }

    /// <summary>
    /// Starts the program off the calling thread (Windows checks a fresh download inside CreateProcess, which can take a
    /// while) and waits for its end; null timeout = as long as the user needs. Returns the exit code and – when stderr is
    /// redirected – what the program wrote there.
    /// </summary>
    private static async Task<(int ExitCode, string Errors)> RunAsync(ProcessStartInfo startInfo, TimeSpan? timeout, ThixApp app, CancellationToken cancellationToken)
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
            var errors = startInfo.RedirectStandardError ? new ErrorText(process.StandardError) : null;
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

            var text = errors is null ? "" : await errors.CollectAsync();
            Log.Info($"{Path.GetFileName(startInfo.FileName)} of {app.Id} ended with {process.ExitCode}{(text.Length > 0 ? ": " + text : "")}");
            return (process.ExitCode, text);
        }
    }

    /// <summary>
    /// Reads stderr while the program runs. A setup may start its app (--start), which can inherit the pipe and keep it open:
    /// so after the end of the setup this waits only a moment for the rest and takes what came.
    /// </summary>
    internal sealed class ErrorText
    {
        private readonly MemoryStream _bytes = new();
        private readonly Task _reading;

        public ErrorText(StreamReader reader) => _reading = Task.Run(async () =>
        {
            var buffer = new byte[1024];
            try
            {
                int read;
                while ((read = await reader.BaseStream.ReadAsync(buffer)) > 0)
                {
                    lock (_bytes)
                    {
                        if (_bytes.Length < 8000)
                        {
                            _bytes.Write(buffer, 0, read);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // the process and its pipe are gone
            }
        });

        public async Task<string> CollectAsync()
        {
            await Task.WhenAny(_reading, Task.Delay(TimeSpan.FromSeconds(1)));
            lock (_bytes)
            {
                return Decode(_bytes.ToArray()).Trim();
            }
        }

        /// <summary>
        /// A Windows program without a console writes stderr in the ANSI code page ("ü" = 0xFC in 1252), one that sets it up in
        /// UTF-8: UTF-8 when the bytes are valid UTF-8, the ANSI code page otherwise.
        /// </summary>
        internal static string Decode(byte[] bytes)
        {
            try
            {
                return new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (System.Text.DecoderFallbackException)
            {
                var ansi = System.Text.CodePagesEncodingProvider.Instance.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
                return (ansi ?? System.Text.Encoding.Latin1).GetString(bytes);
            }
        }
    }
}
