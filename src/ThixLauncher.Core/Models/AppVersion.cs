using System.Globalization;
using System.Text.RegularExpressions;

namespace ThixLauncher.Core.Models;

/// <summary>
/// A version of a Thix app as every app of the family writes it (Directory.Build.props of each app):
///   release  &lt;major&gt;.&lt;minor&gt;.&lt;build&gt;-&lt;date&gt;   0.1.42-20261116 (a tag may lead with "v"; macOS bundles may say 0.1.42)
///   nightly  nightly-&lt;build&gt;-&lt;date&gt;            nightly-43-20261116
///   local    local-&lt;date&gt;                      local-20261116
/// The build number is the run number of the app's CI and grows within a channel: it is what the update check compares. A
/// release compares major and minor first: an app whose CI started over (a new workflow, ThixNote 1.1.1 after 1.0.5) is
/// still newer than the builds before.
/// </summary>
public sealed partial record AppVersion(string Text, string Channel, int BuildNumber, int Major = 0, int Minor = 0)
{
    public const string Release = "release";
    public const string Nightly = "nightly";
    public const string Local = "local";

    /// <summary>The version, or null when the text is none of the family's forms.</summary>
    public static AppVersion? Parse(string? text)
    {
        var value = text?.Trim() ?? "";
        if (value.Length == 0)
        {
            return null;
        }

        if (NightlyPattern().Match(value) is { Success: true } nightly)
        {
            return new AppVersion(value, Nightly, int.Parse(nightly.Groups["build"].Value, CultureInfo.InvariantCulture));
        }

        if (LocalPattern().IsMatch(value))
        {
            return new AppVersion(value, Local, 0);
        }

        if (ReleasePattern().Match(value) is { Success: true } release)
        {
            var shown = value.StartsWith('v') || value.StartsWith('V') ? value[1..] : value;
            return new AppVersion(shown, Release, int.Parse(release.Groups["build"].Value, CultureInfo.InvariantCulture),
                int.Parse(release.Groups["major"].Value, CultureInfo.InvariantCulture), int.Parse(release.Groups["minor"].Value, CultureInfo.InvariantCulture));
        }

        return null;
    }

    /// <summary>
    /// True when this version comes after <paramref name="installed"/>: the same channel and a higher build number – for a
    /// release a higher major.minor first, then the build number.
    /// </summary>
    public bool IsNewerThan(AppVersion installed)
    {
        if (Channel == Local || !string.Equals(Channel, installed.Channel, StringComparison.Ordinal))
        {
            return false;
        }

        return Channel == Release
            ? (Major, Minor, BuildNumber).CompareTo((installed.Major, installed.Minor, installed.BuildNumber)) > 0
            : BuildNumber > installed.BuildNumber;
    }

    public override string ToString() => Text;

    [GeneratedRegex(@"^nightly-(?<build>\d{1,9})(-\d{8})?$", RegexOptions.IgnoreCase)]
    private static partial Regex NightlyPattern();

    [GeneratedRegex(@"^local(-\d{8})?$", RegexOptions.IgnoreCase)]
    private static partial Regex LocalPattern();

    [GeneratedRegex(@"^[vV]?(?<major>\d{1,4})\.(?<minor>\d{1,4})\.(?<build>\d{1,9})(\.\d+)?(-\d{8})?$")]
    private static partial Regex ReleasePattern();
}
