using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace Youthnic.Desktop.Services;

/// <summary>
/// Background check for new releases of the desk app on GitHub
/// (repos/pawanshukla500/fkvms-app-youthnic-2). Checks on start, every 1 hour while
/// running, and on demand. Automatically pulls (downloads) new updates in background.
/// All network work is fire-and-forget off the UI thread and every event is marshalled
/// onto the supplied Dispatcher; a failed, slow or absent network never surfaces to the
/// operator (log only) and never blocks startup.
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    public const string SetupAssetName = "YouthnicPackingDeskSetup.exe";

    private static readonly Uri LatestReleaseUri =
        new("https://api.github.com/repos/pawanshukla500/fkvms-app-youthnic-2/releases/latest");
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private const int RequestTimeoutSeconds = 8;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dispatcher _dispatcher;
    private readonly HttpClient _http;
    private readonly DispatcherTimer _timer;
    private ReleaseInfo? _pending;
    private static readonly SemaphoreSlim DownloadLock = new(1, 1);
    private static Task? _activeDownloadTask;
    private static string? _activeDownloadVersion;

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

    /// <summary>Raised on the UI dispatcher when the installer for a newer release has finished
    /// downloading to disk and is verified ready for immediate installation.</summary>
    public event Action<ReleaseInfo, string>? UpdateReadyToInstall;

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

            // Auto-pull (download) the new installer in background so it is ready for instant install
            _ = Task.Run(async () =>
            {
                try
                {
                    var destination = Path.Combine(UpdatesDir, SetupAssetName);
                    if (File.Exists(destination))
                    {
                        _ = _dispatcher.BeginInvoke(() => UpdateReadyToInstall?.Invoke(release, destination));
                        return;
                    }

                    AppLog.Info($"UpdateChecker: auto-pulling update v{release.Version} to {destination}...");
                    await DownloadAsync(release, destination, null, CancellationToken.None).ConfigureAwait(false);
                    AppLog.Info($"UpdateChecker: auto-pull complete for v{release.Version}");
                    _ = _dispatcher.BeginInvoke(() => UpdateReadyToInstall?.Invoke(release, destination));
                }
                catch (Exception ex)
                {
                    AppLog.Debug($"UpdateChecker: auto-pull background download failed — {ex.Message}");
                }
            }, CancellationToken.None);
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
    /// </summary>
    public async Task DownloadAsync(
        ReleaseInfo release, string destinationPath, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        // Fast-path: if valid setup already downloaded and exists, reuse immediately without network collision
        try
        {
            if (File.Exists(destinationPath))
            {
                var info = new FileInfo(destinationPath);
                if (info.Length > 1024 * 1024)
                {
                    progress?.Report(1.0);
                    return;
                }
            }
        }
        catch { }

        Task downloadTask;
        await DownloadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Coalesce concurrent download requests (e.g. background auto-pull vs user click)
            if (_activeDownloadTask is not null && string.Equals(_activeDownloadVersion, release.Version, StringComparison.OrdinalIgnoreCase))
            {
                downloadTask = _activeDownloadTask;
            }
            else
            {
                _activeDownloadVersion = release.Version;
                downloadTask = _activeDownloadTask = PerformDownloadCoreAsync(release, destinationPath, progress, ct);
            }
        }
        finally
        {
            DownloadLock.Release();
        }

        await downloadTask.ConfigureAwait(false);
    }

    private async Task PerformDownloadCoreAsync(
        ReleaseInfo release, string destinationPath, IProgress<double>? progress, CancellationToken ct)
    {
        // Use unique part filename per download to eliminate any possibility of file-in-use collisions
        var tempPath = destinationPath + $".{release.Version}.{Guid.NewGuid():N}.part";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, release.SetupAssetUrl);
            request.Headers.UserAgent.ParseAdd("YouthnicPackingDesk-Updater");
            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

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
        finally
        {
            await DownloadLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (string.Equals(_activeDownloadVersion, release.Version, StringComparison.OrdinalIgnoreCase))
                {
                    _activeDownloadTask = null;
                    _activeDownloadVersion = null;
                }
            }
            finally
            {
                DownloadLock.Release();
            }
        }
    }

    /// <summary>Launches the downloaded Inno installer and exits this process so the
    /// installer can overwrite the running exe (install-over).</summary>
    public static void LaunchInstallerAndExit(string setupPath)
    {
        AppLog.Info("UpdateChecker: launching downloaded installer — " + setupPath);
        try
        {
            Process.Start(new ProcessStartInfo(setupPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error("UpdateChecker: failed to launch setup", ex);
        }
        System.Windows.Application.Current.Shutdown();
        Environment.Exit(0);
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
