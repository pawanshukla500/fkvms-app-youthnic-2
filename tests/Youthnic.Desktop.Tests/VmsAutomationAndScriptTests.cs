using Xunit;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop.Tests;

public class VmsAutomationAndScriptTests
{
    [Fact]
    public void VmsScripts_FillByName_GeneratesExecutableJsForUsernameAndPassword()
    {
        var userJs = VmsScripts.FillByName("Username", "ethnicfeed8");
        var passJs = VmsScripts.FillByName("Password", "Ethnic@123");

        Assert.Contains("ethnicfeed8", userJs);
        Assert.Contains("__yFindInputByName", userJs);
        Assert.Contains("input", userJs);
        Assert.Contains("change", userJs);

        Assert.Contains("Ethnic@123", passJs);
        Assert.Contains("__yFindInputByName", passJs);
    }

    [Fact]
    public void VmsScripts_FillByPlaceholder_GeneratesExecutableJsForFeedId()
    {
        var feedJs = VmsScripts.FillByPlaceholder("Feed", "9e5195bd");

        Assert.Contains("9e5195bd", feedJs);
        Assert.Contains("__yFindInputByPlaceholder", feedJs);
        Assert.Contains("placeholder", feedJs);
    }

    [Fact]
    public void VmsScripts_ClickByName_GeneratesExecutableJsForLoginAndConnect()
    {
        var loginClickJs = VmsScripts.ClickByName("Login");
        var connectClickJs = VmsScripts.ClickByName("Connect");

        Assert.Contains("Login", loginClickJs);
        Assert.Contains("__yFindButton", loginClickJs);

        Assert.Contains("Connect", connectClickJs);
        Assert.Contains("__yFindButton", connectClickJs);
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
}
