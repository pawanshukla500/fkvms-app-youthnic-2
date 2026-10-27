using Xunit;
using Youthnic.LookupApi.Options;
using Youthnic.LookupApi.Sql;
using Youthnic.LookupApi.Services;

namespace Youthnic.LookupApi.Tests;

public class LookupQueryBuilderTests
{
    private static ForwardScanMapping FullMapping() => new()
    {
        SchemaOrders = "public",
        OrdersTable = "orders",
        ColTracking = "tracking_id",
        ColMarketplace = "marketplace",
        ColSeller = "seller",
        OrdersItemsTable = "order_items",
        OrderItemsOrderJoinColumn = "order_id",
        OrderItemsSkuColumn = "sku",
        SkuMapTable = "sku_map",
        ColSku = "sku",
        ColSkuName = "sku_name",
        ImageTable = "product_images",
        ImageSkuJoinColumn = "sku",
        ColImageUrl = "image_url",
    };

    [Fact]
    public void Build_ProducesReadOnlyParameterizedJoin()
    {
        var sql = LookupQueryBuilder.Build(FullMapping(), out var usesMarketplace);

        Assert.True(usesMarketplace);
        Assert.StartsWith("BEGIN READ ONLY;", sql);
        Assert.EndsWith("COMMIT;", sql);
        Assert.Contains("LIMIT 50", sql);
        Assert.Contains("upper($1)", sql);      // tracking via parameter
        Assert.Contains("lower($2)", sql);      // marketplace via parameter
        Assert.Contains("\"public\".\"orders\" o", sql);
        Assert.Contains("\"order_items\" oi", sql);
        Assert.Contains("LEFT JOIN LATERAL (SELECT sm.\"sku_name\" FROM \"public\".\"sku_map\" sm", sql);
        Assert.Contains("LIMIT 1) m ON true", sql);
        Assert.Contains("LEFT JOIN LATERAL (SELECT sim.\"image_url\" FROM \"public\".\"product_images\" sim", sql);
        Assert.Contains("LIMIT 1) im ON true", sql);
    }

    [Fact]
    public void Build_NeverEmbedsValues_OnlyParameters()
    {
        var sql = LookupQueryBuilder.Build(FullMapping(), out _);
        Assert.DoesNotContain("AWB123", sql);
        Assert.DoesNotContain("flipkart", sql);
        // All predicates must reference positional parameters, not literals.
        Assert.Contains("$1", sql);
        Assert.Contains("$2", sql);
    }

    [Fact]
    public void Build_WithoutMarketplaceColumn_SingleParameter()
    {
        var sql = LookupQueryBuilder.Build(FullMapping() with { ColMarketplace = "" }, out var usesMarketplace);
        Assert.False(usesMarketplace);
        Assert.Contains("$1", sql);
        Assert.DoesNotContain("$2", sql);
        Assert.DoesNotContain("marketplace", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithoutOptionalTables_EmitsNullColumns()
    {
        var minimal = FullMapping() with
        {
            SkuMapTable = "", ColSku = "", ColSkuName = "",
            ImageTable = "", ImageSkuJoinColumn = "", ColImageUrl = "",
            ColSeller = "",
        };
        var sql = LookupQueryBuilder.Build(minimal, out _);
        Assert.Contains("NULL AS sku_name", sql);
        Assert.Contains("NULL AS image_url", sql);
        Assert.Contains("NULL AS seller", sql);
        Assert.DoesNotContain("LEFT JOIN", sql);
    }

    [Fact]
    public void Build_RejectsInjectionInIdentifier()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LookupQueryBuilder.Build(FullMapping() with { ColTracking = "tracking_id; DROP TABLE orders" }, out _));
        Assert.Throws<InvalidOperationException>(() =>
            LookupQueryBuilder.Build(FullMapping() with { OrdersTable = "orders); DELETE FROM orders;--" }, out _));
        Assert.Throws<InvalidOperationException>(() =>
            LookupQueryBuilder.Build(FullMapping() with { SchemaOrders = "pg_catalog;DROP" }, out _));
    }

    [Fact]
    public void Build_RejectsUnresolvedMapping()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LookupQueryBuilder.Build(new ForwardScanMapping(), out _));
    }

    [Fact]
    public void Build_QuotesIdentifiersDefensively()
    {
        // A keyword-like but allowlist-legal identifier must still be quoted.
        var sql = LookupQueryBuilder.Build(
            FullMapping() with { OrdersItemsTable = "order_items", ColSeller = "order" }, out _);
        Assert.Contains("\"order_items\"", sql);
        Assert.Contains("o.\"order\" AS seller", sql);
    }

    [Fact]
    public void Build_ItemsTableMode_LateralJoinsEnforceLimit1ToPreventCartesianMultiplication()
    {
        var sql = LookupQueryBuilder.Build(FullMapping(), out _);

        Assert.Matches(@"LEFT JOIN LATERAL \(SELECT sm\.""sku_name"" FROM ""public""\.""sku_map"" sm WHERE .+ LIMIT 1\) m ON true", sql);
        Assert.Matches(@"LEFT JOIN LATERAL \(SELECT sim\.""image_url"" FROM ""public""\.""product_images"" sim WHERE .+ LIMIT 1\) im ON true", sql);
    }
}

public class LookupQueryBuilderSellerFilterTests
{
    private static ForwardScanMapping FullMapping() => new()
    {
        SchemaOrders = "public",
        OrdersTable = "orders",
        ColTracking = "tracking_id",
        ColMarketplace = "marketplace",
        ColSeller = "seller",
        OrdersItemsTable = "order_items",
        OrderItemsOrderJoinColumn = "order_id",
        OrderItemsSkuColumn = "sku",
    };

    [Fact]
    public void Build_WithSellerFilter_AddsParameterizedThirdPredicate()
    {
        var sql = LookupQueryBuilder.Build(FullMapping(), includeSellerFilter: true, out var usesMarketplace, out var usesSeller);

        Assert.True(usesMarketplace);
        Assert.True(usesSeller);
        Assert.Contains("ANY($3)", sql);          // seller via parameter, never concatenated
        Assert.Contains("\"seller\"", sql);
        Assert.DoesNotContain("ETHNIC", sql);       // values never leak into SQL text
    }

    [Fact]
    public void Build_WithoutSellerFilter_KeepsTwoParameters()
    {
        var sql = LookupQueryBuilder.Build(FullMapping(), includeSellerFilter: false, out _, out var usesSeller);

        Assert.False(usesSeller);
        Assert.DoesNotContain("$3", sql);
    }

    [Fact]
    public void Build_SellerFilterWithoutSellerColumn_Throws()
    {
        var mapping = FullMapping() with { ColSeller = "" };

        Assert.Throws<InvalidOperationException>(() =>
            LookupQueryBuilder.Build(mapping, includeSellerFilter: true, out _, out _));
    }

    [Fact]
    public void SellerScopedCacheKey_DiffersFromUnscoped()
    {
        Assert.NotEqual(
            LookupCache.KeyFor("flipkart", "AWB1", "ETHNIC JUNCTION - Flipkart"),
            LookupCache.KeyFor("flipkart", "AWB1"));
    }
}
