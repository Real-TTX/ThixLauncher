using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Infrastructure;

/// <summary>A feed could not be read (network, GitHub, an unexpected answer). The message is German and meant for the user.</summary>
public sealed class FeedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads where the apps publish their builds and downloads packages – GitHub releases, public, without a token:
///  - <see cref="FeedKind.ChannelManifest"/>: https://github.com/&lt;repo&gt;/releases/download/&lt;channel&gt;-channel/&lt;manifest&gt;
///  - <see cref="FeedKind.GitHubReleases"/>: https://api.github.com/repos/&lt;repo&gt;/releases (newest first; a "v…" release for
///    release, a pre-release "nightly-…" for nightly), the package by the pattern of its file name, its SHA-256 from GitHub's digest.
/// Only the business layer uses this class. Tests replace the network with <see cref="HandlerForTests"/>.
/// </summary>
internal static class ReleaseFeed
{
    private static readonly Lazy<HttpClient> SharedClient = new(() => CreateClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }));

    private static readonly TimeSpan FeedTimeout = TimeSpan.FromSeconds(30);

    private static HttpMessageHandler? _handlerForTests;
    private static HttpClient? _testClient;

    /// <summary>Tests answer every request themselves (a fake GitHub); null = the network again.</summary>
    internal static HttpMessageHandler? HandlerForTests
    {
        get => _handlerForTests;
        set
        {
            _handlerForTests = value;
            _testClient = value is null ? null : CreateClient(value);
        }
    }

    /// <summary>The one client of every request to GitHub (feeds, packages, the store's pictures and changes).</summary>
    internal static HttpClient Http => _testClient ?? SharedClient.Value;

    /// <summary>The newest build of <paramref name="app"/> in <paramref name="channel"/> for <paramref name="platformKey"/>; null when the channel has none for it.</summary>
    public static async Task<AppRelease?> LatestAsync(ThixApp app, string channel, string platformKey, CancellationToken cancellationToken)
    {
        // A feed is a small answer: it does not get the long timeout of a download.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FeedTimeout);
        try
        {
            return app.Feed switch
            {
                FeedKind.ChannelManifest => await FromManifestAsync(app, channel, platformKey, timeout.Token),
                _ => await FromReleaseListAsync(app, channel, platformKey, timeout.Token),
            };
        }
        catch (HttpRequestException exception)
        {
            throw new FeedException(exception.StatusCode == HttpStatusCode.Forbidden
                ? "GitHub nimmt gerade keine Anfragen mehr an (Limit pro Stunde). Später noch einmal versuchen."
                : "Keine Verbindung zu GitHub.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FeedException("GitHub antwortet nicht (Zeitüberschreitung).", exception);
        }
        catch (JsonException exception)
        {
            throw new FeedException("Die Antwort von GitHub ist unerwartet.", exception);
        }
    }

    /// <summary>The URL of the manifest of a channel (ThixLauncher reads its own updates from the same place).</summary>
    public static string ManifestUrl(string repository, string channel, string manifestFile) =>
        $"https://github.com/{repository}/releases/download/{channel}-channel/{manifestFile}";

    /// <summary>The manifest of a channel; null when the channel has not published one yet.</summary>
    public static async Task<UpdateManifest?> ReadManifestAsync(string repository, string channel, string manifestFile, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(ManifestUrl(repository, channel, manifestFile), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UpdateManifest>(cancellationToken);
    }

    /// <summary>
    /// Downloads <paramref name="url"/> into <paramref name="targetFile"/> and checks its SHA-256 (a package without a
    /// checksum is never used). A file that does not match is deleted.
    /// </summary>
    public static async Task DownloadAsync(string url, string targetFile, string expectedSha256, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        try
        {
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = File.Create(targetFile);
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (total > 0)
                    {
                        progress?.Report((double)received / total);
                    }
                }
            }

            var hash = await Task.Run(() =>
            {
                using var stream = File.OpenRead(targetFile);
                return Convert.ToHexString(SHA256.HashData(stream));
            }, cancellationToken);
            if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(targetFile);
                throw new FeedException("Die heruntergeladene Datei ist beschädigt (Prüfsumme stimmt nicht).");
            }
        }
        catch (HttpRequestException exception)
        {
            TryDelete(targetFile);
            throw new FeedException("Der Download ist fehlgeschlagen – keine Verbindung zu GitHub?", exception);
        }
        catch (OperationCanceledException)
        {
            TryDelete(targetFile);
            throw;
        }
    }

    /// <summary>A file name the launcher writes to disk and runs: no folders, no tricks.</summary>
    internal static bool IsSafeFileName(string fileName, bool allowMsi = false) =>
        fileName.Length is > 0 and < 200 &&
        (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
         fileName.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase) ||
         allowMsi && fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) &&
        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !fileName.Contains("..", StringComparison.Ordinal) &&
        !fileName.Contains('/') && !fileName.Contains('\\');

    internal static bool IsAppPackageFileName(ThixApp app, string platformKey, string fileName)
    {
        var usesMsi = app.WindowsMsiUpgradeCode.HasValue && platformKey.StartsWith("win-", StringComparison.Ordinal);
        return IsSafeFileName(fileName, allowMsi: usesMsi) &&
               (!usesMsi || fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<AppRelease?> FromManifestAsync(ThixApp app, string channel, string platformKey, CancellationToken cancellationToken)
    {
        var manifest = await ReadManifestAsync(app.Repository, channel, app.ManifestFileName ?? $"{app.Id}-update.json", cancellationToken);
        if (manifest is null || !string.Equals(manifest.Channel, channel, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var package = manifest.PackageFor(platformKey);
        var version = AppVersion.Parse(manifest.Version);
        if (package is null || version is null || !IsAppPackageFileName(app, platformKey, package.File))
        {
            return null;
        }

        // The manifest's build number is the one the app compares (the text may lack it on old manifests).
        version = version with { BuildNumber = manifest.BuildNumber > 0 ? manifest.BuildNumber : version.BuildNumber };
        var tag = $"{channel}-channel";
        return new AppRelease(app.Id, version, package.File, $"https://github.com/{app.Repository}/releases/download/{tag}/{package.File}",
            package.Sha256, null, manifest.PublishedAt, $"https://github.com/{app.Repository}/releases/tag/{tag}",
            package.File.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ? null : manifest.Installer);
    }

    private static async Task<AppRelease?> FromReleaseListAsync(ThixApp app, string channel, string platformKey, CancellationToken cancellationToken)
    {
        if (!app.PackagePatterns.TryGetValue(platformKey, out var pattern))
        {
            return null;
        }

        var packagePattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{app.Repository}/releases?per_page=30");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        foreach (var release in document.RootElement.EnumerateArray())
        {
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            var isPrerelease = release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();
            var isDraft = release.TryGetProperty("draft", out var draft) && draft.GetBoolean();
            var fitsChannel = channel == AppVersion.Nightly
                ? isPrerelease && tag.StartsWith("nightly-", StringComparison.OrdinalIgnoreCase)
                : !isPrerelease && tag.StartsWith('v');
            if (isDraft || !fitsChannel || AppVersion.Parse(tag) is not { } version || version.Channel != channel)
            {
                continue;
            }

            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!packagePattern.IsMatch(name) || !IsAppPackageFileName(app, platformKey, name))
                {
                    continue;
                }

                var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
                var sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
                var published = release.TryGetProperty("published_at", out var at) && at.ValueKind == JsonValueKind.String ? at.GetDateTimeOffset() : (DateTimeOffset?)null;
                var size = asset.TryGetProperty("size", out var bytes) ? bytes.GetInt64() : (long?)null;
                return new AppRelease(app.Id, version, name, asset.GetProperty("browser_download_url").GetString() ?? "", sha256, size, published,
                    release.TryGetProperty("html_url", out var page) ? page.GetString() ?? app.ProjectUrl : app.ProjectUrl);
            }
        }

        return null;
    }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ThixLauncher/{AppInfo.BuildNumber}");
        return client;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
