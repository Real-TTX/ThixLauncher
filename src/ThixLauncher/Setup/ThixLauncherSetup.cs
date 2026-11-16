using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using MatDesktop;
using ThixLauncher.Controls;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;

namespace ThixLauncher.Setup;

/// <summary>
/// The Windows setup of ThixLauncher in MatDesktop's setup window (SETUP-01, SETUP-02): one window for install, update and
/// uninstall, 600 × 560, the footer with "Version x · channel" (SETUP-07). Install: Welcome (per user, no administrator
/// rights, where to: SETUP-03) → Options → Progress → Finish. Update: Welcome with the installed and the new version →
/// Progress → Finish. Uninstall: what goes and "Auch meine Einstellungen löschen" (off) → Progress → Finish (SETUP-04). The
/// apps installed with the launcher stay: they are programs of their own. The work (InstallService) runs on a background
/// task and reports its steps; the window only steers and cannot be closed halfway through.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ThixLauncherSetup
{
    /// <summary>Shows the assistant; <paramref name="finished"/> gets true when the program folder has to be removed after this process ended.</summary>
    public static void Run(bool isUninstall, Action<bool> finished)
    {
        var removeProgramFolder = false;
        var isUpdate = !isUninstall && InstallService.IsInstalled;
        var source = App.ExecutablePath;
        MatSetupWindow? window = null;
        window = Create(isUninstall, isUpdate, InstallService.InstalledVersion, InstallService.ProgramFolder, async run =>
        {
            void Report(string step) => run.Progress.Report(new MatSetupReport(step, MatSetupStepState.Done));
            try
            {
                if (isUninstall)
                {
                    removeProgramFolder = await Task.Run(() => InstallService.Uninstall(run.DeleteData, Report));
                    window!.Steps[^1].Text = UninstalledText(run.DeleteData);
                }
                else
                {
                    var options = isUpdate
                        ? null
                        : new InstallOptions(run.Options.GetValueOrDefault("autostart"), run.Options.GetValueOrDefault("shortcut"), run.Options.GetValueOrDefault("updates"));
                    await Task.Run(() => InstallService.Install(source, options, Report));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
                                                  or System.Runtime.InteropServices.COMException or System.Security.SecurityException)
            {
                Log.Error(isUninstall ? "Uninstall failed" : "Setup failed", exception);
                throw new InvalidOperationException($"{exception.Message}\n\nDetails stehen im Protokoll ({AppPaths.LogDirectory}).", exception);
            }
        });

        window.Finished += (_, _) =>
        {
            if (window.StartNow)
            {
                InstallService.StartInstalled();
            }

            finished(removeProgramFolder);
        };
        window.Show();
        window.Activate();
    }

    /// <summary>
    /// The window for install (or update) or uninstall, with what it shows decided here once: the mode, the installed
    /// version and the program folder. <paramref name="work"/> is the work of the Install, Update or Uninstall button.
    /// The self test and the snapshots pass work that touches nothing.
    /// </summary>
    public static MatSetupWindow Create(bool isUninstall, bool isUpdate, string? installedVersion, string programFolder, Func<MatSetupRun, Task> work)
    {
        var mode = isUninstall ? MatSetupMode.Uninstall : isUpdate ? MatSetupMode.Update : MatSetupMode.Setup;
        IReadOnlyList<MatSetupStep> steps = mode switch
        {
            MatSetupMode.Uninstall =>
            [
                MatSetupStep.Uninstall("Programm, Startmenü-Eintrag, Desktop-Verknüpfung und Autostart werden entfernt. Die installierten Apps (ThixClip, ThixNote …) bleiben – sie haben ihre eigenen Einträge unter „Installierte Apps“."),
            ],
            MatSetupMode.Update =>
            [
                MatSetupStep.Welcome(string.Empty, Note("Ein laufender ThixLauncher wird beendet und danach wieder gestartet. Deine Apps und Einstellungen bleiben.")),
            ],
            _ =>
            [
                // SETUP-03: for the user, without administrator rights, and where to (the folder on a line of its own).
                MatSetupStep.Welcome(
                    "ThixLauncher installiert die Thix-Apps mit einem Klick, hält sie aktuell und startet sie. Er läuft im Infobereich der Taskleiste.",
                    Note($"Wird für dein Benutzerkonto installiert – ohne Administratorrechte – nach\n{programFolder}")),
                MatSetupStep.WithOptions(null,
                    MatSetupOption.Autostart("Im Infobereich, ohne Fenster – hält die Apps aktuell"),
                    MatSetupOption.DesktopShortcut("Ein Startmenü-Eintrag wird immer angelegt"),
                    MatSetupOption.AutomaticUpdates("Neue Versionen von ThixLauncher ohne Rückfrage, er startet danach neu")),
            ],
        };

        var finish = isUninstall
            ? MatSetupStep.Finish(UninstalledText(deleteData: false), offersStart: false)
            : MatSetupStep.Finish($"ThixLauncher {AppInfo.Version} ist installiert. Du findest ihn im Startmenü und im Infobereich der Taskleiste.");
        var version = isUninstall ? installedVersion ?? AppInfo.Version : AppInfo.Version;
        return new MatSetupWindow(new MatSetup(AppInfo.ProductName, version, AppInfo.Channel, mode, steps, work)
        {
            Logo = Ui.Logo,
            WordMark = WordMark(),
            InstalledVersion = installedVersion ?? "eine ältere Version",
            Finish = finish,
            DeleteDataText = "Auch meine Einstellungen löschen",
            DeleteDataDescription = "Einstellungen und Protokolle von ThixLauncher auf diesem Computer. Die Apps und ihre Daten bleiben in jedem Fall.",
        });
    }

    /// <summary>The word mark of the title bar ("Thix" and "Launcher" in the accent text) for the header of the setup.</summary>
    private static TextBlock WordMark()
    {
        var mark = new TextBlock { FontSize = MatTokens.Type.Fs2xl, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var launcher = new Run("Launcher");
        launcher.Bind(TextElement.ForegroundProperty, mark.GetResourceObservable("Mat.AccentText"));
        mark.Inlines = [new Run("Thix"), launcher];
        return mark;
    }

    /// <summary>A note under the text of a step, in the tint of information.</summary>
    private static InfoBar Note(string text)
    {
        var note = new InfoBar { Margin = new Avalonia.Thickness(0) };
        note.Show(text, InfoSeverity.Info);
        return note;
    }

    private static string UninstalledText(bool deleteData) =>
        deleteData ? "ThixLauncher und seine Einstellungen wurden entfernt. Die Apps bleiben installiert." : "ThixLauncher wurde entfernt. Die Apps bleiben installiert.";
}
