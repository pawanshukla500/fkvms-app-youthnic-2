using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace Youthnic.Desktop.Services;

/// <summary>
/// Bounded in-memory cache of decoded product images (max 24, least-recently-used
/// eviction). Images are decoded at 600px width and frozen, so evicted/replaced
/// entries become plain garbage — no forced GC. Failed downloads are remembered
/// briefly so a broken URL is not retried on every scan.
/// </summary>
public sealed class ImageCache
{
    private const int MaxEntries = 24;
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromMinutes(2);

    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    { Timeout = TimeSpan.FromSeconds(4) };

    private readonly object _gate = new();
    private readonly Dictionary<string, (BitmapImage image, long tick)> _images = new();
    private readonly Dictionary<string, DateTime> _failed = new();
    private long _tick;

    public async Task<BitmapImage?> GetAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        lock (_gate)
        {
            if (_failed.TryGetValue(url, out var failedAt) && DateTime.UtcNow - failedAt < NegativeCacheTtl)
                return null;
            if (_images.TryGetValue(url, out var hit))
            {
                _images[url] = (hit.image, ++_tick);
                return hit.image;
            }
        }

        try
        {
            var bytes = await SharedClient.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.DecodePixelWidth = 600;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes, writable: false);
            bitmap.EndInit();
            bitmap.Freeze();

            lock (_gate)
            {
                if (_images.Count >= MaxEntries)
                    EvictLru();
                _images[url] = (bitmap, ++_tick);
            }
            return bitmap;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or InvalidOperationException)
        {
            lock (_gate)
            {
                _failed[url] = DateTime.UtcNow;
            }
            return null;
        }
    }

    private void EvictLru()
    {
        var oldest = _images.MinBy(p => p.Value.tick);
        if (!string.IsNullOrEmpty(oldest.Key))
            _images.Remove(oldest.Key);
    }
}
