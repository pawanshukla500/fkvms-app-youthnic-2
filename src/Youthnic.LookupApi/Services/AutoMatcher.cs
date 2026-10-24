using Youthnic.LookupApi.Options;

namespace Youthnic.LookupApi.Services;

/// <summary>
/// Conservative auto-match of the Forward Scan schema, ONLY against metadata discovered
/// from information_schema. Documented candidate keywords per field; the first unambiguous
/// pick wins, anything unresolved is reported as a gap — never guessed or fabricated.
/// Env-provided values always win; auto-match only fills the gaps.
/// </summary>
public static class AutoMatcher
{
    // Documented candidate keywords:
    private static readonly string[] OrdersTableExact = ["orders", "order"];
    private static readonly string[] OrdersTableContains = ["order", "shipment", "consignment"];
    private static readonly string[] ItemsTableExact = ["order_items", "items", "order_item"];
    private static readonly string[] ItemsTableContains = ["item", "line_item", "detail"];
    private static readonly string[] SkuMapTableExact = ["sku_map", "skus", "sku_master", "products", "sku_photos"];
    private static readonly string[] SkuMapTableContains = ["sku", "product", "catalog", "master"];
    private static readonly string[] ImageTableExact = ["images", "image", "product_images"];
    private static readonly string[] ImageTableContains = ["image", "photo", "media"];

    public static bool TryResolve(
        SchemaCatalog catalog,
        ForwardScanMapping configured,
        ILogger logger,
        out ForwardScanMapping resolved,
        out string detail)
    {
        var m = configured;
        var gaps = new List<string>();

        // --- Tables ---
        var orders = PickTable(catalog.Tables, OrdersTableExact, OrdersTableContains);
        var items = PickTable(catalog.Tables, ItemsTableExact, ItemsTableContains);
        var skuMap = string.IsNullOrWhiteSpace(m.SkuMapTable)
            ? PickTable(catalog.Tables, SkuMapTableExact, SkuMapTableContains)
            : null;
        var images = string.IsNullOrWhiteSpace(m.ImageTable)
            ? PickTable(catalog.Tables, ImageTableExact, ImageTableContains)
            : null;

        // --- Items source: JSON column on the orders table wins over a join to an items table ---
        var itemsJson = string.IsNullOrWhiteSpace(m.ItemsJsonColumn)
            ? orders?.Columns.FirstOrDefault(c => c.Contains("item", StringComparison.OrdinalIgnoreCase) && orders.IsJsonColumn(c))
            : null;
        var jsonMode = !string.IsNullOrWhiteSpace(m.ItemsJsonColumn) || itemsJson is not null;
        if (jsonMode)
        {
            // In JSON mode there is normally no marketplace column on the orders table
            // (Forward Scan keeps marketplace on the channels table, which this API never
            // joins), so marketplace auto-resolution is skipped — the builder omits the
            // predicate entirely when ColMarketplace stays empty. An env-configured value
            // always wins and is kept.
            items = null;
        }

        // --- Orders columns ---
        var trackingCol = ResolveColumn(gaps, "FS_COL_TRACKING", m.ColTracking,
            orders, ["tracking_norm", "tracking_raw", "tracking_id", "tracking", "awb_no", "awb"], ["tracking", "awb"]);
        var marketplaceCol = jsonMode && string.IsNullOrWhiteSpace(m.ColMarketplace)
            ? null
            : ResolveColumn(gaps, "FS_COL_MARKETPLACE", m.ColMarketplace,
                orders, ["marketplace", "channel"], ["marketplace", "channel"], required: false);
        var sellerCol = ResolveColumn(gaps, "FS_COL_SELLER", m.ColSeller,
            orders, ["seller", "seller_id", "channel_label"], ["seller", "label"], required: false);

        // --- Items columns ---
        string? itemsSkuCol = null, joinCol = null;
        if (!jsonMode)
        {
            itemsSkuCol = ResolveColumn(gaps, "FS_ORDER_ITEMS_SKU_COL", m.OrderItemsSkuColumn,
                items, ["sku", "sku_id", "sku_code"], ["sku"]);
            joinCol = ResolveJoinColumn(gaps, orders, items, m.OrderItemsOrderJoinColumn);
        }

        // --- SKU map columns ---
        string? skuCol = null, skuNameCol = null;
        if (skuMap is not null)
        {
            skuCol = ResolveColumn(gaps, "FS_COL_SKU", m.ColSku,
                skuMap, ["sku", "sku_id", "sku_code"], ["sku"], required: false);
            skuNameCol = ResolveColumn(gaps, "FS_COL_SKU_NAME", m.ColSkuName,
                skuMap, ["sku_name", "item_name", "product_name", "name", "title"], ["name", "title"], required: false);
        }

        // --- Image columns ---
        string? imageJoinCol = null, imageUrlCol = null;
        if (images is not null)
        {
            imageJoinCol = ResolveColumn(gaps, "FS_IMAGE_SKU_JOIN_COL", m.ImageSkuJoinColumn,
                images, ["sku", "sku_id"], ["sku"], required: false);
            imageUrlCol = ResolveColumn(gaps, "FS_COL_IMAGE_URL", m.ColImageUrl,
                images, ["image_url", "img_url", "url", "image_path", "path"], ["url", "path", "image", "photo"], required: false);
        }

        // A partial SKU map or partial image table is worse than none — drop incomplete optional matches.
        if (skuMap is not null && (string.IsNullOrWhiteSpace(skuCol) || string.IsNullOrWhiteSpace(skuNameCol)))
        {
            logger.LogDebug("SKU map table {Table} matched but columns incomplete; ignoring optional SKU map", skuMap.SqlIdentifier);
            skuMap = null; skuCol = null; skuNameCol = null;
        }
        if (images is not null && (string.IsNullOrWhiteSpace(imageJoinCol) || string.IsNullOrWhiteSpace(imageUrlCol)))
        {
            logger.LogDebug("Image table {Table} matched but columns incomplete; ignoring optional image table", images.SqlIdentifier);
            images = null; imageJoinCol = null; imageUrlCol = null;
        }

        if (gaps.Count > 0)
        {
            resolved = configured;
            detail = "unresolved mapping fields: " + string.Join("; ", gaps) +
                     ". Provide the FS_* environment variables listed in .env.example.";
            return false;
        }

        resolved = m with
        {
            SchemaOrders = string.IsNullOrWhiteSpace(m.SchemaOrders) ? (orders!.Schema.Equals("public", StringComparison.OrdinalIgnoreCase) ? "" : orders.Schema) : m.SchemaOrders,
            OrdersTable = string.IsNullOrWhiteSpace(m.OrdersTable) ? orders!.Name : m.OrdersTable,
            ColTracking = string.IsNullOrWhiteSpace(m.ColTracking) ? trackingCol! : m.ColTracking,
            ColMarketplace = string.IsNullOrWhiteSpace(m.ColMarketplace) ? marketplaceCol ?? "" : m.ColMarketplace,
            ColSeller = string.IsNullOrWhiteSpace(m.ColSeller) ? sellerCol ?? "" : m.ColSeller,
            OrdersItemsTable = string.IsNullOrWhiteSpace(m.OrdersItemsTable) ? items?.Name ?? "" : m.OrdersItemsTable,
            OrderItemsOrderJoinColumn = string.IsNullOrWhiteSpace(m.OrderItemsOrderJoinColumn) ? joinCol ?? "" : m.OrderItemsOrderJoinColumn,
            OrderItemsSkuColumn = string.IsNullOrWhiteSpace(m.OrderItemsSkuColumn) ? itemsSkuCol ?? "" : m.OrderItemsSkuColumn,
            ItemsJsonColumn = string.IsNullOrWhiteSpace(m.ItemsJsonColumn) ? itemsJson ?? "" : m.ItemsJsonColumn,
            SkuMapTable = string.IsNullOrWhiteSpace(m.SkuMapTable) ? skuMap?.Name ?? "" : m.SkuMapTable,
            ColSku = string.IsNullOrWhiteSpace(m.ColSku) ? skuCol ?? "" : m.ColSku,
            ColSkuName = string.IsNullOrWhiteSpace(m.ColSkuName) ? skuNameCol ?? "" : m.ColSkuName,
            ImageTable = string.IsNullOrWhiteSpace(m.ImageTable) ? images?.Name ?? "" : m.ImageTable,
            ImageSkuJoinColumn = string.IsNullOrWhiteSpace(m.ImageSkuJoinColumn) ? imageJoinCol ?? "" : m.ImageSkuJoinColumn,
            ColImageUrl = string.IsNullOrWhiteSpace(m.ColImageUrl) ? imageUrlCol ?? "" : m.ColImageUrl,
        };

        // Every identifier we use must pass the same allowlist as configured values.
        try
        {
            MappingValidator.ValidateMapping(resolved);
        }
        catch (InvalidOperationException ex)
        {
            detail = "auto-matched mapping failed validation: " + ex.Message;
            return false;
        }

        detail = string.Empty;
        return true;
    }

    /// <summary>Exact-name match first, then keyword containment; prefers public-schema tables on ties.</summary>
    private static TableInfo? PickTable(IReadOnlyList<TableInfo> tables, string[] exactNames, string[] containsKeywords)
    {
        foreach (var name in exactNames)
        {
            var hit = tables.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        foreach (var keyword in containsKeywords)
        {
            var hits = tables.Where(t => t.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (hits.Length == 0) continue;
            return hits.OrderByDescending(t => t.Schema.Equals("public", StringComparison.OrdinalIgnoreCase)).First();
        }
        return null;
    }

    private static string? ResolveColumn(List<string> gaps, string field, string configuredValue,
        TableInfo? table, string[] exactNames, string[] containsKeywords, bool required = true)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue)) return configuredValue;
        if (table is null)
        {
            if (required) gaps.Add($"{field} (no candidate table found)");
            return null;
        }

        string? pick = null;
        foreach (var name in exactNames)
        {
            pick = table.Columns.FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (pick is not null) break;
        }
        pick ??= table.Columns.FirstOrDefault(c => containsKeywords.Any(k => c.Contains(k, StringComparison.OrdinalIgnoreCase)));

        if (pick is not null && MappingValidator.IsValidIdentifier(pick)) return pick;

        if (required)
            gaps.Add($"{field} (no column matching keywords '{string.Join(",", containsKeywords)}' on {table.SqlIdentifier})");
        return null;
    }

    /// <summary>
    /// Join column must exist in BOTH the orders table and the items table. Preference:
    /// items columns referencing "order" that also exist in orders; exact "order_id" first.
    /// </summary>
    private static string? ResolveJoinColumn(List<string> gaps, TableInfo? orders, TableInfo? items, string configuredValue)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue)) return configuredValue;
        if (orders is null || items is null)
        {
            gaps.Add("FS_ORDER_ITEMS_ORDER_JOIN_COL (no candidate tables found)");
            return null;
        }

        var orderCols = orders.Columns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = items.Columns
            .Where(c => orderCols.Contains(c))
            .Where(c => c.Contains("order", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var pick = candidates.FirstOrDefault(c => c.Equals("order_id", StringComparison.OrdinalIgnoreCase)) ?? candidates.FirstOrDefault();
        if (pick is not null && MappingValidator.IsValidIdentifier(pick)) return pick;

        gaps.Add($"FS_ORDER_ITEMS_ORDER_JOIN_COL (no items column referencing the orders table on {items.SqlIdentifier})");
        return null;
    }
}
