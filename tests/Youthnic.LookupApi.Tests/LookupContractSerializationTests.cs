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

    [Fact]
    public void LookupResponse_WithProductImageUrls_DeserializesAndExtractsImageUrls()
    {
        const string rawJson = """
            {
                "status": "ok",
                "trackingId": "FMPC6583186374",
                "marketplace": "flipkart",
                "items": [
                    {
                        "sku": "SKD102-Tulsi-Grey_L",
                        "skuName": "Grey Silk Blend Embroidered Kurta Sharara With Dupatta Set L",
                        "imageUrls": [
                            "https://cdn.shopify.com/s/files/1/0647/0215/2903/files/2_289b5009-bfce-4e41-a9b5-5a430d08ab00.jpg?v=1740683911",
                            "https://img.fkcdn.com/image/sku/SKD102.jpg"
                        ],
                        "seller": "ETHNIC JUNCTION - Flipkart",
                        "quantity": 1
                    }
                ]
            }
            """;

        var response = JsonSerializer.Deserialize<LookupResponse>(rawJson, JsonOptions);

        Assert.NotNull(response);
        Assert.Equal("ok", response.Status);
        Assert.NotNull(response.Items);
        Assert.Single(response.Items);

        var item = response.Items[0];
        Assert.Equal(2, item.ImageUrls.Length);
        Assert.StartsWith("https://cdn.shopify.com", item.ImageUrls[0]);
        Assert.StartsWith("https://img.fkcdn.com", item.ImageUrls[1]);
    }

    [Fact]
    public void LookupResponse_ForMyntraOrders_DeserializesCorrectly()
    {
        const string rawJson = """
            {
                "status": "ok",
                "trackingId": "MYSP1489001588",
                "marketplace": "myntra",
                "items": [
                    {
                        "sku": "SKD193-Gulnaaz-Pink_XL",
                        "skuName": "SKD193-Gulnaaz-Pink_XL",
                        "imageUrls": [
                            "https://assets.myntassets.com/sku/SKD193.jpg"
                        ],
                        "seller": "ETHNIC JUNCTION - Myntra Youthnic",
                        "quantity": 2
                    }
                ]
            }
            """;

        var response = JsonSerializer.Deserialize<LookupResponse>(rawJson, JsonOptions);

        Assert.NotNull(response);
        Assert.Equal("ok", response.Status);
        Assert.Equal("myntra", response.Marketplace);
        Assert.NotNull(response.Items);
        Assert.Equal("ETHNIC JUNCTION - Myntra Youthnic", response.Items[0].Seller);
        Assert.Equal(2, response.Items[0].Quantity);
        Assert.Single(response.Items[0].ImageUrls);
    }

    [Fact]
    public void LookupResponse_CrossMarketplaceScan_DeserializesDetails()
    {
        const string rawJson = """
            {
                "status": "wrong_marketplace",
                "trackingId": "MYSP1489001588",
                "marketplace": "flipkart",
                "detectedMarketplace": "myntra",
                "detectedSeller": "ETHNIC JUNCTION - Myntra Youthnic",
                "message": "Wrong marketplace! This barcode belongs to Myntra. Do NOT pack at Flipkart desk.",
                "hint": "Scan this package at the Myntra Packing Desk."
            }
            """;

        var response = JsonSerializer.Deserialize<LookupResponse>(rawJson, JsonOptions);

        Assert.NotNull(response);
        Assert.Equal("wrong_marketplace", response.Status);
        Assert.Equal("myntra", response.DetectedMarketplace);
        Assert.Equal("ETHNIC JUNCTION - Myntra Youthnic", response.DetectedSeller);
        Assert.Contains("Do NOT pack at Flipkart desk", response.Message);
    }
}
