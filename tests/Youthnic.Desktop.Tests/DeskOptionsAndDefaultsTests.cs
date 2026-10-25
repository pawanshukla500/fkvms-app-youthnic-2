using System.Text.Json;
using Xunit;
using Youthnic.Desktop.Models;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop.Tests;

public class DeskOptionsAndDefaultsTests
{
    [Fact]
    public void FlipkartDefaults_AreCorrectlyDefined()
    {
        Assert.Equal("ethnicfeed8", DeskOptions.DefaultFlipkartUsername);
        Assert.Equal("Ethnic@123", DeskOptions.DefaultFlipkartPassword);
        Assert.Equal("9e5195bd", DeskOptions.DefaultFlipkartFeedId);
        Assert.Equal("https://fkvms.synlabs.io/", DeskOptions.DefaultFlipkartVmsUrl);
        Assert.Equal("ETHNIC JUNCTION - Flipkart", DeskOptions.DefaultFlipkartSeller);
    }

    [Fact]
    public void MyntraDefaults_AreCorrectlyDefined()
    {
        Assert.Equal("ethnicfeed8", DeskOptions.DefaultMyntraUsername);
        Assert.Equal("Ethnic@123", DeskOptions.DefaultMyntraPassword);
        Assert.Equal("9e5195bd", DeskOptions.DefaultMyntraFeedId);
        Assert.Equal("https://fkvms.synlabs.io/", DeskOptions.DefaultMyntraVmsUrl);
        Assert.Equal("ETHNIC JUNCTION - Myntra Youthnic", DeskOptions.DefaultMyntraSeller);
    }

    [Fact]
    public void HostedLookupApiDefaults_AreCorrectlyDefined()
    {
        Assert.Equal("https://flipkart-vms-server2-0.onrender.com", DeskOptions.DefaultLookupApiUrl);
        Assert.Equal("237c32350310eb6d69fed654499b55216f7be9f6ab54f537f503f799bb1c0cc1", DeskOptions.DefaultLookupApiKey);
    }

    [Fact]
    public void DeskOptionsEmpty_IsImmediatelyConfiguredForBothMarketplaces()
    {
        var empty = DeskOptions.Empty;
        Assert.True(empty.SetupCompleted);
        Assert.Equal("Packing Desk", empty.DeskName);
        Assert.Equal(DeskOptions.DefaultLookupApiUrl, empty.LookupApiUrl);
        Assert.Equal(DeskOptions.DefaultLookupApiKey, empty.LookupApiKey);

        var fk = empty.For(Marketplace.Flipkart);
        Assert.True(fk.IsConfigured);
        Assert.Equal(DeskOptions.DefaultFlipkartVmsUrl, fk.VmsUrl);
        Assert.Equal("ethnicfeed8", fk.Username);
        Assert.Equal("Ethnic@123", fk.Password);
        Assert.Equal("9e5195bd", fk.FeedId);
        Assert.Equal("ETHNIC JUNCTION - Flipkart", fk.SellerAccount);

        var my = empty.For(Marketplace.Myntra);
        Assert.True(my.IsConfigured);
        Assert.Equal(DeskOptions.DefaultMyntraVmsUrl, my.VmsUrl);
        Assert.Equal("ethnicfeed8", my.Username);
        Assert.Equal("Ethnic@123", my.Password);
        Assert.Equal("9e5195bd", my.FeedId);
        Assert.Equal("ETHNIC JUNCTION - Myntra Youthnic", my.SellerAccount);
    }

    [Fact]
    public void MarketplaceConfig_IsConfigured_ChecksVmsUrlAndUsername()
    {
        var valid = new MarketplaceConfig("https://example.com", "user", "pass", "feed", "seller");
        Assert.True(valid.IsConfigured);

        var missingUrl = new MarketplaceConfig("", "user", "pass", "feed", "seller");
        Assert.False(missingUrl.IsConfigured);

        var missingUser = new MarketplaceConfig("https://example.com", "", "pass", "feed", "seller");
        Assert.False(missingUser.IsConfigured);
    }

    [Fact]
    public void WithMarketplace_UpdatesCorrectMarketplaceOnly()
    {
        var initial = DeskOptions.Empty;
        var customFk = new MarketplaceConfig("https://custom.fk.com", "customuser", "custompass", "feed1", "seller1");
        var updated = initial.WithMarketplace(Marketplace.Flipkart, customFk);

        Assert.Equal("https://custom.fk.com", updated.Flipkart.VmsUrl);
        Assert.Equal("customuser", updated.Flipkart.Username);
        Assert.Equal(DeskOptions.DefaultMyntraVmsUrl, updated.Myntra.VmsUrl);
        Assert.Equal("ethnicfeed8", updated.Myntra.Username);
    }

    [Fact]
    public void CreateDefaultOptions_SeedsAllProductionDefaults()
    {
        var options = SecureConfigStore.CreateDefaultOptions();
        Assert.False(string.IsNullOrWhiteSpace(options.DeviceId));
        Assert.True(options.SetupCompleted);
        Assert.Equal(DeskOptions.DefaultLookupApiUrl, options.LookupApiUrl);
        Assert.Equal(DeskOptions.DefaultLookupApiKey, options.LookupApiKey);

        Assert.Equal(DeskOptions.DefaultFlipkartVmsUrl, options.Flipkart.VmsUrl);
        Assert.Equal("ethnicfeed8", options.Flipkart.Username);
        Assert.Equal("Ethnic@123", options.Flipkart.Password);
        Assert.Equal("9e5195bd", options.Flipkart.FeedId);
        Assert.Equal("ETHNIC JUNCTION - Flipkart", options.Flipkart.SellerAccount);

        Assert.Equal(DeskOptions.DefaultMyntraVmsUrl, options.Myntra.VmsUrl);
        Assert.Equal("ethnicfeed8", options.Myntra.Username);
        Assert.Equal("Ethnic@123", options.Myntra.Password);
        Assert.Equal("9e5195bd", options.Myntra.FeedId);
        Assert.Equal("ETHNIC JUNCTION - Myntra Youthnic", options.Myntra.SellerAccount);
    }

    [Fact]
    public void CreateDefaultOptions_UsesInstallDefaultsWhenProvided()
    {
        var customDefaults = new InstallDefaults(
            LookupApiUrl: "http://192.168.1.100:5080",
            FlipkartSeller: "Custom Flipkart Seller",
            MyntraSeller: "Custom Myntra Seller",
            LookupApiKey: "custom-api-key-123",
            FlipkartUsername: "fkuser-test",
            FlipkartPassword: "fkpass-test",
            FlipkartFeedId: "fkfeed-test",
            FlipkartVmsUrl: "https://fk.test.vms.io",
            MyntraUsername: "myuser-test",
            MyntraPassword: "mypass-test",
            MyntraFeedId: "myfeed-test",
            MyntraVmsUrl: "https://my.test.vms.io");

        var options = SecureConfigStore.CreateDefaultOptions(customDefaults);
        Assert.Equal("http://192.168.1.100:5080", options.LookupApiUrl);
        Assert.Equal("custom-api-key-123", options.LookupApiKey);

        Assert.Equal("https://fk.test.vms.io", options.Flipkart.VmsUrl);
        Assert.Equal("fkuser-test", options.Flipkart.Username);
        Assert.Equal("fkpass-test", options.Flipkart.Password);
        Assert.Equal("fkfeed-test", options.Flipkart.FeedId);
        Assert.Equal("Custom Flipkart Seller", options.Flipkart.SellerAccount);

        Assert.Equal("https://my.test.vms.io", options.Myntra.VmsUrl);
        Assert.Equal("myuser-test", options.Myntra.Username);
        Assert.Equal("mypass-test", options.Myntra.Password);
        Assert.Equal("myfeed-test", options.Myntra.FeedId);
        Assert.Equal("Custom Myntra Seller", options.Myntra.SellerAccount);
    }

    [Fact]
    public void EnsureDefaults_PopulatesMissingBlankFieldsAndSetsSetupCompleted()
    {
        var incomplete = new DeskOptions(
            DeviceId: "",
            DeskName: "",
            LookupApiUrl: "",
            LookupApiKey: "",
            Flipkart: new MarketplaceConfig("", "", "", ""),
            Myntra: new MarketplaceConfig("", "", "", ""),
            SetupCompleted: false);

        var reconciled = SecureConfigStore.EnsureDefaults(incomplete);
        Assert.True(reconciled.SetupCompleted);
        Assert.False(string.IsNullOrWhiteSpace(reconciled.DeviceId));
        Assert.Equal("Packing Desk", reconciled.DeskName);
        Assert.Equal(DeskOptions.DefaultLookupApiUrl, reconciled.LookupApiUrl);
        Assert.Equal(DeskOptions.DefaultLookupApiKey, reconciled.LookupApiKey);

        Assert.Equal(DeskOptions.DefaultFlipkartVmsUrl, reconciled.Flipkart.VmsUrl);
        Assert.Equal("ethnicfeed8", reconciled.Flipkart.Username);
        Assert.Equal("Ethnic@123", reconciled.Flipkart.Password);
        Assert.Equal("9e5195bd", reconciled.Flipkart.FeedId);
        Assert.Equal("ETHNIC JUNCTION - Flipkart", reconciled.Flipkart.SellerAccount);

        Assert.Equal(DeskOptions.DefaultMyntraVmsUrl, reconciled.Myntra.VmsUrl);
        Assert.Equal("ethnicfeed8", reconciled.Myntra.Username);
        Assert.Equal("Ethnic@123", reconciled.Myntra.Password);
        Assert.Equal("9e5195bd", reconciled.Myntra.FeedId);
        Assert.Equal("ETHNIC JUNCTION - Myntra Youthnic", reconciled.Myntra.SellerAccount);
    }

    [Fact]
    public void EnsureDefaults_PreservesExistingCustomValues()
    {
        var existing = new DeskOptions(
            DeviceId: "dev-custom-99",
            DeskName: "Station 3",
            LookupApiUrl: "https://custom.api.org",
            LookupApiKey: "custom-secret",
            Flipkart: new MarketplaceConfig("https://custom.fk.org", "customfkuser", "customfkpass", "feed-custom-1", "Seller FK Custom"),
            Myntra: new MarketplaceConfig("https://custom.my.org", "custommyuser", "custommypass", "feed-custom-2", "Seller MY Custom"),
            SetupCompleted: true);

        var reconciled = SecureConfigStore.EnsureDefaults(existing);
        Assert.Equal("dev-custom-99", reconciled.DeviceId);
        Assert.Equal("Station 3", reconciled.DeskName);
        Assert.Equal("https://custom.api.org", reconciled.LookupApiUrl);
        Assert.Equal("custom-secret", reconciled.LookupApiKey);

        Assert.Equal("https://custom.fk.org", reconciled.Flipkart.VmsUrl);
        Assert.Equal("customfkuser", reconciled.Flipkart.Username);
        Assert.Equal("customfkpass", reconciled.Flipkart.Password);
        Assert.Equal("feed-custom-1", reconciled.Flipkart.FeedId);
        Assert.Equal("Seller FK Custom", reconciled.Flipkart.SellerAccount);

        Assert.Equal("https://custom.my.org", reconciled.Myntra.VmsUrl);
        Assert.Equal("custommyuser", reconciled.Myntra.Username);
        Assert.Equal("custommypass", reconciled.Myntra.Password);
        Assert.Equal("feed-custom-2", reconciled.Myntra.FeedId);
        Assert.Equal("Seller MY Custom", reconciled.Myntra.SellerAccount);
    }

    [Fact]
    public void InstallDefaults_DeserializesAllFieldsFromJson()
    {
        const string json = """
        {
            "lookupApiUrl": "https://api.hosted.com",
            "lookupApiKey": "sec-key-1",
            "flipkartSeller": "Seller FK",
            "myntraSeller": "Seller MY",
            "flipkartUsername": "fku",
            "flipkartPassword": "fkp",
            "flipkartFeedId": "fkfeed",
            "flipkartVmsUrl": "https://fkvms.com",
            "myntraUsername": "myu",
            "myntraPassword": "myp",
            "myntraFeedId": "myfeed",
            "myntraVmsUrl": "https://myvms.com"
        }
        """;

        var defaults = JsonSerializer.Deserialize<InstallDefaults>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(defaults);
        Assert.Equal("https://api.hosted.com", defaults.LookupApiUrl);
        Assert.Equal("sec-key-1", defaults.LookupApiKey);
        Assert.Equal("Seller FK", defaults.FlipkartSeller);
        Assert.Equal("Seller MY", defaults.MyntraSeller);
        Assert.Equal("fku", defaults.FlipkartUsername);
        Assert.Equal("fkp", defaults.FlipkartPassword);
        Assert.Equal("fkfeed", defaults.FlipkartFeedId);
        Assert.Equal("https://fkvms.com", defaults.FlipkartVmsUrl);
        Assert.Equal("myu", defaults.MyntraUsername);
        Assert.Equal("myp", defaults.MyntraPassword);
        Assert.Equal("myfeed", defaults.MyntraFeedId);
        Assert.Equal("https://myvms.com", defaults.MyntraVmsUrl);
    }

    [Fact]
    public void InstallDefaults_ParsesDeskNameAndAllFieldsFromFlatJson()
    {
        const string json = """
        {
            "deskName": "Desk-07",
            "lookupApiUrl": "https://lookup.youthnic.com",
            "lookupApiKey": "api-key-desk-07",
            "flipkartSeller": "ETHNIC JUNCTION - Flipkart",
            "myntraSeller": "ETHNIC JUNCTION - Myntra Youthnic",
            "flipkartUsername": "desk7fk",
            "flipkartPassword": "PasswordFk7",
            "flipkartFeedId": "feed-fk-007",
            "flipkartVmsUrl": "https://fkvms.synlabs.io/",
            "myntraUsername": "desk7my",
            "myntraPassword": "PasswordMy7",
            "myntraFeedId": "feed-my-007",
            "myntraVmsUrl": "https://fkvms.synlabs.io/"
        }
        """;

        var defaults = InstallDefaults.Parse(json);
        Assert.False(defaults.IsEmpty());
        Assert.Equal("Desk-07", defaults.DeskName);
        Assert.Equal("https://lookup.youthnic.com", defaults.LookupApiUrl);
        Assert.Equal("api-key-desk-07", defaults.LookupApiKey);
        Assert.Equal("desk7fk", defaults.FlipkartUsername);
        Assert.Equal("PasswordFk7", defaults.FlipkartPassword);
        Assert.Equal("feed-fk-007", defaults.FlipkartFeedId);
        Assert.Equal("desk7my", defaults.MyntraUsername);
        Assert.Equal("PasswordMy7", defaults.MyntraPassword);
        Assert.Equal("feed-my-007", defaults.MyntraFeedId);
    }

    [Fact]
    public void InstallDefaults_ParsesNestedJsonStructure()
    {
        const string json = """
        {
            "deskName": "Desk-09",
            "lookupApiUrl": "https://lookup.synlabs.io",
            "lookupApiKey": "secret-999",
            "flipkart": {
                "vmsUrl": "https://fkvms.synlabs.io/",
                "username": "operator_fk_09",
                "password": "Pass@Desk09_FK",
                "feedId": "feed_fk_99",
                "sellerAccount": "ETHNIC JUNCTION - Flipkart"
            },
            "myntra": {
                "vmsUrl": "https://fkvms.synlabs.io/",
                "username": "operator_my_09",
                "password": "Pass@Desk09_MY",
                "feedId": "feed_my_99",
                "sellerAccount": "ETHNIC JUNCTION - Myntra Youthnic"
            }
        }
        """;

        var defaults = InstallDefaults.Parse(json);
        Assert.False(defaults.IsEmpty());
        Assert.Equal("Desk-09", defaults.DeskName);
        Assert.Equal("https://lookup.synlabs.io", defaults.LookupApiUrl);
        Assert.Equal("secret-999", defaults.LookupApiKey);
        Assert.Equal("operator_fk_09", defaults.FlipkartUsername);
        Assert.Equal("Pass@Desk09_FK", defaults.FlipkartPassword);
        Assert.Equal("feed_fk_99", defaults.FlipkartFeedId);
        Assert.Equal("ETHNIC JUNCTION - Flipkart", defaults.FlipkartSeller);
        Assert.Equal("operator_my_09", defaults.MyntraUsername);
        Assert.Equal("Pass@Desk09_MY", defaults.MyntraPassword);
        Assert.Equal("feed_my_99", defaults.MyntraFeedId);
        Assert.Equal("ETHNIC JUNCTION - Myntra Youthnic", defaults.MyntraSeller);
    }

    [Fact]
    public void CreateDefaultOptions_UsesDeskNameFromInstallDefaults()
    {
        var defaults = new InstallDefaults(
            LookupApiUrl: "https://api.test",
            FlipkartSeller: "FK Seller",
            MyntraSeller: "MY Seller",
            LookupApiKey: "key-1",
            FlipkartUsername: "user-1",
            FlipkartPassword: "pass-1",
            FlipkartFeedId: "feed-1",
            DeskName: "Desk-12");

        var options = SecureConfigStore.CreateDefaultOptions(defaults);
        Assert.Equal("Desk-12", options.DeskName);
        Assert.Equal("user-1", options.Flipkart.Username);
        Assert.Equal("feed-1", options.Flipkart.FeedId);
    }
}
