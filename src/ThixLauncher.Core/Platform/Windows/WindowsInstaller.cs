using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Platform;

/// <summary>Windows Installer owns product registration. Discover an installation by its stable family and remove it by its current ProductCode.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsInstaller
{
    private const uint MoreData = 234;
    private const uint NoMoreItems = 259;
    private const int Installed = 5;
    private const int GuidBufferSize = 39;

    public static InstalledApp? Detect(ThixApp app, Guid family)
    {
        var product = FindProduct(family);
        if (product is null)
        {
            return null;
        }

        var location = ProductInfo(product, "InstallLocation");
        if (string.IsNullOrWhiteSpace(location))
        {
            // A per-user MSI can omit ARPINSTALLLOCATION. This is the install directory of the catalog's MSI apps.
            location = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", app.Name);
        }

        var executable = Path.Combine(location, app.WindowsExecutable);
        FileVersionInfo? binary = File.Exists(executable) ? FileVersionInfo.GetVersionInfo(executable) : null;
        bool matches = string.Equals(binary?.ProductName, app.Name, StringComparison.OrdinalIgnoreCase);
        // MSI's numeric ProductVersion cannot distinguish nightly from release. The application binary can.
        var version = matches && AppVersion.Parse(binary?.ProductVersion) is not null ? binary!.ProductVersion : ProductInfo(product, "VersionString");
        return new InstalledApp(version, location, matches ? executable : null);
    }

    internal static string? FindProduct(Guid family)
    {
        var familyCode = family.ToString("B").ToUpperInvariant();
        var productCode = new StringBuilder(GuidBufferSize);
        for (uint index = 0; ; index++)
        {
            uint result = MsiEnumRelatedProductsW(familyCode, 0, index, productCode);
            if (result == NoMoreItems)
            {
                return null;
            }

            if (result != 0)
            {
                throw new IOException($"Windows Installer konnte die installierten Produkte nicht lesen (Fehler {result}).");
            }

            var product = productCode.ToString();
            if (MsiQueryProductStateW(product) == Installed)
            {
                return product;
            }
        }
    }

    private static string? ProductInfo(string product, string property)
    {
        var value = new StringBuilder(256);
        uint count = (uint)value.Capacity;
        uint result = MsiGetProductInfoW(product, property, value, ref count);
        if (result == MoreData)
        {
            value.EnsureCapacity(checked((int)count + 1));
            count = (uint)value.Capacity;
            result = MsiGetProductInfoW(product, property, value, ref count);
        }
        return result == 0 ? value.ToString() : null;
    }

    internal static void VerifyPackage(ThixApp app, string path, Guid expectedFamily)
    {
        if (!path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            throw new AppSystemException($"Für {app.Name} wird ein MSI-Paket erwartet.");
        }

        uint database = 0, view = 0, record = 0;
        try
        {
            Check(MsiOpenDatabaseW(Path.GetFullPath(path), 0, out database));
            Check(MsiDatabaseOpenViewW(database, "SELECT `Value` FROM `Property` WHERE `Property` = 'UpgradeCode'", out view));
            Check(MsiViewExecute(view, 0));
            Check(MsiViewFetch(view, out record));
            uint length = GuidBufferSize;
            var upgradeCode = new StringBuilder(GuidBufferSize);
            Check(MsiRecordGetStringW(record, 1, upgradeCode, ref length));
            if (!Guid.TryParse(upgradeCode.ToString(), out var actual) || actual != expectedFamily)
            {
                throw new AppSystemException($"Das MSI-Paket gehört nicht zu {app.Name}.");
            }
        }
        finally
        {
            if (record != 0) MsiCloseHandle(record);
            if (view != 0) MsiCloseHandle(view);
            if (database != 0) MsiCloseHandle(database);
        }

        void Check(uint code)
        {
            if (code != 0)
            {
                throw new AppSystemException($"Das MSI-Paket für {app.Name} konnte nicht gelesen werden (Fehler {code}).");
            }
        }
    }

    internal static ProcessStartInfo Command(string action, string target)
    {
        var command = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { action, target, "/qn", "/norestart" },
        };
        return command;
    }

    internal static bool Succeeded(int exitCode) => exitCode is 0 or 1641 or 3010;

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiEnumRelatedProductsW(string upgradeCode, uint reserved, uint index, StringBuilder productCode);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MsiQueryProductStateW(string product);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiGetProductInfoW(string product, string property, StringBuilder value, ref uint count);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiOpenDatabaseW(string path, nint persist, out uint database);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiDatabaseOpenViewW(uint database, string query, out uint view);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewExecute(uint view, uint parameterRecord);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewFetch(uint view, out uint record);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiRecordGetStringW(uint record, uint field, StringBuilder value, ref uint count);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiCloseHandle(uint handle);
}
