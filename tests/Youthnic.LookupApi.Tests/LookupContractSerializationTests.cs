using System.Text.Json;
using Xunit;
using Youthnic.LookupApi.Models;

namespace Youthnic.LookupApi.Tests;

public class LookupContractSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void LookupResponse_WithNullPendingSync_DeserializesSuccessfully()
    {
        const string rawJson = """
            {
                "status": "ok",
                "trackingId": "FMPC6674718845",
                "marketplace": "flipkart",
                "detectedMarketplace": null,
                "detectedSeller": null,
                "message": null,
                "items": [
                    {
                        "sku": "BT165-Nishi-Parrot Green_M",
                        "skuName": "BT165-Nishi-Parrot Green_M",
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

        var response = JsonSerializer.Deserialize<LookupResponse>(rawJson, JsonOptions);

        Assert.NotNull(response);
        Assert.Equal("ok", response.Status);
        Assert.Null(response.PendingSync);
        Assert.NotNull(response.Items);
        Assert.Single(response.Items);
        Assert.Equal("BT165-Nishi-Parrot Green_M", response.Items[0].Sku);
        Assert.Equal(1, response.Items[0].Quantity);
    }

    [Fact]
    public void LookupResponse_WithBooleanPendingSync_DeserializesSuccessfully()
    {
        const string rawJson = """
            {
                "status": "not_found",
                "pendingSync": true,
                "hint": "Order may not be synced yet"
            }
            """;

        var response = JsonSerializer.Deserialize<LookupResponse>(rawJson, JsonOptions);

        Assert.NotNull(response);
        Assert.Equal("not_found", response.Status);
        Assert.True(response.PendingSync);
        Assert.Equal("Order may not be synced yet", response.Hint);
    }
}
