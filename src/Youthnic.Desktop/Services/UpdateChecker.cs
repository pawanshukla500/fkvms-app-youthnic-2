using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace Youthnic.Desktop.Services;

/// <summary>
/// Background check for new releases of the desk app on GitHub
/// (repos/pawanshukla500/fkvms-app-youthnic-2). Checks on start, every 4 hours while
/// running, and on demand. All network work is fire-and-forget off the UI thread and
/// every event is marshalled onto the supplied Dispatcher; a failed, slow or absent
/// network never surfaces to the operator (log only) and never blocks startup.
///
/// TRUST MODEL: the update asset is downloaded over HTTPS directly from the official
/// GitHub release of the configured repo (github.com redirects to their CDN). No
/// additional signature/hash verification is performed.
/// TODO: verify an Authenticode signature or a signed SHA-256 manifest before running
/// the downloaded installer, so a compromised release cannot be installed silently.
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    public const string SetupAssetName = "YouthnicPackingDeskSetup.exe";

    private static readonly Uri LatestReleaseUri =
        new("https://api.github.com/repos/pawanshukla500/fkvms-app-youthnic-2/releases/latest");
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);
    private const int RequestTimeoutSeconds = 8;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dispatcher _dispatcher;
    private readonly HttpClient _http;
    private readonly DispatcherTimer _timer;
    private ReleaseInfo? _pending;

    public UpdateChecker(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds) };
        // GitHub API rejects requests without a User-Agent.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("YouthnicPackingDesk-Updater");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += async (_, _) => await CheckNowAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>The currently known pending update, or null when none was announced.</summary>
    public ReleaseInfo? Pending => _pending;

    /// <summary>Raised on the UI dispatcher when a newer release exists. Fired at most
    /// once per discovered version; re-announced only after an explicit CheckNowAsync.</summary>
    public event Action<ReleaseInfo>? UpdateAvailable;

    /// <summary>Starts the periodic check. The first check runs in the background shortly
    /// after startup; it is deliberately not awaited so app startup is never blocked.</summary>
    public void Start()
    {
        _timer.Start();
        _ = Task.Run(() => CheckNowAsync(CancellationToken.None));
    }

    public void Stop() => _timer.Stop();

    /// <summary>On-demand check (manual "check for updates"). Never throws; failures stay in the log.</summary>
    public async Task CheckNowAsync(CancellationToken ct)
    {
        try
        {
            var release = await FetchLatestAsync(ct).ConfigureAwait(false);
            if (release is null) return;

            var current = CurrentVersion();
            if (!IsNewerVersion(current, release.Version)) return;

            _pending = release;
            AppLog.Info($"UpdateChecker: update available — v{release.Version} (running v{current})");
            _ = _dispatcher.BeginInvoke(() => UpdateAvailable?.Invoke(release));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Explicit cancellation — quiet.
        }
        catch (Exception ex)
        {
            // Offline / GitHub down / proxy blocked / malformed payload: silent by design.
            AppLog.Debug("UpdateChecker: check failed — " + ex.Message);
        }
    }

    /// <summary>
    /// Downloads the setup asset for the announced release to <paramref name="destinationPath"/>,
    /// streaming with progress (0..1 when the content length is known, else 0 until done).
    /// The progress callback may be invoked on a thread-pool thread — callers must marshal
    /// to the UI thread themselves if they touch WPF objects.
    /// TRUST MODEL: HTTPS from the official GitHub release (see class remarks); no extra
    /// signature check yet (TODO).
    /// </summary>
    public async Task DownloadAsync(
        ReleaseInfo release, string destinationPath, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var request = new HttpRequestMessage(HttpMethod.Get, release.SetupAssetUrl);
        request.Headers.UserAgent.ParseAdd("YouthnicPackingDesk-Updater");
        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var tempPath = destinationPath + ".part";
        try
        {
            await using (var output = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    readTotal += read;
                    if (totalBytes > 0)
                        progress?.Report((double)readTotal / totalBytes);
                }
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            progress?.Report(1.0);
        }
        catch
        {
            TryDeleteQuiet(tempPath);
            throw;
        }
    }

    /// <summary>Launches the downloaded Inno installer and exits this process so the
    /// installer can overwrite the running exe (install-over).</summary>
    public static void LaunchInstallerAndExit(string setupPath)
    {
        AppLog.Info("UpdateChecker: launching downloaded installer — " + setupPath);
        Process.Start(new ProcessStartInfo(setupPath) { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }

    public static string UpdatesDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Youthnic", "PackingDesk", "updates");

    public static string CurrentVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>True when <paramref name="candidate"/> is strictly newer than
    /// <paramref name="current"/> on major.minor.patch (pre-release/build suffixes ignored).</summary>
    public static bool IsNewerVersion(string current, string candidate)
    {
        if (!TryParseVersion(current, out var c) || !TryParseVersion(candidate, out var n)) return false;
        return n > c;
    }

    private async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(LatestReleaseUri, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            AppLog.Debug($"UpdateChecker: GitHub API returned {(int)response.StatusCode}");
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<GithubReleaseDto>(stream, JsonOptions, ct).ConfigureAwait(false);
        if (dto is null || string.IsNullOrWhiteSpace(dto.TagName)) return null;

        var version = dto.TagName.Trim().TrimStart('v', 'V');
        var asset = dto.Assets?.FirstOrDefault(a =>
            string.Equals(a.Name, SetupAssetName, StringComparison.OrdinalIgnoreCase));
        if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            AppLog.Debug($"UpdateChecker: latest release {dto.TagName} has no {SetupAssetName} asset");
            return null;
        }

        return new ReleaseInfo(
            Version: version,
            Name: dto.Name ?? dto.TagName,
            Notes: dto.Body ?? string.Empty,
            SetupAssetUrl: asset.BrowserDownloadUrl);
    }

    private static bool TryParseVersion(string raw, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var s = raw.Trim().TrimStart('v', 'V');
        var dash = s.IndexOf('-');
        if (dash >= 0) s = s[..dash]; // drop pre-release suffix
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus]; // drop build metadata

        if (!Version.TryParse(s, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }

    private static void TryDeleteQuiet(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    public void Dispose() => _http.Dispose();

    private sealed class GithubReleaseDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("body")]
        public string? Body { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("assets")]
        public GithubAssetDto[]? Assets { get; set; }
    }

    private sealed class GithubAssetDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}

/// <summary>A newer release discovered on GitHub.</summary>
public sealed record ReleaseInfo(
    string Version,
    string Name,
    string Notes,
    string SetupAssetUrl);
