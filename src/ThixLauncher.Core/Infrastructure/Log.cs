using System.Text;

namespace ThixLauncher.Core.Infrastructure;

/// <summary>
/// A small daily log file in the data folder. Never write passwords or tokens into it: the log is
/// what a user sends along with a problem. The newest lines are also kept in memory (<see cref="Recent"/>): a hang report
/// names them without reading the disk.
/// </summary>
public static class Log
{
    private const int KeptLines = 40;
    private static readonly object Gate = new();
    private static readonly Queue<string> RecentLines = new();

    public static void Info(string message) => Write("INFO ", message);

    public static void Warning(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}: {exception}");

    /// <summary>The newest lines written in this run (oldest first, at most <paramref name="count"/>); never waits for a file write.</summary>
    public static IReadOnlyList<string> Recent(int count)
    {
        lock (RecentLines)
        {
            return RecentLines.Skip(Math.Max(0, RecentLines.Count - count)).ToList();
        }
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {level} {message}";
        lock (RecentLines)
        {
            RecentLines.Enqueue(line);
            while (RecentLines.Count > KeptLines)
            {
                RecentLines.Dequeue();
            }
        }

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                var path = Path.Combine(AppPaths.LogDirectory, $"thixlauncher-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // logging must never break the application
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
