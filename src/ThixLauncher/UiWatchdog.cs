using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;

namespace ThixLauncher;

/// <summary>
/// Notices when the UI thread stops answering ("reagiert nicht"): a background thread knocks every second (a job of the highest
/// priority on the dispatcher); when a knock has waited <see cref="HangThreshold"/> without being run, it writes a hang report –
/// what the UI thread was doing (<see cref="UiThreadProbe"/>), what ran (<see cref="AppService.DescribeActivity"/>), the newest log
/// lines, and on Windows once per run a dump (<see cref="ProblemReports.WriteHang"/>, which holds the launcher still for
/// milliseconds only) – and when that knock has run, it adds how long the UI was gone. A watchdog that was itself late (a busy
/// computer) is no hang: what counts is how long the oldest knock waited, not when the UI answered last. Sleep and hibernation
/// do not count either.
/// </summary>
internal static class UiWatchdog
{
    /// <summary>Windows marks a window as not responding after 5 s; a little later the report still catches the hang.</summary>
    public static readonly TimeSpan HangThreshold = TimeSpan.FromSeconds(8);

    private const int ReportedLogLines = 25;
    private static readonly TimeSpan KnockInterval = TimeSpan.FromSeconds(1);
    private static readonly HangDetector Detector = new(HangThreshold);
    private static UiThreadProbe? _probe;
    private static Thread? _thread;
    private static volatile bool _stopped;

    /// <summary>ThixLauncher is ending (quit, update, uninstall): the UI stops answering on purpose, that is no hang.</summary>
    public static void Stop() => _stopped = true;

    /// <summary>Starts watching the calling thread – the UI thread.</summary>
    public static void Start()
    {
        if (_thread is not null || Debugger.IsAttached)
        {
            return;
        }

        _probe = UiThreadProbe.OfCurrentThread();
        _thread = new Thread(Watch) { IsBackground = true, Name = "ThixLauncher UI watchdog" };
        _thread.Start();
    }

    /// <summary>The details of a hang report: the UI thread, what ran, the newest lines of the log.</summary>
    internal static IReadOnlyList<string> ReportDetails(UiThreadState? state, IReadOnlyList<string> activity, IReadOnlyList<string> log)
    {
        var lines = new List<string> { $"Oberfläche: {Describe(state)}", "", "Was ThixLauncher gerade tat:" };
        lines.AddRange(activity.Select(line => $"  - {line}"));
        lines.Add("");
        lines.Add("Letzte Einträge im Protokoll:");
        lines.AddRange(log.Count > 0 ? log.Select(line => $"  {line}") : ["  (keine in diesem Lauf)"]);
        return lines;
    }

    /// <summary>The UI thread in one sentence: busy computing, or waiting – inside a message loop or not.</summary>
    internal static string Describe(UiThreadState? state)
    {
        if (state is null)
        {
            return "nicht ermittelt (nur unter Windows).";
        }

        var german = CultureInfo.GetCultureInfo("de-DE");
        var busy = state.CpuTime is { } cpu && cpu >= state.Over / 2;
        var text = state.CpuTime is { } used
            ? $"Der UI-Thread {(busy ? "rechnete" : "wartete")} ({used.TotalSeconds.ToString("0.0", german)} s Rechenzeit in den letzten {state.Over.TotalSeconds:0} s)"
            : "Der UI-Thread (Rechenzeit unbekannt)";
        if (busy)
        {
            text += " – eine lange Arbeit auf dem UI-Thread.";
        }
        else
        {
            text += state.AnswersMessages switch
            {
                true => " und nahm dabei Fenster-Nachrichten an: er stand in einer Nachrichtenschleife (leer in GetMessage, in einem modalen Menü oder in einem Aufruf an ein anderes Programm).",
                false => " und nahm keine Fenster-Nachrichten an: er wartete in einem Systemaufruf, auf eine Sperre oder auf eine Datei.",
                null => ".",
            };
        }

        if (state.IsHungWindow == true)
        {
            text += " Windows zeigte das Fenster als „Keine Rückmeldung“.";
        }

        return text;
    }

    private static void Watch()
    {
        var lastLoop = Stopwatch.GetTimestamp();
        string? openReport = null;
        var pause = TimeSpan.Zero;
        var cpuSamples = new Queue<(long At, TimeSpan Cpu)>();
        while (!_stopped)
        {
            Thread.Sleep(KnockInterval);
            if (_stopped)
            {
                return;
            }

            var now = Stopwatch.GetTimestamp();

            // The computer slept: nobody could answer, start over.
            if (Stopwatch.GetElapsedTime(lastLoop, now) > TimeSpan.FromSeconds(5))
            {
                Detector.Slept();
                cpuSamples.Clear();
                openReport = null;
                pause = TimeSpan.Zero;
            }

            lastLoop = now;
            if (_probe?.CpuTime() is { } cpu)
            {
                cpuSamples.Enqueue((now, cpu));
                while (cpuSamples.Count > 60)
                {
                    cpuSamples.Dequeue();
                }
            }

            Detector.Posted(now);
            Dispatcher.UIThread.Post(() => Detector.Answered(now, Stopwatch.GetTimestamp()), DispatcherPriority.Send);

            switch (Detector.Check(Stopwatch.GetTimestamp()))
            {
                case HangDetector.Started started:
                    Log.Warning($"The UI thread has not run a knock for {started.SilentFor.TotalSeconds:0} s");
                    var state = _probe?.Look(started.Since, cpuSamples);
                    var details = ReportDetails(state, AppService.DescribeActivity(), Log.Recent(ReportedLogLines));
                    var report = ProblemReports.WriteHang(started.SilentFor, details);
                    openReport = report?.Path;
                    pause = report?.DumpPause ?? TimeSpan.Zero;
                    lastLoop = Stopwatch.GetTimestamp(); // writing the report is no sleep
                    break;

                case HangDetector.Ended ended:
                    // the snapshot of the dump held the launcher still for a moment: that is not the hang's
                    var lasted = ended.Lasted - pause;
                    Log.Info($"The UI thread answers again after {lasted.TotalSeconds:0} s");
                    if (openReport is not null)
                    {
                        ProblemReports.Append(openReport, $"{Environment.NewLine}Danach reagierte ThixLauncher wieder (nach insgesamt {lasted.TotalSeconds:0} Sekunden" +
                                                          (pause >= TimeSpan.FromMilliseconds(100) ? $", ohne die {pause.TotalSeconds:0.0} s für das Speicherabbild)." : ")."));
                    }

                    openReport = null;
                    pause = TimeSpan.Zero;
                    break;
            }
        }
    }
}

/// <summary>
/// The timing of the watchdog, apart from threads and clocks (time stamps of <see cref="Stopwatch.GetTimestamp"/>): which knocks
/// were posted, which the UI thread ran. A hang begins when the oldest knock not run has waited the threshold and ends when that
/// knock ran – its length runs to the moment it ran. Knocks run in the order they were posted (same priority).
/// </summary>
internal sealed class HangDetector(TimeSpan threshold)
{
    private readonly object _gate = new();
    private readonly Queue<long> _waiting = new();
    private long _answeredPost = long.MinValue;
    private long _answeredAt;
    private long? _hangSince;

    public abstract record Event;

    /// <summary>A hang began at <paramref name="Since"/> (the knock that waits); it has lasted <paramref name="SilentFor"/>.</summary>
    public sealed record Started(TimeSpan SilentFor, long Since) : Event;

    /// <summary>The knock of the hang ran: the UI was gone for <paramref name="Lasted"/>.</summary>
    public sealed record Ended(TimeSpan Lasted) : Event;

    public void Posted(long at)
    {
        lock (_gate)
        {
            _waiting.Enqueue(at);
        }
    }

    /// <summary>The UI thread ran the knock posted at <paramref name="posted"/> (at <paramref name="at"/>).</summary>
    public void Answered(long posted, long at)
    {
        lock (_gate)
        {
            if (posted > _answeredPost)
            {
                _answeredPost = posted;
                _answeredAt = at;
            }
        }
    }

    /// <summary>The computer slept: the knocks before do not count, a hang that was open is forgotten.</summary>
    public void Slept()
    {
        lock (_gate)
        {
            _waiting.Clear();
            _hangSince = null;
        }
    }

    public Event? Check(long now)
    {
        lock (_gate)
        {
            while (_waiting.Count > 0 && _waiting.Peek() <= _answeredPost)
            {
                _waiting.Dequeue();
            }

            if (_hangSince is { } since)
            {
                if (_answeredPost < since)
                {
                    return null;
                }

                _hangSince = null;
                return new Ended(Stopwatch.GetElapsedTime(since, _answeredAt));
            }

            if (_waiting.Count == 0 || Stopwatch.GetElapsedTime(_waiting.Peek(), now) < threshold)
            {
                return null;
            }

            _hangSince = _waiting.Peek();
            return new Started(Stopwatch.GetElapsedTime(_waiting.Peek(), now), _waiting.Peek());
        }
    }
}
