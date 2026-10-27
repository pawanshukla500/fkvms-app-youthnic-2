using Xunit;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop.Tests;

public class VmsAutomationAndScriptTests
{
    [Fact]
    public void VmsScripts_FillByName_GeneratesHumanTypingEvents()
    {
        var userJs = VmsScripts.FillByName("Username", "ethnicfeed8");
        var passJs = VmsScripts.FillByName("Password", "Ethnic@123");

        Assert.Contains("ethnicfeed8", userJs);
        Assert.Contains("__yFindInputByName", userJs);
        Assert.Contains("__yTypeHuman", userJs);
        Assert.Contains("keydown", userJs);
        Assert.Contains("keypress", userJs);
        Assert.Contains("keyup", userJs);
        Assert.Contains("input", userJs);
        Assert.Contains("change", userJs);

        Assert.Contains("Ethnic@123", passJs);
        Assert.Contains("__yFindInputByName", passJs);
        Assert.Contains("__yTypeHuman", passJs);
    }

    [Fact]
    public void VmsScripts_FillByPlaceholder_GeneratesHumanTypingEventsForFeedId()
    {
        var feedJs = VmsScripts.FillByPlaceholder("Feed", "9e5195bd");

        Assert.Contains("9e5195bd", feedJs);
        Assert.Contains("__yFindInputByPlaceholder", feedJs);
        Assert.Contains("__yTypeHuman", feedJs);
        Assert.Contains("keydown", feedJs);
        Assert.Contains("keypress", feedJs);
        Assert.Contains("keyup", feedJs);
    }

    [Fact]
    public void VmsScripts_ClickByName_GeneratesRealMouseSequence()
    {
        var loginClickJs = VmsScripts.ClickByName("Login");
        var connectClickJs = VmsScripts.ClickByName("Connect");

        Assert.Contains("Login", loginClickJs);
        Assert.Contains("__yFindButton", loginClickJs);
        Assert.Contains("mousedown", loginClickJs);
        Assert.Contains("mouseup", loginClickJs);
        Assert.Contains("click", loginClickJs);

        Assert.Contains("Connect", connectClickJs);
        Assert.Contains("__yFindButton", connectClickJs);
        Assert.Contains("mousedown", connectClickJs);
        Assert.Contains("mouseup", connectClickJs);
        Assert.Contains("click", connectClickJs);
    }

    [Fact]
    public void VmsProbe_DetectsLoginAndOperatorVmsStates()
    {
        var loginProbe = new VmsProbe(
            Url: "https://fkvms.synlabs.io/#/login",
            Username: true,
            Password: true,
            Feed: false,
            Awb: false,
            FeedFilled: false,
            LoginVisible: true,
            ConnectVisible: false,
            RecordVisible: false);

        Assert.True(loginProbe.OnLogin);
        Assert.False(loginProbe.OnOperatorVms);

        var feedProbe = new VmsProbe(
            Url: "https://fkvms.synlabs.io/#/operator/vms",
            Username: false,
            Password: false,
            Feed: true,
            Awb: false,
            FeedFilled: false,
            LoginVisible: false,
            ConnectVisible: true,
            RecordVisible: false);

        Assert.False(feedProbe.OnLogin);
        Assert.True(feedProbe.OnOperatorVms);

        var packingProbe = new VmsProbe(
            Url: "https://fkvms.synlabs.io/#/operator/vms",
            Username: false,
            Password: false,
            Feed: false,
            Awb: true,
            FeedFilled: true,
            LoginVisible: false,
            ConnectVisible: false,
            RecordVisible: true);

        Assert.False(packingProbe.OnLogin);
        Assert.True(packingProbe.OnOperatorVms);
    }

    [Fact]
    public void VmsScripts_FocusAwbInput_GeneratesFocusAndSelectScript()
    {
        var js = VmsScripts.FocusAwbInput();
        Assert.NotNull(js);
        Assert.Contains("__yFindAwbInput", js);
        Assert.Contains("el.focus()", js);
        Assert.Contains("el.select()", js);
    }

    [Fact]
    public void VmsAutomationService_UpdateConfig_AcceptsNewConfig()
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        using var service = new VmsAutomationService(dispatcher);
        var initial = new Youthnic.Desktop.Models.MarketplaceConfig("https://example.com", "user1", "pass1", "feed1");
        var updated = new Youthnic.Desktop.Models.MarketplaceConfig("https://example.com", "user2", "pass2", "feed2");

        service.UpdateConfig(updated);
        // Method should execute cleanly and update internal config without throwing
        Assert.Equal(AutomationState.Starting, service.State);
    }

    [Fact]
    public void VmsProbe_DetectsHasTokenState()
    {
        var probeWithTokenOnRoot = new VmsProbe(
            Url: "https://fkvms.synlabs.io/",
            Username: false,
            Password: false,
            Feed: false,
            Awb: false,
            FeedFilled: false,
            LoginVisible: false,
            ConnectVisible: false,
            RecordVisible: false,
            HasToken: true);

        Assert.True(probeWithTokenOnRoot.HasToken);
        Assert.False(probeWithTokenOnRoot.OnLogin);
        Assert.False(probeWithTokenOnRoot.OnOperatorVms);

        var probeExpiredToLogin = new VmsProbe(
            Url: "https://fkvms.synlabs.io/#/login",
            Username: true,
            Password: true,
            Feed: false,
            Awb: false,
            FeedFilled: false,
            LoginVisible: true,
            ConnectVisible: false,
            RecordVisible: false,
            HasToken: false);

        Assert.False(probeExpiredToLogin.HasToken);
        Assert.True(probeExpiredToLogin.OnLogin);
    }

    [Fact]
    public void ProductCardModel_VerificationProperties_FormatProperly()
    {
        var itemSingle = new Youthnic.Desktop.Models.LookupItem(
            Sku: "SKD178-Shukriya-Wine_XL",
            SkuName: "Ethnic Kurta Set",
            Quantity: 1,
            Seller: "ETHNIC JUNCTION - Flipkart",
            ImageUrls: new[] { "https://example.com/img.jpg" });

        var singleModel = Youthnic.Desktop.Models.ProductCardModel.FromLookupItem(itemSingle, 0);
        Assert.False(singleModel.IsMultiQuantity);
        Assert.Equal("x1 UNIT", singleModel.QuantityBadgeText);
        Assert.Equal("XL", singleModel.FormattedSize);
        Assert.Equal("ETHNIC JUNCTION - Flipkart", singleModel.CleanSeller);

        var itemMulti = new Youthnic.Desktop.Models.LookupItem(
            Sku: "KURTI-BLUE-42",
            SkuName: "Blue Kurti",
            Quantity: 3,
            Seller: null,
            ImageUrls: Array.Empty<string>());

        var multiModel = Youthnic.Desktop.Models.ProductCardModel.FromLookupItem(itemMulti, 1);
        Assert.True(multiModel.IsMultiQuantity);
        Assert.Equal("x3 UNITS", multiModel.QuantityBadgeText);
        Assert.Equal("42", multiModel.FormattedSize);
        Assert.Equal("Youthnic", multiModel.CleanSeller);
    }
}
