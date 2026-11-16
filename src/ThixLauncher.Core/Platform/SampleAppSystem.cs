using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Platform;

/// <summary>
/// A computer in memory, for the tests, the self test and the snapshots (<see cref="AppSystem.UseForTests"/>): installing
/// reads the sample package (<see cref="SampleGitHub"/>) and notes the app with its version, removing forgets it, starting
/// is counted. Nothing on the real computer is touched. Never used by the real app.
/// </summary>
internal sealed class SampleAppSystem : IAppSystem
{
    /// <summary>The folder the sample apps "live" in (only a name: nothing is written there).</summary>
    public string ProgramsFolder { get; init; } = OperatingSystem.IsMacOS() ? "/Applications" : @"C:\Users\Anna\AppData\Local\Programs";

    public Dictionary<string, InstalledApp> Installed { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The apps started, in order.</summary>
    public List<string> Launched { get; } = [];

    /// <summary>Simulates a setup the user closed without installing (the setups that ask).</summary>
    public bool CancelSetups { get; set; }

    /// <summary>Holds every installation until it is released (tests of the busy state).</summary>
    public TaskCompletionSource? Hold { get; set; }

    public bool IsSupported => true;

    /// <summary>Notes an app as installed (the sample state).</summary>
    public void Add(ThixApp app, string version)
    {
        var location = Path.Combine(ProgramsFolder, OperatingSystem.IsMacOS() ? app.MacBundleName : app.Name);
        var executable = OperatingSystem.IsMacOS() ? location : Path.Combine(location, app.WindowsExecutable);
        Installed[app.Id] = new InstalledApp(version, location, executable, $"\"{executable}\" --uninstall", $"\"{executable}\" --uninstall --quiet");
    }

    public InstalledApp? Detect(ThixApp app)
    {
        lock (Installed)
        {
            return Installed.GetValueOrDefault(app.Id);
        }
    }

    public async Task InstallAsync(ThixApp app, string packageFile, InstalledApp? installed, IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report(installed is null ? $"{app.Name} wird installiert" : $"{app.Name} wird aktualisiert");
        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        if (CancelSetups && !app.InstallsQuietly)
        {
            return;
        }

        var package = SampleGitHub.ReadPackage(packageFile) ?? throw new AppSystemException("Das Paket ist kein Beispielpaket.");
        if (package.AppId != app.Id)
        {
            throw new AppSystemException($"Das Paket gehört zu {package.AppId}.");
        }

        lock (Installed)
        {
            Add(app, package.Version);
        }
    }

    public Task UninstallAsync(ThixApp app, InstalledApp installed, IProgress<string> progress, CancellationToken cancellationToken)
    {
        progress.Report($"{app.Name} wird entfernt");
        lock (Installed)
        {
            Installed.Remove(app.Id);
        }

        return Task.CompletedTask;
    }

    public void Launch(ThixApp app, InstalledApp installed)
    {
        lock (Launched)
        {
            Launched.Add(app.Id);
        }
    }
}
