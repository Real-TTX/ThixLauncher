using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Business;

/// <summary>
/// The permission checks of the business layer. Every service calls them itself before it changes something, whatever
/// the UI shows or hides.
///
/// An administrator can restrict ThixLauncher with machine policies (value 1 / true):
///   Windows: HKLM\SOFTWARE\Policies\ThixLauncher (DWORD)
///   macOS:   a configuration profile for the domain info.schmoldt.thixlauncher (/Library/Managed Preferences)
/// Policies:
///   LockConfiguration   the settings are read-only (starting apps stays possible)
///   DisableAppChanges   no app is installed, updated or removed through the launcher (it only shows and starts them)
///   DisableUpdates      ThixLauncher neither looks for nor installs new versions of itself
/// </summary>
public static class AccessPolicy
{
    public const string MacPolicyDomain = "info.schmoldt.thixlauncher";

    private const string PolicyKeyPath = @"SOFTWARE\Policies\ThixLauncher";

    private static readonly string MacPolicyFile = $"/Library/Managed Preferences/{MacPolicyDomain}.plist";

    private static Func<string, bool> _readPolicy = ReadMachinePolicy;

    private static (DateTime Written, Dictionary<string, bool> Values) _macPolicies = (DateTime.MinValue, []);

    public static bool IsConfigurationLocked => _readPolicy("LockConfiguration");

    public static bool AreAppChangesDisabled => _readPolicy("DisableAppChanges");

    public static bool AreUpdatesDisabled => _readPolicy("DisableUpdates");

    /// <summary>Any policy is set (the pages say "Von deiner Organisation festgelegt").</summary>
    public static bool IsAnySet => IsConfigurationLocked || AreAppChangesDisabled || AreUpdatesDisabled;

    public static void EnsureConfigurationEditable()
    {
        if (IsConfigurationLocked)
        {
            throw new BusinessException("Die Einstellungen wurden von einem Administrator gesperrt (Richtlinie LockConfiguration).");
        }
    }

    public static void EnsureAppChangesAllowed()
    {
        if (AreAppChangesDisabled)
        {
            throw new BusinessException("Apps dürfen auf diesem Computer nicht installiert, aktualisiert oder entfernt werden (Richtlinie DisableAppChanges).");
        }
    }

    public static void EnsureUpdatesAllowed()
    {
        if (AreUpdatesDisabled)
        {
            throw new BusinessException("Updates wurden von einem Administrator deaktiviert (Richtlinie DisableUpdates).");
        }
    }

    /// <summary>Tests replace the registry with their own answers.</summary>
    internal static void UsePolicyReaderForTests(Func<string, bool>? reader) => _readPolicy = reader ?? ReadMachinePolicy;

    private static bool ReadMachinePolicy(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            return ReadWindowsPolicy(name);
        }

        return OperatingSystem.IsMacOS() && ReadMacPolicies().GetValueOrDefault(name);
    }

    [SupportedOSPlatform("windows")]
    private static bool ReadWindowsPolicy(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(PolicyKeyPath);
        return key?.GetValue(name) is int value && value == 1;
    }

    /// <summary>The managed preferences are a (binary) plist; plutil turns it into JSON. Read again only after a change.</summary>
    private static Dictionary<string, bool> ReadMacPolicies()
    {
        var written = File.Exists(MacPolicyFile) ? File.GetLastWriteTimeUtc(MacPolicyFile) : DateTime.MinValue;
        if (written == _macPolicies.Written)
        {
            return _macPolicies.Values;
        }

        var values = new Dictionary<string, bool>();
        if (written != DateTime.MinValue)
        {
            try
            {
                var startInfo = new ProcessStartInfo("/usr/bin/plutil") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "-convert", "json", "-o", "-", MacPolicyFile })
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using var process = Process.Start(startInfo)!;
                var json = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                using var document = JsonDocument.Parse(json);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    values[property.Name] = property.Value.ValueKind == JsonValueKind.True ||
                                            (property.Value.ValueKind == JsonValueKind.Number && property.Value.GetInt32() == 1);
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Log.Warning($"Could not read the policies in {MacPolicyFile}: {exception.Message}");
            }
        }

        _macPolicies = (written, values);
        return values;
    }
}
