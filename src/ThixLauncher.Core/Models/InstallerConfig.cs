using System.Text.Json.Serialization;

namespace ThixLauncher.Core.Models;

/// <summary>
/// What the setup of a Thix app offers (SETUP-08 of MatDesign): <c>installer.json</c> of the app, embedded in its program and
/// published as the block <c>"installer"</c> of its update manifest. ThixLauncher reads it twice: its own (embedded, for its
/// own setup) and the one of every app it installs (from the manifest, to ask the same questions before a quiet install).
/// An option that is null is not offered.
/// </summary>
public sealed record InstallerConfig
{
    /// <summary>A shortcut on the desktop (Windows; the Start menu entry is always made).</summary>
    [JsonPropertyName("desktopShortcut")]
    public InstallerSwitch? DesktopShortcut { get; init; }

    /// <summary>Start at sign-in (Windows: the Run key, macOS: a LaunchAgent).</summary>
    [JsonPropertyName("autostart")]
    public InstallerSwitch? Autostart { get; init; }

    /// <summary>Install updates without asking (full auto update).</summary>
    [JsonPropertyName("automaticUpdates")]
    public InstallerSwitch? AutomaticUpdates { get; init; }

    /// <summary>The file types the app can open; each one may be associated with it.</summary>
    [JsonPropertyName("fileTypes")]
    public IReadOnlyList<InstallerFileType> FileTypes { get; init; } = [];
}

/// <summary>An option that is on or off; <see cref="Default"/> is what a quiet install does when it is not given.</summary>
public sealed record InstallerSwitch
{
    [JsonPropertyName("default")]
    public bool Default { get; init; }
}

/// <summary>A file type the app opens: ".md", its name ("Markdown-Notiz") and whether it is associated by default.</summary>
public sealed record InstallerFileType
{
    [JsonPropertyName("extension")]
    public string Extension { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("default")]
    public bool Default { get; init; }
}

/// <summary>
/// The choices of one install: a value per option, null = not given (a first install takes the default of the
/// <see cref="InstallerConfig"/>, an installed app keeps what it has). <see cref="FileTypes"/> lists the extensions to associate.
/// </summary>
public sealed record InstallChoices(bool? DesktopShortcut = null, bool? Autostart = null, bool? AutomaticUpdates = null, IReadOnlyList<string>? FileTypes = null)
{
    /// <summary>These choices where given, <paramref name="fallback"/> where not.</summary>
    public InstallChoices Over(InstallChoices fallback) => new(
        DesktopShortcut ?? fallback.DesktopShortcut,
        Autostart ?? fallback.Autostart,
        AutomaticUpdates ?? fallback.AutomaticUpdates,
        FileTypes ?? fallback.FileTypes);

    /// <summary>Equal by value (the list of file types too).</summary>
    public bool Equals(InstallChoices? other) =>
        other is not null && DesktopShortcut == other.DesktopShortcut && Autostart == other.Autostart && AutomaticUpdates == other.AutomaticUpdates &&
        (FileTypes is null ? other.FileTypes is null : other.FileTypes is not null && FileTypes.SequenceEqual(other.FileTypes));

    public override int GetHashCode() => HashCode.Combine(DesktopShortcut, Autostart, AutomaticUpdates, FileTypes?.Count);
}

/// <summary>What the command line of ThixLauncher asks for (SETUP-05).</summary>
public enum SetupAction
{
    /// <summary>No setup argument: the program (or, started outside its folder, the setup assistant).</summary>
    None,

    /// <summary>"--quiet": install without a window.</summary>
    Install,

    /// <summary>"--update": update without a window (an installed launcher keeps its choices).</summary>
    Update,

    /// <summary>"--configure": apply the choices to the installed launcher, nothing is copied.</summary>
    Configure,

    /// <summary>"--uninstall" (with "--quiet" without a window).</summary>
    Uninstall,
}

/// <summary>The setup part of a command line: what to do, with which choices, and whether to start the app afterwards ("--start").</summary>
public sealed record SetupRequest(SetupAction Action, InstallChoices Choices, bool Quiet = false, bool Start = false);

/// <summary>
/// How the platform runs the package of an app the launcher installs (built by <see cref="Business.AppInstallOptions"/>, so
/// tests see it): on Windows the arguments of the app's setup and whether it runs without a window; on macOS the arguments of
/// <c>&lt;App&gt;.app/Contents/MacOS/&lt;App&gt;</c> after the bundle was copied (null: it is not called).
/// </summary>
public sealed record AppSetupCommand(IReadOnlyList<string> WindowsArguments, bool WindowsQuiet, IReadOnlyList<string>? MacConfigureArguments = null);
