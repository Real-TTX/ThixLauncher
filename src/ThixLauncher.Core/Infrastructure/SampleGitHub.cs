using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Infrastructure;

/// <summary>
/// GitHub in memory, for the tests, the self test and the snapshots (<see cref="ReleaseFeed.HandlerForTests"/>): it answers
/// the manifests of the rolling channels (ThixClip, ThixFred, ThixNote, ThixTunnel, ThixLauncher), the release list of the GitHub API (an app
/// with <see cref="FeedKind.GitHubReleases"/>) and the downloads, with the versions set in <see cref="Versions"/>. A package is a small text whose SHA-256 the feeds declare,
/// so the whole way – feed, download, checksum, install – runs as it does with the real GitHub. Never used by the real app.
/// </summary>
internal sealed class SampleGitHub : HttpMessageHandler
{
    /// <summary>The newest version per app id and channel ("thixclip", "release" → "0.1.12-20261115"); missing = the channel has none.</summary>
    public Dictionary<(string AppId, string Channel), string> Versions { get; } = new();

    /// <summary>The time every release was published.</summary>
    public DateTimeOffset PublishedAt { get; set; } = new(2026, 11, 15, 21, 51, 0, TimeSpan.Zero);

    /// <summary>Answer every request with this status instead (GitHub down, the rate limit).</summary>
    public HttpStatusCode? FailWith { get; set; }

    /// <summary>Declare a wrong checksum for every package (a damaged download).</summary>
    public bool BreakChecksums { get; set; }

    /// <summary>Leave the checksums out of the manifests and the release list (a package without a checksum).</summary>
    public bool OmitDigests { get; set; }

    /// <summary>Every URL asked for, in order.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>The sample state of the snapshots: ThixClip 0.1.12 in release (installed: 0.1.9), ThixFred 0.1.1, ThixNote 1.1.1, ThixTunnel 0.2.8, nightlies of all.</summary>
    public static SampleGitHub WithSampleReleases()
    {
        var github = new SampleGitHub();
        github.Versions[("thixclip", AppVersion.Release)] = "0.1.12-20261115";
        github.Versions[("thixclip", AppVersion.Nightly)] = "nightly-14-20261116";
        github.Versions[("thixfred", AppVersion.Release)] = "0.1.1-20261117";
        github.Versions[("thixfred", AppVersion.Nightly)] = "nightly-2-20261117";
        github.Versions[("thixnote", AppVersion.Release)] = "1.1.1-20261116";
        github.Versions[("thixnote", AppVersion.Nightly)] = "nightly-5-20261116";
        github.Versions[("thixtunnel", AppVersion.Release)] = "0.2.8-20261117";
        github.Versions[("thixtunnel", AppVersion.Nightly)] = "nightly-7-20261117";
        return github;
    }

    /// <summary>The bytes of a sample package: the app and the version it installs (<see cref="Platform.SampleAppSystem"/> reads them).</summary>
    public static byte[] PackageBytes(string appId, string version) => Encoding.UTF8.GetBytes($"sample package {appId} {version}");

    /// <summary>The version a sample package installs, or null.</summary>
    public static (string AppId, string Version)? ReadPackage(string file)
    {
        var parts = File.ReadAllText(file, Encoding.UTF8).Split(' ');
        return parts is ["sample", "package", var appId, var version] ? (appId, version) : null;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        lock (Requests)
        {
            Requests.Add(url);
        }

        if (FailWith is { } status)
        {
            return Task.FromResult(new HttpResponseMessage(status));
        }

        return Task.FromResult(Answer(request.RequestUri!) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private HttpResponseMessage? Answer(Uri uri)
    {
        var path = uri.AbsolutePath.Trim('/').Split('/');

        // api.github.com/repos/<owner>/<repo>/releases
        if (uri.Host == "api.github.com" && path is ["repos", _, var repoName, "releases"] && AppOf(repoName) is { } listApp)
        {
            return Json(ReleaseList(listApp));
        }

        // github.com/<owner>/<repo>/releases/download/<tag>/<file>
        if (uri.Host != "github.com" || path is not [_, var repo, "releases", "download", var tag, var file])
        {
            return null;
        }

        var manifestOwner = repo == "ThixLauncher" ? "thixlauncher" : AppOf(repo)?.Id;
        if (manifestOwner is null)
        {
            return null;
        }

        if (file.EndsWith("-update.json", StringComparison.Ordinal))
        {
            var channel = tag.Replace("-channel", "", StringComparison.Ordinal);
            return Versions.TryGetValue((manifestOwner, channel), out var version) ? Json(Manifest(manifestOwner, repo, channel, version)) : null;
        }

        var versionOfTag = tag.EndsWith("-channel", StringComparison.Ordinal)
            ? Versions.GetValueOrDefault((manifestOwner, tag.Replace("-channel", "", StringComparison.Ordinal)))
            : tag.TrimStart('v');
        return versionOfTag is null ? null : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(PackageBytes(manifestOwner, versionOfTag)) };
    }

    private object Manifest(string appId, string repo, string channel, string version)
    {
        var parsed = AppVersion.Parse(version)!;
        var windows = $"{repo}Setup.exe";
        var sha = OmitDigests ? string.Empty : Sha(appId, version);
        return new
        {
            version,
            channel,
            buildNumber = parsed.BuildNumber,
            setup = windows,
            sha256 = sha,
            publishedAt = PublishedAt,
            packages = new Dictionary<string, object>
            {
                ["win-x64"] = new { file = windows, sha256 = sha },
                ["osx-arm64"] = new { file = $"{repo}-macOS-arm64.zip", sha256 = sha },
                ["osx-x64"] = new { file = $"{repo}-macOS-x64.zip", sha256 = sha },
            },
        };
    }

    private object[] ReleaseList(ThixApp app)
    {
        var releases = new List<object>();
        foreach (var channel in new[] { AppVersion.Nightly, AppVersion.Release })
        {
            if (!Versions.TryGetValue((app.Id, channel), out var version))
            {
                continue;
            }

            var tag = channel == AppVersion.Release ? $"v{version}" : version;
            var assets = new[]
                {
                    $"{app.Name}-Setup-{version}-win-x64.exe", $"{app.Name}-{version}-win-x64-portable.zip",
                    $"{app.Name}-{version}-osx-arm64-portable.zip", $"{app.Name}-{version}-osx-arm64.dmg",
                    $"{app.Name}-{version}-osx-x64-portable.zip",
                }
                .Select(name => (object)new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["size"] = 41_500_000L,
                    ["digest"] = OmitDigests ? null : $"sha256:{Sha(app.Id, version)}",
                    ["browser_download_url"] = $"https://github.com/{app.Repository}/releases/download/{tag}/{name}",
                })
                .ToArray();
            releases.Add(new Dictionary<string, object>
            {
                ["tag_name"] = tag,
                ["prerelease"] = channel == AppVersion.Nightly,
                ["draft"] = false,
                ["published_at"] = PublishedAt,
                ["html_url"] = $"https://github.com/{app.Repository}/releases/tag/{tag}",
                ["assets"] = assets,
            });
        }

        return [.. releases];
    }

    private string Sha(string appId, string version) =>
        BreakChecksums ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(PackageBytes(appId, version))).ToLowerInvariant();

    private static ThixApp? AppOf(string repositoryName) =>
        AppCatalog.Apps.FirstOrDefault(app => app.Repository.EndsWith("/" + repositoryName, StringComparison.OrdinalIgnoreCase));

    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}
