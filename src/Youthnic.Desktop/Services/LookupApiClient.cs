using System.Net.Http;
using System.Text.Json;
using Youthnic.Desktop.Models;

namespace Youthnic.Desktop.Services;

/// <summary>
/// Fast-fail JSON client for the lookup API. Singleton HttpClient backed by a
/// handler with a pooled connection lifetime of 2 minutes; the instance is recreated
/// only if the configured base URL changes. 2.5 s timeout, zero retries.
/// </summary>
public sealed class LookupApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private HttpClient? _client;
    private string? _clientBaseUrl;

    /// <summary>Request timeout for API calls (defaults to 8.0 seconds).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(8.0);

    private HttpClient GetClient(string lookupApiUrl)
    {
        var baseUrl = NormalizeBase(lookupApiUrl);
        lock (_gate)
        {
            if (_client is not null && string.Equals(_clientBaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase))
                return _client;

            _client?.Dispose();
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectTimeout = TimeSpan.FromSeconds(3)
            };
            _client = new HttpClient(handler) { Timeout = Timeout };
            _clientBaseUrl = baseUrl;
            return _client;
        }
    }

    /// <summary>Queries the remote lookup service for SKU and line item details given a tracking ID.</summary>
    /// <param name="lookupApiUrl">Base URL of the lookup service endpoint.</param>
    /// <param name="marketplace">Marketplace identifier (e.g. "flipkart", "myntra").</param>
    /// <param name="awb">Airway bill / tracking number to lookup.</param>
    /// <param name="seller">
    /// Optional seller-account filter (e.g. "ETHNIC JUNCTION - Flipkart") configured per
    /// marketplace in the setup wizard; disambiguates duplicate tracking IDs across accounts.
    /// </param>
    /// <param name="apiKey">
    /// Optional shared secret sent as the X-Api-Key header on every request. Required when
    /// the hosted lookup API has LOOKUP_API_KEY configured; ignored for the open loopback server.
    /// </param>
    public async Task<LookupResponse?> LookupAsync(
        string lookupApiUrl, string marketplace, string awb, string? seller, string? apiKey, CancellationToken ct)
    {
        var client = GetClient(lookupApiUrl);
        var url = $"{_clientBaseUrl}/api/lookup?marketplace={Uri.EscapeDataString(marketplace)}" +
                  $"&trackingId={Uri.EscapeDataString(awb)}";
        if (!string.IsNullOrWhiteSpace(seller))
            url += $"&seller={Uri.EscapeDataString(seller.Trim())}";

        HttpResponseMessage? response = null;
        try
        {
            using var request = BuildRequest(HttpMethod.Get, url, apiKey);
            response = await client.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (IsTransient(ex) && !ct.IsCancellationRequested)
        {
            var delayMs = 250;
            if (response?.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            {
                delayMs = Math.Min((int)delta.TotalMilliseconds, 5000);
            }
            response?.Dispose();
            response = null;
            // Fast retry on transient warehouse Wi-Fi glitch / connection reset
            AppLog.Debug($"LookupApiClient: transient network blip for '{awb}', retrying in {delayMs}ms: {ex.Message}");
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
            using var retryRequest = BuildRequest(HttpMethod.Get, url, apiKey);
            response = await client.SendAsync(retryRequest, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }

        using (response)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<LookupResponse>(stream, JsonOptions, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Best-effort GET {base}/health used by the setup wizard's connection test.</summary>
    public async Task<bool> TestHealthAsync(string lookupApiUrl, string? apiKey, CancellationToken ct)
    {
        try
        {
            var client = GetClient(lookupApiUrl);
            using var request = BuildRequest(HttpMethod.Get, $"{_clientBaseUrl}/health", apiKey);
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string url, string? apiKey)
    {
        var request = new HttpRequestMessage(method, url);
        // X-Api-Key is only sent when configured; a blank key must not produce an empty header.
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey.Trim());
        return request;
    }

    private static string NormalizeBase(string lookupApiUrl)
    {
        var trimmed = string.IsNullOrWhiteSpace(lookupApiUrl)
            ? DeskOptions.DefaultLookupApiUrl
            : lookupApiUrl.Trim();
        return trimmed.TrimEnd('/');
    }

    /// <summary>
    /// Determines whether an exception represents a transient failure eligible for fast retry.
    /// Retries connection drops, DNS failures, timeouts, HTTP 408 (Request Timeout),
    /// HTTP 429 (Too Many Requests), and HTTP 5xx server errors.
    /// Permanent client errors (400, 401, 403, 404) are never retried.
    /// </summary>
    private static bool IsTransient(Exception ex)
    {
        if (ex is HttpRequestException hex)
        {
            if (hex.StatusCode.HasValue)
            {
                var code = (int)hex.StatusCode.Value;
                return code == 408 || code == 429 || code >= 500;
            }
            // Network failures, DNS errors, socket resets have no status code -> transient
            return true;
        }
        return ex is TimeoutException or TaskCanceledException;
    }

    /// <inheritdoc/>
    public void Dispose() => _client?.Dispose();
}
