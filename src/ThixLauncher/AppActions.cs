using Avalonia.Controls;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Models;
using ThixLauncher.Dialogs;

namespace ThixLauncher;

/// <summary>What a button does with an app.</summary>
public enum AppAction
{
    Open,
    Install,
    Update,
    Uninstall,
}

/// <summary>A button for an app: what it does, its words and its icon (Action null: shown, but disabled).</summary>
public sealed record AppButton(AppAction? Action, string Text, string Icon, string? Tip = null);

/// <summary>
/// The actions on an app as the list, the detail page and the tray offer them – one place for their words, icons and the
/// question before removing. What may happen is decided by the business layer (<see cref="AppService"/>); its refusal comes
/// back as a <see cref="BusinessException"/> for the page to show.
/// </summary>
internal static class AppActions
{
    /// <summary>The main action of an app: Aktualisieren, Öffnen, Installieren – or a disabled word while it cannot act.</summary>
    public static AppButton Primary(AppStatus status)
    {
        if (status.IsBusy)
        {
            return new AppButton(null, "Bitte warten …", "clock");
        }

        var locked = AccessPolicy.AreAppChangesDisabled;
        return status.State switch
        {
            AppState.UpdateAvailable when !locked => new AppButton(AppAction.Update, "Aktualisieren", "download", $"Auf {status.Latest?.Version.Text} aktualisieren"),
            AppState.UpdateAvailable or AppState.Installed => status.CanLaunch
                ? new AppButton(AppAction.Open, "Öffnen", "play", $"{status.App.Name} starten")
                : new AppButton(null, "Öffnen", "play", "Die Programmdatei wurde nicht gefunden."),
            _ when status.Latest is null => new AppButton(null, "Nicht verfügbar", "x-circle", status.FeedError ?? "Für dieses System gibt es noch keinen Build."),
            _ when locked => new AppButton(null, "Installieren", "download", "Von deiner Organisation festgelegt"),
            _ => new AppButton(AppAction.Install, "Installieren", "download", status.App.InstallsQuietly ? null : "Das Setup der App öffnet sich und fragt nach den Optionen."),
        };
    }

    /// <summary>The second action of an installed app with an update: Öffnen (null: none).</summary>
    public static AppButton? Secondary(AppStatus status) =>
        status is { State: AppState.UpdateAvailable, CanLaunch: true } && !AccessPolicy.AreAppChangesDisabled
            ? new AppButton(AppAction.Open, "Öffnen", "play", $"{status.App.Name} starten")
            : null;

    /// <summary>Deinstallieren is offered for installed apps that are not busy (disabled under the policy).</summary>
    public static bool CanUninstall(AppStatus status) => status.IsInstalled && !status.IsBusy && !AccessPolicy.AreAppChangesDisabled && AppService.IsSupported;

    /// <summary>
    /// Runs the action; returns the sentence of what happened (null: nothing to say, e.g. after a start, or the user did not confirm).
    /// Throws <see cref="BusinessException"/> when the business layer refuses or the action failed.
    /// </summary>
    public static async Task<string?> RunAsync(AppAction action, AppStatus status, Window? owner)
    {
        var app = status.App;
        switch (action)
        {
            case AppAction.Open:
                AppService.Launch(app.Id);
                return null;

            case AppAction.Install:
            case AppAction.Update:
                await AppService.InstallAsync(app.Id);
                var now = AppService.Get(app.Id);
                return action == AppAction.Update
                    ? $"{app.Name} wurde auf {now.Installed?.VersionText} aktualisiert."
                    : $"{app.Name} {now.Installed?.VersionText} ist installiert.";

            case AppAction.Uninstall:
                if (!await ConfirmDialog.AskAsync(owner, $"{app.Name} deinstallieren?",
                        $"{app.Name} wird von diesem Computer entfernt. Deine Daten und Einstellungen der App bleiben erhalten – eine spätere Installation findet sie wieder.",
                        "Deinstallieren", isDestructive: true))
                {
                    return null;
                }

                await AppService.UninstallAsync(app.Id);
                return $"{app.Name} wurde entfernt.";

            default:
                return null;
        }
    }
}
