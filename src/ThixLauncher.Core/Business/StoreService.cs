using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Business;

/// <summary>
/// What the store shows of an app beyond its build, straight from its GitHub repository (public, no token, and not the API –
/// its limit of 60 requests an hour is left to the feeds):
///  - the pictures (<see cref="ThixApp.Screenshots"/>) from <c>raw.githubusercontent.com/&lt;repo&gt;/main/docs/images/&lt;file&gt;</c>,
///    kept in the data folder (<c>store/&lt;app&gt;/</c>) and asked again at most once a day, with the ETag (unchanged = 304, nothing
///    downloaded); in the dark theme "…-dark.png" first, the light one when the repository has no dark one;
///  - the changes: the commits of the branch of the channel (release = main, nightly = dev) from GitHub's Atom feed
///    <c>github.com/&lt;repo&gt;/commits/&lt;branch&gt;.atom</c>, in memory for 6 hours.
/// Without a connection the copies kept are shown; nothing here ever holds up the launcher.
/// </summary>
public static partial class StoreService
{
    /// <summary>Where the apps keep their pictures in their repositories.</summary>
    public const string ImagesFolder = "docs/images";

    /// <summary>The branch whose pictures the store shows (the released apps).</summary>
    public const string ImagesBranch = "main";

    /// <summary>A picture larger than this is not taken (a screenshot is 50 … 500 KB).</summary>
    internal const int MaxPictureBytes = 8 * 1024 * 1024;

    /// <summary>The most changes shown of an app.</summary>
    public const int MaxChanges = 12;

    private static readonly TimeSpan PictureMaxAge = TimeSpan.FromDays(1);
    private static readonly TimeSpan ChangesMaxAge = TimeSpan.FromHours(6);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    private static readonly ConcurrentDictionary<string, Task<string?>> Loading = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<AppChange> Changes)> ChangesCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder of the pictures kept.</summary>
    public static string PictureFolder => Path.Combine(AppPaths.DataDirectory, "store");

    /// <summary>The address of a picture of <paramref name="app"/> in its repository.</summary>
    public static string PictureUrl(ThixApp app, string file) =>
        $"https://raw.githubusercontent.com/{app.Repository}/{ImagesBranch}/{ImagesFolder}/{file}";

    /// <summary>"main-light.png" → "main-dark.png" (a file without "-light" has no dark one: itself).</summary>
    public static string DarkFileOf(string file) =>
        file.EndsWith("-light.png", StringComparison.OrdinalIgnoreCase) ? file[..^"-light.png".Length] + "-dark.png" : file;

    /// <summary>The branch the changes of the current channel come from: release = main, nightly = dev.</summary>
    public static string ChangesBranch => AppService.Channel == AppVersion.Nightly ? "dev" : "main";

    /// <summary>The page with every change of <paramref name="app"/> in the current channel.</summary>
    public static string ChangesPage(ThixApp app) => $"https://github.com/{app.Repository}/commits/{ChangesBranch}";

    /// <summary>The app whose picture the store shows big: the first one not installed yet (catalog order), else the first.</summary>
    public static AppStatus? Featured(IReadOnlyList<AppStatus> statuses) =>
        statuses.FirstOrDefault(status => !status.IsInstalled && status.Latest is not null) ?? statuses.FirstOrDefault();

    /// <summary>
    /// The local file of a picture of <paramref name="app"/> (the dark one when <paramref name="dark"/> and the repository has
    /// it), downloaded when it is missing or older than a day; null when there is none and GitHub cannot be reached. Never throws.
    /// Several requests for the same picture at once share one download.
    /// </summary>
    public static Task<string?> PictureAsync(ThixApp app, AppScreenshot picture, bool dark)
    {
        var key = $"{app.Id}|{picture.File}|{dark}";
        var task = Loading.GetOrAdd(key, _ => LoadPictureAsync(app, picture.File, dark));
        if (task.IsCompleted)
        {
            Loading.TryRemove(new KeyValuePair<string, Task<string?>>(key, task));
        }
        else
        {
            _ = task.ContinueWith(done => Loading.TryRemove(new KeyValuePair<string, Task<string?>>(key, done)), TaskScheduler.Default);
        }

        return task;
    }

    /// <summary>
    /// The newest changes of <paramref name="app"/> (commits of the branch of the channel, newest first, at most
    /// <see cref="MaxChanges"/>). Kept 6 hours. Throws <see cref="BusinessException"/> when GitHub cannot be read.
    /// </summary>
    public static async Task<IReadOnlyList<AppChange>> ChangesAsync(ThixApp app, CancellationToken cancellationToken = default)
    {
        var url = $"https://github.com/{app.Repository}/commits/{ChangesBranch}.atom";
        if (ChangesCache.TryGetValue(url, out var cached) && AppClock.UtcNow - cached.At < ChangesMaxAge)
        {
            return cached.Changes;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await ReleaseFeed.Http.GetAsync(url, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var changes = ReadChanges(stream);
            ChangesCache[url] = (AppClock.UtcNow, changes);
            return changes;
        }
        catch (Exception exception) when (exception is HttpRequestException or XmlException or FormatException ||
                                          exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Log.Warning($"Changes of {app.Id} from {url}: {exception.Message}");
            if (cached.Changes is not null)
            {
                return cached.Changes;
            }

            throw new BusinessException("Die Änderungen konnten nicht von GitHub gelesen werden – keine Verbindung?");
        }
    }

    /// <summary>Forgets what is kept in memory (tests).</summary>
    internal static void ResetForTests()
    {
        Loading.Clear();
        ChangesCache.Clear();
    }

    /// <summary>The entries of a commit feed: the first line of the message, the time, the page of the commit.</summary>
    internal static IReadOnlyList<AppChange> ReadChanges(Stream atom)
    {
        using var reader = XmlReader.Create(atom, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var feed = XDocument.Load(reader);
        return feed.Root?.Elements(Atom + "entry")
            .Select(entry => new AppChange(
                Message(entry),
                DateTimeOffset.Parse(entry.Element(Atom + "updated")?.Value ?? "", System.Globalization.CultureInfo.InvariantCulture),
                entry.Element(Atom + "link")?.Attribute("href")?.Value ?? ""))
            .Where(change => change.Title.Length > 0 && change.Url.StartsWith("https://github.com/", StringComparison.Ordinal))
            .Take(MaxChanges)
            .ToList() ?? [];
    }

    /// <summary>A picture's name from the catalog: letters, digits, "-", "_", "." and ".png" only.</summary>
    internal static bool IsSafePictureName(string file) => SafePictureName().IsMatch(file) && !file.Contains("..", StringComparison.Ordinal);

    private static async Task<string?> LoadPictureAsync(ThixApp app, string file, bool dark)
    {
        if (dark && DarkFileOf(file) is var darkFile && darkFile != file && await FetchAsync(app, darkFile) is { } darkPath)
        {
            return darkPath;
        }

        return await FetchAsync(app, file);
    }

    /// <summary>The kept copy while it is younger than a day; else GitHub is asked (with the ETag of the copy).</summary>
    private static async Task<string?> FetchAsync(ThixApp app, string file)
    {
        if (!IsSafePictureName(file))
        {
            return null;
        }

        var path = Path.Combine(PictureFolder, app.Id, file);
        var etagFile = path + ".etag";
        var missingFile = path + ".missing";
        if (IsFresh(path))
        {
            return path;
        }

        if (IsFresh(missingFile))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var request = new HttpRequestMessage(HttpMethod.Get, PictureUrl(app, file));
            if (File.Exists(path) && File.Exists(etagFile) && File.ReadAllText(etagFile).Trim() is { Length: > 0 } etag)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using var timeout = new CancellationTokenSource(RequestTimeout);
            using var response = await ReleaseFeed.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotModified && File.Exists(path))
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return path;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                await File.WriteAllTextAsync(missingFile, "");
                TryDelete(path);
                TryDelete(etagFile);
                return null;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxPictureBytes)
            {
                throw new InvalidDataException("too large");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            if (bytes.Length > MaxPictureBytes || !bytes.AsSpan().StartsWith(PngSignature))
            {
                throw new InvalidDataException("not a PNG picture");
            }

            var temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
            await File.WriteAllTextAsync(etagFile, response.Headers.ETag?.ToString() ?? "");
            TryDelete(missingFile);
            return path;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or
                                              UnauthorizedAccessException or InvalidDataException)
        {
            Log.Warning($"Picture {file} of {app.Id}: {exception.Message}");
            return File.Exists(path) ? path : null;
        }
    }

    private static bool IsFresh(string path) =>
        File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < PictureMaxAge;

    /// <summary>
    /// The first line of the commit message: the feed's content is the whole message as HTML (&lt;pre&gt;…&lt;/pre&gt;), its title is cut
    /// after 70 characters ("…") – the title only when there is no content.
    /// </summary>
    private static string Message(XElement entry)
    {
        var content = entry.Element(Atom + "content")?.Value;
        var text = content is { Length: > 0 } ? WebUtility.HtmlDecode(HtmlTag().Replace(content, "")) : entry.Element(Atom + "title")?.Value ?? "";
        return text.Trim().Split('\n', 2)[0].Trim();
    }

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag();

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

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,120}\\.png$", RegexOptions.CultureInvariant)]
    private static partial Regex SafePictureName();
}
