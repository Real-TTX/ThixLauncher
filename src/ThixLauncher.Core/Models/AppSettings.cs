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

    /// <summary>The connection to MatCloud. Only <see cref="Business.CloudService"/> writes it.</summary>
    public CloudSettings Cloud { get; init; } = new();

    /// <summary>An app the launcher installs is signed in at MatCloud too (with the launcher's account, without the browser).</summary>
    public bool ConnectInstalledApps { get; init; } = true;
}

/// <summary>Where ThixLauncher is signed in at MatCloud (as the app "launcher").</summary>
public sealed record CloudSettings
{
    /// <summary>The address of the cloud, e.g. https://cloud.example.com.</summary>
    public string ServerUrl { get; init; } = "";

    public string UserName { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string DeviceName { get; init; } = "";

    public long DeviceId { get; init; }

    /// <summary>The device token, protected for this user (Windows DPAPI); on macOS it lives in the Keychain.</summary>
    public string ProtectedToken { get; init; } = "";

    public bool IsConnected => ServerUrl.Length > 0 && DeviceId > 0;

    /// <summary>The account as people read it: "Anna Beispiel (anna)", or "anna".</summary>
    public string AccountText => DisplayName.Length > 0 && DisplayName != UserName ? $"{DisplayName} ({UserName})" : UserName;
}
