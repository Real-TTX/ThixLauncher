using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Platform;

/// <summary>
/// What the platforms read from an installation, as plain text rules (no system calls, so every platform can test them):
/// the program file of a Windows uninstall entry and the version of a macOS app bundle.
/// </summary>
internal static class InstallInfo
{
    /// <summary>The DisplayIcon of an uninstall entry ("C:\…\ThixClip.exe,0" or "\"C:\…\ThixNote.exe\"") → the path of the file.</summary>
    public static string? DisplayIconPath(string? displayIcon)
    {
        var value = displayIcon?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var comma = value.LastIndexOf(',');
        if (comma > 0 && int.TryParse(value[(comma + 1)..], out _))
        {
            value = value[..comma];
        }

        return value.Trim().Trim('"');
    }

    /// <summary>The version a bundle shows: "ThixClip 0.1.9-20261115" (CFBundleGetInfoString) or CFBundleShortVersionString.</summary>
    public static string? BundleVersion(ThixApp app, IReadOnlyDictionary<string, string> info)
    {
        var prefix = app.Name + " ";
        if (info.TryGetValue("CFBundleGetInfoString", out var infoString) && infoString.StartsWith(prefix, StringComparison.Ordinal) &&
            AppVersion.Parse(infoString[prefix.Length..]) is not null)
        {
            return infoString[prefix.Length..];
        }

        return info.GetValueOrDefault("CFBundleShortVersionString");
    }
}
