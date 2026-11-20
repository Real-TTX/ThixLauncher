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
    public async Task The_store_shows_every_app_and_the_library_the_installed_ones()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixclip")!, "0.1.9-20261110");
        await AppService.RefreshAsync();
        var window = Open();

        // the library is the start page: the installed apps, "Apps hinzufügen" last
        Assert.Same(window.Apps, window.PageHost.Content);
        Assert.Equal(["ThixClip"], window.Apps.Cards.Select(card => card.App.Name));
        Assert.True(window.Apps.ShowsAddCard);
        Assert.StartsWith("1 App installiert · 1 Update · Kanal Release · geprüft Heute, ", window.Apps.SubtitleText.Text);

        // the store: every app with its state and action
        window.ShowStore();
        Settle(window);
        var rows = window.Store.Cards;
        Assert.Equal(["ThixCalc", "ThixCalendar", "ThixClip", "ThixFred", "ThixNote", "ThixShow", "ThixTunnel", "ThixWrite"], rows.Select(row => row.App.Name));
        var clip = rows[2];
        Assert.Equal("Aktualisieren", clip.ActionText);
        Assert.Equal("Update verfügbar", clip.Badge.Text);
        Assert.Contains("Installiert: 0.1.9-20261110 · Neu: 0.1.12-20261115", clip.VersionText);
        Assert.Equal("Installieren", rows[0].ActionText);
        Assert.Equal("Nicht installiert", rows[0].Badge.Text);

        // the installed app sits in the title bar and starts with one click
        Assert.Single(window.QuickLaunchButtons);
        window.QuickLaunchButtons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(["thixclip"], data.System.Launched);

        Assert.StartsWith("8 Thix-Apps · 7 noch nicht installiert", window.Store.SubtitleText.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Installing_from_the_store_updates_the_tile_the_library_and_the_title_bar()
    {
        using var data = new TestData();
        await AppService.RefreshAsync();
        var window = Open();
        Assert.Empty(window.Apps.Cards);
        var note = window.Store.Cards.Single(row => row.App.Id == "thixnote");

        // ThixNote offers install options (SETUP-08): they are asked first, set to its defaults
        var install = AppActions.RunAsync(AppAction.Install, note.Status!, window);
        Settle(window);
        var options = window.OwnedWindows.OfType<Dialogs.InstallOptionsDialog>().Single();
        Assert.True(options.Switches["type:.md"].IsChecked);
        Assert.False(options.Switches["type:.txt"].IsChecked);
        Assert.False(options.Switches["autostart"].IsChecked);
        options.Switches["autostart"].IsChecked = true;
        options.Switches["type:.txt"].IsChecked = true;
        options.InstallButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        await WaitUntil(window, () => install.IsCompleted);
        Assert.Equal("ThixNote 1.1.1-20261116 ist installiert.", await install);
        Settle(window);
        Assert.Contains("--autostart=yes", data.System.Setups.Single().Setup.WindowsArguments);
        Assert.Contains("--file-types=.md,.txt", data.System.Setups.Single().Setup.WindowsArguments);

        Assert.Equal("Öffnen", note.ActionText);
        Assert.Equal("Installiert", note.Badge.Text);
        Assert.Single(window.QuickLaunchButtons);
        Assert.Equal(["ThixNote"], window.Apps.Cards.Select(card => card.App.Name));
        window.Close();
    }

    [AvaloniaFact]
    public async Task Cancelling_the_install_options_installs_nothing_and_an_app_without_them_asks_nothing()
    {
        using var data = new TestData();
        await AppService.RefreshAsync();
        var window = Open();

        var cancelled = AppActions.RunAsync(AppAction.Install, AppService.Get("thixnote"), window);
        Settle(window);
        var options = window.OwnedWindows.OfType<Dialogs.InstallOptionsDialog>().Single();
        Assert.Equal(OperatingSystem.IsWindows(), options.Switches.ContainsKey("shortcut"));
        options.Close();
        Assert.Null(await cancelled);
        Assert.False(AppService.Get("thixnote").IsInstalled);
        Assert.Empty(data.System.Setups);

        // ThixClip's build has no install options: it installs straight away, the old way
        var install = AppActions.RunAsync(AppAction.Install, AppService.Get("thixclip"), window);
        await WaitUntil(window, () => install.IsCompleted);
        Assert.Empty(window.OwnedWindows.OfType<Dialogs.InstallOptionsDialog>());
        Assert.True(AppService.Get("thixclip").IsInstalled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Keyboard_goes_through_library_store_and_updates_opens_pages_and_goes_back()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixnote")!, "1.1.1-20261116");
        await AppService.RefreshAsync();
        var window = Open();

        // Ctrl/Cmd+1: the library, the installed apps only
        window.KeyPressQwerty(PhysicalKey.Digit1, RawCommand);
        Settle(window);
        Assert.Equal(AppFilter.Installed, window.Apps.Filter);
        Assert.Equal(["ThixNote"], window.Apps.Cards.Select(row => row.App.Name));
        Assert.Equal("Bibliothek", window.Apps.TitleText.Text);

        // Enter on the library: the page of the chosen app ("Bibliothek" back); Esc: back with the app still chosen
        window.Apps.Select("thixnote");
        window.Apps.FocusList();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(window);
        var detail = Assert.IsType<AppDetailPage>(window.PageHost.Content);
        Assert.Equal("thixnote", detail.AppId);
        Assert.Equal("ThixNote", detail.NameText.Text);
        Assert.Equal("Bibliothek", detail.BackText);
        Assert.Equal("1.1.1-20261116", detail.InstalledText.Text);
        Assert.True(detail.UninstallButton.IsVisible);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(window);
        Assert.Same(window.Apps, window.PageHost.Content);
        Assert.Equal("thixnote", window.Apps.Selected?.App.Id);

        // Ctrl/Cmd+3: the updates – none
        window.KeyPressQwerty(PhysicalKey.Digit3, RawCommand);
        Settle(window);
        Assert.Equal("Updates", window.Apps.TitleText.Text);
        Assert.Empty(window.Apps.Cards);
        Assert.False(window.Apps.ShowsAddCard);

        // Ctrl/Cmd+2: the store; Enter on a tile opens its page ("Store" back), Esc goes back to the store
        window.KeyPressQwerty(PhysicalKey.Digit2, RawCommand);
        Settle(window);
        Assert.Same(window.Store, window.PageHost.Content);
        window.Store.Select("thixclip");
        window.Store.FocusList();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(window);
        Assert.Equal("thixclip", Assert.IsType<AppDetailPage>(window.PageHost.Content).AppId);
        Assert.Equal("Store", window.Detail.BackText);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(window);
        Assert.Same(window.Store, window.PageHost.Content);

        // Ctrl/Cmd+F in the store searches every app; in the library only the installed ones
        window.KeyPressQwerty(PhysicalKey.F, RawCommand);
        Settle(window);
        window.KeyTextInput("clip");
        Settle(window);
        Assert.Equal(["ThixClip"], window.Store.Cards.Select(row => row.App.Name));
        Assert.Null(window.Store.HeroAppId);

        window.KeyPressQwerty(PhysicalKey.Digit1, RawCommand);
        window.KeyPressQwerty(PhysicalKey.F, RawCommand);
        Settle(window);
        window.KeyTextInput("clip");
        Settle(window);
        Assert.Empty(window.Apps.Cards);
        Assert.Contains("im Store gibt es alle Apps", window.Apps.EmptyText.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_add_tile_of_the_library_opens_the_store()
    {
        using var data = new TestData();
        await AppService.RefreshAsync();
        var window = Open();

        // nothing installed yet: the library is only the add tile
        Assert.Empty(window.Apps.Cards);
        Assert.True(window.Apps.ShowsAddCard);
        window.Apps.AppList.SelectedItem = window.Apps.AddCard;
        Settle(window);
        Assert.False(window.Apps.Actions.IsVisible);
        window.Apps.AppList.ContainerFromItem(window.Apps.AddCard)!.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(window);
        Assert.Same(window.Store, window.PageHost.Content);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_store_shows_one_app_big_and_the_page_of_an_app_its_pictures_and_changes_from_GitHub()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixcalc")!, "0.1.1-20261119");
        await AppService.RefreshAsync();
        var window = Open();
        window.ShowStore();
        Settle(window);

        // the first app not installed is big; ‹ › go round the catalog
        Assert.Equal("thixcalendar", window.Store.HeroAppId);
        window.Store.MoveHero(1);
        Assert.Equal("thixclip", window.Store.HeroAppId);
        window.Store.MoveHero(-3);
        Assert.Equal("thixwrite", window.Store.HeroAppId);

        // every tile gets its cover from the app's repository
        var clip = window.Store.Cards.Single(card => card.App.Id == "thixclip");
        await WaitUntil(window, () => clip.Cover.HasPicture);
        Assert.Contains("https://raw.githubusercontent.com/Real-TTX/ThixClip/main/docs/images/main-history-light.png", data.GitHub.Requests);

        // the page of an app: a small picture per screenshot, a click shows it big; the changes of its repository
        window.ShowDetails("thixclip");
        Settle(window);
        var detail = window.Detail;
        Assert.Equal(AppCatalog.Find("thixclip")!.Screenshots.Count, detail.ThumbnailButtons.Count);
        await WaitUntil(window, () => detail.BigPicture.HasPicture);
        Assert.Equal("main-history-light.png", detail.BigPicture.Picture?.File);
        detail.ThumbnailButtons[2].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);
        Assert.Equal(AppCatalog.Find("thixclip")!.Screenshots[2], detail.BigPicture.Picture);
        Assert.Contains("Bild 3 von 4", detail.CaptionText.Text);
        Assert.Contains("current", detail.ThumbnailButtons[2].Classes);

        await WaitUntil(window, () => detail.ChangeTitles.Count > 0);
        Assert.Equal(data.GitHub.Commits.Select(commit => commit.Message.Split('\n')[0]), detail.ChangeTitles);
        Assert.Contains("https://github.com/Real-TTX/ThixClip/commits/main.atom", data.GitHub.Requests);
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_apps_switch_between_tiles_and_list_and_the_choice_stays()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixclip")!, "0.1.9-20261110");
        await AppService.RefreshAsync();
        var window = Open();

        // tiles first, like a launcher; a tile names the versions short
        Assert.Equal(AppsView.Tiles, window.Apps.View);
        Assert.All(window.Apps.Cards, card => Assert.Equal(AppsView.Tiles, card.View));
        Assert.Equal("0.1.9 → 0.1.12", Ui.ShortVersionLine(AppService.Get("thixclip")));
        Assert.True(window.Apps.Select("thixclip"));

        window.Apps.ListButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);
        Assert.Equal(AppsView.List, window.Apps.View);
        Assert.All(window.Apps.Cards, card => Assert.Equal(AppsView.List, card.View));
        Assert.Equal(AppsView.List, window.Apps.AddCard.View);
        Assert.Single(window.Apps.Cards);
        Assert.Equal("thixclip", window.Apps.Selected?.App.Id);
        Assert.Equal(AppsView.List, SettingsService.Get().AppsView);
        window.Close();

        // the next window opens with the list
        var again = Open();
        Assert.Equal(AppsView.List, again.Apps.View);
        again.Apps.TilesButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(again);
        Assert.Equal(AppsView.Tiles, SettingsService.Get().AppsView);
        again.Close();
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

        var rows = window.Store.Cards;
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
