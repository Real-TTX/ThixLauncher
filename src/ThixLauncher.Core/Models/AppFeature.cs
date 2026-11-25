namespace ThixLauncher.Core.Models;

/// <summary>
/// A notable feature of an app (<see cref="ThixApp.Features"/>), shown as a small chip in the store and on the app's page and
/// listed in the catalog of the website. A new feature is a value here and its words in <see cref="AppFeatures"/>.
/// </summary>
public enum AppFeature
{
    /// <summary>Syncs through MatCloud: the app has a <see cref="ThixApp.CloudApp"/> and takes the launcher's sign-in.</summary>
    Cloud,

    /// <summary>Speaks the Model Context Protocol: AI assistants can use the app.</summary>
    Mcp,
}

/// <summary>What the store and the website say about an <see cref="AppFeature"/>.</summary>
public static class AppFeatures
{
    /// <summary>Every feature in the order the store shows them.</summary>
    public static IReadOnlyList<AppFeature> All { get; } = Enum.GetValues<AppFeature>();

    /// <summary>The key of the feature in the catalog of the website ("cloud", "mcp").</summary>
    public static string Key(AppFeature feature) => feature switch
    {
        AppFeature.Cloud => "cloud",
        AppFeature.Mcp => "mcp",
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
    };

    /// <summary>The word on the chip ("Cloud", "MCP").</summary>
    public static string Name(AppFeature feature) => feature switch
    {
        AppFeature.Cloud => "Cloud",
        AppFeature.Mcp => "MCP",
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
    };

    /// <summary>One sentence for the tool tip of the chip and the website.</summary>
    public static string Description(AppFeature feature) => feature switch
    {
        AppFeature.Cloud => "Synchronisiert über MatCloud auf allen Geräten",
        AppFeature.Mcp => "KI-Assistenten können die App über das Model Context Protocol nutzen",
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
    };

    /// <summary>The icon of MatDesign's sprite on the chip.</summary>
    public static string Icon(AppFeature feature) => feature switch
    {
        AppFeature.Cloud => "cloud",
        AppFeature.Mcp => "sparkles",
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
    };
}
