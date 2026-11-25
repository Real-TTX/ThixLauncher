using System.Diagnostics;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Tests;

/// <summary>The watchdog's timing, the hang report's text, and its dumps: taken without freezing the launcher, at most one per run, three kept.</summary>
public sealed class HangReportTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(8);

    private static long At(double seconds) => (long)(seconds * Stopwatch.Frequency);

    [Fact]
    public void A_hang_begins_when_a_knock_waited_the_threshold_and_ends_when_it_ran()
    {
        var detector = new HangDetector(Threshold);
        for (var second = 0; second <= 7; second++)
        {
            detector.Posted(At(second));
            Assert.Null(detector.Check(At(second)));
        }

        detector.Posted(At(8));
        var started = Assert.IsType<HangDetector.Started>(detector.Check(At(8)));
        Assert.Equal(At(0), started.Since);
        Assert.Equal(8, started.SilentFor.TotalSeconds, 3);

        // still hanging: no second report
        detector.Posted(At(9));
        Assert.Null(detector.Check(At(9)));

        // the UI runs the knocks of seconds 0 to 9 at 11.5 s
        for (var second = 0; second <= 9; second++)
        {
            detector.Answered(At(second), At(11.5));
        }

        detector.Posted(At(12));
        var ended = Assert.IsType<HangDetector.Ended>(detector.Check(At(12)));
        Assert.Equal(11.5, ended.Lasted.TotalSeconds, 3);
        detector.Answered(At(12), At(12));
        Assert.Null(detector.Check(At(13)));
    }

    [Fact]
    public void A_late_watchdog_is_no_hang_when_the_UI_ran_every_knock()
    {
        // the watchdog itself woke late (4.9 s twice, a busy computer): the UI answered each knock at once
        var detector = new HangDetector(Threshold);
        foreach (var second in new[] { 0.0, 4.9, 9.8, 14.7 })
        {
            detector.Posted(At(second));
            detector.Answered(At(second), At(second + 0.01));
            Assert.Null(detector.Check(At(second + 0.02)));
        }
    }

    [Fact]
    public void Sleep_forgets_the_knocks_before_it()
    {
        var detector = new HangDetector(Threshold);
        detector.Posted(At(0));
        detector.Slept();
        detector.Posted(At(100));
        Assert.Null(detector.Check(At(101)));
    }

    [Fact]
    public void The_report_says_what_the_UI_thread_did_what_ran_and_the_newest_log_lines()
    {
        var details = UiWatchdog.ReportDetails(
            new UiThreadState(TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(9), AnswersMessages: false, IsHungWindow: true),
            ["thixclip: Updating – Herunterladen (40 %)"],
            ["12:00:01.000 INFO  Updating thixclip 0.1.12-20261115 (ThixClipSetup.exe)"]);
        var text = string.Join("\n", details);
        Assert.Contains("Der UI-Thread wartete (0,2 s Rechenzeit in den letzten 9 s) und nahm keine Fenster-Nachrichten an", text);
        Assert.Contains("„Keine Rückmeldung“", text);
        Assert.Contains("  - thixclip: Updating – Herunterladen (40 %)", text);
        Assert.Contains("  12:00:01.000 INFO  Updating thixclip", text);

        Assert.StartsWith("Der UI-Thread rechnete (8,5 s Rechenzeit in den letzten 9 s) – eine lange Arbeit",
            UiWatchdog.Describe(new UiThreadState(TimeSpan.FromSeconds(8.5), TimeSpan.FromSeconds(9), AnswersMessages: false, IsHungWindow: false)));
        Assert.Contains("nahm dabei Fenster-Nachrichten an",
            UiWatchdog.Describe(new UiThreadState(TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(9), AnswersMessages: true, IsHungWindow: false)));
        Assert.Equal("nicht ermittelt (nur unter Windows).", UiWatchdog.Describe(null));
    }

    [Fact]
    public async Task The_activity_names_the_apps_being_changed_and_the_log_keeps_its_newest_lines()
    {
        using var data = new TestData();
        await AppService.RefreshAsync();
        Assert.Equal(["Keine Aktion an den Apps."], AppService.DescribeActivity());

        data.System.Hold = new TaskCompletionSource();
        var installing = AppService.InstallAsync("thixnote");
        for (var round = 0; round < 500 && AppService.Get("thixnote").State != AppState.Installing; round++)
        {
            await Task.Delay(10);
        }

        Assert.Contains(AppService.DescribeActivity(), line => line.StartsWith("thixnote: Installing", StringComparison.Ordinal));
        data.System.Hold.TrySetResult();
        await installing;

        Log.Info("the newest line");
        Assert.EndsWith("INFO  the newest line", Log.Recent(3)[^1]);
    }

    [Fact]
    public void Only_the_newest_three_dumps_stay()
    {
        using var data = new TestData();
        Directory.CreateDirectory(AppPaths.LogDirectory);
        for (var day = 20; day <= 24; day++)
        {
            File.WriteAllText(Path.Combine(AppPaths.LogDirectory, $"hang-202611{day}-120000.dmp"), "dump");
        }

        ProblemReports.RemoveOldDumps(ProblemReports.KeptDumps);
        Assert.Equal(["hang-20261122-120000.dmp", "hang-20261123-120000.dmp", "hang-20261124-120000.dmp"],
            Directory.GetFiles(AppPaths.LogDirectory, "hang-*.dmp").Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_hang_report_without_a_dump_has_its_details()
    {
        using var data = new TestData();
        var report = ProblemReports.WriteHang(TimeSpan.FromSeconds(9), ["Oberfläche: Der UI-Thread wartete."], withDump: false);
        Assert.NotNull(report);
        Assert.Equal(TimeSpan.Zero, report.DumpPause);
        var text = File.ReadAllText(report.Path);
        Assert.StartsWith("ThixLauncher hat 9 Sekunden lang nicht reagiert", text.TrimStart('﻿'));
        Assert.Contains("Oberfläche: Der UI-Thread wartete.", text);
        Assert.DoesNotContain("Speicherabbild", text);
    }

    [Fact]
    public void The_dump_is_taken_from_a_snapshot_once_per_run_and_never_more_than_three_are_kept()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var data = new TestData();
        Directory.CreateDirectory(AppPaths.LogDirectory);
        for (var day = 20; day <= 22; day++)
        {
            File.WriteAllText(Path.Combine(AppPaths.LogDirectory, $"hang-202611{day}-120000.dmp"), "dump");
        }

        ProblemReports.ResetDumpForTests();
        try
        {
            // a thread that ticks every 10 ms: the dump must not stop it for long
            var longestGap = TimeSpan.Zero;
            using var stop = new CancellationTokenSource();
            var ticker = new Thread(() =>
            {
                var last = Stopwatch.GetTimestamp();
                while (!stop.IsCancellationRequested)
                {
                    Thread.Sleep(10);
                    var now = Stopwatch.GetTimestamp();
                    var gap = Stopwatch.GetElapsedTime(last, now);
                    if (gap > longestGap)
                    {
                        longestGap = gap;
                    }

                    last = now;
                }
            }) { IsBackground = true };
            ticker.Start();
            Thread.Sleep(100);

            var first = ProblemReports.WriteHang(TimeSpan.FromSeconds(9), ["Oberfläche: Test."])!;
            stop.Cancel();
            ticker.Join();

            var dump = Path.ChangeExtension(first.Path, ".dmp");
            Assert.True(File.Exists(dump) && new FileInfo(dump).Length > 1024 * 1024, "no dump written");
            if (Environment.GetEnvironmentVariable("THIXLAUNCHER_KEEP_DUMP") is { Length: > 0 } keep)
            {
                File.Copy(dump, keep, overwrite: true); // to look at it with dotnet-dump analyze (clrstack -all)
            }

            Assert.True(first.DumpPause < TimeSpan.FromSeconds(1), $"the snapshot held the process for {first.DumpPause}");
            Assert.True(longestGap < TimeSpan.FromSeconds(1), $"a thread stood still for {longestGap} while the dump was written");
            Assert.Contains("Speicherabbild dieses Moments", File.ReadAllText(first.Path));
            Assert.Equal(ProblemReports.KeptDumps, Directory.GetFiles(AppPaths.LogDirectory, "hang-*.dmp").Length);

            // a second hang of the same run: no second dump
            Thread.Sleep(1100); // a report of its own (the name has the second)
            var second = ProblemReports.WriteHang(TimeSpan.FromSeconds(9), ["Oberfläche: Test."])!;
            Assert.False(File.Exists(Path.ChangeExtension(second.Path, ".dmp")));
            Assert.Contains("schon eines geschrieben", File.ReadAllText(second.Path));
        }
        finally
        {
            ProblemReports.ResetDumpForTests();
        }
    }
}
