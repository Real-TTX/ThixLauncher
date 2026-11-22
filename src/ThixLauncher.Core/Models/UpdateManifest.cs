using System.Text.Json.Serialization;

namespace ThixLauncher.Core.Models;

/// <summary>One downloadable file of a version (Windows setup, zipped Mac app).</summary>
public sealed record UpdatePackage(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("sha256")] string Sha256);

/// <summary>
/// The manifest the CI of ThixLauncher (and of ThixClip) publishes next to the packages in the rolling release of a channel.
/// <see cref="Setup"/>/<see cref="Sha256"/> are the Windows setup; <see cref="Packages"/> holds one package per platform
/// ("win-x64", "osx-arm64", "osx-x64"); <see cref="Installer"/> is what the setup of the app offers (SETUP-08, null: an older build
/// that does not know the install options).
/// </summary>
public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("buildNumber")] int BuildNumber,
    [property: JsonPropertyName("setup")] string? Setup,
    [property: JsonPropertyName("sha256")] string? Sha256,
    [property: JsonPropertyName("publishedAt")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("packages")] Dictionary<string, UpdatePackage>? Packages = null,
    [property: JsonPropertyName("installer")] InstallerConfig? Installer = null)
{
    /// <summary>The package of a platform; Windows falls back to the setup fields.</summary>
    public UpdatePackage? PackageFor(string platformKey)
    {
        if (Packages?.TryGetValue(platformKey, out var package) == true)
        {
            return package;
        }

        return platformKey.StartsWith("win-", StringComparison.Ordinal) && Setup is { Length: > 0 }
            ? new UpdatePackage(Setup, Sha256 ?? string.Empty)
            : null;
    }
}
