using System.Net;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Tests;

/// <summary>
/// The store's own talk with GitHub (<see cref="StoreService"/>): the pictures of the apps from their repositories – kept, asked
/// again at most once a day with the ETag, dark ones with the light one as fallback, only PNG – and the changes from the commit
/// feed, against GitHub in memory.
/// </summary>
public sealed class StoreTests
{
    private static readonly ThixApp Clip = AppCatalog.Find("thixclip")!;

    [Fact]
    public async Task A_picture_is_downloaded_once_and_asked_again_after_a_day_with_its_ETag()
    {
        using var data = new TestData();
        var picture = Clip.Screenshots[0];

        var path = await StoreService.PictureAsync(Clip, picture, dark: false);
        Assert.NotNull(path);
        Assert.StartsWith(StoreService.PictureFolder, path);
        Assert.Equal(StoreService.PictureUrl(Clip, picture.File), Assert.Single(data.GitHub.Requests));

        // the same day: from the disk, GitHub is not asked
        Assert.Equal(path, await StoreService.PictureAsync(Clip, picture, dark: false));
        Assert.Single(data.GitHub.Requests);

        // a day later: asked again, unchanged (304) – the copy stays and counts as new
        File.SetLastWriteTimeUtc(path!, DateTime.UtcNow.AddDays(-2));
        Assert.Equal(path, await StoreService.PictureAsync(Clip, picture, dark: false));
        Assert.Equal(2, data.GitHub.Requests.Count);
        Assert.True(DateTime.UtcNow - File.GetLastWriteTimeUtc(path!) < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task The_dark_picture_is_taken_when_the_repository_has_one_else_the_light_one()
    {
        using var data = new TestData();
        var picture = Clip.Screenshots[0];
        Assert.Equal("main-history-dark.png", StoreService.DarkFileOf(picture.File));

        // no dark one: the light one, and the missing dark one is not asked again today
        var light = await StoreService.PictureAsync(Clip, picture, dark: true);
        Assert.EndsWith(picture.File, light);
        Assert.Equal(2, data.GitHub.Requests.Count);
        Assert.Equal(light, await StoreService.PictureAsync(Clip, picture, dark: true));
        Assert.Equal(2, data.GitHub.Requests.Count);

        // a repository with dark pictures
        data.GitHub.Pictures = (_, file) => SamplePicture.Png(file);
        Assert.EndsWith("main-dialog-dark.png", await StoreService.PictureAsync(Clip, new AppScreenshot("main-dialog-light.png", "x"), dark: true));
    }

    [Fact]
    public async Task Only_a_PNG_with_a_safe_name_is_taken_and_without_GitHub_the_copy_stays()
    {
        using var data = new TestData();

        // not a picture
        data.GitHub.Pictures = (_, _) => "<html>no picture</html>"u8.ToArray();
        Assert.Null(await StoreService.PictureAsync(Clip, new AppScreenshot("main-page-light.png", "x"), dark: false));

        // a name that leaves the folder is never asked for
        var before = data.GitHub.Requests.Count;
        Assert.Null(await StoreService.PictureAsync(Clip, new AppScreenshot("../../evil.png", "x"), dark: false));
        Assert.Null(await StoreService.PictureAsync(Clip, new AppScreenshot("main.exe", "x"), dark: false));
        Assert.Equal(before, data.GitHub.Requests.Count);

        // GitHub down: the copy kept before is shown
        data.GitHub.Pictures = null;
        var picture = Clip.Screenshots[1];
        var path = await StoreService.PictureAsync(Clip, picture, dark: false);
        File.SetLastWriteTimeUtc(path!, DateTime.UtcNow.AddDays(-2));
        data.GitHub.FailWith = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(path, await StoreService.PictureAsync(Clip, picture, dark: false));

        // and without a copy: none
        Assert.Null(await StoreService.PictureAsync(Clip, Clip.Screenshots[2], dark: false));
    }

    [Fact]
    public async Task The_changes_are_the_whole_first_lines_of_the_commits_of_the_channel_kept_six_hours()
    {
        using var data = new TestData();
        data.GitHub.Commits.Insert(0, ("Install options (SETUP-08): installer.json in the program and the update manifest, the command line", 1));

        var changes = await StoreService.ChangesAsync(Clip);
        Assert.Equal("Install options (SETUP-08): installer.json in the program and the update manifest, the command line", changes[0].Title);
        Assert.Equal("Install options (SETUP-08): the setup asks for the desktop shortcut, autostart and file types", changes[1].Title);
        Assert.Equal(data.GitHub.PublishedAt.AddHours(-1), changes[0].Date);
        Assert.StartsWith("https://github.com/Real-TTX/ThixClip/commit/", changes[0].Url);
        Assert.Equal("https://github.com/Real-TTX/ThixClip/commits/main.atom", Assert.Single(data.GitHub.Requests));

        // kept: GitHub is not asked again
        Assert.Same(changes, await StoreService.ChangesAsync(Clip));
        Assert.Single(data.GitHub.Requests);

        // nightly: the changes of dev
        SettingsService.Update(current => current with { AppChannel = AppChannel.Nightly });
        await StoreService.ChangesAsync(Clip);
        Assert.Contains("https://github.com/Real-TTX/ThixClip/commits/dev.atom", data.GitHub.Requests);
        Assert.Equal("https://github.com/Real-TTX/ThixClip/commits/dev", StoreService.ChangesPage(Clip));
    }

    [Fact]
    public async Task Without_GitHub_the_changes_say_so()
    {
        using var data = new TestData();
        data.GitHub.FailWith = HttpStatusCode.ServiceUnavailable;
        var error = await Assert.ThrowsAsync<BusinessException>(() => StoreService.ChangesAsync(Clip));
        Assert.Contains("GitHub", error.Message);
    }

    [Fact]
    public async Task The_store_shows_first_an_app_not_installed_yet()
    {
        using var data = new TestData();
        data.System.Add(AppCatalog.Find("thixcalc")!, "0.1.1-20261119");
        await AppService.RefreshAsync();
        Assert.Equal("thixcalendar", StoreService.Featured(AppService.All())?.App.Id);

        foreach (var app in AppCatalog.Apps)
        {
            data.System.Add(app, "0.1.1-20261119");
        }

        AppService.DetectInstalled();
        Assert.Equal("thixcalc", StoreService.Featured(AppService.All())?.App.Id);
    }

    [Fact]
    public void Every_app_of_the_catalog_has_pictures_with_safe_names_and_words_for_them()
    {
        foreach (var app in AppCatalog.Apps)
        {
            Assert.NotEmpty(app.Screenshots);
            Assert.All(app.Screenshots, picture =>
            {
                Assert.True(StoreService.IsSafePictureName(picture.File), $"{app.Id}: {picture.File}");
                Assert.EndsWith("-light.png", picture.File);
                Assert.False(string.IsNullOrWhiteSpace(picture.Caption));
            });
        }
    }
}
