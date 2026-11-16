namespace ThixLauncher.Core.Models;

public enum ThemeChoice
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Which builds of the apps the launcher offers.</summary>
public enum AppChannel
{
    /// <summary>The builds of the branch main.</summary>
    Release = 0,

    /// <summary>The builds of the branch dev (newer, less tested).</summary>
    Nightly = 1,
}

/// <summary>The settings of this computer (settings.json). Only <see cref="Business.SettingsService"/> writes them.</summary>
public sealed record AppSettings
{
    public ThemeChoice Theme { get; init; } = ThemeChoice.System;

    /// <summary>The channel of the apps (not of the launcher itself: that follows its own build).</summary>
    public AppChannel AppChannel { get; init; } = AppChannel.Release;

    /// <summary>The launcher looks for new builds of itself and of the apps (at start and every 6 hours).</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Full auto update of the launcher: new versions are installed without asking.</summary>
    public bool InstallUpdatesAutomatically { get; init; } = true;

    /// <summary>Apps that install without a window are updated without asking; the others are offered.</summary>
    public bool UpdateAppsAutomatically { get; init; }

    /// <summary>A notification when an app has an update (only when it is not updated automatically).</summary>
    public bool NotifyAboutAppUpdates { get; init; } = true;

    /// <summary>Size of the main window (device-independent pixels).</summary>
    public double WindowWidth { get; init; } = 1180;

    public double WindowHeight { get; init; } = 760;

    /// <summary>The menu at the left of the main window is folded to its icons.</summary>
    public bool IsSidebarCollapsed { get; init; }
}
