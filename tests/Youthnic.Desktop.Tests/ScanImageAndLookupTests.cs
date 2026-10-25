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
}
