using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Youthnic.Desktop.Models;

namespace Youthnic.Desktop.Services;

public enum LookupOutcome
{
    Ok,
    NotFound,
    WrongMarketplace,
    MappingPending,
    Error
}

public sealed record ScanLookupResult(
    long Seq,
    string Awb,
    LookupOutcome Outcome,
    IReadOnlyList<LookupItem> Items,
    string? Hint,
    string? DetectedMarketplace = null,
    string? Message = null);

/// <summary>
/// Coordinates lookup calls triggered by AWB scans. Every scan gets a monotonically
/// increasing sequence number and its own CancellationTokenSource; a newer scan
/// always cancels the older one so stale results can never overwrite the panel.
/// Identical AWB repeated within 2 s is ignored (scanner double-fire). All network
/// work runs on the thread-pool; the UI is only touched via the supplied Dispatcher.
/// </summary>
public sealed class LookupCoordinator
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(2);

    private readonly Dispatcher _dispatcher;
    private readonly LookupApiClient _apiClient;
    private readonly ImageCache _imageCache;
    private readonly Func<string> _lookupApiUrl;
    private readonly Func<string> _lookupApiKey;
    private readonly Func<(string Marketplace, string? Seller)> _scanContext;

    private readonly object _gate = new();
    private long _lastSeq;
    private CancellationTokenSource? _currentCts;
    private string _lastAwb = string.Empty;
    private DateTime _lastScanAtUtc;
    private bool _hasLastScan;

    public LookupCoordinator(
        Dispatcher dispatcher,
        LookupApiClient apiClient,
        ImageCache imageCache,
        Func<string> lookupApiUrl,
        Func<string> lookupApiKey,
        Func<(string Marketplace, string? Seller)> scanContext)
    {
        _dispatcher = dispatcher;
        _apiClient = apiClient;
        _imageCache = imageCache;
        _lookupApiUrl = lookupApiUrl;
        _lookupApiKey = lookupApiKey;
        _scanContext = scanContext;
    }

    /// <summary>Fired on the UI dispatcher for every completed (or failed) lookup.</summary>
    public event Action<ScanLookupResult>? LookupCompleted;

    /// <summary>
    /// Fired on the UI dispatcher when the first item's product image has finished
    /// downloading/decoding. Raised after LookupCompleted, never before; the panel
    /// swaps the "No image" placeholder for the bitmap. Only raised when a bitmap
    /// actually arrived — failures stay on the placeholder.
    /// </summary>
    public event Action<long, BitmapImage>? ImageReady;

    /// <summary>Fired on the UI dispatcher right before a new lookup starts (panel shows loading).</summary>
    public event Action<long, string>? LookupStarted;

    /// <summary>Called from the WebView2 WebMessageReceived handler on the UI thread.</summary>
    public void HandleScan(string awb)
    {
        var trimmed = (awb ?? string.Empty).Trim();
        if (trimmed.Length < 4) return;

        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (_hasLastScan &&
                string.Equals(_lastAwb, trimmed, StringComparison.Ordinal) &&
                now - _lastScanAtUtc < DuplicateWindow)
            {
                AppLog.Debug($"LookupCoordinator: duplicate scan '{trimmed}' within window, ignored");
                return;
            }

            _lastAwb = trimmed;
            _lastScanAtUtc = now;
            _hasLastScan = true;

            var seq = ++_lastSeq;
            _currentCts?.Cancel();
            _currentCts?.Dispose();
            _currentCts = new CancellationTokenSource();
            var token = _currentCts.Token;

            _dispatcher.BeginInvoke(() => LookupStarted?.Invoke(seq, trimmed));
            _ = Task.Run(() => RunLookupAsync(seq, trimmed, token), CancellationToken.None);
        }
    }

    /// <summary>Re-runs the most recent AWB (panel's manual Retry button).</summary>
    public void RetryLast()
    {
        string awb;
        lock (_gate)
        {
            if (!_hasLastScan) return;
            awb = _lastAwb;
            _lastScanAtUtc = DateTime.MinValue; // bypass the duplicate window
        }
        HandleScan(awb);
    }

    public void CancelPending()
    {
        lock (_gate)
        {
            _currentCts?.Cancel();
        }
    }

    private async Task RunLookupAsync(long seq, string awb, CancellationToken ct)
    {
        ScanLookupResult result;
        try
        {
            var (marketplace, seller) = _scanContext();
            var response = await _apiClient.LookupAsync(_lookupApiUrl(), marketplace, awb, seller, _lookupApiKey(), ct).ConfigureAwait(false);
            if (response is null)
            {
                result = new ScanLookupResult(seq, awb, LookupOutcome.Error, Array.Empty<LookupItem>(), null);
            }
            else
            {
                result = response.Status switch
                {
                    LookupStatus.Ok => new ScanLookupResult(
                        seq, awb, LookupOutcome.Ok,
                        response.Items ?? Array.Empty<LookupItem>(),
                        response.Hint),
                    LookupStatus.WrongMarketplace => new ScanLookupResult(
                        seq, awb, LookupOutcome.WrongMarketplace,
                        response.Items ?? Array.Empty<LookupItem>(),
                        response.Hint,
                        response.DetectedMarketplace,
                        response.Message),
                    LookupStatus.NotFound => new ScanLookupResult(
                        seq, awb, LookupOutcome.NotFound,
                        response.Items ?? Array.Empty<LookupItem>(),
                        response.Hint),
                    LookupStatus.MappingPending => new ScanLookupResult(
                        seq, awb, LookupOutcome.MappingPending,
                        response.Items ?? Array.Empty<LookupItem>(),
                        response.Hint),
                    _ => new ScanLookupResult(
                        seq, awb, LookupOutcome.Error,
                        response.Items ?? Array.Empty<LookupItem>(),
                        response.Hint),
                };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return; // superseded by a newer scan; the newer lookup owns the panel now
        }
        catch (Exception ex)
        {
            AppLog.Error($"LookupCoordinator: lookup failed for awb '{awb}'", ex);
            result = new ScanLookupResult(seq, awb, LookupOutcome.Error, Array.Empty<LookupItem>(), null);
        }

        // Guard against races: only the newest scan may publish.
        lock (_gate)
        {
            if (seq != _lastSeq) return;
        }

        // Publish names/qty/seller the moment the API responds — the loading spinner
        // resolves here, not when the (slow, remote-CDN) product image arrives.
        _ = _dispatcher.BeginInvoke(() => LookupCompleted?.Invoke(result));

        // Progressive image: kick off the download in the background. When the bitmap
        // is ready it is pushed via ImageReady with the same seq guard, so a stale
        // image can never overwrite a newer scan's panel.
        if (result.Outcome == LookupOutcome.Ok && result.Items.Count > 0)
        {
            var urls = result.Items[0].ImageUrls?.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (urls != null && urls.Count > 0)
            {
                _ = LoadMainImageAsync(seq, awb, urls, ct);
            }
        }
    }

    private async Task LoadMainImageAsync(long seq, string awb, IReadOnlyList<string> urls, CancellationToken ct)
    {
        BitmapImage? image = null;
        foreach (var url in urls)
        {
            try
            {
                image = await _imageCache.GetAsync(url, ct).ConfigureAwait(false);
                if (image != null) break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return; // superseded by a newer scan
            }
            catch (Exception ex)
            {
                AppLog.Debug($"LookupCoordinator: image load failed for awb '{awb}' ({url}): {ex.Message}");
            }
        }

        if (image is null) return; // all candidate URLs failed; placeholder stays

        lock (_gate)
        {
            if (seq != _lastSeq) return; // a newer scan owns the panel now
        }

        _ = _dispatcher.BeginInvoke(() => ImageReady?.Invoke(seq, image));
    }
}
