using ThixLauncher.Core.Business;
using ThixLauncher.Core.Models;
using ThixLauncher.Core.Platform;

namespace ThixLauncher.Tests;

/// <summary>The version rules of the family and the small parsers of the platforms.</summary>
public sealed class VersionAndPlatformTests
{
    [Theory]
    [InlineData("0.1.42-20261116", "release", 42, "0.1.42-20261116")]
    [InlineData("v1.0.2-20261116", "release", 2, "1.0.2-20261116")]
    [InlineData("1.0.2", "release", 2, "1.0.2")]
    [InlineData("0.1.9.0", "release", 9, "0.1.9.0")]
    [InlineData("nightly-43-20261116", "nightly", 43, "nightly-43-20261116")]
    [InlineData("local-20261116", "local", 0, "local-20261116")]
    public void Versions_of_the_family_are_read(string text, string channel, int build, string shown)
    {
        var version = AppVersion.Parse(text);
        Assert.NotNull(version);
        Assert.Equal(channel, version.Channel);
        Assert.Equal(build, version.BuildNumber);
        Assert.Equal(shown, version.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("release-channel")]
    [InlineData("1.0")]
    [InlineData("nightly-x-20261116")]
    public void Anything_else_is_no_version(string? text) => Assert.Null(AppVersion.Parse(text));

    [Theory]
    [InlineData("0.1.12-20261115", "0.1.9-20261110", true)]
    [InlineData("0.1.9-20261115", "0.1.9-20261110", false)]
    [InlineData("0.1.8-20261115", "0.1.9-20261110", false)]
    [InlineData("nightly-14-20261116", "nightly-10-20261115", true)]
    [InlineData("nightly-14-20261116", "0.1.9-20261110", false)]
    [InlineData("0.1.12-20261115", "nightly-10-20261115", false)]
    [InlineData("local-20261116", "local-20261115", false)]
    [InlineData("1.1.1-20261116", "1.0.5-20261114", true)] // ThixNote: a new workflow started its build numbers over
    [InlineData("1.0.5-20261114", "1.1.1-20261116", false)]
    [InlineData("1.1.2-20261117", "1.1.1-20261116", true)]
    public void Only_a_higher_build_of_the_same_channel_is_newer(string latest, string installed, bool newer) =>
        Assert.Equal(newer, AppVersion.Parse(latest)!.IsNewerThan(AppVersion.Parse(installed)!));

    [Fact]
    public void Uninstall_commands_are_split_into_program_and_arguments()
    {
        var quiet = CommandLine.Split("\"C:\\Users\\Anna\\AppData\\Local\\Programs\\ThixClip\\ThixClip.exe\" --uninstall --quiet");
        Assert.Equal(@"C:\Users\Anna\AppData\Local\Programs\ThixClip\ThixClip.exe", quiet!.Value.Program);
        Assert.Equal(["--uninstall", "--quiet"], quiet.Value.Arguments);

        var plain = CommandLine.Split(@"C:\Tools\Uninstall.exe /uninstall");
        Assert.Equal(@"C:\Tools\Uninstall.exe", plain!.Value.Program);
        Assert.Equal(["/uninstall"], plain.Value.Arguments);

        Assert.Null(CommandLine.Split("   "));
        Assert.Equal(["--update"], CommandLine.Arguments("--update"));
        Assert.Empty(CommandLine.Arguments(null));
    }

    [Theory]
    [InlineData(@"C:\Programs\ThixClip\ThixClip.exe,0", @"C:\Programs\ThixClip\ThixClip.exe")]
    [InlineData("\"C:\\Programs\\ThixNote\\ThixNote.exe\"", @"C:\Programs\ThixNote\ThixNote.exe")]
    [InlineData(@"C:\Programs\Thix, Note\ThixNote.exe", @"C:\Programs\Thix, Note\ThixNote.exe")]
    [InlineData("", null)]
    public void The_display_icon_names_the_program_file(string icon, string? path) =>
        Assert.Equal(path, InstallInfo.DisplayIconPath(icon));

    [Fact]
    public void A_mac_bundle_shows_its_full_version_when_it_has_one()
    {
        var clip = AppCatalog.Find("thixclip")!;
        var note = AppCatalog.Find("thixnote")!;
        Assert.Equal("0.1.9-20261115", InstallInfo.BundleVersion(clip, new Dictionary<string, string>
        {
            ["CFBundleGetInfoString"] = "ThixClip 0.1.9-20261115",
            ["CFBundleShortVersionString"] = "0.1.9",
        }));
        Assert.Equal("1.0.2", InstallInfo.BundleVersion(note, new Dictionary<string, string> { ["CFBundleShortVersionString"] = "1.0.2" }));
        Assert.Null(InstallInfo.BundleVersion(note, new Dictionary<string, string>()));
    }

    [Fact]
    public void The_launch_agent_starts_the_launcher_hidden()
    {
        var plist = AutostartService.LaunchAgent("/Applications/ThixLauncher.app/Contents/MacOS/ThixLauncher");
        Assert.Contains("<string>info.schmoldt.thixlauncher</string>", plist);
        Assert.Contains($"<string>{AutostartService.StartupArgument}</string>", plist);
        Assert.Contains("<key>RunAtLoad</key>", plist);
    }

    [Fact]
    public void Every_app_of_the_catalog_is_complete()
    {
        Assert.Equal(["thixclip", "thixfred", "thixnote"], AppCatalog.Apps.Select(app => app.Id));
        foreach (var app in AppCatalog.Apps)
        {
            Assert.Matches("^Real-TTX/Thix", app.Repository);
            Assert.EndsWith(".exe", app.WindowsExecutable);
            Assert.EndsWith(".app", app.MacBundleName);
            Assert.True(app.Feed == FeedKind.ChannelManifest ? app.ManifestFileName is not null : app.PackagePatterns.Count == 3, app.Id);
        }
    }
}
