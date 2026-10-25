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
}
