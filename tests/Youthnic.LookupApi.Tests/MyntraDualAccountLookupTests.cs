using Xunit;
using Youthnic.LookupApi.Services;

namespace Youthnic.LookupApi.Tests;

public class MyntraDualAccountLookupTests
{
    [Fact]
    public void Myntra_WhenSellerIsNull_IncludesBothYouthnicAndPpmpAccounts()
    {
        var sellers = LookupService.ResolveSellerFilters("myntra", null);

        Assert.NotNull(sellers);
        Assert.Equal(2, sellers.Length);
        Assert.Contains("ETHNIC JUNCTION - MYNTRA YOUTHNIC", sellers);
        Assert.Contains("VB EXPORT - MYNTRA PPMP", sellers);
    }

    [Theory]
    [InlineData("ETHNIC JUNCTION - Myntra Youthnic")]
    [InlineData("ethnic junction - myntra youthnic")]
    [InlineData("VB EXPORT - Myntra PPMP")]
    [InlineData("vb export - myntra ppmp")]
    [InlineData("ETHNIC JUNCTION - Myntra Youthnic, VB EXPORT - Myntra PPMP")]
    [InlineData("ETHNIC JUNCTION - Myntra Youthnic; VB EXPORT - Myntra PPMP")]
    [InlineData("ETHNIC JUNCTION - Myntra Youthnic+VB EXPORT - Myntra PPMP")]
    public void Myntra_WhenSingleAccountPassed_AutoIncludesBothAccounts(string inputSeller)
    {
        var sellers = LookupService.ResolveSellerFilters("myntra", inputSeller);

        Assert.NotNull(sellers);
        Assert.Equal(2, sellers.Length);
        Assert.Contains("ETHNIC JUNCTION - MYNTRA YOUTHNIC", sellers);
        Assert.Contains("VB EXPORT - MYNTRA PPMP", sellers);
    }

    [Fact]
    public void Myntra_WhenCustomAccountPassed_DoesNotIncludeDefaultAccounts()
    {
        var sellers = LookupService.ResolveSellerFilters("myntra", "CUSTOM_SELLER_CORP");

        Assert.NotNull(sellers);
        Assert.Single(sellers);
        Assert.Equal("CUSTOM_SELLER_CORP", sellers[0]);
    }

    [Fact]
    public void Flipkart_WhenSellerIsNull_DefaultsToEthnicJunctionFlipkart()
    {
        var sellers = LookupService.ResolveSellerFilters("flipkart", null);

        Assert.NotNull(sellers);
        Assert.Single(sellers);
        Assert.Equal("ETHNIC JUNCTION - FLIPKART", sellers[0]);
    }

    [Fact]
    public void Flipkart_WhenCustomSellerPassed_UsesSpecifiedSeller()
    {
        var sellers = LookupService.ResolveSellerFilters("flipkart", "CUSTOM - SELLER");

        Assert.NotNull(sellers);
        Assert.Single(sellers);
        Assert.Equal("CUSTOM - SELLER", sellers[0]);
    }

    [Theory]
    [InlineData("B24-White Lotus-Teal_XL", "B24-White Lotus-Teal", "XL", "White Lotus Teal (Size XL) · B24")]
    [InlineData("BT231-Kapoor-Teal_L", "BT231-Kapoor-Teal", "L", "Kapoor Teal (Size L) · BT231")]
    [InlineData("SKD74-Archi-Cherry_XL", "SKD74-Archi-Cherry", "XL", "Archi Cherry (Size XL) · SKD74")]
    [InlineData("T86-Coffee-Rust_M", "T86-Coffee-Rust", "M", "Coffee Rust (Size M) · T86")]
    [InlineData("BT254-Satya-Navy_XXS", "BT254-Satya-Navy", "XXS", "Satya Navy (Size XXS) · BT254")]
    public void MyntraPpmp_SkuPatterns_ExtractBaseSizeAndCleanTitleCorrectly(
        string sku, string expectedBase, string expectedSize, string expectedTitle)
    {
        var baseSku = LookupService.ExtractBaseSku(sku);
        var size = LookupService.ExtractSize(sku);
        var title = LookupService.FormatCleanTitle(sku);

        Assert.Equal(expectedBase, baseSku);
        Assert.Equal(expectedSize, size);
        Assert.Equal(expectedTitle, title);
    }

    [Theory]
    [InlineData("B24-White Lotus-Teal_XL", "B24WHITELOTUSTEALXL")]
    [InlineData("BT231-Kapoor-Teal_L", "BT231KAPOORTEALL")]
    [InlineData("B31-Bharat- Rama_2XL", "B31BHARATRAMA2XL")]
    public void NormalizeSku_RemovesPunctuationAndWhitespace(string input, string expected)
    {
        var normalized = LookupService.NormalizeSku(input);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("B31-Bharat- Rama_2XL", "B31-Bharat-Rama_2XL")]
    [InlineData("BT267 - Raman - Red _ XL", "BT267-Raman-Red_XL")]
    public void NormalizeSpacing_RemovesExcessiveWhitespaceAroundSeparators(string input, string expected)
    {
        var normalized = LookupService.NormalizeSpacing(input);
        Assert.Equal(expected, normalized);
    }
}
