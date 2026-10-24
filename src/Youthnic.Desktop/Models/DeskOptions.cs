namespace Youthnic.Desktop.Models;

public enum Marketplace
{
    Flipkart,
    Myntra
}

/// <summary>Per-marketplace VMS connection details. All secrets live only inside the DPAPI-encrypted store.</summary>
/// <param name="SellerAccount">
/// Optional exact Forward Scan seller-account name (e.g. "ETHNIC JUNCTION - Flipkart") used
/// to disambiguate duplicate tracking IDs across seller accounts. Empty = no filter.
/// </param>
public sealed record MarketplaceConfig(
    string VmsUrl,
    string Username,
    string Password,
    string FeedId,
    string SellerAccount = "")
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(VmsUrl) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(FeedId);
}

/// <param name="LookupApiKey">
/// Optional shared key sent as the X-Api-Key header on every lookup-API request.
/// Required when the API is hosted with LOOKUP_API_KEY configured (e.g. the public
/// Render deployment); the desk-side loopback server stays open without it. This is
/// a desk-to-API shared secret, not a marketplace credential.
/// </param>
public sealed record DeskOptions(
    string DeviceId,
    string DeskName,
    string LookupApiUrl,
    MarketplaceConfig Flipkart,
    MarketplaceConfig Myntra,
    bool SetupCompleted,
    string LookupApiKey = "")
{
    public const string DefaultLookupApiUrl = "https://flipkart-vms-server2-0.onrender.com";
    public const string DefaultLookupApiKey = "237c32350310eb6d69fed654499b55216f7be9f6ab54f537f503f799bb1c0cc1";
    public const string DefaultFlipkartVmsUrl = "https://fkvms.synlabs.io/";
    public const string DefaultMyntraVmsUrl = "https://fkvms.synlabs.io/";
    public const string DefaultUsername = "ethnicfeed8";
    public const string DefaultPassword = "Ethnic@123";
    public const string DefaultFeedId = "9e5195bd";
    public const string DefaultFlipkartSeller = "ETHNIC JUNCTION - Flipkart";
    public const string DefaultMyntraSeller = "ETHNIC JUNCTION - Myntra Youthnic";

    public static DeskOptions Empty { get; } = new(
        DeviceId: string.Empty,
        DeskName: "Packing Desk",
        LookupApiUrl: DefaultLookupApiUrl,
        Flipkart: new MarketplaceConfig(DefaultFlipkartVmsUrl, DefaultUsername, DefaultPassword, DefaultFeedId, DefaultFlipkartSeller),
        Myntra: new MarketplaceConfig(DefaultMyntraVmsUrl, DefaultUsername, DefaultPassword, DefaultFeedId, DefaultMyntraSeller),
        SetupCompleted: true,
        LookupApiKey: DefaultLookupApiKey);

    public MarketplaceConfig For(Marketplace marketplace) =>
        marketplace == Marketplace.Myntra ? Myntra : Flipkart;

    public DeskOptions WithMarketplace(Marketplace marketplace, MarketplaceConfig config) =>
        marketplace == Marketplace.Myntra
            ? this with { Myntra = config }
            : this with { Flipkart = config };
}
