using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Platform;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThixLauncher.Tests;

/// <summary>
/// Every test gets its own data folder, GitHub in memory (<see cref="SampleGitHub"/>), a computer in memory
/// (<see cref="SampleAppSystem"/>), no machine policies and an autostart entry in memory. Nothing a test does touches the
/// network, the real apps, the registry or the LaunchAgents.
/// </summary>
internal sealed class TestData : IDisposable
{
    public TestData()
    {
        Folder = Path.Combine(Path.GetTempPath(), "thixlauncher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        AppPaths.UseDirectoryForTests(Folder);
        SettingsStore.Reset();
        GitHub = SampleGitHub.WithSampleReleases();
        ReleaseFeed.HandlerForTests = GitHub;
        System = new SampleAppSystem();
        AppSystem.UseForTests(System);
        AccessPolicy.UsePolicyReaderForTests(name => Policies.Contains(name));
        AutostartService.UseInMemoryEntry(false);
        AppService.ResetForTests();
    }

    public string Folder { get; }

    public SampleGitHub GitHub { get; }

    public SampleAppSystem System { get; }

    /// <summary>The machine policies that are set (e.g. "DisableAppChanges").</summary>
    public HashSet<string> Policies { get; } = [];

    public void Dispose()
    {
        AppService.ResetForTests();
        ReleaseFeed.HandlerForTests = null;
        AppSystem.UseForTests(null);
        AccessPolicy.UsePolicyReaderForTests(null);
        AutostartService.UseInMemoryEntry(null);
        SettingsStore.Reset();
        AppPaths.UseDirectoryForTests(null);
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
