using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Tests;

/// <summary>The settings with their checks and policies, and the update of ThixLauncher itself.</summary>
public sealed class SettingsAndUpdateTests
{
    [Fact]
    public void Settings_are_saved_and_read_again()
    {
        using var data = new TestData();
        SettingsService.Update(settings => settings with { AppChannel = AppChannel.Nightly, Theme = ThemeChoice.Dark, UpdateAppsAutomatically = true });
        SettingsStore.Reset();

        var read = SettingsService.Get();
        Assert.Equal(AppChannel.Nightly, read.AppChannel);
        Assert.Equal(ThemeChoice.Dark, read.Theme);
        Assert.True(read.UpdateAppsAutomatically);
        Assert.True(File.Exists(AppPaths.SettingsFile));
    }

    [Fact]
    public void Values_that_do_not_exist_are_refused_at_their_field()
    {
        using var data = new TestData();
        var refused = Assert.Throws<BusinessException>(() => SettingsService.Update(settings => settings with { AppChannel = (AppChannel)7 }));
        Assert.Equal(nameof(AppSettings.AppChannel), refused.Field);
        Assert.Equal(AppChannel.Release, SettingsService.Get().AppChannel);
    }

    [Fact]
    public void A_locked_configuration_keeps_the_settings_but_remembers_the_window()
    {
        using var data = new TestData();
        data.Policies.Add("LockConfiguration");

        Assert.Contains("LockConfiguration", Assert.Throws<BusinessException>(() => SettingsService.Update(settings => settings with { Theme = ThemeChoice.Dark })).Message);
        Assert.Throws<BusinessException>(() => AutostartService.Enable("ThixLauncher"));
        SettingsService.Remember(settings => settings with { WindowWidth = 900, IsSidebarCollapsed = true });
        Assert.Equal(900, SettingsService.Get().WindowWidth);
        Assert.True(SettingsService.Get().IsSidebarCollapsed);
    }

    [Fact]
    public void Automatic_updates_cannot_be_switched_on_against_a_policy()
    {
        using var data = new TestData();
        SettingsService.Update(settings => settings with { InstallUpdatesAutomatically = false });
        data.Policies.Add("DisableUpdates");
        data.Policies.Add("DisableAppChanges");

        Assert.Equal(nameof(AppSettings.InstallUpdatesAutomatically),
            Assert.Throws<BusinessException>(() => SettingsService.Update(settings => settings with { InstallUpdatesAutomatically = true })).Field);
        Assert.Equal(nameof(AppSettings.UpdateAppsAutomatically),
            Assert.Throws<BusinessException>(() => SettingsService.Update(settings => settings with { UpdateAppsAutomatically = true })).Field);
    }

    [Fact]
    public void The_window_size_stays_in_sensible_bounds()
    {
        using var data = new TestData();
        SettingsService.Remember(settings => settings with { WindowWidth = 10, WindowHeight = 99_999 });
        Assert.Equal(640, SettingsService.Get().WindowWidth);
        Assert.Equal(3000, SettingsService.Get().WindowHeight);
    }

    [Fact]
    public void Autostart_is_switched_through_the_business_layer()
    {
        using var data = new TestData();
        AutostartService.Enable("ThixLauncher");
        Assert.True(AutostartService.IsEnabled());
        AutostartService.Disable();
        Assert.False(AutostartService.IsEnabled());
    }

    [Fact]
    public async Task The_launcher_finds_its_own_update_in_its_channel()
    {
        using var data = new TestData();
        data.GitHub.Versions[("thixlauncher", AppVersion.Release)] = "0.1.7-20261116";
        try
        {
            AppInfo.Pin("0.1.5-20261115", AppVersion.Release, buildNumber: 5);
            var result = await UpdateService.CheckAsync();
            Assert.True(result.IsUpdateAvailable, result.Message);
            Assert.Equal("0.1.7-20261116", result.Manifest!.Version);
            Assert.Contains(data.GitHub.Requests, url => url.EndsWith("/releases/download/release-channel/thixlauncher-update.json", StringComparison.Ordinal));

            AppInfo.Pin("0.1.7-20261116", AppVersion.Release, buildNumber: 7);
            Assert.False((await UpdateService.CheckAsync()).IsUpdateAvailable);

            AppInfo.Pin("local-20261116", AppVersion.Local, buildNumber: 0);
            Assert.Contains("Lokaler Build", (await UpdateService.CheckAsync()).Message);
        }
        finally
        {
            AppInfo.Pin(null);
        }
    }

    [Fact]
    public async Task The_policy_DisableUpdates_stops_the_update_check()
    {
        using var data = new TestData();
        data.Policies.Add("DisableUpdates");
        Assert.False(UpdateService.CanUpdate);
        await Assert.ThrowsAsync<BusinessException>(() => UpdateService.CheckAsync());
    }
}
