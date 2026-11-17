using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher;

/// <summary>
/// Small helpers for code-built UI: the app icon and logos, the UI thread, opening addresses, texts of times, sizes and states.
/// Colours, sizes and named looks are MatDesktop's (<c>control.Use(property, "Mat.TextMuted")</c>,
/// <c>control.Themed("MatButton.Primary")</c>), icons are <c>MatIcon</c> with the name of the sprite.
/// </summary>
internal static class Ui
{
    private static readonly Dictionary<string, Bitmap?> AppLogos = new(StringComparer.OrdinalIgnoreCase);
    private static WindowIcon? _appIcon;
    private static Bitmap? _logo;

    /// <summary>The icon of every window of ThixLauncher (the task bar, Alt+Tab): MatThemeOptions.Icon gives it to every MatWindow.</summary>
    public static WindowIcon AppIcon => _appIcon ??= new WindowIcon(AssetLoader.Open(new Uri("avares://ThixLauncher/Assets/thixlauncher.ico")));

    /// <summary>The logo (the mark of the launcher), for the title bar and the header of the setup.</summary>
    public static Bitmap Logo => _logo ??= new Bitmap(AssetLoader.Open(new Uri("avares://ThixLauncher/Assets/logo-256.png")));

    /// <summary>The logo of an app of the catalog (Assets/apps/&lt;id&gt;.png), or null when there is none.</summary>
    public static Bitmap? AppLogo(string appId)
    {
        if (AppLogos.TryGetValue(appId, out var cached))
        {
            return cached;
        }

        var uri = new Uri($"avares://ThixLauncher/Assets/apps/{appId}.png");
        var logo = AssetLoader.Exists(uri) ? new Bitmap(AssetLoader.Open(uri)) : null;
        AppLogos[appId] = logo;
        return logo;
    }

    /// <summary>
    /// A named look of MatDesktop (a control theme such as "MatButton.Primary" or "MatCardRow") for an object initializer;
    /// anywhere else <c>control.Themed(key)</c>. A control theme is the same in light and dark.
    /// </summary>
    public static ControlTheme Theme(string key) =>
        Application.Current!.TryGetResource(key, null, out var value) && value is ControlTheme theme
            ? theme
            : throw new InvalidOperationException($"The control theme {key} is missing (MatDesktop/Themes/Controls.axaml).");

    /// <summary>Runs the action on the UI thread (events of the business layer come from any thread).</summary>
    public static void Post(Action action) => Dispatcher.UIThread.Post(action);

    /// <summary>The window that hosts a control (null while it is not shown).</summary>
    public static Window? WindowOf(Control control) => TopLevel.GetTopLevel(control) as Window;

    /// <summary>Ctrl on Windows, Cmd on macOS: the modifier of the app's own shortcuts.</summary>
    public static KeyModifiersName CommandKey => OperatingSystem.IsMacOS() ? new("⌘", Avalonia.Input.KeyModifiers.Meta) : new("Strg", Avalonia.Input.KeyModifiers.Control);

    /// <summary>Tests and snapshots never open a browser: they get the address instead.</summary>
    internal static Action<string>? OpenExternalForTests { get; set; }

    /// <summary>Opens a web address or a folder with the program the system uses for it (browser, Explorer, Finder).</summary>
    public static void OpenExternal(string pathOrUrl)
    {
        if (OpenExternalForTests is { } open)
        {
            open(pathOrUrl);
            return;
        }

        try
        {
            var startInfo = OperatingSystem.IsMacOS()
                ? new System.Diagnostics.ProcessStartInfo("open") { ArgumentList = { pathOrUrl }, UseShellExecute = false }
                : OperatingSystem.IsLinux()
                    ? new System.Diagnostics.ProcessStartInfo("xdg-open") { ArgumentList = { pathOrUrl }, UseShellExecute = false }
                    : new System.Diagnostics.ProcessStartInfo(pathOrUrl) { UseShellExecute = true };
            System.Diagnostics.Process.Start(startInfo)?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warning($"Could not open {pathOrUrl}: {exception.Message}");
        }
    }

    /// <summary>"Heute, 13:47", "Gestern, 09:12" or "12.11.2026, 13:47".</summary>
    public static string FormatDateTime(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        if (local.Date == AppClock.Today)
        {
            return $"Heute, {local:HH:mm}";
        }

        return local.Date == AppClock.Today.AddDays(-1) ? $"Gestern, {local:HH:mm}" : local.ToString("dd.MM.yyyy, HH:mm");
    }

    /// <summary>"12.11.2026".</summary>
    public static string FormatDate(DateTimeOffset value) => value.ToLocalTime().ToString("dd.MM.yyyy");

    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.0} MB",
    };

    /// <summary>The state of an app in words ("Installiert", "Update verfügbar" …).</summary>
    public static string StateText(AppStatus status) => status.State switch
    {
        AppState.Installing => "Wird installiert …",
        AppState.Updating => "Wird aktualisiert …",
        AppState.Removing => "Wird entfernt …",
        AppState.UpdateAvailable => "Update verfügbar",
        AppState.Installed => "Installiert",
        _ when status.Latest is null => "Nicht verfügbar",
        _ => "Nicht installiert",
    };

    /// <summary>The look of the state: the brush of its badge and its text (MatDesktop's state colours).</summary>
    public static (string Background, string Foreground) StateColors(AppStatus status) => status.State switch
    {
        AppState.UpdateAvailable => ("Mat.WarningBg", "Mat.WarningText"),
        AppState.Installed => ("Mat.SuccessBg", "Mat.SuccessText"),
        AppState.Installing or AppState.Updating or AppState.Removing => ("Mat.InfoBg", "Mat.InfoText"),
        _ => ("Mat.Surface3", "Mat.TextMuted"),
    };

    /// <summary>The line under the name: "Installiert: 0.1.9 · Neu: 0.1.12" and the like.</summary>
    public static string VersionLine(AppStatus status)
    {
        if (status.Progress is { } progress)
        {
            return progress.Fraction is { } fraction ? $"{progress.Step} – {fraction:P0}" : progress.Step;
        }

        var installed = status.Installed?.VersionText;
        var latest = status.Latest?.Version.Text;
        return status.State switch
        {
            AppState.UpdateAvailable => $"Installiert: {installed} · Neu: {latest}",
            AppState.Installed => installed is null ? "Installiert" : $"Version {installed}",
            _ when latest is not null => $"Version {latest}" + (status.Latest!.Size is { } size ? $" · {FormatBytes(size)}" : ""),
            _ when status.FeedError is not null => status.FeedError,
            _ => "Für dieses System gibt es noch keinen Build.",
        };
    }
}

/// <summary>The name and the modifier of the command key of the platform.</summary>
internal sealed record KeyModifiersName(string Name, Avalonia.Input.KeyModifiers Modifier);
