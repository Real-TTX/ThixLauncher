using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Business;

/// <summary>
/// Reading and changing the settings of this computer. Every change is checked here (values, policy) before it is saved;
/// the UI only offers. <see cref="Changed"/> tells the app (theme, channel, updates) what is new.
/// </summary>
public static class SettingsService
{
    /// <summary>Raised after every saved change (any thread).</summary>
    public static event EventHandler<AppSettings>? Changed;

    public static AppSettings Get() => SettingsStore.Read();

    /// <summary>Changes settings the user chooses (not locked by a policy, valid values only).</summary>
    public static AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        AccessPolicy.EnsureConfigurationEditable();
        var current = SettingsStore.Read();
        var next = change(current) with { Cloud = current.Cloud };
        Validate(next);
        if (next.AppChannel != current.AppChannel)
        {
            Log.Info($"Channel of the apps: {next.AppChannel}");
        }

        return Save(_ => next);
    }

    /// <summary>Things the app remembers by itself (window size, folded menu, tiles or list): no configuration lock.</summary>
    public static AppSettings Remember(Func<AppSettings, AppSettings> change)
    {
        var current = SettingsStore.Read();
        var next = change(current);
        return Save(_ => current with
        {
            WindowWidth = Math.Clamp(next.WindowWidth, 640, 4000),
            WindowHeight = Math.Clamp(next.WindowHeight, 420, 3000),
            IsSidebarCollapsed = next.IsSidebarCollapsed,
            AppsView = Enum.IsDefined(next.AppsView) ? next.AppsView : AppsView.Tiles,
        });
    }

    /// <summary>The connection to MatCloud (only <see cref="CloudService"/>: it comes from the sign-in, not from a choice of the user).</summary>
    internal static AppSettings UpdateCloud(Func<CloudSettings, CloudSettings> change) =>
        Save(settings => settings with { Cloud = change(settings.Cloud) });

    private static AppSettings Save(Func<AppSettings, AppSettings> change)
    {
        var saved = SettingsStore.Update(change);
        Changed?.Invoke(null, saved);
        return saved;
    }

    private static void Validate(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.Theme))
        {
            throw new BusinessException("Dieses Erscheinungsbild gibt es nicht.", nameof(AppSettings.Theme));
        }

        if (!Enum.IsDefined(settings.AppChannel))
        {
            throw new BusinessException("Diesen Kanal gibt es nicht.", nameof(AppSettings.AppChannel));
        }

        if (settings.InstallUpdatesAutomatically && AccessPolicy.AreUpdatesDisabled)
        {
            throw new BusinessException("Updates wurden von einem Administrator deaktiviert (Richtlinie DisableUpdates).", nameof(AppSettings.InstallUpdatesAutomatically));
        }

        if (settings.UpdateAppsAutomatically && AccessPolicy.AreAppChangesDisabled)
        {
            throw new BusinessException("Apps dürfen auf diesem Computer nicht geändert werden (Richtlinie DisableAppChanges).", nameof(AppSettings.UpdateAppsAutomatically));
        }
    }
}
