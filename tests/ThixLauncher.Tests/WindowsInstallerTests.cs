using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ThixLauncher.Core.Business;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;
using ThixLauncher.Core.Platform;

namespace ThixLauncher.Tests;

public sealed class WindowsInstallerTests
{
    private static readonly Guid Family = new("B75BDAC5-9408-45CC-B1BE-0AE9049DD8C0");
    private static ThixApp MsiApp => AppCatalog.Find("thixclip")! with { Name = "MSI test", WindowsMsiUpgradeCode = Family, WindowsQuietInstallArguments = null };

    [Theory]
    [InlineData("win-x64", "My app.MSI", true)]
    [InlineData("win-x64", "Wrong.exe", false)]
    [InlineData("win-x64", "../outside.msi", false)]
    [InlineData("win-x64", @"folder\outside.msi", false)]
    [InlineData("osx-arm64", "My app.msi", false)]
    [InlineData("osx-arm64", "My app.zip", true)]
    public void Only_an_opted_in_Windows_feed_accepts_a_safe_MSI_name(string platform, string file, bool expected)
    {
        Assert.Equal(expected, ReleaseFeed.IsAppPackageFileName(MsiApp, platform, file));
        Assert.False(ReleaseFeed.IsSafeFileName("My app.msi")); // The launcher self-update still accepts its original package types.
        Assert.False(ReleaseFeed.IsAppPackageFileName(AppCatalog.Find("thixclip")!, "win-x64", "My app.msi"));
    }

    [Fact]
    public void An_MSI_is_quiet_without_any_EXE_setup_arguments()
    {
        Assert.True(MsiApp.InstallsQuietly);
        var setup = AppInstallOptions.SetupFor(MsiApp, null, null, isUpdate: false);
        Assert.True(setup.WindowsQuiet);
        Assert.Empty(setup.WindowsArguments);
    }

    [Fact]
    public void Windows_Installer_gets_the_literal_package_or_product_and_never_reboots_the_machine()
    {
        if (!OperatingSystem.IsWindows()) return;
        string package = Path.Combine(Path.GetTempPath(), "Installer test (x64).msi");
        foreach (var (action, target) in new[] { ("/i", package), ("/x", Guid.NewGuid().ToString("B")) })
        {
            var command = WindowsInstaller.Command(action, target);
            Assert.Equal(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), command.FileName);
            Assert.Equal(new[] { action, target, "/qn", "/norestart" }, command.ArgumentList);
            Assert.False(command.UseShellExecute);
            Assert.True(command.CreateNoWindow);
        }
        foreach (int code in new[] { 0, 1641, 3010 })
        {
            Assert.True(WindowsInstaller.Succeeded(code));
        }
        Assert.False(WindowsInstaller.Succeeded(1603));
    }

    [Fact]
    public void Native_package_validation_accepts_only_the_expected_upgrade_family_before_starting_an_install()
    {
        if (!OperatingSystem.IsWindows()) return;
        CheckPackageFamilies();
    }

    [SupportedOSPlatform("windows")]
    private static void CheckPackageFamilies()
    {
        var folder = Directory.CreateTempSubdirectory("ThixLauncher-Msi-");
        try
        {
            string package = Path.Combine(folder.FullName, "Package.msi");
            CreateMetadataOnlyMsi(package, Family);
            WindowsInstaller.VerifyPackage(MsiApp, package, Family);
            Assert.Throws<AppSystemException>(() => WindowsInstaller.VerifyPackage(MsiApp, package, Guid.NewGuid()));
            var invalid = Path.Combine(folder.FullName, "Invalid.msi");
            File.WriteAllText(invalid, "Not an installer");
            Assert.Throws<AppSystemException>(() => WindowsInstaller.VerifyPackage(MsiApp, invalid, Family));
        }
        finally { folder.Delete(recursive: true); }
    }

    // A real database with just the metadata table; no user product is read, installed or registered.
    [SupportedOSPlatform("windows")]
    private static void CreateMetadataOnlyMsi(string path, Guid family)
    {
        Assert.Equal(0u, MsiOpenDatabaseW(path, 3, out uint database)); // MSIDBOPEN_CREATE
        try
        {
            Execute("CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) NOT NULL PRIMARY KEY `Property`)");
            Execute($"INSERT INTO `Property` (`Property`, `Value`) VALUES ('UpgradeCode', '{family:B}')");
            Assert.Equal(0u, MsiDatabaseCommit(database));
        }
        finally { MsiCloseHandle(database); }

        void Execute(string sql)
        {
            Assert.Equal(0u, MsiDatabaseOpenViewW(database, sql, out uint view));
            try { Assert.Equal(0u, MsiViewExecute(view, 0)); }
            finally { MsiCloseHandle(view); }
        }
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiOpenDatabaseW(string path, nint persist, out uint database);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiDatabaseOpenViewW(uint database, string query, out uint view);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiViewExecute(uint view, uint record);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiDatabaseCommit(uint database);
    [DllImport("msi.dll", ExactSpelling = true)]
    private static extern uint MsiCloseHandle(uint handle);
}
