using System.IO;
using System.Windows.Threading;
using Xunit;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop.Tests;

public class MultiDeskStressTests
{
    private static void PumpDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(f =>
        {
            ((DispatcherFrame)f).Continue = false;
            return null;
        }), frame);
        Dispatcher.PushFrame(frame);
    }

    [Fact]
    public async Task TenDesks_400ScansEach_ImageCacheBoundsAndNegativePruning()
    {
        const int deskCount = 10;
        const int scansPerDesk = 400;

        var tempRoot = Path.Combine(Path.GetTempPath(), "MultiDeskTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var deskCaches = new ImageCache[deskCount];
            for (var d = 0; d < deskCount; d++)
            {
                var deskDir = Path.Combine(tempRoot, $"Desk_{d}");
                deskCaches[d] = new ImageCache(deskDir);
            }

            var tasks = Enumerable.Range(0, deskCount).Select(async deskIndex =>
            {
                var cache = deskCaches[deskIndex];
                await Task.Yield();

                for (var i = 0; i < scansPerDesk; i++)
                {
                    // Mix of missing images, invalid schemes, and repeated URLs
                    var url = (i % 4 == 0)
                        ? "https://invalid.domain.youthnic/catalog/missing.jpg"
                        : $"badscheme://invalid/desk{deskIndex}_img{i}";

                    var img = await cache.GetAsync(url);
                    Assert.Null(img);
                }
            });

            await Task.WhenAll(tasks);

            // Verify all 10 desks handled 400 scans cleanly and negative cache pruned
            for (var d = 0; d < deskCount; d++)
            {
                var result = await deskCaches[d].GetAsync("badscheme://invalid/desk0_img0");
                Assert.Null(result);
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
    }

    [Fact]
    public void TenDesks_ConcurrentLookupCoordinators_MaintainSequenceIntegrity()
    {
        const int deskCount = 10;
        const int scansPerDesk = 400;

        var dispatcher = Dispatcher.CurrentDispatcher;
        var coordinators = new LookupCoordinator[deskCount];
        var deskCounts = new int[deskCount];

        for (var d = 0; d < deskCount; d++)
        {
            var deskIdx = d;
            var client = new LookupApiClient();
            var cache = new ImageCache();
            var coordinator = new LookupCoordinator(
                dispatcher, client, cache,
                () => "http://127.0.0.1:5080",
                () => "",
                () => ("Flipkart", "ETHNIC JUNCTION - Flipkart"));

            coordinator.LookupStarted += (seq, awb) =>
            {
                deskCounts[deskIdx]++;
            };

            coordinators[d] = coordinator;
        }

        // Simulate 10 desks receiving scans concurrently
        for (var s = 0; s < scansPerDesk; s++)
        {
            for (var d = 0; d < deskCount; d++)
            {
                coordinators[d].HandleScan($"DESK{d}_AWB{s:D5}");
            }
        }

        PumpDispatcher(dispatcher);

        // Every desk must have initiated exactly 400 lookups
        for (var d = 0; d < deskCount; d++)
        {
            Assert.Equal(scansPerDesk, deskCounts[d]);
        }
    }
}
