using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Models;
using ThixLauncher.Pages;

[assembly: AvaloniaTestApplication(typeof(ThixLauncher.Tests.UiTestApp))]

namespace ThixLauncher.Tests;

/// <summary>The real app (App.axaml, MatDesktop's theme, the pages) rendered headless with Skia: real pixels, no screen needed.</summary>
public static class UiTestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// The main window driven like a user would (mouse and keyboard), and the pictures of every window in light and dark
/// (set THIXLAUNCHER_SCREENSHOTS to a folder to keep them; docs/images has the ones of the README).
/// </summary>
public sealed class UiTests
{
    private static readonly string? ShotFolder = Environment.GetEnvironmentVariable("THIXLAUNCHER_SCREENSHOTS");

    private static KeyModifiers Command => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    private static RawInputModifiers RawCommand => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    [AvaloniaFact]
    public async Task The_list_shows_every_app_with_its_state_and_action()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixclip")!, "0.1.9-20261110");
        await AppService.RefreshAsync();
        var window = Open();

        var rows = window.Apps.Rows;
        Assert.Equal(["ThixCalendar", "ThixClip", "ThixFred", "ThixNote", "ThixTunnel", "ThixWrite"], rows.Select(row => row.App.Name));
        var clip = rows[1];
        Assert.Equal("Aktualisieren", clip.ActionText);
        Assert.Equal("Update verfügbar", clip.Badge.Text);
        Assert.Contains("Installiert: 0.1.9-20261110 · Neu: 0.1.12-20261115", clip.VersionText);
        Assert.Equal("Installieren", rows[0].ActionText);
        Assert.Equal("Nicht installiert", rows[0].Badge.Text);

        // the installed app sits in the title bar and starts with one click
        Assert.Single(window.QuickLaunchButtons);
        window.QuickLaunchButtons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(["thixclip"], data.System.Launched);

        // the menu counts the updates
        Assert.StartsWith("6 Apps · 1 installiert · 1 Update · Kanal Release · geprüft Heute, ", window.Apps.SubtitleText.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Installing_from_the_row_updates_the_row_and_the_title_bar()
    {
        using var data = new TestData();
        await AppService.RefreshAsync();
        var window = Open();
        var note = window.Apps.Rows.Single(row => row.App.Id == "thixnote");

        var install = AppActions.RunAsync(AppAction.Install, note.Status!, window);
        await WaitUntil(window, () => install.IsCompleted);
        Assert.Equal("ThixNote 1.1.1-20261116 ist installiert.", await install);
        Settle(window);

        Assert.Equal("Öffnen", note.ActionText);
        Assert.Equal("Installiert", note.Badge.Text);
        Assert.Single(window.QuickLaunchButtons);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Keyboard_filters_opens_details_and_goes_back()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixnote")!, "1.1.1-20261116");
        await AppService.RefreshAsync();
        var window = Open();

        // Ctrl/Cmd+2: the installed apps only
        window.KeyPressQwerty(PhysicalKey.Digit2, RawCommand);
        Settle(window);
        Assert.Equal(AppFilter.Installed, window.Apps.Filter);
        Assert.Equal(["ThixNote"], window.Apps.Rows.Select(row => row.App.Name));
        Assert.Equal("Installierte Apps", window.Apps.TitleText.Text);

        // Enter on the list: the page of the chosen app; Esc: back to the list with the app still chosen
        window.Apps.FocusList();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(window);
        var detail = Assert.IsType<AppDetailPage>(window.PageHost.Content);
        Assert.Equal("thixnote", detail.AppId);
        Assert.Equal("ThixNote", detail.NameText.Text);
        Assert.Equal("1.1.1-20261116", detail.InstalledText.Text);
        Assert.True(detail.UninstallButton.IsVisible);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(window);
        Assert.Same(window.Apps, window.PageHost.Content);
        Assert.Equal("thixnote", window.Apps.Selected?.App.Id);

        // Ctrl/Cmd+F: the search, typing filters at once
        window.KeyPressQwerty(PhysicalKey.Digit1, RawCommand);
        window.KeyPressQwerty(PhysicalKey.F, RawCommand);
        Settle(window);
        window.KeyTextInput("clip");
        Settle(window);
        Assert.Equal(["ThixClip"], window.Apps.Rows.Select(row => row.App.Name));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Removing_asks_first_and_keeps_the_app_when_cancelled()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixclip")!, "0.1.12-20261115");
        await AppService.RefreshAsync();
        var window = Open();

        var cancelled = AppActions.RunAsync(AppAction.Uninstall, AppService.Get("thixclip"), window);
        Settle(window);
        var dialog = window.OwnedWindows.OfType<Dialogs.ConfirmDialog>().Single();
        dialog.Close();
        Assert.Null(await cancelled);
        Assert.True(AppService.Get("thixclip").IsInstalled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Under_the_policy_the_buttons_that_change_apps_are_off()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixclip")!, "0.1.9-20261110");
        data.Policies.Add("DisableAppChanges");
        await AppService.RefreshAsync();
        var window = Open();

        var rows = window.Apps.Rows;
        Assert.Equal("Öffnen", rows.Single(row => row.App.Id == "thixclip").ActionText);
        Assert.Null(rows.Single(row => row.App.Id == "thixnote").MainAction.Action);
        window.Apps.Select("thixclip");
        Settle(window);
        Assert.False(window.Apps.UninstallButton.IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public void Pictures_of_every_window_in_light_and_dark()
    {
        var folder = ShotFolder ?? Path.Combine(Path.GetTempPath(), "thixlauncher-shots", Guid.NewGuid().ToString("N"));
        using var data = new TestData();
        var report = Snapshot.Render(folder);

        foreach (var file in report.Written)
        {
            Assert.True(new FileInfo(Path.Combine(folder, file)).Length > 1000, file);
        }

        Assert.Equal(Snapshot.FileNames.Count, report.Written.Count + report.Skipped.Count);
        if (ShotFolder is null)
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_copy_of_MatDesktop_is_unchanged()
    {
        var folder = Path.Combine(Repository.Root, "src", "ThixLauncher", "MatDesktop");
        var all = File.ReadAllLines(Path.Combine(folder, "matdesktop.version"));
        Assert.StartsWith("MatDesktop ", all[0]);
        var lines = all.Skip(1).Where(line => line.Length > 0).ToList();
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            var hash = line[..64];
            var file = line[66..];
            // Git may check the files out with CRLF (core.autocrlf): the hash is of the text with LF, as MatDesign copied it.
            var text = File.ReadAllText(Path.Combine(folder, file)).Replace("\r\n", "\n", StringComparison.Ordinal);
            var actual = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            Assert.True(hash == actual, $"MatDesktop/{file} was edited: change it in MatDesign and copy it again (tools/matdesktop-copy.js).");
        }
    }

    [Fact]
    public void The_copy_of_the_MatCloud_contract_is_unchanged()
    {
        var folder = Path.Combine(Repository.Root, "src", "ThixLauncher.Core", "MatCloud");
        var all = File.ReadAllLines(Path.Combine(folder, "matcloud.version"));
        Assert.StartsWith("MatCloud.Contract ", all[0]);
        var lines = all.Skip(1).Where(line => line.Length > 0).ToList();
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            var hash = line[..64];
            var file = line[66..];
            var text = File.ReadAllText(Path.Combine(folder, file)).Replace("\r\n", "\n", StringComparison.Ordinal);
            var actual = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            Assert.True(hash == actual, $"MatCloud/{file} was edited: change it in MatCloud and copy it again (node ../MatCloud/tools/contract-copy.js src/ThixLauncher.Core).");
        }
    }

    [AvaloniaFact]
    public async Task The_cloud_tab_signs_in_in_the_browser_and_shows_the_account()
    {
        using var data = new TestData();
        var opened = new List<string>();
        Ui.OpenExternalForTests = opened.Add;
        try
        {
            var window = Open();
            window.ShowSettings(SettingsPage.CloudTab);
            Settle(window);
            var page = (SettingsPage)window.PageHost.Content!;
            Assert.True(page.CloudLogin.IsVisible);
            Assert.False(page.CloudConnected.IsVisible);
            Assert.Equal(Core.Infrastructure.AppInfo.MachineName, page.DeviceNameBox.Text);

            page.ServerUrlBox.Text = "cloud.example.com";
            page.BrowserConnectButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await WaitUntil(window, () => page.BrowserCode.IsVisible);
            Assert.Equal(Core.Infrastructure.SampleCloud.Code, page.BrowserCodeText.Text);
            Assert.Equal(["https://cloud.example.com/Connect?code=ACDE-FGHJ"], opened);

            data.Cloud.ApproveLink();
            await WaitUntil(window, () => page.CloudConnected.IsVisible);
            Assert.False(page.CloudLogin.IsVisible);
            Assert.Equal("Anna Beispiel (anna)", page.CloudAccountText.Text);
            Assert.Equal("Verbunden", page.CloudStateText.Text);
            Assert.True(page.ConnectAppsSwitch.IsChecked);

            page.ConnectAppsSwitch.IsChecked = false;
            Assert.False(SettingsService.Get().ConnectInstalledApps);
            window.Close();
        }
        finally
        {
            Ui.OpenExternalForTests = null;
        }
    }

    private static MainWindow Open()
    {
        var window = new MainWindow();
        window.ShowWindow();
        Settle(window);
        return window;
    }

    private static void Settle(Window window)
    {
        for (var round = 0; round < 6; round++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        window.UpdateLayout();
    }

    private static async Task WaitUntil(Window window, Func<bool> condition)
    {
        for (var round = 0; round < 300 && !condition(); round++)
        {
            Settle(window);
            await Task.Delay(10);
        }

        Assert.True(condition(), "the condition was not met in time");
    }
}

/// <summary>The folder of the repository (the tests run from bin/…).</summary>
internal static class Repository
{
    public static string Root
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "ThixLauncher.slnx")))
            {
                folder = folder.Parent;
            }

            return folder?.FullName ?? throw new InvalidOperationException("ThixLauncher.slnx not found above " + AppContext.BaseDirectory);
        }
    }
}
