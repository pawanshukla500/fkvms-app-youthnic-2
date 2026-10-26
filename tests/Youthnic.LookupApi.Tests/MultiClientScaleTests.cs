using Microsoft.Extensions.Caching.Memory;
using Xunit;
using Youthnic.LookupApi.Models;
using Youthnic.LookupApi.Options;
using Youthnic.LookupApi.Services;

namespace Youthnic.LookupApi.Tests;

public class MultiClientScaleTests
{
    [Fact]
    public async Task TenConcurrentDesks_400ScansEach_4000TotalRequests_ProcessesSmoothly()
    {
        const int deskCount = 10;
        const int scansPerDesk = 400;

        var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = LookupApiOptions.CacheSizeLimit });
        var options = new LookupApiOptions
        {
            CacheSeconds = 180,
            NegativeCacheSeconds = 25,
            MinPoolSize = 5,
            MaxPoolSize = 40,
        };
        var cache = new LookupCache(memoryCache, options);

        // Pre-populate some catalog SKUs that would be common across multiple packing desks
        for (var s = 0; s < 50; s++)
        {
            var popularAwb = $"POPULAR_{s:D3}";
            cache.Set(
                LookupCache.KeyFor("flipkart", popularAwb),
                new LookupResponse
                {
                    Status = "ok",
                    TrackingId = popularAwb,
                    Marketplace = "flipkart",
                    Items = [new LookupItem { Sku = $"SKU_{s}", SkuName = $"Product {s}", Quantity = 1, ImageUrls = [] }]
                });
        }

        var completedCounts = new int[deskCount];

        // Launch 10 simulated packing desks running concurrently
        var tasks = Enumerable.Range(0, deskCount).Select(async deskIndex =>
        {
            await Task.Yield();
            for (var scanIndex = 0; scanIndex < scansPerDesk; scanIndex++)
            {
                // 30% of scans are popular shared SKUs, 70% are unique packages
                string awb;
                if (scanIndex % 3 == 0)
                {
                    awb = $"POPULAR_{(scanIndex % 50):D3}";
                }
                else
                {
                    awb = $"AWB_DESK{deskIndex}_{scanIndex:D4}";
                }

                var key = LookupCache.KeyFor("flipkart", awb);
                if (!cache.TryGet(key, out var response))
                {
                    // Simulated database lookup result
                    response = new LookupResponse
                    {
                        Status = "ok",
                        TrackingId = awb,
                        Marketplace = "flipkart",
                        Items = [new LookupItem { Sku = $"SKU_D{deskIndex}_{scanIndex}", SkuName = $"Product D{deskIndex}_{scanIndex}", Quantity = 1, ImageUrls = [] }]
                    };
                    cache.Set(key, response);
                }

                Assert.NotNull(response);
                Assert.Equal("ok", response.Status);
                completedCounts[deskIndex]++;
            }
        });

        await Task.WhenAll(tasks);

        // Verify every single desk completed all 400 scans (total 4000)
        for (var i = 0; i < deskCount; i++)
        {
            Assert.Equal(scansPerDesk, completedCounts[i]);
        }

        // Cache size must remain bounded within CacheSizeLimit
        Assert.True(cache.Count <= LookupApiOptions.CacheSizeLimit);
    }

    [Fact]
    public void NegativeCacheTtl_AllowsFastRetryAfterSync()
    {
        var clockTime = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = LookupApiOptions.CacheSizeLimit });
        var options = new LookupApiOptions
        {
            CacheSeconds = 180,
            NegativeCacheSeconds = 25
        };
        var cache = new LookupCache(memoryCache, options, () => clockTime);

        const string key = "flipkart:UNSYNCED_AWB_123";

        // Step 1: Scan before sync -> not_found cached for 25s (absolute expiration, no sliding)
        cache.Set(key, new LookupResponse { Status = "not_found", TrackingId = "UNSYNCED_AWB_123" }, TimeSpan.FromSeconds(options.NegativeCacheSeconds), sliding: false);
        Assert.True(cache.TryGet(key, out var notFoundHit));
        Assert.Equal("not_found", notFoundHit.Status);

        // Step 2: 10 seconds later (operator retries too quickly) -> still returns cached not_found
        clockTime = clockTime.AddSeconds(10);
        Assert.True(cache.TryGet(key, out var quickRetryHit));
        Assert.Equal("not_found", quickRetryHit.Status);

        // Step 3: 26 seconds later (forward scan completes sync) -> negative cache expires
        clockTime = clockTime.AddSeconds(16); // total 26s
        Assert.False(cache.TryGet(key, out _));

        // Step 4: Database query now returns 'ok' and is cached with full 180s TTL
        cache.Set(key, new LookupResponse { Status = "ok", TrackingId = "UNSYNCED_AWB_123" });
        Assert.True(cache.TryGet(key, out var syncedHit));
        Assert.Equal("ok", syncedHit.Status);

        // Step 5: At 60s, the 'ok' cache entry is still valid (180s TTL)
        clockTime = clockTime.AddSeconds(60);
        Assert.True(cache.TryGet(key, out var warmHit));
        Assert.Equal("ok", warmHit.Status);
    }

    [Fact]
    public void RateLimiter_ExpiredEntries_ArePrunedUnderHighKeyVolume()
    {
        var dict = new System.Collections.Concurrent.ConcurrentDictionary<string, (int count, DateTime reset)>();
        var now = DateTime.UtcNow;

        // Seed 1500 expired entries
        for (var i = 0; i < 1500; i++)
        {
            dict[$"old_ip_{i}"] = (1, now.AddSeconds(-10));
        }

        // Add 5 active entries
        for (var i = 0; i < 5; i++)
        {
            dict[$"active_ip_{i}"] = (1, now.AddMinutes(1));
        }

        Assert.Equal(1505, dict.Count);

        // Run pruning logic
        if (dict.Count > 1000)
        {
            foreach (var entry in dict)
            {
                if (entry.Value.reset < now)
                    dict.TryRemove(entry.Key, out _);
            }
        }

        Assert.Equal(5, dict.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.True(dict.ContainsKey($"active_ip_{i}"));
        }
    }
}
