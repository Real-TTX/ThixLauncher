using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace ThixLauncher;

/// <summary>What the UI thread did during a hang: its CPU time over the hang, whether it took window messages, whether Windows called the window hung.</summary>
internal sealed record UiThreadState(TimeSpan? CpuTime, TimeSpan Over, bool? AnswersMessages, bool? IsHungWindow);

/// <summary>
/// Looks at the UI thread from the watchdog's thread (Windows; elsewhere there is none), cheaply and without stopping it:
///  - its CPU time (GetThreadTimes): a thread that used most of the hang's time computed, one that used little waited;
///  - whether it takes a sent message (SendMessageTimeout with WM_NULL to one of its windows, 1 s): a thread that waits inside a
///    message loop – idle in GetMessage, a modal menu or move/size loop, a SendMessage or COM call to another program – handles
///    it; one that waits in a system call, on a lock or a file does not;
///  - whether Windows calls its window hung (IsHungAppWindow: no message taken for 5 s).
/// </summary>
internal sealed class UiThreadProbe
{
    private readonly uint _threadId;
    private readonly SafeWaitHandle _thread;

    private UiThreadProbe(uint threadId, SafeWaitHandle thread)
    {
        _threadId = threadId;
        _thread = thread;
    }

    /// <summary>A probe of the calling thread (call it on the UI thread); null where it does not work.</summary>
    public static UiThreadProbe? OfCurrentThread()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        const uint threadQueryLimitedInformation = 0x0800;
        var id = GetCurrentThreadId();
        var handle = OpenThread(threadQueryLimitedInformation, false, id);
        return handle.IsInvalid ? null : new UiThreadProbe(id, handle);
    }

    /// <summary>The CPU time the thread used so far (kernel + user).</summary>
    public TimeSpan? CpuTime() =>
        OperatingSystem.IsWindows() && GetThreadTimes(_thread, out _, out _, out var kernel, out var user)
            ? TimeSpan.FromTicks(kernel + user)
            : null;

    /// <summary>The state of the thread now; <paramref name="samples"/> = its CPU time a second apart, to measure the time since <paramref name="since"/>.</summary>
    public UiThreadState Look(long since, IEnumerable<(long At, TimeSpan Cpu)> samples)
    {
        var now = Stopwatch.GetTimestamp();
        var start = samples.Where(sample => sample.At <= since).Select(sample => ((long, TimeSpan)?)sample).LastOrDefault()
                    ?? samples.Select(sample => ((long, TimeSpan)?)sample).FirstOrDefault();
        var cpu = CpuTime();
        TimeSpan? used = cpu is { } total && start is { } first ? total - first.Item2 : null;
        var over = start is { } begin ? Stopwatch.GetElapsedTime(begin.Item1, now) : Stopwatch.GetElapsedTime(since, now);

        var window = OperatingSystem.IsWindows() ? WindowOfThread() : IntPtr.Zero;
        bool? answers = null;
        bool? hung = null;
        if (window != IntPtr.Zero && OperatingSystem.IsWindows())
        {
            hung = IsHungAppWindow(window);
            const uint wmNull = 0, abortIfHung = 0x2;
            answers = SendMessageTimeout(window, wmNull, IntPtr.Zero, IntPtr.Zero, abortIfHung, 1000, out _) != IntPtr.Zero;
        }

        return new UiThreadState(used, over, answers, hung);
    }

    [SupportedOSPlatform("windows")]
    private IntPtr WindowOfThread()
    {
        var found = IntPtr.Zero;
        EnumThreadWindows(_threadId, (window, _) =>
        {
            found = window;
            return false; // the first one will do
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeWaitHandle OpenThread(uint access, bool inheritHandle, uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetThreadTimes(SafeWaitHandle thread, out long creation, out long exit, out long kernel, out long user);

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsHungAppWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
