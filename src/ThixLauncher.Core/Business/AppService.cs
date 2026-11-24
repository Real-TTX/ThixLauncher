using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;
using ThixLauncher.Core.Platform;

namespace ThixLauncher.Core.Business;

/// <summary>
/// The apps of the catalog on this computer: what is installed (asked from the system), what their channel offers (read
/// from GitHub), and the actions – install, update, remove, start. Every action checks here what may happen (policy,
/// platform, state, a checksum for every package), whatever the UI shows or hides. One action per app at a time; the state
/// of each app is kept in memory and <see cref="Changed"/> tells the UI (from any thread).
/// </summary>
public static class AppService
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static Task? _refresh;
    private static bool _isUpdatingAll;

    /// <summary>Raised after every change of an app's state (any thread).</summary>
    public static event EventHandler? Changed;

    /// <summary>The launcher can install apps on this system (Windows, macOS).</summary>
    public static bool IsSupported => AppSystem.Current.IsSupported;

    /// <summary>When the feeds were read the last time (null: not yet).</summary>
    public static DateTimeOffset? LastRefresh { get; private set; }

    public static bool IsRefreshing
    {
        get
        {
            lock (Gate)
            {
                return _refresh is { IsCompleted: false };
            }
        }
    }

    /// <summary>"Alle aktualisieren" (or the automatic update) is running.</summary>
    public static bool IsUpdatingAll
    {
        get
        {
            lock (Gate)
            {
                return _isUpdatingAll;
            }
        }
    }

    /// <summary>
    /// "Alle aktualisieren" can be offered: an installed app has an update, none of them is being changed right now, the
    /// organisation allows changes and the system is supported.
    /// </summary>
    public static bool CanUpdateAll =>
        !AccessPolicy.AreAppChangesDisabled && IsSupported && !IsUpdatingAll && All().Any(status => status.HasUpdate && !status.IsBusy);

    /// <summary>"release" or "nightly": the channel of the apps (setting "Kanal").</summary>
    public static string Channel => SettingsService.Get().AppChannel == AppChannel.Nightly ? AppVersion.Nightly : AppVersion.Release;

    /// <summary>"Release" or "Nightly", for texts.</summary>
    public static string ChannelName => Channel == AppVersion.Nightly ? "Nightly" : "Release";

    /// <summary>The state of one app.</summary>
    public static AppStatus Get(string appId)
    {
        var app = AppCatalog.Find(appId) ?? throw new BusinessException("Diese App gibt es nicht.");
        return StatusOf(app);
    }

    /// <summary>Every app of the catalog with its state, in the order of the catalog.</summary>
    public static IReadOnlyList<AppStatus> All() => AppCatalog.Apps.Select(StatusOf).ToList();

    /// <summary>The list of the toolbar: search over name, line and description, the filter and the order.</summary>
    public static IReadOnlyList<AppStatus> Query(AppQuery query)
    {
        var search = query.Search.Trim();
        var matches = All().Where(status => Fits(status, query.Filter) && (search.Length == 0 || Mentions(status.App, search)));
        return (query.Sort == AppSort.State
                ? matches.OrderBy(Rank).ThenBy(status => status.App.Name, StringComparer.CurrentCultureIgnoreCase)
                : matches.OrderBy(status => status.App.Name, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
    }

    /// <summary>How many apps a filter shows (the counts of the menu and the filter).</summary>
    public static int Count(AppFilter filter) => All().Count(status => Fits(status, filter));

    /// <summary>Asks the system again what is installed (fast, no network): after the user installed or removed an app by hand.</summary>
    public static void DetectInstalled()
    {
        foreach (var app in AppCatalog.Apps)
        {
            var installed = Detect(app);
            lock (Gate)
            {
                EntryOf(app).Installed = installed;
            }
        }

        RaiseChanged();
    }

    /// <summary>
    /// Asks the system what is installed and GitHub what each channel offers (all apps at once). A refresh that is running
    /// already is joined, not started twice. A feed that cannot be read keeps its last answer and names the problem.
    /// </summary>
    public static Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (Gate)
        {
            if (_refresh is { IsCompleted: false } running)
            {
                return running;
            }

            _refresh = Task.Run(() => RefreshAllAsync(cancellationToken), cancellationToken);
            return _refresh;
        }
    }

    /// <summary>
    /// What to ask before <see cref="InstallAsync"/> (SETUP-08): the install options of the newest build – the block
    /// "installer" of its manifest – that apply on this system, with their defaults. Null when there is nothing to ask: the app
    /// is installed (an update keeps its choices), its build has no such block (older builds, single GitHub releases), or
    /// nothing it offers applies here.
    /// </summary>
    public static InstallerConfig? InstallOptionsOf(string appId)
    {
        var status = Get(appId);
        return status is { IsInstalled: false, Latest.Installer: { } installer } ? AppInstallOptions.ForThisSystem(installer) : null;
    }

    /// <summary>
    /// Installs the newest build of the channel, or updates to it. Downloads the package, checks its SHA-256 and runs it.
    /// Apps whose setup asks open it, and this waits until the user closed it. A fresh install of an app that knows MatCloud takes the sign-in of
    /// the launcher along (<see cref="CloudService.HandOffAsync"/>; never in the way: without the cloud the app signs in by itself). Returns the
    /// account of MatCloud the app signs in with at its first start, or null.
    /// <paramref name="choices"/> are the answers to the install options of the app (<see cref="InstallOptionsOf"/>, SETUP-08): a
    /// first install passes them to the setup of the app, what is left open takes the default of the app; an update ignores
    /// them (the app keeps what it has), and so does an app without install options.
    /// </summary>
    public static async Task<string?> InstallAsync(string appId, InstallChoices? choices = null, CancellationToken cancellationToken = default)
    {
        var app = AppCatalog.Find(appId) ?? throw new BusinessException("Diese App gibt es nicht.");
        AccessPolicy.EnsureAppChangesAllowed();
        EnsureSupported();

        var installed = Detect(app);
        var latest = LatestOf(app) ?? await ReadLatestAsync(app, cancellationToken)
                     ?? throw new BusinessException($"Für {app.Name} gibt es im Kanal {ChannelName} noch keinen Build für dieses System.");
        if (installed?.Version is { } current && current.Channel == latest.Version.Channel && !latest.Version.IsNewerThan(current))
        {
            throw new BusinessException($"{app.Name} ist schon aktuell ({current.Text}).");
        }

        if (latest.Sha256 is not { Length: 64 })
        {
            throw new BusinessException($"Das Paket von {app.Name} hat keine Prüfsumme – es wird aus Sicherheitsgründen nicht installiert.");
        }

        var isUpdate = installed is not null;
        var setup = AppInstallOptions.SetupFor(app, latest.Installer, choices, isUpdate);
        Begin(app, isUpdate ? AppState.Updating : AppState.Installing, new AppProgress("Herunterladen", 0));
        var folder = Path.Combine(AppPaths.DownloadDirectory, app.Id);
        string? cloudAccount = null;
        try
        {
            Log.Info($"{(isUpdate ? "Updating" : "Installing")} {app.Id} {latest.Version.Text} ({latest.PackageFile})");
            var packageFile = Path.Combine(folder, latest.PackageFile);
            await ReleaseFeed.DownloadAsync(latest.DownloadUrl, packageFile, latest.Sha256,
                new Reporter<double>(fraction => Report(app, new AppProgress("Herunterladen", fraction))), cancellationToken);

            if (!isUpdate && app.CloudApp is not null && CloudService.IsConnected)
            {
                Report(app, new AppProgress("Anmeldung bei der MatCloud"));
                cloudAccount = await CloudService.HandOffAsync(app, latest.Version.Text, cancellationToken);
            }

            await AppSystem.Current.InstallAsync(app, packageFile, installed, setup, new Reporter<string>(step => Report(app, new AppProgress(step))), cancellationToken);

            var now = Detect(app);
            lock (Gate)
            {
                EntryOf(app).Installed = now;
            }

            if (now is null)
            {
                CloudService.ForgetHandoff(app);
                throw new BusinessException(!OperatingSystem.IsWindows() || setup.WindowsQuiet
                    ? $"{app.Name} wurde nicht installiert. Details stehen im Protokoll."
                    : $"{app.Name} wurde nicht installiert – das Setup wurde abgebrochen.");
            }

            Log.Info($"{app.Id} is installed: {now.VersionText} in {now.Location}");
            return cloudAccount;
        }
        catch (Exception exception) when (exception is FeedException or AppSystemException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"{(isUpdate ? "Update" : "Installation")} of {app.Id} failed", exception);
            // the app may be there all the same (macOS: copied, but its options refused): then it keeps the sign-in
            var now = Detect(app);
            lock (Gate)
            {
                EntryOf(app).Installed = now;
            }

            if (!isUpdate && now is null)
            {
                CloudService.ForgetHandoff(app);
            }

            throw new BusinessException(exception.Message);
        }
        finally
        {
            End(app);
            DeleteFolder(folder);
        }
    }

    /// <summary>Removes the app with its own uninstaller (the quiet one when it has one). Its user data stays.</summary>
    public static async Task UninstallAsync(string appId, CancellationToken cancellationToken = default)
    {
        var app = AppCatalog.Find(appId) ?? throw new BusinessException("Diese App gibt es nicht.");
        AccessPolicy.EnsureAppChangesAllowed();
        EnsureSupported();
        var installed = Detect(app) ?? throw new BusinessException($"{app.Name} ist nicht installiert.");

        Begin(app, AppState.Removing, new AppProgress($"{app.Name} wird entfernt"));
        try
        {
            Log.Info($"Removing {app.Id} {installed.VersionText} from {installed.Location}");
            await AppSystem.Current.UninstallAsync(app, installed, new Reporter<string>(step => Report(app, new AppProgress(step))), cancellationToken);

            var now = Detect(app);
            lock (Gate)
            {
                EntryOf(app).Installed = now;
            }

            if (now is not null)
            {
                throw new BusinessException($"{app.Name} ist noch installiert – die Deinstallation wurde abgebrochen.");
            }

            Log.Info($"{app.Id} was removed");
            CloudService.ForgetHandoff(app);
        }
        catch (Exception exception) when (exception is AppSystemException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"Removing {app.Id} failed", exception);
            throw new BusinessException(exception.Message);
        }
        finally
        {
            End(app);
        }
    }

    /// <summary>Starts the installed app (a running one shows its window: every Thix app runs once per user).</summary>
    public static void Launch(string appId)
    {
        var status = Get(appId);
        EnsureSupported();
        if (status.Installed is null)
        {
            throw new BusinessException($"{status.App.Name} ist nicht installiert.");
        }

        if (status.IsBusy)
        {
            throw new BusinessException($"{status.App.Name} wird gerade geändert – gleich noch einmal versuchen.");
        }

        if (status.Installed.Executable is null)
        {
            throw new BusinessException($"Die Programmdatei von {status.App.Name} wurde nicht gefunden. Eine neue Installation behebt das.");
        }

        try
        {
            AppSystem.Current.Launch(status.App, status.Installed);
            Log.Info($"Started {status.App.Id}");
        }
        catch (AppSystemException exception)
        {
            throw new BusinessException(exception.Message);
        }
    }

    /// <summary>
    /// Updates every app that has an update; <paramref name="quietOnly"/> leaves out apps whose setup asks (the automatic
    /// update runs without anybody at the computer). One after the other; an app that is being changed right now is left
    /// alone. Only one run at a time. Returns what was updated and what failed, with the reason.
    /// </summary>
    public static async Task<AppUpdateReport> UpdateAllAsync(bool quietOnly, CancellationToken cancellationToken = default)
    {
        AccessPolicy.EnsureAppChangesAllowed();
        EnsureSupported();
        lock (Gate)
        {
            if (_isUpdatingAll)
            {
                throw new BusinessException("Die Apps werden gerade aktualisiert – bitte warten, bis das fertig ist.");
            }

            _isUpdatingAll = true;
        }

        RaiseChanged();
        var report = new AppUpdateReport();
        try
        {
            foreach (var status in All().Where(status => status.HasUpdate && !status.IsBusy && (!quietOnly || status.App.InstallsQuietly)))
            {
                try
                {
                    await InstallAsync(status.App.Id, cancellationToken: cancellationToken);
                    report.Updated.Add(status.App.Name);
                }
                catch (BusinessException exception)
                {
                    report.Failed.Add((status.App.Name, exception.Message));
                }
            }
        }
        finally
        {
            lock (Gate)
            {
                _isUpdatingAll = false;
            }

            RaiseChanged();
        }

        return report;
    }

    /// <summary>Forgets every state (tests).</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            Entries.Clear();
            _refresh = null;
            _isUpdatingAll = false;
            LastRefresh = null;
        }

        StoreService.ResetForTests();
    }

    private static async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        var channel = Channel;
        await Task.WhenAll(AppCatalog.Apps.Select(async app =>
        {
            var installed = Detect(app);
            AppRelease? latest = null;
            string? error = null;
            try
            {
                latest = await ReleaseFeed.LatestAsync(app, channel, UpdateService.PlatformKey, cancellationToken);
            }
            catch (FeedException exception)
            {
                Log.Warning($"Feed of {app.Id} could not be read: {exception.Message} ({exception.InnerException?.Message})");
                error = exception.Message;
            }

            lock (Gate)
            {
                var entry = EntryOf(app);
                entry.Installed = installed;
                if (error is null || entry.LatestChannel != channel)
                {
                    entry.Latest = latest;
                    entry.LatestChannel = channel;
                }

                entry.FeedError = error;
            }
        }));

        LastRefresh = AppClock.Now;
        RaiseChanged();
    }

    private static async Task<AppRelease?> ReadLatestAsync(ThixApp app, CancellationToken cancellationToken)
    {
        var channel = Channel;
        try
        {
            var latest = await ReleaseFeed.LatestAsync(app, channel, UpdateService.PlatformKey, cancellationToken);
            lock (Gate)
            {
                var entry = EntryOf(app);
                entry.Latest = latest;
                entry.LatestChannel = channel;
                entry.FeedError = null;
            }

            return latest;
        }
        catch (FeedException exception)
        {
            throw new BusinessException(exception.Message);
        }
    }

    private static AppStatus StatusOf(ThixApp app)
    {
        lock (Gate)
        {
            if (!Entries.ContainsKey(app.Id))
            {
                EntryOf(app).Installed = Detect(app);
            }

            var entry = EntryOf(app);
            var latest = entry.LatestChannel == Channel ? entry.Latest : null;
            var state = entry.Busy ?? StateOf(entry.Installed, latest);
            return new AppStatus(app, entry.Installed, latest, state, entry.FeedError, entry.Progress);
        }
    }

    private static AppState StateOf(InstalledApp? installed, AppRelease? latest)
    {
        if (installed is null)
        {
            return AppState.NotInstalled;
        }

        return latest is not null && installed.Version is { } current && latest.Version.IsNewerThan(current)
            ? AppState.UpdateAvailable
            : AppState.Installed;
    }

    private static AppRelease? LatestOf(ThixApp app)
    {
        lock (Gate)
        {
            var entry = EntryOf(app);
            return entry.LatestChannel == Channel ? entry.Latest : null;
        }
    }

    private static InstalledApp? Detect(ThixApp app)
    {
        try
        {
            return AppSystem.Current.Detect(app);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warning($"Could not detect {app.Id}: {exception.Message}");
            return null;
        }
    }

    private static void EnsureSupported()
    {
        if (!AppSystem.Current.IsSupported)
        {
            throw new BusinessException("Auf diesem System kann ThixLauncher keine Apps installieren (nur Windows und macOS).");
        }
    }

    /// <summary>Marks the app as busy; a second action on the same app is refused.</summary>
    private static void Begin(ThixApp app, AppState state, AppProgress progress)
    {
        lock (Gate)
        {
            var entry = EntryOf(app);
            if (entry.Busy is not null)
            {
                throw new BusinessException($"{app.Name} wird gerade geändert – bitte warten, bis das fertig ist.");
            }

            entry.Busy = state;
            entry.Progress = progress;
        }

        RaiseChanged();
    }

    private static void Report(ThixApp app, AppProgress progress)
    {
        lock (Gate)
        {
            EntryOf(app).Progress = progress;
        }

        RaiseChanged();
    }

    private static void End(ThixApp app)
    {
        lock (Gate)
        {
            var entry = EntryOf(app);
            entry.Busy = null;
            entry.Progress = null;
        }

        RaiseChanged();
    }

    private static Entry EntryOf(ThixApp app)
    {
        if (!Entries.TryGetValue(app.Id, out var entry))
        {
            entry = new Entry();
            Entries[app.Id] = entry;
        }

        return entry;
    }

    private static bool Fits(AppStatus status, AppFilter filter) => filter switch
    {
        AppFilter.Installed => status.IsInstalled,
        AppFilter.Updates => status.HasUpdate || status.State == AppState.Updating,
        AppFilter.NotInstalled => !status.IsInstalled,
        _ => true,
    };

    private static bool Mentions(ThixApp app, string search) =>
        app.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
        app.Tagline.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
        app.Description.Contains(search, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>What needs attention first: updates and running actions, then installed apps, then the rest.</summary>
    private static int Rank(AppStatus status) => status.State switch
    {
        AppState.UpdateAvailable or AppState.Installing or AppState.Updating or AppState.Removing => 0,
        AppState.Installed => 1,
        _ => 2,
    };

    private static void RaiseChanged() => Changed?.Invoke(null, EventArgs.Empty);

    private static void DeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Could not delete {folder}: {exception.Message}");
        }
    }

    private sealed class Entry
    {
        public InstalledApp? Installed { get; set; }

        public AppRelease? Latest { get; set; }

        /// <summary>The channel <see cref="Latest"/> was read for: after a change of the setting it does not count.</summary>
        public string? LatestChannel { get; set; }

        public string? FeedError { get; set; }

        public AppState? Busy { get; set; }

        public AppProgress? Progress { get; set; }
    }

    /// <summary>Reports straight away on the thread that reports (Progress&lt;T&gt; would post to the UI thread of the caller).</summary>
    private sealed class Reporter<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

/// <summary>What "Alle aktualisieren" did.</summary>
public sealed class AppUpdateReport
{
    public List<string> Updated { get; } = [];

    public List<(string App, string Reason)> Failed { get; } = [];
}
