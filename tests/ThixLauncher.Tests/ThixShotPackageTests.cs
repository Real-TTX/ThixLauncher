using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;
using Xunit.Abstractions;

namespace ThixLauncher.Tests;

public sealed class ThixShotPackageTests(ITestOutputHelper output)
{
    [LiveThixShotFact]
    public async Task The_release_packages_verify_and_Windows_installs_detects_and_removes_ThixShot()
    {
        Assert.Equal("true", Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.Equal("github-hosted", Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT"));
        Assert.True(OperatingSystem.IsWindows());

        const string appId = "thixshot";
        var app = Assert.IsType<ThixApp>(AppCatalog.Find(appId));
        Assert.Equal("Real-TTX/ThixShot", app.Repository);
        Assert.Equal(AppVersion.Release, AppService.Channel);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));

        foreach (var platform in new[] { "osx-arm64", "osx-x64" })
        {
            var release = Assert.IsType<AppRelease>(await ReleaseFeed.LatestAsync(app, AppVersion.Release, platform, timeout.Token));
            Assert.Equal($"ThixShot-{platform}.zip", release.PackageFile);
            Assert.NotNull(release.Sha256);
            var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-{release.PackageFile}");
            try
            {
                await ReleaseFeed.DownloadAsync(release.DownloadUrl, file, release.Sha256, null, timeout.Token);
                output.WriteLine($"{release.Version.Text} {release.PackageFile} SHA-256 {release.Sha256}");
            }
            finally
            {
                File.Delete(file);
            }
        }

        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "ThixShot", "ThixShot.exe");
        await AppService.DetectInstalledAsync();
        Assert.False(AppService.Get(appId).IsInstalled);
        Assert.False(File.Exists(executable));
        try
        {
            await AppService.InstallAsync(appId, cancellationToken: timeout.Token);
            await AppService.DetectInstalledAsync();
            var status = AppService.Get(appId);
            var latest = Assert.IsType<AppRelease>(status.Latest);
            var installed = Assert.IsType<InstalledApp>(status.Installed);
            Assert.Equal("ThixShot-win-x64.msi", latest.PackageFile);
            Assert.Equal(latest.Version.Text, installed.VersionText);
            Assert.Equal(executable, installed.Executable, StringComparer.OrdinalIgnoreCase);
            Assert.True(File.Exists(executable));
            Assert.True(status.CanLaunch);
            Assert.Equal(AppState.Installed, status.State);
            output.WriteLine($"{latest.Version.Text} {latest.PackageFile} SHA-256 {latest.Sha256}; detected {installed.Executable}");
        }
        finally
        {
            await AppService.DetectInstalledAsync();
            if (AppService.Get(appId).IsInstalled)
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                await AppService.UninstallAsync(appId, cleanupTimeout.Token);
            }
        }

        await AppService.DetectInstalledAsync();
        Assert.Equal(AppState.NotInstalled, AppService.Get(appId).State);
        Assert.False(File.Exists(executable));
    }
}

internal sealed class LiveThixShotFactAttribute : FactAttribute
{
    public LiveThixShotFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("THIXLAUNCHER_LIVE_THIXSHOT") != "1")
        {
            Skip = "Opt in with the verify_thixshot workflow input on a fresh GitHub-hosted Windows runner.";
        }
    }
}
