using System.Text.Json.Nodes;
using ThixLauncher.Core.Business;

namespace ThixLauncher.Tests;

/// <summary>
/// The catalog of the website (<see cref="WebCatalog"/>): docs/catalog.json is what the catalog says (the website reads that file), every app
/// has its own short name for its page, and every address points where the launcher itself looks.
/// </summary>
public sealed class WebCatalogTests
{
    [Fact]
    public void The_catalog_file_of_the_website_is_up_to_date()
    {
        var path = Path.Combine(Repository.Root, WebCatalog.FilePath);
        var expected = WebCatalog.Json();
        if (Environment.GetEnvironmentVariable("THIXLAUNCHER_WRITE_CATALOG") == "1")
        {
            File.WriteAllText(path, expected);
        }

        Assert.True(File.Exists(path), $"{WebCatalog.FilePath} is missing: run the tests with THIXLAUNCHER_WRITE_CATALOG=1");
        Assert.True(expected == File.ReadAllText(path).Replace("\r\n", "\n"),
            $"{WebCatalog.FilePath} is not the catalog any more: run the tests with THIXLAUNCHER_WRITE_CATALOG=1");
    }

    [Fact]
    public void Every_app_has_a_short_name_of_its_own_in_lower_case_letters()
    {
        var names = AppCatalog.Apps.Select(app => app.ShortName).ToList();
        Assert.All(names, name => Assert.Matches("^[a-z]+$", name));
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void The_catalog_names_every_app_with_its_manifest_its_icon_and_its_pictures()
    {
        var catalog = JsonNode.Parse(WebCatalog.Json())!;
        Assert.Equal(WebCatalog.Format, (int)catalog["format"]!);
        Assert.Equal("https://github.com/Real-TTX/ThixLauncher/releases/download/release-channel/thixlauncher-update.json",
            (string?)catalog["launcher"]!["manifest"]);

        var apps = catalog["apps"]!.AsArray();
        Assert.Equal(AppCatalog.Apps.Count, apps.Count);
        var mail = apps.Single(app => (string?)app!["id"] == "thixmail")!;
        Assert.Equal("mail", (string?)mail["shortName"]);
        Assert.Equal("https://github.com/Real-TTX/ThixOffice/releases/download/release-channel/thixmail-update.json", (string?)mail["manifest"]);
        Assert.Equal("https://raw.githubusercontent.com/Real-TTX/ThixLauncher/main/src/ThixLauncher/Assets/apps/thixmail.png", (string?)mail["icon"]);
        Assert.True((bool)mail["cloud"]!);

        foreach (var app in AppCatalog.Apps)
        {
            Assert.True(File.Exists(Path.Combine(Repository.Root, WebCatalog.AppIconFolder, app.Id + ".png")), app.Id + " has no logo");
            var entry = apps.Single(node => (string?)node!["id"] == app.Id)!;
            var pictures = entry["screenshots"]!.AsArray();
            Assert.Equal(app.Screenshots.Count, pictures.Count);
            Assert.All(pictures, picture => Assert.StartsWith($"https://raw.githubusercontent.com/{app.Repository}/main/", (string?)picture!["url"]));
        }

        Assert.True(File.Exists(Path.Combine(Repository.Root, WebCatalog.LauncherIconPath)));
    }
}
