using Xunit;
using Youthnic.LookupApi.Options;
using Youthnic.LookupApi.Services;

namespace Youthnic.LookupApi.Tests;

public class MappingValidatorTests
{
    [Theory]
    [InlineData("orders")]
    [InlineData("order_items")]
    [InlineData("public.orders")]
    [InlineData("_audit_log")]
    [InlineData("a1")]
    [InlineData("schema_2.t")]
    public void ValidIdentifiers_Accepted(string identifier)
        => Assert.True(MappingValidator.IsValidIdentifier(identifier));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1abc")]
    [InlineData("HasUpper")]
    [InlineData("has space")]
    [InlineData("has-dash")]
    [InlineData("a;b")]
    [InlineData("x'; DROP TABLE orders;--")]
    [InlineData("public..orders")]
    [InlineData(".orders")]
    [InlineData("orders.")]
    [InlineData("a.b.c")]
    [InlineData("\"quoted\"")]
    public void InvalidIdentifiers_Rejected(string? identifier)
        => Assert.False(MappingValidator.IsValidIdentifier(identifier));

    private static ForwardScanMapping FullyResolvedMapping() => new()
    {
        OrdersTable = "orders",
        ColTracking = "tracking_id",
        OrdersItemsTable = "order_items",
        OrderItemsOrderJoinColumn = "order_id",
        OrderItemsSkuColumn = "sku",
    };

    [Fact]
    public void ValidateMapping_PassesForMinimalRequiredMapping()
        => MappingValidator.ValidateMapping(FullyResolvedMapping());

    [Fact]
    public void ValidateMapping_ThrowsListingMissingRequiredFields()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => MappingValidator.ValidateMapping(new ForwardScanMapping()));
        Assert.Contains("OrdersTable", ex.Message);
        Assert.Contains("ColTracking", ex.Message);
        Assert.Contains("OrdersItemsTable", ex.Message);
        Assert.Contains("OrderItemsOrderJoinColumn", ex.Message);
        Assert.Contains("OrderItemsSkuColumn", ex.Message);
    }

    [Fact]
    public void ValidateMapping_RejectsInjectionInOptionalField()
    {
        var mapping = FullyResolvedMapping() with { ColSeller = "seller; DROP TABLE orders" };
        var ex = Assert.Throws<InvalidOperationException>(() => MappingValidator.ValidateMapping(mapping));
        Assert.Contains("ColSeller", ex.Message);
    }

    [Fact]
    public void FindMissingRequired_OnlyReportsEmptyRequiredFields()
    {
        var missing = MappingValidator.FindMissingRequired(FullyResolvedMapping());
        Assert.Empty(missing);
    }
}
