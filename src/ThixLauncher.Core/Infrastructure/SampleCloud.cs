using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MatCloud.Contract;

namespace ThixLauncher.Core.Infrastructure;

/// <summary>
/// MatCloud in memory, for the tests, the self test and the snapshots (<see cref="Business.CloudService.HandlerForTests"/>): the sign-in with a
/// password (anna / anna-password) and in the browser (<see cref="ApproveLink"/> plays the person in the browser), the account, the sign-out and the
/// hand-over of the launcher to an app (<see cref="CloudContract.DeviceHandoff"/>, fetched with the poll like MatCloud does). Never used by the
/// real app.
/// </summary>
internal sealed class SampleCloud : HttpMessageHandler
{
    public const string Address = "https://cloud.example.com";
    public const string UserName = "anna";
    public const string Password = "anna-password";
    public const string DisplayName = "Anna Beispiel";
    public const string Code = "ACDE-FGHJ";

    private readonly object _gate = new();
    private readonly Dictionary<string, (string App, string DeviceName)> _devices = new();
    private readonly Dictionary<string, (string App, string DeviceName, bool Approved)> _links = new();

    /// <summary>False: every request fails as if the cloud were not reachable.</summary>
    public bool IsReachable { get; set; } = true;

    /// <summary>The apps an administrator switched off: no sign-in, no hand-over.</summary>
    public HashSet<string> SwitchedOff { get; } = [];

    /// <summary>The apps the launcher handed its sign-in to, in order.</summary>
    public List<string> HandedOver { get; } = [];

    /// <summary>The devices signed in now (app, device name).</summary>
    public IReadOnlyList<(string App, string DeviceName)> Devices
    {
        get
        {
            lock (_gate)
            {
                return _devices.Values.ToList();
            }
        }
    }

    /// <summary>The person confirms every sign-in waiting in the browser.</summary>
    public void ApproveLink()
    {
        lock (_gate)
        {
            foreach (var token in _links.Keys.ToList())
            {
                _links[token] = _links[token] with { Approved = true };
            }
        }
    }

    /// <summary>The person removes every device of <paramref name="app"/> in the browser (its token answers 401 from now on).</summary>
    public void RemoveDevices(string app)
    {
        lock (_gate)
        {
            foreach (var token in _devices.Where(d => d.Value.App == app).Select(d => d.Key).ToList())
            {
                _devices.Remove(token);
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (!IsReachable)
        {
            throw new HttpRequestException("No such host is known. (cloud.example.com:443)");
        }

        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_gate)
        {
            return path switch
            {
                CloudContract.DeviceLogin => Login(Read<DeviceLoginRequest>(body)),
                CloudContract.DeviceLink => StartLink(Read<DeviceLinkRequest>(body)),
                CloudContract.DeviceLinkPoll => Poll(Read<DeviceLinkPollRequest>(body)),
                CloudContract.DeviceLogout => Device(request) is { } token && _devices.Remove(token) ? new HttpResponseMessage(HttpStatusCode.NoContent) : Unauthorized(),
                CloudContract.Account => Device(request) is { } token ? Json(Account(_devices[token])) : Unauthorized(),
                CloudContract.DeviceHandoff => HandOff(Device(request), Read<DeviceHandoffRequest>(body)),
                _ => Error(HttpStatusCode.NotFound, "Nicht gefunden."),
            };
        }
    }

    private HttpResponseMessage Login(DeviceLoginRequest request)
    {
        if (!CloudApps.IsKnown(request.App) || SwitchedOff.Contains(request.App))
        {
            return Error(HttpStatusCode.Forbidden, $"{CloudApps.NameOf(request.App)} ist in dieser MatCloud ausgeschaltet.", "app");
        }

        return !string.Equals(request.UserName, UserName, StringComparison.OrdinalIgnoreCase) || request.Password != Password
            ? Error(HttpStatusCode.Unauthorized, "Benutzername oder Passwort stimmen nicht.", "password")
            : Json(NewDevice(request.App, request.DeviceName));
    }

    private HttpResponseMessage StartLink(DeviceLinkRequest request)
    {
        var token = Guid.NewGuid().ToString("D");
        _links[token] = (request.App, request.DeviceName, false);
        return Json(new DeviceLinkStart(Code, $"{Address}{CloudContract.ConnectPage}?code={Code}", token, 600, 1));
    }

    private HttpResponseMessage Poll(DeviceLinkPollRequest request)
    {
        if (!_links.TryGetValue(request.PollToken, out var link))
        {
            return Json(new DeviceLinkPollResponse(DeviceLinkState.Expired));
        }

        if (!link.Approved)
        {
            return Json(new DeviceLinkPollResponse(DeviceLinkState.Pending));
        }

        _links.Remove(request.PollToken);
        return Json(new DeviceLinkPollResponse(DeviceLinkState.Approved, NewDevice(link.App, link.DeviceName)));
    }

    private HttpResponseMessage HandOff(string? token, DeviceHandoffRequest request)
    {
        if (token is null)
        {
            return Unauthorized();
        }

        var launcher = _devices[token];
        if (launcher.App != CloudApps.Launcher)
        {
            return Error(HttpStatusCode.Forbidden, "Nur ThixLauncher gibt seine Anmeldung an andere Apps weiter.");
        }

        if (!CloudApps.IsKnown(request.App) || request.App == CloudApps.Launcher || SwitchedOff.Contains(request.App))
        {
            return Error(HttpStatusCode.BadRequest, $"{CloudApps.NameOf(request.App)} ist in dieser MatCloud ausgeschaltet.", "app");
        }

        var pollToken = Guid.NewGuid().ToString("D");
        _links[pollToken] = (request.App, launcher.DeviceName, true);
        HandedOver.Add(request.App);
        return Json(new DeviceHandoffStart(pollToken, 86400));
    }

    private DeviceLoginResponse NewDevice(string app, string deviceName)
    {
        var token = Guid.NewGuid().ToString("D");
        _devices[token] = (app, deviceName);
        return new DeviceLoginResponse(token, _devices.Count + 100, Account((app, deviceName)));
    }

    private static AccountInfo Account((string App, string DeviceName) device) => new(UserName, DisplayName, device.DeviceName, device.App, "sample");

    private string? Device(HttpRequestMessage request)
    {
        var token = request.Headers.Authorization?.Parameter;
        return token is not null && _devices.ContainsKey(token) ? token : null;
    }

    private static T Read<T>(string? body) => JsonSerializer.Deserialize<T>(body ?? "null", CloudContract.Json)!;

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: CloudContract.Json) };

    private static HttpResponseMessage Unauthorized() => Error(HttpStatusCode.Unauthorized, "Dieses Gerät ist nicht (mehr) angemeldet. Bitte in der App erneut anmelden.");

    private static HttpResponseMessage Error(HttpStatusCode status, string message, string? field = null) =>
        new(status) { Content = JsonContent.Create(new ApiError(message, field), options: CloudContract.Json) };
}
