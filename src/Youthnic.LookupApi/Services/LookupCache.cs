using Microsoft.Extensions.Caching.Memory;
using Youthnic.LookupApi.Models;
using Youthnic.LookupApi.Options;

namespace Youthnic.LookupApi.Services;

/// <summary>
/// Size-limited in-memory cache (IMemoryCache, sliding expiration, 500 entries) for
/// lookup results. The cache key always includes marketplace + trackingId. Successful
/// and not_found results are cached; errors are never cached. The sliding window is
/// tracked explicitly with an injectable clock so expiry is deterministic and testable.
/// </summary>
public sealed class LookupCache
{
    private sealed class Entry
    {
        public required LookupResponse Response { get; init; }
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset LastAccessUtc { get; set; }
        public TimeSpan Ttl { get; init; }
        public bool IsSliding { get; init; } = true;
    }

    private readonly MemoryCache _cache;
    private readonly int _ttlSeconds;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Initializes a new instance of <see cref="LookupCache"/>.</summary>
    public LookupCache(IMemoryCache cache, LookupApiOptions options, Func<DateTimeOffset>? clock = null)
    {
        _cache = (MemoryCache)cache;
        _ttlSeconds = Math.Max(1, options.CacheSeconds);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Builds a composite cache key for marketplace and tracking ID.</summary>
    public static string KeyFor(string marketplace, string trackingId) =>
        $"{marketplace.Trim().ToLowerInvariant()}:{trackingId.Trim().ToUpperInvariant()}";

    /// <summary>Builds a composite cache key for marketplace, tracking ID, and seller filter.</summary>
    public static string KeyFor(string marketplace, string trackingId, string? seller)
        => string.IsNullOrWhiteSpace(seller)
            ? KeyFor(marketplace, trackingId)
            : $"{marketplace.Trim().ToLowerInvariant()}:{trackingId.Trim().ToUpperInvariant()}@{seller.Trim().ToUpperInvariant()}";

    /// <summary>Returns the current number of cached entries.</summary>
    public int Count => _cache.Count;

    /// <summary>Attempts to retrieve a cached lookup response by key.</summary>
    public bool TryGet(string key, out LookupResponse response)
    {
        if (_cache.TryGetValue(key, out Entry? entry) && entry is not null)
        {
            var elapsed = entry.IsSliding
                ? (_clock() - entry.LastAccessUtc)
                : (_clock() - entry.CreatedAtUtc);

            if (elapsed > entry.Ttl)
            {
                _cache.Remove(key);
                response = null!;
                return false;
            }

            if (entry.IsSliding)
            {
                var now = _clock();
                lock (entry)
                {
                    if (now > entry.LastAccessUtc)
                        entry.LastAccessUtc = now;
                }
            }
            response = entry.Response;
            return true;
        }
        response = null!;
        return false;
    }

    /// <summary>Stores a lookup response in the cache with sliding or absolute expiration.</summary>
    public void Set(string key, LookupResponse response, TimeSpan? customTtl = null, bool sliding = true)
    {
        var now = _clock();
        var ttl = customTtl ?? TimeSpan.FromSeconds(_ttlSeconds);
        var entry = new Entry
        {
            Response = response,
            CreatedAtUtc = now,
            LastAccessUtc = now,
            Ttl = ttl,
            IsSliding = sliding
        };

        var memOptions = new MemoryCacheEntryOptions { Size = 1 };
        if (sliding)
            memOptions.SlidingExpiration = ttl;
        else
            memOptions.AbsoluteExpirationRelativeToNow = ttl;

        _cache.Set(key, entry, memOptions);
    }
}
