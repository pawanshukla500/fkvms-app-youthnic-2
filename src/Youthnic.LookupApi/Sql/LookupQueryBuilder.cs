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

        var sb = new StringBuilder(640);
        sb.Append("BEGIN READ ONLY; SELECT ");
        sb.Append("oi.").Append(Quoted(m.OrderItemsSkuColumn)).Append(" AS sku, ");
        sb.Append(hasSkuMap ? "m." + Quoted(m.ColSkuName) : "NULL").Append(" AS sku_name, ");
        sb.Append(hasImages ? "im." + Quoted(m.ColImageUrl) : "NULL").Append(" AS image_url, ");
        sb.Append(string.IsNullOrWhiteSpace(m.ColSeller) ? "NULL" : "o." + Quoted(m.ColSeller)).Append(" AS seller ");
        sb.Append("FROM ").Append(orders).Append(" o JOIN ").Append(items)
          .Append(" oi ON oi.").Append(Quoted(m.OrderItemsOrderJoinColumn))
          .Append(" = o.").Append(Quoted(m.OrderItemsOrderJoinColumn));
        if (hasSkuMap)
            sb.Append(" LEFT JOIN ").Append(TableIdent(m.SchemaOrders, m.SkuMapTable))
              .Append(" m ON (m.").Append(Quoted(m.ColSku)).Append(" = upper(oi.").Append(Quoted(m.OrderItemsSkuColumn))
              .Append(") OR m.").Append(Quoted(m.ColSku)).Append(" = oi.").Append(Quoted(m.OrderItemsSkuColumn)).Append(")");
        if (hasImages)
            sb.Append(" LEFT JOIN ").Append(TableIdent(m.SchemaOrders, m.ImageTable))
              .Append(" im ON (im.").Append(Quoted(m.ImageSkuJoinColumn)).Append(" = upper(oi.").Append(Quoted(m.OrderItemsSkuColumn))
              .Append(") OR im.").Append(Quoted(m.ImageSkuJoinColumn)).Append(" = oi.").Append(Quoted(m.OrderItemsSkuColumn)).Append(")");
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

        var sb = new StringBuilder(960);
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
          .Append("THEN (it.item ->> 'qty')::int ELSE 1 END, 1) AS qty");
        sb.Append(" FROM ").Append(orders).Append(" o CROSS JOIN LATERAL jsonb_array_elements(o.")
          .Append(itemsJson).Append("::jsonb) AS it(item)");
        if (hasSkuMap)
            sb.Append(" LEFT JOIN ").Append(TableIdent(m.SchemaOrders, m.SkuMapTable))
              .Append(" m ON (m.").Append(Quoted(m.ColSku)).Append(" = upper(").Append(JsonSkuExpression)
              .Append(") OR m.").Append(Quoted(m.ColSku)).Append(" = ").Append(JsonSkuExpression).Append(")");
        if (hasImages)
            sb.Append(" LEFT JOIN ").Append(TableIdent(m.SchemaOrders, m.ImageTable))
              .Append(" im ON (im.").Append(Quoted(m.ImageSkuJoinColumn)).Append(" = upper(").Append(JsonSkuExpression)
              .Append(") OR im.").Append(Quoted(m.ImageSkuJoinColumn)).Append(" = ").Append(JsonSkuExpression).Append(")");
        sb.Append(" WHERE (o.").Append(Quoted(m.ColTracking)).Append(" = upper($1) OR upper(o.").Append(Quoted(m.ColTracking)).Append(") = upper($1))");
        usesMarketplaceParameter = !string.IsNullOrWhiteSpace(m.ColMarketplace);
        if (usesMarketplaceParameter)
            sb.Append(" AND lower(o.").Append(Quoted(m.ColMarketplace)).Append(") = lower($2)");
        AppendSellerFilter(sb, m, includeSellerFilter, usesMarketplaceParameter, out usesSellerParameter);
        sb.Append(" LIMIT ").Append(MaxRows).Append("; COMMIT;");
        return sb.ToString();
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
