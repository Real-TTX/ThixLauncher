using MatCloud.Contract;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Platform;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThixLauncher.Tests;

/// <summary>
/// Every test gets its own data folder, GitHub in memory (<see cref="SampleGitHub"/>), a computer in memory
/// (<see cref="SampleAppSystem"/>), MatCloud in memory (<see cref="SampleCloud"/>, the token in memory, the hand-overs in its own folder), no
/// machine policies and an autostart entry in memory. Nothing a test does touches the network, the real apps, the registry, the Keychain or the
/// LaunchAgents.
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
        Cloud = new SampleCloud();
        CloudService.HandlerForTests = () => Cloud;
        CloudService.ResetForTests();
        TokenStore.UseMemoryForTests(true);
        CloudHandoff.Folder = Path.Combine(Folder, "handoff");
        AppService.ResetForTests();
    }

    public string Folder { get; }

    public SampleGitHub GitHub { get; }

    public SampleAppSystem System { get; }

    public SampleCloud Cloud { get; }

    /// <summary>The machine policies that are set (e.g. "DisableAppChanges").</summary>
    public HashSet<string> Policies { get; } = [];

    public void Dispose()
    {
        AppService.ResetForTests();
        ReleaseFeed.HandlerForTests = null;
        CloudService.HandlerForTests = null;
        CloudService.ResetForTests();
        TokenStore.UseMemoryForTests(false);
        CloudHandoff.Folder = CloudHandoff.DefaultFolder();
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
