using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ThixLauncher.Core.Infrastructure;

/// <summary>A crash or a hang, written to the log folder; the app shows it on its next start.</summary>
public sealed record ProblemReport(string FilePath, DateTime Time, bool IsHang, string Text);

/// <summary>
/// Makes crashes and hangs visible instead of letting ThixLauncher vanish:
///  - an unhandled exception on any thread is written to logs/crash-*.txt (version, system, thread, the whole exception)
///    and, on Windows, shown at once in a message box (Ctrl+C there copies the text);
///  - a hang (the UI thread does not answer, App's watchdog) goes to logs/hang-*.txt with what the launcher was doing and its
///    newest log lines; on Windows the first hang of a run adds a memory dump hang-*.dmp next to it (the newest
///    <see cref="KeptDumps"/> are kept: a dump holds the memory of the process and stays on this computer unless the user
///    passes it on). The dump is taken from a snapshot of the process (<see cref="WriteDump"/>): the launcher stands still for
///    milliseconds, not for the seconds the file takes;
///  - the next start shows every report that has not been shown yet (<see cref="TakeUnseen"/>).
/// Reports name exceptions and stacks, nothing else.
/// </summary>
public static class ProblemReports
{
    /// <summary>How many hang dumps stay in the log folder (the newest).</summary>
    public const int KeptDumps = 3;

    private const string SeenMarker = "reports-seen.txt";
    private const int KeptReports = 20;
    private static int _installed;
    private static int _dumpWritten;

    /// <summary>Called first thing in Main: from then on no exception ends ThixLauncher without a report.</summary>
    public static void Install(bool showMessageBox)
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            var text = exception?.ToString() ?? e.ExceptionObject?.ToString() ?? "unknown error";
            Log.Error("Crash", exception);
            var path = Write("crash", "ThixLauncher wurde wegen eines unerwarteten Fehlers beendet.", text);
            if (showMessageBox && OperatingSystem.IsWindows())
            {
                ShowMessageBox(exception, path);
                MarkSeen(path);
            }
        };

        // A forgotten task must not take its error with it.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task error", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// Writes a hang report with <paramref name="details"/> (what the UI thread did, what ran, the newest log lines) and – on
    /// Windows, once per run – a dump. Returns the report file (null when it could not be written) and how long the dump held the
    /// launcher still (to leave out of the length of the hang).
    /// </summary>
    public static HangReportFile? WriteHang(TimeSpan blockedFor, IEnumerable<string> details, bool withDump = true)
    {
        var path = Write("hang", $"ThixLauncher hat {blockedFor.TotalSeconds:0} Sekunden lang nicht reagiert (die Oberfläche war blockiert).",
            string.Join(Environment.NewLine, details));
        if (path is null)
        {
            return null;
        }

        if (!withDump || !OperatingSystem.IsWindows())
        {
            return new HangReportFile(path, TimeSpan.Zero);
        }

        if (Interlocked.Exchange(ref _dumpWritten, 1) == 1)
        {
            Append(path, $"{Environment.NewLine}Kein Speicherabbild: in diesem Lauf wurde schon eines geschrieben.");
            return new HangReportFile(path, TimeSpan.Zero);
        }

        var dump = WriteDump(Path.ChangeExtension(path, ".dmp"));
        Append(path, dump is null
            ? $"{Environment.NewLine}Das Speicherabbild konnte nicht geschrieben werden (Details im Protokoll)."
            : string.Join(Environment.NewLine,
                "",
                $"Daneben liegt ein Speicherabbild dieses Moments: {Path.GetFileName(dump.Path)} ({dump.Bytes / 1024 / 1024} MB, " +
                $"ThixLauncher stand dafür {dump.Paused.TotalMilliseconds:0} ms still, geschrieben in {dump.Writing.TotalSeconds:0.0} s).",
                "Es enthält den Speicher von ThixLauncher – gib es nur weiter, wenn du das möchtest."));
        return new HangReportFile(path, dump?.Paused ?? TimeSpan.Zero);
    }

    /// <summary>A new run may write a dump again (tests).</summary>
    internal static void ResetDumpForTests() => Interlocked.Exchange(ref _dumpWritten, 0);

    /// <summary>Adds a line to a report (e.g. how long the hang lasted in the end).</summary>
    public static void Append(string path, string line)
    {
        try
        {
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The reports the user has not seen yet (oldest first); they count as seen afterwards.</summary>
    public static IReadOnlyList<ProblemReport> TakeUnseen()
    {
        try
        {
            if (!Directory.Exists(AppPaths.LogDirectory))
            {
                return [];
            }

            var seen = ReadSeen();
            var reports = ReportFiles()
                .Where(file => string.CompareOrdinal(Stamp(file), seen) > 0)
                .Select(Read)
                .OfType<ProblemReport>()
                .OrderBy(report => report.Time)
                .ToList();
            if (reports.Count > 0)
            {
                MarkSeen(reports[^1].FilePath);
            }

            return reports;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Problem reports could not be read: {exception.Message}");
            return [];
        }
    }

    private static string? Write(string kind, string headline, string details)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            var now = DateTime.Now;
            var path = Path.Combine(AppPaths.LogDirectory, $"{kind}-{now:yyyyMMdd-HHmmss}.txt");
            var text = new StringBuilder()
                .AppendLine(headline)
                .AppendLine()
                .AppendLine($"Zeit:     {now:dd.MM.yyyy HH:mm:ss}")
                .AppendLine($"Version:  {AppInfo.Version} ({RuntimeInformation.RuntimeIdentifier})")
                .AppendLine($"System:   {RuntimeInformation.OSDescription}, .NET {Environment.Version}")
                .AppendLine($"Thread:   {Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString()}")
                .AppendLine()
                .AppendLine(details)
                .ToString();
            File.WriteAllText(path, text, Encoding.UTF8);
            RemoveOldReports();
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ProblemReport? Read(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (!DateTime.TryParseExact(Stamp(path), "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out var time))
        {
            return null;
        }

        return new ProblemReport(path, time, name.StartsWith("hang-", StringComparison.Ordinal), File.ReadAllText(path, Encoding.UTF8));
    }

    private static IEnumerable<string> ReportFiles() =>
        Directory.EnumerateFiles(AppPaths.LogDirectory, "crash-*.txt")
            .Concat(Directory.EnumerateFiles(AppPaths.LogDirectory, "hang-*.txt"));

    /// <summary>"crash-20261115-181200.txt" -> "20261115-181200": sorts by time across both kinds.</summary>
    private static string Stamp(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name[(name.IndexOf('-') + 1)..];
    }

    /// <summary>The marker keeps the time stamp of the newest report shown.</summary>
    private static string ReadSeen()
    {
        var marker = Path.Combine(AppPaths.LogDirectory, SeenMarker);
        return File.Exists(marker) ? File.ReadAllText(marker).Trim() : "";
    }

    private static void MarkSeen(string? reportPath)
    {
        if (reportPath is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(Path.Combine(AppPaths.LogDirectory, SeenMarker), Stamp(reportPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void RemoveOldReports()
    {
        foreach (var old in ReportFiles().OrderByDescending(Path.GetFileName).Skip(KeptReports))
        {
            File.Delete(old);
        }

        RemoveOldDumps(KeptDumps);
    }

    /// <summary>A dump is big: only the newest <paramref name="keep"/> stay.</summary>
    internal static void RemoveOldDumps(int keep)
    {
        foreach (var dump in Directory.EnumerateFiles(AppPaths.LogDirectory, "hang-*.dmp").OrderByDescending(Path.GetFileName).Skip(keep))
        {
            try
            {
                File.Delete(dump);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Warning($"Could not delete the old dump {dump}: {exception.Message}");
            }
        }
    }

    private static void ShowMessageBox(Exception? exception, string? path)
    {
        var text = $"ThixLauncher wurde wegen eines unerwarteten Fehlers beendet.\n\n{exception?.GetType().FullName}: {exception?.Message}\n\n" +
                   (path is null ? "" : $"Der vollständige Bericht liegt hier:\n{path}\n\n") +
                   "Strg+C kopiert diese Meldung.";
        const uint iconError = 0x10, topMost = 0x40000, setForeground = 0x10000;
        _ = MessageBoxW(IntPtr.Zero, text, "ThixLauncher – unerwarteter Fehler", iconError | topMost | setForeground);
    }

    /// <summary>
    /// A dump with the private memory and thread data – what createdump calls "heap": enough for <c>dotnet-dump analyze</c>
    /// (clrstack -all, pstacks) and the native stacks. A type without the private memory keeps no managed frame (tried), and most
    /// of the size is native memory of the graphics, not the managed heap – so the type stays and the cost goes elsewhere:
    /// MiniDumpWriteDump on its own process suspends every other thread for as long as it writes (on 23.11.2026: 20 s for 284 MB
    /// while an MSI installation kept the disk busy, which made the reported hang 38 s long). So it reads a snapshot instead
    /// (PssCaptureSnapshot with a copy-on-write clone of the address space, as procdump -r does): the threads stand still while the
    /// snapshot is taken (milliseconds), the file is written from the clone while the launcher goes on. Without a snapshot there
    /// is no dump – never one that freezes the app.
    /// </summary>
    internal static HangDump? WriteDump(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            RemoveOldDumps(KeptDumps - 1);
            var process = GetCurrentProcess();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = PssCaptureSnapshot(process, SnapshotFlags, ThreadContextFlags(), out var snapshot);
            var paused = clock.Elapsed;
            if (result != 0)
            {
                Log.Warning($"Hang dump skipped: no snapshot of the process (error {result})");
                return null;
            }

            try
            {
                var callback = Marshal.AllocHGlobal(IntPtr.Size * 2);
                try
                {
                    // MINIDUMP_CALLBACK_INFORMATION: the routine and its parameter
                    Marshal.WriteIntPtr(callback, 0, Marshal.GetFunctionPointerForDelegate(SnapshotCallback));
                    Marshal.WriteIntPtr(callback, IntPtr.Size, IntPtr.Zero);
                    using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                    if (!MiniDumpWriteDump(snapshot, (uint)Environment.ProcessId, file.SafeFileHandle, DumpType, IntPtr.Zero, IntPtr.Zero, callback))
                    {
                        // ERROR_PARTIAL_COPY: a region could not be read – the dump is still usable
                        var error = Marshal.GetLastPInvokeError();
                        Log.Warning($"Hang dump: MiniDumpWriteDump reported 0x{error:X8}");
                    }

                    var dump = new HangDump(path, file.Length, paused, clock.Elapsed - paused);
                    Log.Info($"Hang dump written ({dump.Bytes / 1024 / 1024} MB; paused {paused.TotalMilliseconds:0} ms, written in {dump.Writing.TotalSeconds:0.0} s)");
                    return dump.Bytes > 0 ? dump : null;
                }
                finally
                {
                    Marshal.FreeHGlobal(callback);
                }
            }
            finally
            {
                _ = PssFreeSnapshot(process, snapshot);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
                                              or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warning($"Hang dump failed: {exception.Message}");
            return null;
        }
    }

    /// <summary>MiniDumpWithDataSegs | WithHandleData | WithUnloadedModules | WithPrivateReadWriteMemory | WithFullMemoryInfo | WithThreadInfo.</summary>
    private const uint DumpType = 0x1 | 0x4 | 0x20 | 0x200 | 0x800 | 0x1000;

    /// <summary>
    /// PSS_CAPTURE_VA_CLONE | HANDLES | HANDLE_NAME_INFORMATION | HANDLE_BASIC_INFORMATION | HANDLE_TYPE_SPECIFIC_INFORMATION |
    /// THREADS | THREAD_CONTEXT | THREAD_CONTEXT_EXTENDED.
    /// </summary>
    private const uint SnapshotFlags = 0x1 | 0x4 | 0x8 | 0x10 | 0x20 | 0x80 | 0x100 | 0x200;

    /// <summary>Tells MiniDumpWriteDump that its "process" is a snapshot (IsProcessSnapshotCallback → S_FALSE).</summary>
    private static readonly MiniDumpCallback SnapshotCallback = OnDumpCallback;

    /// <summary>CONTEXT_ALL of the architecture.</summary>
    private static uint ThreadContextFlags() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => 0x0001003F,
        Architecture.Arm64 => 0x0040001F,
        _ => 0x0010001F,
    };

    private static bool OnDumpCallback(IntPtr parameter, IntPtr input, IntPtr output)
    {
        const int isProcessSnapshotCallback = 16, sFalse = 1;
        // MINIDUMP_CALLBACK_INPUT (packed to 4): ULONG ProcessId, HANDLE ProcessHandle, ULONG CallbackType
        if (Marshal.ReadInt32(input, 4 + IntPtr.Size) == isProcessSnapshotCallback)
        {
            Marshal.WriteInt32(output, sFalse);
        }

        return true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool MiniDumpCallback(IntPtr parameter, IntPtr input, IntPtr output);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern int PssCaptureSnapshot(IntPtr process, uint captureFlags, uint threadContextFlags, out IntPtr snapshot);

    [DllImport("kernel32.dll")]
    private static extern int PssFreeSnapshot(IntPtr process, IntPtr snapshot);

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(IntPtr process, uint processId, SafeFileHandle file, uint type,
        IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);
}

/// <summary>A hang report: its file and how long its dump held the launcher still.</summary>
public sealed record HangReportFile(string Path, TimeSpan DumpPause);

/// <summary>A dump that was written: its size, how long the process stood still (the snapshot) and how long the file took.</summary>
public sealed record HangDump(string Path, long Bytes, TimeSpan Paused, TimeSpan Writing);
