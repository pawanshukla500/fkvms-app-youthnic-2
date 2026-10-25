namespace Youthnic.LookupApi.Options;

public sealed record LookupApiOptions
{
    public const int DefaultQueryTimeoutMs = 2000;
    public const int DefaultCacheSeconds = 180;
    public const int DefaultNegativeCacheSeconds = 25;
    public const int DefaultMinPoolSize = 5;
    public const int DefaultMaxPoolSize = 40;
    public const string DefaultApiUrl = "http://127.0.0.1:5080";
    public const int CacheSizeLimit = 2000;

    /// <summary>Full Npgsql connection string (may be a postgres:// URI). Never log or return this.</summary>
    public string DatabaseUrl { get; init; } = string.Empty;

    /// <summary>When true, the app refuses to start unless read-only operation is verified at startup.</summary>
    public bool ReadOnly { get; init; } = true;

    public int QueryTimeoutMs { get; init; } = DefaultQueryTimeoutMs;

    public int CacheSeconds { get; init; } = DefaultCacheSeconds;

    public int NegativeCacheSeconds { get; init; } = DefaultNegativeCacheSeconds;

    public int MinPoolSize { get; init; } = DefaultMinPoolSize;

    public int MaxPoolSize { get; init; } = DefaultMaxPoolSize;

    /// <summary>Kestrel bind URL. Defaults to loopback-only.</summary>
    public string ApiUrl { get; init; } = DefaultApiUrl;

    /// <summary>API key required for /api/* over non-loopback connections (LOOKUP_API_KEY). Never log or return this.</summary>
    public string ApiKey { get; init; } = string.Empty;

    public ForwardScanMapping Mapping { get; init; } = new();

    public static LookupApiOptions FromEnvironment(Func<string, string?>? get = null)
    {
        get ??= Environment.GetEnvironmentVariable;
        return new LookupApiOptions
        {
            DatabaseUrl = (get("VMS_DATABASE_URL") ?? string.Empty).Trim(),
            ReadOnly = ParseBool(get("VMS_DATABASE_READ_ONLY"), defaultValue: true),
            QueryTimeoutMs = ParseInt(get("VMS_DB_QUERY_TIMEOUT_MS"), DefaultQueryTimeoutMs, min: 100, max: 30000),
            CacheSeconds = ParseInt(get("VMS_LOOKUP_CACHE_SECONDS"), DefaultCacheSeconds, min: 1, max: 3600),
            NegativeCacheSeconds = ParseInt(get("VMS_LOOKUP_NEGATIVE_CACHE_SECONDS"), DefaultNegativeCacheSeconds, min: 5, max: 300),
            MinPoolSize = ParseInt(get("VMS_DB_MIN_POOL_SIZE"), DefaultMinPoolSize, min: 2, max: 50),
            MaxPoolSize = ParseInt(get("VMS_DB_MAX_POOL_SIZE"), DefaultMaxPoolSize, min: 10, max: 100),
            ApiUrl = (get("LOOKUP_API_URL") ?? string.Empty).Trim() is { Length: > 0 } url ? url : DefaultApiUrl,
            ApiKey = (get("LOOKUP_API_KEY") ?? string.Empty).Trim(),
            Mapping = ForwardScanMapping.FromEnvironment(get),
        };
    }

    private static bool ParseBool(string? value, bool defaultValue)
        => value is null or "" ? defaultValue
         : bool.TryParse(value, out var parsed) ? parsed
         : defaultValue;

    private static int ParseInt(string? value, int defaultValue, int min, int max)
        => value is null or "" ? defaultValue
         : int.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max)
         : defaultValue;
}
