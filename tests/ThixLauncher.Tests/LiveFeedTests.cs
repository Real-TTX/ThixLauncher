using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Tests;

/// <summary>
/// The real feeds of the apps on GitHub (read only: nothing is installed; the store's pictures and commit feeds are read into a
/// temporary folder). They run only with
/// THIXLAUNCHER_LIVE_FEEDS=1, so CI and offline runs do not depend on GitHub and its hourly limit.
/// </summary>
public sealed class LiveFeedTests
{
    private static readonly bool IsEnabled = Environment.GetEnvironmentVariable("THIXLAUNCHER_LIVE_FEEDS") == "1";

    [Fact]
    public async Task Every_app_of_the_catalog_has_a_release_build_for_every_platform()
    {
        if (!IsEnabled)
        {
            return;
        }

        ReleaseFeed.HandlerForTests = null;
        foreach (var app in AppCatalog.Apps)
        {
            foreach (var platform in new[] { "win-x64", "osx-arm64", "osx-x64" })
            {
                var release = await ReleaseFeed.LatestAsync(app, AppVersion.Release, platform, CancellationToken.None);
                Assert.True(release is not null, $"{app.Name} has no release build for {platform}");
                Assert.Equal(AppVersion.Release, release.Version.Channel);
                Assert.True(release.Sha256 is { Length: 64 }, $"{app.Name} {platform}: no checksum");
                Assert.StartsWith("https://github.com/" + app.Repository + "/releases/download/", release.DownloadUrl);
            }
        }
    }

    [Fact]
    public async Task Every_picture_of_the_catalog_is_in_its_repository_and_every_repository_has_changes()
    {
        if (!IsEnabled)
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), "thixlauncher-live", Guid.NewGuid().ToString("N"));
        AppPaths.UseDirectoryForTests(folder);
        ReleaseFeed.HandlerForTests = null;
        StoreService.ResetForTests();
        try
        {
            foreach (var app in AppCatalog.Apps)
            {
                foreach (var picture in app.Screenshots)
                {
                    Assert.True(await StoreService.PictureAsync(app, picture, dark: false) is not null, $"{app.Name}: {StoreService.PictureUrl(app, picture.File)} is missing");
                }

                Assert.NotEmpty(await StoreService.ChangesAsync(app));
            }
        }
        finally
        {
            AppPaths.UseDirectoryForTests(null);
            Directory.Delete(folder, recursive: true);
        }
    }
}
