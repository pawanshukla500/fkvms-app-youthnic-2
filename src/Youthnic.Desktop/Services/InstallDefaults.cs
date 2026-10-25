using System.IO;
using System.Text.Json;

namespace Youthnic.Desktop.Services;

/// <summary>
/// First-run defaults written by the installer or administrator to:
/// 1) {app}\Desktop\desk-config.json
/// 2) {app}\desk-config.json
/// 3) %ProgramData%\Youthnic\PackingDesk\install-defaults.json
/// 4) %LocalAppData%\Youthnic\PackingDesk\desk-config.json
/// Pre-seeds per-desk identity (DeskName), camera feed IDs, Flipkart/Myntra credentials,
/// and lookup API connection so that each packing station boots ready to pack.
/// </summary>
public sealed record InstallDefaults(
    string? LookupApiUrl,
    string? FlipkartSeller,
    string? MyntraSeller,
    string? LookupApiKey,
    string? FlipkartUsername = null,
    string? FlipkartPassword = null,
    string? FlipkartFeedId = null,
    string? FlipkartVmsUrl = null,
    string? MyntraUsername = null,
    string? MyntraPassword = null,
    string? MyntraFeedId = null,
    string? MyntraVmsUrl = null,
    string? DeskName = null)
{
    public static InstallDefaults Empty { get; } = new(null, null, null, null);

    public bool IsEmpty() =>
        string.IsNullOrWhiteSpace(LookupApiUrl) &&
        string.IsNullOrWhiteSpace(FlipkartUsername) &&
        string.IsNullOrWhiteSpace(FlipkartPassword) &&
        string.IsNullOrWhiteSpace(FlipkartFeedId) &&
        string.IsNullOrWhiteSpace(MyntraUsername) &&
        string.IsNullOrWhiteSpace(MyntraPassword) &&
        string.IsNullOrWhiteSpace(MyntraFeedId) &&
        string.IsNullOrWhiteSpace(DeskName);

    public static InstallDefaults Load()
    {
        var candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "desk-config.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "desk-config.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Youthnic", "PackingDesk", "install-defaults.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Youthnic", "PackingDesk", "desk-config.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Youthnic", "PackingDesk", "desk-config.json")
        };

        foreach (var path in candidates)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var parsed = Parse(json);
                    if (!parsed.IsEmpty())
                    {
                        AppLog.Info($"InstallDefaults: loaded desk defaults from {path} (Desk: {parsed.DeskName ?? "unspecified"})");
                        return parsed;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Debug($"InstallDefaults: could not read {path}: {ex.Message}");
            }
        }

        return Empty;
    }

    public static InstallDefaults Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? GetProp(JsonElement elem, params string[] names)
            {
                foreach (var name in names)
                {
                    if (elem.TryGetProperty(name, out var val) && val.ValueKind == JsonValueKind.String)
                    {
                        var s = val.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                }
                return null;
            }

            var deskName = GetProp(root, "deskName", "DeskName", "stationName", "StationName");
            var lookupApiUrl = GetProp(root, "lookupApiUrl", "LookupApiUrl");
            var lookupApiKey = GetProp(root, "lookupApiKey", "LookupApiKey");

            var fkSeller = GetProp(root, "flipkartSeller", "FlipkartSeller");
            var fkUser = GetProp(root, "flipkartUsername", "FlipkartUsername");
            var fkPass = GetProp(root, "flipkartPassword", "FlipkartPassword");
            var fkFeed = GetProp(root, "flipkartFeedId", "FlipkartFeedId");
            var fkVms = GetProp(root, "flipkartVmsUrl", "FlipkartVmsUrl");

            if (root.TryGetProperty("flipkart", out var fkElem) || root.TryGetProperty("Flipkart", out fkElem))
            {
                fkUser ??= GetProp(fkElem, "username", "Username");
                fkPass ??= GetProp(fkElem, "password", "Password");
                fkFeed ??= GetProp(fkElem, "feedId", "FeedId");
                fkSeller ??= GetProp(fkElem, "sellerAccount", "SellerAccount", "seller", "Seller");
                fkVms ??= GetProp(fkElem, "vmsUrl", "VmsUrl");
            }

            var mySeller = GetProp(root, "myntraSeller", "MyntraSeller");
            var myUser = GetProp(root, "myntraUsername", "MyntraUsername");
            var myPass = GetProp(root, "myntraPassword", "MyntraPassword");
            var myFeed = GetProp(root, "myntraFeedId", "MyntraFeedId");
            var myVms = GetProp(root, "myntraVmsUrl", "MyntraVmsUrl");

            if (root.TryGetProperty("myntra", out var myElem) || root.TryGetProperty("Myntra", out myElem))
            {
                myUser ??= GetProp(myElem, "username", "Username");
                myPass ??= GetProp(myElem, "password", "Password");
                myFeed ??= GetProp(myElem, "feedId", "FeedId");
                mySeller ??= GetProp(myElem, "sellerAccount", "SellerAccount", "seller", "Seller");
                myVms ??= GetProp(myElem, "vmsUrl", "VmsUrl");
            }

            return new InstallDefaults(
                LookupApiUrl: lookupApiUrl,
                FlipkartSeller: fkSeller,
                MyntraSeller: mySeller,
                LookupApiKey: lookupApiKey,
                FlipkartUsername: fkUser,
                FlipkartPassword: fkPass,
                FlipkartFeedId: fkFeed,
                FlipkartVmsUrl: fkVms,
                MyntraUsername: myUser,
                MyntraPassword: myPass,
                MyntraFeedId: myFeed,
                MyntraVmsUrl: myVms,
                DeskName: deskName);
        }
        catch (Exception ex)
        {
            AppLog.Debug($"InstallDefaults.Parse error: {ex.Message}");
            return Empty;
        }
    }
}

