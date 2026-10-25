using Xunit;
using Youthnic.Desktop.Models;

namespace Youthnic.Desktop.Tests;

public class ProductCardModelTests
{
    [Theory]
    [InlineData("BT218-Cat-Light Pink_S", "S")]
    [InlineData("T86-Coffee-Red_S", "S")]
    [InlineData("T100-Army-Pink_M", "M")]
    [InlineData("B161-Kishori-Bottle Green_XL", "XL")]
    [InlineData("EJ-Dress-Teal_2XL", "2XL")]
    [InlineData("EJ-Dress-Teal_XXL", "XXL")]
    [InlineData("KURTI-RED-FREE", "FREE")]
    [InlineData("KURTI-RED-FS", "FS")]
    [InlineData("PLAIN-SKU-NO-SIZE", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractSize_ParsesKnownSizeSuffixes(string? sku, string? expectedSize)
    {
        var size = ProductCardModel.ExtractSize(sku!);
        Assert.Equal(expectedSize, size);
    }

    [Fact]
    public void FromLookupItem_DecodesHtmlAndSetsPropertiesCorrectly()
    {
        var item = new LookupItem(
            Sku: "T86-Coffee-Red_S",
            SkuName: "Women&#39;s Cotton T-Shirt &amp; Top",
            Quantity: 2,
            Seller: "ETHNIC JUNCTION &amp; CO",
            ImageUrls: new[] { "https://rukminim2.flixcart.com/image/test.jpg" }
        );

        var model = ProductCardModel.FromLookupItem(item, 0);

        Assert.Equal(0, model.Index);
        Assert.Equal("T86-Coffee-Red_S", model.Sku);
        Assert.Equal("Women's Cotton T-Shirt & Top", model.SkuName);
        Assert.Equal(2, model.Quantity);
        Assert.Equal("S", model.Size);
        Assert.Equal("ETHNIC JUNCTION & CO", model.Seller);
        Assert.Equal("https://rukminim2.flixcart.com/image/test.jpg", model.ImageUrl);
        Assert.False(model.HasImage);
        Assert.Null(model.Image);
    }

    [Fact]
    public void FromLookupItem_EmptyTitle_FallsBackToSku()
    {
        var item = new LookupItem(
            Sku: "BT218-Cat-Light Pink_S",
            SkuName: "",
            Quantity: 1,
            Seller: null,
            ImageUrls: Array.Empty<string>()
        );

        var model = ProductCardModel.FromLookupItem(item, 1);

        Assert.Equal(1, model.Index);
        Assert.Equal("BT218-Cat-Light Pink_S", model.Sku);
        Assert.Equal("BT218-Cat-Light Pink_S", model.SkuName);
        Assert.Equal(1, model.Quantity);
        Assert.Equal("S", model.Size);
    }

    [Fact]
    public void PropertyChanged_FiresOnImageChange()
    {
        var model = new ProductCardModel
        {
            Sku = "TEST_SKU_M",
            SkuName = "Test Product",
            Quantity = 1,
            Size = "M"
        };

        var changedProps = new List<string>();
        model.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != null) changedProps.Add(args.PropertyName);
        };

        Assert.False(model.HasImage);
        Assert.True(model.IsLoadingImage);

        // Setting image null when already null does not fire
        model.Image = null;
        Assert.Empty(changedProps);

        // Setting actual image fires notifications for Image, HasImage, and IsLoadingImage
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        model.Image = bmp;

        Assert.True(model.HasImage);
        Assert.False(model.IsLoadingImage);
        Assert.Contains(nameof(model.Image), changedProps);
        Assert.Contains(nameof(model.HasImage), changedProps);
        Assert.Contains(nameof(model.IsLoadingImage), changedProps);
    }
}
