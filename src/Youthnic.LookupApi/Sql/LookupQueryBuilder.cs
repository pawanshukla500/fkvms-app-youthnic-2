using System.Text;
using Youthnic.LookupApi.Options;
using Youthnic.LookupApi.Services;

namespace Youthnic.LookupApi.Sql;

/// <summary>
/// Builds the single-round-trip lookup query. Identifiers come only from the
/// validated mapping and are validated again here (defense in depth) before being
/// quoted; values travel exclusively as $1/$2 parameters. The whole statement runs
/// inside an explicit READ ONLY transaction.
/// Two shapes: items-table mode (orders JOIN order_items) and items-JSON mode
/// (orders CROSS JOIN LATERAL jsonb_array_elements of a JSON column on the orders row).
/// </summary>
public static class LookupQueryBuilder
{
    public const int MaxRows = 50;

    /// <summary>
    /// Returns SQL of the form:
    /// BEGIN READ ONLY; SELECT ... FROM orders o JOIN order_items oi ON ... WHERE upper(o.tracking)=upper($1) AND lower(o.marketplace)=lower($2) [AND upper(o.seller)=ANY($N)] LIMIT 50; COMMIT;
    /// (or the items-JSON lateral-join shape when <see cref="ForwardScanMapping.ItemsJsonColumn"/> is set).
    /// </summary>
    public static string Build(ForwardScanMapping m, out bool usesMarketplaceParameter)
        => Build(m, includeSellerFilter: false, out usesMarketplaceParameter, out _);

    /// <summary>
    /// Same as <see cref="Build(ForwardScanMapping, out bool)"/> with an optional
    /// parameterized seller-accounts filter ($2/$3) using ANY($N) so one packing desk can pin
    /// lookups to specific seller account(s) (e.g. "ETHNIC JUNCTION - Flipkart" or Myntra accounts).
    /// </summary>
    public static string Build(ForwardScanMapping m, bool includeSellerFilter, out bool usesMarketplaceParameter, out bool usesSellerParameter)
    {
        MappingValidator.ValidateMapping(m);

        if (includeSellerFilter && string.IsNullOrWhiteSpace(m.ColSeller))
            throw new InvalidOperationException("seller filter requested but mapping has no seller column");

        return string.IsNullOrWhiteSpace(m.ItemsJsonColumn)
            ? BuildItemsTableQuery(m, includeSellerFilter, out usesMarketplaceParameter, out usesSellerParameter)
            : BuildItemsJsonQuery(m, includeSellerFilter, out usesMarketplaceParameter, out usesSellerParameter);
    }

    /// <summary>
    /// Builds a fast 1-row query to find the seller/channel for a tracking ID across all channels
    /// (used to detect cross-marketplace scans, e.g. scanning a Myntra barcode on a Flipkart desk).
    /// </summary>
    public static string BuildCrossCheckQuery(ForwardScanMapping m)
    {
        MappingValidator.ValidateMapping(m);
        var orders = TableIdent(m.SchemaOrders, m.OrdersTable);
        var sellerCol = string.IsNullOrWhiteSpace(m.ColSeller) ? "NULL" : Quoted(m.ColSeller);
        return $"SELECT o.{sellerCol} FROM {orders} o WHERE (o.{Quoted(m.ColTracking)} = upper($1) OR upper(o.{Quoted(m.ColTracking)}) = upper($1)) LIMIT 1;";
    }

    // ---------------------------------------------------------------- items-table mode

    private static string BuildItemsTableQuery(ForwardScanMapping m, bool includeSellerFilter,
        out bool usesMarketplaceParameter, out bool usesSellerParameter)
    {
        var orders = TableIdent(m.SchemaOrders, m.OrdersTable);
        var items = TableIdent(m.SchemaOrders, m.OrdersItemsTable);
        var hasSkuMap = !string.IsNullOrWhiteSpace(m.SkuMapTable);
        var hasImages = !string.IsNullOrWhiteSpace(m.ImageTable) &&
                        !string.IsNullOrWhiteSpace(m.ImageSkuJoinColumn) &&
                        !string.IsNullOrWhiteSpace(m.ColImageUrl);

        var skuExpr = "oi." + Quoted(m.OrderItemsSkuColumn);

        var sb = new StringBuilder(1024);
        sb.Append("BEGIN READ ONLY; SELECT ");
        sb.Append(skuExpr).Append(" AS sku, ");
        sb.Append(hasSkuMap ? "m." + Quoted(m.ColSkuName) : "NULL").Append(" AS sku_name, ");
        sb.Append(hasImages ? "im." + Quoted(m.ColImageUrl) : "NULL").Append(" AS image_url, ");
        sb.Append(string.IsNullOrWhiteSpace(m.ColSeller) ? "NULL" : "o." + Quoted(m.ColSeller)).Append(" AS seller ");
        sb.Append("FROM ").Append(orders).Append(" o JOIN ").Append(items)
          .Append(" oi ON oi.").Append(Quoted(m.OrderItemsOrderJoinColumn))
          .Append(" = o.").Append(Quoted(m.OrderItemsOrderJoinColumn));

        if (hasSkuMap)
            AppendSkuMapLateralJoin(sb, m, skuExpr);

        if (hasImages)
            AppendImageLateralJoin(sb, m, skuExpr);

        sb.Append(" WHERE (o.").Append(Quoted(m.ColTracking)).Append(" = upper($1) OR upper(o.").Append(Quoted(m.ColTracking)).Append(") = upper($1))");
        usesMarketplaceParameter = !string.IsNullOrWhiteSpace(m.ColMarketplace);
        if (usesMarketplaceParameter)
            sb.Append(" AND lower(o.").Append(Quoted(m.ColMarketplace)).Append(") = lower($2)");
        AppendSellerFilter(sb, m, includeSellerFilter, usesMarketplaceParameter, out usesSellerParameter);
        sb.Append(" LIMIT ").Append(MaxRows).Append("; COMMIT;");
        return sb.ToString();
    }

    // ----------------------------------------------------------------- items-JSON mode

    private const string JsonSkuExpression = "(it.item ->> 'sku')";

    private static string BuildItemsJsonQuery(ForwardScanMapping m, bool includeSellerFilter,
        out bool usesMarketplaceParameter, out bool usesSellerParameter)
    {
        var orders = TableIdent(m.SchemaOrders, m.OrdersTable);
        var itemsJson = Quoted(m.ItemsJsonColumn);
        var hasSkuMap = !string.IsNullOrWhiteSpace(m.SkuMapTable) &&
                        !string.IsNullOrWhiteSpace(m.ColSku) &&
                        !string.IsNullOrWhiteSpace(m.ColSkuName);
        var hasImages = !string.IsNullOrWhiteSpace(m.ImageTable) &&
                        !string.IsNullOrWhiteSpace(m.ImageSkuJoinColumn) &&
                        !string.IsNullOrWhiteSpace(m.ColImageUrl);

        var sb = new StringBuilder(1280);
        sb.Append("BEGIN READ ONLY; SELECT ");
        sb.Append(JsonSkuExpression).Append(" AS sku, ");
        // Newer schema versions carry title/image_url inline in the JSON element;
        // fall back to the sku-map / image tables when the inline value is absent.
        sb.Append(hasSkuMap
            ? "COALESCE(NULLIF(it.item ->> 'title', ''), m." + Quoted(m.ColSkuName) + ")"
            : "NULLIF(it.item ->> 'title', '')").Append(" AS sku_name, ");
        sb.Append(hasImages
            ? "COALESCE(NULLIF(it.item ->> 'image_url', ''), im." + Quoted(m.ColImageUrl) + ")"
            : "NULLIF(it.item ->> 'image_url', '')").Append(" AS image_url, ");
        sb.Append(string.IsNullOrWhiteSpace(m.ColSeller) ? "NULL" : "o." + Quoted(m.ColSeller)).Append(" AS seller, ");
        // Defensive qty parse: bad JSON data must not error the lookup; clamp to >= 1.
        sb.Append("GREATEST(CASE WHEN (it.item ->> 'qty') ~ '^[0-9]+$' ")
          .Append("THEN (it.item ->> 'qty')::int ELSE 1 END, 1) AS qty, ");
        sb.Append("COALESCE(it.item ->> 'sub_order_id', it.item ->> 'id', '') AS sub_order_id");
        sb.Append(" FROM ").Append(orders).Append(" o CROSS JOIN LATERAL jsonb_array_elements(o.")
          .Append(itemsJson).Append("::jsonb) AS it(item)");

        if (hasSkuMap)
            AppendSkuMapLateralJoin(sb, m, JsonSkuExpression);

        if (hasImages)
            AppendImageLateralJoin(sb, m, JsonSkuExpression);

        sb.Append(" WHERE (o.").Append(Quoted(m.ColTracking)).Append(" = upper($1) OR upper(o.").Append(Quoted(m.ColTracking)).Append(") = upper($1))");
        usesMarketplaceParameter = !string.IsNullOrWhiteSpace(m.ColMarketplace);
        if (usesMarketplaceParameter)
            sb.Append(" AND lower(o.").Append(Quoted(m.ColMarketplace)).Append(") = lower($2)");
        AppendSellerFilter(sb, m, includeSellerFilter, usesMarketplaceParameter, out usesSellerParameter);
        sb.Append(" LIMIT ").Append(MaxRows).Append("; COMMIT;");
        return sb.ToString();
    }

    /// <summary>
    /// Multi-level photo match:
    /// 1. Exact full SKU (case-insensitive)
    /// 2. Base SKU (size suffix stripped, e.g. _XS, _S, _M, _L, _XL, _2XL, etc.)
    /// 3. Sibling size variant of the same style/garment
    /// </summary>
    private static void AppendImageLateralJoin(StringBuilder sb, ForwardScanMapping m, string skuExpr)
    {
        var skuCol = Quoted(m.ImageSkuJoinColumn);
        var imgCol = Quoted(m.ColImageUrl);
        var imgTable = TableIdent(m.SchemaOrders, m.ImageTable);

        sb.Append(" LEFT JOIN LATERAL (SELECT sim.").Append(imgCol)
          .Append(" FROM ").Append(imgTable).Append(" sim")
          .Append(" WHERE (sim.").Append(skuCol).Append(" = ").Append(skuExpr)
          .Append(" OR upper(sim.").Append(skuCol).Append(") = upper(").Append(skuExpr).Append(")")
          .Append(" OR sim.").Append(skuCol).Append(" = REGEXP_REPLACE(").Append(skuExpr).Append(", '_([0-9]?[A-Za-z]+|FREE ?SIZE)$', '', 'i')")
          .Append(" OR upper(sim.").Append(skuCol).Append(") = upper(REGEXP_REPLACE(").Append(skuExpr).Append(", '_([0-9]?[A-Za-z]+|FREE ?SIZE)$', '', 'i'))")
          .Append(" OR sim.").Append(skuCol).Append(" ILIKE REGEXP_REPLACE(").Append(skuExpr).Append(", '_([0-9]?[A-Za-z]+|FREE ?SIZE)$', '', 'i') || '_%'")
          .Append(") AND sim.").Append(imgCol).Append(" IS NOT NULL AND sim.").Append(imgCol).Append(" != ''")
          .Append(" ORDER BY CASE")
          .Append(" WHEN sim.").Append(skuCol).Append(" = ").Append(skuExpr)
          .Append(" OR upper(sim.").Append(skuCol).Append(") = upper(").Append(skuExpr).Append(") THEN 1")
          .Append(" WHEN sim.").Append(skuCol).Append(" = REGEXP_REPLACE(").Append(skuExpr).Append(", '_([0-9]?[A-Za-z]+|FREE ?SIZE)$', '', 'i') THEN 2")
          .Append(" ELSE 3 END LIMIT 1) im ON true");
    }

    /// <summary>
    /// Multi-level title/SKU-name match:
    /// 1. Exact full SKU (case-insensitive)
    /// 2. Base SKU (size suffix stripped)
    /// 3. Sibling size variant
    /// </summary>
    private static void AppendSkuMapLateralJoin(StringBuilder sb, ForwardScanMapping m, string skuExpr)
    {
        var skuCol = Quoted(m.ColSku);
        var nameCol = Quoted(m.ColSkuName);
        var mapTable = TableIdent(m.SchemaOrders, m.SkuMapTable);

        sb.Append(" LEFT JOIN LATERAL (SELECT sm.").Append(nameCol)
          .Append(" FROM ").Append(mapTable).Append(" sm")
          .Append(" WHERE (sm.").Append(skuCol).Append(" = upper(").Append(skuExpr).Append(")")
          .Append(" OR sm.").Append(skuCol).Append(" = ").Append(skuExpr)
          .Append(" OR sm.").Append(skuCol).Append(" = REGEXP_REPLACE(").Append(skuExpr).Append(", '_([0-9]?[A-Za-z]+|FREE ?SIZE)$', '', 'i')")
          .Append(" OR upper(sm.").Append(skuCol).Append(") = upper(REGEXP_REPLACE(").Append(skuExpr).Append(", '_([0-9]?[A-Za-z]+|FREE ?SIZE)$', '', 'i'))")
          .Append(" OR sm.").Append(skuCol).Append(" ILIKE REGEXP_REPLACE(").Append(skuExpr).Append(", '_([0-9]?[A-Za-z]+|FREE ?SIZE)$', '', 'i') || '_%'")
          .Append(") ORDER BY CASE")
          .Append(" WHEN sm.").Append(skuCol).Append(" = ").Append(skuExpr)
          .Append(" OR upper(sm.").Append(skuCol).Append(") = upper(").Append(skuExpr).Append(") THEN 1")
          .Append(" WHEN sm.").Append(skuCol).Append(" = REGEXP_REPLACE(").Append(skuExpr).Append(", '_([0-9]?[A-Za-z]+|FREE ?SIZE)$', '', 'i') THEN 2")
          .Append(" ELSE 3 END LIMIT 1) m ON true");
    }

    /// <summary>
    /// Appends the parameterized seller predicate using ANY($N). The parameter number follows the
    /// marketplace parameter: $3 when a marketplace predicate exists, otherwise $2.
    /// </summary>
    private static void AppendSellerFilter(StringBuilder sb, ForwardScanMapping m, bool includeSellerFilter,
        bool usesMarketplaceParameter, out bool usesSellerParameter)
    {
        usesSellerParameter = includeSellerFilter;
        if (!includeSellerFilter) return;
        var n = usesMarketplaceParameter ? 3 : 2;
        sb.Append(" AND upper(o.").Append(Quoted(m.ColSeller)).Append(") = ANY($").Append(n).Append(")");
    }

    /// <summary>Quotes a single identifier (no dots) after allowlist validation.</summary>
    internal static string Quoted(string identifier)
    {
        MappingValidator.EnsureValid("identifier", identifier);
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }

    public static string TableIdent(string schema, string table)
    {
        MappingValidator.EnsureValid("table", table);
        if (string.IsNullOrWhiteSpace(schema)) return Quoted(table);
        MappingValidator.EnsureValid("schema", schema);
        return Quoted(schema) + "." + Quoted(table);
    }
}
