using Avalonia;
using Avalonia.Controls;
using ThixLauncher.Core.Infrastructure;

namespace ThixLauncher;

internal static class Program
{
    /// <summary>
    /// One program for everything (App decides): the launcher and - on Windows - its setup.
    /// The application ends only through "Beenden" (tray / menu bar, ⌘Q), not when its window closes (DESK-06).
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        // First of all: no error ends ThixLauncher without a report (and on Windows a message box).
        var isSnapshot = args.Contains(Snapshot.Argument);
        ProblemReports.Install(showMessageBox: !args.Contains(SelfTest.Argument) && !isSnapshot);

        // The self test and the snapshots work in a fresh folder of their own (before anything reads the data folder).
        if (args.Contains(SelfTest.Argument) || isSnapshot)
        {
            Environment.SetEnvironmentVariable(AppPaths.DataDirectoryVariable,
                Path.Combine(Path.GetTempPath(), isSnapshot ? "thixlauncher-snapshot" : "thixlauncher-self-test", Guid.NewGuid().ToString("N")));
        }

        // The snapshots draw the windows headless: no screen, no tray, no single-instance check, no network.
        if (isSnapshot)
        {
            return Snapshot.Run(args);
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>Also used by the XAML previewer.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { OverlayPopups = true })
            .LogToTrace();
}
