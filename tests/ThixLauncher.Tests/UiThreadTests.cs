using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Models;
using ThixLauncher.Core.Platform;

namespace ThixLauncher.Tests;

/// <summary>
/// Nothing that asks the system runs on the UI thread: detection, setups, uninstallers and starts go to a worker thread, so a
/// system that takes its time (Windows Installer busy with another installation, a virus scanner) never freezes the window.
/// </summary>
public sealed class UiThreadTests
{
    [AvaloniaFact]
    public async Task Detection_runs_on_a_worker_and_the_window_keeps_answering_while_it_waits()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixclip")!, "0.1.12-20261115");
        using var slow = new WatchedAppSystem(data.System) { HoldDetection = true };
        AppSystem.UseForTests(slow);

        var clock = Stopwatch.StartNew();
        var window = new MainWindow();
        window.ShowWindow(); // asks the system again – on a worker, which now waits
        Settle(window);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"opening the window waited for the system ({clock.Elapsed})");
        Assert.True(slow.WaitingDetections > 0, "the detection did not start");
        Assert.False(AppService.HasDetected);
        Assert.Empty(window.Apps.Cards);
        Assert.Equal("Die installierten Apps werden gesucht …", window.Apps.EmptyText.Text);

        // the UI thread answers while the detection waits
        var answered = await Dispatcher.UIThread.InvokeAsync(() => 42, DispatcherPriority.Send);
        Assert.Equal(42, answered);

        slow.Release();
        await WaitUntil(window, () => window.Apps.Cards.Count == 1);
        Assert.True(AppService.HasDetected);
        Assert.Equal("ThixClip", window.Apps.Cards[0].App.Name);
        Assert.Empty(slow.CallsOnUiThread);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Install_start_and_removal_started_on_the_UI_thread_ask_the_system_on_a_worker()
    {
        using var data = new TestData();
        using var watched = new WatchedAppSystem(data.System);
        AppSystem.UseForTests(watched);
        await AppService.RefreshAsync();
        Assert.True(Dispatcher.UIThread.CheckAccess());

        await AppService.InstallAsync("thixnote");
        Assert.Equal("1.1.1-20261116", AppService.Get("thixnote").Installed?.VersionText);
        Assert.Equal(AppState.Installed, AppService.Get("thixnote").State);

        await AppService.LaunchAsync("thixnote");
        Assert.Equal(["thixnote"], data.System.Launched);

        await AppService.UninstallAsync("thixnote");
        Assert.False(AppService.Get("thixnote").IsInstalled);

        await AppService.DetectInstalledAsync();
        Assert.Contains("Detect", watched.Calls);
        Assert.Contains("Install", watched.Calls);
        Assert.Contains("Launch", watched.Calls);
        Assert.Contains("Uninstall", watched.Calls);
        Assert.Empty(watched.CallsOnUiThread);
    }

    [Fact]
    public async Task A_detection_that_began_before_an_installation_does_not_overwrite_what_the_installation_found()
    {
        using var data = new TestData();
        using var watched = new WatchedAppSystem(data.System);
        AppSystem.UseForTests(watched);
        await AppService.RefreshAsync();

        // the detection has looked at ThixNote (not installed) and waits at the last app of the catalog
        watched.HoldOnly = AppCatalog.Apps[^1].Id;
        Assert.NotEqual("thixnote", watched.HoldOnly);
        watched.HoldDetection = true;
        var detection = AppService.DetectInstalledAsync();
        for (var round = 0; round < 500 && watched.WaitingDetections == 0; round++)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, watched.WaitingDetections);
        await AppService.InstallAsync("thixnote");
        watched.Release();
        await detection;
        Assert.Equal("1.1.1-20261116", AppService.Get("thixnote").Installed?.VersionText);
    }

    private static void Settle(Avalonia.Controls.Window window)
    {
        for (var round = 0; round < 6; round++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        window.UpdateLayout();
    }

    private static async Task WaitUntil(Avalonia.Controls.Window window, Func<bool> condition)
    {
        for (var round = 0; round < 500 && !condition(); round++)
        {
            Settle(window);
            await Task.Delay(10);
        }

        Assert.True(condition(), "the condition was not met in time");
    }

    /// <summary>The computer in memory, watched: which calls came, which of them on the UI thread; detection can be held.</summary>
    private sealed class WatchedAppSystem(SampleAppSystem inner) : IAppSystem, IDisposable
    {
        private readonly ManualResetEventSlim _released = new(false);
        private int _waiting;

        public bool HoldDetection { get; set; }

        /// <summary>Only the detection of this app is held (null: every app).</summary>
        public string? HoldOnly { get; set; }

        public int WaitingDetections => Volatile.Read(ref _waiting);

        public ConcurrentQueue<string> Calls { get; } = new();

        public ConcurrentQueue<string> CallsOnUiThread { get; } = new();

        public bool IsSupported => inner.IsSupported;

        public void Release() => _released.Set();

        public InstalledApp? Detect(ThixApp app)
        {
            Note("Detect");
            if (HoldDetection && (HoldOnly is null || HoldOnly == app.Id))
            {
                Interlocked.Increment(ref _waiting);
                _released.Wait(TimeSpan.FromSeconds(20));
            }

            return inner.Detect(app);
        }

        public Task InstallAsync(ThixApp app, string packageFile, InstalledApp? installed, AppSetupCommand setup, IProgress<string> progress, CancellationToken cancellationToken)
        {
            Note("Install");
            return inner.InstallAsync(app, packageFile, installed, setup, progress, cancellationToken);
        }

        public Task UninstallAsync(ThixApp app, InstalledApp installed, IProgress<string> progress, CancellationToken cancellationToken)
        {
            Note("Uninstall");
            return inner.UninstallAsync(app, installed, progress, cancellationToken);
        }

        public void Launch(ThixApp app, InstalledApp installed)
        {
            Note("Launch");
            inner.Launch(app, installed);
        }

        public void Dispose()
        {
            _released.Set();
            AppSystem.UseForTests(null);
        }

        private void Note(string call)
        {
            Calls.Enqueue(call);
            if (Dispatcher.UIThread.CheckAccess())
            {
                CallsOnUiThread.Enqueue(call);
            }
        }
    }
}
