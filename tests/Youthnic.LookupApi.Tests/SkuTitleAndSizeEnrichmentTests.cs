using Xunit;
using Youthnic.LookupApi.Services;

namespace Youthnic.LookupApi.Tests;

public class SkuTitleAndSizeEnrichmentTests
{
    [Theory]
    [InlineData("SKD237-Juhi-Mehendi_M", "SKD237-Juhi-Mehendi")]
    [InlineData("SKD237-Juhi-Mehendi_2XL", "SKD237-Juhi-Mehendi")]
    [InlineData("SKD237-Juhi-Red-XL", "SKD237-Juhi-Red")]
    [InlineData("SKD237-Juhi-Red-FREE", "SKD237-Juhi-Red")]
    [InlineData("PLAINSKU", "PLAINSKU")]
    [InlineData("T95_Box_Shirt_flower_rama", "T95_Box_Shirt_flower_rama")]
    [InlineData("T95_Box_Shirt_flower_rama_S", "T95_Box_Shirt_flower_rama")]
    [InlineData("", "")]
    public void ExtractBaseSku_ExtractsBaseDesignCorrectly(string sku, string expected)
    {
        var actual = LookupService.ExtractBaseSku(sku);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("SKD237-Juhi-Mehendi_M", "M")]
    [InlineData("SKD237-Juhi-Mehendi_2XL", "2XL")]
    [InlineData("SKD237-Juhi-Red-XL", "XL")]
    [InlineData("SKD237-Juhi-Red-FREE", "FREE")]
    [InlineData("SKD237-Juhi-Red-FS", "FS")]
    [InlineData("PLAINSKU", null)]
    public void ExtractSize_DetectsKnownSizes(string sku, string? expected)
    {
        var actual = LookupService.ExtractSize(sku);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("SKD237-Juhi-Mehendi_M", "Juhi Mehendi (Size M) · SKD237")]
    [InlineData("SKD237-Juhi-Red_2XL", "Juhi Red (Size 2XL) · SKD237")]
    [InlineData("SKD237-Juhi-Mehendi", "Juhi Mehendi · SKD237")]
    [InlineData("YTH-Kurta-Blue_L", "Kurta Blue (Size L) · YTH")]
    [InlineData("SINGLEWORD", "SINGLEWORD")]
    public void FormatCleanTitle_ProducesReadablePackingTitle(string sku, string expected)
    {
        var actual = LookupService.FormatCleanTitle(sku);
        Assert.Equal(expected, actual);
    }
}
