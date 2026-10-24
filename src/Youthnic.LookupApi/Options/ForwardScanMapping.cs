namespace Youthnic.LookupApi.Options;

/// <summary>
/// Configurable mapping between the Forward Scan database schema and the lookup query.
/// All values are table/column identifiers (never values) and must pass the strict
/// allowlist in <see cref="Services.MappingValidator"/> before use.
///
/// Required for a "ready" mapping: OrdersTable, ColTracking, plus EITHER
/// OrdersItemsTable + OrderItemsOrderJoinColumn + OrderItemsSkuColumn (items join mode)
/// OR ItemsJsonColumn (items-JSON mode, line items stored in a JSON/JSONB array column
/// on the orders table itself).
/// Optional: SchemaOrders (default schema for all tables), ColMarketplace,
/// ColSeller, SkuMapTable + ColSku + ColSkuName, ImageTable + ImageSkuJoinColumn + ColImageUrl.
/// </summary>
public sealed record ForwardScanMapping
{
    public string SchemaOrders { get; init; } = string.Empty;
    public string OrdersTable { get; init; } = string.Empty;
    public string ColTracking { get; init; } = string.Empty;
    public string ColMarketplace { get; init; } = string.Empty;
    public string ColSeller { get; init; } = string.Empty;

    public string OrdersItemsTable { get; init; } = string.Empty;
    /// <summary>Join column that must exist in BOTH the orders table (PK side) and the items table (FK side).</summary>
    public string OrderItemsOrderJoinColumn { get; init; } = string.Empty;
    /// <summary>SKU column on the order-items table.</summary>
    public string OrderItemsSkuColumn { get; init; } = string.Empty;

    /// <summary>
    /// JSON/JSONB array column on the orders table holding one element per line item
    /// ({"sku","qty","title","image_url",...}). When set, line items come from this
    /// column (lateral join) instead of the join to OrdersItemsTable.
    /// </summary>
    public string ItemsJsonColumn { get; init; } = string.Empty;

    public string SkuMapTable { get; init; } = string.Empty;
    public string ColSku { get; init; } = string.Empty;
    public string ColSkuName { get; init; } = string.Empty;

    public string ImageTable { get; init; } = string.Empty;
    public string ImageSkuJoinColumn { get; init; } = string.Empty;
    public string ColImageUrl { get; init; } = string.Empty;

    public static ForwardScanMapping FromEnvironment(Func<string, string?>? get = null)
    {
        get ??= Environment.GetEnvironmentVariable;
        return new ForwardScanMapping
        {
            SchemaOrders = (get("FS_SCHEMA_ORDERS") ?? string.Empty).Trim(),
            OrdersTable = (get("FS_ORDERS_TABLE") ?? string.Empty).Trim(),
            ColTracking = (get("FS_COL_TRACKING") ?? string.Empty).Trim(),
            ColMarketplace = (get("FS_COL_MARKETPLACE") ?? string.Empty).Trim(),
            ColSeller = (get("FS_COL_SELLER") ?? string.Empty).Trim(),
            OrdersItemsTable = (get("FS_ORDERS_ITEMS_TABLE") ?? string.Empty).Trim(),
            OrderItemsOrderJoinColumn = (get("FS_ORDER_ITEMS_ORDER_JOIN_COL") ?? string.Empty).Trim(),
            OrderItemsSkuColumn = (get("FS_ORDER_ITEMS_SKU_COL") ?? string.Empty).Trim(),
            ItemsJsonColumn = (get("FS_ITEMS_JSON_COLUMN") ?? string.Empty).Trim(),
            SkuMapTable = (get("FS_SKU_MAP_TABLE") ?? string.Empty).Trim(),
            ColSku = (get("FS_COL_SKU") ?? string.Empty).Trim(),
            ColSkuName = (get("FS_COL_SKU_NAME") ?? string.Empty).Trim(),
            ImageTable = (get("FS_IMAGE_TABLE") ?? string.Empty).Trim(),
            ImageSkuJoinColumn = (get("FS_IMAGE_SKU_JOIN_COL") ?? string.Empty).Trim(),
            ColImageUrl = (get("FS_COL_IMAGE_URL") ?? string.Empty).Trim(),
        };
    }
}
