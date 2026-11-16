namespace ThixLauncher.Core.Models;

/// <summary>The newest build of an app in a channel for this system, as its feed names it.</summary>
public sealed record AppRelease(
    string AppId,
    AppVersion Version,
    string PackageFile,
    string DownloadUrl,
    string? Sha256,
    long? Size,
    DateTimeOffset? PublishedAt,
    string ReleasePage);

/// <summary>An installation the launcher found on this computer (installed by the launcher or by hand).</summary>
public sealed record InstalledApp(
    string? VersionText,
    string Location,
    string? Executable,
    string? UninstallCommand = null,
    string? QuietUninstallCommand = null)
{
    public AppVersion? Version => AppVersion.Parse(VersionText);
}

/// <summary>What an app is doing on this computer right now.</summary>
public enum AppState
{
    /// <summary>Not installed; it can be installed when its feed has a package for this system.</summary>
    NotInstalled,

    /// <summary>Installed and up to date (or nothing newer is known).</summary>
    Installed,

    /// <summary>Installed, and its channel has a newer build.</summary>
    UpdateAvailable,

    Installing,
    Updating,
    Removing,
}

/// <summary>A step of an installation for the UI: what happens and how far the download is (0 … 1, null while unknown).</summary>
public sealed record AppProgress(string Step, double? Fraction = null);

/// <summary>An app with what the launcher knows about it: the installation, the newest build, the state.</summary>
public sealed record AppStatus(
    ThixApp App,
    InstalledApp? Installed,
    AppRelease? Latest,
    AppState State,
    string? FeedError = null,
    AppProgress? Progress = null)
{
    public bool IsInstalled => Installed is not null;

    public bool IsBusy => State is AppState.Installing or AppState.Updating or AppState.Removing;

    public bool HasUpdate => State == AppState.UpdateAvailable;

    /// <summary>The app can be started: installed, its program file is known and nothing is being changed.</summary>
    public bool CanLaunch => Installed?.Executable is not null && !IsBusy;
}

public enum AppFilter
{
    All,
    Installed,
    Updates,
    NotInstalled,
}

public enum AppSort
{
    /// <summary>By name, A to Z.</summary>
    Name,

    /// <summary>What needs attention first: updates, then installed, then the rest.</summary>
    State,
}

/// <summary>The toolbar of the app list: search text, filter and sort order.</summary>
public sealed record AppQuery(string Search = "", AppFilter Filter = AppFilter.All, AppSort Sort = AppSort.Name);
