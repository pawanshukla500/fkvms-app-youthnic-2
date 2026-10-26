using Xunit;
using Youthnic.Desktop.Models;
using Youthnic.Desktop.Views;

namespace Youthnic.Desktop.Tests;

public class MyntraDesktopSellerTests
{
    [Fact]
    public void MyntraSellerConstants_DefaultAndSecondary_MatchRequiredNames()
    {
        Assert.Equal("ETHNIC JUNCTION - Myntra Youthnic", DeskOptions.DefaultMyntraSeller);
        Assert.Equal("VB EXPORT - Myntra PPMP", DeskOptions.SecondaryMyntraSeller);
    }

    [Theory]
    [InlineData("ETHNIC JUNCTION - Myntra Youthnic")]
    [InlineData("ethnic junction - myntra youthnic")]
    [InlineData("VB EXPORT - Myntra PPMP")]
    [InlineData("vb export - myntra ppmp")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveDesktopSellerParameter_MyntraDefaultOrSecondarySeller_ReturnsNullToQueryBothAccounts(string? seller)
    {
        var resolved = PackingWindow.ResolveDesktopSellerParameter(Marketplace.Myntra, seller);
        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveDesktopSellerParameter_MyntraCustomSeller_PreservesCustomSeller()
    {
        var resolved = PackingWindow.ResolveDesktopSellerParameter(Marketplace.Myntra, "THIRD_PARTY_SELLER");
        Assert.Equal("THIRD_PARTY_SELLER", resolved);
    }

    [Fact]
    public void ResolveDesktopSellerParameter_FlipkartDefaultSeller_PreservesFlipkartSeller()
    {
        var resolved = PackingWindow.ResolveDesktopSellerParameter(Marketplace.Flipkart, DeskOptions.DefaultFlipkartSeller);
        Assert.Equal("ETHNIC JUNCTION - Flipkart", resolved);
    }
}
