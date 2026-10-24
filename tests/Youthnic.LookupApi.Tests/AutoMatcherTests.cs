using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Youthnic.LookupApi.Options;
using Youthnic.LookupApi.Services;

namespace Youthnic.LookupApi.Tests;

/// <summary>Auto-match against a catalog shaped like the verified production schema:
/// oms_orders(items_json jsonb, tracking_norm, tracking_raw, channel_label, ...),
/// sku_photos(sku, title, image_url), channels(id, name, marketplace, company).</summary>
public class AutoMatcherTests
{
    private static TableInfo Table(string name, params (string Column, string DataType)[] columns)
    {
        var t = new TableInfo("public", name);
        foreach (var (column, dataType) in columns)
        {
            t.Columns.Add(column);
            t.ColumnTypes[column] = dataType;
        }
        return t;
    }

    private static SchemaCatalog VerifiedCatalog()
    {
        var catalog = new SchemaCatalog();
        catalog.Tables.Add(Table("oms_orders",
            ("id", "bigint"),
            ("channel_id", "bigint"),
            ("channel_label", "character varying"),
            ("tracking_raw", "character varying"),
            ("tracking_norm", "character varying"),
            ("items_json", "jsonb"),
            ("status", "character varying"),
            ("created_at", "timestamp without time zone")));
        catalog.Tables.Add(Table("sku_photos",
            ("sku", "character varying"),
            ("title", "character varying"),
            ("image_url", "text"),
            ("marketplace", "character varying"),
            ("updated_at", "timestamp without time zone")));
        catalog.Tables.Add(Table("channels",
            ("id", "bigint"),
            ("name", "character varying"),
            ("marketplace", "character varying"),
            ("company", "character varying")));
        return catalog;
    }

    [Fact]
    public void TryResolve_VerifiedForwardScanSchema_ResolvesItemsJsonMode()
    {
        var ok = AutoMatcher.TryResolve(VerifiedCatalog(), new ForwardScanMapping(),
            NullLogger.Instance, out var resolved, out var detail);

        Assert.True(ok);
        Assert.Equal(string.Empty, detail);

        Assert.Equal("oms_orders", resolved.OrdersTable);
        Assert.Equal("tracking_norm", resolved.ColTracking);       // normalized AWB preferred
        Assert.Equal("channel_label", resolved.ColSeller);         // seller account name
        Assert.Equal("items_json", resolved.ItemsJsonColumn);

        // Items-table mode relaxed: no items table in this schema.
        Assert.Equal("", resolved.OrdersItemsTable);
        Assert.Equal("", resolved.OrderItemsOrderJoinColumn);
        Assert.Equal("", resolved.OrderItemsSkuColumn);

        // Marketplace lives only on channels (never joined) — predicate must be omitted.
        Assert.Equal("", resolved.ColMarketplace);

        // sku_photos serves as both sku map and image table.
        Assert.Equal("sku_photos", resolved.SkuMapTable);
        Assert.Equal("sku", resolved.ColSku);
        Assert.Equal("title", resolved.ColSkuName);
        Assert.Equal("sku_photos", resolved.ImageTable);
        Assert.Equal("sku", resolved.ImageSkuJoinColumn);
        Assert.Equal("image_url", resolved.ColImageUrl);

        // The resolved mapping must pass the same validation as a configured one.
        MappingValidator.ValidateMapping(resolved);
    }

    [Fact]
    public void TryResolve_ResolvedJsonMapping_BuildsJsonQuery()
    {
        AutoMatcher.TryResolve(VerifiedCatalog(), new ForwardScanMapping(),
            NullLogger.Instance, out var resolved, out _);

        var sql = Sql.LookupQueryBuilder.Build(resolved, out var usesMarketplace);

        Assert.False(usesMarketplace);
        Assert.Contains("CROSS JOIN LATERAL jsonb_array_elements", sql);
        Assert.Contains("o.\"channel_label\" AS seller", sql);
        Assert.Contains("upper(o.\"tracking_norm\") = upper($1)", sql);
    }

    [Fact]
    public void TryResolve_ItemsNamedColumnWithoutJsonType_IsNotUsedAsItemsJson()
    {
        // A column merely named like items is not enough — it must be json/jsonb typed;
        // with no items table either, the match must stay unresolved rather than guess.
        var catalog = new SchemaCatalog();
        catalog.Tables.Add(Table("oms_orders",
            ("id", "bigint"),
            ("tracking_norm", "character varying"),
            ("items_blob", "text")));   // contains "item" but NOT json-typed

        var ok = AutoMatcher.TryResolve(catalog, new ForwardScanMapping(),
            NullLogger.Instance, out var resolved, out var detail);

        Assert.False(ok);
        Assert.Equal(new ForwardScanMapping(), resolved);
        Assert.Contains("FS_ORDER_ITEMS_SKU_COL", detail);
    }

    [Fact]
    public void TryResolve_EnvValuesAlwaysWinOverAutoMatch()
    {
        var configured = new ForwardScanMapping
        {
            ColTracking = "tracking_raw",       // prefer the raw AWB column here
            ColSeller = "channel_label",
        };

        var ok = AutoMatcher.TryResolve(VerifiedCatalog(), configured,
            NullLogger.Instance, out var resolved, out _);

        Assert.True(ok);
        Assert.Equal("tracking_raw", resolved.ColTracking);
        Assert.Equal("items_json", resolved.ItemsJsonColumn);      // auto-matched around env values
    }
}
