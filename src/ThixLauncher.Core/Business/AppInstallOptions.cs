using ThixLauncher.Core.Models;
using ThixLauncher.Core.Platform;

namespace ThixLauncher.Core.Business;

/// <summary>
/// The install options of the apps the launcher installs (SETUP-08 of MatDesign). An app whose update manifest carries the
/// block <c>"installer"</c> says what its setup offers; before its first install the launcher asks the same questions (the UI
/// gets them from <see cref="AppService.InstallOptionsOf"/>) and passes the answers on the command line of the contract:
/// <list type="bullet">
/// <item>Windows: <c>&lt;App&gt;Setup.exe --quiet --desktop-shortcut=yes|no --autostart=yes|no --automatic-updates=yes|no
/// --file-types=.a,.b|none [--start]</c> (<c>--start</c> when it starts at sign-in, so it runs right away).</item>
/// <item>macOS: after the bundle was copied <c>&lt;App&gt;.app/Contents/MacOS/&lt;App&gt; --configure …</c>.</item>
/// </list>
/// Updates, and apps without the block (older builds, apps of single GitHub releases), keep the old way exactly
/// (<see cref="ThixApp.WindowsQuietInstallArguments"/>, e.g. "--update"; on macOS nothing is called): an installed app keeps its choices.
/// </summary>
public static class AppInstallOptions
{
    /// <summary>
    /// What the launcher asks on this system: the offered options of <paramref name="config"/> without those that do not apply
    /// here (the desktop shortcut is a Windows thing). Null when nothing is left to ask.
    /// </summary>
    public static InstallerConfig? ForThisSystem(InstallerConfig config)
    {
        var offered = OperatingSystem.IsWindows() ? config : config with { DesktopShortcut = null };
        return offered is { DesktopShortcut: null, Autostart: null, AutomaticUpdates: null, FileTypes.Count: 0 } ? null : offered;
    }

    /// <summary>
    /// How the platform runs the package of <paramref name="app"/>: <paramref name="installer"/> is the block of its manifest
    /// (null: the app does not know the contract), <paramref name="choices"/> the answers of the user (null or a value of null:
    /// the default of the block). Only options the app offers are passed – it refuses any other with exit code 2.
    /// </summary>
    public static AppSetupCommand SetupFor(ThixApp app, InstallerConfig? installer, InstallChoices? choices, bool isUpdate)
    {
        var legacy = new AppSetupCommand(CommandLine.Arguments(app.WindowsQuietInstallArguments), app.WindowsQuietInstallArguments is not null);
        if (isUpdate || installer is null)
        {
            return legacy;
        }

        var wanted = Offered(choices ?? new InstallChoices(), installer).Over(InstallerOptions.DefaultsOf(installer));
        var windows = new List<string> { InstallerOptions.QuietArgument };
        windows.AddRange(InstallerOptions.ToArguments(wanted));
        if (wanted.Autostart == true)
        {
            windows.Add(InstallerOptions.StartArgument);
        }

        var mac = new List<string> { InstallerOptions.ConfigureArgument };
        mac.AddRange(InstallerOptions.ToArguments(wanted with { DesktopShortcut = null }));
        return new AppSetupCommand(windows, WindowsQuiet: true, mac);
    }

    /// <summary>Only what <paramref name="installer"/> offers; file types by the spelling of the block, each once.</summary>
    private static InstallChoices Offered(InstallChoices choices, InstallerConfig installer) => new(
        installer.DesktopShortcut is null ? null : choices.DesktopShortcut,
        installer.Autostart is null ? null : choices.Autostart,
        installer.AutomaticUpdates is null ? null : choices.AutomaticUpdates,
        installer.FileTypes.Count == 0 || choices.FileTypes is null
            ? null
            : installer.FileTypes
                .Where(type => choices.FileTypes.Any(wanted => Same(wanted, type.Extension)))
                .Select(type => type.Extension)
                .ToList());

    private static bool Same(string wanted, string extension) =>
        string.Equals(wanted.StartsWith('.') ? wanted : "." + wanted, extension, StringComparison.OrdinalIgnoreCase);
}
