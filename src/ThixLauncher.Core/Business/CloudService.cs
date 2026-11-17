using MatCloud.Contract;
using ThixLauncher.Core.Infrastructure;
using ThixLauncher.Core.Models;

namespace ThixLauncher.Core.Business;

public enum CloudState
{
    /// <summary>Not signed in.</summary>
    Off,

    /// <summary>Signing in right now.</summary>
    Connecting,

    /// <summary>Signed in; the cloud answered the last time it was asked.</summary>
    Online,

    /// <summary>Signed in, but the cloud did not answer (the apps are installed anyway, without the hand-over).</summary>
    Offline,

    /// <summary>The cloud did not know the device any more (removed in the browser): signed out here too.</summary>
    SignedOut,
}

/// <summary>Where the connection to MatCloud stands (settings page, menu).</summary>
public sealed record CloudStatus(CloudState State, string Message)
{
    public bool IsConnected => State is CloudState.Online or CloudState.Offline;
}

/// <summary>
/// ThixLauncher at MatCloud (as the app "launcher"): signing in (in the browser or with a password) and out, and the one thing it is signed in for –
/// an app it installs is signed in too (<see cref="HandOffAsync"/>): before the app's setup runs, the launcher asks MatCloud for a sign-in of the
/// app and leaves it in <see cref="CloudHandoff"/>; the app takes it at its first start. Nothing here may hold up the apps: a cloud that does not
/// answer only means that the app signs in by itself.
/// </summary>
public static class CloudService
{
    private const string NotConnected = "Nicht verbunden";
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(8);

    private static CloudStatus? _status;

    /// <summary>Raised when <see cref="Status"/> changes (any thread).</summary>
    public static event EventHandler<CloudStatus>? StatusChanged;

    public static CloudStatus Status => _status ??= Initial();

    public static bool IsConnected => SettingsService.Get().Cloud.IsConnected;

    /// <summary>"ThixLauncher/0.1.42-20261117 (Windows)": the cloud shows the version with the device.</summary>
    public static string UserAgent => $"ThixLauncher/{Token(AppInfo.Version)} ({Token(AppInfo.PlatformName)})";

    /// <summary>Tests talk to a cloud in memory.</summary>
    internal static Func<HttpMessageHandler>? HandlerForTests { get; set; }

    /// <summary>
    /// Signs ThixLauncher in in the browser (every account, also one of MatIdentity): MatCloud answers a code, <paramref name="onCode"/> shows it,
    /// <paramref name="openAddress"/> opens the page where the person confirms it; this waits until it is confirmed, refused or expired. Returns false
    /// when <paramref name="cancellationToken"/> ended the wait.
    /// </summary>
    public static async Task<bool> ConnectInBrowserAsync(string serverUrl, string deviceName, Action<DeviceLinkStart> onCode, Action<string> openAddress,
        CancellationToken cancellationToken = default)
    {
        AccessPolicy.EnsureCloudAllowed();
        var url = NormalizeServerUrl(serverUrl);
        deviceName = CheckDeviceName(deviceName);
        if (IsConnected)
        {
            await DisconnectAsync();
        }

        SetStatus(new CloudStatus(CloudState.Connecting, "Warte auf die Bestätigung im Browser …"));
        DeviceLoginResponse login;
        try
        {
            using var client = Client(url, null);
            var start = await client.StartLinkAsync(new DeviceLinkRequest(deviceName, AppInfo.PlatformName, AppInfo.Version, CloudApps.Launcher), cancellationToken);
            onCode(start);
            openAddress(start.VerificationUrl);
            login = await client.WaitForLinkAsync(start, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(new CloudStatus(CloudState.Off, NotConnected));
            return false;
        }
        catch (Exception exception) when (IsCloudProblem(exception) || exception is UriFormatException)
        {
            SetStatus(new CloudStatus(CloudState.Off, NotConnected));
            throw SignInFailed(exception, url);
        }

        Complete(url, login);
        return true;
    }

    /// <summary>Signs ThixLauncher in with the user name and password of a local account. The password is used once and never stored.</summary>
    public static async Task ConnectAsync(string serverUrl, string userName, string password, string deviceName, CancellationToken cancellationToken = default)
    {
        AccessPolicy.EnsureCloudAllowed();
        var url = NormalizeServerUrl(serverUrl);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BusinessException("Bitte den Benutzernamen angeben.", nameof(CloudSettings.UserName));
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new BusinessException("Bitte das Passwort angeben.", "Password");
        }

        deviceName = CheckDeviceName(deviceName);
        if (IsConnected)
        {
            await DisconnectAsync();
        }

        SetStatus(new CloudStatus(CloudState.Connecting, "Verbinde …"));
        DeviceLoginResponse login;
        try
        {
            using var client = Client(url, null);
            login = await client.LoginAsync(new DeviceLoginRequest(userName.Trim(), password, deviceName, AppInfo.PlatformName, AppInfo.Version, CloudApps.Launcher),
                cancellationToken);
        }
        catch (CloudException exception) when (exception.IsUnauthorized)
        {
            SetStatus(new CloudStatus(CloudState.Off, NotConnected));
            throw new BusinessException("Benutzername oder Passwort stimmen nicht.", "Password");
        }
        catch (Exception exception) when (IsCloudProblem(exception) || exception is UriFormatException)
        {
            SetStatus(new CloudStatus(CloudState.Off, NotConnected));
            throw SignInFailed(exception, url);
        }

        Complete(url, login);
    }

    /// <summary>Signs ThixLauncher out (the cloud forgets the device). The apps it connected stay connected: each is a device of its own.</summary>
    public static async Task DisconnectAsync()
    {
        var cloud = SettingsService.Get().Cloud;
        if (cloud.IsConnected && ReadToken(cloud) is { } token)
        {
            try
            {
                using var client = Client(cloud.ServerUrl, token);
                await client.LogoutAsync(CancellationToken.None);
            }
            catch (Exception exception) when (IsCloudProblem(exception))
            {
                Log.Warning($"Logout at the cloud failed ({exception.Message}); signing out locally");
            }
        }

        Forget(cloud);
        SetStatus(new CloudStatus(CloudState.Off, NotConnected));
    }

    /// <summary>
    /// Asks the cloud whether it still knows this device (at start and after a channel change, in the background): a removed device signs out here
    /// too; a cloud that does not answer only changes the state.
    /// </summary>
    public static async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        var cloud = SettingsService.Get().Cloud;
        if (!cloud.IsConnected || AccessPolicy.IsCloudDisabled)
        {
            SetStatus(Initial());
            return;
        }

        if (ReadToken(cloud) is not { } token)
        {
            SignedOut(cloud, "Der Zugang zur MatCloud fehlt – bitte neu anmelden.");
            return;
        }

        try
        {
            using var client = Client(cloud.ServerUrl, token);
            var account = await client.GetAccountAsync(cancellationToken);
            if (account.DisplayName != cloud.DisplayName || account.DeviceName != cloud.DeviceName)
            {
                SettingsService.UpdateCloud(current => current with { DisplayName = account.DisplayName, DeviceName = account.DeviceName });
            }

            SetStatus(new CloudStatus(CloudState.Online, "Verbunden"));
        }
        catch (CloudException exception) when (exception.IsUnauthorized)
        {
            SignedOut(cloud, "Dieser Computer wurde in der MatCloud abgemeldet – bitte neu anmelden.");
        }
        catch (Exception exception) when (IsCloudProblem(exception))
        {
            Log.Warning($"The cloud did not answer ({exception.Message})");
            SetStatus(new CloudStatus(CloudState.Offline, "Die MatCloud ist gerade nicht erreichbar."));
        }
    }

    /// <summary>
    /// Before ThixLauncher installs <paramref name="app"/>: asks MatCloud for a sign-in of the app on this computer and leaves it for the app's first
    /// start (<see cref="CloudHandoff"/>). Only when ThixLauncher is signed in, the user wants it (<see cref="AppSettings.ConnectInstalledApps"/>)
    /// and the app knows the cloud. Never throws and waits at most a few seconds: a cloud that does not answer, refuses (the app is switched off) or
    /// signed this device out leaves the app to sign in by itself. Returns the account the app is signed in for, or null.
    /// </summary>
    public static async Task<string?> HandOffAsync(ThixApp app, string appVersion, CancellationToken cancellationToken = default)
    {
        var settings = SettingsService.Get();
        var cloud = settings.Cloud;
        if (app.CloudApp is not { } cloudApp || !cloud.IsConnected || !settings.ConnectInstalledApps || AccessPolicy.IsCloudDisabled
            || ReadToken(cloud) is not { } token)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HandoffTimeout);
        try
        {
            using var client = Client(cloud.ServerUrl, token);
            var start = await client.HandOffAsync(new DeviceHandoffRequest(cloudApp, appVersion), timeout.Token);
            CloudHandoff.Write(cloudApp, new DeviceHandoff(cloud.ServerUrl, start.PollToken, cloud.UserName,
                DateTimeOffset.UtcNow.AddSeconds(start.ExpiresInSeconds)));
            Log.Info($"Left a sign-in of the cloud for {app.Id}");
            SetStatus(new CloudStatus(CloudState.Online, "Verbunden"));
            return cloud.AccountText;
        }
        catch (CloudException exception) when (exception.IsUnauthorized)
        {
            SignedOut(cloud, "Dieser Computer wurde in der MatCloud abgemeldet – bitte neu anmelden.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsCloudProblem(exception) || exception is OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            Log.Warning($"No sign-in of the cloud for {app.Id} ({exception.Message}); it signs in by itself");
            if (exception is not CloudException)
            {
                SetStatus(new CloudStatus(CloudState.Offline, "Die MatCloud ist gerade nicht erreichbar."));
            }
        }

        return null;
    }

    /// <summary>The app was removed: a sign-in it never took goes too.</summary>
    public static void ForgetHandoff(ThixApp app)
    {
        if (app.CloudApp is { } cloudApp)
        {
            CloudHandoff.Remove(cloudApp);
        }
    }

    /// <summary>The address as it is stored: http(s) ("https://" when it is missing), no trailing slash.</summary>
    public static string NormalizeServerUrl(string serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new BusinessException("Bitte die Adresse der Cloud angeben.", nameof(CloudSettings.ServerUrl));
        }

        try
        {
            return MatCloudClient.NormalizeServerUrl(serverUrl);
        }
        catch (UriFormatException)
        {
            throw new BusinessException("Das ist keine gültige Adresse (z. B. https://cloud.example.com).", nameof(CloudSettings.ServerUrl));
        }
    }

    /// <summary>Forgets the state (tests, snapshots).</summary>
    internal static void ResetForTests(CloudStatus? status = null) => _status = status;

    private static CloudStatus Initial()
    {
        if (AccessPolicy.IsCloudDisabled)
        {
            return new CloudStatus(CloudState.Off, "Durch Richtlinie ausgeschaltet");
        }

        return SettingsService.Get().Cloud.IsConnected ? new CloudStatus(CloudState.Online, "Verbunden") : new CloudStatus(CloudState.Off, NotConnected);
    }

    private static void Complete(string url, DeviceLoginResponse login)
    {
        var protectedToken = TokenStore.Save(AccountKey(url, login.Account.UserName), login.Token);
        SettingsService.UpdateCloud(_ => new CloudSettings
        {
            ServerUrl = url,
            UserName = login.Account.UserName,
            DisplayName = login.Account.DisplayName,
            DeviceName = login.Account.DeviceName,
            DeviceId = login.DeviceId,
            ProtectedToken = protectedToken,
        });
        Log.Info($"Connected to {url} as device {login.DeviceId}");
        SetStatus(new CloudStatus(CloudState.Online, "Verbunden"));
    }

    private static void SignedOut(CloudSettings cloud, string message)
    {
        Log.Warning($"Cloud: signed out ({message})");
        Forget(cloud);
        SetStatus(new CloudStatus(CloudState.SignedOut, message));
    }

    private static void Forget(CloudSettings cloud)
    {
        if (cloud.ServerUrl.Length > 0)
        {
            TokenStore.Delete(AccountKey(cloud.ServerUrl, cloud.UserName));
        }

        SettingsService.UpdateCloud(current => new CloudSettings { ServerUrl = current.ServerUrl, UserName = current.UserName, DeviceName = current.DeviceName });
    }

    private static string CheckDeviceName(string deviceName)
    {
        deviceName = string.IsNullOrWhiteSpace(deviceName) ? AppInfo.MachineName : deviceName.Trim();
        if (deviceName.Length > CloudContract.MaxDeviceNameLength)
        {
            throw new BusinessException($"Der Gerätename ist zu lang (höchstens {CloudContract.MaxDeviceNameLength} Zeichen).", nameof(CloudSettings.DeviceName));
        }

        return deviceName;
    }

    /// <summary>A failed sign-in for the person: the message of MatCloud at its field, or "not reachable" at the address.</summary>
    private static BusinessException SignInFailed(Exception exception, string url) => exception is CloudException cloudException
        ? new BusinessException(cloudException.Message, FieldOf(cloudException.Field))
        : new BusinessException($"Die MatCloud ist unter {url} nicht erreichbar.", nameof(CloudSettings.ServerUrl));

    /// <summary>A field MatCloud names ("deviceName") as the form here names it ("DeviceName"); the app itself has no field.</summary>
    private static string? FieldOf(string? field) =>
        string.IsNullOrEmpty(field) || field == "app" ? null : char.ToUpperInvariant(field[0]) + field[1..];

    private static bool IsCloudProblem(Exception exception) =>
        exception is CloudException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException;

    private static MatCloudClient Client(string url, string? token) => new(url, token, UserAgent, HandlerForTests?.Invoke());

    private static string? ReadToken(CloudSettings cloud) =>
        cloud.IsConnected ? TokenStore.Read(AccountKey(cloud.ServerUrl, cloud.UserName), cloud.ProtectedToken) : null;

    private static string AccountKey(string serverUrl, string userName) => $"{userName}@{serverUrl}";

    private static void SetStatus(CloudStatus status)
    {
        if (status == _status)
        {
            return;
        }

        _status = status;
        StatusChanged?.Invoke(null, status);
    }

    /// <summary>Only what a User-Agent header allows (letters, digits, '.', '-', '_').</summary>
    private static string Token(string text)
    {
        var token = new string(text.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-').ToArray()).Trim('-');
        return token.Length > 0 ? token : "unknown";
    }
}
