using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Tests;

/// <summary>
/// The install options of SETUP-08 on both sides: ThixLauncher's own (installer.json, its command line, applying the choices)
/// and the apps it installs (the block "installer" of their manifests, the arguments of their setups, the old way without it).
/// </summary>
public sealed class InstallOptionsTests
{
    private static readonly ThixApp Note = AppCatalog.Find("thixnote")!;

    [Fact]
    public void The_launcher_offers_shortcut_autostart_and_automatic_updates_without_file_types()
    {
        var config = InstallerOptions.Config;
        Assert.False(config.DesktopShortcut!.Default);
        Assert.True(config.Autostart!.Default);
        Assert.True(config.AutomaticUpdates!.Default);
        Assert.Empty(config.FileTypes);
        Assert.Equal(new InstallChoices(false, true, true), InstallerOptions.Defaults);

        // the file in the repository is the one CI puts into the manifest
        var file = Path.Combine(Repository.Root, "src", "ThixLauncher.Core", "installer.json");
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void The_command_line_reads_the_options()
    {
        var install = InstallerOptions.Parse(["--quiet", "--desktop-shortcut=yes", "--autostart=no", "--automatic-updates=NO", "--start"]);
        Assert.Equal(SetupAction.Install, install.Action);
        Assert.Equal(new InstallChoices(true, false, false), install.Choices);
        Assert.True(install.Start);

        Assert.Equal(new SetupRequest(SetupAction.Update, new InstallChoices()), InstallerOptions.Parse(["--update"]));
        Assert.Equal(SetupAction.Configure, InstallerOptions.Parse(["--configure", "--autostart=ja"]).Action);
        Assert.Equal(new InstallChoices(FileTypes: []), InstallerOptions.Parse(["--quiet", "--file-types=none"]).Choices);
        Assert.Equal(new SetupRequest(SetupAction.Uninstall, new InstallChoices(), Quiet: true), InstallerOptions.Parse(["--uninstall", "--quiet"]));

        // the start at sign-in ("--autostart" without a value) and the other arguments of the program are no options
        Assert.Equal(new SetupRequest(SetupAction.None, new InstallChoices()), InstallerOptions.Parse(["--autostart"]));
        Assert.Equal(SetupAction.None, InstallerOptions.Parse(["--tray"]).Action);
    }

    [Theory]
    [InlineData("--quiet --colour=blue")]
    [InlineData("--quiet --autostart=maybe")]
    [InlineData("--quiet --file-types=.md")]
    [InlineData("--autostart=yes")]
    [InlineData("--uninstall --desktop-shortcut=no")]
    public void A_wrong_option_is_refused(string commandLine) =>
        Assert.Throws<BusinessException>(() => InstallerOptions.Parse(commandLine.Split(' ')));

    [Fact]
    public void The_arguments_of_choices_read_back_as_the_same_choices()
    {
        var choices = new InstallChoices(true, false, true);
        var arguments = InstallerOptions.ToArguments(choices);
        Assert.Equal(["--desktop-shortcut=yes", "--autostart=no", "--automatic-updates=yes"], arguments);
        Assert.Equal(choices, InstallerOptions.Parse(["--quiet", .. arguments]).Choices);
        Assert.Equal(["--file-types=none"], InstallerOptions.ToArguments(new InstallChoices(FileTypes: [])));
    }

    [Fact]
    public void Applying_the_choices_sets_shortcut_autostart_and_updates_and_an_update_keeps_them()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // the desktop shortcut is a Windows thing; autostart and updates are the same code on macOS
        }

        using var data = new TestData();
        InstallService.UseLocationsForTests(Path.Combine(data.Folder, "program"), Path.Combine(data.Folder, "shortcuts"));
        try
        {
            var steps = new List<string>();
            InstallerOptions.Apply(new InstallChoices(true, true, false), Path.Combine(data.Folder, "ThixLauncher.exe"), steps.Add);
            Assert.True(File.Exists(InstallService.DesktopShortcut));
            Assert.True(AutostartService.IsEnabled());
            Assert.False(SettingsService.Get().InstallUpdatesAutomatically);
            Assert.Equal(new InstallChoices(true, true, false), InstallerOptions.Current());
            Assert.Contains("Desktop-Verknüpfung angelegt", steps);

            // what is not given stays as it is
            InstallerOptions.Apply(new InstallChoices(DesktopShortcut: false), Path.Combine(data.Folder, "ThixLauncher.exe"), steps.Add);
            Assert.False(File.Exists(InstallService.DesktopShortcut));
            Assert.Equal(new InstallChoices(false, true, false), InstallerOptions.Current());
        }
        finally
        {
            InstallService.UseLocationsForTests(null);
        }
    }

    [Fact]
    public void An_app_without_install_options_and_every_update_keep_the_old_arguments()
    {
        foreach (var old in new[]
                 {
                     AppInstallOptions.SetupFor(Note, installer: null, new InstallChoices(Autostart: true), isUpdate: false),
                     AppInstallOptions.SetupFor(Note, SampleGitHub.SampleNoteInstaller, new InstallChoices(Autostart: true), isUpdate: true),
                 })
        {
            Assert.Equal(["--update"], old.WindowsArguments);
            Assert.True(old.WindowsQuiet);
            Assert.Null(old.MacConfigureArguments);
        }

        var asking = Note with { WindowsQuietInstallArguments = null };
        var setup = AppInstallOptions.SetupFor(asking, installer: null, choices: null, isUpdate: false);
        Assert.Empty(setup.WindowsArguments);
        Assert.False(setup.WindowsQuiet);
        Assert.Null(setup.MacConfigureArguments);
    }

    [Fact]
    public void A_first_install_passes_every_offered_option_with_the_defaults_for_what_was_not_answered()
    {
        var defaults = AppInstallOptions.SetupFor(Note, SampleGitHub.SampleNoteInstaller, choices: null, isUpdate: false);
        Assert.True(defaults.WindowsQuiet);
        Assert.Equal(["--quiet", "--desktop-shortcut=no", "--autostart=no", "--automatic-updates=yes", "--file-types=.md"], defaults.WindowsArguments);
        Assert.Equal(["--configure", "--autostart=no", "--automatic-updates=yes", "--file-types=.md"], defaults.MacConfigureArguments);

        // autostart chosen: the app starts right away ("--start"); file types as the block spells them, each once
        var chosen = AppInstallOptions.SetupFor(Note, SampleGitHub.SampleNoteInstaller,
            new InstallChoices(DesktopShortcut: true, Autostart: true, FileTypes: ["TXT", ".md", ".txt"]), isUpdate: false);
        Assert.Equal(["--quiet", "--desktop-shortcut=yes", "--autostart=yes", "--automatic-updates=yes", "--file-types=.md,.txt", "--start"], chosen.WindowsArguments);
        Assert.Equal(["--configure", "--autostart=yes", "--automatic-updates=yes", "--file-types=.md,.txt"], chosen.MacConfigureArguments);

        // no file types at all: "none"
        var none = AppInstallOptions.SetupFor(Note, SampleGitHub.SampleNoteInstaller, new InstallChoices(FileTypes: []), isUpdate: false);
        Assert.Contains("--file-types=none", none.WindowsArguments);
    }

    [Fact]
    public void Only_options_the_app_offers_are_passed()
    {
        // an app that offers only the autostart refuses every other option (exit code 2): they are never passed
        var installer = new InstallerConfig { Autostart = new InstallerSwitch { Default = true } };
        var setup = AppInstallOptions.SetupFor(Note, installer, new InstallChoices(true, false, true, [".md", ".exe"]), isUpdate: false);
        Assert.Equal(["--quiet", "--autostart=no"], setup.WindowsArguments);
        Assert.Equal(["--configure", "--autostart=no"], setup.MacConfigureArguments);

        // an extension the app does not offer is left out
        var unknown = AppInstallOptions.SetupFor(Note, SampleGitHub.SampleNoteInstaller, new InstallChoices(FileTypes: [".docx"]), isUpdate: false);
        Assert.Contains("--file-types=none", unknown.WindowsArguments);
    }

    [Fact]
    public void What_is_asked_depends_on_the_system()
    {
        var asked = AppInstallOptions.ForThisSystem(SampleGitHub.SampleNoteInstaller)!;
        Assert.Equal(OperatingSystem.IsWindows(), asked.DesktopShortcut is not null);
        Assert.Equal(2, asked.FileTypes.Count);
        Assert.Null(AppInstallOptions.ForThisSystem(new InstallerConfig()));
    }

    [Fact]
    public async Task The_block_of_the_manifest_is_read_and_asked_before_a_first_install_only()
    {
        using var data = new TestData();
        await AppService.RefreshAsync();

        Assert.Equal(SampleGitHub.SampleNoteInstaller.FileTypes, AppService.Get("thixnote").Latest!.Installer!.FileTypes);
        Assert.NotNull(AppService.InstallOptionsOf("thixnote"));
        Assert.Null(AppService.InstallOptionsOf("thixclip")); // a build without the block: nothing to ask

        data.System.Add(Note, "1.0.1-20261115");
        await AppService.DetectInstalledAsync();
        Assert.Null(AppService.InstallOptionsOf("thixnote")); // installed: an update keeps what the app has
    }

    [Fact]
    public async Task Installing_passes_the_answers_and_an_update_does_not()
    {
        using var data = new TestData();
        await AppService.RefreshAsync();

        await AppService.InstallAsync("thixnote", new InstallChoices(Autostart: true, FileTypes: [".txt"]));
        var (appId, setup) = Assert.Single(data.System.Setups);
        Assert.Equal("thixnote", appId);
        Assert.Equal(["--quiet", "--desktop-shortcut=no", "--autostart=yes", "--automatic-updates=yes", "--file-types=.txt", "--start"], setup.WindowsArguments);

        // ThixClip has no block (an older build): the old way, whatever was answered
        await AppService.InstallAsync("thixclip", new InstallChoices(Autostart: false));
        Assert.Equal(["--update"], data.System.Setups[^1].Setup.WindowsArguments);
        Assert.Null(data.System.Setups[^1].Setup.MacConfigureArguments);

        // an update of ThixNote keeps its choices
        data.GitHub.Versions[("thixnote", AppVersion.Release)] = "1.1.2-20261120";
        await AppService.RefreshAsync();
        await AppService.InstallAsync("thixnote", new InstallChoices(Autostart: false));
        Assert.Equal(["--update"], data.System.Setups[^1].Setup.WindowsArguments);
        Assert.Equal("1.1.2-20261120", AppService.Get("thixnote").Installed!.VersionText);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task On_Windows_exit_code_2_of_a_quiet_setup_comes_back_with_what_it_wrote_on_stderr()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // cmd.exe plays a setup that refuses its options: exit code 2 and the reason on stderr
        var setup = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var refusing = new AppSetupCommand(["/c", "echo ThixNote bietet --colour nicht an. 1>&2 & exit 2"], WindowsQuiet: true);
        var refused = await Assert.ThrowsAsync<Core.Platform.AppSystemException>(() =>
            new Core.Platform.WindowsAppSystem().InstallAsync(Note, setup, null, refusing, new Progress<string>(), CancellationToken.None));
        Assert.Equal("Das Setup von ThixNote lehnt die Optionen ab: ThixNote bietet --colour nicht an.", refused.Message);

        var failing = new AppSetupCommand(["/c", "exit 3"], WindowsQuiet: true);
        var failed = await Assert.ThrowsAsync<Core.Platform.AppSystemException>(() =>
            new Core.Platform.WindowsAppSystem().InstallAsync(Note, setup, null, failing, new Progress<string>(), CancellationToken.None));
        Assert.Equal("Das Setup von ThixNote endete mit dem Fehler 3.", failed.Message);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void The_reason_on_stderr_is_read_in_UTF8_or_in_the_ANSI_code_page()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal("verknüpft „.md“", Core.Platform.WindowsAppSystem.ErrorText.Decode(System.Text.Encoding.UTF8.GetBytes("verknüpft „.md“")));
        if (System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage == 1252)
        {
            // what a Windows program without a console writes: "ü" = 0xFC, „ = 0x84, “ = 0x93
            Assert.Equal("verknüpft „.md“", Core.Platform.WindowsAppSystem.ErrorText.Decode([.. "verkn"u8, 0xFC, .. "pft "u8, 0x84, .. ".md"u8, 0x93]));
        }
    }

    [Fact]
    public async Task A_setup_that_refuses_the_options_is_an_error_with_its_reason()
    {
        using var data = new TestData();
        await AppService.RefreshAsync();
        data.System.RefuseOptions = "ThixNote verknüpft „.md“ nicht.";

        var refused = await Assert.ThrowsAsync<BusinessException>(() => AppService.InstallAsync("thixnote"));
        Assert.Contains("lehnt die Optionen ab: ThixNote verknüpft „.md“ nicht.", refused.Message);
        Assert.False(AppService.Get("thixnote").IsInstalled);
    }
}
