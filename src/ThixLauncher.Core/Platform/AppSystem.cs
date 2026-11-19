using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Platform;

/// <summary>
/// What the operating system does for the launcher: find an installed app, run its package, remove it and start it. One
/// implementation per platform (<see cref="WindowsAppSystem"/>, <see cref="MacAppSystem"/>); the business layer
/// (<see cref="Business.AppService"/>) decides what may happen and calls it. Tests put a fake system in its place.
/// </summary>
internal interface IAppSystem
{
    /// <summary>The launcher can install apps on this system.</summary>
    bool IsSupported { get; }

    /// <summary>The installation of <paramref name="app"/>, or null when it is not installed.</summary>
    InstalledApp? Detect(ThixApp app);

    /// <summary>
    /// Installs or updates <paramref name="app"/> from the downloaded (and checked) package, run as <paramref name="setup"/> says
    /// (the business layer builds it: the arguments of the setup on Windows, the call of --configure on macOS).
    /// </summary>
    Task InstallAsync(ThixApp app, string packageFile, InstalledApp? installed, AppSetupCommand setup, IProgress<string> progress, CancellationToken cancellationToken);

    /// <summary>Removes <paramref name="app"/>; its user data stays.</summary>
    Task UninstallAsync(ThixApp app, InstalledApp installed, IProgress<string> progress, CancellationToken cancellationToken);

    /// <summary>Starts the installed app (or brings its window to the front: every Thix app runs once per user).</summary>
    void Launch(ThixApp app, InstalledApp installed);
}

/// <summary>The system of this computer (or of a test).</summary>
internal static class AppSystem
{
    private static IAppSystem? _forTests;

    public static IAppSystem Current => _forTests ?? Native.Value;

    private static readonly Lazy<IAppSystem> Native = new(() =>
        OperatingSystem.IsWindows() ? new WindowsAppSystem()
        : OperatingSystem.IsMacOS() ? new MacAppSystem()
        : new UnsupportedAppSystem());

    /// <summary>Tests put a system of their own in place; null = the real one again.</summary>
    internal static void UseForTests(IAppSystem? system) => _forTests = system;

    /// <summary>Linux and the rest: the launcher shows the apps, but installs nothing.</summary>
    private sealed class UnsupportedAppSystem : IAppSystem
    {
        public bool IsSupported => false;

        public InstalledApp? Detect(ThixApp app) => null;

        public Task InstallAsync(ThixApp app, string packageFile, InstalledApp? installed, AppSetupCommand setup, IProgress<string> progress, CancellationToken cancellationToken) =>
            throw new PlatformNotSupportedException();

        public Task UninstallAsync(ThixApp app, InstalledApp installed, IProgress<string> progress, CancellationToken cancellationToken) =>
            throw new PlatformNotSupportedException();

        public void Launch(ThixApp app, InstalledApp installed) => throw new PlatformNotSupportedException();
    }
}

/// <summary>A program that was run for an app (setup, uninstaller) ended with an error or could not be started.</summary>
internal sealed class AppSystemException(string message, Exception? inner = null) : Exception(message, inner);
