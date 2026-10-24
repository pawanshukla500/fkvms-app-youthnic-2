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
        public DateTimeOffset LastAccessUtc { get; set; }
    }

    private readonly MemoryCache _cache;
    private readonly int _ttlSeconds;
    private readonly Func<DateTimeOffset> _clock;

    public LookupCache(IMemoryCache cache, LookupApiOptions options, Func<DateTimeOffset>? clock = null)
    {
        _cache = (MemoryCache)cache;
        _ttlSeconds = Math.Max(1, options.CacheSeconds);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public static string KeyFor(string marketplace, string trackingId) => $"{marketplace}:{trackingId}";

    public static string KeyFor(string marketplace, string trackingId, string? seller)
        => seller is null ? KeyFor(marketplace, trackingId) : $"{marketplace}:{trackingId}@{seller}";

    public int Count => _cache.Count;

    public bool TryGet(string key, out LookupResponse response)
    {
        if (_cache.TryGetValue(key, out Entry? entry) && entry is not null)
        {
            if ((_clock() - entry.LastAccessUtc).TotalSeconds > _ttlSeconds)
            {
                _cache.Remove(key);
                response = null!;
                return false;
            }
            entry.LastAccessUtc = _clock(); // sliding
            response = entry.Response;
            return true;
        }
        response = null!;
        return false;
    }

    public void Set(string key, LookupResponse response)
    {
        _cache.Set(key, new Entry { Response = response, LastAccessUtc = _clock() },
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromSeconds(_ttlSeconds),
                Size = 1,
            });
    }
}
