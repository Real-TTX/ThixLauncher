using MatCloud.Contract;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Tests;

/// <summary>ThixLauncher at MatCloud (in memory): the sign-in, and the sign-in it hands to an app it installs – never in the way of the app.</summary>
public sealed class CloudTests
{
    private static Task SignInAsync() => CloudService.ConnectAsync(SampleCloud.Address, SampleCloud.UserName, SampleCloud.Password, "ANNA-PC");

    [Fact]
    public async Task The_launcher_signs_in_with_a_password_and_out_again()
    {
        using var data = new TestData();
        var wrong = await Assert.ThrowsAsync<BusinessException>(() => CloudService.ConnectAsync("cloud.example.com", "anna", "nope", "ANNA-PC"));
        Assert.Equal("Password", wrong.Field);
        Assert.Equal("ServerUrl", (await Assert.ThrowsAsync<BusinessException>(() => CloudService.ConnectAsync(" ", "anna", "x", "PC"))).Field);
        Assert.Equal("UserName", (await Assert.ThrowsAsync<BusinessException>(() => CloudService.ConnectAsync("cloud.example.com", "", "x", "PC"))).Field);

        await CloudService.ConnectAsync("cloud.example.com/", "anna", SampleCloud.Password, "ANNA-PC");
        var cloud = SettingsService.Get().Cloud;
        Assert.True(cloud.IsConnected);
        Assert.Equal(SampleCloud.Address, cloud.ServerUrl);
        Assert.Equal("Anna Beispiel (anna)", cloud.AccountText);
        Assert.Equal([(CloudApps.Launcher, "ANNA-PC")], data.Cloud.Devices);
        Assert.Equal(CloudState.Online, CloudService.Status.State);

        // a change of the settings cannot touch the sign-in
        SettingsService.Update(settings => settings with { Cloud = new CloudSettings(), Theme = ThemeChoice.Dark });
        Assert.True(SettingsService.Get().Cloud.IsConnected);

        await CloudService.DisconnectAsync();
        Assert.False(SettingsService.Get().Cloud.IsConnected);
        Assert.Equal(SampleCloud.Address, SettingsService.Get().Cloud.ServerUrl);
        Assert.Empty(data.Cloud.Devices);
    }

    [Fact]
    public async Task The_launcher_signs_in_in_the_browser()
    {
        using var data = new TestData();
        // waits for the browser to open – the last step before the launcher polls – and remembers the code shown by then: the two callbacks
        // run one after the other on another thread, so a test that waits only for the code may look before the browser was opened
        DeviceLinkStart? shown = null;
        var opened = new TaskCompletionSource<(string Address, DeviceLinkStart? Shown)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connecting = CloudService.ConnectInBrowserAsync(SampleCloud.Address, "", start => shown = start, address => opened.TrySetResult((address, shown)));
        if (await Task.WhenAny(opened.Task, connecting).WaitAsync(TimeSpan.FromSeconds(30)) == connecting)
        {
            await connecting; // it ended before it opened the browser: show why
            Assert.Fail("The sign-in ended without opening the browser.");
        }

        var (address, codeShown) = await opened.Task;
        Assert.Equal(SampleCloud.Code, codeShown?.Code);
        Assert.Equal($"{SampleCloud.Address}/Connect?code={SampleCloud.Code}", address);
        Assert.Equal(CloudState.Connecting, CloudService.Status.State);

        data.Cloud.ApproveLink();
        Assert.True(await connecting);
        Assert.True(CloudService.IsConnected);
        Assert.Equal(AppInfo.MachineName, SettingsService.Get().Cloud.DeviceName);

        // cancelled while it waits: not connected, no error
        await CloudService.DisconnectAsync();
        using var cancel = new CancellationTokenSource();
        var cancelled = CloudService.ConnectInBrowserAsync(SampleCloud.Address, "PC", _ => cancel.Cancel(), _ => { }, cancel.Token);
        Assert.False(await cancelled);
        Assert.False(CloudService.IsConnected);
    }

    [Fact]
    public async Task An_app_installed_by_the_signed_in_launcher_takes_its_sign_in_along()
    {
        using var data = new TestData();
        await SignInAsync();
        await AppService.RefreshAsync();

        var account = await AppService.InstallAsync("thixclip");
        Assert.Equal("Anna Beispiel (anna)", account);
        Assert.Equal([CloudApps.Clip], data.Cloud.HandedOver);

        // what ThixClip finds at its first start, and the sign-in it gets with it
        var handoff = CloudHandoff.Find(CloudApps.Clip)!;
        Assert.Equal((SampleCloud.Address, SampleCloud.UserName), (handoff.ServerUrl, handoff.UserName));
        Assert.True(handoff.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23));
        using var app = new MatCloudClient(handoff.ServerUrl, null, "ThixClip/0.1.12 (Windows)", data.Cloud);
        var login = await app.RedeemHandoffAsync(handoff, CancellationToken.None);
        Assert.Equal((CloudApps.Clip, "ANNA-PC"), (login!.Account.App, login.Account.DeviceName));
        Assert.Null(await app.RedeemHandoffAsync(handoff, CancellationToken.None));

        // an update takes nothing along: the app is signed in already (or chose not to be)
        data.GitHub.Versions[("thixclip", AppVersion.Release)] = "0.1.13-20261118";
        await AppService.RefreshAsync();
        Assert.Null(await AppService.InstallAsync("thixclip"));
        Assert.Single(data.Cloud.HandedOver);
    }

    [Fact]
    public async Task Without_the_cloud_the_app_is_installed_anyway()
    {
        using var data = new TestData();
        await SignInAsync();
        await AppService.RefreshAsync();
        data.Cloud.IsReachable = false;

        Assert.Null(await AppService.InstallAsync("thixnote"));
        Assert.True(AppService.Get("thixnote").IsInstalled);
        Assert.Null(CloudHandoff.Find(CloudApps.Note));
        Assert.Equal(CloudState.Offline, CloudService.Status.State);
        Assert.True(CloudService.IsConnected);

        // the check at start does not sign out either; the next answer brings it back
        await CloudService.CheckAsync();
        Assert.Equal(CloudState.Offline, CloudService.Status.State);
        data.Cloud.IsReachable = true;
        await CloudService.CheckAsync();
        Assert.Equal(CloudState.Online, CloudService.Status.State);
    }

    [Fact]
    public async Task No_sign_in_goes_along_when_the_launcher_is_not_signed_in_or_should_not()
    {
        using var data = new TestData();
        await AppService.RefreshAsync();
        Assert.Null(await AppService.InstallAsync("thixclip"));

        await SignInAsync();
        SettingsService.Update(settings => settings with { ConnectInstalledApps = false });
        Assert.Null(await AppService.InstallAsync("thixnote"));

        // an app the administrator switched off in the cloud signs in by itself (and is refused there)
        SettingsService.Update(settings => settings with { ConnectInstalledApps = true });
        data.Cloud.SwitchedOff.Add(CloudApps.Fred);
        Assert.Null(await AppService.InstallAsync("thixfred"));

        Assert.Empty(data.Cloud.HandedOver);
        Assert.All(new[] { CloudApps.Clip, CloudApps.Note, CloudApps.Fred }, app => Assert.False(CloudHandoff.Exists(app)));
        Assert.Equal(CloudState.Online, CloudService.Status.State);
    }

    [Fact]
    public async Task A_launcher_removed_in_the_browser_is_signed_out_and_the_app_is_installed()
    {
        using var data = new TestData();
        await SignInAsync();
        await AppService.RefreshAsync();
        data.Cloud.RemoveDevices(CloudApps.Launcher);

        Assert.Null(await AppService.InstallAsync("thixclip"));
        Assert.True(AppService.Get("thixclip").IsInstalled);
        Assert.False(CloudService.IsConnected);
        Assert.Equal(CloudState.SignedOut, CloudService.Status.State);

        await SignInAsync();
        data.Cloud.RemoveDevices(CloudApps.Launcher);
        await CloudService.CheckAsync();
        Assert.Equal(CloudState.SignedOut, CloudService.Status.State);
    }

    [Fact]
    public async Task Removing_an_app_takes_a_sign_in_it_never_used_along()
    {
        using var data = new TestData();
        await SignInAsync();
        await AppService.RefreshAsync();
        await AppService.InstallAsync("thixnote");
        Assert.True(CloudHandoff.Exists(CloudApps.Note));

        await AppService.UninstallAsync("thixnote");
        Assert.False(CloudHandoff.Exists(CloudApps.Note));
    }

    [Fact]
    public async Task The_policy_DisableCloud_keeps_the_launcher_and_its_apps_away_from_the_cloud()
    {
        using var data = new TestData();
        await SignInAsync();
        data.Policies.Add("DisableCloud");
        CloudService.ResetForTests();

        await Assert.ThrowsAsync<BusinessException>(SignInAsync);
        await AppService.RefreshAsync();
        Assert.Null(await AppService.InstallAsync("thixclip"));
        Assert.Empty(data.Cloud.HandedOver);
        Assert.Equal(CloudState.Off, CloudService.Status.State);
        Assert.True(AccessPolicy.IsAnySet);
    }
}
