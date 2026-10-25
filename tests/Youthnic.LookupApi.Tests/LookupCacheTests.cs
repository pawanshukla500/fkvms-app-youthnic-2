using Xunit;
using Microsoft.Extensions.Caching.Memory;
using Youthnic.LookupApi.Models;
using Youthnic.LookupApi.Options;
using Youthnic.LookupApi.Services;

namespace Youthnic.LookupApi.Tests;

public class LookupCacheTests
{
    private sealed class FakeClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
    }

    private static (LookupCache Cache, FakeClock Clock) Create(int ttlSeconds = 60)
    {
        var clock = new FakeClock();
        var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = LookupApiOptions.CacheSizeLimit });
        var options = new LookupApiOptions { CacheSeconds = ttlSeconds };
        return (new LookupCache(memoryCache, options, () => clock.Now), clock);
    }

    private static LookupResponse Response(string status = "ok") => new() { Status = status };

    [Fact]
    public void SetThenGet_ReturnsCachedValue()
    {
        var (cache, _) = Create();
        cache.Set("flipkart:AWB1", Response());
        Assert.True(cache.TryGet("flipkart:AWB1", out var hit));
        Assert.Equal("ok", hit.Status);
    }

    [Fact]
    public void Get_UnknownKey_Misses()
    {
        var (cache, _) = Create();
        Assert.False(cache.TryGet("flipkart:NOPE", out _));
    }

    [Fact]
    public void EntryExpiresAfterTtl()
    {
        var (cache, clock) = Create(ttlSeconds: 60);
        cache.Set("flipkart:AWB1", Response());
        clock.Now = clock.Now.AddSeconds(61);
        Assert.False(cache.TryGet("flipkart:AWB1", out _));
    }

    [Fact]
    public void AccessSlidesExpiration()
    {
        var (cache, clock) = Create(ttlSeconds: 60);
        cache.Set("flipkart:AWB1", Response());

        clock.Now = clock.Now.AddSeconds(45);
        Assert.True(cache.TryGet("flipkart:AWB1", out _)); // slide

        clock.Now = clock.Now.AddSeconds(45); // 90s after set, 45s after last access
        Assert.True(cache.TryGet("flipkart:AWB1", out _));

        clock.Now = clock.Now.AddSeconds(61); // 106s after last access
        Assert.False(cache.TryGet("flipkart:AWB1", out _));
    }

    [Fact]
    public void CacheKey_DistinguishesMarketplace()
    {
        var (cache, _) = Create();
        cache.Set("flipkart:AWB1", Response("flipkart-ok"));
        cache.Set("myntra:AWB1", Response("myntra-ok"));
        Assert.True(cache.TryGet("flipkart:AWB1", out var a));
        Assert.True(cache.TryGet("myntra:AWB1", out var b));
        Assert.Equal("flipkart-ok", a.Status);
        Assert.Equal("myntra-ok", b.Status);
    }

    [Fact]
    public void Capacity_IsBounded()
    {
        var (cache, _) = Create();
        for (var i = 0; i < LookupApiOptions.CacheSizeLimit + 100; i++)
            cache.Set($"flipkart:AWB{i}", Response());
        Assert.True(cache.Count <= LookupApiOptions.CacheSizeLimit);
    }

    [Fact]
    public void KeyFor_NormalizesMarketplaceToLowerAndTrackingAndSellerToUpper()
    {
        var key1 = LookupCache.KeyFor("FLIPKART", "fmpc6678851344");
        var key2 = LookupCache.KeyFor("flipkart", "FMPC6678851344");
        Assert.Equal("flipkart:FMPC6678851344", key1);
        Assert.Equal(key1, key2);

        var keyWithSeller1 = LookupCache.KeyFor("Flipkart", "fmpc6678851344", "ethnic junction - flipkart");
        var keyWithSeller2 = LookupCache.KeyFor("FLIPKART", "FMPC6678851344", "ETHNIC JUNCTION - FLIPKART");
        Assert.Equal("flipkart:FMPC6678851344@ETHNIC JUNCTION - FLIPKART", keyWithSeller1);
        Assert.Equal(keyWithSeller1, keyWithSeller2);
    }
}
