using System.Text.Json.Serialization;

namespace Youthnic.Desktop.Models;

public static class LookupStatus
{
    public const string Ok = "ok";
    public const string NotFound = "not_found";
    public const string LookupError = "lookup_error";
    public const string MappingPending = "mapping_pending";
}

public sealed record LookupItem(
    [property: JsonPropertyName("sku")] string Sku,
    [property: JsonPropertyName("skuName")] string SkuName,
    [property: JsonPropertyName("imageUrls")] string[] ImageUrls,
    [property: JsonPropertyName("seller")] string? Seller,
    [property: JsonPropertyName("quantity")] int Quantity);

public sealed record LookupResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("items")] LookupItem[]? Items,
    [property: JsonPropertyName("pendingSync")] bool PendingSync,
    [property: JsonPropertyName("hint")] string? Hint);
