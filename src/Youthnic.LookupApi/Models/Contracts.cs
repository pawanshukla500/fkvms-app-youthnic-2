using System.Text.Json.Serialization;

namespace Youthnic.LookupApi.Models;

public sealed record LookupItem
{
    [JsonPropertyName("sku")] public required string Sku { get; init; }
    [JsonPropertyName("skuName")] public required string SkuName { get; init; }
    [JsonPropertyName("imageUrls")] public required string[] ImageUrls { get; init; }
    [JsonPropertyName("seller")] public string? Seller { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; } = 1;
}

public sealed class LookupResponse
{
    /// <summary>ok | not_found | wrong_marketplace | lookup_error | mapping_pending | invalid_request</summary>
    [JsonPropertyName("status")] public required string Status { get; init; }

    [JsonPropertyName("trackingId")] public string? TrackingId { get; init; }
    [JsonPropertyName("marketplace")] public string? Marketplace { get; init; }
    [JsonPropertyName("detectedMarketplace")] public string? DetectedMarketplace { get; init; }
    [JsonPropertyName("detectedSeller")] public string? DetectedSeller { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("items")] public List<LookupItem>? Items { get; init; }
    [JsonPropertyName("source")] public string? Source { get; init; }
    [JsonPropertyName("pendingSync")] public bool? PendingSync { get; init; }
    [JsonPropertyName("hint")] public string? Hint { get; init; }
    [JsonPropertyName("detail")] public string? Detail { get; init; }
}

public sealed class HealthResponse
{
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("database")] public required string Database { get; init; }
    [JsonPropertyName("mapping")] public required string Mapping { get; init; }
    [JsonPropertyName("readOnly")] public required bool ReadOnly { get; init; }
}
