using Avalonia.Threading;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher;

/// <summary>
/// When the launcher looks at the apps and what it does with an update: it reads what is installed and what the channel
/// offers shortly after start, every 6 hours (setting "Automatisch nach Updates suchen") and after a change of the channel.
/// An update is installed by itself when the user chose "Apps automatisch aktualisieren" and the app installs without a
/// window; otherwise it is offered once (a notification, the badge of the tray and the menu). Beside it, it asks MatCloud whether the launcher
/// is still signed in – apart from the apps, so that a cloud that does not answer never holds them up.
/// </summary>
internal static class AppCoordinator
{
    private static readonly DispatcherTimer Timer = new() { Interval = UpdateService.CheckInterval };
    private static readonly HashSet<string> Announced = [];
    private static AppChannel _channel;
    private static bool _started;

    /// <summary>Updates that were not announced before (UI thread).</summary>
    public static event EventHandler<IReadOnlyList<AppStatus>>? UpdatesFound;

    /// <summary>What the automatic update did (UI thread).</summary>
    public static event EventHandler<AppUpdateReport>? AppsUpdated;

    public static void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _channel = SettingsService.Get().AppChannel;
        Timer.Tick += async (_, _) =>
        {
            if (SettingsService.Get().CheckForUpdates)
            {
                await RefreshAsync(automatic: true);
            }

            await CloudService.CheckAsync();
        };
        Timer.Start();

        SettingsService.Changed += (_, settings) => Ui.Post(() =>
        {
            if (settings.AppChannel != _channel)
            {
                _channel = settings.AppChannel;
                Announced.Clear();
                _ = RefreshAsync(automatic: false);
            }
        });

        // The window shows the installed apps at once; the feeds follow a moment later.
        AppService.DetectInstalled();
        DispatcherTimer.RunOnce(() => _ = RefreshAsync(automatic: true), TimeSpan.FromSeconds(2));

        // The cloud on its own: whether it answers changes nothing for the apps (it only decides whether an app installed now is signed in too).
        DispatcherTimer.RunOnce(() => _ = CloudService.CheckAsync(), TimeSpan.FromSeconds(3));
    }

    public static void Stop() => Timer.Stop();

    /// <summary>Reads the state of every app; <paramref name="automatic"/> may install updates (setting) and announce new ones.</summary>
    public static async Task RefreshAsync(bool automatic)
    {
        try
        {
            await AppService.RefreshAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            Log.Warning($"Refreshing the apps failed: {exception.Message}");
            return;
        }

        var settings = SettingsService.Get();
        if (automatic && settings.UpdateAppsAutomatically && !AccessPolicy.AreAppChangesDisabled && AppService.IsSupported)
        {
            try
            {
                var report = await AppService.UpdateAllAsync(quietOnly: true);
                if (report.Updated.Count + report.Failed.Count > 0)
                {
                    AppsUpdated?.Invoke(null, report);
                }
            }
            catch (BusinessException exception)
            {
                Log.Warning($"Automatic app update: {exception.Message}");
            }
        }

        var waiting = AppService.Query(new AppQuery(Filter: AppFilter.Updates))
            .Where(status => status.Latest is not null && Announced.Add($"{status.App.Id}:{status.Latest.Version.Text}"))
            .ToList();
        if (waiting.Count > 0 && settings.NotifyAboutAppUpdates)
        {
            UpdatesFound?.Invoke(null, waiting);
        }
    }
}
