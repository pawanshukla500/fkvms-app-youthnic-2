using Xunit;
using Youthnic.LookupApi.Options;
using Youthnic.LookupApi.Sql;
using Youthnic.LookupApi.Services;

namespace Youthnic.LookupApi.Tests;

/// <summary>Items-JSON mode: line items live in a JSONB array column on the orders row
/// (verified Forward Scan schema: oms_orders.items_json), not in a separate items table.</summary>
public class LookupQueryBuilderItemsJsonTests
{
    private static ForwardScanMapping JsonMapping() => new()
    {
        SchemaOrders = "public",
        OrdersTable = "oms_orders",
        ColTracking = "tracking_norm",
        ColSeller = "channel_label",
        ItemsJsonColumn = "items_json",
        SkuMapTable = "sku_photos",
        ColSku = "sku",
        ColSkuName = "title",
        ImageTable = "sku_photos",
        ImageSkuJoinColumn = "sku",
        ColImageUrl = "image_url",
    };

    [Fact]
    public void Build_ItemsJsonMode_ProducesReadOnlyLateralQuery()
    {
        var sql = LookupQueryBuilder.Build(JsonMapping(), out var usesMarketplace);

        Assert.False(usesMarketplace);                       // no marketplace column on oms_orders
        Assert.StartsWith("BEGIN READ ONLY;", sql);
        Assert.EndsWith("COMMIT;", sql);
        Assert.Contains("LIMIT 50", sql);
        Assert.Contains("CROSS JOIN LATERAL jsonb_array_elements", sql);
        Assert.Contains("o.\"items_json\"::jsonb", sql);
        Assert.Contains("AS it(item)", sql);
        Assert.Contains("(it.item ->> 'sku') AS sku", sql);
        Assert.Contains("upper(o.\"tracking_norm\") = upper($1)", sql);
        Assert.Contains("o.\"channel_label\" AS seller", sql);
    }

    [Fact]
    public void Build_ItemsJsonMode_QtyParseIsDefensive()
    {
        var sql = LookupQueryBuilder.Build(JsonMapping(), out _);

        // Bad JSON data must fall back to 1 instead of erroring the lookup.
        Assert.Contains("CASE WHEN (it.item ->> 'qty') ~ '^[0-9]+$'", sql);
        Assert.Contains("THEN (it.item ->> 'qty')::int ELSE 1 END, 1) AS qty", sql);
        Assert.DoesNotContain("GREATEST((it.item ->> 'qty')::int", sql);
    }

    [Fact]
    public void Build_ItemsJsonMode_InlineTitleAndImageUrlWithTableFallback()
    {
        var sql = LookupQueryBuilder.Build(JsonMapping(), out _);

        Assert.Contains("COALESCE(NULLIF(it.item ->> 'title', ''), m.\"title\") AS sku_name", sql);
        Assert.Contains("COALESCE(NULLIF(it.item ->> 'image_url', ''), im.\"image_url\") AS image_url", sql);
        Assert.Contains("LEFT JOIN \"public\".\"sku_photos\" m ON m.\"sku\" = (it.item ->> 'sku')", sql);
        Assert.Contains("LEFT JOIN \"public\".\"sku_photos\" im ON im.\"sku\" = (it.item ->> 'sku')", sql);
    }

    [Fact]
    public void Build_ItemsJsonMode_WithoutOptionalTables_UsesInlineExpressionsOnly()
    {
        var minimal = JsonMapping() with
        {
            SkuMapTable = "", ColSku = "", ColSkuName = "",
            ImageTable = "", ImageSkuJoinColumn = "", ColImageUrl = "",
            ColSeller = "",
        };
        var sql = LookupQueryBuilder.Build(minimal, out _);

        Assert.Contains("NULLIF(it.item ->> 'title', '') AS sku_name", sql);
        Assert.Contains("NULLIF(it.item ->> 'image_url', '') AS image_url", sql);
        Assert.Contains("NULL AS seller", sql);
        Assert.DoesNotContain("LEFT JOIN", sql);
    }

    [Fact]
    public void Build_ItemsJsonMode_OmitsMarketplacePredicateWhenNoColumn()
    {
        var sql = LookupQueryBuilder.Build(JsonMapping(), out var usesMarketplace);

        Assert.False(usesMarketplace);
        Assert.Contains("$1", sql);
        Assert.DoesNotContain("lower($2)", sql);
        Assert.DoesNotContain("marketplace", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_ItemsJsonMode_WithMarketplaceColumn_AddsSecondPredicate()
    {
        var sql = LookupQueryBuilder.Build(JsonMapping() with { ColMarketplace = "marketplace" }, out var usesMarketplace);

        Assert.True(usesMarketplace);
        Assert.Contains("lower(o.\"marketplace\") = lower($2)", sql);
    }

    [Fact]
    public void Build_ItemsJsonMode_SellerFilterIsParameterizedAndNumberedAfterMarketplace()
    {
        // No marketplace column → seller is $2.
        var sqlNoMp = LookupQueryBuilder.Build(JsonMapping(), includeSellerFilter: true, out _, out var usesSeller);
        Assert.True(usesSeller);
        Assert.Contains("upper(o.\"channel_label\") = upper($2)", sqlNoMp);
        Assert.DoesNotContain("$3", sqlNoMp);
        Assert.DoesNotContain("ETHNIC", sqlNoMp);           // values never leak into SQL text

        // With marketplace column → seller becomes $3.
        var sqlMp = LookupQueryBuilder.Build(JsonMapping() with { ColMarketplace = "marketplace" },
            includeSellerFilter: true, out _, out _);
        Assert.Contains("lower($2)", sqlMp);
        Assert.Contains("upper($3)", sqlMp);
    }

    [Fact]
    public void Build_ItemsJsonMode_PrefersJsonOverItemsTableWhenBothConfigured()
    {
        var both = JsonMapping() with
        {
            OrdersItemsTable = "order_items",
            OrderItemsOrderJoinColumn = "order_id",
            OrderItemsSkuColumn = "sku",
        };
        var sql = LookupQueryBuilder.Build(both, out _);

        Assert.Contains("CROSS JOIN LATERAL jsonb_array_elements", sql);
        Assert.Contains("o.\"items_json\"::jsonb", sql);
        Assert.DoesNotContain("JOIN \"order_items\" oi", sql);
    }

    [Fact]
    public void Build_ItemsJsonMode_RejectsInjectionInAnyIdentifier()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LookupQueryBuilder.Build(JsonMapping() with { ItemsJsonColumn = "items_json; DROP TABLE oms_orders" }, out _));
        Assert.Throws<InvalidOperationException>(() =>
            LookupQueryBuilder.Build(JsonMapping() with { ColTracking = "tracking_norm)) OR 1=1--" }, out _));
        Assert.Throws<InvalidOperationException>(() =>
            LookupQueryBuilder.Build(JsonMapping() with { OrdersTable = "oms_orders); DELETE FROM oms_orders;--" }, out _));
    }

    [Fact]
    public void Build_ItemsJsonMode_UnresolvedMappingThrows()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LookupQueryBuilder.Build(new ForwardScanMapping(), out _));
        // JSON column alone is not enough — tracking column is still required.
        Assert.Throws<InvalidOperationException>(() =>
            LookupQueryBuilder.Build(new ForwardScanMapping { OrdersTable = "oms_orders", ItemsJsonColumn = "items_json" }, out _));
    }
}

public class MappingValidatorItemsJsonTests
{
    private static ForwardScanMapping ItemsJsonMapping() => new()
    {
        OrdersTable = "oms_orders",
        ColTracking = "tracking_norm",
        ItemsJsonColumn = "items_json",
    };

    [Fact]
    public void ValidateMapping_PassesForItemsJsonModeWithoutItemsTable()
        => MappingValidator.ValidateMapping(ItemsJsonMapping());

    [Fact]
    public void ValidateMapping_ItemsJsonModeStillRequiresOrdersAndTracking()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MappingValidator.ValidateMapping(ItemsJsonMapping() with { ColTracking = "" }));
        Assert.Contains("ColTracking", ex.Message);
        Assert.DoesNotContain("OrdersItemsTable", ex.Message);   // relaxed in JSON mode
    }

    [Fact]
    public void ValidateMapping_ItemsTableModeStillRequiresItemsFields()
    {
        // No ItemsJsonColumn and no items table → the items-table fields are reported missing.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MappingValidator.ValidateMapping(ItemsJsonMapping() with { ItemsJsonColumn = "" }));
        Assert.Contains("OrdersItemsTable", ex.Message);
        Assert.Contains("OrderItemsOrderJoinColumn", ex.Message);
        Assert.Contains("OrderItemsSkuColumn", ex.Message);
    }

    [Fact]
    public void ValidateMapping_RejectsBadItemsJsonIdentifier()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MappingValidator.ValidateMapping(ItemsJsonMapping() with { ItemsJsonColumn = "items json; DROP" }));
        Assert.Contains("ItemsJsonColumn", ex.Message);
    }

    [Fact]
    public void FindMissingRequired_ItemsJsonModeSkipsItemsTableFields()
    {
        var missing = MappingValidator.FindMissingRequired(ItemsJsonMapping());
        Assert.Empty(missing);
    }

    [Fact]
    public void FromEnvironment_ReadsItemsJsonColumn()
    {
        var m = ForwardScanMapping.FromEnvironment(name => name switch
        {
            "FS_ORDERS_TABLE" => "oms_orders",
            "FS_COL_TRACKING" => "tracking_norm",
            "FS_ITEMS_JSON_COLUMN" => "items_json",
            _ => null,
        });
        Assert.Equal("items_json", m.ItemsJsonColumn);
        MappingValidator.ValidateMapping(m);
    }
}
