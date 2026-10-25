using Xunit;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop.Tests;

public class ScanObserverTests
{
    [Theory]
    [InlineData("{\"type\":\"scan\",\"awb\":\"FMPC12345678\"}", "FMPC12345678")]
    [InlineData("{\"type\":\"scan\",\"awb\":\"fmpc6678851344\"}", "FMPC6678851344")]
    [InlineData("{\"type\":\"scan\",\"awb\":\"  mynt9876543210  \"}", "MYNT9876543210")]
    [InlineData("{\"type\":\"scan\",\"awb\":\"1234\"}", "1234")]
    public void TryParseScanMessage_ValidScan_ReturnsTrueAndTrimmedAwb(string json, string expectedAwb)
    {
        var success = ScanObserver.TryParseScanMessage(json, out var awb);
        Assert.True(success);
        Assert.Equal(expectedAwb, awb);
    }

    [Theory]
    [InlineData("{\"type\":\"other\",\"awb\":\"FMPC12345678\"}")]
    [InlineData("{\"type\":\"scan\",\"awb\":\"\"}")]
    [InlineData("{\"type\":\"scan\",\"awb\":\"   \"}")]
    [InlineData("{\"type\":\"scan\",\"awb\":\"123\"}")] // less than 4 chars
    [InlineData("{\"type\":\"scan\"}")]
    [InlineData("{\"awb\":\"FMPC12345678\"}")]
    [InlineData("")]
    [InlineData("not a json")]
    [InlineData("{\"type\":123}")]
    public void TryParseScanMessage_InvalidOrNonScan_ReturnsFalse(string json)
    {
        var success = ScanObserver.TryParseScanMessage(json, out var awb);
        Assert.False(success);
        Assert.Equal(string.Empty, awb);
    }

    [Fact]
    public void ScanObserverScript_IsEmbeddedAndLoadsValidJavaScript()
    {
        var script = ScanObserver.Script;
        Assert.NotNull(script);
        Assert.Contains("window.chrome.webview.postMessage", script);
        Assert.Contains("keydown", script);
        Assert.Contains("ensureScanBarFocus", script);
    }
}
