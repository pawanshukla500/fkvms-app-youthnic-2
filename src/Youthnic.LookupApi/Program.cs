using System.Net;
using Npgsql;
using Youthnic.LookupApi;
using Youthnic.LookupApi.Logging;
using Youthnic.LookupApi.Models;
using Youthnic.LookupApi.Options;
using Youthnic.LookupApi.Services;

// Dev convenience: load repo-root .env if present (never overrides real environment variables).
var envCount = EnvFileLoader.Load();

var options = LookupApiOptions.FromEnvironment();
var connectionString = NpgsqlConnectionStringFactory.Build(options.DatabaseUrl, options.QueryTimeoutMs);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
builder.WebHost.UseUrls(options.ApiUrl);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddProvider(new BoundedFileLoggerProvider());
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning); // minimal request logging
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<NpgsqlDataSource>(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
builder.Services.AddMemoryCache(cache => cache.SizeLimit = LookupApiOptions.CacheSizeLimit);
builder.Services.AddSingleton<MappingRuntimeState>();
builder.Services.AddSingleton<LookupCache>();
builder.Services.AddSingleton<LookupService>();
builder.Services.AddSingleton<DatabasePinger>();
builder.Services.AddSingleton<SchemaDiscoveryService>();
builder.Services.AddSingleton<InspectService>();

var app = builder.Build();
var logger = app.Logger;

app.UseDefaultFiles();
app.UseStaticFiles();

// --- API-key gate (LOOKUP_API_KEY): protects /api/* on non-loopback connections ---
// Note: /api/track is public for the customer portal (protected by rate limiting)
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") &&
        !context.Request.Path.StartsWithSegments("/api/track"))
    {
        var isLoopback = IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? IPAddress.None);
        var verdict = ApiKeyGate.Evaluate(options.ApiKey, context.Request.Headers[ApiKeyGate.HeaderName], isLoopback);
        if (verdict == ApiKeyVerdict.Challenge)
        {
            // Never echo or log the presented key.
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { status = "unauthorized", detail = $"valid {ApiKeyGate.HeaderName} header required" });
            return;
        }
    }
    await next();
});

if (envCount > 0)
    logger.LogDebug("Loaded {Count} variables from .env (existing environment always wins)", envCount);

if (!IsLoopback(options.ApiUrl))
    logger.LogWarning("LOOKUP_API_URL {Url} is not loopback — the API exposes database lookups on a non-loopback interface; ensure this is intentional", options.ApiUrl);

// API-key gate for public deployments. /health stays open; loopback callers are exempt.
// Deliberately uses Connection.RemoteIpAddress only (never forwarded headers) so a remote
// client cannot spoof X-Forwarded-For: 127.0.0.1 to bypass the key.
if (!string.IsNullOrEmpty(options.ApiKey))
    logger.LogInformation("API-key auth enabled: {KeyDesc}", ApiKeyGate.DescribeKey(options.ApiKey));
else
    logger.LogWarning("LOOKUP_API_KEY not set — /api/lookup and /api/inspect are open to any caller that can reach this server");

// --- Startup read-only verification (defense in depth layer 3) ---
var databaseReachable = true;
if (options.ReadOnly)
{
    ReadOnlyProbeOutcome? outcome = null;
    try
    {
        outcome = await new ReadOnlyProbeRunner(
            app.Services.GetRequiredService<NpgsqlDataSource>(), logger).RunAsync();
    }
    catch (Exception ex)
    {
        // Unreachable database is an availability problem, not a security
        // misconfiguration: keep serving so /health reports it and the
        // desktop panel degrades gracefully instead of hanging.
        databaseReachable = false;
        logger.LogError("Read-only verification skipped: database unreachable ({Message}). Starting in degraded mode.", ex.Message);
    }

    if (outcome is not null)
    {
        if (!ReadOnlyProbeDecision.ShouldStart(readOnlyRequired: true, outcome))
        {
            logger.LogError("Read-only operation could not be verified ({Detail}). Refusing to start because VMS_DATABASE_READ_ONLY=true.", outcome.Detail);
            return 1;
        }
        logger.LogInformation("Read-only operation verified: {Detail}", outcome.Detail);
    }
}
else
{
    logger.LogWarning("VMS_DATABASE_READ_ONLY=false — read-only verification skipped; the API may run with write-capable credentials.");
}

// --- Startup schema discovery + mapping resolution (never guesses) ---
var mappingState = app.Services.GetRequiredService<MappingRuntimeState>();
if (databaseReachable)
{
    await new SchemaDiscoveryService(
        app.Services.GetRequiredService<NpgsqlDataSource>(),
        app.Services.GetRequiredService<ILogger<SchemaDiscoveryService>>()).DiscoverAndResolveAsync(options, mappingState);
}
else
{
    mappingState.SetPending("database unavailable — schema discovery deferred");
}

if (mappingState.IsReady)
    logger.LogInformation("Lookup mapping ready ({Source})", mappingState.Source);
else
    logger.LogWarning("Mapping pending: {Detail}. /api/lookup will return 503 mapping_pending until FS_* mapping variables are provided.", mappingState.PendingDetail);

// --- Endpoints ---
app.MapGet("/health", async Task<IResult> (DatabasePinger pinger, MappingRuntimeState state, LookupApiOptions opts, CancellationToken ct) =>
{
    var database = await pinger.IsAvailableAsync(ct) ? "connected" : "unavailable";
    IResult result = Results.Json(new HealthResponse
    {
        Status = "ok",
        Database = database,
        Mapping = state.IsReady ? "ready" : "pending",
        ReadOnly = opts.ReadOnly,
    });
    return result;
});

app.MapGet("/api/lookup", async Task<IResult> (
    string? marketplace,
    string? trackingId,
    string? seller,
    LookupService lookup,
    MappingRuntimeState state,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var log = loggerFactory.CreateLogger("LookupApi.Lookup");

    var mp = (marketplace ?? string.Empty).Trim().ToLowerInvariant();
    if (mp.Length == 0 || mp.Length > 32)
        return Results.Json(new LookupResponse { Status = "invalid_request", Detail = "marketplace is required (max 32 chars)" }, statusCode: 400);

    var tid = (trackingId ?? string.Empty).Trim();
    if (tid.Length == 0 || tid.Length > 64)
        return Results.Json(new LookupResponse { Status = "invalid_request", Detail = "trackingId is required (max 64 chars)" }, statusCode: 400);
    tid = tid.ToUpperInvariant();

    if (!state.IsReady || state.Mapping is null)
    {
        log.LogDebug("lookup rejected: mapping pending for {Marketplace}/{TrackingId}", mp, tid);
        return Results.Json(
            new LookupResponse { Status = "mapping_pending", Detail = state.PendingDetail },
            statusCode: 503);
    }

    var result = await lookup.LookupAsync(state.Mapping, mp, tid, seller, ct);

    // ok / not_found / lookup_error all return 200; the desktop client distinguishes by "status".
    log.LogDebug("lookup {Marketplace}/{TrackingId}/{Seller} -> {Status}", mp, tid, seller ?? "-", result.Status);
    return Results.Json(result);
});

// --- Diagnostics: schema, seller accounts, sample tracking→SKU→qty→image rows, and
// image-URL fetch timing. Answers "is the DB configured right and how fast do images
// load" without exposing any credential. When LOOKUP_API_KEY is configured, the key gate
// above controls access (packing PCs over the internet use their key); without a key it
// stays loopback-only so it can never leak to the open internet.
app.MapGet("/api/inspect", async Task<IResult> (HttpContext http, InspectService inspect, LookupApiOptions opts, CancellationToken ct) =>
{
    if (string.IsNullOrEmpty(opts.ApiKey) &&
        !IPAddress.IsLoopback(http.Connection.RemoteIpAddress ?? IPAddress.None))
        return Results.Json(new { status = "forbidden", detail = "inspect is loopback-only" }, statusCode: 403);
    return Results.Json(await inspect.RunAsync(ct));
});

// --- Public Customer Tracking endpoint (rate-limited, no secret key required) ---
var ipRateLimits = new System.Collections.Concurrent.ConcurrentDictionary<string, (int count, DateTime reset)>();

app.MapGet("/api/track", async Task<IResult> (
    HttpContext http,
    string? marketplace,
    string? trackingId,
    string? seller,
    LookupService lookup,
    MappingRuntimeState state,
    ILoggerFactory loggerFactory,
    CancellationToken ct) =>
{
    var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var now = DateTime.UtcNow;
    var rate = ipRateLimits.AddOrUpdate(ip,
        _ => (1, now.AddMinutes(1)),
        (_, old) => old.reset < now ? (1, now.AddMinutes(1)) : (old.count + 1, old.reset));

    if (rate.count > 60)
        return Results.Json(new { status = "rate_limited", detail = "Too many tracking requests. Please try again shortly." }, statusCode: 429);

    var mp = (marketplace ?? "flipkart").Trim().ToLowerInvariant();
    if (mp.Length == 0 || mp.Length > 32)
        return Results.Json(new LookupResponse { Status = "invalid_request", Detail = "marketplace is required (max 32 chars)" }, statusCode: 400);

    var tid = (trackingId ?? string.Empty).Trim().ToUpperInvariant();
    if (tid.Length == 0 || tid.Length > 64)
        return Results.Json(new LookupResponse { Status = "invalid_request", Detail = "trackingId is required (max 64 chars)" }, statusCode: 400);

    if (!state.IsReady || state.Mapping is null)
    {
        return Results.Json(
            new LookupResponse { Status = "mapping_pending", Detail = state.PendingDetail },
            statusCode: 503);
    }

    var result = await lookup.LookupAsync(state.Mapping, mp, tid, seller, ct);
    return Results.Json(result);
});

// --- Portal route fallback ---
app.MapFallbackToFile("index.html");

await app.RunAsync();
return 0;

static bool IsLoopback(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
    if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
    return IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip);
}
