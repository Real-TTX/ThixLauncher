using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixLauncher.Controls;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Dialogs;

/// <summary>
/// The questions of an app's setup before its first install (SETUP-08): one switch per option the app offers on this system
/// (<see cref="Core.Business.AppService.InstallOptionsOf"/>) – Desktop-Verknüpfung, Beim Anmelden starten, Updates automatisch
/// installieren, and "&lt;.ext&gt;-Dateien mit &lt;App&gt; öffnen" per file type –, each set to the app's default. Installieren / Abbrechen;
/// Esc cancels. The dialog only asks: the business layer turns the answers into the setup's arguments.
/// </summary>
public sealed class InstallOptionsDialog : MatWindow
{
    public const string ShortcutKey = "shortcut";
    public const string AutostartKey = "autostart";
    public const string UpdatesKey = "updates";
    public const string FileTypeKey = "type:";

    private readonly Dictionary<string, ToggleSwitch> _switches = new(StringComparer.Ordinal);
    private readonly InstallerConfig _config;
    private bool _confirmed;

    private InstallOptionsDialog(ThixApp app, InstallerConfig config)
    {
        _config = config;
        Title = $"{app.Name} installieren";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this.Use(BackgroundProperty, "Mat.Surface");

        InstallButton = new Button
        {
            Content = new IconLabel { Icon = "download", Text = "Installieren" },
            IsDefault = true,
            Theme = Ui.Theme("MatButton.Primary"),
        };
        InstallButton.Click += (_, _) =>
        {
            _confirmed = true;
            Close();
        };
        var cancel = new Button { Content = new IconLabel { Icon = "x", Text = "Abbrechen" }, IsCancel = true };
        cancel.Click += (_, _) => Close();

        var heading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Margin = new Thickness(0, 0, 0, 10),
            Children =
            {
                new AppLogo { AppId = app.Id, FallbackIcon = app.Icon, Size = 40 },
                new TextBlock { Text = Title, FontSize = MatTokens.Type.FsLg, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var text = new TextBlock
        {
            Text = $"So richtet sich {app.Name} ein – ändern kannst du das später in {app.Name}.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");

        var rows = new StackPanel();
        foreach (var (key, title, description, isOn) in Questions(app, config))
        {
            var toggle = new ToggleSwitch { IsChecked = isOn, OnContent = string.Empty, OffContent = string.Empty };
            AutomationProperties.SetName(toggle, title);
            _switches[key] = toggle;
            rows.Children.Add(new MatSettingRow { Title = title, Description = description, Content = toggle });
        }

        if (rows.Children.LastOrDefault() is MatSettingRow last)
        {
            last.BorderThickness = new Thickness(0);
        }

        var card = new Border { Padding = new Thickness(16, 0), Margin = new Thickness(0, 0, 0, 22), Child = rows }.Themed("MatCard");
        Content = new StackPanel { Margin = new Thickness(24, 22), Children = { heading, text, card, new ActionBar { Children = { InstallButton, cancel } } } };
        Opened += (_, _) => InstallButton.Focus();
    }

    /// <summary>The switches by their key ("shortcut", "autostart", "updates", "type:.md").</summary>
    internal IReadOnlyDictionary<string, ToggleSwitch> Switches => _switches;

    internal Button InstallButton { get; }

    /// <summary>The answers as they stand (an option that is not asked stays null).</summary>
    internal InstallChoices Choices => new(
        Answer(ShortcutKey),
        Answer(AutostartKey),
        Answer(UpdatesKey),
        _config.FileTypes.Count == 0
            ? null
            : _config.FileTypes.Where(type => Answer(FileTypeKey + type.Extension) == true).Select(type => type.Extension).ToList());

    /// <summary>Asks the questions; the answers, or null when the user cancelled.</summary>
    public static async Task<InstallChoices?> AskAsync(Window? owner, ThixApp app, InstallerConfig config)
    {
        var dialog = new InstallOptionsDialog(app, config);
        if (owner is { IsVisible: true })
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            // No visible window (tray): an own window in the middle of the screen.
            var closed = new TaskCompletionSource();
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Topmost = true;
            dialog.ShowInTaskbar = true;
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.Show();
            dialog.Activate();
            await closed.Task;
        }

        return dialog._confirmed ? dialog.Choices : null;
    }

    /// <summary>The questions in the order of the setup assistant, each with the app's default.</summary>
    private static IEnumerable<(string Key, string Title, string Description, bool IsOn)> Questions(ThixApp app, InstallerConfig config)
    {
        if (config.DesktopShortcut is { } shortcut)
        {
            yield return (ShortcutKey, "Desktop-Verknüpfung", "Ein Startmenü-Eintrag wird immer angelegt", shortcut.Default);
        }

        if (config.Autostart is { } autostart)
        {
            yield return (AutostartKey, "Beim Anmelden starten", $"{app.Name} startet mit deiner Anmeldung im Hintergrund", autostart.Default);
        }

        if (config.AutomaticUpdates is { } updates)
        {
            yield return (UpdatesKey, "Updates automatisch installieren", $"Neue Versionen von {app.Name} ohne Rückfrage", updates.Default);
        }

        foreach (var type in config.FileTypes)
        {
            yield return (FileTypeKey + type.Extension, $"{type.Extension}-Dateien mit {app.Name} öffnen", type.Name, type.Default);
        }
    }

    private bool? Answer(string key) => _switches.TryGetValue(key, out var toggle) ? toggle.IsChecked == true : null;
}
