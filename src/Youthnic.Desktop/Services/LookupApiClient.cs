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

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(8.0);

    private HttpClient GetClient(string lookupApiUrl)
    {
        var baseUrl = NormalizeBase(lookupApiUrl);
        lock (_gate)
        {
            if (_client is not null && string.Equals(_clientBaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase))
                return _client;

            _client?.Dispose();
            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
            _client = new HttpClient(handler) { Timeout = Timeout };
            _clientBaseUrl = baseUrl;
            return _client;
        }
    }

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
        catch (HttpRequestException ex) when (!ct.IsCancellationRequested)
        {
            // Fast retry on transient warehouse Wi-Fi glitch / connection reset
            AppLog.Debug($"LookupApiClient: transient network blip for '{awb}', retrying: {ex.Message}");
            await Task.Delay(250, ct).ConfigureAwait(false);
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

    public void Dispose() => _client?.Dispose();
}
