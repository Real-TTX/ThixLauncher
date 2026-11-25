using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Business;

/// <summary>
/// The catalog for the website (thixapps.de, MatCMS plugin "thix-store"): the launcher and every app of <see cref="AppCatalog"/> as JSON,
/// with the addresses a page needs (the manifest of the release channel, the icon, the pictures) and its features ("features": id, name
/// and description of every <see cref="AppFeature"/> of the app; "cloud" stays for older readers). It is the file <c>docs/catalog.json</c>
/// of this repository; the website reads it from the main branch, so the shop window shows what the released launcher installs.
/// A test keeps the file equal to the catalog (THIXLAUNCHER_WRITE_CATALOG=1 writes it).
/// </summary>
public static class WebCatalog
{
    /// <summary>Where the file lies in the repository.</summary>
    public const string FilePath = "docs/catalog.json";

    /// <summary>Raised when the shape changes in a way an older reader cannot follow.</summary>
    public const int Format = 1;

    /// <summary>The launcher's own logo on the main branch.</summary>
    public const string LauncherIconPath = "src/ThixLauncher/Assets/logo-256.png";

    /// <summary>The logos of the apps on the main branch (&lt;id&gt;.png).</summary>
    public const string AppIconFolder = "src/ThixLauncher/Assets/apps";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // umlauts and „quotes" stay readable in the file
    };

    /// <summary>The catalog as the file holds it (LF line ends, a final line end).</summary>
    public static string Json()
    {
        var apps = new JsonArray();
        foreach (var app in AppCatalog.Apps.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase))
        {
            apps.Add(App(app));
        }

        var catalog = new JsonObject
        {
            ["format"] = Format,
            ["launcher"] = new JsonObject
            {
                ["name"] = "ThixLauncher",
                ["repository"] = AppInfo.Repository,
                ["manifest"] = ManifestUrl(AppInfo.Repository, UpdateService.ManifestFileName),
                ["icon"] = Raw(AppInfo.Repository, LauncherIconPath),
                ["project"] = $"https://github.com/{AppInfo.Repository}",
            },
            ["apps"] = apps,
        };
        return catalog.ToJsonString(Options).Replace("\r\n", "\n") + "\n";
    }

    private static JsonObject App(ThixApp app)
    {
        var screenshots = new JsonArray();
        foreach (var picture in app.Screenshots)
        {
            var entry = new JsonObject
            {
                ["url"] = StoreService.PictureUrl(app, picture.File),
                ["caption"] = picture.Caption,
            };
            if (StoreService.DarkFileOf(picture.File) is var dark && dark != picture.File)
            {
                entry["dark"] = StoreService.PictureUrl(app, dark);
            }

            screenshots.Add(entry);
        }

        var features = new JsonArray();
        foreach (var feature in app.Features)
        {
            features.Add(new JsonObject
            {
                ["id"] = AppFeatures.Key(feature),
                ["name"] = AppFeatures.Name(feature),
                ["description"] = AppFeatures.Description(feature),
            });
        }

        return new JsonObject
        {
            ["id"] = app.Id,
            ["shortName"] = app.ShortName,
            ["name"] = app.Name,
            ["tagline"] = app.Tagline,
            ["description"] = app.Description,
            ["repository"] = app.Repository,
            ["manifest"] = app.ManifestFileName is { } manifest ? ManifestUrl(app.Repository, manifest) : null,
            ["icon"] = Raw(AppInfo.Repository, $"{AppIconFolder}/{app.Id}.png"),
            ["project"] = app.ProjectUrl,
            ["cloud"] = app.CloudApp is not null,
            ["features"] = features,
            ["screenshots"] = screenshots,
        };
    }

    /// <summary>The manifest of the release channel: version and the package of every platform.</summary>
    public static string ManifestUrl(string repository, string fileName) =>
        $"https://github.com/{repository}/releases/download/release-channel/{fileName}";

    private static string Raw(string repository, string path) =>
        $"https://raw.githubusercontent.com/{repository}/{StoreService.ImagesBranch}/{path}";
}
