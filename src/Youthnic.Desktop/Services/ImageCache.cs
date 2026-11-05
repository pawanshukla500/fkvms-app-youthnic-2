using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace Youthnic.Desktop.Services;

/// <summary>
/// Two-tier high-performance product image cache:
/// Tier 1: In-memory LRU cache of decoded frozen BitmapImages (max 128 entries).
/// Tier 2: Persistent on-disk cache in %LocalAppData%\Youthnic\PackingDesk\ImageCache.
/// Once an SKU image is fetched by any desk, subsequent scans on that PC load
/// instantaneously in &lt; 2 ms from local SSD with zero network traffic.
/// Automatically bounds on-disk storage to ~250 MB with LRU file pruning.
/// </summary>
public sealed class ImageCache
{
    private const int MaxMemoryEntries = 128;
    private const long MaxDiskCacheSizeBytes = 250 * 1024 * 1024; // 250 MB
    private const long TargetDiskCacheSizeBytes = 200 * 1024 * 1024; // 200 MB
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromMinutes(2);

    private static readonly HttpClient SharedClient = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YouthnicPackingDesk/1.0 (Windows NT 10.0; Win64; x64)");
        return client;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, (BitmapImage image, long tick)> _memoryCache = new();
    private readonly Dictionary<string, DateTime> _failed = new();
    private readonly string _diskCacheDir;
    private long _tick;
    private int _diskWritesSincePrune;

    /// <summary>Initializes an image cache with optional disk cache directory override.</summary>
    public ImageCache(string? diskCacheDir = null)
    {
        _diskCacheDir = diskCacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Youthnic", "PackingDesk", "ImageCache");

        try
        {
            Directory.CreateDirectory(_diskCacheDir);
            Task.Run(PruneDiskCacheIfNeeded);
        }
        catch (Exception ex)
        {
            AppLog.Debug($"ImageCache: could not initialize disk cache directory: {ex.Message}");
        }
    }

    /// <summary>Asynchronously retrieves an image by URL, trying memory LRU cache first, local disk cache second, and remote HTTP download third.</summary>
    public async Task<BitmapImage?> GetAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        // 1. Check memory cache (0 ms)
        lock (_gate)
        {
            PruneExpiredFailedEntries();

            if (_failed.TryGetValue(url, out var failedAt) && DateTime.UtcNow - failedAt < NegativeCacheTtl)
                return null;

            if (_memoryCache.TryGetValue(url, out var hit))
            {
                _memoryCache[url] = (hit.image, ++_tick);
                return hit.image;
            }
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            lock (_gate)
            {
                _failed[url] = DateTime.UtcNow;
            }
            return null;
        }

        byte[]? bytes = null;
        var diskPath = GetDiskCachePath(url);

        // 2. Check local disk cache (1-2 ms SSD read)
        if (File.Exists(diskPath))
        {
            try
            {
                bytes = await File.ReadAllBytesAsync(diskPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Debug($"ImageCache: disk read failed for {url}: {ex.Message}");
                bytes = null;
            }
        }

        // 3. Network fetch if absent from disk cache
        if (bytes is null || bytes.Length == 0)
        {
            try
            {
                bytes = await SharedClient.GetByteArrayAsync(url, ct).ConfigureAwait(false);
                _ = SaveToDiskAsync(diskPath, bytes);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Superseded by a newer scan; do NOT negative-cache
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or NotSupportedException or UriFormatException)
            {
                lock (_gate)
                {
                    _failed[url] = DateTime.UtcNow;
                }
                return null;
            }
        }

        // 4. Decode bitmap, freeze for UI thread safety, and store in Tier 1 memory cache
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.DecodePixelWidth = 500;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes, writable: false);
            bitmap.EndInit();
            bitmap.Freeze();

            lock (_gate)
            {
                if (_memoryCache.Count >= MaxMemoryEntries)
                    EvictMemoryLru();

                _memoryCache[url] = (bitmap, ++_tick);
            }
            return bitmap;
        }
        catch (Exception ex)
        {
            AppLog.Debug($"ImageCache: bitmap decode failed for {url}: {ex.Message}");
            lock (_gate)
            {
                _failed[url] = DateTime.UtcNow;
            }
            return null;
        }
    }

    private string GetDiskCachePath(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        return Path.Combine(_diskCacheDir, $"{hash}.img");
    }

    private async Task SaveToDiskAsync(string path, byte[] bytes)
    {
        string? tempPath = null;
        try
        {
            tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
            tempPath = null;

            // Periodically verify disk cache bounds every 50 new images written during continuous shifts
            if (Interlocked.Increment(ref _diskWritesSincePrune) >= 50)
            {
                Interlocked.Exchange(ref _diskWritesSincePrune, 0);
                _ = Task.Run(PruneDiskCacheIfNeeded);
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug($"ImageCache: disk write failed: {ex.Message}");
        }
        finally
        {
            if (tempPath != null)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
    }

    private void PruneExpiredFailedEntries()
    {
        if (_failed.Count < 32) return;
        var now = DateTime.UtcNow;
        var expired = _failed.Where(kvp => now - kvp.Value >= NegativeCacheTtl).Select(kvp => kvp.Key).ToList();
        foreach (var key in expired)
        {
            _failed.Remove(key);
        }
        if (_failed.Count > 500)
        {
            var excess = _failed.OrderBy(kvp => kvp.Value).Take(_failed.Count - 500).Select(kvp => kvp.Key).ToList();
            foreach (var key in excess)
            {
                _failed.Remove(key);
            }
        }
    }

    private void EvictMemoryLru()
    {
        var oldest = _memoryCache.MinBy(p => p.Value.tick);
        if (!string.IsNullOrEmpty(oldest.Key))
            _memoryCache.Remove(oldest.Key);
    }

    private void PruneDiskCacheIfNeeded()
    {
        try
        {
            if (!Directory.Exists(_diskCacheDir)) return;

            var dirInfo = new DirectoryInfo(_diskCacheDir);
            var files = dirInfo.GetFiles("*.img");
            long totalSize = 0;
            foreach (var f in files)
                totalSize += f.Length;

            if (totalSize <= MaxDiskCacheSizeBytes) return;

            // Sort oldest first and delete until under target
            var sorted = files.OrderBy(f => f.LastAccessTimeUtc).ToList();
            foreach (var file in sorted)
            {
                try
                {
                    totalSize -= file.Length;
                    file.Delete();
                }
                catch { }

                if (totalSize <= TargetDiskCacheSizeBytes) break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug($"ImageCache: disk pruning error: {ex.Message}");
        }
    }
}
