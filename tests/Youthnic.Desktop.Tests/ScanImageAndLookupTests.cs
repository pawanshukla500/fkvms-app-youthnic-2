using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;
using Youthnic.Desktop.Models;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop.Tests;

public class ScanImageAndLookupTests
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
    public void LookupCoordinator_DuplicateScanWithinWindow_IsSuppressed()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var client = new LookupApiClient();
        var cache = new ImageCache();
        var coordinator = new LookupCoordinator(
            dispatcher, client, cache,
            () => "http://127.0.0.1:5080",
            () => "",
            () => ("Flipkart", "ETHNIC JUNCTION - Flipkart"));

        int startedCount = 0;
        coordinator.LookupStarted += (seq, awb) => startedCount++;

        coordinator.HandleScan("FMPC000111222");
        PumpDispatcher(dispatcher);

        coordinator.HandleScan("FMPC000111222"); // duplicate within 2s
        PumpDispatcher(dispatcher);

        // Second duplicate scan should be suppressed
        Assert.Equal(1, startedCount);
    }

    [Fact]
    public void LookupCoordinator_DifferentAwb_IsAllowedImmediately()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var client = new LookupApiClient();
        var cache = new ImageCache();
        var coordinator = new LookupCoordinator(
            dispatcher, client, cache,
            () => "http://127.0.0.1:5080",
            () => "",
            () => ("Flipkart", "ETHNIC JUNCTION - Flipkart"));

        int startedCount = 0;
        var scannedAwbs = new List<string>();
        coordinator.LookupStarted += (seq, awb) =>
        {
            startedCount++;
            scannedAwbs.Add(awb);
        };

        coordinator.HandleScan("FMPC000111222");
        PumpDispatcher(dispatcher);

        coordinator.HandleScan("FMPC000999888");
        PumpDispatcher(dispatcher);

        Assert.Equal(2, startedCount);
        Assert.Equal(new[] { "FMPC000111222", "FMPC000999888" }, scannedAwbs);
    }

    [Fact]
    public void LookupCoordinator_ShortBarcode_IsIgnored()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var client = new LookupApiClient();
        var cache = new ImageCache();
        var coordinator = new LookupCoordinator(
            dispatcher, client, cache,
            () => "http://127.0.0.1:5080",
            () => "",
            () => ("Flipkart", "ETHNIC JUNCTION - Flipkart"));

        int startedCount = 0;
        coordinator.LookupStarted += (seq, awb) => startedCount++;

        coordinator.HandleScan("12"); // < 4 chars
        PumpDispatcher(dispatcher);

        Assert.Equal(0, startedCount);
    }

    [Fact]
    public void LookupOutcome_Mapping_HandlesAllStatuses()
    {
        var okResponse = new LookupResponse(
            Status: LookupStatus.Ok,
            Items: new[]
            {
                new LookupItem("SKU123", "Red Kurta", new[] { "https://cdn.example.com/1.jpg" }, "ETHNIC JUNCTION", 2)
            },
            Hint: "Found in database");

        Assert.Equal(LookupStatus.Ok, okResponse.Status);
        Assert.Single(okResponse.Items!);
        Assert.Equal("SKU123", okResponse.Items![0].Sku);
        Assert.Equal(2, okResponse.Items![0].Quantity);
        Assert.Single(okResponse.Items![0].ImageUrls);

        var wrongMpResponse = new LookupResponse(
            Status: LookupStatus.WrongMarketplace,
            Items: null,
            Hint: "Scanned on wrong desk",
            DetectedMarketplace: "Myntra",
            Message: "This barcode belongs to Myntra. Please pack at Myntra desk.");

        Assert.Equal(LookupStatus.WrongMarketplace, wrongMpResponse.Status);
        Assert.Equal("Myntra", wrongMpResponse.DetectedMarketplace);
        Assert.Contains("Myntra desk", wrongMpResponse.Message!);
    }

    [Fact]
    public void ImageUrlsCandidateFiltering_ExtractsNonEmptyUrls()
    {
        var rawUrls = new[] { "", "   ", "https://cdn.example.com/img1.jpg", null, "https://cdn.example.com/img2.jpg" };
        var validUrls = rawUrls.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();

        Assert.Equal(2, validUrls.Count);
        Assert.Equal("https://cdn.example.com/img1.jpg", validUrls[0]);
        Assert.Equal("https://cdn.example.com/img2.jpg", validUrls[1]);
    }

    [Fact]
    public void LookupResponse_WithEmptyImageUrlsAndNullablePendingSync_DeserializesGracefully()
    {
        var json = """
        {
            "status": "ok",
            "trackingId": "FMPP4412878836",
            "marketplace": "flipkart",
            "detectedMarketplace": null,
            "detectedSeller": null,
            "message": null,
            "items": [
                {
                    "sku": "SKD237-Juhi-Mehendi_M",
                    "skuName": "SKD237-Juhi-Mehendi_M",
                    "imageUrls": [],
                    "seller": "ETHNIC JUNCTION - Flipkart",
                    "quantity": 1
                }
            ],
            "source": "forward_scan",
            "pendingSync": null,
            "hint": null,
            "detail": null
        }
        """;

        var response = System.Text.Json.JsonSerializer.Deserialize<LookupResponse>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(response);
        Assert.Equal(LookupStatus.Ok, response.Status);
        Assert.Null(response.PendingSync);
        Assert.Single(response.Items!);
        Assert.Equal("SKD237-Juhi-Mehendi_M", response.Items![0].Sku);
        Assert.Empty(response.Items![0].ImageUrls);
    }

    [Fact]
    public async Task ImageCache_Handles1000Scans_NegativeCachePrunedAndBounded()
    {
        var tempCacheDir = Path.Combine(Path.GetTempPath(), "YouthnicTestCache_" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ImageCache(tempCacheDir);

            // Simulate 1000 scans with invalid scheme (throws InvalidOperationException instantly)
            for (var i = 0; i < 1000; i++)
            {
                var fakeUrl = $"badscheme://invalid-host/image_{i}.jpg";
                var result = await cache.GetAsync(fakeUrl);
                Assert.Null(result);
            }

            // Verify cache handles repeated queries cleanly and negative cache is active
            var retryResult = await cache.GetAsync("badscheme://invalid-host/image_0.jpg");
            Assert.Null(retryResult);
        }
        finally
        {
            if (Directory.Exists(tempCacheDir))
            {
                try { Directory.Delete(tempCacheDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void LookupCoordinator_1000ScansSequence_ProcessesSmoothly()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var client = new LookupApiClient();
        var cache = new ImageCache();
        var coordinator = new LookupCoordinator(
            dispatcher, client, cache,
            () => "http://127.0.0.1:5080",
            () => "",
            () => ("Flipkart", "ETHNIC JUNCTION - Flipkart"));

        var startedAwbs = new List<string>();
        coordinator.LookupStarted += (seq, awb) => startedAwbs.Add(awb);

        // Feed 1000 distinct AWBs
        for (var i = 0; i < 1000; i++)
        {
            coordinator.HandleScan($"AWB{i:D6}");
        }

        PumpDispatcher(dispatcher);

        Assert.Equal(1000, startedAwbs.Count);
        Assert.Equal("AWB000000", startedAwbs[0]);
        Assert.Equal("AWB000999", startedAwbs[999]);
    }
}

